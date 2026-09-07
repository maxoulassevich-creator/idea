<?php
/**
 * RELOD Wishlist — совместимость с кешированием страниц (WP Rocket и др.).
 *
 * Правило простое: персональные данные не должны попадать в кешируемый HTML.
 *  • Счётчик и состояние кнопок больше не рендерятся на сервере для гостей —
 *    их подставляет JS из некешируемого AJAX-ответа.
 *  • Страницы, которые целиком состоят из избранного (шорткод / грид UE),
 *    исключаются из кеша: там персонализацию клиентом уже не спрятать.
 *  • Скрипты плагина выводятся из-под «Delay JavaScript execution» и defer,
 *    иначе первый клик по сердечку уходит в никуда.
 *
 * @package relod-wishlist
 */

defined( 'ABSPATH' ) || exit;

final class Relod_WL_Cache {

	/** Опция со списком ID страниц, где стоит шорткод избранного. */
	const PAGES_OPTION = 'relod_wl_wishlist_pages';

	private static ?self $instance = null;

	/** Уже пометили текущий запрос как некешируемый. */
	private bool $no_cache_done = false;

	/** Хендлы скриптов плагинов семейства RELOD Wishlist. */
	private array $handles = [
		'relod-wishlist',
		'relod-wlsc-frontend',
		'relod-wishlist-ue-bridge',
	];

	public static function instance(): self {
		if ( null === self::$instance ) {
			self::$instance = new self();
		}
		return self::$instance;
	}

	private function __construct() {
		/* Наши скрипты нельзя откладывать: иначе клик не обрабатывается. */
		add_filter( 'rocket_exclude_defer_js', [ $this, 'exclude_patterns' ] );
		add_filter( 'rocket_delay_js_exclusions', [ $this, 'exclude_patterns' ] );
		add_filter( 'rocket_exclude_js', [ $this, 'exclude_paths' ] );
		add_filter( 'rocket_minify_excluded_external_js', [ $this, 'exclude_patterns' ] );
		add_filter( 'rocket_defer_inline_exclusions', [ $this, 'exclude_patterns' ] );

		/* LiteSpeed Cache. */
		add_filter( 'litespeed_optm_js_defer_exc', [ $this, 'exclude_patterns' ] );
		add_filter( 'litespeed_optm_gm_js_exc', [ $this, 'exclude_patterns' ] );

		/* Страницы избранного — мимо кеша. */
		add_filter( 'rocket_cache_reject_uri', [ $this, 'reject_wishlist_uris' ] );

		/* Поддерживаем список страниц с шорткодом в актуальном состоянии. */
		add_action( 'save_post', [ $this, 'maybe_index_post' ], 20, 2 );
		add_action( 'deleted_post', [ $this, 'drop_from_index' ] );

		/* Чистим кеш страниц при обновлении плагина. */
		add_action( 'relod_wl_purge_page_cache', [ $this, 'purge' ] );
	}

	/* ────────────────────────────────────────
	   Некешируемый запрос
	   ──────────────────────────────────────── */

	/**
	 * Помечает текущий запрос как непригодный для кеша страниц.
	 *
	 * WP Rocket проверяет DONOTCACHEPAGE и do_rocket_generate_caching_files
	 * в момент сброса буфера, поэтому вызов прямо во время рендера шорткода
	 * ещё успевает сработать — страница просто не попадёт в кеш.
	 */
	public static function no_cache( string $reason = 'RELOD Wishlist' ): void {
		$self = self::instance();

		if ( $self->no_cache_done ) {
			return;
		}
		$self->no_cache_done = true;

		if ( ! defined( 'DONOTCACHEPAGE' ) ) {
			define( 'DONOTCACHEPAGE', true );
		}
		if ( ! defined( 'DONOTCACHEOBJECT' ) ) {
			define( 'DONOTCACHEOBJECT', true );
		}
		if ( ! defined( 'DONOTCACHEDB' ) ) {
			define( 'DONOTCACHEDB', true );
		}

		/* WP Rocket. */
		add_filter( 'do_rocket_generate_caching_files', '__return_false' );
		add_filter( 'rocket_override_donotcachepage', '__return_true' );

		/* LiteSpeed Cache. */
		do_action( 'litespeed_control_set_nocache', $reason );

		/* WP Super Cache / W3TC / Batcache читают DONOTCACHEPAGE сами. */

		if ( ! headers_sent() ) {
			nocache_headers();
		}
	}

