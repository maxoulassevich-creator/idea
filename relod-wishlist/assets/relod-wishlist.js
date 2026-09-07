/**
 * RELOD Wishlist — Front-end JS  v1.2.0
 *
 * Отличия от 1.0.0 и зачем они:
 *
 *  1. Никакого jQuery. WP Rocket с «Delay JavaScript execution» откладывает
 *     jquery-core до первого взаимодействия, и обработчик клика к моменту
 *     нажатия ещё не существовал: первый клик пропадал, пользователь жал
 *     второй раз — уходило два тоггла подряд и счётчик расходился со списком.
 *
 *  2. Гидрация. Страница может прийти из кеша WP Rocket с чужим или
 *     устаревшим состоянием, поэтому реальное избранное всегда забирается
 *     из некешируемого relod_wl_state и раскладывается по кнопкам и счётчикам.
 *
 *  3. Идемпотентные операции. На сервер уходит явное add/remove, а не
 *     «переверни как есть». Раньше клик «добавить» на закешированной странице
 *     приводил к УДАЛЕНИЮ товара: сервер видел его в списке и снимал метку.
 *
 *  4. Состояние после ответа берётся из ответа сервера, а не из локальных
 *     догадок. Если запись не удалась, UI честно откатывается.
 *
 *  5. Страница избранного сверяется со списком и при расхождении
 *     перерисовывается с сервера — «7 в счётчике, 5 на странице» больше нет.
 */
