<?php
namespace RELOD\WCVMS;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

class Admin {
	/** @var Meta */
	private $meta;

	/** @var Settings */
	private $settings;

	public function __construct( Meta $meta, Settings $settings ) {
		$this->meta     = $meta;
		$this->settings = $settings;

		add_action( 'admin_enqueue_scripts', [ $this, 'enqueue_assets' ] );

		add_filter( 'woocommerce_product_data_tabs',   [ $this, 'add_product_data_tab' ] );
		add_action( 'woocommerce_product_data_panels', [ $this, 'render_product_data_panel' ] );
		add_action( 'woocommerce_process_product_meta', [ $this, 'save_product_data_panel' ] );

		add_action( 'woocommerce_product_after_variable_attributes', [ $this, 'render_variation_fields' ], 20, 3 );
		add_action( 'woocommerce_save_product_variation', [ $this, 'save_variation_fields' ], 20, 2 );
		add_action( 'woocommerce_create_product_variation', [ $this, 'apply_default_color_to_variation' ], 20, 1 );

		add_action( 'init', [ $this, 'register_color_attribute_term_fields' ], 30 );
	}

	// -------------------------------------------------------------------------
	// Assets
	// -------------------------------------------------------------------------

	public function enqueue_assets( string $hook ): void {
		$screen = function_exists( 'get_current_screen' ) ? get_current_screen() : null;

		$is_product_screen = in_array( $hook, [ 'post.php', 'post-new.php' ], true )
			&& $screen
			&& 'product' === $screen->post_type;

		$taxonomy = isset( $_GET['taxonomy'] ) ? sanitize_key( wp_unslash( $_GET['taxonomy'] ) ) : '';
		$is_color_term_screen = in_array( $hook, [ 'edit-tags.php', 'term.php' ], true )
			&& '' !== $taxonomy
			&& $this->is_color_attribute_taxonomy( $taxonomy );

		if ( ! $is_product_screen && ! $is_color_term_screen ) {
			return;
		}

		wp_enqueue_style( 'wp-color-picker' );
		wp_enqueue_style(
			'relod-wcvms-admin',
			RELOD_WCVMS_URL . 'assets/css/admin.css',
			[ 'wp-color-picker' ],
			RELOD_WCVMS_VERSION
		);

		if ( $is_product_screen ) {
			wp_enqueue_media();
			wp_enqueue_script( 'jquery-ui-sortable' );
		}

		wp_enqueue_script(
			'relod-wcvms-admin',
			RELOD_WCVMS_URL . 'assets/js/admin.js',
			[ 'jquery', 'wp-color-picker', 'jquery-ui-sortable' ],
			RELOD_WCVMS_VERSION,
			true
		);

		$product_id       = $is_product_screen && isset( $_GET['post'] ) ? absint( wp_unslash( $_GET['post'] ) ) : 0;
		$product          = $product_id ? wc_get_product( $product_id ) : null;
		$attribute_values = [];

		if ( $product instanceof \WC_Product ) {
			foreach ( $product->get_attributes() as $key => $attribute ) {
				if ( ! $attribute instanceof \WC_Product_Attribute || ! $attribute->get_variation() ) {
					continue;
				}
				$attribute_values[] = [
					'key'   => (string) $key,
					'label' => wc_attribute_label( $key ),
				];
			}
		}

		wp_localize_script(
			'relod-wcvms-admin',
			'relodWcvmsAdmin',
			[
				'productId'  => $product_id,
				'attributes' => $attribute_values,
				'palette'    => [
					'#000000', '#ffffff', '#f7f7f7', '#d4d4d4',
					'#f7c5d8', '#d84f68', '#c94ef2', '#7c5cff',
					'#4e7fff', '#58c4dd', '#66c17a', '#c8d96f',
					'#f5b14b', '#e8703a', '#8a5b3c', '#3d2b1f',
				],
				'i18n'       => [
					'chooseImages'   => __( 'Выберите изображения', 'relod-wc-variation-media-swatches' ),
					'useImages'      => __( 'Использовать изображения', 'relod-wc-variation-media-swatches' ),
					'chooseVideos'   => __( 'Выберите видео', 'relod-wc-variation-media-swatches' ),
					'useVideos'      => __( 'Использовать видео', 'relod-wc-variation-media-swatches' ),
					'chooseVideo'    => __( 'Выберите видео', 'relod-wc-variation-media-swatches' ),
					'useVideo'       => __( 'Использовать', 'relod-wc-variation-media-swatches' ),
					'remove'         => __( 'Удалить', 'relod-wc-variation-media-swatches' ),
					'emptyImages'    => __( 'Изображения не добавлены', 'relod-wc-variation-media-swatches' ),
					'emptyVideos'    => __( 'Видео не добавлены', 'relod-wc-variation-media-swatches' ),
					'emptyVideo'     => __( 'Видео не выбрано', 'relod-wc-variation-media-swatches' ),
					'dragHint'       => __( 'Перетащите для изменения порядка', 'relod-wc-variation-media-swatches' ),
					'photo'          => __( 'фото', 'relod-wc-variation-media-swatches' ),
					'video'          => __( 'видео', 'relod-wc-variation-media-swatches' ),
					'noColor'        => __( 'Цвет не задан', 'relod-wc-variation-media-swatches' ),
				],
			]
		);
	}