	/**
	 * Можно ли отрисовать персональное состояние прямо в HTML.
	 *
	 * Для гостей ответ почти всегда «нет»: страницу может забрать кеш и
	 * отдать её другому посетителю вместе с чужим счётчиком и чужими
	 * активными сердечками. Именно так и появлялись «7 в шапке, 5 на странице».
	 */
	public static function can_render_personal_state(): bool {
		if ( defined( 'DONOTCACHEPAGE' ) && DONOTCACHEPAGE ) {
			return true;
		}

		/* Кеша страниц нет — прятать нечего и не от кого. */
		if ( ! self::page_cache_active() ) {
			return (bool) apply_filters( 'relod_wl_render_personal_state', true );
		}

		if ( is_user_logged_in() ) {
			/* WP Rocket кеширует залогиненных только при явно включённой опции. */
			if ( function_exists( 'get_rocket_option' ) && get_rocket_option( 'cache_logged_user' ) ) {
				return (bool) apply_filters( 'relod_wl_render_personal_state', false );
			}
			return (bool) apply_filters( 'relod_wl_render_personal_state', true );
		}

		return (bool) apply_filters( 'relod_wl_render_personal_state', false );
	}

	/**
	 * Активен ли вообще плагин кеширования страниц.
	 */
	public static function page_cache_active(): bool {
		$active = defined( 'WP_ROCKET_VERSION' )
			|| defined( 'LSCWP_V' )
			|| defined( 'WPCACHEHOME' )
			|| defined( 'W3TC' )
			|| ( defined( 'WP_CACHE' ) && WP_CACHE );

		return (bool) apply_filters( 'relod_wl_page_cache_active', $active );
	}

	/* ────────────────────────────────────────
	   Исключения оптимизаторов JS
	   ──────────────────────────────────────── */

	public function exclude_patterns( $excluded ): array {
		if ( ! is_array( $excluded ) ) {
			$excluded = [];
		}

		return array_merge(
			$excluded,
			[
				'relod-wishlist',
				'relod-wlsc-frontend',
				'relod-wishlist-ue-bridge',
				'relodWL',
				'relodWLSC',
				'relodWishlistUEBridge',
				'relod-wl-early',
				'/wp-content/plugins/relod-wishlist',
			]
		);
	}

	public function exclude_paths( $excluded ): array {
		if ( ! is_array( $excluded ) ) {
			$excluded = [];
		}

		$excluded[] = '/wp-content/plugins/relod-wishlist/assets/(.*).js';
		$excluded[] = '/wp-content/plugins/relod-wishlist-shortcodes/assets/js/(.*).js';
		$excluded[] = '/wp-content/plugins/relod-wishlist-ue-bridge/assets/(.*).js';

		return $excluded;
	}

	/**
	 * Хендлы, которым нельзя ставить defer.
	 */
	public function handles(): array {
		return $this->handles;
	}

	/* ────────────────────────────────────────
	   Индекс страниц избранного
	   ──────────────────────────────────────── */

	public function reject_wishlist_uris( $uris ): array {
		if ( ! is_array( $uris ) ) {
			$uris = [];
		}

		foreach ( self::get_wishlist_pages() as $page_id ) {
			$path = wp_parse_url( (string) get_permalink( $page_id ), PHP_URL_PATH );
			if ( $path && '/' !== $path ) {
				$uris[] = rtrim( $path, '/' ) . '(/.*)?';
			}
		}

		return $uris;
	}

