<?php
/**
 * Plugin Name: Яндекс доставка — диагностика и восстановление заказов
 * Description: Дополнение к плагину «Яндекс доставка для Woocommerce» (Woodev). Показывает реальную причину, по которой заказ не экспортируется, восстанавливает служебные данные (статус и ПВЗ) у заказов, которые их не получили при оформлении, и страхует оформление заказа, чтобы такие «потерянные» заказы больше не появлялись.
 * Version: 1.1.0
 * Requires PHP: 7.4
 * Requires Plugins: woocommerce
 */

defined( 'ABSPATH' ) || exit;

/**
 * Почему это нужно.
 *
 * Плагин Woodev считает заказ «заказом Яндекс доставки» только по мета-полю
 * _yandex_delivery_state_status. Это поле выставляется в единственном месте —
 * WC_Yandex_Delivery_Checkout::checkout_update_order() на хуке
 * woocommerce_checkout_update_order_meta. Если заказ создан любым другим путём
 * (блочный чекаут / Store API, быстрый заказ через admin-ajax.php, ручное
 * создание, оформление, при котором класс чекаута плагина не был загружен),
 * то мета не пишется, и заказ:
 *
 *  - не появляется в разделе «Заказы Я.Доставки» (список фильтруется по этой мете);
 *  - не экспортируется автоматически (WC_Yandex_Delivery::export_order() требует статус NEW);
 *  - не показывает кнопку «Экспортировать» (get_allowed_actions() требует NEW или EXPORT_INVALID);
 *  - при нажатии «Выбрать и экспортировать» отдаёт пустое окно без вариантов доставки,
 *    потому что вместе со статусом не сохранился и выбранный покупателем ПВЗ
 *    (_yandex_delivery_destination_station_id): Яндекс отвечает на такую заявку
 *    ошибкой «Not found station by station_id», а assets/js/admin/admin-order.js
 *    разбирает ответ как `if ( response.success ) { return response.data }`
 *    и молча её проглатывает.
 */
class WCYD_Order_Repair {

	const VERSION = '1.1.0';

	const META_STATUS  = '_yandex_delivery_state_status';
	const META_STATION = '_yandex_delivery_destination_station_id';
	const META_ADDRESS = '_yandex_delivery_destination_station_address';

	/** Срок жизни кеша списка ПВЗ города. */
	const POINTS_CACHE_TTL = 6 * HOUR_IN_SECONDS;

	/** @var WCYD_Order_Repair|null */
	private static $instance = null;

	/** @var array<int, Abstract_WC_Yandex_Delivery_Shipping_Method|false> Кеш экземпляров метода доставки по ID заказа. */
	private array $method_instances = [];

	public static function instance(): self {

		if ( is_null( self::$instance ) ) {
			self::$instance = new self();
		}

		return self::$instance;
	}

	private function __construct() {
		add_action( 'plugins_loaded', [ $this, 'init' ], 20 );
	}

	public function init() {

		// Страховка: дописываем служебные данные сразу после создания заказа,
		// каким бы способом он ни был оформлен.
		add_action( 'woocommerce_checkout_order_processed', [ $this, 'backfill_by_order_id' ], 99 );
		add_action( 'woocommerce_store_api_checkout_order_processed', [ $this, 'backfill_order' ], 99 );

		if ( is_admin() ) {

			add_action( 'add_meta_boxes', [ $this, 'add_meta_box' ], 20, 2 );
			add_action( 'admin_enqueue_scripts', [ $this, 'enqueue_assets' ] );
			add_action( 'admin_print_footer_scripts', [ $this, 'print_offers_error_script' ], 99 );

			add_action( 'wp_ajax_wcyd_load_points', [ $this, 'ajax_load_points' ] );
			add_action( 'wp_ajax_wcyd_save_repair', [ $this, 'ajax_save_repair' ] );
			add_action( 'wp_ajax_wcyd_diagnose_order', [ $this, 'ajax_diagnose' ] );
		}
	}

	/**
	 * Плагин Woodev загружен и его классы доступны.
	 */
	private function is_ready(): bool {
		return function_exists( 'wc_yandex_shipping' ) && class_exists( 'WC_Yandex_Delivery_Order' );
	}

	/* ---------------------------------------------------------------------
	 * Общие помощники
	 * ------------------------------------------------------------------ */

	/**
	 * @param int|WC_Order|null $order
	 *
	 * @return WC_Yandex_Delivery_Order|null
	 */
	private function get_yandex_order( $order ): ?WC_Yandex_Delivery_Order {

		if ( ! $this->is_ready() ) {
			return null;
		}

		$order_id = $order instanceof WC_Order ? $order->get_id() : absint( $order );

		if ( ! $order_id ) {
			return null;
		}

		$yandex_order = new WC_Yandex_Delivery_Order( $order_id );

		return $yandex_order->get_id() ? $yandex_order : null;
	}

