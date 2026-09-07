<?php
/**
 * Plugin Name: Яндекс доставка — диагностика и восстановление заказов
 * Description: Дополнение к плагину «Яндекс доставка для Woocommerce» (Woodev). Показывает реальную причину, по которой заказ не экспортируется, восстанавливает служебные данные (статус и ПВЗ) у заказов, которые их не получили при оформлении, и страхует оформление заказа, чтобы такие «потерянные» заказы больше не появлялись.
 * Version: 1.0.0
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
 *    (_yandex_delivery_destination_station_id), а Яндекс без него не выдаёт офферы.
 *    Ошибку при этом не видно: assets/js/admin/admin-order.js разбирает ответ как
 *    `if ( response.success ) { return response.data }` и молча игнорирует ошибку.
 */
class WCYD_Order_Repair {

	const META_STATUS  = '_yandex_delivery_state_status';
	const META_STATION = '_yandex_delivery_destination_station_id';
	const META_ADDRESS = '_yandex_delivery_destination_station_address';

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
			add_action( 'admin_post_wcyd_repair_order', [ $this, 'handle_repair' ] );
			add_action( 'wp_ajax_wcyd_diagnose_order', [ $this, 'ajax_diagnose' ] );
			add_action( 'admin_notices', [ $this, 'admin_notices' ] );
			add_action( 'admin_print_footer_scripts', [ $this, 'print_offers_error_script' ], 99 );
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