	// -------------------------------------------------------------------------
	// Attribute term HEX color fields
	// -------------------------------------------------------------------------

	public function register_color_attribute_term_fields(): void {
		if ( ! function_exists( 'wc_get_attribute_taxonomies' ) || ! function_exists( 'wc_attribute_taxonomy_name' ) ) {
			return;
		}

		foreach ( wc_get_attribute_taxonomies() as $attribute ) {
			$taxonomy = wc_attribute_taxonomy_name( $attribute->attribute_name ?? '' );
			if ( ! $this->is_color_attribute_taxonomy( $taxonomy, $attribute ) ) {
				continue;
			}

			add_action( $taxonomy . '_add_form_fields', [ $this, 'render_color_term_add_field' ], 20, 1 );
			add_action( $taxonomy . '_edit_form_fields', [ $this, 'render_color_term_edit_field' ], 20, 2 );
			add_action( 'created_' . $taxonomy, [ $this, 'save_color_term_meta' ], 20, 2 );
			add_action( 'edited_' . $taxonomy, [ $this, 'save_color_term_meta' ], 20, 2 );
		}
	}

	public function render_color_term_add_field( string $taxonomy ): void {
		if ( ! $this->is_color_attribute_taxonomy( $taxonomy ) ) {
			return;
		}
		?>
		<div class="form-field term-relod-wcvms-color-wrap">
			<label for="relod_wcvms_term_color"><?php esc_html_e( 'Цвет swatch (HEX)', 'relod-wc-variation-media-swatches' ); ?></label>
			<input type="text"
				   id="relod_wcvms_term_color"
				   name="relod_wcvms_term_color"
				   class="relod-wcvms-color-field relod-wcvms-term-color-field"
				   value=""
				   placeholder="#000000" />
			<p class="description"><?php esc_html_e( 'Этот HEX будет автоматически подставляться в вариации, где выбрано это значение атрибута «Цвет».', 'relod-wc-variation-media-swatches' ); ?></p>
			<?php wp_nonce_field( 'relod_wcvms_save_term_color', 'relod_wcvms_term_color_nonce' ); ?>
		</div>
		<?php
	}

	public function render_color_term_edit_field( \WP_Term $term, string $taxonomy ): void {
		if ( ! $this->is_color_attribute_taxonomy( $taxonomy ) ) {
			return;
		}

		$color = $this->meta->get_term_color( (int) $term->term_id );
		?>
		<tr class="form-field term-relod-wcvms-color-wrap">
			<th scope="row">
				<label for="relod_wcvms_term_color"><?php esc_html_e( 'Цвет swatch (HEX)', 'relod-wc-variation-media-swatches' ); ?></label>
			</th>
			<td>
				<input type="text"
					   id="relod_wcvms_term_color"
					   name="relod_wcvms_term_color"
					   class="relod-wcvms-color-field relod-wcvms-term-color-field"
					   value="<?php echo esc_attr( $color ); ?>"
					   placeholder="#000000" />
				<p class="description"><?php esc_html_e( 'Этот HEX будет автоматически подставляться в вариации, где выбрано это значение атрибута «Цвет».', 'relod-wc-variation-media-swatches' ); ?></p>
				<?php wp_nonce_field( 'relod_wcvms_save_term_color', 'relod_wcvms_term_color_nonce' ); ?>
			</td>
		</tr>
		<?php
	}

	public function save_color_term_meta( int $term_id, int $tt_id = 0 ): void {
		if ( ! isset( $_POST['relod_wcvms_term_color_nonce'] ) || ! wp_verify_nonce( sanitize_text_field( wp_unslash( $_POST['relod_wcvms_term_color_nonce'] ) ), 'relod_wcvms_save_term_color' ) ) {
			return;
		}

		if ( ! current_user_can( 'manage_product_terms' ) && ! current_user_can( 'manage_woocommerce' ) ) {
			return;
		}

		$color = isset( $_POST['relod_wcvms_term_color'] )
			? $this->meta->sanitize_hex_color( wp_unslash( $_POST['relod_wcvms_term_color'] ) )
			: '';

		if ( '' !== $color ) {
			update_term_meta( $term_id, Meta::TERM_COLOR, $color );
		} else {
			delete_term_meta( $term_id, Meta::TERM_COLOR );
		}
	}

	public function apply_default_color_to_variation( int $variation_id ): void {
		if ( ! current_user_can( 'edit_post', $variation_id ) ) {
			return;
		}

		$manual = $this->meta->sanitize_hex_color( (string) get_post_meta( $variation_id, Meta::VAR_COLOR, true ) );
		if ( '' !== $manual ) {
			return;
		}

		$color = $this->meta->get_variation_attribute_term_color( $variation_id );
		if ( '' !== $color ) {
			update_post_meta( $variation_id, Meta::VAR_COLOR, $color );
		}
	}

