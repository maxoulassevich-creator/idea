<?php
/**
 * RELOD Wishlist — основной класс.
 *
 * @package relod-wishlist
 */

defined( 'ABSPATH' ) || exit;

final class Relod_Wishlist {

	private static ?self $instance = null;

	public static function instance(): self {
		if ( null === self::$instance ) {
			self::$instance = new self();
		}
		return self::$instance;
	}

	private function __construct() {
		/* Слияние гостевого списка с аккаунтом при входе. */
		add_action( 'wp_login', [ $this, 'merge_guest_on_login' ], 10, 2 );

		/* Фронтенд. */
		add_action( 'wp_enqueue_scripts', [ $this, 'enqueue' ] );
		add_action( 'wp_head', [ $this, 'print_early_click_capture' ], 2 );
		add_action( 'wp_footer', [ $this, 'print_config' ], 1 );
		add_filter( 'script_loader_tag', [ $this, 'no_defer_tag' ], 20, 2 );

		/* AJAX. */
		Relod_WL_Ajax::instance();

		/* Шорткод страницы избранного. */
		add_shortcode( 'relod_wishlist_page', [ $this, 'shortcode_wishlist_page' ] );

		/* Интеграция с виджетом UE. */
		add_action( 'relod_wcwl_render_ue_button', [ $this, 'render_ue_button' ], 10, 2 );
		add_action( 'relod_ue_woo_gallery_data_attrs', [ $this, 'render_gallery_data_attrs' ], 10, 3 );

		/* HPOS. */
		add_action(
			'before_woocommerce_init',
			function () {
				if ( class_exists( '\Automattic\WooCommerce\Utilities\FeaturesUtil' ) ) {
					\Automattic\WooCommerce\Utilities\FeaturesUtil::declare_compatibility( 'custom_order_tables', RELOD_WL_FILE, true );
				}
			}
		);
	}

	/* ════════════════════════════════════════
	   Публичный API хранилища (обратная совместимость)
	   ════════════════════════════════════════ */

	public static function get_ids( ?int $user_id = null ): array {
		return Relod_WL_Store::get_ids( $user_id );
	}

	public static function save_ids( array $ids, ?int $user_id = null ): bool {
		return Relod_WL_Store::save_ids( $ids, $user_id );
	}

	public static function has_product( int $product_id ): bool {
		return in_array( $product_id, self::get_ids(), true );
	}

	public static function get_count(): int {
		return count( self::get_ids() );
	}

	public function merge_guest_on_login( string $user_login, $user ): void {
		if ( $user instanceof WP_User ) {
			Relod_WL_Store::merge_guest_into_user( (int) $user->ID );
		}
	}

	/* ════════════════════════════════════════
	   Ассеты
	   ════════════════════════════════════════ */

	public function enqueue(): void {
		wp_enqueue_style(
			'relod-wishlist',
			RELOD_WL_URL . 'assets/relod-wishlist.css',
			[],
			RELOD_WL_VERSION
		);

		/**
		 * Скрипт намеренно без зависимости от jQuery.
		 *
		 * WP Rocket с «Delay JavaScript execution» откладывает jQuery до первого
		 * взаимодействия, и обработчик клика к моменту нажатия ещё не навешен:
		 * первый клик пропадал, пользователь жал второй раз — получался двойной
		 * тоггл и рассинхрон счётчика.
		 */
		wp_enqueue_script(
			'relod-wishlist',
			RELOD_WL_URL . 'assets/relod-wishlist.js',
			[],
			RELOD_WL_VERSION,
			true
		);

		/**
		 * Базовый конфиг — сразу и всегда: без него скрипт не стартует.
		 * Персонального состояния здесь нет, поэтому его безопасно кешировать.
		 */
		wp_localize_script(
			'relod-wishlist',
			'relodWL',
			[
				'ajax'  => admin_url( 'admin-ajax.php' ),
				'nonce' => wp_create_nonce( Relod_WL_Ajax::NONCE_ACTION ),
				'ids'   => [],
				'fresh' => false,
				'i18n'  => [
					'added'   => __( 'Добавлено в избранное', 'relod-wishlist' ),
					'removed' => __( 'Удалено из избранного', 'relod-wishlist' ),
					'empty'   => __( 'В избранном пока пусто.', 'relod-wishlist' ),
				],
			]
		);
	}

