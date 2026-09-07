<?php
namespace RELOD\WCVMS;

if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

require_once RELOD_WCVMS_PATH . 'includes/class-relod-wcvms-settings.php';
require_once RELOD_WCVMS_PATH . 'includes/class-relod-wcvms-meta.php';
require_once RELOD_WCVMS_PATH . 'includes/class-relod-wcvms-admin.php';
require_once RELOD_WCVMS_PATH . 'includes/class-relod-wcvms-frontend.php';

class Plugin {
    /** @var Plugin|null */
    private static $instance = null;

    /** @var Settings */
    public $settings;

    /** @var Meta */
    public $meta;

    /** @var Admin */
    public $admin;

    /** @var Frontend */
    public $frontend;

    public static function instance(): self {
        if ( null === self::$instance ) {
            self::$instance = new self();
        }

        return self::$instance;
    }

    private function __construct() {
        $this->settings = new Settings();
        $this->meta     = new Meta( $this->settings );
        $this->admin    = new Admin( $this->meta, $this->settings );
        $this->frontend = new Frontend( $this->meta, $this->settings );
    }
}