	private function is_color_attribute_taxonomy( string $taxonomy, $attribute = null ): bool {
		$taxonomy = sanitize_key( $taxonomy );
		if ( '' === $taxonomy || 0 !== strpos( $taxonomy, 'pa_' ) ) {
			return false;
		}

		$candidates = [
			$taxonomy,
			str_replace( 'pa_', '', $taxonomy ),
			wc_attribute_label( $taxonomy ),
		];

		if ( is_object( $attribute ) ) {
			$candidates[] = (string) ( $attribute->attribute_name ?? '' );
			$candidates[] = (string) ( $attribute->attribute_label ?? '' );
		}

		$normalized_candidates = array_map(
			static function( $value ) {
				return mb_strtolower( trim( (string) $value ) );
			},
			$candidates
		);

		foreach ( $this->settings->get_color_keywords() as $keyword ) {
			$keyword = mb_strtolower( trim( (string) $keyword ) );
			if ( '' === $keyword ) {
				continue;
			}
			foreach ( $normalized_candidates as $candidate ) {
				if ( '' !== $candidate && false !== mb_strpos( $candidate, $keyword ) ) {
					return true;
				}
			}
		}

		return false;
	}


	// -------------------------------------------------------------------------
	// Product data tab (only variable products)
	// -------------------------------------------------------------------------

	public function add_product_data_tab( array $tabs ): array {
		$tabs['relod_wcvms'] = [
			'label'    => __( 'Variation Media', 'relod-wc-variation-media-swatches' ),
			'target'   => 'relod_wcvms_product_data',
			'class'    => [ 'show_if_variable' ],  // only for variable products
			'priority' => 80,
		];

		return $tabs;
	}

	// -------------------------------------------------------------------------
	// Product-level panel
	// -------------------------------------------------------------------------

