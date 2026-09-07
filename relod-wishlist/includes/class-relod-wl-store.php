<?php
/**
 * RELOD Wishlist — хранилище избранного.
 *
 * Ключевые отличия от версии 1.0.0:
 *  • Гостевые списки лежат в собственной таблице, а не в транзиентах.
 *    Транзиенты вытесняются объектным кешем (Redis/Memcached) и стираются
 *    при «Очистить кеш», из-за чего избранное молча пропадало.
 *  • Запись возвращает bool. Раньше save_ids() тихо ничего не делал,
 *    если не было гостевой куки, а AJAX всё равно отвечал success —
 *    счётчик рос, товары никуда не сохранялись.
 *  • Изменения списка идут через mutate() под блокировкой MySQL,
 *    поэтому два быстрых клика больше не затирают друг друга.
 *  • prune() выкидывает товары, которых уже нет в каталоге, поэтому
 *    счётчик всегда совпадает с тем, что реально рендерится на странице.
 *
 * @package relod-wishlist
 */

defined( 'ABSPATH' ) || exit;

final class Relod_WL_Store {

	/** Версия схемы БД. */
	const DB_VERSION = '1';

	/** Опция с версией схемы. */
	const DB_VERSION_OPTION = 'relod_wl_db_version';

	/** Опция-флаг: таблица недоступна, работаем на транзиентах. */
	const FALLBACK_OPTION = 'relod_wl_storage_fallback';

	/** Хук ежедневной уборки. */
	const GC_HOOK = 'relod_wl_gc';

	/** Ключ гостя, созданный в текущем запросе. */
	private static string $runtime_guest_key = '';

	/** Мемоизация очищенных списков в пределах запроса. */
	private static array $memo = [];

	/** Держим имя захваченной блокировки, чтобы гарантированно её отпустить. */
	private static string $held_lock = '';

	/* ────────────────────────────────────────
	   Таблица
	   ──────────────────────────────────────── */

	public static function table(): string {
		global $wpdb;
		return $wpdb->prefix . 'relod_wishlist_guests';
	}

	public static function on_activate(): void {
		self::install();
		self::schedule_gc();
	}

	public static function on_deactivate(): void {
		$timestamp = wp_next_scheduled( self::GC_HOOK );
		if ( $timestamp ) {
			wp_unschedule_event( $timestamp, self::GC_HOOK );
		}
	}

	public static function install(): void {
		global $wpdb;

		require_once ABSPATH . 'wp-admin/includes/upgrade.php';

		$table   = self::table();
		$collate = $wpdb->get_charset_collate();

		$sql = "CREATE TABLE {$table} (
			guest_key varchar(64) NOT NULL,
			product_ids longtext NOT NULL,
			updated_at datetime NOT NULL DEFAULT '0000-00-00 00:00:00',
			PRIMARY KEY  (guest_key),
			KEY updated_at (updated_at)
		) {$collate};";

		dbDelta( $sql );

