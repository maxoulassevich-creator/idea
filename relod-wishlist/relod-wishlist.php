<?php
/**
 * Plugin Name: RELOD Wishlist
 * Plugin URI:  https://amaressence.ru
 * Description: Облегчённый вишлист для WooCommerce с AJAX-тогглом и шорткодом страницы избранного. Совместим с кешированием страниц (WP Rocket, LiteSpeed, WP Super Cache).
 * Version:     1.2.0
 * Author:      RELOD
 * Author URI:  https://amaressence.ru
 * Requires PHP: 7.4
 * Requires at least: 5.8
 * WC requires at least: 7.0
 * Text Domain: relod-wishlist
 * License:     GPL-2.0+
 */

defined( 'ABSPATH' ) || exit;

/* ────────────────────────────────────────────
   Constants
   ──────────────────────────────────────────── */
define( 'RELOD_WL_VERSION', '1.2.0' );
define( 'RELOD_WL_FILE', __FILE__ );
define( 'RELOD_WL_PATH', plugin_dir_path( __FILE__ ) );
define( 'RELOD_WL_URL', plugin_dir_url( __FILE__ ) );
define( 'RELOD_WL_COOKIE', 'relod_wl_guest' );
define( 'RELOD_WL_META', '_relod_wishlist' );
define( 'RELOD_WL_VERSION_OPTION', 'relod_wl_plugin_version' );

require_once RELOD_WL_PATH . 'includes/class-relod-wl-store.php';
require_once RELOD_WL_PATH . 'includes/class-relod-wl-cache.php';
require_once RELOD_WL_PATH . 'includes/class-relod-wl-ajax.php';
require_once RELOD_WL_PATH . 'includes/class-relod-wl.php';

/* ────────────────────────────────────────────
   Активация / деактивация
   ──────────────────────────────────────────── */
register_activation_hook(
	__FILE__,
	static function () {
		Relod_WL_Store::on_activate();
		Relod_WL_Cache::reindex();
		update_option( RELOD_WL_VERSION_OPTION, RELOD_WL_VERSION, true );

		/**
		 * Старые страницы в кеше содержат счётчик и активные сердечки того,
		 * кто первым их открыл. Их надо выкинуть, иначе после обновления
		 * рассинхрон продолжится ещё сутки.
		 */
		Relod_WL_Cache::instance()->purge();
	}
);

register_deactivation_hook( __FILE__, [ 'Relod_WL_Store', 'on_deactivate' ] );

/* Ежедневная уборка заброшенных гостевых списков. */
add_action( Relod_WL_Store::GC_HOOK, [ 'Relod_WL_Store', 'gc' ] );

/* ────────────────────────────────────────────
   Bootstrap
   ──────────────────────────────────────────── */
add_action(
	'plugins_loaded',
	static function () {
		/* Хранилище и слой кеширования нужны и без WooCommerce:
		   на них опираются плагины-сателлиты. */
		Relod_WL_Store::maybe_upgrade();
		Relod_WL_Cache::instance();

		/**
		 * Обновление плагина без деактивации.
		 *
		 * Переиндексацию и сброс кеша откладываем до wp_loaded: на
		 * plugins_loaded функций WP Rocket может ещё не быть, да и лишние
		 * запросы к БД на раннем хуке ни к чему.
		 */
		if ( get_option( RELOD_WL_VERSION_OPTION ) !== RELOD_WL_VERSION ) {
			update_option( RELOD_WL_VERSION_OPTION, RELOD_WL_VERSION, true );

			add_action(
				'wp_loaded',
				static function () {
					Relod_WL_Cache::reindex();
					Relod_WL_Cache::instance()->purge();
				}
			);
		}

		if ( ! class_exists( 'WooCommerce' ) ) {
			return;
		}

		Relod_Wishlist::instance();
	},
	20
);
