<?php
/**
 * Plugin Name: RELOD Wishlist × UE Woo Product Grid Bridge
 * Description: Переходный мини-плагин: связывает RELOD Wishlist с виджетом Unlimited Elements Woo Product Grid через Query ID и показывает красивую заглушку, когда избранное пустое.
 * Version: 1.2.0
 * Author: RELOD
 * Requires at least: 5.8
 * Requires PHP: 7.4
 * Text Domain: relod-wishlist-ue-bridge
 * License: GPL-2.0+
 */

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

define( 'RELOD_WL_UE_BRIDGE_VERSION', '1.2.0' );
define( 'RELOD_WL_UE_BRIDGE_FILE', __FILE__ );
define( 'RELOD_WL_UE_BRIDGE_PATH', plugin_dir_path( __FILE__ ) );
define( 'RELOD_WL_UE_BRIDGE_URL', plugin_dir_url( __FILE__ ) );

add_action(
	'plugins_loaded',
	static function () {
		Relod_Wishlist_UE_Bridge::instance();
	},
	30
);

final class Relod_Wishlist_UE_Bridge {

	/**
	 * CSS-класс, помечающий грид избранного.
	 *
	 * Без него JS не отличал бы грид избранного от обычного каталога и удалял
	 * карточку товара из витрины при снятии метки.
	 */
	public const GRID_CLASS = 'relod-wl-ue-wishlist-grid';

	private static ?self $instance = null;

	/**
	 * Использовал ли текущий рендерящийся виджет Query ID избранного.
	 */
	private bool $current_widget_is_wishlist = false;

	public static function instance(): self {
		if ( null === self::$instance ) {
			self::$instance = new self();
		}

		return self::$instance;
	}

	private function __construct() {
		add_action( 'wp_enqueue_scripts', [ $this, 'enqueue_assets' ] );
		add_filter( 'body_class', [ $this, 'body_class' ] );

		// Помечаем разметку виджета, который реально запросил товары из избранного.
		add_action( 'elementor/widget/before_render_content', [ $this, 'reset_widget_flag' ] );
		add_filter( 'elementor/widget/render_content', [ $this, 'mark_wishlist_grid' ], 5, 2 );

		foreach ( $this->get_query_ids() as $query_id ) {
			if ( ! is_string( $query_id ) || '' === $query_id ) {
				continue;
			}

			add_filter( $query_id, [ $this, 'filter_widget_query_args' ], 10, 2 );
		}
	}

	/**
	 * Query IDs that can be used inside Unlimited Elements widget.
	 * Default value expected in widget: relod_wishlist_grid
	 */
	private function get_query_ids(): array {
		$query_ids = apply_filters( 'relod_wl_ue_bridge_query_ids', [ 'relod_wishlist_grid' ] );

		if ( ! is_array( $query_ids ) ) {
			$query_ids = [ 'relod_wishlist_grid' ];
		}

		$query_ids = array_values(
			array_unique(
				array_filter(
					array_map( 'sanitize_key', $query_ids )
				)
			)
		);

		return empty( $query_ids ) ? [ 'relod_wishlist_grid' ] : $query_ids;
	}

	public function body_class( array $classes ): array {
		$classes[] = 'relod-wl-ue-bridge';
		return $classes;
	}

	public function enqueue_assets(): void {
		wp_enqueue_style(
			'relod-wishlist-ue-bridge',
			RELOD_WL_UE_BRIDGE_URL . 'assets/bridge.css',
			[],
			RELOD_WL_UE_BRIDGE_VERSION
		);

		/* Зависит от ядра: слушает его событие relod_wl:changed. */
		$deps = wp_script_is( 'relod-wishlist', 'registered' ) || wp_script_is( 'relod-wishlist', 'enqueued' )
			? [ 'jquery', 'relod-wishlist' ]
			: [ 'jquery' ];

		wp_enqueue_script(
			'relod-wishlist-ue-bridge',
			RELOD_WL_UE_BRIDGE_URL . 'assets/bridge.js',
			$deps,
			RELOD_WL_UE_BRIDGE_VERSION,
			true
		);

		$shop_url = function_exists( 'wc_get_page_permalink' ) ? wc_get_page_permalink( 'shop' ) : home_url( '/' );
		if ( empty( $shop_url ) ) {
			$shop_url = home_url( '/' );
		}

		wp_localize_script(
			'relod-wishlist-ue-bridge',
			'relodWishlistUEBridge',
			[
				'emptyTitle'  => apply_filters( 'relod_wl_ue_bridge_empty_title', __( 'В избранном пока нет товаров', 'relod-wishlist-ue-bridge' ) ),
				'emptyText'   => apply_filters( 'relod_wl_ue_bridge_empty_text', __( 'Добавляйте понравившиеся товары в избранное — они появятся здесь автоматически.', 'relod-wishlist-ue-bridge' ) ),
				'buttonText'  => apply_filters( 'relod_wl_ue_bridge_button_text', __( 'Перейти в каталог', 'relod-wishlist-ue-bridge' ) ),
				'buttonUrl'   => apply_filters( 'relod_wl_ue_bridge_button_url', $shop_url ),
				'emptyInline' => apply_filters( 'relod_wl_ue_bridge_inline_empty_message', __( 'Товаров в избранном пока нет.', 'relod-wishlist-ue-bridge' ) ),
				'gridClass'   => self::GRID_CLASS,
			]
		);
	}