	/**
	 * Перехват кликов до загрузки основного скрипта.
	 *
	 * Крошечный инлайн-обработчик складывает ранние клики в очередь,
	 * основной скрипт её разбирает. Ни один клик не теряется, даже если
	 * оптимизатор отложил загрузку JS.
	 */
	public function print_early_click_capture(): void {
		if ( ! $this->has_frontend() ) {
			return;
		}
		?>
<script id="relod-wl-early" data-no-optimize="1" data-no-defer="1" data-cfasync="false" data-wpfc-render="false">
window.relodWLEarly=(function(){var q=[];function h(e){if(window.relodWLReady){return;}var t=e.target;if(!t||!t.closest){return;}var b=t.closest('.relod-wcwl-btn');if(!b){return;}e.preventDefault();e.stopPropagation();b.classList.add('relod-wl-busy');if(q.indexOf(b)===-1){q.push(b);}}
document.addEventListener('click',h,true);
var api={queue:q,off:function(){document.removeEventListener('click',h,true);}};
window.setTimeout(function(){if(!window.relodWLReady){api.off();for(var i=0;i<q.length;i++){q[i].classList.remove('relod-wl-busy');}q.length=0;}},10000);
return api;})();
</script>
		<?php
	}

	/**
	 * Конфиг печатается в футере (приоритет 1), уже после рендера контента.
	 *
	 * Это важно: решение «можно ли отдавать персональное состояние» зависит
	 * от того, объявил ли шорткод страницу некешируемой, а шорткоды
	 * выполняются позже wp_enqueue_scripts.
	 */
	public function print_config(): void {
		if ( ! $this->has_frontend() ) {
			return;
		}

		/**
		 * Состояние отдаём в HTML только там, где страница точно не уедет
		 * в общий кеш: у залогиненных и на самой странице избранного.
		 * Для всех остальных JS запросит его сам — чужого счётчика в кеше
		 * не останется.
		 *
		 * Если инлайн по какой-то причине не напечатается, скрипт просто
		 * сходит за состоянием по AJAX: сломать это нельзя.
		 */
		if ( ! Relod_WL_Cache::can_render_personal_state() ) {
			return;
		}

		wp_add_inline_script(
			'relod-wishlist',
			'if(window.relodWL){window.relodWL.ids=' . wp_json_encode( self::get_ids() ) . ';window.relodWL.fresh=true;}',
			'before'
		);
	}

	/**
	 * Снимаем defer/async с наших скриптов, если их навесила тема или плагин.
	 */
	public function no_defer_tag( $tag, $handle ) {
		if ( ! in_array( $handle, Relod_WL_Cache::instance()->handles(), true ) ) {
			return $tag;
		}

		$tag = (string) preg_replace( '/\s(?:defer|async)(?:=([\'"]).*?\1)?(?=[\s>])/i', '', $tag );

		if ( false === strpos( $tag, 'data-no-defer' ) ) {
			$tag = str_replace( '<script ', '<script data-no-optimize="1" data-no-defer="1" data-cfasync="false" ', $tag );
		}

		return $tag;
	}

	private function has_frontend(): bool {
		if ( is_admin() || ( defined( 'DOING_AJAX' ) && DOING_AJAX ) ) {
			return false;
		}
		return wp_script_is( 'relod-wishlist', 'enqueued' ) || wp_script_is( 'relod-wishlist', 'done' );
	}

	/* ════════════════════════════════════════
	   Иконки
	   ════════════════════════════════════════ */

	public static function svg_inactive_url(): string {
		return RELOD_WL_URL . 'assets/svg/inactive.svg';
	}

	public static function svg_active_url(): string {
		return RELOD_WL_URL . 'assets/svg/active.svg';
	}

	/**
	 * Разметка кнопки избранного.
	 *
	 * Класс is-active ставится только тогда, когда состояние точно не уедет
	 * в общий кеш страниц. В остальных случаях кнопка рендерится нейтральной,
	 * а активной её делает JS после гидрации.
	 */
	public static function button_html( int $product_id, array $classes = [], array $attrs = [] ): string {
		$personal = Relod_WL_Cache::can_render_personal_state();
		$active   = $personal && self::has_product( $product_id );

		$classes[] = 'relod-wcwl-btn';
		if ( $active ) {
			$classes[] = 'is-active';
		}
		if ( ! $personal ) {
			$classes[] = 'relod-wl-unresolved';
		}

		$attr_html = '';
		foreach ( $attrs as $name => $value ) {
			$attr_html .= ' ' . esc_attr( $name ) . '="' . esc_attr( $value ) . '"';
		}

		ob_start();
		?>
		<button type="button"
			class="<?php echo esc_attr( implode( ' ', array_unique( array_filter( $classes ) ) ) ); ?>"
			data-product-id="<?php echo esc_attr( (string) $product_id ); ?>"
			data-relod-wl-button="1"
			aria-pressed="<?php echo $active ? 'true' : 'false'; ?>"
			aria-label="<?php esc_attr_e( 'Избранное', 'relod-wishlist' ); ?>"<?php echo $attr_html; // phpcs:ignore WordPress.Security.EscapeOutput ?>>
			<span class="relod-wcwl-icon relod-wcwl-icon--inactive"
				style="background-image:url('<?php echo esc_url( self::svg_inactive_url() ); ?>')"></span>
			<span class="relod-wcwl-icon relod-wcwl-icon--active"
				style="background-image:url('<?php echo esc_url( self::svg_active_url() ); ?>')"></span>
		</button>
		<?php
		return (string) ob_get_clean();
	}