	/**
	 * Строка доставки заказа, относящаяся к Яндекс доставке.
	 */
	private function get_shipping_item( WC_Order $order ): ?WC_Order_Item_Shipping {

		foreach ( $order->get_shipping_methods() as $item ) {
			if ( 0 === strpos( (string) $item->get_method_id(), 'yandex_delivery' ) ) {
				return $item;
			}
		}

		return null;
	}

	/**
	 * Экземпляр метода доставки. Возвращает false, если зона/метод удалены из настроек.
	 *
	 * @return Abstract_WC_Yandex_Delivery_Shipping_Method|false
	 */
	private function get_method_instance( WC_Order $order ) {

		$order_id = $order->get_id();

		if ( array_key_exists( $order_id, $this->method_instances ) ) {
			return $this->method_instances[ $order_id ];
		}

		$instance = false;
		$item     = $this->get_shipping_item( $order );

		if ( $item ) {

			$found = WC_Shipping_Zones::get_shipping_method( $item->get_instance_id() );

			if ( $found instanceof Abstract_WC_Yandex_Delivery_Shipping_Method ) {
				$instance = $found;
			}
		}

		return $this->method_instances[ $order_id ] = $instance;
	}

	private function is_pickup_order( WC_Order $order ): bool {

		$instance = $this->get_method_instance( $order );

		return $instance && $instance->is_pickup();
	}

	/**
	 * GEO ID населённого пункта получателя.
	 *
	 * Сначала берём из меты строки доставки (её кладёт сам метод доставки в add_rate()),
	 * при отсутствии — определяем по городу заказа через API Яндекса.
	 */
	private function get_geo_id( WC_Order $order, bool $allow_api_request = true ): int {

		$item = $this->get_shipping_item( $order );

		if ( $item ) {

			$meta = $item->get_meta( 'yandex_delivery' );

			if ( is_array( $meta ) && ! empty( $meta['geo_id'] ) ) {
				return absint( $meta['geo_id'] );
			}
		}

		// Определение города через API стоит одного запроса к Яндексу, поэтому
		// на оформлении заказа (когда важна скорость) его не делаем.
		if ( ! $allow_api_request || ! function_exists( 'wc_yandex_delivery_get_location' ) ) {
			return 0;
		}

		$city = $order->get_shipping_city() ?: $order->get_billing_city();

		if ( ! $city ) {
			return 0;
		}

		$variants = wc_yandex_delivery_get_location( [
			'country' => $order->get_shipping_country() ?: $order->get_billing_country(),
			'state'   => $order->get_shipping_state() ?: $order->get_billing_state(),
			'city'    => $city,
		] );

		foreach ( (array) $variants as $variant ) {
			if ( ! empty( $variant->geo_id ) ) {
				return absint( $variant->geo_id );
			}
		}

		return 0;
	}

	/**
	 * Чего не хватает заказу для экспорта.
	 *
	 * @return string[] Список человекочитаемых названий недостающих данных.
	 */
	private function get_missing_data( WC_Yandex_Delivery_Order $order ): array {

		$missing = [];

		if ( '' === (string) $order->get_state_status() ) {
			$missing['status'] = 'статус Яндекс доставки (заказ не виден в разделе «Заказы Я.Доставки»)';
		}

		if ( $this->is_pickup_order( $order ) && '' === (string) $order->get_destination_station_id() ) {
			$missing['station'] = 'пункт выдачи заказа (ПВЗ), выбранный покупателем';
		}

		if ( ! $this->get_method_instance( $order ) ) {
			$missing['method'] = 'метод доставки в настройках Woocommerce (зона доставки или метод удалены/изменены)';
		}

		return $missing;
	}

	/**
	 * Ищет пункт (ПВЗ или пункт отгрузки) в базе Яндекса по его идентификатору.
	 *
	 * @return object|null
	 */
	private function find_station( string $station_id ) {

		if ( '' === $station_id || ! function_exists( 'wc_yandex_delivery_get_points_list' ) ) {
			return null;
		}

		foreach ( wc_yandex_delivery_get_points_list( [ 'pickup_point_ids' => [ $station_id ] ] ) as $point ) {
			if ( ! empty( $point->id ) && (string) $point->id === $station_id ) {
				return $point;
			}
		}

		return null;
	}

	private function get_station_address( $point ): string {

		if ( ! is_object( $point ) ) {
			return '';
		}

		return (string) ( $point->address->full_address ?? $point->name ?? '' );
	}

	/**
	 * Описание пункта для отчёта диагностики.
	 */
	private function describe_station( string $station_id ): string {

		$point = $this->find_station( $station_id );

		if ( $point ) {
			return sprintf( '%s — %s (найден в Яндексе)', $station_id, $this->get_station_address( $point ) ?: 'без адреса' );
		}

		return sprintf( '%s — НЕ НАЙДЕН в базе Яндекса', $station_id );
	}

	/* ---------------------------------------------------------------------
	 * Страховка при оформлении заказа
	 * ------------------------------------------------------------------ */