	/**
	 * @param string             $post_type
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

	private function get_order_screen_ids(): array {

		$ids = [ 'shop_order' ];

		if ( function_exists( 'wc_get_page_screen_id' ) ) {
			$ids[] = wc_get_page_screen_id( 'shop-order' );
		}

		return array_unique( array_filter( $ids ) );
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

	public function render_meta_box( $post ) {

		$order = $this->get_yandex_order( $this->resolve_order_from_screen( $post ) );

		if ( ! $order ) {
			return;
		}

		$missing     = $this->get_missing_data( $order );
		$need_station = isset( $missing['station'] );
		$points      = $need_station ? $this->get_pickup_points( $order ) : [];
		?>
		<style>
			.wcyd-repair p.description { margin: 0 0 12px; }
			.wcyd-repair ul { margin: 0 0 12px 18px; list-style: disc; }
			.wcyd-repair .wcyd-field { margin-bottom: 12px; }
			.wcyd-repair select, .wcyd-repair input[type="text"] { width: 100%; max-width: 640px; }
			.wcyd-repair .wcyd-result { margin-top: 12px; padding: 10px 12px; border-left: 4px solid #72aee6; background: #f6f7f7; white-space: pre-wrap; word-break: break-word; display: none; }
			.wcyd-repair .wcyd-result.is-error { border-left-color: #d63638; }
		</style>
		<div class="wcyd-repair">

			<p class="description">
				У этого заказа выбран метод доставки «Яндекс доставка», но плагин не получил служебные данные при оформлении.
				Из-за этого заказ не попал в раздел «Заказы Я.Доставки» и не экспортируется. Не хватает:
			</p>

			<ul>
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

			<form method="post" action="<?php echo esc_url( admin_url( 'admin-post.php' ) ); ?>">

				<input type="hidden" name="action" value="wcyd_repair_order" />
				<input type="hidden" name="order_id" value="<?php echo esc_attr( $order->get_id() ); ?>" />
				<?php wp_nonce_field( 'wcyd-repair-order-' . $order->get_id() ); ?>

				<?php if ( $need_station ) : ?>

					<div class="wcyd-field">
						<label for="wcyd-pickup-point"><strong>Пункт выдачи (ПВЗ), который выбрал покупатель</strong></label>
						<?php if ( $points ) : ?>
							<select name="pickup_point_id" id="wcyd-pickup-point">
								<option value="">— выберите пункт выдачи —</option>
								<?php foreach ( $points as $id => $address ) : ?>
									<option value="<?php echo esc_attr( $id ); ?>"><?php echo esc_html( $address ); ?></option>
								<?php endforeach; ?>
							</select>
						<?php else : ?>
							<p class="description">
								Список ПВЗ получить не удалось (не определён город получателя или API Яндекса недоступно).
								Укажите идентификатор пункта выдачи вручную — его можно посмотреть в личном кабинете Яндекс Доставки.
							</p>
						<?php endif; ?>
					</div>

					<div class="wcyd-field">
						<label for="wcyd-pickup-point-manual">Либо укажите ID пункта выдачи вручную</label>
						<input type="text" name="pickup_point_id_manual" id="wcyd-pickup-point-manual" placeholder="например, 9d4d1d2e-...-b1f0" />
					</div>

				<?php endif; ?>

				<p class="submit" style="margin: 0; padding: 0;">
					<button type="submit" class="button button-primary">Восстановить данные заказа</button>
					<button type="button" class="button wcyd-diagnose" data-order_id="<?php echo esc_attr( $order->get_id() ); ?>" data-nonce="<?php echo esc_attr( wp_create_nonce( 'wcyd-diagnose-' . $order->get_id() ) ); ?>">Проверить экспорт и показать ошибку</button>
				</p>
			</form>

			<div class="wcyd-result"></div>
		</div>

		<script>
			( function ( $ ) {
				$( document ).on( 'click', '.wcyd-repair .wcyd-diagnose', function ( event ) {

					event.preventDefault();

					var $button = $( this ),
						$result = $button.closest( '.wcyd-repair' ).find( '.wcyd-result' );

					$button.prop( 'disabled', true );
					$result.removeClass( 'is-error' ).text( 'Запрашиваем варианты доставки у Яндекса…' ).show();

					$.post( ajaxurl, {
						action: 'wcyd_diagnose_order',
						order_id: $button.data( 'order_id' ),
						security: $button.data( 'nonce' )
					} ).done( function ( response ) {
						var data = ( response && response.data ) ? response.data : response;
						$result.toggleClass( 'is-error', ! ( response && response.success ) ).text( typeof data === 'string' ? data : JSON.stringify( data ) );
					} ).fail( function ( xhr ) {
						$result.addClass( 'is-error' ).text( 'Сервер вернул ошибку ' + xhr.status + '. Ответ: ' + String( xhr.responseText ).substring( 0, 1000 ) );
					} ).always( function () {
						$button.prop( 'disabled', false );
					} );
				} );
			} )( jQuery );
		</script>
		<?php
	}

	/**
	 * Список ПВЗ города получателя: id => адрес.
	 */
	private function get_pickup_points( WC_Order $order ): array {

		if ( ! function_exists( 'wc_yandex_delivery_get_points_list' ) ) {
			return [];
		}

		$geo_id = $this->get_geo_id( $order );

		if ( ! $geo_id ) {
			return [];
		}

		$points = [];

		foreach ( wc_yandex_delivery_get_points_list( [ 'geo_id' => $geo_id ] ) as $point ) {

			if ( empty( $point->id ) ) {
				continue;
			}

			$points[ $point->id ] = $point->address->full_address ?? $point->name ?? $point->id;
		}

		asort( $points );

		return $points;
	}

	/* ---------------------------------------------------------------------
	 * Обработка формы восстановления
	 * ------------------------------------------------------------------ */