	/* ════════════════════════════════════════
	   Интеграция с виджетом UE
	   ════════════════════════════════════════ */

	public function render_ue_button( int $product_id, string $uc_id = '' ): void {
		echo self::button_html( $product_id, [ 'ue-wishlist-btn' ] ); // phpcs:ignore WordPress.Security.EscapeOutput
	}

	public function render_gallery_data_attrs( int $product_id, string $image_size = 'woocommerce_thumbnail', int $max = 4 ): void {
		$product = wc_get_product( $product_id );
		if ( ! $product ) {
			return;
		}

		$ids = $product->get_gallery_image_ids();
		if ( empty( $ids ) ) {
			return;
		}

		$main_id = $product->get_image_id();
		$all     = $main_id ? array_merge( [ $main_id ], $ids ) : $ids;
		$all     = array_slice( array_unique( $all ), 0, $max );

		$urls = [];
		foreach ( $all as $att_id ) {
			$src = wp_get_attachment_image_url( $att_id, $image_size );
			if ( $src ) {
				$urls[] = $src;
			}
		}

		if ( count( $urls ) < 2 ) {
			return;
		}

		echo ' data-ue-gallery="' . esc_attr( (string) wp_json_encode( $urls ) ) . '"';
	}

	/* ════════════════════════════════════════
	   Шорткод страницы избранного
	   ════════════════════════════════════════ */

	public function shortcode_wishlist_page( $atts ): string {
		$atts = shortcode_atts(
			[
				'columns'    => 4,
				'empty_text' => __( 'В избранном пока пусто.', 'relod-wishlist' ),
			],
			$atts,
			'relod_wishlist_page'
		);

		/**
		 * Страница избранного персональна целиком — её нельзя класть в кеш.
		 * Иначе первый посетитель «фиксирует» свой список для всех остальных,
		 * и счётчик в шапке перестаёт сходиться с содержимым страницы.
		 */
		Relod_WL_Cache::no_cache( 'RELOD Wishlist page' );

		$config = wp_json_encode(
			[
				'columns'   => absint( $atts['columns'] ),
				'emptyText' => (string) $atts['empty_text'],
			]
		);

		return '<div class="relod-wl-page" data-relod-wl-page="' . esc_attr( (string) $config ) . '">'
			. $this->render_page_inner( $atts )
			. '</div>';
	}

	/**
	 * Внутренняя разметка страницы избранного.
	 *
	 * Вынесена отдельно, потому что этим же кодом отвечает AJAX-эндпоинт
	 * relod_wl_page: если страница всё-таки пришла из кеша, JS заменяет
	 * содержимое на актуальное.
	 */
	public function render_page_inner( array $atts ): string {
		$atts = wp_parse_args(
			$atts,
			[
				'columns'    => 4,
				'empty_text' => __( 'В избранном пока пусто.', 'relod-wishlist' ),
			]
		);

		$ids = self::get_ids();

		if ( empty( $ids ) ) {
			return $this->empty_html( (string) $atts['empty_text'] );
		}

		/* Один запрос вместо N: дальше wc_get_product() берёт посты из кеша. */
		_prime_post_caches( $ids, true, true );

		$products = [];
		foreach ( $ids as $pid ) {
			$product = wc_get_product( $pid );
			if ( $product instanceof WC_Product ) {
				$products[] = $product;
			}
		}

		if ( empty( $products ) ) {
			return $this->empty_html( (string) $atts['empty_text'] );
		}

		ob_start();
		?>
		<div class="relod-wl-grid" data-relod-wl-grid="1" style="--relod-wl-cols:<?php echo absint( $atts['columns'] ); ?>">
			<?php
			foreach ( $products as $product ) {
				$this->render_product_card( $product );
			}
			?>
		</div>
		<?php
		return (string) ob_get_clean();
	}

	private function empty_html( string $text ): string {
		return '<div class="relod-wl-empty"><p>' . esc_html( $text ) . '</p></div>';
	}

	/* ────────────────────────────────────────
	   Карточка товара
	   ──────────────────────────────────────── */

