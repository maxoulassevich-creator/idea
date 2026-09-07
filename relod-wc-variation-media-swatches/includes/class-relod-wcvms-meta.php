<?php
namespace RELOD\WCVMS;

if ( ! defined( 'ABSPATH' ) ) {
    exit;
}

class Meta {
    public const PRODUCT_COLOR_ATTRIBUTE = '_relod_wcvms_color_attribute';
    public const PRODUCT_SIZE_ATTRIBUTE  = '_relod_wcvms_size_attribute';
    public const PRODUCT_MAIN_VIDEO      = '_relod_wcvms_main_video_id';
    public const PRODUCT_GALLERY_VIDEOS  = '_relod_wcvms_gallery_video_ids';

    public const TERM_COLOR = '_relod_wcvms_term_color';

    public const VAR_COLOR            = '_relod_wcvms_color';
    public const VAR_GALLERY_IMAGES   = '_relod_wcvms_gallery_image_ids';
    public const VAR_GALLERY_VIDEOS   = '_relod_wcvms_gallery_video_ids';
    public const VAR_LONG_DESCRIPTION = '_relod_wcvms_long_description';
    public const VAR_SWATCH_LABEL     = '_relod_wcvms_swatch_label';
    public const VAR_SIZE_LABEL       = '_relod_wcvms_size_label';
    public const VAR_HIDE_IN_SWATCHES = '_relod_wcvms_hide_in_swatches';

    /** @var Settings */
    private $settings;

    public function __construct( Settings $settings ) {
        $this->settings = $settings;
        add_action( 'init', [ $this, 'register_meta' ] );
    }

    public function register_meta(): void {
        register_post_meta(
            'product',
            self::PRODUCT_COLOR_ATTRIBUTE,
            [
                'type'              => 'string',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_product_attribute_key' ],
                'show_in_rest'      => true,
                'auth_callback'     => [ $this, 'auth_callback' ],
            ]
        );

        register_post_meta(
            'product',
            self::PRODUCT_SIZE_ATTRIBUTE,
            [
                'type'              => 'string',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_product_attribute_key' ],
                'show_in_rest'      => true,
                'auth_callback'     => [ $this, 'auth_callback' ],
            ]
        );

        register_post_meta(
            'product',
            self::PRODUCT_MAIN_VIDEO,
            [
                'type'              => 'integer',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_video_id' ],
                'show_in_rest'      => true,
                'auth_callback'     => [ $this, 'auth_callback' ],
            ]
        );

        register_post_meta(
            'product',
            self::PRODUCT_GALLERY_VIDEOS,
            [
                'type'              => 'array',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_video_ids' ],
                'show_in_rest'      => [
                    'schema' => [
                        'type'  => 'array',
                        'items' => [ 'type' => 'integer' ],
                    ],
                ],
                'auth_callback'     => [ $this, 'auth_callback' ],
            ]
        );

        register_term_meta(
            '',
            self::TERM_COLOR,
            [
                'type'              => 'string',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_hex_color' ],
                'show_in_rest'      => true,
                'auth_callback'     => [ $this, 'term_auth_callback' ],
            ]
        );

        foreach ( [ self::VAR_COLOR, self::VAR_LONG_DESCRIPTION, self::VAR_SWATCH_LABEL, self::VAR_SIZE_LABEL ] as $key ) {
            register_post_meta(
                'product_variation',
                $key,
                [
                    'type'              => 'string',
                    'single'            => true,
                    'sanitize_callback' => [ $this, 'sanitize_string_meta' ],
                    'show_in_rest'      => true,
                    'auth_callback'     => [ $this, 'auth_callback' ],
                ]
            );
        }

        register_post_meta(
            'product_variation',
            self::VAR_HIDE_IN_SWATCHES,
            [
                'type'              => 'string',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_yes_no' ],
                'show_in_rest'      => true,
                'auth_callback'     => [ $this, 'auth_callback' ],
            ]
        );

        register_post_meta(
            'product_variation',
            self::VAR_GALLERY_IMAGES,
            [
                'type'              => 'array',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_image_ids' ],
                'show_in_rest'      => [
                    'schema' => [
                        'type'  => 'array',
                        'items' => [ 'type' => 'integer' ],
                    ],
                ],
                'auth_callback'     => [ $this, 'auth_callback' ],
            ]
        );

        register_post_meta(
            'product_variation',
            self::VAR_GALLERY_VIDEOS,
            [
                'type'              => 'array',
                'single'            => true,
                'sanitize_callback' => [ $this, 'sanitize_video_ids' ],
                'show_in_rest'      => [
                    'schema' => [
                        'type'  => 'array',
                        'items' => [ 'type' => 'integer' ],
                    ],
                ],
                'auth_callback'     => [ $this, 'auth_callback' ],
            ]
        );
    }