	public function render_product_data_panel(): void {
		global $post;

		if ( ! $post instanceof \WP_Post || 'product' !== $post->post_type ) {
			return;
		}

		$product_id           = (int) $post->ID;
		$selected_attribute   = $this->meta->get_product_color_attribute( $product_id );
		$selected_size_attr   = $this->meta->get_product_size_attribute( $product_id );
		$main_video_id        = $this->meta->get_product_main_video_id( $product_id );
		$gallery_video_ids    = $this->meta->get_product_gallery_video_ids( $product_id );
		$product              = wc_get_product( $product_id );
		$variation_attributes = [];

		if ( $product instanceof \WC_Product ) {
			foreach ( $product->get_attributes() as $key => $attribute ) {
				if ( ! $attribute instanceof \WC_Product_Attribute || ! $attribute->get_variation() ) {
					continue;
				}
				$variation_attributes[ $key ] = wc_attribute_label( $key );
			}
		}
		?>
		<div id="relod_wcvms_product_data" class="panel woocommerce_options_panel hidden">

			<?php wp_nonce_field( 'relod_wcvms_save_product', 'relod_wcvms_product_nonce' ); ?>

			<!-- Блок: Настройки swatches -->
			<div class="relod-wcvms-panel-section">
				<h3 class="relod-wcvms-panel-heading">
					<span class="dashicons dashicons-admin-appearance"></span>
					<?php esc_html_e( 'Настройки swatches', 'relod-wc-variation-media-swatches' ); ?>
				</h3>

				<p class="form-field">
					<label for="relod_wcvms_color_attribute"><?php esc_html_e( 'Атрибут цвета для swatches', 'relod-wc-variation-media-swatches' ); ?></label>
					<select id="relod_wcvms_color_attribute" name="relod_wcvms_color_attribute" class="select short">
						<option value=""><?php esc_html_e( '— Автоопределение —', 'relod-wc-variation-media-swatches' ); ?></option>
						<?php foreach ( $variation_attributes as $attribute_key => $label ) : ?>
							<option value="<?php echo esc_attr( $attribute_key ); ?>" <?php selected( $selected_attribute, $attribute_key ); ?>>
								<?php echo esc_html( $label . ' (' . $attribute_key . ')' ); ?>
							</option>
						<?php endforeach; ?>
					</select>
					<span class="description"><?php esc_html_e( 'Выберите атрибут для цветовых кружков. Пустое значение — автоопределение по ключевым словам.', 'relod-wc-variation-media-swatches' ); ?></span>
				</p>

				<p class="form-field">
					<label for="relod_wcvms_size_attribute"><?php esc_html_e( 'Атрибут размера для swatches', 'relod-wc-variation-media-swatches' ); ?></label>
					<select id="relod_wcvms_size_attribute" name="relod_wcvms_size_attribute" class="select short">
						<option value=""><?php esc_html_e( '— Автоопределение —', 'relod-wc-variation-media-swatches' ); ?></option>
						<?php foreach ( $variation_attributes as $attribute_key => $label ) : ?>
							<option value="<?php echo esc_attr( $attribute_key ); ?>" <?php selected( $selected_size_attr, $attribute_key ); ?>>
								<?php echo esc_html( $label . ' (' . $attribute_key . ')' ); ?>
							</option>
						<?php endforeach; ?>
					</select>
					<span class="description"><?php esc_html_e( 'Выберите атрибут для вывода размеров одежды. Пустое значение — автоопределение по ключевым словам.', 'relod-wc-variation-media-swatches' ); ?></span>
				</p>
			</div>

			<!-- Блок: Шорткоды -->
			<div class="relod-wcvms-panel-section">
				<h3 class="relod-wcvms-panel-heading">
					<span class="dashicons dashicons-shortcode"></span>
					<?php esc_html_e( 'Шорткоды', 'relod-wc-variation-media-swatches' ); ?>
				</h3>
				<div class="relod-wcvms-shortcodes-grid">
					<div class="relod-wcvms-shortcode-item">
						<code>[relod_variation_swatches]</code>
						<span class="description"><?php esc_html_e( 'Цветовые кружки вариаций', 'relod-wc-variation-media-swatches' ); ?></span>
					</div>
					<div class="relod-wcvms-shortcode-item">
						<code>[relod_variation_size_swatches]</code>
						<span class="description"><?php esc_html_e( 'Кнопки размеров вариаций', 'relod-wc-variation-media-swatches' ); ?></span>
					</div>
					<div class="relod-wcvms-shortcode-item">
						<code>[relod_product_media_gallery]</code>
						<span class="description"><?php esc_html_e( 'Галерея с превью (миниатюры снизу)', 'relod-wc-variation-media-swatches' ); ?></span>
					</div>
					<div class="relod-wcvms-shortcode-item">
						<code>[relod_main_image fill="yes"]</code>
						<span class="description"><?php esc_html_e( 'Главное фото вариации (во всю секцию)', 'relod-wc-variation-media-swatches' ); ?></span>
					</div>
					<div class="relod-wcvms-shortcode-item relod-wcvms-shortcode-item--new">
						<code>[relod_gallery_slider fill="yes" arrows="yes"]</code>
						<span class="description"><?php esc_html_e( 'Слайдер галереи с точками (во всю секцию)', 'relod-wc-variation-media-swatches' ); ?></span>
					</div>
				</div>
				<p class="description relod-wcvms-shortcode-note">
					<?php esc_html_e( 'Параметры: id="" — ID товара, fill="yes" — растянуть на секцию, ratio="4/5" — задать пропорции, arrows="yes" — кнопки Вперёд/Назад.', 'relod-wc-variation-media-swatches' ); ?>
				</p>
			</div>

			<!-- Блок: Видео товара -->
			<div class="relod-wcvms-panel-section">
				<h3 class="relod-wcvms-panel-heading">
					<span class="dashicons dashicons-video-alt3"></span>
					<?php esc_html_e( 'Видео товара', 'relod-wc-variation-media-swatches' ); ?>
				</h3>

				<div class="relod-wcvms-field-group">
					<p class="relod-wcvms-field-label"><?php esc_html_e( 'Главное видео', 'relod-wc-variation-media-swatches' ); ?></p>
					<div class="relod-wcvms-media-manager relod-wcvms-media-manager--single" data-kind="video">
						<div class="relod-wcvms-media-list relod-wcvms-media-list--single">
							<?php echo $this->render_single_media_item_row( $main_video_id, 'video', 'relod_wcvms_main_video_id' ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
						</div>
						<p class="form-field">
							<button type="button" class="button relod-wcvms-pick-media">
								<span class="dashicons dashicons-video-alt3"></span>
								<?php echo esc_html( $main_video_id ? __( 'Заменить видео', 'relod-wc-variation-media-swatches' ) : __( 'Выбрать видео', 'relod-wc-variation-media-swatches' ) ); ?>
							</button>
						</p>
					</div>
				</div>

				<div class="relod-wcvms-field-group">
					<p class="relod-wcvms-field-label">
						<?php esc_html_e( 'Видео в галерее', 'relod-wc-variation-media-swatches' ); ?>
						<span class="relod-wcvms-count-badge" data-count-for="video"><?php echo count( $gallery_video_ids ) > 0 ? count( $gallery_video_ids ) . ' ' . __( 'видео', 'relod-wc-variation-media-swatches' ) : ''; ?></span>
					</p>
					<div class="relod-wcvms-media-manager" data-kind="video">
						<div class="relod-wcvms-media-list"
							 data-field-name="relod_wcvms_gallery_video_ids[]"
							 data-empty-text="<?php echo esc_attr__( 'Видео не добавлены', 'relod-wc-variation-media-swatches' ); ?>">
							<?php foreach ( $gallery_video_ids as $video_id ) : ?>
								<?php echo $this->render_media_item_row( $video_id, 'video', 'relod_wcvms_gallery_video_ids[]' ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
							<?php endforeach; ?>
						</div>
						<p class="form-field">
							<button type="button" class="button relod-wcvms-pick-media">
								<span class="dashicons dashicons-plus-alt"></span>
								<?php esc_html_e( 'Добавить видео', 'relod-wc-variation-media-swatches' ); ?>
							</button>
							<span class="description relod-wcvms-drag-hint"><?php esc_html_e( 'Перетащите для изменения порядка', 'relod-wc-variation-media-swatches' ); ?></span>
						</p>
					</div>
				</div>
			</div>

		</div>
		<?php
	}

	// -------------------------------------------------------------------------
	// Save product panel
	// -------------------------------------------------------------------------

	public function save_product_data_panel( int $product_id ): void {
		if ( ! isset( $_POST['relod_wcvms_product_nonce'] ) || ! wp_verify_nonce( sanitize_text_field( wp_unslash( $_POST['relod_wcvms_product_nonce'] ) ), 'relod_wcvms_save_product' ) ) {
			return;
		}

		if ( ! current_user_can( 'edit_post', $product_id ) ) {
			return;
		}

		$attribute = isset( $_POST['relod_wcvms_color_attribute'] )
			? $this->meta->sanitize_product_attribute_key( wp_unslash( $_POST['relod_wcvms_color_attribute'] ) )
			: '';
		update_post_meta( $product_id, Meta::PRODUCT_COLOR_ATTRIBUTE, $attribute );

		$size_attribute = isset( $_POST['relod_wcvms_size_attribute'] )
			? $this->meta->sanitize_product_attribute_key( wp_unslash( $_POST['relod_wcvms_size_attribute'] ) )
			: '';
		update_post_meta( $product_id, Meta::PRODUCT_SIZE_ATTRIBUTE, $size_attribute );

		$main_video_id = isset( $_POST['relod_wcvms_main_video_id'] )
			? $this->meta->sanitize_video_id( wp_unslash( $_POST['relod_wcvms_main_video_id'] ) )
			: 0;
		if ( $main_video_id > 0 ) {
			update_post_meta( $product_id, Meta::PRODUCT_MAIN_VIDEO, $main_video_id );
		} else {
			delete_post_meta( $product_id, Meta::PRODUCT_MAIN_VIDEO );
		}

		$gallery_videos = [];
		if ( isset( $_POST['relod_wcvms_gallery_video_ids'] ) && is_array( $_POST['relod_wcvms_gallery_video_ids'] ) ) {
			$gallery_videos = $this->meta->sanitize_video_ids( wp_unslash( $_POST['relod_wcvms_gallery_video_ids'] ) );
		}
		if ( ! empty( $gallery_videos ) ) {
			update_post_meta( $product_id, Meta::PRODUCT_GALLERY_VIDEOS, $gallery_videos );
		} else {
			delete_post_meta( $product_id, Meta::PRODUCT_GALLERY_VIDEOS );
		}
	}

	// -------------------------------------------------------------------------
	// Variation fields
	// -------------------------------------------------------------------------

	public function render_variation_fields( $loop, $variation_data, $variation ): void {
		$variation_id = $variation->ID;
		$color        = $this->meta->get_variation_color( $variation_id );
		$images       = $this->meta->get_variation_gallery_image_ids( $variation_id );
		$videos       = $this->meta->get_variation_gallery_video_ids( $variation_id );
		$long_desc    = $this->meta->get_variation_long_description( $variation_id );
		$label        = $this->meta->get_variation_swatch_label( $variation_id );
		$size_label   = $this->meta->get_variation_size_label( $variation_id );
		$hide         = $this->meta->is_variation_hidden_in_swatches( $variation_id ) ? 'yes' : 'no';

		$image_count = count( $images );
		$video_count = count( $videos );
		?>
		<div class="relod-wcvms-variation-group form-row form-row-full">

			<!-- Заголовок секции с превью и счётчиком -->
			<div class="relod-wcvms-var-header">
				<span class="relod-wcvms-var-color-preview"
					  style="<?php echo $color ? 'background:' . esc_attr( $color ) . ';' : ''; ?>"
					  data-color-preview="<?php echo esc_attr( (string) $variation_id ); ?>"
					  title="<?php echo $color ? esc_attr( $color ) : esc_attr__( 'Цвет не задан', 'relod-wc-variation-media-swatches' ); ?>">
				</span>
				<strong class="relod-wcvms-var-title"><?php esc_html_e( 'RELOD: цвет и медиафайлы', 'relod-wc-variation-media-swatches' ); ?></strong>
				<span class="relod-wcvms-var-counts">
					<?php if ( $image_count > 0 ) : ?>
						<span class="relod-wcvms-badge relod-wcvms-badge--img"
							  data-count-badge="image_<?php echo esc_attr( (string) $variation_id ); ?>">
							<?php echo esc_html( $image_count . ' ' . __( 'фото', 'relod-wc-variation-media-swatches' ) ); ?>
						</span>
					<?php else : ?>
						<span class="relod-wcvms-badge relod-wcvms-badge--img relod-wcvms-badge--empty"
							  data-count-badge="image_<?php echo esc_attr( (string) $variation_id ); ?>">
						</span>
					<?php endif; ?>
					<?php if ( $video_count > 0 ) : ?>
						<span class="relod-wcvms-badge relod-wcvms-badge--vid"
							  data-count-badge="video_<?php echo esc_attr( (string) $variation_id ); ?>">
							<?php echo esc_html( $video_count . ' ' . __( 'видео', 'relod-wc-variation-media-swatches' ) ); ?>
						</span>
					<?php else : ?>
						<span class="relod-wcvms-badge relod-wcvms-badge--vid relod-wcvms-badge--empty"
							  data-count-badge="video_<?php echo esc_attr( (string) $variation_id ); ?>">
						</span>
					<?php endif; ?>
				</span>
			</div>

			<!-- Строка 1: цвет + подпись -->
			<div class="relod-wcvms-row-3col">
				<p class="form-row">
					<label><?php esc_html_e( 'Цвет swatch (HEX)', 'relod-wc-variation-media-swatches' ); ?></label>
					<input type="text"
						   class="short relod-wcvms-color-field"
						   name="relod_wcvms_color[<?php echo esc_attr( $variation_id ); ?>]"
						   value="<?php echo esc_attr( $color ); ?>"
						   placeholder="#f7c5d8"
						   data-variation-id="<?php echo esc_attr( $variation_id ); ?>" />
					<span class="description"><?php esc_html_e( 'Введите HEX или выберите из палитры ниже', 'relod-wc-variation-media-swatches' ); ?></span>
				</p>

				<p class="form-row">
					<label><?php esc_html_e( 'Подпись цветового swatch', 'relod-wc-variation-media-swatches' ); ?></label>
					<input type="text"
						   class="short"
						   name="relod_wcvms_swatch_label[<?php echo esc_attr( $variation_id ); ?>]"
						   value="<?php echo esc_attr( $label ); ?>"
						   placeholder="<?php esc_attr_e( 'напр.: Розовый', 'relod-wc-variation-media-swatches' ); ?>" />
					<span class="description"><?php esc_html_e( 'Если пусто — берётся из цветового атрибута', 'relod-wc-variation-media-swatches' ); ?></span>
				</p>

				<p class="form-row">
					<label><?php esc_html_e( 'Подпись размера', 'relod-wc-variation-media-swatches' ); ?></label>
					<input type="text"
						   class="short"
						   name="relod_wcvms_size_label[<?php echo esc_attr( $variation_id ); ?>]"
						   value="<?php echo esc_attr( $size_label ); ?>"
						   placeholder="<?php esc_attr_e( 'напр.: XL или 46', 'relod-wc-variation-media-swatches' ); ?>" />
					<span class="description"><?php esc_html_e( 'Если пусто — берётся из размерного атрибута', 'relod-wc-variation-media-swatches' ); ?></span>
				</p>
			</div>

			<!-- Скрыть вариацию -->
			<p class="form-row form-row-full relod-wcvms-hide-toggle">
				<label>
					<input type="checkbox"
						   name="relod_wcvms_hide_in_swatches[<?php echo esc_attr( $variation_id ); ?>]"
						   value="yes"
						   <?php checked( $hide, 'yes' ); ?> />
					<?php esc_html_e( 'Скрыть эту вариацию в swatches', 'relod-wc-variation-media-swatches' ); ?>
				</label>
			</p>

			<!-- Галерея изображений -->
			<div class="form-row form-row-full relod-wcvms-media-block">
				<p class="relod-wcvms-field-label">
					<span class="dashicons dashicons-format-image"></span>
					<?php esc_html_e( 'Фотогалерея вариации', 'relod-wc-variation-media-swatches' ); ?>
					<span class="relod-wcvms-badge relod-wcvms-badge--img <?php echo 0 === $image_count ? 'relod-wcvms-badge--empty' : ''; ?>"
						  data-count-badge="image_<?php echo esc_attr( (string) $variation_id ); ?>">
						<?php echo $image_count > 0 ? esc_html( $image_count . ' ' . __( 'фото', 'relod-wc-variation-media-swatches' ) ) : ''; ?>
					</span>
				</p>
				<div class="relod-wcvms-media-manager" data-kind="image">
					<div class="relod-wcvms-media-list"
						 data-field-name="relod_wcvms_gallery_image_ids[<?php echo esc_attr( $variation_id ); ?>][]"
						 data-empty-text="<?php echo esc_attr__( 'Изображения не добавлены', 'relod-wc-variation-media-swatches' ); ?>"
						 data-badge-key="image_<?php echo esc_attr( $variation_id ); ?>"
						 data-media-kind="image">
						<?php foreach ( $images as $image_id ) : ?>
							<?php echo $this->render_media_item_row( $image_id, 'image', 'relod_wcvms_gallery_image_ids[' . $variation_id . '][]' ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
						<?php endforeach; ?>
					</div>
					<p class="form-field relod-wcvms-media-actions">
						<button type="button" class="button relod-wcvms-pick-media">
							<span class="dashicons dashicons-plus-alt"></span>
							<?php esc_html_e( 'Добавить фото', 'relod-wc-variation-media-swatches' ); ?>
						</button>
						<?php if ( $image_count > 0 ) : ?>
							<button type="button" class="button-link relod-wcvms-clear-media" data-confirm="<?php esc_attr_e( 'Удалить все изображения?', 'relod-wc-variation-media-swatches' ); ?>">
								<?php esc_html_e( 'Очистить', 'relod-wc-variation-media-swatches' ); ?>
							</button>
						<?php endif; ?>
						<span class="description relod-wcvms-drag-hint"><?php esc_html_e( 'Перетащите для изменения порядка', 'relod-wc-variation-media-swatches' ); ?></span>
					</p>
				</div>
			</div>

			<!-- Галерея видео -->
			<div class="form-row form-row-full relod-wcvms-media-block">
				<p class="relod-wcvms-field-label">
					<span class="dashicons dashicons-video-alt3"></span>
					<?php esc_html_e( 'Видеогалерея вариации', 'relod-wc-variation-media-swatches' ); ?>
					<span class="relod-wcvms-badge relod-wcvms-badge--vid <?php echo 0 === $video_count ? 'relod-wcvms-badge--empty' : ''; ?>"
						  data-count-badge="video_<?php echo esc_attr( (string) $variation_id ); ?>">
						<?php echo $video_count > 0 ? esc_html( $video_count . ' ' . __( 'видео', 'relod-wc-variation-media-swatches' ) ) : ''; ?>
					</span>
				</p>
				<div class="relod-wcvms-media-manager" data-kind="video">
					<div class="relod-wcvms-media-list"
						 data-field-name="relod_wcvms_gallery_video_ids[<?php echo esc_attr( $variation_id ); ?>][]"
						 data-empty-text="<?php echo esc_attr__( 'Видео не добавлены', 'relod-wc-variation-media-swatches' ); ?>"
						 data-badge-key="video_<?php echo esc_attr( $variation_id ); ?>"
						 data-media-kind="video">
						<?php foreach ( $videos as $video_id ) : ?>
							<?php echo $this->render_media_item_row( $video_id, 'video', 'relod_wcvms_gallery_video_ids[' . $variation_id . '][]' ); // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped ?>
						<?php endforeach; ?>
					</div>
					<p class="form-field relod-wcvms-media-actions">
						<button type="button" class="button relod-wcvms-pick-media">
							<span class="dashicons dashicons-plus-alt"></span>
							<?php esc_html_e( 'Добавить видео', 'relod-wc-variation-media-swatches' ); ?>
						</button>
						<?php if ( $video_count > 0 ) : ?>
							<button type="button" class="button-link relod-wcvms-clear-media" data-confirm="<?php esc_attr_e( 'Удалить все видео?', 'relod-wc-variation-media-swatches' ); ?>">
								<?php esc_html_e( 'Очистить', 'relod-wc-variation-media-swatches' ); ?>
							</button>
						<?php endif; ?>
						<span class="description relod-wcvms-drag-hint"><?php esc_html_e( 'Перетащите для изменения порядка', 'relod-wc-variation-media-swatches' ); ?></span>
					</p>
				</div>
			</div>

			<!-- Расширенное описание -->
			<div class="form-row form-row-full relod-wcvms-variation-longdesc">
				<label>
					<span class="dashicons dashicons-text-page"></span>
					<?php esc_html_e( 'Расширенное описание вариации', 'relod-wc-variation-media-swatches' ); ?>
				</label>
				<textarea name="relod_wcvms_long_description[<?php echo esc_attr( $variation_id ); ?>]" rows="3" placeholder="<?php esc_attr_e( 'Описание для этой вариации. Если пусто — используется описание товара.', 'relod-wc-variation-media-swatches' ); ?>"><?php echo esc_textarea( $long_desc ); ?></textarea>
			</div>

		</div>
		<?php
	}

	// -------------------------------------------------------------------------
	// Save variation fields (FIXED: nonce + capabilities check added)
	// -------------------------------------------------------------------------

	public function save_variation_fields( int $variation_id, int $i ): void {
		// Security: verify nonce (WooCommerce sets this when saving variations)
		$nonce_action = 'woocommerce-save-product_' . $variation_id;
		if ( isset( $_POST['woocommerce_meta_nonce'] ) ) {
			if ( ! wp_verify_nonce( sanitize_text_field( wp_unslash( $_POST['woocommerce_meta_nonce'] ) ), 'woocommerce-save-product' ) ) {
				return;
			}
		}

		// Security: capabilities check
		if ( ! current_user_can( 'edit_post', $variation_id ) ) {
			return;
		}

		// Color: manual HEX has priority; if empty, inherit HEX from the selected term of the color attribute.
		$color = isset( $_POST['relod_wcvms_color'][ $variation_id ] )
			? $this->meta->sanitize_hex_color( wp_unslash( $_POST['relod_wcvms_color'][ $variation_id ] ) )
			: '';

		if ( '' === $color ) {
			$color = $this->meta->get_variation_attribute_term_color( $variation_id );
		}

		if ( '' !== $color ) {
			update_post_meta( $variation_id, Meta::VAR_COLOR, $color );
		} else {
			delete_post_meta( $variation_id, Meta::VAR_COLOR );
		}

		// Swatch label
		$label = isset( $_POST['relod_wcvms_swatch_label'][ $variation_id ] )
			? sanitize_text_field( wp_unslash( $_POST['relod_wcvms_swatch_label'][ $variation_id ] ) )
			: '';
		if ( '' !== $label ) {
			update_post_meta( $variation_id, Meta::VAR_SWATCH_LABEL, $label );
		} else {
			delete_post_meta( $variation_id, Meta::VAR_SWATCH_LABEL );
		}

		$size_label = isset( $_POST['relod_wcvms_size_label'][ $variation_id ] )
			? sanitize_text_field( wp_unslash( $_POST['relod_wcvms_size_label'][ $variation_id ] ) )
			: '';
		if ( '' !== $size_label ) {
			update_post_meta( $variation_id, Meta::VAR_SIZE_LABEL, $size_label );
		} else {
			delete_post_meta( $variation_id, Meta::VAR_SIZE_LABEL );
		}

		// Hide in swatches
		$hide = isset( $_POST['relod_wcvms_hide_in_swatches'][ $variation_id ] ) ? 'yes' : 'no';
		update_post_meta( $variation_id, Meta::VAR_HIDE_IN_SWATCHES, $hide );

		// Gallery images
		$images = [];
		if ( isset( $_POST['relod_wcvms_gallery_image_ids'][ $variation_id ] ) && is_array( $_POST['relod_wcvms_gallery_image_ids'][ $variation_id ] ) ) {
			$images = $this->meta->sanitize_image_ids( wp_unslash( $_POST['relod_wcvms_gallery_image_ids'][ $variation_id ] ) );
		}
		if ( ! empty( $images ) ) {
			update_post_meta( $variation_id, Meta::VAR_GALLERY_IMAGES, $images );
		} else {
			delete_post_meta( $variation_id, Meta::VAR_GALLERY_IMAGES );
		}

		// Gallery videos
		$videos = [];
		if ( isset( $_POST['relod_wcvms_gallery_video_ids'][ $variation_id ] ) && is_array( $_POST['relod_wcvms_gallery_video_ids'][ $variation_id ] ) ) {
			$videos = $this->meta->sanitize_video_ids( wp_unslash( $_POST['relod_wcvms_gallery_video_ids'][ $variation_id ] ) );
		}
		if ( ! empty( $videos ) ) {
			update_post_meta( $variation_id, Meta::VAR_GALLERY_VIDEOS, $videos );
		} else {
			delete_post_meta( $variation_id, Meta::VAR_GALLERY_VIDEOS );
		}

		// Long description
		$long_description = isset( $_POST['relod_wcvms_long_description'][ $variation_id ] )
			? wp_kses_post( wp_unslash( $_POST['relod_wcvms_long_description'][ $variation_id ] ) )
			: '';
		if ( '' !== trim( $long_description ) ) {
			update_post_meta( $variation_id, Meta::VAR_LONG_DESCRIPTION, $long_description );
		} else {
			delete_post_meta( $variation_id, Meta::VAR_LONG_DESCRIPTION );
		}
	}

	// =========================================================================
	// Private helpers
	// =========================================================================

	private function render_single_media_item_row( int $attachment_id, string $kind, string $field_name ): string {
		if ( $attachment_id < 1 ) {
			return '<div class="relod-wcvms-empty-placeholder">' . esc_html__( 'Видео не выбрано', 'relod-wc-variation-media-swatches' ) . '</div><input type="hidden" name="' . esc_attr( $field_name ) . '" value="">';
		}

		return $this->render_media_item_row( $attachment_id, $kind, $field_name );
	}

	private function render_media_item_row( int $attachment_id, string $kind, string $field_name ): string {
		$attachment_id = absint( $attachment_id );
		if ( $attachment_id < 1 ) {
			return '';
		}

		$url      = wp_get_attachment_url( $attachment_id );
		$title    = trim( (string) get_the_title( $attachment_id ) );
		$filename = basename( (string) get_attached_file( $attachment_id ) );
		$mime     = (string) get_post_mime_type( $attachment_id );
		$preview  = '';

		if ( 'image' === $kind ) {
			$preview = wp_get_attachment_image( $attachment_id, 'thumbnail', false, [ 'class' => 'relod-wcvms-thumb-image' ] );
		} else {
			$poster_id = get_post_thumbnail_id( $attachment_id );
			if ( $poster_id ) {
				$preview = wp_get_attachment_image( $poster_id, 'thumbnail', false, [ 'class' => 'relod-wcvms-thumb-image' ] );
			}
			if ( '' === $preview ) {
				$preview = '<span class="relod-wcvms-thumb-video-icon">▶</span>';
			}
		}

		if ( '' === $title ) {
			$title = $filename ? $filename : sprintf( 'ID %d', $attachment_id );
		}

		$hidden_name = esc_attr( $field_name );

		return '<div class="relod-wcvms-media-row" data-id="' . esc_attr( (string) $attachment_id ) . '">' .
			'<div class="relod-wcvms-media-row-left">' .
				'<span class="relod-wcvms-drag dashicons dashicons-move" title="' . esc_attr__( 'Перетащить', 'relod-wc-variation-media-swatches' ) . '"></span>' .
				'<span class="relod-wcvms-thumb">' . $preview . '</span>' .
				'<span class="relod-wcvms-media-row-text"><strong>' . esc_html( $title ) . '</strong><span class="relod-wcvms-media-row-meta">' . esc_html( $mime ) . '</span></span>' .
			'</div>' .
			'<button type="button" class="button-link-delete relod-wcvms-remove-media" title="' . esc_attr__( 'Удалить', 'relod-wc-variation-media-swatches' ) . '">' .
				'<span class="dashicons dashicons-no-alt"></span>' .
			'</button>' .
			'<input type="hidden" name="' . $hidden_name . '" value="' . esc_attr( (string) $attachment_id ) . '">' .
		'</div>';
	}
}