	public function handle_repair() {

		$order_id = isset( $_POST['order_id'] ) ? absint( $_POST['order_id'] ) : 0;

		if ( ! current_user_can( 'edit_shop_orders' ) ) {
			wp_die( 'У вас недостаточно прав на выполнение этой операции.' );
		}

		check_admin_referer( 'wcyd-repair-order-' . $order_id );

		$order = $this->get_yandex_order( $order_id );

		if ( ! $order ) {
			wp_die( 'Заказ не найден.' );
		}

		$station_id = '';

		if ( ! empty( $_POST['pickup_point_id_manual'] ) ) {
			$station_id = sanitize_text_field( wp_unslash( $_POST['pickup_point_id_manual'] ) );
		} elseif ( ! empty( $_POST['pickup_point_id'] ) ) {
			$station_id = sanitize_text_field( wp_unslash( $_POST['pickup_point_id'] ) );
		}

		$changes = [];

		if ( $station_id ) {

			$address = '';

			foreach ( $this->get_pickup_points( $order ) as $id => $point_address ) {
				if ( $id === $station_id ) {
					$address = $point_address;
					break;
				}
			}

			$order->set_destination_station_id( $station_id );
			$order->set_destination_station_address( $address ?: $station_id );

			$changes[] = sprintf( 'пункт выдачи — %s', $address ?: $station_id );
		}

		if ( '' === (string) $order->get_state_status() ) {
			$order->set_state_status( 'NEW' );
			$changes[] = 'статус Яндекс доставки — «Новый»';
		}

		if ( $changes ) {
			$order->add_order_note( sprintf( 'Данные Яндекс доставки восстановлены вручную: %s.', implode( ', ', $changes ) ) );
			$order->save();
		}

		$redirect = wp_get_referer() ?: $order->get_edit_order_url();

		wp_safe_redirect( add_query_arg( 'wcyd_repaired', $changes ? 1 : 0, $redirect ) );
		exit;
	}

	public function admin_notices() {

		if ( ! isset( $_GET['wcyd_repaired'] ) ) {
			return;
		}

		if ( '1' === (string) $_GET['wcyd_repaired'] ) {
			printf( '<div class="notice notice-success is-dismissible"><p>%s</p></div>', 'Данные Яндекс доставки восстановлены. Теперь заказ виден в разделе «Заказы Я.Доставки», и его можно экспортировать кнопкой «Выбрать и экспортировать».' );
		} else {
			printf( '<div class="notice notice-warning is-dismissible"><p>%s</p></div>', 'Нечего было восстанавливать — данные заказа не изменились.' );
		}
	}

	/* ---------------------------------------------------------------------
	 * Диагностика: показываем настоящую ошибку от API Яндекса
	 * ------------------------------------------------------------------ */

	public function ajax_diagnose() {

		$order_id = isset( $_POST['order_id'] ) ? absint( $_POST['order_id'] ) : 0;

		if ( ! current_user_can( 'edit_shop_orders' ) ) {
			wp_send_json_error( 'У вас недостаточно прав на выполнение этой операции.' );
		}

		check_ajax_referer( 'wcyd-diagnose-' . $order_id, 'security' );

		$order = $this->get_yandex_order( $order_id );

		if ( ! $order ) {
			wp_send_json_error( 'Заказ не найден.' );
		}

		$method = $this->get_method_instance( $order );

		$report = [
			sprintf( 'Заказ #%s', $order->get_order_number() ),
			sprintf( 'Статус Яндекс доставки: %s', $order->get_state_status() ?: '— (не задан, заказ не виден в списке заказов Я.Доставки)' ),
			sprintf( 'Метод доставки: %s', $method ? $method->get_method_title() : '— (не найден в настройках Woocommerce)' ),
			sprintf( 'Тариф: %s', $this->is_pickup_order( $order ) ? 'до ПВЗ' : 'до двери' ),
			sprintf( 'ПВЗ получателя: %s', $order->get_destination_station_id() ?: '— (не сохранён)' ),
			sprintf( 'Пункт отгрузки магазина: %s', $method ? ( $method->get_platform_station_id() ?: '— (не выбран в настройках метода)' ) : '—' ),
			'',
		];

		try {

			$offers = $order->create_offers();

			if ( is_wp_error( $offers ) ) {
				$report[] = sprintf( 'Яндекс отклонил запрос: %s', $offers->get_error_message() );
				wp_send_json_error( implode( PHP_EOL, $report ) );
			}

			if ( empty( $offers ) ) {
				$report[] = 'Яндекс не вернул ни одного варианта доставки. Обычно это значит, что для указанной пары «пункт отгрузки → ПВЗ» нет доступных дат, либо ПВЗ закрыт/не обслуживается.';
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