    public function auth_callback( $allowed, $meta_key, $post_id ): bool {
        return current_user_can( 'edit_post', $post_id );
    }

    public function term_auth_callback( ...$args ): bool {
        return current_user_can( 'manage_product_terms' ) || current_user_can( 'manage_woocommerce' );
    }

    public function sanitize_product_attribute_key( $value ): string {
        $value = wc_clean( wp_unslash( (string) $value ) );
        return preg_replace( '/[^a-zA-Z0-9_\-]/', '', $value );
    }

    public function sanitize_string_meta( $value ): string {
        return trim( wp_kses_post( wp_unslash( (string) $value ) ) );
    }

    public function sanitize_yes_no( $value ): string {
        return 'yes' === (string) $value ? 'yes' : 'no';
    }

    public function sanitize_hex_color( $value ): string {
        $value = sanitize_hex_color( (string) $value );
        return $value ? $value : '';
    }

    public function sanitize_video_id( $value ): int {
        $value = absint( $value );
        if ( $value < 1 ) {
            return 0;
        }

        $mime = (string) get_post_mime_type( $value );
        return 0 === strpos( $mime, 'video/' ) ? $value : 0;
    }

    public function sanitize_image_id( $value ): int {
        $value = absint( $value );
        if ( $value < 1 ) {
            return 0;
        }

        $mime = (string) get_post_mime_type( $value );
        return 0 === strpos( $mime, 'image/' ) ? $value : 0;
    }

    public function sanitize_video_ids( $value ): array {
        if ( ! is_array( $value ) ) {
            return [];
        }

        $clean = [];
        foreach ( $value as $video_id ) {
            $video_id = $this->sanitize_video_id( $video_id );
            if ( $video_id > 0 ) {
                $clean[] = $video_id;
            }
        }

        return array_values( array_unique( $clean ) );
    }

    public function sanitize_image_ids( $value ): array {
        if ( ! is_array( $value ) ) {
            return [];
        }

        $clean = [];
        foreach ( $value as $image_id ) {
            $image_id = $this->sanitize_image_id( $image_id );
            if ( $image_id > 0 ) {
                $clean[] = $image_id;
            }
        }

        return array_values( array_unique( $clean ) );
    }

    public function get_product_color_attribute( int $product_id ): string {
        return $this->sanitize_product_attribute_key( (string) get_post_meta( $product_id, self::PRODUCT_COLOR_ATTRIBUTE, true ) );
    }

    public function get_product_size_attribute( int $product_id ): string {
        return $this->sanitize_product_attribute_key( (string) get_post_meta( $product_id, self::PRODUCT_SIZE_ATTRIBUTE, true ) );
    }

    public function get_product_main_video_id( int $product_id ): int {
        $current = $this->sanitize_video_id( (int) get_post_meta( $product_id, self::PRODUCT_MAIN_VIDEO, true ) );
        if ( $current > 0 ) {
            return $current;
        }

        $legacy = $this->sanitize_video_id( (int) get_post_meta( $product_id, '_relod_wcpv_main_video_id', true ) );
        if ( $legacy > 0 ) {
            return $legacy;
        }

        $legacy_rows = get_post_meta( $product_id, '_relod_wc_product_videos', true );
        if ( is_array( $legacy_rows ) ) {
            foreach ( $legacy_rows as $row ) {
                if ( empty( $row['enabled'] ) || empty( $row['attachment_id'] ) ) {
                    continue;
                }
                if ( ! in_array( $row['placement'] ?? '', [ 'main', 'both' ], true ) ) {
                    continue;
                }
                $legacy = $this->sanitize_video_id( (int) $row['attachment_id'] );
                if ( $legacy > 0 ) {
                    return $legacy;
                }
            }
        }

        return 0;
    }

