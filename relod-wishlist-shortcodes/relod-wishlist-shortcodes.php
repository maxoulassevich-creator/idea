<?php
/**
 * Plugin Name: RELOD Wishlist Shortcodes
 * Description: Дополнительные шорткоды для RELOD Wishlist: кнопка избранного на странице товара и иконка избранного для хедера с настраиваемым стилем. Счётчик безопасен для кеша страниц.
 * Version: 1.2.0
 * Author: RELOD
 * Requires at least: 5.8
 * Requires PHP: 7.4
 * Text Domain: relod-wishlist-shortcodes
 */

defined( 'ABSPATH' ) || exit;

define( 'RELOD_WLSC_VERSION', '1.2.0' );
define( 'RELOD_WLSC_PATH', plugin_dir_path( __FILE__ ) );
define( 'RELOD_WLSC_URL', plugin_dir_url( __FILE__ ) );
define( 'RELOD_WLSC_OPTION_KEY', 'relod_wlsc_header_settings' );

final class RELOD_Wishlist_Shortcodes {

	private static $instance = null;
	private $did_enqueue_front = false;

	public static function instance(): self {
		if ( null === self::$instance ) {
			self::$instance = new self();
		}
		return self::$instance;
	}

	private function __construct() {
		add_action( 'plugins_loaded', [ $this, 'load' ], 25 );
	}

	public function load(): void {
		add_action( 'admin_notices', [ $this, 'dependency_notice' ] );
		add_action( 'admin_menu', [ $this, 'admin_menu' ] );
		add_action( 'admin_init', [ $this, 'register_settings' ] );
		add_action( 'admin_enqueue_scripts', [ $this, 'admin_assets' ] );

		add_shortcode( 'relod_wishlist_product_button', [ $this, 'shortcode_product_button' ] );
		add_shortcode( 'relod_wishlist_header_icon', [ $this, 'shortcode_header_icon' ] );
		add_shortcode( 'relod_wishlist_header', [ $this, 'shortcode_header_icon' ] );
	}

	public function dependency_notice(): void {
		if ( ! current_user_can( 'activate_plugins' ) ) {
			return;
		}
		if ( class_exists( 'Relod_Wishlist' ) && class_exists( 'Relod_WL_Cache' ) ) {
			return;
		}
		echo '<div class="notice notice-warning"><p><strong>RELOD Wishlist Shortcodes:</strong> нужен активный плагин <strong>RELOD Wishlist</strong> версии 1.2.0 или выше.</p></div>';
	}

	public function admin_menu(): void {
		add_options_page(
			'RELOD Wishlist Header Icon',
			'Wishlist Header Icon',
			'manage_options',
			'relod-wlsc-settings',
			[ $this, 'render_settings_page' ]
		);
	}

	public function register_settings(): void {
		register_setting(
			'relod_wlsc_settings_group',
			RELOD_WLSC_OPTION_KEY,
			[ $this, 'sanitize_settings' ]
		);

		add_settings_section(
			'relod_wlsc_main',
			'Настройки иконки избранного в хедере',
			function () {
				echo '<p>Здесь можно настроить внешний вид шорткода <code>[relod_wishlist_header_icon]</code>.</p>';
			},
			'relod-wlsc-settings'
		);

		$fields = [
			'link_url'         => [ 'label' => 'Ссылка по клику', 'type' => 'url' ],
			'open_new_tab'     => [ 'label' => 'Открывать в новой вкладке', 'type' => 'checkbox' ],
			'hide_when_empty'  => [ 'label' => 'Прятать счётчик, когда избранное пустое', 'type' => 'checkbox' ],
			'icon_size'        => [ 'label' => 'Размер иконки, px', 'type' => 'number', 'min' => 14, 'max' => 120 ],
			'badge_size'       => [ 'label' => 'Размер кружка счётчика, px', 'type' => 'number', 'min' => 12, 'max' => 60 ],
			'badge_font_size'  => [ 'label' => 'Размер цифры в кружке, px', 'type' => 'number', 'min' => 8, 'max' => 32 ],
			'icon_color'       => [ 'label' => 'Цвет иконки', 'type' => 'color' ],
			'icon_hover_color' => [ 'label' => 'Цвет иконки при наведении', 'type' => 'color' ],
			'badge_bg'         => [ 'label' => 'Цвет кружка счётчика', 'type' => 'color' ],
			'badge_text'       => [ 'label' => 'Цвет цифры счётчика', 'type' => 'color' ],
		];

		foreach ( $fields as $key => $field ) {
			add_settings_field(
				$key,
				$field['label'],
				[ $this, 'render_field' ],
				'relod-wlsc-settings',
				'relod_wlsc_main',
				array_merge( [ 'key' => $key ], $field )
			);
		}
	}

