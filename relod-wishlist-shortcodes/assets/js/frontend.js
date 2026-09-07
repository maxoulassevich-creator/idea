/**
 * RELOD Wishlist Shortcodes — счётчик в шапке. v1.2.0
 *
 * Что изменилось:
 *  • Больше не читаем число из wp_localize_script: оно приезжало из кеша
 *    WP Rocket и показывало чужое избранное («в шапке 7, на странице 5»).
 *  • Ушли от jQuery.ajaxSuccess с поиском подстроки в теле запроса: ядро
 *    само сообщает актуальное состояние событием relod_wl:changed.
 *
 * Кнопки и счётчики синхронизирует relod-wishlist.js. Здесь остаётся только
 * подстраховка на случай, если разметка появилась позже (Elementor popup,
 * мега-меню, отложенная подгрузка хедера).
 */
(function (w, d) {
	'use strict';

	var COUNT_SELECTOR = '[data-relod-wl-count],[data-relod-wlsc-count]';

	function render(count) {
		var safe  = parseInt(count, 10);
		if (isNaN(safe) || safe < 0) {
			safe = 0;
		}

		var nodes = d.querySelectorAll(COUNT_SELECTOR);
		for (var i = 0; i < nodes.length; i++) {
			nodes[i].textContent = String(safe);
			nodes[i].classList.remove('is-loading');
			if (safe > 0) {
				nodes[i].classList.remove('is-empty');
			} else {
				nodes[i].classList.add('is-empty');
			}
		}
	}

	function currentCount() {
		if (w.RelodWL && typeof w.RelodWL.getCount === 'function') {
			return w.RelodWL.getCount();
		}
		return null;
	}

	d.addEventListener('relod_wl:changed', function (e) {
		if (e && e.detail && typeof e.detail.count !== 'undefined') {
			render(e.detail.count);
		}
	});

	/* Разметка счётчика могла появиться после гидрации ядра. */
	if ('MutationObserver' in w) {
		var observer = new MutationObserver(function (mutations) {
			for (var i = 0; i < mutations.length; i++) {
				var added = mutations[i].addedNodes;
				for (var j = 0; j < added.length; j++) {
					var node = added[j];
					if (node.nodeType !== 1) {
						continue;
					}
					var matches = node.matches && node.matches(COUNT_SELECTOR);
					if (matches || (node.querySelector && node.querySelector(COUNT_SELECTOR))) {
						var count = currentCount();
						if (count !== null) {
							render(count);
						}
						return;
					}
				}
			}
		});

		d.addEventListener('DOMContentLoaded', function () {
			observer.observe(d.body, { childList: true, subtree: true });
		});
	}
})(window, document);