    public function get_product_gallery_video_ids( int $product_id ): array {
        $raw = get_post_meta( $product_id, self::PRODUCT_GALLERY_VIDEOS, true );
        if ( is_array( $raw ) && ! empty( $raw ) ) {
            return $this->sanitize_video_ids( $raw );
        }

        $legacy = get_post_meta( $product_id, '_relod_wcpv_gallery_video_ids', true );
        if ( is_array( $legacy ) && ! empty( $legacy ) ) {
            return $this->sanitize_video_ids( $legacy );
        }

        $legacy_rows = get_post_meta( $product_id, '_relod_wc_product_videos', true );
        $legacy_ids  = [];
        if ( is_array( $legacy_rows ) ) {
            foreach ( $legacy_rows as $row ) {
                if ( empty( $row['enabled'] ) || empty( $row['attachment_id'] ) ) {
                    continue;
                }
                if ( ! in_array( $row['placement'] ?? '', [ 'gallery', 'both' ], true ) ) {
                    continue;
                }
                $legacy_ids[] = (int) $row['attachment_id'];
            }
        }

        return $this->sanitize_video_ids( $legacy_ids );
    }

    public function get_term_color( int $term_id ): string {
        return $this->sanitize_hex_color( (string) get_term_meta( $term_id, self::TERM_COLOR, true ) );
    }

    public function get_variation_color( int $variation_id ): string {
        $manual = $this->sanitize_hex_color( (string) get_post_meta( $variation_id, self::VAR_COLOR, true ) );
        if ( '' !== $manual ) {
            return $manual;
        }

        return $this->get_variation_attribute_term_color( $variation_id );
    }

    public function get_variation_attribute_term_color( int $variation_id ): string {
        $variation = wc_get_product( $variation_id );
        if ( ! $variation instanceof \WC_Product_Variation ) {
            return '';
        }

        $parent_id = (int) $variation->get_parent_id();
        $parent    = $parent_id > 0 ? wc_get_product( $parent_id ) : null;
        $color_attribute_key = $parent instanceof \WC_Product
            ? $this->get_product_color_attribute_auto( $parent )
            : '';

        if ( '' !== $color_attribute_key ) {
            $value = $this->get_variation_attribute_value( $variation, $color_attribute_key );
            $color = $this->get_attribute_term_color_by_value( $color_attribute_key, $value );
            if ( '' !== $color ) {
                return $color;
            }
        }

        foreach ( $variation->get_variation_attributes() as $raw_attribute_key => $value ) {
            $attribute_key = preg_replace( '/^attribute_/', '', (string) $raw_attribute_key );
            if ( '' === $attribute_key || '' === (string) $value ) {
                continue;
            }

            $color = $this->get_attribute_term_color_by_value( $attribute_key, (string) $value );
            if ( '' !== $color ) {
                return $color;
            }
        }

        return '';
    }

    public function get_attribute_term_color_by_value( string $attribute_key, string $value ): string {
        $attribute_key = $this->sanitize_product_attribute_key( $attribute_key );
        $value         = wc_clean( rawurldecode( (string) $value ) );

        if ( '' === $attribute_key || '' === $value || ! taxonomy_exists( $attribute_key ) ) {
            return '';
        }

        $term = get_term_by( 'slug', $value, $attribute_key );
        if ( ! $term || is_wp_error( $term ) ) {
            $term = get_term_by( 'name', $value, $attribute_key );
        }
        if ( ! $term || is_wp_error( $term ) ) {
            return '';
        }

        return $this->get_term_color( (int) $term->term_id );
    }

    private function get_variation_attribute_value( \WC_Product_Variation $variation, string $attribute_key ): string {
        $variation_key = 'attribute_' . $this->sanitize_product_attribute_key( $attribute_key );
        $attributes    = $variation->get_variation_attributes();

        return isset( $attributes[ $variation_key ] ) ? (string) $attributes[ $variation_key ] : '';
    }

    public function get_variation_gallery_image_ids( int $variation_id ): array {
        $raw = get_post_meta( $variation_id, self::VAR_GALLERY_IMAGES, true );
        return is_array( $raw ) ? $this->sanitize_image_ids( $raw ) : [];
    }

    public function get_variation_gallery_video_ids( int $variation_id ): array {
        $raw = get_post_meta( $variation_id, self::VAR_GALLERY_VIDEOS, true );
        return is_array( $raw ) ? $this->sanitize_video_ids( $raw ) : [];
    }

    public function get_variation_long_description( int $variation_id ): string {
        return (string) get_post_meta( $variation_id, self::VAR_LONG_DESCRIPTION, true );
    }

    public function get_variation_swatch_label( int $variation_id ): string {
        return trim( (string) get_post_meta( $variation_id, self::VAR_SWATCH_LABEL, true ) );
    }

    public function get_variation_size_label( int $variation_id ): string {
        return trim( (string) get_post_meta( $variation_id, self::VAR_SIZE_LABEL, true ) );
    }

    public function is_variation_hidden_in_swatches( int $variation_id ): bool {
        return 'yes' === (string) get_post_meta( $variation_id, self::VAR_HIDE_IN_SWATCHES, true );
    }

