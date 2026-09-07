<?php
namespace RELOD\WCVMS;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

class Frontend {
	/** @var Meta */
	private $meta;

	/** @var Settings */
	private $settings;

	public function __construct( Meta $meta, Settings $settings ) {
		$this->meta     = $meta;
		$this->settings = $settings;

		add_action( 'wp', [ $this, 'setup_single_product_hooks' ] );
		add_action( 'wp_enqueue_scripts', [ $this, 'register_assets' ] );

		// Existing shortcodes
		add_shortcode( 'relod_variation_swatches',    [ $this, 'shortcode_swatches' ] );
		add_shortcode( 'relod_product_variation_swatches', [ $this, 'shortcode_swatches' ] );
		add_shortcode( 'relod_variation_size_swatches', [ $this, 'shortcode_size_swatches' ] );
		add_shortcode( 'relod_product_variation_sizes', [ $this, 'shortcode_size_swatches' ] );
		add_shortcode( 'relod_product_media_gallery', [ $this, 'shortcode_gallery' ] );
		add_shortcode( 'relod_wc_product_gallery',    [ $this, 'shortcode_gallery' ] );

		// NEW: main image shortcode (single image, fills section)
		add_shortcode( 'relod_main_image',           [ $this, 'shortcode_main_image' ] );
		add_shortcode( 'relod_variation_main_image', [ $this, 'shortcode_main_image' ] );

		// NEW: slider shortcode (dots overlay, fills section)
		add_shortcode( 'relod_gallery_slider',           [ $this, 'shortcode_slider' ] );
		add_shortcode( 'relod_variation_gallery_slider', [ $this, 'shortcode_slider' ] );

		add_filter( 'woocommerce_available_variation', [ $this, 'extend_available_variation_data' ], 10, 3 );
		add_filter( 'woocommerce_hide_invisible_variations', [ $this, 'keep_out_of_stock_variations_visible' ], 10, 3 );

		add_action( 'wp_ajax_relod_wcvms_variation_payload',        [ $this, 'ajax_variation_payload' ] );
		add_action( 'wp_ajax_nopriv_relod_wcvms_variation_payload', [ $this, 'ajax_variation_payload' ] );

		add_action( 'relod_wc_vms_render_ue_swatches', [ $this, 'output_widget_swatches' ], 10, 5 );
	}

	// -------------------------------------------------------------------------
	// Assets
	// -------------------------------------------------------------------------

	public function register_assets(): void {
		wp_register_style(
			'relod-wcvms-frontend',
			RELOD_WCVMS_URL . 'assets/css/frontend.css',
			[],
			RELOD_WCVMS_VERSION
		);

		wp_register_script(
			'relod-wcvms-frontend',
			RELOD_WCVMS_URL . 'assets/js/frontend.js',
			[ 'jquery', 'wc-add-to-cart-variation' ],
			RELOD_WCVMS_VERSION,
			true
		);
	}

	// -------------------------------------------------------------------------
	// Single product auto-hooks
	// -------------------------------------------------------------------------

	public function setup_single_product_hooks(): void {
		if ( ! $this->settings->is_enabled() ) {
			return;
		}

		if ( ! function_exists( 'is_product' ) || ! is_product() ) {
			return;
		}

		if ( $this->settings->auto_replace_gallery() ) {
			remove_action( 'woocommerce_before_single_product_summary', 'woocommerce_show_product_images', 20 );
			add_action( 'woocommerce_before_single_product_summary', [ $this, 'render_gallery' ], 20 );
		}

		if ( $this->settings->auto_render_swatches() ) {
			add_action( 'woocommerce_before_add_to_cart_form', [ $this, 'render_auto_swatches' ], 5 );
		}
	}