	public function backfill_by_order_id( $order_id ) {
		$this->backfill_order( wc_get_order( $order_id ) );
	}

	/**
	 * Если плагин Яндекс доставки не записал свои данные — записываем их сами.
	 *
	 * @param WC_Order|mixed $order
	 */
	public function backfill_order( $order ) {

		if ( ! $order instanceof WC_Order ) {
			return;
		}

		$yandex_order = $this->get_yandex_order( $order );

		// Заказ без Яндекс доставки нас не касается.
		if ( ! $yandex_order || ! $yandex_order->get_yandex_shipping_method() ) {
			return;
		}

		// Плагин отработал штатно — вмешиваться не нужно.
		if ( '' !== (string) $yandex_order->get_state_status() ) {
			return;
		}

		$note_parts = [ 'Служебные данные Яндекс доставки не были записаны при оформлении заказа. Статус выставлен принудительно, чтобы заказ появился в разделе «Заказы Я.Доставки».' ];

		if ( $this->is_pickup_order( $yandex_order ) && '' === (string) $yandex_order->get_destination_station_id() ) {

			$point = $this->guess_pickup_point( $yandex_order );

			if ( ! empty( $point['id'] ) ) {

				$yandex_order->set_destination_station_id( $point['id'] );
				$yandex_order->set_destination_station_address( $point['address'] );

				$note_parts[] = sprintf( 'Пункт выдачи восстановлен: %s (%s).', $point['address'] ?: '—', $point['id'] );

			} else {
				$note_parts[] = 'Пункт выдачи определить не удалось — укажите его вручную в блоке «Яндекс доставка — восстановление данных» на странице заказа, иначе Яндекс не вернёт варианты доставки.';
			}
		}

		$yandex_order->set_state_status( 'NEW' );
		$yandex_order->add_order_note( implode( ' ', $note_parts ) );
		$yandex_order->save();
	}

	/**
	 * Пытается восстановить выбранный покупателем ПВЗ из данных запроса или сессии Woocommerce.
	 *
	 * @return array{id:string,address:string}
	 */
	private function guess_pickup_point( WC_Order $order ): array {

		$point = [ 'id' => '', 'address' => '' ];

		// 1. Скрытые поля формы оформления заказа.
		if ( ! empty( $_REQUEST['yandex_pickup_point'] ) ) {
			$point['id']      = sanitize_text_field( wp_unslash( $_REQUEST['yandex_pickup_point'] ) );
			$point['address'] = isset( $_REQUEST['yandex_pickup_point_address'] ) ? sanitize_text_field( wp_unslash( $_REQUEST['yandex_pickup_point_address'] ) ) : '';
		}

		// 2. Сессия покупателя: ПВЗ сохраняется туда при выборе точки на карте.
		if ( '' === $point['id'] && WC()->session ) {

			$session_key = function_exists( 'wc_yandex_delivery_get_chosen_pickup_points_session_key' )
				? wc_yandex_delivery_get_chosen_pickup_points_session_key()
				: 'chosen_yandex_pickup_point';

			$chosen = (array) WC()->session->get( $session_key, [] );
			$geo_id = $this->get_geo_id( $order, false );

			if ( $geo_id && ! empty( $chosen[ $geo_id ]['id'] ) ) {

				$point['id']      = (string) $chosen[ $geo_id ]['id'];
				$point['address'] = (string) ( $chosen[ $geo_id ]['name'] ?? '' );

			} elseif ( 1 === count( $chosen ) ) {

				// В сессии одна-единственная точка — покупатель выбирал ПВЗ только один раз.
				$single = reset( $chosen );

				if ( ! empty( $single['id'] ) ) {
					$point['id']      = (string) $single['id'];
					$point['address'] = (string) ( $single['name'] ?? '' );
				}
			}
		}

		return $point;
	}

	/* ---------------------------------------------------------------------
	 * Метабокс восстановления на странице заказа
	 * ------------------------------------------------------------------ */

	private function get_order_screen_ids(): array {

		$ids = [ 'shop_order' ];

		if ( function_exists( 'wc_get_page_screen_id' ) ) {
			$ids[] = wc_get_page_screen_id( 'shop-order' );
		}

		return array_unique( array_filter( $ids ) );
	}

	/**
	 * Селект с поиском берём из самого Woocommerce (selectWoo), ничего своего не грузим.
	 */
	public function enqueue_assets( $hook_suffix ) {

		$screen = function_exists( 'get_current_screen' ) ? get_current_screen() : null;

		if ( ! $screen || ! in_array( $screen->id, $this->get_order_screen_ids(), true ) ) {
			return;
		}

		if ( wp_script_is( 'wc-enhanced-select', 'registered' ) ) {
			wp_enqueue_script( 'wc-enhanced-select' );
		}

		if ( wp_style_is( 'woocommerce_admin_styles', 'registered' ) ) {
			wp_enqueue_style( 'woocommerce_admin_styles' );
		}
	}

