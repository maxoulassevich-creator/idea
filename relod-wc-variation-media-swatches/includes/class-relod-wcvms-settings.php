<?php
namespace RELOD\WCVMS;

if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

class Settings {
    public const SECTION = 'relod_variation_media_swatches';

    public const OPTION_ENABLED              = 'relod_wcvms_enabled';
    public const OPTION_AUTO_REPLACE_GALLERY = 'relod_wcvms_auto_replace_gallery';
    public const OPTION_AUTO_RENDER_SWATCHES = 'relod_wcvms_auto_render_swatches';
    public const OPTION_OPEN_TARGET          = 'relod_wcvms_open_target';
    public const OPTION_HISTORY_MODE         = 'relod_wcvms_history_mode';
    public const OPTION_VIDEO_FIT            = 'relod_wcvms_video_fit';
    public const OPTION_STAGE_RATIO          = 'relod_wcvms_stage_ratio';
    public const OPTION_COLOR_KEYWORDS       = 'relod_wcvms_color_keywords';
    public const OPTION_SIZE_KEYWORDS        = 'relod_wcvms_size_keywords';
    public const OPTION_SINGLE_SWATCH_SIZE   = 'relod_wcvms_single_swatch_size';
    public const OPTION_GRID_SWATCH_SIZE     = 'relod_wcvms_grid_swatch_size';

    public function __construct() {
        add_filter( 'woocommerce_get_sections_products', [ $this, 'add_section' ] );
        add_filter( 'woocommerce_get_settings_products', [ $this, 'get_settings' ], 10, 2 );
        add_action( 'woocommerce_settings_save_products', [ $this, 'save_settings' ] );
    }

    public function add_section( array $sections ): array {
        $sections[ self::SECTION ] = __( 'Variation swatches & media', 'relod-wc-variation-media-swatches' );
        return $sections;
    }