;(function (w, d) {
	'use strict';

	var cfg = w.relodWL || null;
	if (!cfg || !cfg.ajax) {
		return;
	}

	var BTN_SELECTOR   = '.relod-wcwl-btn';
	var COUNT_SELECTOR = '[data-relod-wl-count],[data-relod-wlsc-count]';
	var SCRUB_SELECTOR = '.relod-wl-card__image[data-ue-gallery]';
	var CHANGE_EVENT   = 'relod_wl:changed';

	var state = {
		ids:       normalizeIds(cfg.ids),
		count:     0,
		nonce:     cfg.nonce || '',
		hydrated:  !!cfg.fresh,
		hydrating: false
	};
	state.count = state.ids.length;

	var pending  = {};
	var pageBusy = false;
	var hasScrub = false;

	/* ═══════════════════════════════════════
	   Утилиты
	   ═══════════════════════════════════════ */

	function normalizeIds(list) {
		var out = [];
		if (!list || !list.length) {
			return out;
		}
		for (var i = 0; i < list.length; i++) {
			var id = parseInt(list[i], 10);
			if (id > 0 && out.indexOf(id) === -1) {
				out.push(id);
			}
		}
		return out;
	}

	function sameIds(a, b) {
		if (a.length !== b.length) {
			return false;
		}
		var x = a.slice().sort(numeric);
		var y = b.slice().sort(numeric);
		for (var i = 0; i < x.length; i++) {
			if (x[i] !== y[i]) {
				return false;
			}
		}
		return true;
	}

	function numeric(a, b) {
		return a - b;
	}

	function has(pid) {
		return state.ids.indexOf(parseInt(pid, 10)) !== -1;
	}

	function closest(el, selector) {
		if (!el) {
			return null;
		}
		if (el.closest) {
			return el.closest(selector);
		}
		while (el && el.nodeType === 1) {
			if (el.matches ? el.matches(selector) : false) {
				return el;
			}
			el = el.parentNode;
		}
		return null;
	}

	function post(action, params, done) {
		var xhr = new XMLHttpRequest();

		xhr.open('POST', cfg.ajax, true);
		xhr.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded; charset=UTF-8');
		xhr.setRequestHeader('X-Requested-With', 'XMLHttpRequest');

		xhr.onreadystatechange = function () {
			if (xhr.readyState !== 4) {
				return;
			}
			var json = null;
			try {
				json = JSON.parse(xhr.responseText);
			} catch (e) {
				json = null;
			}
			done(json, xhr.status);
		};

		var body = 'action=' + encodeURIComponent(action);
		for (var key in params) {
			if (Object.prototype.hasOwnProperty.call(params, key)) {
				body += '&' + encodeURIComponent(key) + '=' + encodeURIComponent(params[key]);
			}
		}

		xhr.send(body);
	}

	/**
	 * Принимает состояние, пришедшее с сервера. Сервер — единственный
	 * источник правды; локальные догадки живут только до его ответа.
	 */
	function adopt(data) {
		if (!data) {
			return;
		}
		if (data.ids) {
			state.ids = normalizeIds(data.ids);
		}
		state.count = typeof data.count === 'number' ? data.count : state.ids.length;
		if (data.nonce) {
			state.nonce = data.nonce;
		}
	}

	function setLocal(pid, active) {
		pid = parseInt(pid, 10);
		var idx = state.ids.indexOf(pid);

		if (active && idx === -1) {
			state.ids.push(pid);
		} else if (!active && idx !== -1) {
			state.ids.splice(idx, 1);
		}

		state.count = state.ids.length;
	}

	function emit(action, pid, extra) {
		var detail = {
			action:    action,
			productId: parseInt(pid, 10) || 0,
			ids:       state.ids.slice(),
			count:     state.count
		};

		if (extra) {
			for (var key in extra) {
				if (Object.prototype.hasOwnProperty.call(extra, key)) {
					detail[key] = extra[key];
				}
			}
		}

		var event;
		try {
			event = new CustomEvent(CHANGE_EVENT, { detail: detail, bubbles: true });
		} catch (e) {
			event = d.createEvent('CustomEvent');
			event.initCustomEvent(CHANGE_EVENT, true, false, detail);
		}
		d.dispatchEvent(event);

		/* Совместимость со сторонним кодом на jQuery. */
		if (w.jQuery) {
			w.jQuery(d).trigger(CHANGE_EVENT, [detail]);
		}
	}

	/* ═══════════════════════════════════════
	   Отрисовка состояния
	   ═══════════════════════════════════════ */

	function syncButtons() {
		var buttons = d.querySelectorAll(BTN_SELECTOR);

		for (var i = 0; i < buttons.length; i++) {
			var btn    = buttons[i];
			var pid    = parseInt(btn.getAttribute('data-product-id'), 10);
			var active = pid > 0 && has(pid);

			btn.classList.remove('relod-wl-unresolved');
			if (!pending[pid]) {
				btn.classList.remove('relod-wl-busy');
			}

			if (active) {
				btn.classList.add('is-active');
				btn.setAttribute('aria-pressed', 'true');
			} else {
				btn.classList.remove('is-active');
				btn.classList.remove('active');
				btn.setAttribute('aria-pressed', 'false');
			}
		}
	}

	function syncCounters() {
		var nodes = d.querySelectorAll(COUNT_SELECTOR);

		for (var i = 0; i < nodes.length; i++) {
			var node = nodes[i];
			node.textContent = String(state.count);

			/* Пока состояние не подтверждено сервером, кружок остаётся
			   скрытым: иначе на закешированной странице мелькает «0». */
			if (state.hydrated) {
				node.classList.remove('is-loading');
			}

			if (state.count > 0) {
				node.classList.remove('is-empty');
			} else {
				node.classList.add('is-empty');
			}
		}
	}

	function applyState() {
		syncButtons();
		syncCounters();
		hasScrub = !!d.querySelector(SCRUB_SELECTOR);
	}

	/**
	 * Есть ли на странице хоть что-то, чему нужно актуальное состояние.
	 * Если нет — не дёргаем admin-ajax зря на каждой странице сайта.
	 */
	function hasUi() {
		return !!(
			d.querySelector(BTN_SELECTOR) ||
			d.querySelector(COUNT_SELECTOR) ||
			d.querySelector('.relod-wl-page[data-relod-wl-page]')
		);
	}

	/* ═══════════════════════════════════════
	   Гидрация
	   ═══════════════════════════════════════ */

	function hydrate(done) {
		if (state.hydrating) {
			return;
		}
		state.hydrating = true;

		post('relod_wl_state', {}, function (json) {
			state.hydrating = false;

			var ok = !!(json && json.success && json.data);
			if (ok) {
				adopt(json.data);
			}

			/* Даже при сбое считаем гидрацию завершённой: кнопки должны
			   работать, а не залипнуть в «загрузке» навсегда. */
			state.hydrated = true;

			applyState();
			syncPage();

			/* ok = состояние действительно подтверждено сервером.
			   Сателлиты ориентируются на этот флаг, прежде чем что-то
			   удалять из разметки. */
			emit('hydrated', 0, { ok: ok });

			if (done) {
				done();
			}
		});
	}

	/* ═══════════════════════════════════════
	   Страница избранного
	   ═══════════════════════════════════════ */

	/**
	 * Сверяет отрисованный список с реальным и, если они разошлись,
	 * забирает свежую разметку с сервера.
	 *
	 * Ровно этот случай и описан в баг-репорте: страница пришла из кеша
	 * WP Rocket, счётчик обновился по AJAX, а грид остался чужим.
	 */
	function syncPage() {
		var host = d.querySelector('.relod-wl-page[data-relod-wl-page]');
		if (!host || pageBusy) {
			return;
		}

		var options = {};
		try {
			options = JSON.parse(host.getAttribute('data-relod-wl-page')) || {};
		} catch (e) {
			options = {};
		}

		var cards  = host.querySelectorAll('.relod-wl-card[data-product-id]');
		var domIds = [];
		for (var i = 0; i < cards.length; i++) {
			domIds.push(parseInt(cards[i].getAttribute('data-product-id'), 10));
		}

		if (sameIds(domIds, state.ids)) {
			return;
		}

		pageBusy = true;

		post('relod_wl_page', {
			columns:    options.columns || 4,
			empty_text: options.emptyText || (cfg.i18n && cfg.i18n.empty) || ''
		}, function (json) {
			pageBusy = false;

			if (json && json.success && json.data && typeof json.data.html === 'string') {
				adopt(json.data);
				host.innerHTML = json.data.html;
				applyState();
			}
		});
	}

	function fadeOutCard(pid) {
		var card = d.querySelector('.relod-wl-card[data-product-id="' + pid + '"]');
		if (!card) {
			return;
		}

		card.classList.add('relod-wl-removing');
		w.setTimeout(function () {
			if (card.parentNode) {
				card.parentNode.removeChild(card);
			}
			syncPage();
		}, 350);
	}

	/* ═══════════════════════════════════════
	   Тоггл
	   ═══════════════════════════════════════ */

	function pulse(btn) {
		btn.classList.remove('relod-wl-pulse');
		void btn.offsetWidth;
		btn.classList.add('relod-wl-pulse');
		w.setTimeout(function () {
			btn.classList.remove('relod-wl-pulse');
		}, 300);
	}

	function send(op, pid, allowRetry) {
		post('relod_wl_toggle', {
			nonce:      state.nonce,
			product_id: pid,
			op:         op
		}, function (json) {
			var data = json && json.data ? json.data : null;

			if (json && json.success && data) {
				delete pending[pid];
				adopt(data);
				applyState();
				emit(data.action, pid);

				if (data.action === 'removed') {
					fadeOutCard(pid);
				} else {
					syncPage();
				}
				return;
			}

			/**
			 * Протухший nonce — штатная ситуация под кешем страниц: HTML живёт
			 * дольше, чем nonce. Берём свежий и повторяем ровно один раз.
			 */
			if (data && data.code === 'invalid_nonce' && allowRetry) {
				if (data.nonce) {
					state.nonce = data.nonce;
				}
				send(op, pid, false);
				return;
			}

			delete pending[pid];

			/* Откат: если сервер прислал состояние — берём его, иначе
			   возвращаем кнопку в положение до клика. */
			if (data && typeof data.count !== 'undefined') {
				adopt(data);
			} else {
				setLocal(pid, op !== 'add');
			}

			applyState();
			emit('failed', pid);
		});
	}

	function handleButton(btn) {
		var pid = parseInt(btn.getAttribute('data-product-id'), 10);

		if (!pid || pending[pid]) {
			return;
		}

		pending[pid] = true;

		/**
		 * Явная операция вместо «переверни». Если состояние на странице было
		 * из кеша и не совпадало с реальным, идемпотентный add/remove всё
		 * равно приведёт список к тому, чего хотел пользователь.
		 */
		var op = has(pid) ? 'remove' : 'add';

		setLocal(pid, op === 'add');
		applyState();
		pulse(btn);

		send(op, pid, true);
	}

	function onClick(e) {
		var btn = closest(e.target, BTN_SELECTOR);
		if (!btn) {
			return;
		}

		e.preventDefault();
		e.stopPropagation();

		handleButton(btn);
	}

	/* ═══════════════════════════════════════
	   Галерея при наведении
	   ═══════════════════════════════════════ */

	function scrubState(el) {
		if (el._relodScrub) {
			return el._relodScrub;
		}

		var raw = el.getAttribute('data-ue-gallery');
		if (!raw) {
			return null;
		}

		var urls;
		try {
			urls = JSON.parse(raw);
		} catch (e) {
			return null;
		}

		if (!urls || !urls.length || urls.length < 2) {
			return null;
		}

		var img = el.querySelector('img');
		if (!img) {
			return null;
		}

		urls = urls.slice(0, 4);
		el._relodScrub = { urls: urls, n: urls.length, img: img, idx: 0 };

		return el._relodScrub;
	}

	function scrubSet(s, idx) {
		if (!s) {
			return;
		}
		idx = Math.max(0, Math.min(idx, s.n - 1));
		if (idx === s.idx) {
			return;
		}
		s.idx     = idx;
		s.img.src = s.urls[idx];
	}

	function onScrubMove(e) {
		if (!hasScrub) {
			return;
		}

		var el = closest(e.target, SCRUB_SELECTOR);
		if (!el) {
			return;
		}

		var s = scrubState(el);
		if (!s) {
			return;
		}

		var rect = el.getBoundingClientRect();
		var p    = Math.min(Math.max((e.clientX - rect.left) / (rect.width || 1), 0), 0.999999);

		scrubSet(s, Math.floor(p * s.n));
	}

	function onScrubOut(e) {
		if (!hasScrub) {
			return;
		}

		var el = closest(e.target, SCRUB_SELECTOR);
		if (!el || (e.relatedTarget && el.contains(e.relatedTarget))) {
			return;
		}

		scrubSet(scrubState(el), 0);
	}

	/* ═══════════════════════════════════════
	   Старт
	   ═══════════════════════════════════════ */

	d.addEventListener('click', onClick, false);
	d.addEventListener('mousemove', onScrubMove, true);
	d.addEventListener('mouseout', onScrubOut, true);

	/* Возврат «назад» отдаёт страницу из bfcache — состояние там древнее. */
	w.addEventListener('pageshow', function (e) {
		if (e.persisted) {
			state.hydrated = false;
			hydrate();
		}
	});

	function boot() {
		applyState();

		/* Разбираем клики, пойманные инлайн-перехватчиком до загрузки скрипта. */
		w.relodWLReady = true;

		if (w.relodWLEarly) {
			w.relodWLEarly.off();

			var queued = w.relodWLEarly.queue || [];
			for (var i = 0; i < queued.length; i++) {
				queued[i].classList.remove('relod-wl-busy');
				handleButton(queued[i]);
			}
			queued.length = 0;
		}

		/**
		 * cfg.fresh = true означает, что страница гарантированно не уйдёт в
		 * кеш (залогиненный пользователь или страница избранного), состояние
		 * в HTML настоящее — лишний запрос не нужен.
		 */
		if (!state.hydrated && hasUi()) {
			hydrate();
		} else if (state.hydrated) {
			syncPage();
		}
	}

	if (d.readyState === 'loading') {
		d.addEventListener('DOMContentLoaded', boot);
	} else {
		boot();
	}

	/* ═══════════════════════════════════════
	   Публичный API
	   ═══════════════════════════════════════ */

	w.RelodWL = {
		getIds:  function () { return state.ids.slice(); },
		getCount: function () { return state.count; },
		has:     function (pid) { return has(pid); },
		add:     function (pid) { if (!has(pid)) { pending[pid] = true; setLocal(pid, true); applyState(); send('add', parseInt(pid, 10), true); } },
		remove:  function (pid) { if (has(pid)) { pending[pid] = true; setLocal(pid, false); applyState(); send('remove', parseInt(pid, 10), true); } },
		refresh: function () { state.hydrating = false; hydrate(); },
		event:   CHANGE_EVENT
	};

})(window, document);