	/**
	 * @param string                $post_type
	 * @param WP_Post|WC_Order|null $post
	 */
	public function add_meta_box( $post_type, $post = null ) {

		if ( ! $this->is_ready() || ! in_array( $post_type, $this->get_order_screen_ids(), true ) ) {
			return;
		}

		$order = $this->get_yandex_order( $this->resolve_order_from_screen( $post ) );

		if ( ! $order || ! $order->get_yandex_shipping_method() || $order->is_exported() ) {
			return;
		}

		if ( ! $this->get_missing_data( $order ) ) {
			return;
		}

		add_meta_box(
			'wcyd-order-repair',
			'Яндекс доставка — восстановление данных',
			[ $this, 'render_meta_box' ],
			$post_type,
			'normal',
			'high'
		);
	}

	/**
	 * @param WP_Post|WC_Order|null $post
	 *
	 * @return WC_Order|null
	 */
	private function resolve_order_from_screen( $post ): ?WC_Order {

		if ( $post instanceof WC_Order ) {
			return $post;
		}

		$order_id = 0;

		if ( $post instanceof WP_Post ) {
			$order_id = $post->ID;
		} elseif ( ! empty( $_GET['id'] ) ) {
			$order_id = absint( $_GET['id'] );
		} elseif ( ! empty( $_GET['post'] ) ) {
			$order_id = absint( $_GET['post'] );
		}

		$order = $order_id ? wc_get_order( $order_id ) : null;

		return $order instanceof WC_Order ? $order : null;
	}

