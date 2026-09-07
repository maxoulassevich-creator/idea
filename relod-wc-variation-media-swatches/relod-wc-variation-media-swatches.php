<?php
/**
 * Plugin Name: RELOD WC Variation Media Swatches
 * Description: Цветовые swatches для вариаций WooCommerce, галереи изображений/видео для вариаций, шорткоды для страницы товара и интеграция с Woo Product Grid (Unlimited Elements).
 * Version: 1.4.4
 * Author: OpenAI for RELOD
 * Text Domain: relod-wc-variation-media-swatches
 * Requires at least: 6.4
 * Requires PHP: 7.4
 * WC requires at least: 8.5
 * WC tested up to: 10.1
 */

if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

define( 'RELOD_WCVMS_VERSION', '1.4.4' );
define( 'RELOD_WCVMS_FILE', __FILE__ );
define( 'RELOD_WCVMS_PATH', plugin_dir_path( __FILE__ ) );
define( 'RELOD_WCVMS_URL', plugin_dir_url( __FILE__ ) );

require_once RELOD_WCVMS_PATH . 'includes/class-relod-wcvms-plugin.php';

add_action(
    'plugins_loaded',
    static function() {
        load_plugin_textdomain( 'relod-wc-variation-media-swatches', false, dirname( plugin_basename( RELOD_WCVMS_FILE ) ) . '/languages' );

        if ( ! class_exists( 'WooCommerce' ) ) {
            add_action(
                'admin_notices',
                static function() {
                    if ( ! current_user_can( 'activate_plugins' ) ) {
                        return;
                    }

                    echo '<div class="notice notice-error"><p>' . esc_html__( 'Плагин RELOD WC Variation Media Swatches работает только при активном WooCommerce.', 'relod-wc-variation-media-swatches' ) . '</p></div>';
                }
            );
            return;
        }

        \RELOD\WCVMS\Plugin::instance();
    }
);