	public function render_gallery(): void {
		$product_id = get_the_ID();
		if ( $product_id < 1 ) {
			return;
		}
		echo $this->get_gallery_html( $product_id, 0, 'relod-wcvms-gallery-wrap', '', '' ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped
	}

	public function render_auto_swatches(): void {
		echo $this->shortcode_swatches( [] ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped
	}

	// -------------------------------------------------------------------------
	// Shortcode: existing gallery (thumbnails strip)
	// -------------------------------------------------------------------------

	public function shortcode_gallery( $atts ): string {
		$atts = shortcode_atts(
			[
				'id'           => 0,
				'variation_id' => 0,
				'class'        => '',
				'video_fit'    => 'contain',
				'stage_ratio'  => '',
				'ratio'        => '',
				'fill'         => 'no',
				'arrows'       => 'yes',
				'autoplay'     => 'yes',
				'autoplay_ms'  => 10000,
			],
			$atts,
			'relod_product_media_gallery'
		);

		$product_id = absint( $atts['id'] );
		if ( $product_id < 1 ) {
			$product_id = $this->detect_current_product_id();
		}
		if ( $product_id < 1 ) {
			return '';
		}

		$requested_ratio = '' !== trim( (string) $atts['stage_ratio'] ) ? (string) $atts['stage_ratio'] : (string) $atts['ratio'];

		return $this->get_shortcode_gallery_html(
			$product_id,
			absint( $atts['variation_id'] ),
			trim( (string) $atts['class'] ),
			(string) $atts['video_fit'],
			$requested_ratio,
			'yes' === (string) $atts['fill'],
			'yes' === (string) $atts['autoplay'],
			max( 1000, absint( $atts['autoplay_ms'] ) ),
			'yes' === (string) $atts['arrows']
		);
	}

	// -------------------------------------------------------------------------
	// Shortcode: existing swatches
	// -------------------------------------------------------------------------

	public function shortcode_swatches( $atts ): string {
		$atts = shortcode_atts(
			[
				'id'                    => 0,
				'class'                 => '',
				'show_out_of_stock'     => 'true',
				'max_items'             => 0,
				'selected_variation_id' => 0,
			],
			$atts,
			'relod_variation_swatches'
		);

		$product_id = absint( $atts['id'] );
		if ( $product_id < 1 ) {
			$product_id = $this->detect_current_product_id();
		}
		if ( $product_id < 1 ) {
			return '';
		}

		$product = wc_get_product( $product_id );
		if ( ! $product instanceof \WC_Product_Variable ) {
			return '';
		}

		$swatches = $this->get_product_swatches(
			$product,
			[
				'show_out_of_stock' => 'true' === (string) $atts['show_out_of_stock'],
				'max_items'         => absint( $atts['max_items'] ),
			]
		);

		if ( empty( $swatches ) ) {
			return '';
		}

		$this->enqueue_assets_for_single();

		$selected_variation_id = absint( $atts['selected_variation_id'] );
		if ( $selected_variation_id < 1 ) {
			$selected_variation_id = $this->detect_selected_variation_id_from_request( $product );
		}

		$classes             = trim( 'relod-wcvms-swatches relod-wcvms-single-swatches ' . (string) $atts['class'] );
		$single_swatch_size = max( 1, (int) round( $this->settings->get_single_swatch_size() * 1.4 ) );
		ob_start();
		?>
		<div class="<?php echo esc_attr( $classes ); ?>" data-relod-swatches data-product-id="<?php echo esc_attr( (string) $product_id ); ?>" data-relod-variation-matrix="<?php echo esc_attr( wp_json_encode( $this->get_product_variation_matrix( $product ) ) ); ?>" style="--relod-wcvms-single-swatch-size: <?php echo esc_attr( (string) $single_swatch_size ); ?>px;">
			<?php foreach ( $swatches as $item ) : ?>
				<button
					type="button"
					class="relod-wcvms-swatch <?php echo ( $selected_variation_id === (int) $item['variation_id'] ) ? 'is-active' : ''; ?> <?php echo ! empty( $item['out_of_stock'] ) ? 'is-out-of-stock' : ''; ?>"
					data-variation-id="<?php echo esc_attr( (string) $item['variation_id'] ); ?>"
					data-variation-url="<?php echo esc_url( $item['url'] ); ?>"
					data-variation-attributes="<?php echo esc_attr( wp_json_encode( $item['attributes'] ) ); ?>"
					data-attribute-name="<?php echo esc_attr( $item['attribute_name'] ?? '' ); ?>"
					data-attribute-value="<?php echo esc_attr( $item['attribute_value'] ?? '' ); ?>"
					aria-label="<?php echo esc_attr( $item['label'] ); ?>"
					title="<?php echo esc_attr( $item['label'] ); ?>"
				>
					<span class="relod-wcvms-swatch__color" style="background: <?php echo esc_attr( $item['color'] ); ?>;"></span>
				</button>
			<?php endforeach; ?>
		</div>
		<?php
		return (string) ob_get_clean();
	}

	public function shortcode_size_swatches( $atts ): string {
		$atts = shortcode_atts(
			[
				'id'                    => 0,
				'class'                 => '',
				'show_out_of_stock'     => 'true',
				'max_items'             => 0,
				'selected_variation_id' => 0,
			],
			$atts,
			'relod_variation_size_swatches'
		);

		$product_id = absint( $atts['id'] );
		if ( $product_id < 1 ) {
			$product_id = $this->detect_current_product_id();
		}
		if ( $product_id < 1 ) {
			return '';
		}

		$product = wc_get_product( $product_id );
		if ( ! $product instanceof \WC_Product_Variable ) {
			return '';
		}

		$sizes = $this->get_product_size_swatches(
			$product,
			[
				'show_out_of_stock' => 'true' === (string) $atts['show_out_of_stock'],
				'max_items'         => absint( $atts['max_items'] ),
			]
		);

		if ( empty( $sizes ) ) {
			return '';
		}

		$this->enqueue_assets_for_single();

		$selected_variation_id = absint( $atts['selected_variation_id'] );
		if ( $selected_variation_id < 1 ) {
			$selected_variation_id = $this->detect_selected_variation_id_from_request( $product );
		}

		$classes = trim( 'relod-wcvms-size-swatches ' . (string) $atts['class'] );
		ob_start();
		?>
		<div class="<?php echo esc_attr( $classes ); ?>" data-relod-size-swatches data-product-id="<?php echo esc_attr( (string) $product_id ); ?>" data-relod-variation-matrix="<?php echo esc_attr( wp_json_encode( $this->get_product_variation_matrix( $product ) ) ); ?>">
			<?php foreach ( $sizes as $item ) : ?>
				<button
					type="button"
					class="relod-wcvms-size-swatch <?php echo ( $selected_variation_id === (int) $item['variation_id'] ) ? 'is-active' : ''; ?> <?php echo ! empty( $item['out_of_stock'] ) ? 'is-out-of-stock' : ''; ?>"
					data-variation-id="<?php echo esc_attr( (string) $item['variation_id'] ); ?>"
					data-variation-url="<?php echo esc_url( $item['url'] ); ?>"
					data-variation-attributes="<?php echo esc_attr( wp_json_encode( $item['attributes'] ) ); ?>"
					data-attribute-name="<?php echo esc_attr( $item['attribute_name'] ?? '' ); ?>"
					data-attribute-value="<?php echo esc_attr( $item['attribute_value'] ?? '' ); ?>"
					aria-label="<?php echo esc_attr( $item['label'] ); ?>"
					title="<?php echo esc_attr( $item['label'] ); ?>"
				>
					<span class="relod-wcvms-size-swatch__text"><?php echo esc_html( $item['label'] ); ?></span>
				</button>
			<?php endforeach; ?>
		</div>
		<?php
		return (string) ob_get_clean();
	}

	// -------------------------------------------------------------------------
	// Shortcode NEW: main product/variation image (fills section)
	// [relod_main_image id="" variation_id="" fit="cover|contain" fill="yes|no" ratio="4/5"]
	// -------------------------------------------------------------------------

	public function shortcode_main_image( $atts ): string {
		$atts = shortcode_atts(
			[
				'id'           => 0,
				'variation_id' => 0,
				'fit'          => 'cover',   // cover | contain
				'fill'         => 'no',      // yes = height:100% fills section; no = use ratio
				'ratio'        => '',        // e.g. "4/5", "16/9" — overrides settings ratio
				'class'        => '',
			],
			$atts,
			'relod_main_image'
		);

		$product_id = absint( $atts['id'] );
		if ( $product_id < 1 ) {
			$product_id = $this->detect_current_product_id();
		}
		if ( $product_id < 1 ) {
			return '';
		}

		$product      = wc_get_product( $product_id );
		$variation_id = absint( $atts['variation_id'] );
		if ( $variation_id < 1 && $product instanceof \WC_Product_Variable ) {
			$variation_id = $this->detect_selected_variation_id_from_request( $product );
		}

		$fit = in_array( $atts['fit'], [ 'cover', 'contain' ], true ) ? $atts['fit'] : 'cover';
		$image_html = $this->get_main_image_html( $product_id, $variation_id, $fit );
		if ( '' === $image_html ) {
			return '';
		}

		$this->enqueue_assets_for_single();

		$fill      = 'yes' === (string) $atts['fill'];
		$ratio     = $this->resolve_stage_ratio( (string) $atts['ratio'] );
		$css_class = trim( 'relod-wcvms-main-image-wrap relod-wcvms-fit--' . $fit . ( $fill ? ' relod-wcvms-fill' : '' ) . ' ' . (string) $atts['class'] );

		$inline_style   = $fill ? '' : ( 'aspect-ratio:' . esc_attr( $ratio ) . ';' );
		$lightbox_items = $this->build_lightbox_items( $product_id, $variation_id );

		return '<div class="' . esc_attr( $css_class ) . '"'
			. ' data-relod-main-image="1"'
			. ( empty( $lightbox_items ) ? '' : ' data-relod-lightbox="1"' )
			. ' data-product-id="' . esc_attr( (string) $product_id ) . '"'
			. ' data-variation-id="' . esc_attr( (string) $variation_id ) . '"'
			. ' style="' . $inline_style . '">'
			. $image_html
			. $this->render_lightbox_data( $lightbox_items )
			. '</div>';
	}

	// -------------------------------------------------------------------------
	// Shortcode NEW: gallery slider with dots overlay (fills section)
	// [relod_gallery_slider id="" variation_id="" fill="yes|no" ratio="4/5"
	//   video_fit="cover" arrows="yes|no" class=""]
	// -------------------------------------------------------------------------

	public function shortcode_slider( $atts ): string {
		$atts = shortcode_atts(
			[
				'id'           => 0,
				'variation_id' => 0,
				'fill'         => 'no',      // yes = height:100% fills section
				'ratio'        => '',        // overrides settings ratio
				'video_fit'    => 'contain',
				'arrows'       => 'yes',     // yes = show prev/next arrows
				'class'        => '',
			],
			$atts,
			'relod_gallery_slider'
		);

		$product_id = absint( $atts['id'] );
		if ( $product_id < 1 ) {
			$product_id = $this->detect_current_product_id();
		}
		if ( $product_id < 1 ) {
			return '';
		}

		$product      = wc_get_product( $product_id );
		$variation_id = absint( $atts['variation_id'] );
		if ( $variation_id < 1 && $product instanceof \WC_Product_Variable ) {
			$variation_id = $this->detect_selected_variation_id_from_request( $product );
		}

		$video_fit = $this->resolve_video_fit( (string) $atts['video_fit'] );
		$items     = $this->build_media_items( $product_id, $variation_id, $video_fit, false );

		if ( empty( $items ) ) {
			return '';
		}

		$this->enqueue_assets_for_single();

		$fill         = 'yes' === (string) $atts['fill'];
		$show_arrows  = 'yes' === (string) $atts['arrows'];
		$ratio        = $this->resolve_stage_ratio( (string) $atts['ratio'] );
		$css_class    = trim( 'relod-wcvms-slider-wrap relod-wcvms-video-fit--' . $video_fit . ( $fill ? ' relod-wcvms-fill' : '' ) . ' ' . (string) $atts['class'] );
		$inline_style = $fill ? '' : ( '--relod-wcvms-slider-ratio:' . esc_attr( $ratio ) . ';' );

		ob_start();
		?>
		<div class="<?php echo esc_attr( $css_class ); ?>"
			 data-relod-slider="1"
			 data-relod-lightbox="1"
			 data-relod-instance="<?php echo esc_attr( $this->generate_slider_instance_id() ); ?>"
			 data-product-id="<?php echo esc_attr( (string) $product_id ); ?>"
			 data-variation-id="<?php echo esc_attr( (string) $variation_id ); ?>"
			 data-video-fit="<?php echo esc_attr( $video_fit ); ?>"
			 data-fill="<?php echo $fill ? '1' : '0'; ?>"
			 data-ratio="<?php echo esc_attr( $ratio ); ?>"
			 data-arrows="<?php echo $show_arrows ? '1' : '0'; ?>"
			 style="<?php echo $inline_style; // phpcs:ignore ?>">

			<?php echo $this->render_slider_inner( $items, $show_arrows ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
			<?php echo $this->render_lightbox_data( $this->build_lightbox_items( $product_id, $variation_id, $video_fit ) ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>

		</div>
		<?php
		return (string) ob_get_clean();
	}

	/**
	 * WooCommerce can remove out-of-stock variations from the front-end variation
	 * dataset when catalog settings hide unavailable products. For this plugin the
	 * user must still be able to select an existing out-of-stock combination and
	 * then use the back-in-stock notification button.
	 */
	public function keep_out_of_stock_variations_visible( bool $hide, int $product_id, $variation ): bool {
		if ( ! $this->settings->is_enabled() ) {
			return $hide;
		}

		if ( is_admin() && ! wp_doing_ajax() ) {
			return $hide;
		}

		if ( $variation instanceof \WC_Product_Variation && $variation->exists() && 'publish' === $variation->get_status() && ! $variation->is_in_stock() ) {
			return false;
		}

		return $hide;
	}

	// -------------------------------------------------------------------------
	// Extend WooCommerce variation data
	// -------------------------------------------------------------------------

	public function extend_available_variation_data( array $data, \WC_Product_Variable $product, \WC_Product_Variation $variation ): array {
		$data['relod_wcvms_color']        = $this->meta->get_variation_color( $variation->get_id() );
		$data['relod_wcvms_swatch_label'] = $this->meta->get_variation_display_label( $variation, $this->meta->get_product_color_attribute_auto( $product ) );
		$data['relod_wcvms_url']          = $this->meta->build_variation_url( $variation );
		$data['relod_wcvms_hidden']       = $this->meta->is_variation_hidden_in_swatches( $variation->get_id() );
		$data['relod_wcvms_is_in_stock']  = $variation->is_in_stock();
		$data['relod_wcvms_is_purchasable'] = $variation->is_purchasable();
		return $data;
	}

	// -------------------------------------------------------------------------
	// AJAX: variation payload (extended with new shortcode fields)
	// -------------------------------------------------------------------------

	public function ajax_variation_payload(): void {
		check_ajax_referer( 'relod_wcvms_nonce', 'nonce' );

		$product_id   = isset( $_POST['product_id'] ) ? absint( wp_unslash( $_POST['product_id'] ) ) : 0;
		$variation_id = isset( $_POST['variation_id'] ) ? absint( wp_unslash( $_POST['variation_id'] ) ) : 0;

		if ( $product_id < 1 || $variation_id < 1 ) {
			wp_send_json_error( [ 'message' => __( 'Некорректные параметры.', 'relod-wc-variation-media-swatches' ) ], 400 );
		}

		$product   = wc_get_product( $product_id );
		$variation = wc_get_product( $variation_id );

		if ( ! $product instanceof \WC_Product_Variable || ! $variation instanceof \WC_Product_Variation || $variation->get_parent_id() !== $product->get_id() ) {
			wp_send_json_error( [ 'message' => __( 'Вариация не найдена.', 'relod-wc-variation-media-swatches' ) ], 404 );
		}

		$shortcode_gallery_instances = isset( $_POST['shortcode_gallery_instances'] ) && is_array( $_POST['shortcode_gallery_instances'] )
			? $this->sanitize_shortcode_gallery_instances( wp_unslash( $_POST['shortcode_gallery_instances'] ) )
			: [];
		$slider_instances = isset( $_POST['slider_instances'] ) && is_array( $_POST['slider_instances'] )
			? $this->sanitize_slider_instances( wp_unslash( $_POST['slider_instances'] ) )
			: [];

		wp_send_json_success( $this->get_variation_payload( $product, $variation, $shortcode_gallery_instances, $slider_instances ) );
	}

	// -------------------------------------------------------------------------
	// Unlimited Elements widget swatches
	// -------------------------------------------------------------------------

	public function output_widget_swatches( $product_id, $uc_id = '', $open_mode = '_blank', $max_items = 0, $show_out_of_stock = 'false' ): void {
		$product_id = absint( $product_id );
		if ( $product_id < 1 ) {
			return;
		}

		$product = wc_get_product( $product_id );
		if ( ! $product instanceof \WC_Product_Variable ) {
			return;
		}

		$swatches = $this->get_product_swatches(
			$product,
			[
				'show_out_of_stock' => 'true' === (string) $show_out_of_stock,
				'max_items'         => absint( $max_items ),
			]
		);

		if ( empty( $swatches ) ) {
			return;
		}

		wp_enqueue_style( 'relod-wcvms-frontend' );

		$target = in_array( (string) $open_mode, [ '_self', '_blank' ], true ) ? (string) $open_mode : $this->settings->get_open_target();
		$rel    = '_blank' === $target ? 'noopener noreferrer' : 'nofollow';

		$grid_swatch_size = max( 1, (int) round( $this->settings->get_grid_swatch_size() * 1.4 ) );
		echo '<div class="relod-ue-vms-swatches" data-relod-ue-swatches="1" style="--relod-wcvms-grid-swatch-size:' . esc_attr( (string) $grid_swatch_size ) . 'px;">';
		foreach ( $swatches as $item ) {
			echo '<a class="relod-ue-vms-swatch ' . ( ! empty( $item['out_of_stock'] ) ? 'is-out-of-stock' : '' ) . '" href="' . esc_url( $item['url'] ) . '" target="' . esc_attr( $target ) . '" rel="' . esc_attr( $rel ) . '" title="' . esc_attr( $item['label'] ) . '"><span class="relod-ue-vms-swatch__color" style="background:' . esc_attr( $item['color'] ) . ';"></span></a>';
		}
		echo '</div>';
	}

	// =========================================================================
	// Private helpers
	// =========================================================================

	private function enqueue_assets_for_single(): void {
		wp_enqueue_style( 'relod-wcvms-frontend' );
		wp_enqueue_script( 'relod-wcvms-frontend' );

		$current_product_id = $this->detect_current_product_id();
		wp_localize_script(
			'relod-wcvms-frontend',
			'relodWcvms',
			[
				'ajaxUrl'     => admin_url( 'admin-ajax.php' ),
				'nonce'       => wp_create_nonce( 'relod_wcvms_nonce' ),
				'productId'   => $current_product_id,
				'historyMode' => $this->settings->get_history_mode(),
				'lightbox'    => [
					'maxZoom'    => 3.5,
					'stepZoom'   => 2.2,
					'showThumbs' => true,
				],
				'i18n'        => [
					'gallery'        => __( 'Галерея товара', 'relod-wc-variation-media-swatches' ),
					'close'          => __( 'Закрыть', 'relod-wc-variation-media-swatches' ),
					'prev'           => __( 'Предыдущее изображение', 'relod-wc-variation-media-swatches' ),
					'next'           => __( 'Следующее изображение', 'relod-wc-variation-media-swatches' ),
					'zoomIn'         => __( 'Увеличить', 'relod-wc-variation-media-swatches' ),
					'zoomOut'        => __( 'Уменьшить', 'relod-wc-variation-media-swatches' ),
					'fullscreen'     => __( 'Во весь экран', 'relod-wc-variation-media-swatches' ),
					'exitFullscreen' => __( 'Выйти из полноэкранного режима', 'relod-wc-variation-media-swatches' ),
					'openGallery'    => __( 'Открыть изображение во весь экран', 'relod-wc-variation-media-swatches' ),
					'slideOf'        => __( 'Изображение %1$s из %2$s', 'relod-wc-variation-media-swatches' ),
				],
				'selectors'   => [
					'gallery'          => '.relod-wcvms-gallery-wrap, .relod-wcpv-gallery-wrap, [data-relod-gallery-root]',
					'shortcodeGallery' => '[data-relod-shortcode-gallery]',
					'mainImage'        => '[data-relod-main-image]',
					'slider'           => '[data-relod-slider]',
					'price'       => '.summary .price, .single_variation_wrap .woocommerce-variation-price',
					'title'       => '.product_title, h1.product_title',
					'shortDesc'   => '.woocommerce-product-details__short-description',
					'description' => '.woocommerce-Tabs-panel--description',
					'additional'  => '.woocommerce-Tabs-panel--additional_information',
					'sku'         => '.sku_wrapper .sku',
					'meta'        => '.product_meta',
					'stock'       => '.single_variation_wrap .woocommerce-variation-availability, .stock',
				],
			]
		);
	}

	private function sanitize_shortcode_gallery_instances( array $instances ): array {
		$clean = [];

		foreach ( $instances as $instance ) {
			if ( ! is_array( $instance ) ) {
				continue;
			}

			$instance_id = sanitize_key( (string) ( $instance['instance_id'] ?? '' ) );
			if ( '' === $instance_id ) {
				continue;
			}

			$clean[ $instance_id ] = [
				'instance_id' => $instance_id,
				'video_fit'   => $this->resolve_video_fit( (string) ( $instance['video_fit'] ?? '' ) ),
				'arrows'      => ! array_key_exists( 'arrows', $instance ) || ! empty( $instance['arrows'] ),
			];
		}

		return array_values( $clean );
	}

	private function sanitize_slider_instances( array $instances ): array {
		$clean = [];

		foreach ( $instances as $instance ) {
			if ( ! is_array( $instance ) ) {
				continue;
			}

			$instance_id = sanitize_key( (string) ( $instance['instance_id'] ?? '' ) );
			if ( '' === $instance_id ) {
				continue;
			}

			$clean[ $instance_id ] = [
				'instance_id' => $instance_id,
				'video_fit'   => $this->resolve_video_fit( (string) ( $instance['video_fit'] ?? '' ) ),
				'arrows'      => ! empty( $instance['arrows'] ),
			];
		}

		return array_values( $clean );
	}

	private function generate_slider_instance_id(): string {
		static $counter = 0;
		++$counter;

		return 'relodwcvms' . $counter . '_' . substr( md5( uniqid( '', true ) ), 0, 8 );
	}

	// -------------------------------------------------------------------------
	// NEW: Main image HTML (used by shortcode + payload)
	// -------------------------------------------------------------------------

	private function get_main_image_html( int $product_id, int $variation_id, string $fit = 'cover' ): string {
		$image_id = 0;

		if ( $variation_id > 0 ) {
			$variation = wc_get_product( $variation_id );
			if ( $variation instanceof \WC_Product_Variation && $variation->get_parent_id() === $product_id ) {
				$vid = $variation->get_image_id();
				if ( $vid ) {
					$image_id = (int) $vid;
				}
			}
		}

		if ( ! $image_id ) {
			$thumb = get_post_thumbnail_id( $product_id );
			if ( $thumb ) {
				$image_id = (int) $thumb;
			}
		}

		if ( ! $image_id ) {
			return '';
		}

		$fit_class = in_array( $fit, [ 'cover', 'contain' ], true ) ? $fit : 'cover';

		return wp_get_attachment_image(
			$image_id,
			'full',
			false,
			[
				'class'                  => 'relod-wcvms-main-img relod-wcvms-fit--' . esc_attr( $fit_class ),
				'loading'                => 'eager',
				'data-relod-popup-image' => '1',
				'data-relod-media-key'   => 'image_' . $image_id,
			]
		) ?: '';
	}

	// -------------------------------------------------------------------------
	// NEW: Slider inner HTML (track + dots + arrows) — used by shortcode + payload
	// -------------------------------------------------------------------------

	private function render_slider_inner( array $items, bool $show_arrows = false ): string {
		if ( empty( $items ) ) {
			return '';
		}

		ob_start();
		?>
		<div class="relod-wcvms-slider-track">
			<?php foreach ( $items as $index => $item ) : ?>
				<div class="relod-wcvms-slider-item <?php echo 0 === $index ? 'is-active' : ''; ?>"
					 data-index="<?php echo esc_attr( (string) $index ); ?>"
					 data-kind="<?php echo esc_attr( $item['kind'] ); ?>">
					<?php echo $item['stage_html']; // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
				</div>
			<?php endforeach; ?>
		</div>

		<?php if ( count( $items ) > 1 ) : ?>
			<?php if ( $show_arrows ) : ?>
				<button type="button" class="relod-wcvms-slider-arrow relod-wcvms-slider-arrow--prev" aria-label="<?php esc_attr_e( 'Назад', 'relod-wc-variation-media-swatches' ); ?>">&#8249;</button>
				<button type="button" class="relod-wcvms-slider-arrow relod-wcvms-slider-arrow--next" aria-label="<?php esc_attr_e( 'Вперёд', 'relod-wc-variation-media-swatches' ); ?>">&#8250;</button>
			<?php endif; ?>

			<div class="relod-wcvms-slider-dots">
				<?php foreach ( $items as $index => $item ) : ?>
					<button type="button"
							class="relod-wcvms-slider-dot <?php echo 0 === $index ? 'is-active' : ''; ?>"
							data-index="<?php echo esc_attr( (string) $index ); ?>"
							aria-label="<?php echo esc_attr( $item['title'] ); ?>">
					</button>
				<?php endforeach; ?>
			</div>
		<?php endif; ?>
		<?php
		return (string) ob_get_clean();
	}

	// -------------------------------------------------------------------------
	// Product detection / selection helpers
	// -------------------------------------------------------------------------

	private function detect_current_product_id(): int {
		$post_id = get_the_ID();
		if ( $post_id > 0 && 'product' === get_post_type( $post_id ) ) {
			return (int) $post_id;
		}

		global $product;
		if ( $product instanceof \WC_Product ) {
			return (int) $product->get_id();
		}

		return 0;
	}

	private function detect_selected_variation_id_from_request( \WC_Product_Variable $product ): int {
		$attributes = $product->get_variation_attributes();
		if ( empty( $attributes ) ) {
			return 0;
		}

		$chosen = [];
		foreach ( array_keys( $attributes ) as $attribute_key ) {
			$request_key = 'attribute_' . $attribute_key;
			if ( isset( $_GET[ $request_key ] ) ) {
				$chosen[ $request_key ] = wc_clean( wp_unslash( $_GET[ $request_key ] ) );
			}
		}

		if ( empty( $chosen ) ) {
			return 0;
		}

		foreach ( $product->get_children() as $variation_id ) {
			$variation = wc_get_product( $variation_id );
			if ( ! $variation instanceof \WC_Product_Variation ) {
				continue;
			}
			if ( $variation->get_variation_attributes() === $chosen ) {
				return (int) $variation_id;
			}
		}

		return 0;
	}

	// -------------------------------------------------------------------------
	// Swatches collection
	// -------------------------------------------------------------------------

	private function get_product_swatches( \WC_Product_Variable $product, array $args = [] ): array {
		return $this->get_product_attribute_swatches( $product, 'color', $args );
	}

	private function get_product_size_swatches( \WC_Product_Variable $product, array $args = [] ): array {
		return $this->get_product_attribute_swatches( $product, 'size', $args );
	}

	private function get_product_attribute_swatches( \WC_Product_Variable $product, string $type, array $args = [] ): array {
		$args = wp_parse_args(
			$args,
			[
				'show_out_of_stock' => false,
				'max_items'         => 0,
			]
		);

		$attribute_key = 'size' === $type
			? $this->meta->get_product_size_attribute_auto( $product )
			: $this->meta->get_product_color_attribute_auto( $product );

		$items = [];

		foreach ( $product->get_children() as $variation_id ) {
			$variation = wc_get_product( $variation_id );
			if ( ! $variation instanceof \WC_Product_Variation ) {
				continue;
			}
			if ( ! $variation->exists() || 'publish' !== $variation->get_status() ) {
				continue;
			}

			$out_of_stock = ! $variation->is_in_stock();
			if ( ! $out_of_stock && ! $variation->variation_is_visible() ) {
				continue;
			}

			if ( $this->meta->is_variation_hidden_in_swatches( $variation_id ) ) {
				continue;
			}

			if ( $out_of_stock && ! $args['show_out_of_stock'] ) {
				continue;
			}

			$variation_attributes = $variation->get_variation_attributes();

			if ( 'size' === $type ) {
				$label = $this->get_variation_size_display_label( $variation, $attribute_key );
				if ( '' === $label ) {
					continue;
				}

				$group_key = $this->build_size_group_key( $variation, $attribute_key, $label );
				$item = [
					'variation_id'    => $variation_id,
					'label'           => $label,
					'url'             => $this->meta->build_variation_url( $variation ),
					'attributes'      => $variation_attributes,
					'attribute_name'  => $attribute_key ? 'attribute_' . $attribute_key : '',
					'attribute_value' => $attribute_key ? $this->get_variation_attribute_value( $variation, $attribute_key ) : '',
					'out_of_stock'    => $out_of_stock,
				];
			} else {
				$color = $this->meta->get_variation_color( $variation_id );
				if ( '' === $color ) {
					continue;
				}

				$item_attribute_key = '' !== $attribute_key ? $attribute_key : $this->detect_variation_color_attribute_key( $variation, $color );
				$group_key          = $this->build_color_group_key( $variation, $item_attribute_key, $color );
				$item = [
					'variation_id'    => $variation_id,
					'color'           => $color,
					'label'           => $this->meta->get_variation_display_label( $variation, $item_attribute_key ),
					'url'             => $this->meta->build_variation_url( $variation ),
					'attributes'      => $variation_attributes,
					'attribute_name'  => $item_attribute_key ? 'attribute_' . $item_attribute_key : '',
					'attribute_value' => $item_attribute_key ? $this->get_variation_attribute_value( $variation, $item_attribute_key ) : '',
					'out_of_stock'    => $out_of_stock,
				];
			}

			if ( isset( $items[ $group_key ] ) && empty( $items[ $group_key ]['out_of_stock'] ) ) {
				continue;
			}

			$items[ $group_key ] = $item;
		}

		$items = array_values( $items );
		if ( $args['max_items'] > 0 ) {
			$items = array_slice( $items, 0, (int) $args['max_items'] );
		}

		return $items;
	}

	private function get_product_variation_matrix( \WC_Product_Variable $product ): array {
		$matrix = [];

		foreach ( $product->get_children() as $variation_id ) {
			$variation = wc_get_product( $variation_id );
			if ( ! $variation instanceof \WC_Product_Variation ) {
				continue;
			}

			// Матрица должна содержать все существующие опубликованные сочетания,
			// включая отсутствующие на складе. Иначе out-of-stock вариации нельзя
			// выбрать для заявки «сообщить о поступлении».
			if ( ! $variation->exists() || 'publish' !== $variation->get_status() ) {
				continue;
			}

			if ( $this->meta->is_variation_hidden_in_swatches( $variation_id ) ) {
				continue;
			}

			$attributes = [];
			foreach ( $variation->get_variation_attributes() as $attribute_key => $value ) {
				if ( '' === (string) $value ) {
					continue;
				}
				$attributes[ (string) $attribute_key ] = (string) $value;
			}

			if ( empty( $attributes ) ) {
				continue;
			}

			$matrix[] = [
				'variation_id'   => (int) $variation_id,
				'attributes'     => $attributes,
				'is_in_stock'    => $variation->is_in_stock(),
				'is_purchasable' => $variation->is_purchasable(),
			];
		}

		return $matrix;
	}

	private function detect_variation_color_attribute_key( \WC_Product_Variation $variation, string $color ): string {
		foreach ( $variation->get_variation_attributes() as $raw_attribute_key => $value ) {
			$attribute_key = preg_replace( '/^attribute_/', '', (string) $raw_attribute_key );
			$attribute_key = $this->meta->sanitize_product_attribute_key( $attribute_key );
			if ( '' === $attribute_key || '' === (string) $value ) {
				continue;
			}

			$term_color = $this->meta->get_attribute_term_color_by_value( $attribute_key, (string) $value );
			if ( '' !== $term_color && strtolower( $term_color ) === strtolower( $color ) ) {
				return $attribute_key;
			}
		}

		return '';
	}

	private function build_color_group_key( \WC_Product_Variation $variation, string $attribute_key, string $color ): string {
		$variation_attributes = $variation->get_variation_attributes();

		if ( '' !== $attribute_key ) {
			$variation_key = 'attribute_' . $attribute_key;
			$attribute_val = isset( $variation_attributes[ $variation_key ] ) ? (string) $variation_attributes[ $variation_key ] : '';
			if ( '' !== $attribute_val ) {
				return 'attribute:' . $variation_key . ':' . $attribute_val;
			}
		}

		$custom_label = $this->meta->get_variation_swatch_label( $variation->get_id() );
		if ( '' !== $custom_label ) {
			return 'label:' . sanitize_title( $custom_label );
		}

		return 'color:' . strtolower( $color );
	}

	private function build_size_group_key( \WC_Product_Variation $variation, string $attribute_key, string $label ): string {
		$variation_attributes = $variation->get_variation_attributes();

		if ( '' !== $attribute_key ) {
			$variation_key = 'attribute_' . $attribute_key;
			$attribute_val = isset( $variation_attributes[ $variation_key ] ) ? (string) $variation_attributes[ $variation_key ] : '';
			if ( '' !== $attribute_val ) {
				return 'attribute:' . $variation_key . ':' . $attribute_val;
			}
		}

		$custom_label = $this->meta->get_variation_size_label( $variation->get_id() );
		if ( '' !== $custom_label ) {
			return 'size-label:' . sanitize_title( $custom_label );
		}

		return 'size:' . sanitize_title( $label );
	}

	private function get_variation_size_display_label( \WC_Product_Variation $variation, string $attribute_key = '' ): string {
		$custom = $this->meta->get_variation_size_label( $variation->get_id() );
		if ( '' !== $custom ) {
			return $custom;
		}

		if ( '' === $attribute_key ) {
			return '';
		}

		$value = $this->get_variation_attribute_value( $variation, $attribute_key );
		if ( '' === $value ) {
			return '';
		}

		return $this->meta->humanize_attribute_value( $attribute_key, $value );
	}

	private function get_variation_attribute_value( \WC_Product_Variation $variation, string $attribute_key ): string {
		$variation_attributes = $variation->get_variation_attributes();
		$variation_key        = 'attribute_' . $attribute_key;

		if ( isset( $variation_attributes[ $variation_key ] ) && '' !== (string) $variation_attributes[ $variation_key ] ) {
			return (string) $variation_attributes[ $variation_key ];
		}

		return '';
	}

	// -------------------------------------------------------------------------
	// AJAX payload builder (extended)
	// -------------------------------------------------------------------------

	private function get_variation_payload( \WC_Product_Variable $product, \WC_Product_Variation $variation, array $shortcode_gallery_instances = [], array $slider_instances = [] ): array {
		$variation_title       = $variation->get_name();
		$variation_description = $variation->get_description();
		$extended_description  = $this->meta->get_variation_long_description( $variation->get_id() );
		$long_description      = '' !== trim( $extended_description ) ? $extended_description : apply_filters( 'the_content', $product->get_description() );

		$shortcode_gallery_inner_map = [];
		foreach ( $shortcode_gallery_instances as $instance ) {
			$instance_id = isset( $instance['instance_id'] ) ? (string) $instance['instance_id'] : '';
			if ( '' === $instance_id ) {
				continue;
			}

			$video_fit = $this->resolve_video_fit( (string) ( $instance['video_fit'] ?? '' ) );
			$items     = $this->build_media_items( $product->get_id(), $variation->get_id(), $video_fit, false );
			$shortcode_gallery_inner_map[ $instance_id ] = $this->render_slider_inner( $items, ! empty( $instance['arrows'] ) );
		}

		$slider_inner_html_map = [];
		foreach ( $slider_instances as $instance ) {
			$instance_id = isset( $instance['instance_id'] ) ? (string) $instance['instance_id'] : '';
			if ( '' === $instance_id ) {
				continue;
			}

			$video_fit = $this->resolve_video_fit( (string) ( $instance['video_fit'] ?? '' ) );
			$items     = $this->build_media_items( $product->get_id(), $variation->get_id(), $video_fit, false );
			$slider_inner_html_map[ $instance_id ] = $this->render_slider_inner( $items, ! empty( $instance['arrows'] ) );
		}

		return [
			'variation_id'                => $variation->get_id(),
			'url'                         => $this->meta->build_variation_url( $variation ),
			'title'                       => $variation_title,
			'price_html'                  => $variation->get_price_html(),
			'short_description_html'      => '' !== trim( $variation_description ) ? wpautop( wp_kses_post( $variation_description ) ) : '',
			'description_html'            => $long_description,
			'additional_information_html' => $this->build_additional_information_html( $product, $variation ),
			'meta_html'                   => $this->build_meta_html( $product, $variation ),
			'sku'                         => (string) $variation->get_sku(),
			'stock_html'                  => wc_get_stock_html( $variation ),
			'gallery_html'                => $this->get_gallery_html( $product->get_id(), $variation->get_id(), 'relod-wcvms-gallery-wrap', '', '' ),
			'main_image_html'             => $this->get_main_image_html( $product->get_id(), $variation->get_id() ),
			'lightbox_items'              => $this->build_lightbox_items( $product->get_id(), $variation->get_id() ),
			'shortcode_gallery_inner_map' => $shortcode_gallery_inner_map,
			'slider_inner_html_map'       => $slider_inner_html_map,
		];
	}

	// -------------------------------------------------------------------------
	// HTML builders: gallery (original), meta, additional info
	// -------------------------------------------------------------------------

	private function build_additional_information_html( \WC_Product_Variable $product, \WC_Product_Variation $variation ): string {
		$rows = [];

		foreach ( $variation->get_variation_attributes() as $attribute_key => $value ) {
			if ( '' === (string) $value ) {
				continue;
			}
			$clean_key = preg_replace( '/^attribute_/', '', (string) $attribute_key );
			$rows[]    = sprintf(
				'<tr><th>%1$s</th><td>%2$s</td></tr>',
				esc_html( wc_attribute_label( $clean_key, $product ) ),
				esc_html( $this->meta->humanize_attribute_value( $clean_key, (string) $value ) )
			);
		}

		if ( $variation->get_weight() ) {
			$rows[] = '<tr><th>' . esc_html__( 'Вес', 'relod-wc-variation-media-swatches' ) . '</th><td>' . esc_html( wc_format_weight( $variation->get_weight() ) ) . '</td></tr>';
		}

		$dimensions = wc_format_dimensions( $variation->get_dimensions( false ) );
		if ( $dimensions ) {
			$rows[] = '<tr><th>' . esc_html__( 'Размеры', 'relod-wc-variation-media-swatches' ) . '</th><td>' . esc_html( $dimensions ) . '</td></tr>';
		}

		if ( empty( $rows ) ) {
			return '';
		}

		return '<table class="shop_attributes relod-wcvms-shop-attributes"><tbody>' . implode( '', $rows ) . '</tbody></table>';
	}

	private function build_meta_html( \WC_Product_Variable $product, \WC_Product_Variation $variation ): string {
		$pieces = [];
		if ( $variation->get_sku() ) {
			$pieces[] = '<span class="sku_wrapper">' . esc_html__( 'Артикул:', 'relod-wc-variation-media-swatches' ) . ' <span class="sku">' . esc_html( $variation->get_sku() ) . '</span></span>';
		}

		$pieces[] = '<span class="posted_in">' . wc_get_product_category_list( $product->get_id(), ', ' ) . '</span>';
		$pieces[] = '<span class="tagged_as">' . wc_get_product_tag_list( $product->get_id(), ', ' ) . '</span>';

		return '<div class="product_meta relod-wcvms-product-meta">' . implode( '<span class="relod-wcvms-meta-sep"> </span>', array_filter( $pieces ) ) . '</div>';
	}

	private function get_gallery_html( int $product_id, int $variation_id = 0, string $extra_class = '', string $requested_fit = '', string $requested_ratio = '' ): string {
		$video_fit   = $this->resolve_video_fit( $requested_fit );
		$stage_ratio = $this->resolve_stage_ratio( $requested_ratio );
		$items       = $this->build_media_items( $product_id, $variation_id, $video_fit );

		if ( empty( $items ) ) {
			return '';
		}

		$this->enqueue_assets_for_single();

		$classes = trim( 'woocommerce-product-gallery woocommerce-product-gallery--with-images images relod-wcvms-gallery-wrap relod-wcvms-video-fit--' . $video_fit . ' ' . $extra_class );
		ob_start();
		?>
		<div class="<?php echo esc_attr( $classes ); ?>" data-relod-gallery-root="1" data-product-id="<?php echo esc_attr( (string) $product_id ); ?>" data-variation-id="<?php echo esc_attr( (string) $variation_id ); ?>" style="opacity:1; transition:opacity .25s ease-in-out; --relod-wcvms-stage-ratio: <?php echo esc_attr( $stage_ratio ); ?>;">
			<div class="relod-wcvms-gallery" data-relod-lightbox="1" data-pause-others="1" data-video-fit="<?php echo esc_attr( $video_fit ); ?>">
				<?php echo $this->render_lightbox_data( $this->build_lightbox_items( $product_id, $variation_id, $video_fit ) ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
				<div class="relod-wcvms-stage">
					<?php foreach ( $items as $index => $item ) : ?>
						<div class="relod-wcvms-slide <?php echo 0 === $index ? 'is-active' : ''; ?>" data-index="<?php echo esc_attr( (string) $index ); ?>" data-kind="<?php echo esc_attr( $item['kind'] ); ?>">
							<?php echo $item['stage_html']; // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
						</div>
					<?php endforeach; ?>
				</div>
				<?php if ( count( $items ) > 1 ) : ?>
					<div class="relod-wcvms-thumbs" style="--relod-wcvms-thumb-ratio: 1 / 1;">
						<?php foreach ( $items as $index => $item ) : ?>
							<button type="button" class="relod-wcvms-thumb <?php echo 0 === $index ? 'is-active' : ''; ?>" data-index="<?php echo esc_attr( (string) $index ); ?>" aria-label="<?php echo esc_attr( $item['title'] ); ?>">
								<span class="relod-wcvms-thumb-media"><?php echo $item['thumb_html']; // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?></span>
							</button>
						<?php endforeach; ?>
					</div>
				<?php endif; ?>
			</div>
		</div>
		<?php
		return (string) ob_get_clean();
	}


	private function get_shortcode_gallery_html( int $product_id, int $variation_id = 0, string $extra_class = '', string $requested_fit = '', string $requested_ratio = '', bool $fill = true, bool $autoplay = true, int $autoplay_ms = 10000, bool $show_arrows = true ): string {
		$video_fit = $this->resolve_video_fit( $requested_fit );
		$ratio     = $this->resolve_stage_ratio( $requested_ratio );
		$items     = $this->build_media_items( $product_id, $variation_id, $video_fit, false );

		if ( empty( $items ) ) {
			return '';
		}

		$this->enqueue_assets_for_single();

		$classes = trim( 'relod-wcvms-shortcode-gallery relod-wcvms-slider-wrap relod-wcvms-video-fit--' . $video_fit . ( $fill ? ' relod-wcvms-fill' : '' ) . ' ' . $extra_class );
		$style   = $fill ? '' : ( '--relod-wcvms-slider-ratio:' . esc_attr( $ratio ) . ';' );

		ob_start();
		?>
		<div class="<?php echo esc_attr( $classes ); ?>"
			 data-relod-slider="1"
			 data-relod-shortcode-gallery="1"
			 data-relod-lightbox="1"
			 data-relod-instance="<?php echo esc_attr( $this->generate_slider_instance_id() ); ?>"
			 data-product-id="<?php echo esc_attr( (string) $product_id ); ?>"
			 data-variation-id="<?php echo esc_attr( (string) $variation_id ); ?>"
			 data-video-fit="<?php echo esc_attr( $video_fit ); ?>"
			 data-fill="<?php echo $fill ? '1' : '0'; ?>"
			 data-stage-ratio="<?php echo esc_attr( $ratio ); ?>"
			 data-autoplay="<?php echo $autoplay ? '1' : '0'; ?>"
			 data-autoplay-ms="<?php echo esc_attr( (string) max( 1000, $autoplay_ms ) ); ?>"
			 data-arrows="<?php echo $show_arrows ? '1' : '0'; ?>"
			 style="<?php echo $style; // phpcs:ignore ?>">

			<?php echo $this->render_slider_inner( $items, $show_arrows ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
			<?php echo $this->render_lightbox_data( $this->build_lightbox_items( $product_id, $variation_id, $video_fit ) ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>

		</div>
		<?php
		return (string) ob_get_clean();
	}

	// -------------------------------------------------------------------------
	// Video / image fit helpers
	// -------------------------------------------------------------------------

	private function resolve_video_fit( string $requested_fit ): string {
		return '' !== trim( $requested_fit ) ? $this->settings->sanitize_video_fit( $requested_fit ) : $this->settings->get_video_fit();
	}

	private function resolve_stage_ratio( string $requested_ratio ): string {
		$requested_ratio = trim( $requested_ratio );
		// Support slash without spaces: "4/5" → "4 / 5"
		if ( '' !== $requested_ratio && false === strpos( $requested_ratio, ' ' ) ) {
			$requested_ratio = str_replace( '/', ' / ', $requested_ratio );
		}
		$allowed = [ '1 / 1', '5 / 4', '4 / 5', '3 / 4', '2 / 3', '9 / 14', '9 / 16', '16 / 9' ];
		return in_array( $requested_ratio, $allowed, true ) ? $requested_ratio : $this->settings->get_stage_ratio();
	}

	// -------------------------------------------------------------------------
	// Media item builders
	// -------------------------------------------------------------------------

	/**
	 * Собирает упорядоченный список медиа (id + тип) для товара/вариации.
	 *
	 * Порядок и правила дедупликации те же, что и раньше в build_media_items():
	 * основное медиа всегда участвует в дедупликации, но попадает в результат
	 * только при $include_main_media = true.
	 *
	 * @return array<int, array{kind:string, id:int}>
	 */
	private function collect_media_ids( int $product_id, int $variation_id, bool $include_main_media = true ): array {
		$product = wc_get_product( $product_id );

		if ( $variation_id > 0 ) {
			$variation = wc_get_product( $variation_id );
			if ( $variation instanceof \WC_Product_Variation && $variation->get_parent_id() === $product_id ) {
				$variation_refs  = [];
				$variation_added = [];

				$image_id = (int) $variation->get_image_id();
				if ( $image_id > 0 ) {
					$variation_added[ 'image_' . $image_id ] = true;
					if ( $include_main_media ) {
						$variation_refs[] = [
							'kind' => 'image',
							'id'   => $image_id,
						];
					}
				}

				foreach ( $this->meta->get_variation_gallery_image_ids( $variation_id ) as $img_id ) {
					$img_id = (int) $img_id;
					if ( $img_id < 1 || ! empty( $variation_added[ 'image_' . $img_id ] ) ) {
						continue;
					}
					$variation_added[ 'image_' . $img_id ] = true;
					$variation_refs[]                      = [
						'kind' => 'image',
						'id'   => $img_id,
					];
				}

				foreach ( $this->meta->get_variation_gallery_video_ids( $variation_id ) as $video_id ) {
					$video_id = (int) $video_id;
					if ( $video_id < 1 || ! empty( $variation_added[ 'video_' . $video_id ] ) ) {
						continue;
					}
					$variation_added[ 'video_' . $video_id ] = true;
					$variation_refs[]                        = [
						'kind' => 'video',
						'id'   => $video_id,
					];
				}

				if ( ! empty( $variation_refs ) ) {
					return $variation_refs;
				}
			}
		}

		$refs  = [];
		$added = [];

		$main_video = (int) $this->meta->get_product_main_video_id( $product_id );
		if ( $main_video > 0 ) {
			$added[ 'video_' . $main_video ] = true;
			if ( $include_main_media ) {
				$refs[] = [
					'kind' => 'video',
					'id'   => $main_video,
				];
			}
		}

		$featured_id = (int) get_post_thumbnail_id( $product_id );
		if ( $featured_id > 0 ) {
			$added[ 'image_' . $featured_id ] = true;
			if ( $include_main_media ) {
				$refs[] = [
					'kind' => 'image',
					'id'   => $featured_id,
				];
			}
		}

		if ( $product instanceof \WC_Product ) {
			foreach ( array_filter( array_map( 'absint', $product->get_gallery_image_ids() ) ) as $attachment_id ) {
				if ( ! empty( $added[ 'image_' . $attachment_id ] ) ) {
					continue;
				}
				$added[ 'image_' . $attachment_id ] = true;
				$refs[]                             = [
					'kind' => 'image',
					'id'   => (int) $attachment_id,
				];
			}
		}

		foreach ( $this->meta->get_product_gallery_video_ids( $product_id ) as $video_id ) {
			$video_id = (int) $video_id;
			if ( $video_id < 1 || ! empty( $added[ 'video_' . $video_id ] ) ) {
				continue;
			}
			$added[ 'video_' . $video_id ] = true;
			$refs[]                        = [
				'kind' => 'video',
				'id'   => $video_id,
			];
		}

		return $refs;
	}

	private function build_items_from_refs( array $refs, string $video_fit ): array {
		$items                  = [];
		$primary_stage_rendered = false;

		foreach ( $refs as $ref ) {
			$item = 'video' === $ref['kind']
				? $this->build_video_item( $ref['id'], $video_fit, ! $primary_stage_rendered )
				: $this->build_image_item( $ref['id'], ! $primary_stage_rendered );

			if ( empty( $item ) ) {
				continue;
			}

			$items[]                = $item;
			$primary_stage_rendered = true;
		}

		return array_values( $items );
	}

	private function build_media_items( int $product_id, int $variation_id, string $video_fit, bool $include_main_media = true ): array {
		$items = $this->build_items_from_refs(
			$this->collect_media_ids( $product_id, $variation_id, $include_main_media ),
			$video_fit
		);

		// Если у вариации ничего не отрисовалось (например, вложения удалены),
		// показываем медиа самого товара — как это делала прежняя реализация.
		if ( empty( $items ) && $variation_id > 0 ) {
			$items = $this->build_items_from_refs(
				$this->collect_media_ids( $product_id, 0, $include_main_media ),
				$video_fit
			);
		}

		return $items;
	}

	// -------------------------------------------------------------------------
	// Lightbox (модальное окно с перелистыванием и зумом)
	// -------------------------------------------------------------------------

	/**
	 * Данные всех медиа товара/вариации для модального окна.
	 *
	 * В модалку всегда попадает полный набор, включая основное изображение,
	 * даже если конкретный шорткод его на странице не показывает.
	 *
	 * @return array<int, array<string, mixed>>
	 */
	private function build_lightbox_items( int $product_id, int $variation_id, string $video_fit = 'contain' ): array {
		// На странице обычно несколько шорткодов сразу ([relod_main_image] +
		// [relod_product_media_gallery]), а набор для модалки у них общий.
		static $cache = [];

		$cache_key = $product_id . ':' . $variation_id . ':' . $video_fit;
		if ( isset( $cache[ $cache_key ] ) ) {
			return $cache[ $cache_key ];
		}

		$items = $this->build_lightbox_from_refs( $this->collect_media_ids( $product_id, $variation_id, true ), $video_fit );

		if ( empty( $items ) && $variation_id > 0 ) {
			$items = $this->build_lightbox_from_refs( $this->collect_media_ids( $product_id, 0, true ), $video_fit );
		}

		$cache[ $cache_key ] = $items;

		return $items;
	}

	private function build_lightbox_from_refs( array $refs, string $video_fit ): array {
		$items = [];

		foreach ( $refs as $ref ) {
			$item = 'video' === $ref['kind']
				? $this->build_lightbox_video( $ref['id'] )
				: $this->build_lightbox_image( $ref['id'] );

			if ( ! empty( $item ) ) {
				$item['video_fit'] = $video_fit;
				$items[]           = $item;
			}
		}

		return $items;
	}

	private function build_lightbox_image( int $attachment_id ): array {
		$full = wp_get_attachment_image_src( $attachment_id, 'full' );
		if ( empty( $full[0] ) ) {
			return [];
		}

		$title = trim( (string) get_the_title( $attachment_id ) );

		return [
			'key'    => 'image_' . $attachment_id,
			'kind'   => 'image',
			'id'     => $attachment_id,
			'src'    => (string) $full[0],
			'srcset' => (string) wp_get_attachment_image_srcset( $attachment_id, 'full' ),
			'sizes'  => '100vw',
			'width'  => ! empty( $full[1] ) ? absint( $full[1] ) : 0,
			'height' => ! empty( $full[2] ) ? absint( $full[2] ) : 0,
			'alt'    => $this->get_attachment_alt_text( $attachment_id ),
			'title'  => '' !== $title ? $title : __( 'Изображение товара', 'relod-wc-variation-media-swatches' ),
			'thumb'  => (string) ( wp_get_attachment_image_url( $attachment_id, 'woocommerce_thumbnail' ) ?: $full[0] ),
		];
	}

	private function build_lightbox_video( int $attachment_id ): array {
		$attachment_id = $this->meta->sanitize_video_id( $attachment_id );
		if ( $attachment_id < 1 ) {
			return [];
		}

		$src = (string) wp_get_attachment_url( $attachment_id );
		if ( '' === $src ) {
			return [];
		}

		$title = trim( (string) get_the_title( $attachment_id ) );
		if ( '' === $title ) {
			$title = __( 'Видео товара', 'relod-wc-variation-media-swatches' );
		}

		$poster = $this->get_video_poster_url( $attachment_id, 'full' );

		return [
			'key'    => 'video_' . $attachment_id,
			'kind'   => 'video',
			'id'     => $attachment_id,
			'src'    => $src,
			'mime'   => (string) get_post_mime_type( $attachment_id ),
			'poster' => $poster,
			'title'  => $title,
			'alt'    => $title,
			'thumb'  => (string) ( $this->get_video_poster_url( $attachment_id, 'woocommerce_thumbnail' ) ?: $poster ),
		];
	}

	/**
	 * JSON-контейнер с данными модального окна внутри блока галереи.
	 */
	private function render_lightbox_data( array $items ): string {
		if ( empty( $items ) ) {
			return '';
		}

		$json = wp_json_encode( $items, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_HEX_TAG | JSON_HEX_AMP | JSON_HEX_APOS | JSON_HEX_QUOT );
		if ( ! is_string( $json ) ) {
			return '';
		}

		return '<script type="application/json" class="relod-wcvms-lightbox-data">' . $json . '</script>';
	}

	private function build_image_item( int $attachment_id, bool $is_primary_stage = false ): array {
		$stage = $this->build_stage_image_html( $attachment_id, $is_primary_stage );
		$thumb = wp_get_attachment_image(
			$attachment_id,
			'woocommerce_thumbnail',
			false,
			[
				'loading'  => 'lazy',
				'decoding' => 'async',
			]
		);
		$title = trim( (string) get_the_title( $attachment_id ) );

		if ( '' === $stage || '' === $thumb ) {
			return [];
		}

		return [
			'kind'       => 'image',
			'title'      => $title ? $title : __( 'Изображение товара', 'relod-wc-variation-media-swatches' ),
			'stage_html' => $stage,
			'thumb_html' => $thumb,
		];
	}

	private function build_stage_image_html( int $attachment_id, bool $is_primary_stage = false ): string {
		$image = wp_get_attachment_image_src( $attachment_id, 'full' );
		if ( empty( $image[0] ) ) {
			return '';
		}

		$attrs = [
			'src'                    => esc_url( (string) $image[0] ),
			'width'                  => ! empty( $image[1] ) ? (string) absint( $image[1] ) : null,
			'height'                 => ! empty( $image[2] ) ? (string) absint( $image[2] ) : null,
			'class'                  => 'relod-wcvms-stage-image',
			'alt'                    => $this->get_attachment_alt_text( $attachment_id ),
			'loading'                => $is_primary_stage ? 'eager' : 'lazy',
			'decoding'               => $is_primary_stage ? 'sync' : 'async',
			'data-relod-popup-image' => '1',
			'data-relod-media-key'   => 'image_' . $attachment_id,
		];

		if ( $is_primary_stage ) {
			$attrs['fetchpriority'] = 'high';
		}

		return '<img' . $this->build_html_attrs( $attrs ) . '>';
	}

	private function get_attachment_alt_text( int $attachment_id ): string {
		$alt = trim( (string) get_post_meta( $attachment_id, '_wp_attachment_image_alt', true ) );
		if ( '' !== $alt ) {
			return $alt;
		}

		return trim( (string) get_the_title( $attachment_id ) );
	}

	private function build_html_attrs( array $attrs ): string {
		$html = '';
		foreach ( $attrs as $name => $value ) {
			if ( null === $value || false === $value ) {
				continue;
			}
			$html .= ' ' . esc_attr( (string) $name ) . '="' . esc_attr( (string) $value ) . '"';
		}

		return $html;
	}

	private function build_video_item( int $attachment_id, string $video_fit, bool $is_primary_stage = false ): array {
		$attachment_id = $this->meta->sanitize_video_id( $attachment_id );
		if ( $attachment_id < 1 ) {
			return [];
		}

		$src  = (string) wp_get_attachment_url( $attachment_id );
		$mime = (string) get_post_mime_type( $attachment_id );
		if ( '' === $src ) {
			return [];
		}

		$title = trim( (string) get_the_title( $attachment_id ) );
		if ( '' === $title ) {
			$title = basename( (string) get_attached_file( $attachment_id ) );
		}
		if ( '' === $title ) {
			$title = __( 'Видео товара', 'relod-wc-variation-media-swatches' );
		}

		$stage_poster_url = $this->get_video_poster_url( $attachment_id, 'full' );
		$thumb_poster_url = $this->get_video_poster_url( $attachment_id, 'woocommerce_thumbnail' );
		$poster_attr      = $stage_poster_url ? ' poster="' . esc_url( $stage_poster_url ) . '"' : '';
		$fit_class        = ' relod-wcvms-stage-video--' . esc_attr( $video_fit );
		$preload          = $is_primary_stage ? 'metadata' : 'none';

		$soft_background_html = '';
		if ( 'soft' === $video_fit ) {
			if ( '' !== $stage_poster_url ) {
				$soft_background_html = '<span class="relod-wcvms-video-soft-bg" aria-hidden="true"><img src="' . esc_url( $stage_poster_url ) . '" alt="" loading="lazy" decoding="async"></span>';
			} else {
				$soft_background_html = '<span class="relod-wcvms-video-soft-bg" aria-hidden="true"><video class="relod-wcvms-video-bg-tag" muted playsinline preload="' . esc_attr( $preload ) . '" loop tabindex="-1"><source src="' . esc_url( $src ) . '" type="' . esc_attr( $mime ) . '"></video></span>';
			}
		}

		$stage_html = '<div class="relod-wcvms-stage-video' . $fit_class . '" data-relod-media-key="video_' . (int) $attachment_id . '">' . $soft_background_html . '<video playsinline loop preload="' . esc_attr( $preload ) . '" class="relod-wcvms-video-tag" muted disablepictureinpicture controlslist="nodownload noplaybackrate noremoteplayback"' . $poster_attr . '><source src="' . esc_url( $src ) . '" type="' . esc_attr( $mime ) . '"></video></div>';

		return [
			'kind'       => 'video',
			'title'      => $title,
			'stage_html' => $stage_html,
			'thumb_html' => $this->build_video_thumbnail_html( $attachment_id, $thumb_poster_url ),
		];
	}

	private function get_video_poster_url( int $attachment_id, string $size = 'full' ): string {
		$thumb_id = get_post_thumbnail_id( $attachment_id );
		if ( $thumb_id ) {
			$poster = wp_get_attachment_image_url( $thumb_id, $size );
			if ( $poster ) {
				return (string) $poster;
			}
		}
		return '';
	}

	private function build_video_thumbnail_html( int $attachment_id, string $poster = '' ): string {
		if ( '' !== $poster ) {
			return '<span class="relod-wcvms-thumb-video"><img src="' . esc_url( $poster ) . '" alt="" loading="lazy" decoding="async"><span class="relod-wcvms-play-icon" aria-hidden="true"></span></span>';
		}

		$src  = (string) wp_get_attachment_url( $attachment_id );
		$mime = (string) get_post_mime_type( $attachment_id );
		if ( '' !== $src ) {
			return '<span class="relod-wcvms-thumb-video relod-wcvms-thumb-video--generated"><video class="relod-wcvms-thumb-video-tag" muted playsinline preload="none" tabindex="-1" aria-hidden="true"><source src="' . esc_url( $src ) . '" type="' . esc_attr( $mime ) . '"></video><span class="relod-wcvms-play-icon" aria-hidden="true"></span></span>';
		}

		$label = trim( (string) get_the_title( $attachment_id ) );
		if ( '' === $label ) {
			$label = __( 'Видео', 'relod-wc-variation-media-swatches' );
		}

		return '<span class="relod-wcvms-thumb-fallback"><span class="relod-wcvms-play-icon" aria-hidden="true"></span><span class="relod-wcvms-thumb-label">' . esc_html( $label ) . '</span></span>';
	}

}