	/**
	 * Важно: внутри метабокса нельзя использовать <form> — страница заказа сама
	 * является одной большой формой, а вложенные формы браузер выбрасывает,
	 * и поля уходят в форму заказа (из-за чего Woocommerce уводил на список записей).
	 * Поэтому здесь только поля и кнопки type="button", а сохранение идёт через AJAX.
	 */
	public function render_meta_box( $post ) {

		$order = $this->get_yandex_order( $this->resolve_order_from_screen( $post ) );

		if ( ! $order ) {
			return;
		}

		$missing      = $this->get_missing_data( $order );
		$is_pickup    = $this->is_pickup_order( $order );
		$station_id   = (string) $order->get_destination_station_id();
		$station_text = (string) $order->get_destination_station_address();
		?>
		<style>
			.wcyd-repair p.description { margin: 0 0 12px; }
			.wcyd-repair ul.wcyd-missing { margin: 0 0 12px 18px; list-style: disc; }
			.wcyd-repair .wcyd-field { margin-bottom: 16px; }
			.wcyd-repair .wcyd-field > label { display: block; font-weight: 600; margin-bottom: 4px; }
			.wcyd-repair select.wcyd-points, .wcyd-repair input.wcyd-manual { width: 100%; max-width: 680px; }
			.wcyd-repair .select2-container { max-width: 680px; }
			.wcyd-repair .wcyd-result { margin-top: 12px; padding: 10px 12px; border-left: 4px solid #72aee6; background: #f6f7f7; white-space: pre-wrap; word-break: break-word; display: none; }
			.wcyd-repair .wcyd-result.is-error { border-left-color: #d63638; }
			.wcyd-repair .wcyd-result.is-success { border-left-color: #00a32a; }
			.wcyd-repair .spinner.is-active { float: none; margin: 0 0 0 6px; vertical-align: middle; }
		</style>

		<div class="wcyd-repair"
			 data-order_id="<?php echo esc_attr( $order->get_id() ); ?>"
			 data-nonce="<?php echo esc_attr( wp_create_nonce( 'wcyd-repair-' . $order->get_id() ) ); ?>">

			<p class="description">
				У этого заказа выбран метод доставки «Яндекс доставка», но плагин не получил служебные данные при оформлении.
				Из-за этого заказ не попал в раздел «Заказы Я.Доставки» и не экспортируется. Не хватает:
			</p>

			<ul class="wcyd-missing">
				<?php foreach ( $missing as $text ) : ?>
					<li><?php echo esc_html( $text ); ?></li>
				<?php endforeach; ?>
			</ul>

			<?php if ( isset( $missing['method'] ) ) : ?>
				<p class="description">
					<strong>Внимание:</strong> метод доставки этого заказа не найден в настройках Woocommerce
					(WooCommerce → Настройки → Доставка). Пока метод не восстановлен, экспорт работать не будет —
					плагин не сможет определить пункт отгрузки и тариф.
				</p>
			<?php endif; ?>

			<?php if ( $is_pickup ) : ?>

				<div class="wcyd-field">
					<label for="wcyd-points-<?php echo esc_attr( $order->get_id() ); ?>">Пункт выдачи (ПВЗ), который выбрал покупатель</label>

					<select class="wcyd-points" id="wcyd-points-<?php echo esc_attr( $order->get_id() ); ?>">
						<?php if ( $station_id ) : ?>
							<option value="<?php echo esc_attr( $station_id ); ?>" selected><?php echo esc_html( $station_text ?: $station_id ); ?></option>
						<?php else : ?>
							<option value="">— список ещё не загружен —</option>
						<?php endif; ?>
					</select>

					<p class="description" style="margin-top:6px;">
						Нажмите «Загрузить список ПВЗ», чтобы получить пункты выдачи города получателя из API Яндекса.
						В открывшемся списке работает поиск — начните вводить улицу или район.
					</p>

					<p style="margin:8px 0 0;">
						<button type="button" class="button wcyd-load-points">Загрузить список ПВЗ</button>
						<span class="spinner"></span>
					</p>
				</div>

				<div class="wcyd-field">
					<label for="wcyd-manual-<?php echo esc_attr( $order->get_id() ); ?>">Либо укажите ID пункта выдачи вручную</label>
					<input type="text" class="wcyd-manual" id="wcyd-manual-<?php echo esc_attr( $order->get_id() ); ?>" placeholder="например, 278bfc4d-f56c-401a-b6b7-d6d6e784bf3e" autocomplete="off" />
					<p class="description" style="margin-top:6px;">
						ID нужен, только если список выше не загрузился. Взять его можно в личном кабинете Яндекс Доставки:
						<a href="https://delivery.yandex.ru" target="_blank" rel="noopener">delivery.yandex.ru</a> → раздел с пунктами выдачи,
						идентификатор пункта имеет вид <code>278bfc4d-f56c-401a-b6b7-d6d6e784bf3e</code>.
						Указанный ID проверяется в базе Яндекса перед сохранением.
					</p>
				</div>

			<?php endif; ?>

			<p style="margin: 0;">
				<button type="button" class="button button-primary wcyd-save">Восстановить данные заказа</button>
				<button type="button" class="button wcyd-diagnose">Проверить экспорт и показать ошибку</button>
				<span class="spinner"></span>
			</p>

			<div class="wcyd-result"></div>
		</div>

		<script>
			( function ( $ ) {

				$( function () {

					$( '.wcyd-repair' ).each( function () {

						var $box     = $( this ),
							$result  = $box.find( '.wcyd-result' ),
							$points  = $box.find( 'select.wcyd-points' ),
							$manual  = $box.find( 'input.wcyd-manual' ),
							orderId  = $box.data( 'order_id' ),
							nonce    = $box.data( 'nonce' );

						function busy( $button, state ) {
							$button.prop( 'disabled', state );
							$button.siblings( '.spinner' ).toggleClass( 'is-active', state );
						}

						function show( message, type ) {
							$result
								.removeClass( 'is-error is-success' )
								.addClass( type ? 'is-' + type : '' )
								.text( typeof message === 'string' ? message : JSON.stringify( message ) )
								.show();
						}

						function payload( response ) {
							return ( response && typeof response.data !== 'undefined' ) ? response.data : response;
						}

						function messageOf( data, fallback ) {

							if ( ! data ) {
								return fallback;
							}

							return ( typeof data === 'object' && data.message ) ? data.message : data;
						}

						function post( action, data ) {
							return $.post( ajaxurl, $.extend( {
								action: action,
								order_id: orderId,
								security: nonce
							}, data || {} ) );
						}

						function fail( xhr ) {
							show( 'Сервер вернул ошибку ' + xhr.status + '.\n\n' + String( xhr.responseText ).substring( 0, 1000 ), 'error' );
						}

						function enhance( $select ) {

							// Повторная загрузка списка: сначала снимаем прежнюю обёртку.
							if ( $select.data( 'select2' ) ) {
								$select.select2( 'destroy' );
							}

							var options = {
								placeholder: 'Начните вводить адрес пункта выдачи',
								width: '100%',
								allowClear: true
							};

							if ( $.fn.selectWoo ) {
								$select.selectWoo( options );
							} else if ( $.fn.select2 ) {
								$select.select2( options );
							}
						}

						// Загрузка списка ПВЗ города получателя.
						$box.on( 'click', '.wcyd-load-points', function () {

							var $button = $( this );

							busy( $button, true );
							show( 'Запрашиваем список пунктов выдачи у Яндекса…' );

							post( 'wcyd_load_points' ).done( function ( response ) {

								if ( ! response || ! response.success ) {
									show( messageOf( payload( response ), 'Не удалось получить список ПВЗ.' ), 'error' );
									return;
								}

								var current = $points.val();

								$points.empty().append( $( '<option/>', { value: '', text: '' } ) );

								$.each( response.data.points, function ( index, point ) {
									$points.append( $( '<option/>', { value: point.id, text: point.text } ) );
								} );

								if ( current ) {
									$points.val( current );
								}

								enhance( $points );

								show( 'Загружено пунктов выдачи: ' + response.data.points.length + '. Выберите нужный в списке и нажмите «Восстановить данные заказа».', 'success' );

							} ).fail( fail ).always( function () {
								busy( $button, false );
							} );
						} );

						// Сохранение.
						function save( $button, force ) {

							busy( $button, true );
							show( 'Сохраняем…' );

							return post( 'wcyd_save_repair', {
								pickup_point_id: $points.length ? ( $points.val() || '' ) : '',
								pickup_point_id_manual: $manual.length ? $.trim( $manual.val() ) : '',
								force: force ? 1 : 0
							} ).done( function ( response ) {

								var data = payload( response );

								if ( response && response.success ) {

									show( messageOf( data, 'Сохранено.' ) + '\n\nОбновляем страницу…', 'success' );

									window.setTimeout( function () {
										window.location.reload();
									}, 1200 );

									return;
								}

								var message = messageOf( data, 'Не удалось сохранить данные.' );

								show( message, 'error' );

								if ( ! force && data && data.can_force && window.confirm( message + '\n\nСохранить ID без проверки?' ) ) {
									save( $button, true );
								}

							} ).fail( fail ).always( function () {
								busy( $button, false );
							} );
						}

						$box.on( 'click', '.wcyd-save', function () {
							save( $( this ), false );
						} );

						// Диагностика.
						$box.on( 'click', '.wcyd-diagnose', function () {

							var $button = $( this );

							busy( $button, true );
							show( 'Проверяем заявку и запрашиваем варианты доставки у Яндекса…' );

							post( 'wcyd_diagnose_order' ).done( function ( response ) {

								show( messageOf( payload( response ), 'Пустой ответ сервера.' ), ( response && response.success ) ? 'success' : 'error' );

							} ).fail( fail ).always( function () {
								busy( $button, false );
							} );
						} );
					} );
				} );

			} )( jQuery );
		</script>
		<?php
	}

	/* ---------------------------------------------------------------------
	 * AJAX
	 * ------------------------------------------------------------------ */

	/**
	 * Проверка прав и nonce, общая для всех запросов метабокса.
	 *
	 * @return WC_Yandex_Delivery_Order
	 */
	private function authorize_request(): WC_Yandex_Delivery_Order {

		$order_id = isset( $_POST['order_id'] ) ? absint( $_POST['order_id'] ) : 0;

		if ( ! current_user_can( 'edit_shop_orders' ) ) {
			wp_send_json_error( 'У вас недостаточно прав на выполнение этой операции.' );
		}

		check_ajax_referer( 'wcyd-repair-' . $order_id, 'security' );

		$order = $this->get_yandex_order( $order_id );

		if ( ! $order ) {
			wp_send_json_error( 'Заказ не найден.' );
		}

		return $order;
	}

	/**
	 * Список ПВЗ города получателя для выпадающего списка с поиском.
	 */
	public function ajax_load_points() {

		$order = $this->authorize_request();

		$geo_id = $this->get_geo_id( $order );

		if ( ! $geo_id ) {
			wp_send_json_error( sprintf(
				'Не удалось определить город получателя (geo_id) по адресу «%s». Укажите ID пункта выдачи вручную.',
				$order->get_shipping_city() ?: $order->get_billing_city() ?: '—'
			) );
		}

		$points = $this->get_pickup_points( $geo_id );

		if ( ! $points ) {
			wp_send_json_error( sprintf(
				'Яндекс не вернул ни одного пункта выдачи для города с geo_id %d. Проверьте токен API в настройках интеграции или укажите ID пункта выдачи вручную.',
				$geo_id
			) );
		}

		$prepared = [];

		foreach ( $points as $id => $address ) {
			$prepared[] = [ 'id' => $id, 'text' => $address ];
		}

		wp_send_json_success( [ 'geo_id' => $geo_id, 'points' => $prepared ] );
	}

	/**
	 * Список ПВЗ города: id => адрес. Кешируется, потому что для крупных городов
	 * запрос тяжёлый (сам плагин Woodev на нём принудительно отключает кеш).
	 */
	private function get_pickup_points( int $geo_id ): array {

		if ( ! $geo_id || ! function_exists( 'wc_yandex_delivery_get_points_list' ) ) {
			return [];
		}

		$cache_key = 'wcyd_points_' . $geo_id;
		$cached    = get_transient( $cache_key );

		if ( is_array( $cached ) ) {
			return $cached;
		}

		$points = [];

		foreach ( wc_yandex_delivery_get_points_list( [ 'geo_id' => $geo_id ] ) as $point ) {

			if ( empty( $point->id ) ) {
				continue;
			}

			$points[ (string) $point->id ] = $this->get_station_address( $point ) ?: (string) $point->id;
		}

		asort( $points );

		if ( $points ) {
			set_transient( $cache_key, $points, self::POINTS_CACHE_TTL );
		}

		return $points;
	}

	/**
	 * Сохранение восстановленных данных.
	 */
	public function ajax_save_repair() {

		$order = $this->authorize_request();

		$station_id = '';

		if ( ! empty( $_POST['pickup_point_id_manual'] ) ) {
			$station_id = sanitize_text_field( wp_unslash( $_POST['pickup_point_id_manual'] ) );
		} elseif ( ! empty( $_POST['pickup_point_id'] ) ) {
			$station_id = sanitize_text_field( wp_unslash( $_POST['pickup_point_id'] ) );
		}

		$changes = [];

		if ( '' !== $station_id ) {

			// Проверяем ПВЗ в базе Яндекса до сохранения: иначе на экспорте
			// снова получим «Not found station by station_id», но уже без объяснений.
			$point = $this->find_station( $station_id );
			$force = ! empty( $_POST['force'] );

			if ( ! $point && ! $force ) {
				wp_send_json_error( [
					'message'   => sprintf(
						'Пункт выдачи с ID «%s» не найден в базе Яндекса. С несуществующим ID экспорт завершится ошибкой «Not found station by station_id», поэтому по умолчанию мы его не сохраняем. Проверьте ID в личном кабинете Яндекс Доставки — либо сохраните без проверки, если уверены (например, API Яндекса сейчас недоступно).',
						$station_id
					),
					'can_force' => true,
				] );
			}

			$address = $this->get_station_address( $point ) ?: $station_id;

			$order->set_destination_station_id( $station_id );
			$order->set_destination_station_address( $address );

			$changes[] = sprintf( 'пункт выдачи — %s', $address );
		}

		if ( '' === (string) $order->get_state_status() ) {
			$order->set_state_status( 'NEW' );
			$changes[] = 'статус Яндекс доставки — «Новый»';
		}

		if ( ! $changes ) {
			wp_send_json_error( 'Нечего сохранять: выберите пункт выдачи или укажите его ID вручную.' );
		}

		$order->add_order_note( sprintf( 'Данные Яндекс доставки восстановлены вручную: %s.', implode( ', ', $changes ) ) );
		$order->save();

		wp_send_json_success( sprintf( 'Сохранено: %s.', implode( ', ', $changes ) ) );
	}

	/* ---------------------------------------------------------------------
	 * Диагностика: показываем настоящую ошибку от API Яндекса
	 * ------------------------------------------------------------------ */

	public function ajax_diagnose() {

		$order  = $this->authorize_request();
		$method = $this->get_method_instance( $order );

		$destination_id = (string) $order->get_destination_station_id();
		$source_id      = $method ? (string) $method->get_platform_station_id() : '';
		$is_pickup      = $this->is_pickup_order( $order );

		$report = [
			sprintf( 'Заказ #%s', $order->get_order_number() ),
			sprintf( 'Статус Яндекс доставки: %s', $order->get_state_status() ?: '— (не задан, заказ не виден в списке заказов Я.Доставки)' ),
			sprintf( 'Метод доставки: %s', $method ? $method->get_method_title() : '— (не найден в настройках Woocommerce)' ),
			sprintf( 'Тариф: %s', $is_pickup ? 'до ПВЗ' : 'до двери' ),
		];

		$blocker  = '';
		$warnings = [];

		if ( $is_pickup ) {

			if ( '' === $destination_id ) {

				$report[] = 'ПВЗ получателя: — НЕ СОХРАНЁН';
				$blocker  = 'В заявку уходит пустой ПВЗ получателя — именно поэтому Яндекс отвечает «Not found station by station_id». Выберите пункт выдачи выше и нажмите «Восстановить данные заказа».';

			} else {

				$destination_point = $this->find_station( $destination_id );

				$report[] = sprintf( 'ПВЗ получателя: %s', $destination_point
					? sprintf( '%s — %s', $destination_id, $this->get_station_address( $destination_point ) ?: 'без адреса' )
					: sprintf( '%s — не найден в базе Яндекса', $destination_id ) );

				if ( ! $destination_point ) {
					$warnings[] = 'Сохранённый ПВЗ получателя не отдаётся API Яндекса по своему ID — возможно, пункт закрыт. Если запрос ниже упадёт с «Not found station by station_id», выберите пункт выдачи заново.';
				}
			}
		}

		if ( '' === $source_id ) {

			$report[] = 'Пункт отгрузки магазина: — не выбран';
			$blocker  = $blocker ?: 'В настройках метода доставки (WooCommerce → Настройки → Доставка → ваш метод «Яндекс доставка») не выбран «Пункт приёма яндекса» или склад отгрузки.';

		} else {

			// Склад магазина (shipment_type = warehouse) в списке ПВЗ не значится,
			// поэтому проверяем по базе только пункты приёма Яндекса.
			$is_dropoff_point = ! $method || 'pickpoint' === $method->get_option( 'shipment_type', 'pickpoint' );
			$source_point     = $is_dropoff_point ? $this->find_station( $source_id ) : null;

			if ( ! $is_dropoff_point ) {
				$report[] = sprintf( 'Пункт отгрузки магазина: %s (собственный склад)', $source_id );
			} else {
				$report[] = sprintf( 'Пункт отгрузки магазина: %s', $source_point
					? sprintf( '%s — %s', $source_id, $this->get_station_address( $source_point ) ?: 'без адреса' )
					: sprintf( '%s — не найден в базе Яндекса', $source_id ) );

				if ( ! $source_point ) {
					$warnings[] = 'Пункт отгрузки магазина не отдаётся API Яндекса по своему ID. Если экспорт падает с «Not found station by station_id» и ПВЗ получателя при этом на месте, перевыберите «Пункт приёма яндекса» в настройках метода доставки.';
				}
			}
		}

		$report[] = '';

		foreach ( $warnings as $warning ) {
			$report[] = sprintf( 'Предупреждение: %s', $warning );
		}

		if ( $warnings ) {
			$report[] = '';
		}

		if ( $blocker ) {
			$report[] = sprintf( 'Экспорт невозможен: %s', $blocker );
			wp_send_json_error( implode( PHP_EOL, $report ) );
		}

		try {

			$offers = $order->create_offers();

			if ( is_wp_error( $offers ) ) {
				$report[] = sprintf( 'Яндекс отклонил запрос: %s', $offers->get_error_message() );
				wp_send_json_error( implode( PHP_EOL, $report ) );
			}

			if ( empty( $offers ) ) {
				$report[] = 'Яндекс не вернул ни одного варианта доставки. Обычно это значит, что для пары «пункт отгрузки → ПВЗ» нет доступных дат: проверьте расписание вывозов и не закрыт ли пункт.';
				wp_send_json_error( implode( PHP_EOL, $report ) );
			}

			$report[] = sprintf( 'Получено вариантов доставки: %d. Экспорт возможен — нажмите «Выбрать и экспортировать».', count( $offers ) );

			wp_send_json_success( implode( PHP_EOL, $report ) );

		} catch ( Throwable $throwable ) {

			// create_offers() ловит только Exception, поэтому TypeError/Error из разбора
			// ответа API доходят сюда и в штатном сценарии превращаются в «пустое окно».
			$report[] = sprintf(
				'Во время запроса произошла ошибка PHP: %s (%s:%d)',
				$throwable->getMessage(),
				$throwable->getFile(),
				$throwable->getLine()
			);

			wp_send_json_error( implode( PHP_EOL, $report ) );
		}
	}

	/**
	 * Окно «Выбор варианта доставки» молча проглатывает ошибки: admin-order.js
	 * возвращает данные только при response.success и никак не показывает отказ.
	 * Дописываем вывод ошибки прямо в окно.
	 */
	public function print_offers_error_script() {

		$screen = function_exists( 'get_current_screen' ) ? get_current_screen() : null;

		if ( ! $screen || ! in_array( $screen->id, $this->get_order_screen_ids(), true ) ) {
			return;
		}
		?>
		<script>
			( function ( $ ) {

				if ( typeof $ === 'undefined' ) {
					return;
				}

				function isOffersRequest( settings ) {
					return settings && typeof settings.data === 'string' && settings.data.indexOf( 'wc_yandex_delivery_get_order_offers' ) !== -1;
				}

				function showError( message ) {

					var $list = $( '.wc-yandex-select-offers-list' );

					if ( ! $list.length ) {
						return;
					}

					if ( $.fn.unblock ) {
						$list.unblock();
					}

					$list.html( $( '<li/>', {
						'class': 'wcyd-offers-error',
						css: { padding: '12px', color: '#d63638', whiteSpace: 'pre-wrap' },
						text: message
					} ) );
				}

				$( document ).ajaxComplete( function ( event, xhr, settings ) {

					if ( ! isOffersRequest( settings ) ) {
						return;
					}

					var response;

					try {
						response = xhr.responseJSON || JSON.parse( xhr.responseText );
					} catch ( error ) {
						showError( 'Сервер вернул некорректный ответ — скорее всего, произошла ошибка PHP при подготовке заявки.\n\n' + String( xhr.responseText ).substring( 0, 1000 ) );
						return;
					}

					if ( response && response.success === false ) {
						showError( 'Яндекс не вернул варианты доставки.\n\n' + ( typeof response.data === 'string' ? response.data : JSON.stringify( response.data ) ) );
					} else if ( response && response.success && ( ! response.data || ! response.data.length ) ) {
						showError( 'Яндекс не вернул ни одного варианта доставки. Проверьте, сохранён ли у заказа пункт выдачи и выбран ли пункт отгрузки в настройках метода доставки.' );
					}
				} );

				$( document ).ajaxError( function ( event, xhr, settings ) {

					if ( ! isOffersRequest( settings ) ) {
						return;
					}

					showError( 'Запрос вариантов доставки завершился ошибкой ' + xhr.status + '.\n\n' + String( xhr.responseText ).substring( 0, 1000 ) );
				} );

			} )( window.jQuery );
		</script>
		<?php
	}
}

WCYD_Order_Repair::instance();