	/**
	 * Unlimited Elements Query ID filter callback.
	 *
	 * @param array $args Original widget query args.
	 * @param array $widget_data Current widget controls values.
	 */
	public function filter_widget_query_args( $args, $widget_data ): array {
		if ( ! is_array( $args ) ) {
			$args = [];
		}

		$this->current_widget_is_wishlist = true;

		/**
		 * Страница с гридом избранного персональна целиком — её нельзя
		 * класть в кеш страниц.
		 *
		 * Иначе WP Rocket сохраняет товары первого посетителя и отдаёт их
		 * всем остальным: счётчик в шапке (он живой) и содержимое грида
		 * (оно из кеша) начинают расходиться — «7 в счётчике, 5 на странице».
		 */
		if ( class_exists( 'Relod_WL_Cache' ) ) {
			Relod_WL_Cache::no_cache( 'RELOD Wishlist UE grid' );
		} elseif ( ! defined( 'DONOTCACHEPAGE' ) ) {
			define( 'DONOTCACHEPAGE', true );
		}

		$wishlist_ids = $this->get_wishlist_ids();

		$args['post_type']              = 'product';
		$args['post_status']            = 'publish';
		$args['ignore_sticky_posts']    = true;
		$args['no_found_rows']          = false;
		$args['update_post_meta_cache'] = true;
		$args['update_post_term_cache'] = true;
		$args['orderby']                = 'post__in';

		if ( empty( $wishlist_ids ) ) {
			$args['post__in']            = [ 0 ];
			$args['posts_per_page']      = 1;
			$args['paged']               = 1;
			$args['ignore_sticky_posts'] = true;
		} else {
			$args['post__in'] = $wishlist_ids;

			if ( empty( $args['posts_per_page'] ) && empty( $args['showposts'] ) ) {
				$args['posts_per_page'] = count( $wishlist_ids );
			}
		}

		/**
		 * Final query args filter.
		 *
		 * @param array $args
		 * @param array $widget_data
		 * @param array $wishlist_ids
		 */
		return apply_filters( 'relod_wl_ue_bridge_final_args', $args, (array) $widget_data, $wishlist_ids );
	}

	/**
	 * Сбрасывает флаг перед рендером очередного виджета.
	 */
	public function reset_widget_flag(): void {
		$this->current_widget_is_wishlist = false;
	}

	/**
	 * Добавляет гриду класс-маркер, если виджет запрашивал товары из избранного.
	 *
	 * @param string $content Разметка виджета.
	 * @param mixed  $widget  Объект виджета Elementor.
	 * @return string
	 */
	public function mark_wishlist_grid( $content, $widget = null ) {
		if ( ! $this->current_widget_is_wishlist ) {
			return $content;
		}

		// Флаг одноразовый: следующий виджет не должен унаследовать метку.
		$this->current_widget_is_wishlist = false;

		if ( ! is_string( $content ) || false === strpos( $content, 'woocommerce_product_grid' ) ) {
			return $content;
		}

		if ( false !== strpos( $content, self::GRID_CLASS ) ) {
			return $content;
		}

		$marked = preg_replace(
			'/(<[a-z][a-z0-9-]*[^>]*\sclass\s*=\s*["\'][^"\']*\bwoocommerce_product_grid\b)/i',
			'$1 ' . self::GRID_CLASS,
			$content,
			1
		);

		return ( null === $marked ) ? $content : $marked;
	}

	/**
	 * Список избранного.
	 *
	 * Relod_Wishlist::get_ids() начиная с 1.2.0 уже отфильтрован от товаров,
	 * которых нет в каталоге (удалены, сняты с публикации). Раньше такие ID
	 * попадали в post__in, WP_Query их не находил, и грид показывал меньше
	 * товаров, чем было в счётчике.
	 */
	private function get_wishlist_ids(): array {
		if ( class_exists( 'Relod_Wishlist' ) && method_exists( 'Relod_Wishlist', 'get_ids' ) ) {
			$ids = Relod_Wishlist::get_ids();
			if ( is_array( $ids ) ) {
				return array_values( array_unique( array_filter( array_map( 'absint', $ids ) ) ) );
			}
		}

		return [];
	}
}