	public static function get_wishlist_pages(): array {
		$pages = get_option( self::PAGES_OPTION, [] );
		return is_array( $pages ) ? array_values( array_unique( array_map( 'absint', $pages ) ) ) : [];
	}

	public function maybe_index_post( $post_id, $post = null ): void {
		if ( ! $post instanceof WP_Post || wp_is_post_revision( $post_id ) ) {
			return;
		}

		$has     = self::content_has_wishlist( $post );
		$pages   = self::get_wishlist_pages();
		$post_id = (int) $post_id;
		$in_list = in_array( $post_id, $pages, true );

		if ( $has && ! $in_list ) {
			$pages[] = $post_id;
		} elseif ( ! $has && $in_list ) {
			$pages = array_values( array_diff( $pages, [ $post_id ] ) );
		} else {
			return;
		}

		update_option( self::PAGES_OPTION, $pages, true );
	}

	public function drop_from_index( $post_id ): void {
		$pages = self::get_wishlist_pages();
		if ( ! in_array( (int) $post_id, $pages, true ) ) {
			return;
		}
		update_option( self::PAGES_OPTION, array_values( array_diff( $pages, [ (int) $post_id ] ) ), true );
	}

	public static function content_has_wishlist( WP_Post $post ): bool {
		$content = (string) $post->post_content;

		if ( has_shortcode( $content, 'relod_wishlist_page' ) ) {
			return true;
		}

		/* Elementor хранит разметку в мете, шорткод и Query ID лежат там же. */
		$elementor = get_post_meta( $post->ID, '_elementor_data', true );
		if ( is_string( $elementor ) && '' !== $elementor ) {
			if ( false !== strpos( $elementor, 'relod_wishlist_page' ) || false !== strpos( $elementor, 'relod_wishlist_grid' ) ) {
				return true;
			}
		}

		return false;
	}

	/**
	 * Полная переиндексация — на активации и при обновлении версии.
	 *
	 * Ищем напрямую в БД: WP_Query по 's' не заглядывает в постмету, а
	 * Elementor держит разметку именно там, поэтому страницы, собранные
	 * виджетом, обычным поиском не находятся.
	 */
	public static function reindex(): void {
		global $wpdb;

		$found = [];

		$from_content = $wpdb->get_col(
			"SELECT ID FROM {$wpdb->posts}
			 WHERE post_status IN ('publish','private','draft')
			   AND post_content LIKE '%relod_wishlist_page%'
			 LIMIT 100"
		);

		$from_meta = $wpdb->get_col(
			"SELECT DISTINCT post_id FROM {$wpdb->postmeta}
			 WHERE meta_key = '_elementor_data'
			   AND (meta_value LIKE '%relod_wishlist_page%' OR meta_value LIKE '%relod_wishlist_grid%')
			 LIMIT 100"
		);

		foreach ( array_merge( (array) $from_content, (array) $from_meta ) as $post_id ) {
			$post_id = (int) $post_id;
			if ( $post_id > 0 ) {
				$found[] = $post_id;
			}
		}

		update_option( self::PAGES_OPTION, array_values( array_unique( $found ) ), true );
	}

	/* ────────────────────────────────────────
	   Сброс кеша страниц
	   ──────────────────────────────────────── */

	/**
	 * Сбрасывает кеш страниц: старые копии могут содержать чужой счётчик.
	 */
	public function purge(): void {
		if ( function_exists( 'rocket_clean_domain' ) ) {
			rocket_clean_domain();
		}
		if ( function_exists( 'wp_cache_clear_cache' ) ) {
			wp_cache_clear_cache();
		}
		if ( has_action( 'litespeed_purge_all' ) ) {
			do_action( 'litespeed_purge_all' );
		}
		if ( function_exists( 'w3tc_flush_posts' ) ) {
			w3tc_flush_posts();
		}
	}
}
