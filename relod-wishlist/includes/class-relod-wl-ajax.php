<?php
/**
 * RELOD Wishlist — AJAX-эндпоинты.
 *
 * Что изменилось по сравнению с 1.0.0:
 *  • relod_wl_state — новая точка гидрации. Страница может прийти из кеша
 *    WP Rocket с чужим (или устаревшим) состоянием, поэтому реальное
 *    избранное JS всегда забирает отсюда.
 *  • relod_wl_toggle принимает явную операцию add/remove вместо «переверни».
 *    Операция идемпотентна: даже если фронт ошибся в исходном состоянии,
 *    сервер не сделает обратное действие. Раньше клик «добавить» на
 *    закешированной странице приводил к удалению товара.
 *  • Битый nonce больше не убивает запрос через die('-1'): отдаём JSON,
 *    фронт берёт свежий nonce и повторяет.
 *  • Ответ отдаёт состояние, реально прочитанное из хранилища. Если запись
 *    не удалась — это ошибка, а не success. Фантомных «+1 к счётчику» нет.
 *
 * @package relod-wishlist
 */

defined( 'ABSPATH' ) || exit;

final class Relod_WL_Ajax {

	const NONCE_ACTION = 'relod_wl';

	private static ?self $instance = null;

	public static function instance(): self {
		if ( null === self::$instance ) {
			self::$instance = new self();
		}
		return self::$instance;
	}

	private function __construct() {
		$actions = [
			'relod_wl_state'  => 'state',
			'relod_wl_toggle' => 'toggle',
			'relod_wl_page'   => 'page',
		];

		foreach ( $actions as $action => $method ) {
			add_action( 'wp_ajax_' . $action, [ $this, $method ] );
			add_action( 'wp_ajax_nopriv_' . $action, [ $this, $method ] );
		}
	}

	/* ────────────────────────────────────────
	   Общее
	   ──────────────────────────────────────── */

	/**
	 * Ответы эндпоинтов персональные — их не должен кешировать ни один слой.
	 *
	 * HTTP-коды ошибок здесь намеренно не выставляются: 403 и 500 от
	 * admin-ajax.php охотно перехватывают WAF, CDN и «страницы ошибок»
	 * хостинга, и до фронта доезжает HTML вместо JSON. Признак ошибки —
	 * success:false в теле.
	 */
	private function send_headers(): void {
		if ( headers_sent() ) {
			return;
		}

		nocache_headers();
		header( 'Cache-Control: no-store, no-cache, must-revalidate, max-age=0, private' );
		header( 'Vary: Cookie' );
		header( 'X-Robots-Tag: noindex' );
	}

	private function payload( array $extra = [] ): array {
		$ids = Relod_Wishlist::get_ids();

		return array_merge(
			[
				'ids'   => $ids,
				'count' => count( $ids ),
				'nonce' => wp_create_nonce( self::NONCE_ACTION ),
			],
			$extra
		);
	}

	/**
	 * Убирает из хранилища товары, которых больше нет в каталоге.
	 *
	 * Делается только в записывающих контекстах, чтобы обычный рендер
	 * страницы не порождал запись в БД.
	 */
	private function persist_pruned(): void {
		$raw     = Relod_WL_Store::get_raw_ids();
		$pruned  = Relod_WL_Store::get_ids();

		if ( $raw !== $pruned ) {
			Relod_WL_Store::save_ids( $pruned );
		}
	}

	/* ────────────────────────────────────────
	   relod_wl_state — гидрация
	   ──────────────────────────────────────── */

	public function state(): void {
		$this->send_headers();

		/* Создаём гостевой ключ заранее, до первого клика: так два
		   одновременных запроса не создадут два разных ключа. */
		if ( ! is_user_logged_in() ) {
			Relod_WL_Store::guest_key( true );
		}

		$this->persist_pruned();

		wp_send_json_success( $this->payload() );
	}

	/* ────────────────────────────────────────
	   relod_wl_toggle — изменение
	   ──────────────────────────────────────── */

	public function toggle(): void {
		$this->send_headers();

		$nonce = isset( $_POST['nonce'] ) ? sanitize_text_field( wp_unslash( $_POST['nonce'] ) ) : '';

		if ( ! wp_verify_nonce( $nonce, self::NONCE_ACTION ) ) {
			/**
			 * Nonce «протухает» вместе с закешированной страницей: WP Rocket
			 * может отдавать HTML сутками, а срок жизни nonce — 12 часов.
			 * Раньше здесь запрос умирал с телом «-1», фронт молча откатывал
			 * кнопку — и получалось «иногда избранное не работает».
			 */
			wp_send_json_error( $this->payload( [ 'code' => 'invalid_nonce' ] ) );
		}

		$product_id = isset( $_POST['product_id'] ) ? absint( $_POST['product_id'] ) : 0;

		if ( ! $product_id || ! function_exists( 'wc_get_product' ) || ! wc_get_product( $product_id ) ) {
			wp_send_json_error( $this->payload( [ 'code' => 'invalid_product' ] ) );
		}

		$op = isset( $_POST['op'] ) ? sanitize_key( wp_unslash( $_POST['op'] ) ) : 'toggle';
		if ( ! in_array( $op, [ 'add', 'remove', 'toggle' ], true ) ) {
			$op = 'toggle';
		}

		if ( ! is_user_logged_in() ) {
			Relod_WL_Store::guest_key( true );
		}

		$result = Relod_WL_Store::mutate( $product_id, $op );

		if ( empty( $result['ok'] ) ) {
			/* Запись не прошла — честно сообщаем об этом, чтобы фронт откатил UI. */
			wp_send_json_error(
				$this->payload(
					[
						'code'       => 'store_failed',
						'product_id' => $product_id,
					]
				)
			);
		}

		do_action( 'relod_wl_changed', $product_id, $result['action'], $result['ids'] );

		wp_send_json_success(
			[
				'action'     => $result['action'],
				'product_id' => $product_id,
				'ids'        => $result['ids'],
				'count'      => (int) $result['count'],
				'nonce'      => wp_create_nonce( self::NONCE_ACTION ),
			]
		);
	}

	/* ────────────────────────────────────────
	   relod_wl_page — перерисовка страницы избранного
	   ──────────────────────────────────────── */

	/**
	 * Отдаёт актуальную разметку грида избранного.
	 *
	 * Нужна, когда страница пришла из кеша: HTML в ней чужой или устаревший,
	 * и его надо заменить на настоящий список текущего посетителя.
	 */
	public function page(): void {
		$this->send_headers();

		if ( ! class_exists( 'Relod_Wishlist' ) ) {
			wp_send_json_error( [ 'code' => 'unavailable' ] );
		}

		$atts = [
			'columns'    => isset( $_POST['columns'] ) ? absint( $_POST['columns'] ) : 4,
			'empty_text' => isset( $_POST['empty_text'] ) ? sanitize_text_field( wp_unslash( $_POST['empty_text'] ) ) : 'В избранном пока пусто.',
		];

		$this->persist_pruned();

		$html = Relod_Wishlist::instance()->render_page_inner( $atts );

		wp_send_json_success( $this->payload( [ 'html' => $html ] ) );
	}
}