    public function get_settings( array $settings, string $current_section ): array {
        if ( self::SECTION !== $current_section ) {
            return $settings;
        }

        return [
            [
                'title' => __( 'RELOD Variation Media Swatches', 'relod-wc-variation-media-swatches' ),
                'type'  => 'title',
                'id'    => 'relod_wcvms_settings_title',
                'desc'  => __( 'Настройки swatches, галерей изображений/видео и поведения на странице товара.', 'relod-wc-variation-media-swatches' ),
            ],
            [
                'title'   => __( 'Включить плагин', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_ENABLED,
                'type'    => 'checkbox',
                'default' => 'yes',
            ],
            [
                'title'   => __( 'Автоматически заменять стандартную галерею WooCommerce', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_AUTO_REPLACE_GALLERY,
                'type'    => 'checkbox',
                'default' => 'no',
                'desc'    => __( 'Если включено, на странице товара стандартная галерея WooCommerce будет заменена на галерею плагина, поддерживающую изображения и видео вариаций.', 'relod-wc-variation-media-swatches' ),
            ],
            [
                'title'   => __( 'Автоматически выводить swatches под формой вариаций', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_AUTO_RENDER_SWATCHES,
                'type'    => 'checkbox',
                'default' => 'no',
            ],
            [
                'title'   => __( 'Открытие swatches в карточках товара', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_OPEN_TARGET,
                'type'    => 'select',
                'default' => '_blank',
                'options' => [
                    '_blank' => __( 'В новой вкладке', 'relod-wc-variation-media-swatches' ),
                    '_self'  => __( 'В текущей вкладке', 'relod-wc-variation-media-swatches' ),
                ],
            ],
            [
                'title'   => __( 'Обновление URL на странице товара', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_HISTORY_MODE,
                'type'    => 'select',
                'default' => 'replace',
                'options' => [
                    'replace' => __( 'Менять URL без перезагрузки', 'relod-wc-variation-media-swatches' ),
                    'none'    => __( 'Не менять URL', 'relod-wc-variation-media-swatches' ),
                ],
            ],
            [
                'title'    => __( 'Ключевые слова для автоопределения цветового атрибута', 'relod-wc-variation-media-swatches' ),
                'id'       => self::OPTION_COLOR_KEYWORDS,
                'type'     => 'text',
                'default'  => 'color,colour,цвет,цвета',
                'desc'     => __( 'Через запятую. Используется, когда для товара явно не выбран атрибут цвета.', 'relod-wc-variation-media-swatches' ),
                'desc_tip' => true,
            ],
            [
                'title'    => __( 'Ключевые слова для автоопределения атрибута размера', 'relod-wc-variation-media-swatches' ),
                'id'       => self::OPTION_SIZE_KEYWORDS,
                'type'     => 'text',
                'default'  => 'size,размер,размеры',
                'desc'     => __( 'Через запятую. Используется, когда для товара явно не выбран атрибут размера.', 'relod-wc-variation-media-swatches' ),
                'desc_tip' => true,
            ],
            [
                'title'   => __( 'Размер swatches на странице товара', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_SINGLE_SWATCH_SIZE,
                'type'    => 'number',
                'default' => '22',
                'css'     => 'width:80px;',
                'custom_attributes' => [
                    'min'  => '12',
                    'max'  => '60',
                    'step' => '1',
                ],
            ],
            [
                'title'   => __( 'Размер swatches в карточках товара', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_GRID_SWATCH_SIZE,
                'type'    => 'number',
                'default' => '14',
                'css'     => 'width:80px;',
                'custom_attributes' => [
                    'min'  => '8',
                    'max'  => '40',
                    'step' => '1',
                ],
            ],
            [
                'title'   => __( 'Режим показа видео', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_VIDEO_FIT,
                'type'    => 'select',
                'default' => 'cover',
                'options' => [
                    'cover'   => __( 'Заполнить без полос', 'relod-wc-variation-media-swatches' ),
                    'contain' => __( 'Показать целиком', 'relod-wc-variation-media-swatches' ),
                    'soft'    => __( 'Показать целиком с мягким фоном', 'relod-wc-variation-media-swatches' ),
                ],
            ],
            [
                'title'   => __( 'Пропорция основного медиаблока', 'relod-wc-variation-media-swatches' ),
                'id'      => self::OPTION_STAGE_RATIO,
                'type'    => 'select',
                'default' => '4 / 5',
                'options' => [
                    '1 / 1'  => '1 / 1',
                    '5 / 4'  => '5 / 4',
                    '4 / 5'  => '4 / 5',
                    '3 / 4'  => '3 / 4',
                    '2 / 3'  => '2 / 3',
                    '9 / 14' => '9 / 14',
                    '9 / 16' => '9 / 16',
                    '16 / 9' => '16 / 9',
                ],
            ],
            [
                'type' => 'sectionend',
                'id'   => 'relod_wcvms_settings_title',
            ],
        ];
    }

    public function save_settings(): void {
        global $current_section;

        if ( self::SECTION !== $current_section ) {
            return;
        }

        woocommerce_update_options( $this->get_settings( [], self::SECTION ) );
    }

    public function is_enabled(): bool {
        return 'yes' === $this->get_option( self::OPTION_ENABLED, 'yes' );
    }

    public function auto_replace_gallery(): bool {
        return 'yes' === $this->get_option( self::OPTION_AUTO_REPLACE_GALLERY, 'no' );
    }

    public function auto_render_swatches(): bool {
        return 'yes' === $this->get_option( self::OPTION_AUTO_RENDER_SWATCHES, 'no' );
    }

    public function get_open_target(): string {
        $value = (string) $this->get_option( self::OPTION_OPEN_TARGET, '_blank' );
        return in_array( $value, [ '_blank', '_self' ], true ) ? $value : '_blank';
    }

    public function get_history_mode(): string {
        $value = (string) $this->get_option( self::OPTION_HISTORY_MODE, 'replace' );
        return in_array( $value, [ 'replace', 'none' ], true ) ? $value : 'replace';
    }

    public function get_option( string $key, $default = '' ) {
        return get_option( $key, $default );
    }

    public function get_video_fit(): string {
        return $this->sanitize_video_fit( (string) $this->get_option( self::OPTION_VIDEO_FIT, 'cover' ) );
    }

    public function sanitize_video_fit( string $value ): string {
        $value = strtolower( trim( $value ) );
        return in_array( $value, [ 'cover', 'contain', 'soft' ], true ) ? $value : 'cover';
    }

    public function get_stage_ratio(): string {
        $value   = (string) $this->get_option( self::OPTION_STAGE_RATIO, '4 / 5' );
        $allowed = [ '1 / 1', '5 / 4', '4 / 5', '3 / 4', '2 / 3', '9 / 14', '9 / 16', '16 / 9' ];
        return in_array( $value, $allowed, true ) ? $value : '4 / 5';
    }

    public function get_color_keywords(): array {
        $raw = (string) $this->get_option( self::OPTION_COLOR_KEYWORDS, 'color,colour,цвет,цвета' );
        $parts = array_filter( array_map( 'trim', explode( ',', mb_strtolower( $raw ) ) ) );
        return array_values( array_unique( $parts ) );
    }

    public function get_size_keywords(): array {
        $raw = (string) $this->get_option( self::OPTION_SIZE_KEYWORDS, 'size,размер,размеры' );
        $parts = array_filter( array_map( 'trim', explode( ',', mb_strtolower( $raw ) ) ) );
        return array_values( array_unique( $parts ) );
    }

    public function get_single_swatch_size(): int {
        $value = absint( $this->get_option( self::OPTION_SINGLE_SWATCH_SIZE, 22 ) );
        return $value > 0 ? $value : 22;
    }

    public function get_grid_swatch_size(): int {
        $value = absint( $this->get_option( self::OPTION_GRID_SWATCH_SIZE, 14 ) );
        return $value > 0 ? $value : 14;
    }
}