	public function sanitize_settings( $input ): array {
		$defaults = $this->get_settings();
		$out      = [];

		$out['link_url']         = isset( $input['link_url'] ) ? esc_url_raw( trim( (string) $input['link_url'] ) ) : $defaults['link_url'];
		$out['open_new_tab']     = ! empty( $input['open_new_tab'] ) ? '1' : '0';
		$out['hide_when_empty']  = ! empty( $input['hide_when_empty'] ) ? '1' : '0';
		$out['icon_size']        = $this->sanitize_int( $input['icon_size'] ?? $defaults['icon_size'], 14, 120, (int) $defaults['icon_size'] );
		$out['badge_size']       = $this->sanitize_int( $input['badge_size'] ?? $defaults['badge_size'], 12, 60, (int) $defaults['badge_size'] );
		$out['badge_font_size']  = $this->sanitize_int( $input['badge_font_size'] ?? $defaults['badge_font_size'], 8, 32, (int) $defaults['badge_font_size'] );
		$out['icon_color']       = $this->sanitize_hex( $input['icon_color'] ?? $defaults['icon_color'], $defaults['icon_color'] );
		$out['icon_hover_color'] = $this->sanitize_hex( $input['icon_hover_color'] ?? $defaults['icon_hover_color'], $defaults['icon_hover_color'] );
		$out['badge_bg']         = $this->sanitize_hex( $input['badge_bg'] ?? $defaults['badge_bg'], $defaults['badge_bg'] );
		$out['badge_text']       = $this->sanitize_hex( $input['badge_text'] ?? $defaults['badge_text'], $defaults['badge_text'] );

		return $out;
	}

	private function sanitize_int( $value, int $min, int $max, int $fallback ): int {
		$value = absint( $value );
		if ( $value < $min || $value > $max ) {
			return $fallback;
		}
		return $value;
	}

	private function sanitize_hex( $value, string $fallback ): string {
		$value = sanitize_hex_color( (string) $value );
		return $value ? $value : $fallback;
	}

	public function render_field( array $args ): void {
		$settings = $this->get_settings();
		$key      = $args['key'];
		$value    = $settings[ $key ] ?? '';
		$name     = RELOD_WLSC_OPTION_KEY . '[' . $key . ']';
		$type     = $args['type'] ?? 'text';

		switch ( $type ) {
			case 'checkbox':
				printf(
					'<label><input type="checkbox" name="%1$s" value="1" %2$s> Да</label>',
					esc_attr( $name ),
					checked( $value, '1', false )
				);
				break;

			case 'color':
				printf(
					'<input type="text" class="relod-wlsc-color" name="%1$s" value="%2$s" data-default-color="%2$s">',
					esc_attr( $name ),
					esc_attr( $value )
				);
				break;

			case 'number':
				printf(
					'<input type="number" class="small-text" name="%1$s" value="%2$s" min="%3$d" max="%4$d" step="1">',
					esc_attr( $name ),
					esc_attr( (string) $value ),
					(int) ( $args['min'] ?? 0 ),
					(int) ( $args['max'] ?? 999 )
				);
				break;

			case 'url':
			default:
				printf(
					'<input type="url" class="regular-text" name="%1$s" value="%2$s" placeholder="https://site.ru/wishlist/">',
					esc_attr( $name ),
					esc_attr( $value )
				);
		}
	}

	public function render_settings_page(): void {
		if ( ! current_user_can( 'manage_options' ) ) {
			return;
		}
		?>
		<div class="wrap">
			<h1>RELOD Wishlist Header Icon</h1>
			<form method="post" action="options.php">
				<?php
				settings_fields( 'relod_wlsc_settings_group' );
				do_settings_sections( 'relod-wlsc-settings' );
				submit_button();
				?>
			</form>
			<hr>
			<h2>Шорткоды</h2>
			<p><code>[relod_wishlist_product_button]</code> — круглая кнопка избранного для карточки товара.</p>
			<p><code>[relod_wishlist_product_button product_id="123"]</code> — кнопка для конкретного товара.</p>
			<p><code>[relod_wishlist_header_icon]</code> — иконка избранного для хедера со счётчиком.</p>
			<hr>
			<h2>Кеширование</h2>
			<p>Счётчик и состояние кнопок подставляются на стороне браузера, поэтому страницы с ними
			можно спокойно кешировать в WP Rocket. Отдельно исключать их из кеша не нужно —
			страница избранного исключается плагином автоматически.</p>
		</div>
		<?php
	}