		if ( self::table_exists( true ) ) {
			update_option( self::DB_VERSION_OPTION, self::DB_VERSION, true );
			update_option( self::FALLBACK_OPTION, '0', true );
		} else {
			// Таблицу создать не удалось — не ломаемся, уходим на транзиенты.
			update_option( self::FALLBACK_OPTION, '1', true );
		}
	}

	/**
	 * Досоздаёт таблицу после обновления плагина без деактивации.
	 */
	public static function maybe_upgrade(): void {
		if ( get_option( self::DB_VERSION_OPTION ) === self::DB_VERSION ) {
			return;
		}
		self::install();
		self::schedule_gc();
	}

	private static function schedule_gc(): void {
		if ( ! wp_next_scheduled( self::GC_HOOK ) ) {
			wp_schedule_event( time() + HOUR_IN_SECONDS, 'daily', self::GC_HOOK );
		}
	}

	public static function table_exists( bool $force = false ): bool {
		static $exists = null;

		if ( ! $force && null !== $exists ) {
			return $exists;
		}

		if ( ! $force && '1' === get_option( self::FALLBACK_OPTION ) ) {
			$exists = false;
			return $exists;
		}

		global $wpdb;
		$table  = self::table();
		$found  = $wpdb->get_var( $wpdb->prepare( 'SHOW TABLES LIKE %s', $wpdb->esc_like( $table ) ) );
		$exists = ( $found === $table );

		return $exists;
	}

	/**
	 * Удаляет заброшенные гостевые списки.
	 */
	public static function gc(): void {
		if ( ! self::table_exists() ) {
			return;
		}

		global $wpdb;

		$ttl_days = (int) apply_filters( 'relod_wl_guest_ttl_days', 90 );
		if ( $ttl_days < 1 ) {
			return;
		}

		$cutoff = gmdate( 'Y-m-d H:i:s', time() - $ttl_days * DAY_IN_SECONDS );

		$wpdb->query(
			$wpdb->prepare(
				'DELETE FROM ' . self::table() . ' WHERE updated_at < %s', // phpcs:ignore WordPress.DB.PreparedSQL
				$cutoff
			)
		);
	}

	/* ────────────────────────────────────────
	   Ключ гостя
	   ──────────────────────────────────────── */

	/**
	 * Ключ гостевого списка.
	 *
	 * Кука создаётся ТОЛЬКО в AJAX-запросах ($create = true). При обычном
	 * рендере страницы это бессмысленно: WP Rocket отдаёт готовый HTML без
	 * запуска PHP, заголовок Set-Cookie в кеш не попадает, и посетитель
	 * оставался без ключа — а значит, и без сохранённого избранного.
	 */
	public static function guest_key( bool $create = false ): string {
		if ( isset( $_COOKIE[ RELOD_WL_COOKIE ] ) ) {
			$key = sanitize_key( wp_unslash( $_COOKIE[ RELOD_WL_COOKIE ] ) );
			if ( '' !== $key ) {
				return $key;
			}
		}

		if ( '' !== self::$runtime_guest_key ) {
			return self::$runtime_guest_key;
		}

		if ( ! $create || headers_sent() ) {
			return '';
		}

		$key = str_replace( '-', '', wp_generate_uuid4() );

		$expire = time() + (int) apply_filters( 'relod_wl_guest_cookie_ttl', 365 * DAY_IN_SECONDS );
		$path   = COOKIEPATH ? COOKIEPATH : '/';

		setcookie(
			RELOD_WL_COOKIE,
			$key,
			[
				'expires'  => $expire,
				'path'     => $path,
				'domain'   => COOKIE_DOMAIN,
				'secure'   => is_ssl(),
				'httponly' => true,
				'samesite' => 'Lax',
			]
		);

		$_COOKIE[ RELOD_WL_COOKIE ] = $key;
		self::$runtime_guest_key    = $key;

		return $key;
	}

	/**
	 * Идентификатор владельца списка для мемоизации и блокировок.
	 */
	private static function owner_token( ?int $user_id = null ): string {
		$uid = $user_id ?: ( is_user_logged_in() ? get_current_user_id() : 0 );
		if ( $uid ) {
			return 'u' . $uid;
		}
		$key = self::guest_key();
		return $key ? 'g' . $key : '';
	}

	/* ────────────────────────────────────────
	   Чтение / запись
	   ──────────────────────────────────────── */

	/**
	 * Сырой список из хранилища, без проверки товаров.
	 */
	public static function get_raw_ids( ?int $user_id = null ): array {
		$uid = $user_id ?: ( is_user_logged_in() ? get_current_user_id() : 0 );

		if ( $uid ) {
			$ids = get_user_meta( $uid, RELOD_WL_META, true );
			return self::normalize( is_array( $ids ) ? $ids : [] );
		}

		$key = self::guest_key();
		if ( '' === $key ) {
			return [];
		}

		return self::normalize( self::read_guest( $key ) );
	}

	/**
	 * Список, очищенный от товаров, которых больше нет в каталоге.
	 *
	 * Именно рассинхрон между count() сырого списка и тем, что способен
	 * отрисовать WooCommerce, давал «7 в счётчике, 5 на странице».
	 */
	public static function get_ids( ?int $user_id = null ): array {
		$token = $user_id ? 'u' . $user_id : self::owner_token();

		if ( '' !== $token && isset( self::$memo[ $token ] ) ) {
			return self::$memo[ $token ];
		}

		$ids = self::prune( self::get_raw_ids( $user_id ) );

		if ( '' !== $token ) {
			self::$memo[ $token ] = $ids;
		}

		return $ids;
	}

	public static function save_ids( array $ids, ?int $user_id = null ): bool {
		$ids = self::normalize( $ids );
		$uid = $user_id ?: ( is_user_logged_in() ? get_current_user_id() : 0 );

		if ( $uid ) {
			$current = self::get_raw_ids( $uid );
			if ( $current === $ids ) {
				self::forget( 'u' . $uid );
				return true;
			}
			// update_user_meta возвращает false и когда значение не изменилось,
			// поэтому равенство проверяем выше и здесь верим результату.
			$saved = (bool) update_user_meta( $uid, RELOD_WL_META, $ids );
			self::forget( 'u' . $uid );
			return $saved;
		}

		$key = self::guest_key( true );
		if ( '' === $key ) {
			return false;
		}

		$saved = self::write_guest( $key, $ids );
		self::forget( 'g' . $key );

		return $saved;
	}

	private static function forget( string $token ): void {
		if ( '' !== $token ) {
			unset( self::$memo[ $token ] );
		}
	}

	/* ────────────────────────────────────────
	   Атомарное изменение
	   ──────────────────────────────────────── */

	/**
	 * Добавляет / убирает товар под блокировкой.
	 *
	 * @param int    $product_id ID товара.
	 * @param string $op         add | remove | toggle.
	 *
	 * @return array{ok:bool,action:string,ids:array,count:int}
	 */
	public static function mutate( int $product_id, string $op = 'toggle' ): array {
		$token  = self::owner_token();
		$locked = self::lock( $token );

		try {
			// Читаем уже после захвата блокировки, иначе смысла в ней нет.
			self::forget( $token );
			$ids = self::prune( self::get_raw_ids() );

			$has = in_array( $product_id, $ids, true );

			if ( 'toggle' === $op ) {
				$op = $has ? 'remove' : 'add';
			}

			if ( 'add' === $op ) {
				if ( ! $has ) {
					$ids[] = $product_id;
				}
				$action = 'added';
			} else {
				if ( $has ) {
					$ids = array_values( array_diff( $ids, [ $product_id ] ) );
				}
				$action = 'removed';
			}

			$saved = self::save_ids( $ids );

			if ( ! $saved ) {
				return [
					'ok'     => false,
					'action' => $action,
					'ids'    => self::get_ids(),
					'count'  => count( self::get_ids() ),
				];
			}

			return [
				'ok'     => true,
				'action' => $action,
				'ids'    => $ids,
				'count'  => count( $ids ),
			];
		} finally {
			if ( $locked ) {
				self::unlock();
			}
		}
	}

	/* ────────────────────────────────────────
	   Блокировка
	   ──────────────────────────────────────── */

	private static function lock( string $token ): bool {
		if ( '' === $token ) {
			return false;
		}

		global $wpdb;

		$name = 'relod_wl_' . md5( DB_NAME . '|' . $token );

		$wpdb->suppress_errors( true );
		$got = $wpdb->get_var( $wpdb->prepare( 'SELECT GET_LOCK(%s, %d)', $name, 5 ) );
		$wpdb->suppress_errors( false );

		if ( '1' !== (string) $got ) {
			// GET_LOCK может быть недоступен (некоторые managed-хостинги).
			// Работаем без блокировки: это хуже, но не ломает функциональность.
			return false;
		}

		self::$held_lock = $name;

		return true;
	}

	private static function unlock(): void {
		if ( '' === self::$held_lock ) {
			return;
		}

		global $wpdb;

		$wpdb->suppress_errors( true );
		$wpdb->get_var( $wpdb->prepare( 'SELECT RELEASE_LOCK(%s)', self::$held_lock ) );
		$wpdb->suppress_errors( false );

		self::$held_lock = '';
	}

	/* ────────────────────────────────────────
	   Гостевое хранилище
	   ──────────────────────────────────────── */

	private static function read_guest( string $key ): array {
		if ( self::table_exists() ) {
			global $wpdb;

			$raw = $wpdb->get_var(
				$wpdb->prepare(
					'SELECT product_ids FROM ' . self::table() . ' WHERE guest_key = %s', // phpcs:ignore WordPress.DB.PreparedSQL
					$key
				)
			);

			if ( null !== $raw ) {
				$ids = json_decode( (string) $raw, true );
				return is_array( $ids ) ? $ids : [];
			}

			// Миграция со старого хранилища на транзиентах.
			$legacy = get_transient( 'relod_wl_' . $key );
			if ( is_array( $legacy ) && $legacy ) {
				self::write_guest( $key, self::normalize( $legacy ) );
				delete_transient( 'relod_wl_' . $key );
				return $legacy;
			}

			return [];
		}

		$ids = get_transient( 'relod_wl_' . $key );

		return is_array( $ids ) ? $ids : [];
	}

	private static function write_guest( string $key, array $ids ): bool {
		if ( self::table_exists() ) {
			global $wpdb;

			$result = $wpdb->query(
				$wpdb->prepare(
					'INSERT INTO ' . self::table() . ' (guest_key, product_ids, updated_at) VALUES (%s, %s, %s) ' // phpcs:ignore WordPress.DB.PreparedSQL
					. 'ON DUPLICATE KEY UPDATE product_ids = VALUES(product_ids), updated_at = VALUES(updated_at)',
					$key,
					(string) wp_json_encode( array_values( $ids ) ),
					gmdate( 'Y-m-d H:i:s' )
				)
			);

			return false !== $result;
		}

		return (bool) set_transient( 'relod_wl_' . $key, $ids, 365 * DAY_IN_SECONDS );
	}

	public static function delete_guest( string $key ): void {
		if ( '' === $key ) {
			return;
		}

		if ( self::table_exists() ) {
			global $wpdb;
			$wpdb->delete( self::table(), [ 'guest_key' => $key ], [ '%s' ] );
		}

		delete_transient( 'relod_wl_' . $key );
	}

	/* ────────────────────────────────────────
	   Утилиты
	   ──────────────────────────────────────── */

	public static function normalize( array $ids ): array {
		return array_values( array_unique( array_filter( array_map( 'absint', $ids ) ) ) );
	}

	/**
	 * Оставляет только реально существующие опубликованные товары,
	 * сохраняя порядок добавления.
	 */
	public static function prune( array $ids ): array {
		$ids = self::normalize( $ids );

		if ( ! $ids ) {
			return [];
		}

		$args = apply_filters(
			'relod_wl_prune_query_args',
			[
				'post_type'              => [ 'product', 'product_variation' ],
				'post_status'            => 'publish',
				'post__in'               => $ids,
				'posts_per_page'         => count( $ids ),
				'fields'                 => 'ids',
				'orderby'                => 'post__in',
				'no_found_rows'          => true,
				'ignore_sticky_posts'    => true,
				'update_post_meta_cache' => false,
				'update_post_term_cache' => false,
				'suppress_filters'       => false,
			],
			$ids
		);

		$valid = get_posts( $args );

		if ( ! is_array( $valid ) ) {
			return $ids;
		}

		$valid = array_map( 'absint', $valid );

		// array_intersect сохраняет порядок первого массива — порядок добавления.
		return array_values( array_intersect( $ids, $valid ) );
	}

	/**
	 * Слияние гостевого списка с пользовательским при входе.
	 */
	public static function merge_guest_into_user( int $user_id ): void {
		$key = self::guest_key();
		if ( '' === $key || ! $user_id ) {
			return;
		}

		$guest_ids = self::normalize( self::read_guest( $key ) );
		if ( ! $guest_ids ) {
			return;
		}

		$merged = self::normalize( array_merge( self::get_raw_ids( $user_id ), $guest_ids ) );

		self::save_ids( $merged, $user_id );
		self::delete_guest( $key );
	}
}