    public function get_product_color_attribute_auto( \WC_Product $product ): string {
        return $this->detect_product_attribute_auto(
            $product,
            $this->get_product_color_attribute( $product->get_id() ),
            $this->settings->get_color_keywords()
        );
    }

    public function get_product_size_attribute_auto( \WC_Product $product ): string {
        $detected = $this->detect_product_attribute_auto(
            $product,
            $this->get_product_size_attribute( $product->get_id() ),
            $this->settings->get_size_keywords()
        );

        if ( '' !== $detected ) {
            return $detected;
        }

        return $this->detect_first_non_color_variation_attribute( $product );
    }

    private function detect_first_non_color_variation_attribute( \WC_Product $product ): string {
        $color_attribute = $this->get_product_color_attribute_auto( $product );

        foreach ( $product->get_attributes() as $attribute_key => $attribute ) {
            if ( ! $attribute instanceof \WC_Product_Attribute || ! $attribute->get_variation() ) {
                continue;
            }

            $attribute_key = $this->sanitize_product_attribute_key( (string) $attribute_key );
            if ( '' === $attribute_key || $attribute_key === $color_attribute ) {
                continue;
            }

            return $attribute_key;
        }

        return '';
    }

    private function detect_product_attribute_auto( \WC_Product $product, string $explicit, array $keywords ): string {
        $attributes = $product->get_attributes();

        if ( '' !== $explicit ) {
            $explicit = $this->sanitize_product_attribute_key( $explicit );
            if (
                isset( $attributes[ $explicit ] )
                && $attributes[ $explicit ] instanceof \WC_Product_Attribute
                && $attributes[ $explicit ]->get_variation()
            ) {
                return $explicit;
            }
        }

        if ( empty( $keywords ) ) {
            return '';
        }

        foreach ( $attributes as $attribute_key => $attribute ) {
            if ( ! $attribute instanceof \WC_Product_Attribute || ! $attribute->get_variation() ) {
                continue;
            }

            $candidates = [
                mb_strtolower( (string) $attribute_key ),
                mb_strtolower( (string) wc_attribute_label( $attribute_key ) ),
            ];

            foreach ( $keywords as $keyword ) {
                foreach ( $candidates as $candidate ) {
                    if ( '' !== $keyword && false !== mb_strpos( $candidate, $keyword ) ) {
                        return $this->sanitize_product_attribute_key( (string) $attribute_key );
                    }
                }
            }
        }

        return '';
    }

    public function build_variation_url( \WC_Product_Variation $variation ): string {
        $url        = $variation->get_permalink();
        $attributes = $variation->get_variation_attributes();

        if ( empty( $attributes ) ) {
            return $url;
        }

        $args = [];
        foreach ( $attributes as $key => $value ) {
            if ( '' === (string) $value ) {
                continue;
            }
            $args[ $key ] = $value;
        }

        return ! empty( $args ) ? add_query_arg( $args, $variation->get_parent_id() ? get_permalink( $variation->get_parent_id() ) : $url ) : $url;
    }

    public function get_variation_display_label( \WC_Product_Variation $variation, string $color_attribute_key = '' ): string {
        $custom = $this->get_variation_swatch_label( $variation->get_id() );
        if ( '' !== $custom ) {
            return $custom;
        }

        $attributes = $variation->get_variation_attributes();
        if ( '' !== $color_attribute_key ) {
            $variation_key = 'attribute_' . $color_attribute_key;
            if ( isset( $attributes[ $variation_key ] ) && '' !== (string) $attributes[ $variation_key ] ) {
                return $this->humanize_attribute_value( $color_attribute_key, (string) $attributes[ $variation_key ] );
            }
        }

        $parts = [];
        foreach ( $attributes as $attribute_key => $value ) {
            if ( '' === (string) $value ) {
                continue;
            }
            $clean_key = preg_replace( '/^attribute_/', '', (string) $attribute_key );
            $parts[]   = $this->humanize_attribute_value( $clean_key, (string) $value );
        }

        return implode( ' / ', $parts );
    }

    public function humanize_attribute_value( string $attribute_key, string $value ): string {
        $attribute_key = $this->sanitize_product_attribute_key( $attribute_key );

        if ( taxonomy_exists( $attribute_key ) ) {
            $term = get_term_by( 'slug', $value, $attribute_key );
            if ( $term && ! is_wp_error( $term ) ) {
                return (string) $term->name;
            }
        }

        return wc_clean( rawurldecode( $value ) );
    }
}