	public function admin_assets( string $hook ): void {
		if ( 'settings_page_relod-wlsc-settings' !== $hook ) {
			return;
		}
		wp_enqueue_style( 'wp-color-picker' );
		wp_enqueue_script(
			'relod-wlsc-admin',
			RELOD_WLSC_URL . 'assets/js/admin.js',
			[ 'wp-color-picker' ],
			RELOD_WLSC_VERSION,
			true
		);
	}

	private function get_settings(): array {
		$defaults = [
			'link_url'         => '',
			'open_new_tab'     => '0',
			'hide_when_empty'  => '1',
			'icon_size'        => 24,
			'badge_size'       => 18,
			'badge_font_size'  => 10,
			'icon_color'       => '#111111',
			'icon_hover_color' => '#662132',
			'badge_bg'         => '#111111',
			'badge_text'       => '#ffffff',
		];

		$saved = get_option( RELOD_WLSC_OPTION_KEY, [] );
		return wp_parse_args( is_array( $saved ) ? $saved : [], $defaults );
	}

	private function enqueue_front(): void {
		if ( $this->did_enqueue_front ) {
			return;
		}
		$this->did_enqueue_front = true;

		wp_enqueue_style(
			'relod-wlsc-frontend',
			RELOD_WLSC_URL . 'assets/css/frontend.css',
			[ 'relod-wishlist' ],
			RELOD_WLSC_VERSION
		);

		/**
		 * Скрипт зависит от relod-wishlist и слушает его событие.
		 *
		 * Раньше здесь через wp_localize_script в HTML печатался счётчик,
		 * посчитанный на сервере. WP Rocket клал этот HTML в кеш, и все
		 * гости получали количество товаров того, кто сгенерировал кеш —
		 * отсюда «в шапке 7, на странице 5».
		 */
		wp_enqueue_script(
			'relod-wlsc-frontend',
			RELOD_WLSC_URL . 'assets/js/frontend.js',
			[ 'relod-wishlist' ],
			RELOD_WLSC_VERSION,
			true
		);
	}

	private function get_product_id_from_context( array $atts ): int {
		$product_id = isset( $atts['product_id'] ) ? absint( $atts['product_id'] ) : 0;
		if ( $product_id > 0 ) {
			return $product_id;
		}

		if ( function_exists( 'is_product' ) && is_product() ) {
			global $product;
			if ( is_object( $product ) && method_exists( $product, 'get_id' ) ) {
				return (int) $product->get_id();
			}
			$post_id = get_the_ID();
			if ( $post_id ) {
				return (int) $post_id;
			}
		}

		return 0;
	}

	public function shortcode_product_button( $atts = [] ): string {
		if ( ! class_exists( 'Relod_Wishlist' ) ) {
			return $this->admin_only_message( 'Нужен активный плагин RELOD Wishlist.' );
		}

		$atts       = shortcode_atts( [ 'product_id' => 0 ], (array) $atts, 'relod_wishlist_product_button' );
		$product_id = $this->get_product_id_from_context( $atts );

		if ( ! $product_id || ! function_exists( 'wc_get_product' ) || ! wc_get_product( $product_id ) ) {
			return $this->admin_only_message( 'Шорткод [relod_wishlist_product_button] не нашёл товар.' );
		}

		$this->enqueue_front();

		/* Разметку кнопки строит ядро — там же решается, безопасно ли
		   отдавать активное состояние в кешируемый HTML. */
		$button = method_exists( 'Relod_Wishlist', 'button_html' )
			? Relod_Wishlist::button_html( $product_id, [ 'relod-wl-btn', 'relod-wl-shortcode-product' ] )
			: '';

		return '<span class="relod-wlsc-product-wrap">' . $button . '</span>';
	}