	private function render_product_card( WC_Product $product ): void {
		$pid        = $product->get_id();
		$link       = $product->get_permalink();
		$title      = $product->get_name();
		$price_html = $product->get_price_html();
		$image_size = apply_filters( 'relod_wl_image_size', 'woocommerce_thumbnail' );

		$gallery_ids = $product->get_gallery_image_ids();
		$main_img_id = $product->get_image_id();
		$main_img    = $main_img_id ? wp_get_attachment_image_url( $main_img_id, $image_size ) : wc_placeholder_img_src( $image_size );
		$main_alt    = $main_img_id ? get_post_meta( $main_img_id, '_wp_attachment_image_alt', true ) : $title;

		$gallery_urls = [];
		if ( $main_img_id ) {
			$gallery_urls[] = $main_img;
		}
		foreach ( array_slice( $gallery_ids, 0, 3 ) as $att_id ) {
			$src = wp_get_attachment_image_url( $att_id, $image_size );
			if ( $src ) {
				$gallery_urls[] = $src;
			}
		}
		$gallery_urls = array_unique( $gallery_urls );

		$has_scrub  = count( $gallery_urls ) >= 2;
		$scrub_json = $has_scrub ? esc_attr( (string) wp_json_encode( array_values( $gallery_urls ) ) ) : '';

		$swatches = $product->is_type( 'variable' ) ? $this->get_variation_swatches( $product ) : [];
		?>
		<div class="relod-wl-card" data-product-id="<?php echo esc_attr( (string) $pid ); ?>">
			<div class="relod-wl-card__image-wrap">
				<a href="<?php echo esc_url( $link ); ?>" class="relod-wl-card__image-link">
					<div class="relod-wl-card__image"<?php if ( $has_scrub ) : ?> data-ue-gallery="<?php echo $scrub_json; // phpcs:ignore WordPress.Security.EscapeOutput ?>"<?php endif; ?>>
						<img src="<?php echo esc_url( (string) $main_img ); ?>"
							alt="<?php echo esc_attr( (string) $main_alt ); ?>"
							loading="lazy" decoding="async">
					</div>
				</a>
				<?php echo self::button_html( (int) $pid, [ 'relod-wl-btn' ] ); // phpcs:ignore WordPress.Security.EscapeOutput ?>
			</div>

			<div class="relod-wl-card__body">
				<a href="<?php echo esc_url( $link ); ?>" class="relod-wl-card__title"><?php echo esc_html( $title ); ?></a>
				<div class="relod-wl-card__price"><?php echo $price_html; // phpcs:ignore ?></div>

				<?php if ( ! empty( $swatches ) ) : ?>
					<div class="relod-wl-card__swatches">
						<?php foreach ( $swatches as $sw ) : ?>
							<a href="<?php echo esc_url( $sw['url'] ); ?>"
								class="relod-wl-swatch<?php echo $sw['out_of_stock'] ? ' is-out-of-stock' : ''; ?>"
								title="<?php echo esc_attr( $sw['name'] ); ?>">
								<span class="relod-wl-swatch__color" style="background:<?php echo esc_attr( $sw['color'] ); ?>"></span>
							</a>
						<?php endforeach; ?>
					</div>
				<?php endif; ?>
			</div>
		</div>
		<?php
	}

	/* ────────────────────────────────────────
	   Свотчи вариаций
	   ──────────────────────────────────────── */

	private function get_variation_swatches( WC_Product_Variable $product ): array {
		$swatches   = [];
		$variations = $product->get_available_variations();
		$link       = $product->get_permalink();
		$seen       = [];

		$color_attr = '';
		foreach ( $product->get_variation_attributes() as $attr_name => $values ) {
			$lower = mb_strtolower( (string) $attr_name );
			if ( false !== strpos( $lower, 'color' ) || false !== strpos( $lower, 'colour' ) || false !== strpos( $lower, 'цвет' ) ) {
				$color_attr = $attr_name;
				break;
			}
		}

		if ( ! $color_attr ) {
			return [];
		}

		$taxonomy = wc_attribute_taxonomy_name( str_replace( 'pa_', '', $color_attr ) );
		if ( taxonomy_exists( $taxonomy ) ) {
			$color_attr = $taxonomy;
		}

		foreach ( $variations as $var ) {
			$attr_key = 'attribute_' . sanitize_title( $color_attr );
			$val      = $var['attributes'][ $attr_key ] ?? '';
			if ( ! $val || isset( $seen[ $val ] ) ) {
				continue;
			}
			$seen[ $val ] = true;

			$hex = '';
			if ( taxonomy_exists( $color_attr ) ) {
				$term = get_term_by( 'slug', $val, $color_attr );
				if ( $term ) {
					foreach ( [ 'color', '_color', 'product_attribute_color' ] as $meta_key ) {
						$hex = get_term_meta( $term->term_id, $meta_key, true );
						if ( $hex ) {
							break;
						}
					}
				}
			}

			if ( ! $hex ) {
				$hex = '#cccccc';
			}

			$swatches[] = [
				'name'         => ucfirst( $val ),
				'color'        => $hex,
				'url'          => $link,
				'out_of_stock' => ! $var['is_in_stock'],
			];
		}

		return $swatches;
	}
}