	public function shortcode_header_icon( $atts = [] ): string {
		if ( ! class_exists( 'Relod_Wishlist' ) ) {
			return $this->admin_only_message( 'Нужен активный плагин RELOD Wishlist.' );
		}

		$this->enqueue_front();

		$settings = $this->get_settings();
		$atts     = shortcode_atts( [ 'link' => '' ], (array) $atts, 'relod_wishlist_header_icon' );

		$link_url = $atts['link'] ? esc_url_raw( $atts['link'] ) : $settings['link_url'];

		/**
		 * Счётчик рендерится реальным числом только там, где страница
		 * гарантированно не попадёт в общий кеш. В остальных случаях
		 * выводится нейтральный ноль, а настоящее число подставляет JS
		 * после гидрации — так чужой счётчик не «залипает» в кеше.
		 */
		$personal = class_exists( 'Relod_WL_Cache' ) ? Relod_WL_Cache::can_render_personal_state() : false;
		$count    = $personal ? count( Relod_Wishlist::get_ids() ) : 0;

		$badge_classes = [ 'relod-wlsc-header-icon__count' ];
		if ( ! $personal ) {
			$badge_classes[] = 'is-loading';
		}
		if ( '1' === $settings['hide_when_empty'] ) {
			$badge_classes[] = 'relod-wlsc-hide-empty';
			if ( 0 === $count ) {
				$badge_classes[] = 'is-empty';
			}
		}

		$style = sprintf(
			'--relod-wlsc-icon-size:%1$dpx;--relod-wlsc-badge-size:%2$dpx;--relod-wlsc-badge-font-size:%3$dpx;--relod-wlsc-icon-color:%4$s;--relod-wlsc-icon-hover:%5$s;--relod-wlsc-badge-bg:%6$s;--relod-wlsc-badge-text:%7$s;',
			(int) $settings['icon_size'],
			(int) $settings['badge_size'],
			(int) $settings['badge_font_size'],
			$settings['icon_color'],
			$settings['icon_hover_color'],
			$settings['badge_bg'],
			$settings['badge_text']
		);

		$target = '1' === $settings['open_new_tab'] ? ' target="_blank" rel="noopener noreferrer"' : '';
		$tag    = $link_url ? 'a' : 'span';

		ob_start();
		?>
		<<?php echo esc_html( $tag ); ?>
			class="relod-wlsc-header-icon"
			<?php echo 'a' === $tag ? 'href="' . esc_url( $link_url ) . '"' : ''; ?>
			<?php echo $target; // phpcs:ignore WordPress.Security.EscapeOutput ?>
			style="<?php echo esc_attr( $style ); ?>"
			aria-label="<?php echo esc_attr__( 'Избранное', 'relod-wishlist-shortcodes' ); ?>">
			<span class="relod-wlsc-header-icon__svg" aria-hidden="true"><?php echo $this->get_header_svg_inline(); // phpcs:ignore WordPress.Security.EscapeOutput ?></span>
			<span class="<?php echo esc_attr( implode( ' ', $badge_classes ) ); ?>"
				data-relod-wlsc-count
				data-relod-wl-count><?php echo esc_html( (string) $count ); ?></span>
		</<?php echo esc_html( $tag ); ?>>
		<?php
		return (string) ob_get_clean();
	}

	private function admin_only_message( string $message ): string {
		if ( current_user_can( 'manage_options' ) ) {
			return '<div class="relod-wlsc-admin-note">' . esc_html( $message ) . '</div>';
		}
		return '';
	}

	private function get_header_svg_inline(): string {
		static $cache = null;

		if ( null !== $cache ) {
			return $cache;
		}

		$path = RELOD_WLSC_PATH . 'assets/svg/header-icon.svg';
		if ( ! file_exists( $path ) ) {
			$cache = '';
			return $cache;
		}

		$svg = file_get_contents( $path ); // phpcs:ignore WordPress.WP.AlternativeFunctions
		if ( ! is_string( $svg ) || '' === $svg ) {
			$cache = '';
			return $cache;
		}

		$svg   = str_replace( '#000000', 'currentColor', $svg );
		$svg   = (string) preg_replace( '/<\?xml.*?\?>/i', '', $svg );
		$svg   = (string) preg_replace( '/<!--.*?-->/s', '', $svg );
		$cache = $svg;

		return $cache;
	}
}

RELOD_Wishlist_Shortcodes::instance();
