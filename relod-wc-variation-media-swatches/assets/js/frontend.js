(function ($) {
  'use strict';

  function conf() {
    return window.relodWcvms || { selectors: {}, historyMode: 'replace' };
  }

  function safeParseJSON(raw, fallback) {
    try {
      return JSON.parse(raw);
    } catch (e) {
      return fallback;
    }
  }

  function boolAttr(value) {
    return String(value || '0') === '1' || String(value || '').toLowerCase() === 'yes' || value === true;
  }

  function getSliderInstanceId(el) {
    return el ? String(el.getAttribute('data-relod-instance') || '').trim() : '';
  }

  function collectShortcodeGalleryInstances() {
    var map = {};
    document.querySelectorAll('[data-relod-shortcode-gallery]').forEach(function (el) {
      var instanceId = getSliderInstanceId(el);
      if (!instanceId || map[instanceId]) { return; }
      map[instanceId] = {
        instance_id: instanceId,
        video_fit: el.getAttribute('data-video-fit') || '',
        arrows: boolAttr(el.getAttribute('data-arrows')) ? 1 : 0
      };
    });
    return Object.keys(map).map(function (key) { return map[key]; });
  }

  function collectSliderInstances() {
    var map = {};
    document.querySelectorAll('[data-relod-slider]:not([data-relod-shortcode-gallery])').forEach(function (el) {
      var instanceId = getSliderInstanceId(el);
      if (!instanceId || map[instanceId]) { return; }
      map[instanceId] = {
        instance_id: instanceId,
        video_fit: el.getAttribute('data-video-fit') || '',
        arrows: boolAttr(el.getAttribute('data-arrows')) ? 1 : 0
      };
    });
    return Object.keys(map).map(function (key) { return map[key]; });
  }

  // ───────────────────────────────────────────────────────────────
  // Media helpers
  // ───────────────────────────────────────────────────────────────

  function resetMedia(slide) {
    if (!slide) { return; }
    slide.querySelectorAll('video').forEach(function (video) {
      try {
        video.pause();
        if (!video.classList.contains('relod-wcvms-thumb-video-tag')) {
          video.currentTime = 0;
        }
      } catch (e) {}
    });

    slide.querySelectorAll('iframe').forEach(function (frame) {
      var src = frame.getAttribute('src');
      if (src) {
        frame.setAttribute('src', src);
      }
    });
  }

  function runWhenBrowserIsIdle(callback, timeout) {
    if (window.requestIdleCallback) {
      window.requestIdleCallback(callback, { timeout: timeout || 2500 });
      return;
    }
    window.setTimeout(callback, timeout || 2500);
  }

  function prepareThumbVideo(video) {
    if (!video || video.dataset.relodPrepared === '1') { return; }
    video.dataset.relodPrepared = '1';
    video.setAttribute('preload', 'none');

    var seek = function () {
      try {
        if (video.readyState >= 2) {
          video.currentTime = 0.1;
        }
      } catch (e) {}
    };

    var loadThumbMetadata = function () {
      if (video.dataset.relodThumbLoaded === '1') { return; }
      video.dataset.relodThumbLoaded = '1';
      video.setAttribute('preload', 'metadata');
      try { video.load(); } catch (e) {}
    };

    video.addEventListener('loadeddata', seek, { once: true });
    video.addEventListener('loadedmetadata', seek, { once: true });
    video.addEventListener('mouseenter', loadThumbMetadata, { once: true });
    video.addEventListener('touchstart', loadThumbMetadata, { once: true, passive: true });

    runWhenBrowserIsIdle(loadThumbMetadata, 3000);
  }

  function attemptPlay(video) {
    if (!video) { return; }
    try { video.currentTime = 0; } catch (e) {}

    var promise = null;
    try { promise = video.play(); } catch (e) {}

    if (promise && typeof promise.catch === 'function') {
      promise.catch(function () {
        try {
          video.muted = true;
          video.play();
        } catch (e) {}
      });
    }
  }

  function playActiveVideo(slide) {
    if (!slide) { return; }
    attemptPlay(slide.querySelector('.relod-wcvms-video-tag'));
    attemptPlay(slide.querySelector('.relod-wcvms-video-bg-tag'));
  }

  function pauseActiveVideo(slide) {
    if (!slide) { return; }
    slide.querySelectorAll('video').forEach(function (video) {
      try { video.pause(); } catch (e) {}
    });
  }

  function getActiveGallerySlide(gallery) {
    return gallery ? gallery.querySelector('.relod-wcvms-slide.is-active') : null;
  }

  function getActiveSliderItem(wrap) {
    return wrap ? wrap.querySelector('.relod-wcvms-slider-item.is-active') : null;
  }

  function elementIsHovered(el) {
    if (!el || !el.matches) { return false; }
    try { return el.matches(':hover'); } catch (e) { return false; }
  }

  // ───────────────────────────────────────────────────────────────
  // Gallery with thumbnail strip
  // ───────────────────────────────────────────────────────────────

  function activateSlide(gallery, index) {
    if (!gallery) { return; }

    var pauseOthers = gallery.getAttribute('data-pause-others') === '1';
    var activeSlide = null;

    gallery.querySelectorAll('.relod-wcvms-slide').forEach(function (slide) {
      var isTarget = slide.getAttribute('data-index') === String(index);
      if (!isTarget && pauseOthers) {
        resetMedia(slide);
      }
      slide.classList.toggle('is-active', isTarget);
      if (isTarget) {
        activeSlide = slide;
      }
    });

    gallery.querySelectorAll('.relod-wcvms-thumb').forEach(function (thumb) {
      thumb.classList.toggle('is-active', thumb.getAttribute('data-index') === String(index));
    });

    syncMediaKindState(gallery, activeSlide ? activeSlide.getAttribute('data-kind') : '');

    if (elementIsHovered(gallery)) {
      playActiveVideo(activeSlide);
    }
  }

  function initGalleries(scope) {
    (scope || document).querySelectorAll('.relod-wcvms-gallery').forEach(function (gallery) {
      gallery.querySelectorAll('.relod-wcvms-thumb-video-tag').forEach(prepareThumbVideo);

      if (gallery.dataset.relodVideoHoverBound !== '1') {
        gallery.dataset.relodVideoHoverBound = '1';
        gallery.addEventListener('mouseenter', function () {
          playActiveVideo(getActiveGallerySlide(gallery));
        });
        gallery.addEventListener('mouseleave', function () {
          pauseActiveVideo(getActiveGallerySlide(gallery));
        });
      }

      var activeThumb = gallery.querySelector('.relod-wcvms-thumb.is-active');
      if (activeThumb) {
        activateSlide(gallery, activeThumb.getAttribute('data-index'));
      }

      ensureZoomHint(gallery);
    });
  }

  // ───────────────────────────────────────────────────────────────
  // Slider
  // ───────────────────────────────────────────────────────────────

  function getSliderItems(wrap) {
    return wrap ? wrap.querySelectorAll('.relod-wcvms-slider-item') : [];
  }

  function getActiveSliderIndex(wrap) {
    var items = getSliderItems(wrap);
    for (var i = 0; i < items.length; i++) {
      if (items[i].classList.contains('is-active')) {
        return i;
      }
    }
    return 0;
  }

  function updateSliderUiState(wrap) {
    if (!wrap) { return; }
    var total = getSliderItems(wrap).length;
    var current = getActiveSliderIndex(wrap);

    wrap.classList.toggle('relod-wcvms-slider--single', total < 2);

    wrap.querySelectorAll('.relod-wcvms-slider-arrow--prev').forEach(function (button) {
      button.disabled = total < 2 || current <= 0;
    });

    wrap.querySelectorAll('.relod-wcvms-slider-arrow--next').forEach(function (button) {
      button.disabled = total < 2 || current >= total - 1;
    });
  }

  function goToSliderSlide(wrap, index) {
    if (!wrap) { return; }

    var items = getSliderItems(wrap);
    var total = items.length;
    if (!total) {
      updateSliderUiState(wrap);
      return;
    }

    var normalized = Math.max(0, Math.min(total - 1, parseInt(index, 10) || 0));
    var activeItem = null;

    items.forEach(function (item, currentIndex) {
      var isTarget = currentIndex === normalized;
      if (!isTarget) {
        resetMedia(item);
      }
      item.classList.toggle('is-active', isTarget);
      if (isTarget) {
        activeItem = item;
      }
    });

    wrap.querySelectorAll('.relod-wcvms-slider-dot').forEach(function (dot) {
      dot.classList.toggle('is-active', parseInt(dot.getAttribute('data-index'), 10) === normalized);
    });

    updateSliderUiState(wrap);
    syncMediaKindState(wrap, activeItem ? activeItem.getAttribute('data-kind') : '');

    if (elementIsHovered(wrap)) {
      playActiveVideo(activeItem);
    }
  }

  function stopSliderAutoplay(wrap) {
    if (!wrap || !wrap._relodAutoplayTimer) { return; }
    window.clearInterval(wrap._relodAutoplayTimer);
    wrap._relodAutoplayTimer = null;
  }

  function startSliderAutoplay(wrap) {
    if (!wrap) { return; }
    stopSliderAutoplay(wrap);

    var total = getSliderItems(wrap).length;
    if (total < 2 || !boolAttr(wrap.getAttribute('data-autoplay'))) { return; }

    var delay = parseInt(wrap.getAttribute('data-autoplay-ms'), 10) || 10000;
    wrap._relodAutoplayTimer = window.setInterval(function () {
      var current = getActiveSliderIndex(wrap);
      if (current >= total - 1) {
        stopSliderAutoplay(wrap);
        return;
      }
      goToSliderSlide(wrap, current + 1);
    }, Math.max(1000, delay));
  }

  function restartSliderAutoplay(wrap) {
    if (!wrap) { return; }
    startSliderAutoplay(wrap);
  }

  function preventNativeDrag(wrap) {
    if (!wrap) { return; }

    wrap.querySelectorAll('img, video').forEach(function (node) {
      node.setAttribute('draggable', 'false');
    });

    if (wrap.dataset.relodDragPrevented === '1') { return; }
    wrap.dataset.relodDragPrevented = '1';

    wrap.addEventListener('dragstart', function (e) {
      if (e.target && (e.target.tagName === 'IMG' || e.target.tagName === 'VIDEO')) {
        e.preventDefault();
      }
    });
  }

  function initSliderSwipe(wrap) {
    if (!wrap || wrap.dataset.relodSwipeInit === '1') { return; }
    wrap.dataset.relodSwipeInit = '1';
    wrap.style.touchAction = 'pan-y';

    var state = {
      pointerId: null,
      startX: 0,
      startY: 0,
      lastX: 0,
      isPointerDown: false,
      isSwiping: false
    };

    function resetState() {
      state.pointerId = null;
      state.startX = 0;
      state.startY = 0;
      state.lastX = 0;
      state.isPointerDown = false;
      state.isSwiping = false;
      wrap.classList.remove('is-dragging');
    }

    function finishSwipe() {
      if (!state.isPointerDown) {
        resetState();
        return;
      }

      var total = getSliderItems(wrap).length;
      var dx = state.lastX - state.startX;

      if (state.isSwiping) {
        wrap.dataset.relodSuppressClickUntil = String(Date.now() + 300);
      }

      if (state.isSwiping && total > 1 && Math.abs(dx) >= 45) {
        var current = getActiveSliderIndex(wrap);
        var next = dx < 0 ? Math.min(current + 1, total - 1) : Math.max(current - 1, 0);
        if (next !== current) {
          goToSliderSlide(wrap, next);
          restartSliderAutoplay(wrap);
        }
      }

      resetState();
    }

    wrap.addEventListener('pointerdown', function (e) {
      if (e.pointerType === 'mouse' && e.button !== 0) { return; }
      if (getSliderItems(wrap).length < 2) { return; }
      if (e.target.closest('.relod-wcvms-slider-dot, .relod-wcvms-slider-arrow, button, a, input, select, textarea, label')) {
        return;
      }

      state.pointerId = e.pointerId;
      state.startX = e.clientX;
      state.startY = e.clientY;
      state.lastX = e.clientX;
      state.isPointerDown = true;
      state.isSwiping = false;
      wrap.classList.add('is-dragging');

      try {
        if (wrap.setPointerCapture) {
          wrap.setPointerCapture(e.pointerId);
        }
      } catch (err) {}
    });

    wrap.addEventListener('pointermove', function (e) {
      if (!state.isPointerDown) { return; }
      if (state.pointerId !== null && e.pointerId !== state.pointerId) { return; }

      state.lastX = e.clientX;
      var dx = e.clientX - state.startX;
      var dy = e.clientY - state.startY;

      if (!state.isSwiping && Math.abs(dx) > 8 && Math.abs(dx) > Math.abs(dy)) {
        state.isSwiping = true;
      }

      if (state.isSwiping) {
        e.preventDefault();
      }
    }, { passive: false });

    wrap.addEventListener('pointerup', finishSwipe);
    wrap.addEventListener('pointercancel', finishSwipe);
    wrap.addEventListener('lostpointercapture', finishSwipe);
    wrap.addEventListener('pointerleave', function () {
      if (state.isPointerDown && state.isSwiping) {
        finishSwipe();
      }
    });
  }

  function initSliders(scope) {
    (scope || document).querySelectorAll('[data-relod-slider]').forEach(function (wrap) {
      wrap.querySelectorAll('.relod-wcvms-thumb-video-tag').forEach(prepareThumbVideo);
      preventNativeDrag(wrap);
      initSliderSwipe(wrap);

      if (wrap.dataset.relodAutoplayHoverBound !== '1') {
        wrap.dataset.relodAutoplayHoverBound = '1';
        wrap.addEventListener('mouseenter', function () {
          stopSliderAutoplay(wrap);
          playActiveVideo(getActiveSliderItem(wrap));
        });
        wrap.addEventListener('mouseleave', function () {
          pauseActiveVideo(getActiveSliderItem(wrap));
          startSliderAutoplay(wrap);
        });
      }

      var total = getSliderItems(wrap).length;
      if (total > 0 && !wrap.querySelector('.relod-wcvms-slider-item.is-active')) {
        goToSliderSlide(wrap, 0);
      } else {
        updateSliderUiState(wrap);
      }

      startSliderAutoplay(wrap);
      ensureZoomHint(wrap);
    });
  }

  // ───────────────────────────────────────────────────────────────
  // Variation form helpers
  // ───────────────────────────────────────────────────────────────



  function getSelectedFormAttributes($form) {
    var selected = {};
    if (!$form || !$form.length) { return selected; }

    $form.find('select[name^="attribute_"], input[name^="attribute_"]').each(function () {
      var name = this.getAttribute('name');
      if (!name) { return; }
      selected[name] = $(this).val() || '';
    });

    return selected;
  }

  function getSwatchMatrix() {
    var source = document.querySelector('[data-relod-variation-matrix]');
    if (!source) { return []; }
    return safeParseJSON(source.getAttribute('data-relod-variation-matrix') || '[]', []);
  }

  function variationMatchesSelection(variationAttrs, selection, ignoreAttribute) {
    var keys = Object.keys(selection || {});
    for (var i = 0; i < keys.length; i++) {
      var key = keys[i];
      if (key === ignoreAttribute || !selection[key]) { continue; }
      if (!variationAttrs || String(variationAttrs[key] || '') !== String(selection[key])) {
        return false;
      }
    }
    return true;
  }

  function getSwatchCompatibilityInfo(button, selected, matrix) {
    var attrName = button.getAttribute('data-attribute-name') || '';
    var attrValue = button.getAttribute('data-attribute-value') || '';

    if (!attrName || !attrValue || !matrix.length) {
      // Нет полной матрицы — не ломаем старое поведение и оставляем swatch доступным.
      return { exists: true, inStock: true };
    }

    var exists = false;
    var inStock = false;

    matrix.forEach(function (variation) {
      var attrs = variation && variation.attributes ? variation.attributes : {};
      if (String(attrs[attrName] || '') !== String(attrValue)) { return; }
      if (!variationMatchesSelection(attrs, selected, attrName)) { return; }

      exists = true;
      if (variation.is_in_stock === true || variation.is_in_stock === 1 || String(variation.is_in_stock) === '1') {
        inStock = true;
      }
    });

    return { exists: exists, inStock: inStock };
  }

  function updateSwatchesAvailability($form) {
    var selected = getSelectedFormAttributes($form || $('form.variations_form').first());
    var matrix = getSwatchMatrix();

    document.querySelectorAll('[data-relod-swatches] .relod-wcvms-swatch, [data-relod-size-swatches] .relod-wcvms-size-swatch').forEach(function (button) {
      var info = getSwatchCompatibilityInfo(button, selected, matrix);
      button.classList.toggle('is-relod-unavailable', !info.exists);
      button.classList.toggle('is-relod-out-of-stock-for-selection', !!info.exists && !info.inStock);

      // Out-of-stock сочетания должны оставаться кликабельными для заявки
      // «сообщить о поступлении». Поэтому физически кнопку не disabled.
      button.disabled = false;
      button.setAttribute('aria-disabled', info.exists ? 'false' : 'true');
    });
  }

  function syncSwatchesWithForm($form) {
    var selected = getSelectedFormAttributes($form || $('form.variations_form').first());

    document.querySelectorAll('[data-relod-swatches] .relod-wcvms-swatch, [data-relod-size-swatches] .relod-wcvms-size-swatch').forEach(function (button) {
      var attrName = button.getAttribute('data-attribute-name') || '';
      var attrValue = button.getAttribute('data-attribute-value') || '';
      var isActive = !!attrName && !!attrValue && String(selected[attrName] || '') === String(attrValue);
      button.classList.toggle('is-active', isActive);
    });
  }

  function getButtonVariationAttributes(button) {
    if (!button) { return {}; }
    return safeParseJSON(button.getAttribute('data-variation-attributes') || '{}', {});
  }

  function inferButtonAttributePayload(button) {
    var attrs = getButtonVariationAttributes(button);
    var keys = Object.keys(attrs).filter(function (key) { return !!attrs[key]; });
    if (!keys.length) { return {}; }
    if (keys.length === 1) {
      var only = {};
      only[keys[0]] = attrs[keys[0]];
      return only;
    }

    var container = button.closest('[data-relod-swatches], [data-relod-size-swatches]');
    var valueMap = {};

    if (container) {
      container.querySelectorAll('.relod-wcvms-swatch, .relod-wcvms-size-swatch').forEach(function (sibling) {
        var siblingAttrs = getButtonVariationAttributes(sibling);
        Object.keys(siblingAttrs).forEach(function (key) {
          if (!siblingAttrs[key]) { return; }
          if (!valueMap[key]) { valueMap[key] = {}; }
          valueMap[key][String(siblingAttrs[key])] = true;
        });
      });
    }

    var bestKey = keys[0];
    var bestScore = -1;
    keys.forEach(function (key) {
      var score = valueMap[key] ? Object.keys(valueMap[key]).length : 0;
      if (score > bestScore) {
        bestScore = score;
        bestKey = key;
      }
    });

    var payload = {};
    payload[bestKey] = attrs[bestKey];
    return payload;
  }

  function unlockVariationSelectOptions($form) {
    if (!$form || !$form.length) { return; }

    $form.find('select[name^="attribute_"]').each(function () {
      $(this).find('option').each(function () {
        if (!this.value) { return; }
        this.disabled = false;
        this.hidden = false;
        this.style.display = '';
        $(this).removeAttr('disabled').removeAttr('hidden');
      });
    });
  }

  function fieldHasValue($field, value) {
    var wanted = String(value || '');
    if (!wanted) { return true; }

    if ($field.is('select')) {
      var exists = false;
      $field.find('option').each(function () {
        if (String($(this).val() || '') === wanted) {
          exists = true;
        }
      });
      return exists;
    }

    return true;
  }

  function ensureFieldValueExists($field, value) {
    var wanted = String(value || '');
    if (!$field.length || !wanted || fieldHasValue($field, wanted)) { return; }

    if ($field.is('select')) {
      $field.append($('<option></option>').val(wanted).text(wanted));
    }
  }

  function restoreDesiredSelection($form, desired) {
    var changed = false;
    if (!$form.length || !desired) { return false; }

    unlockVariationSelectOptions($form);

    Object.keys(desired).forEach(function (key) {
      var value = desired[key];
      if (!value) { return; }

      var $field = $form.find('[name="' + key + '"]');
      if (!$field.length) { return; }

      ensureFieldValueExists($field, value);
      if (String($field.val() || '') !== String(value)) {
        $field.val(value);
        changed = true;
      }
    });

    return changed;
  }

  function setVariationFormAttributes($form, attrs) {
    if (!$form.length || !attrs) { return; }

    var desired = $.extend({}, getSelectedFormAttributes($form), attrs);
    var changedKeys = Object.keys(attrs);
    var primaryKey = changedKeys.length ? changedKeys[0] : Object.keys(desired)[0];

    restoreDesiredSelection($form, desired);

    if (primaryKey) {
      $form.find('[name="' + primaryKey + '"]').trigger('change');
    } else {
      $form.trigger('check_variations');
    }

    [0, 30, 120].forEach(function (delay) {
      window.setTimeout(function () {
        var restored = restoreDesiredSelection($form, desired);
        if (restored) {
          $form.trigger('check_variations');
        }
        syncSwatchesWithForm($form);
        updateSwatchesAvailability($form);
      }, delay);
    });
  }

  function replaceHTML(selector, html) {
    if (!selector || typeof html !== 'string') { return; }
    $(selector).each(function () {
      $(this).html(html);
    });
  }

  function replaceText(selector, text) {
    if (!selector || typeof text !== 'string') { return; }
    $(selector).each(function () {
      $(this).text(text);
    });
  }

  // ───────────────────────────────────────────────────────────────
  // Apply payload
  // ───────────────────────────────────────────────────────────────

  function applyPayload(payload) {
    if (!payload) { return; }
    var sel = conf().selectors || {};

    if (payload.gallery_html) {
      $(sel.gallery).first().replaceWith(payload.gallery_html);
      initGalleries(document);
    }

    if (payload.shortcode_gallery_inner_map && typeof payload.shortcode_gallery_inner_map === 'object') {
      $(sel.shortcodeGallery || '[data-relod-shortcode-gallery]').each(function () {
        var wrap = this;
        var instanceId = getSliderInstanceId(wrap);
        var html = payload.shortcode_gallery_inner_map[instanceId] || '';
        if (!html) { return; }
        stopSliderAutoplay(wrap);
        wrap.innerHTML = html;
        preventNativeDrag(wrap);
        goToSliderSlide(wrap, 0);
        startSliderAutoplay(wrap);
        ensureZoomHint(wrap);
      });
    }

    if (typeof payload.main_image_html === 'string') {
      $(sel.mainImage || '[data-relod-main-image]').each(function () {
        $(this).html(payload.main_image_html);
      });
    }

    if (payload.slider_inner_html_map && typeof payload.slider_inner_html_map === 'object') {
      $(sel.slider || '[data-relod-slider]').not('[data-relod-shortcode-gallery]').each(function () {
        var wrap = this;
        var instanceId = getSliderInstanceId(wrap);
        var html = payload.slider_inner_html_map[instanceId] || '';
        if (!html) { return; }
        stopSliderAutoplay(wrap);
        wrap.innerHTML = html;
        preventNativeDrag(wrap);
        goToSliderSlide(wrap, 0);
        startSliderAutoplay(wrap);
        ensureZoomHint(wrap);
      });
    }

    if (typeof payload.price_html === 'string' && payload.price_html) {
      replaceHTML(sel.price, payload.price_html);
    }
    if (typeof payload.title === 'string' && payload.title) {
      replaceText(sel.title, payload.title);
    }
    if (typeof payload.short_description_html === 'string') {
      replaceHTML(sel.shortDesc, payload.short_description_html);
    }
    if (typeof payload.description_html === 'string') {
      replaceHTML(sel.description, payload.description_html);
    }
    if (typeof payload.additional_information_html === 'string' && payload.additional_information_html) {
      replaceHTML(sel.additional, payload.additional_information_html);
    }
    if (typeof payload.meta_html === 'string' && payload.meta_html) {
      replaceHTML(sel.meta, payload.meta_html);
    }
    if (typeof payload.stock_html === 'string' && payload.stock_html) {
      replaceHTML(sel.stock, payload.stock_html);
    }
    if (typeof payload.sku === 'string') {
      replaceText(sel.sku, payload.sku);
    }

    if (payload.url && conf().historyMode === 'replace' && window.history && window.history.replaceState) {
      window.history.replaceState({}, '', payload.url);
    }
  }

  // ───────────────────────────────────────────────────────────────
  // AJAX
  // ───────────────────────────────────────────────────────────────

  var activePayloadXhr = null;
  var activePayloadRequestId = 0;

  function requestPayload(productId, variationId) {
    activePayloadRequestId += 1;
    var requestId = activePayloadRequestId;

    if (activePayloadXhr && activePayloadXhr.readyState !== 4) {
      activePayloadXhr.abort();
    }

    activePayloadXhr = $.ajax({
      url: conf().ajaxUrl,
      type: 'POST',
      dataType: 'json',
      data: {
        action: 'relod_wcvms_variation_payload',
        nonce: conf().nonce,
        product_id: productId,
        variation_id: variationId,
        shortcode_gallery_instances: collectShortcodeGalleryInstances(),
        slider_instances: collectSliderInstances()
      }
    });

    activePayloadXhr._relodRequestId = requestId;
    return activePayloadXhr;
  }

  // ───────────────────────────────────────────────────────────────
  // Event bindings
  // ───────────────────────────────────────────────────────────────

  $(document)
    .on('click', '.relod-wcvms-thumb', function () {
      var gallery = this.closest('.relod-wcvms-gallery');
      if (gallery) {
        activateSlide(gallery, this.getAttribute('data-index'));
      }
    })

    .on('click', '[data-relod-slider] .relod-wcvms-slider-dot', function () {
      var wrap = this.closest('[data-relod-slider]');
      if (!wrap) { return; }
      goToSliderSlide(wrap, this.getAttribute('data-index'));
      restartSliderAutoplay(wrap);
    })

    .on('click', '[data-relod-slider] .relod-wcvms-slider-arrow--prev', function () {
      var wrap = this.closest('[data-relod-slider]');
      if (!wrap) { return; }
      var current = getActiveSliderIndex(wrap);
      if (current <= 0) { return; }
      goToSliderSlide(wrap, current - 1);
      restartSliderAutoplay(wrap);
    })

    .on('click', '[data-relod-slider] .relod-wcvms-slider-arrow--next', function () {
      var wrap = this.closest('[data-relod-slider]');
      if (!wrap) { return; }
      var total = getSliderItems(wrap).length;
      var current = getActiveSliderIndex(wrap);
      if (total < 2 || current >= total - 1) { return; }
      goToSliderSlide(wrap, current + 1);
      restartSliderAutoplay(wrap);
    })

    .on('click', '[data-relod-swatches] .relod-wcvms-swatch, [data-relod-size-swatches] .relod-wcvms-size-swatch', function () {
      var $btn = $(this);
      if ($btn.hasClass('is-relod-unavailable')) { return; }

      var attrName = $btn.attr('data-attribute-name') || '';
      var attrValue = $btn.attr('data-attribute-value') || '';
      var $form = $('form.variations_form').first();

      if (!$form.length) { return; }

      if (attrName && attrValue) {
        // Меняем только атрибут выбранного swatch. Остальные выбранные значения
        // остаются в форме, а итоговую variation_id должен определить штатный WC.
        setVariationFormAttributes($form, (function () {
          var data = {};
          data[attrName] = attrValue;
          return data;
        })());
      } else {
        // Fallback для старой разметки без data-attribute-name/value.
        setVariationFormAttributes($form, inferButtonAttributePayload(this));
      }

      syncSwatchesWithForm($form);
      updateSwatchesAvailability($form);
    })

    .on('found_variation', 'form.variations_form', function (event, variation) {
      if (!variation || !variation.variation_id) { return; }

      var productId = parseInt($(this).data('product_id'), 10) ||
        parseInt($(this).find('input[name="product_id"]').val(), 10) ||
        conf().productId || 0;

      var xhr = requestPayload(productId, variation.variation_id);
      var requestId = xhr._relodRequestId;

      xhr.done(function (resp) {
        if (requestId !== activePayloadRequestId) { return; }
        if (resp && resp.success && resp.data) {
          applyPayload(resp.data);
        }
      });

      syncSwatchesWithForm($(this));
      updateSwatchesAvailability($(this));
    })

    .on('woocommerce_update_variation_values', 'form.variations_form', function () {
      unlockVariationSelectOptions($(this));
      syncSwatchesWithForm($(this));
      updateSwatchesAvailability($(this));
    })

    .on('woocommerce_variation_has_changed reset_data', 'form.variations_form', function () {
      unlockVariationSelectOptions($(this));
      syncSwatchesWithForm($(this));
      updateSwatchesAvailability($(this));
    })

    .on('click', '.relod-ue-vms-swatches a', function (e) {
      e.stopPropagation();
    });

  // ───────────────────────────────────────────────────────────────
  // Модальная галерея: перелистывание, миниатюры, зум (лупа)
  // ───────────────────────────────────────────────────────────────

  var LB_HOST_SELECTOR = '[data-relod-lightbox]';
  var SVG_NS = 'http://www.w3.org/2000/svg';

  function i18n(key, fallback) {
    var strings = conf().i18n || {};
    return strings[key] || fallback;
  }

  function lightboxConf() {
    var options = conf().lightbox || {};
    return {
      maxZoom: parseFloat(options.maxZoom) || 3.5,
      stepZoom: parseFloat(options.stepZoom) || 2.2,
      showThumbs: options.showThumbs !== false
    };
  }

  function prefersReducedMotion() {
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  }

  function isCoarsePointer() {
    return !!(window.matchMedia && window.matchMedia('(pointer: coarse)').matches);
  }

  function clamp(value, min, max) {
    return Math.min(max, Math.max(min, value));
  }

  // ── Источник данных модалки ─────────────────────────────────────

  function findDataScript(host) {
    if (!host) { return null; }
    for (var i = 0; i < host.children.length; i++) {
      var node = host.children[i];
      if (node.tagName === 'SCRIPT' && node.classList.contains('relod-wcvms-lightbox-data')) {
        return node;
      }
    }
    return null;
  }

  function getLightboxItems(host) {
    if (!host) { return []; }
    if (Array.isArray(host._relodLightboxItems)) {
      return host._relodLightboxItems;
    }

    var script = findDataScript(host);
    var items = script ? safeParseJSON(script.textContent || '[]', []) : [];
    if (!Array.isArray(items)) { items = []; }

    host._relodLightboxItems = items;
    return items;
  }

  function setLightboxItems(host, items) {
    if (!host) { return; }
    host._relodLightboxItems = Array.isArray(items) ? items : [];
  }

  function updateAllLightboxItems(items) {
    if (!Array.isArray(items)) { return; }
    document.querySelectorAll(LB_HOST_SELECTOR).forEach(function (host) {
      setLightboxItems(host, items);
    });
  }

  function getLightboxHost(target) {
    if (!target || !target.closest) { return null; }
    var host = target.closest(LB_HOST_SELECTOR);
    return host && getLightboxItems(host).length ? host : null;
  }

  /**
   * Индекс кликнутого медиа в общем наборе модалки.
   * Сопоставление идёт по data-relod-media-key, чтобы шорткоды с урезанным
   * набором (без основного изображения) открывали правильный слайд.
   */
  function findItemIndexByKey(items, key) {
    if (!key) { return -1; }
    for (var i = 0; i < items.length; i++) {
      if (items[i] && String(items[i].key) === String(key)) { return i; }
    }
    return -1;
  }

  function resolveMediaKey(element) {
    if (!element) { return ''; }
    var keyed = element.matches && element.matches('[data-relod-media-key]')
      ? element
      : element.querySelector('[data-relod-media-key]');
    return keyed ? keyed.getAttribute('data-relod-media-key') || '' : '';
  }

  function getActiveMediaElement(host) {
    if (!host) { return null; }

    if (host.matches('[data-relod-main-image]')) {
      return host.querySelector('.relod-wcvms-main-img');
    }

    return getActiveSliderItem(host) || getActiveGallerySlide(host);
  }

  // ── Иконки ──────────────────────────────────────────────────────

  function svgIcon(paths, extraClass) {
    var svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('focusable', 'false');
    if (extraClass) { svg.setAttribute('class', extraClass); }

    paths.forEach(function (d) {
      var path = document.createElementNS(SVG_NS, 'path');
      path.setAttribute('d', d);
      svg.appendChild(path);
    });

    return svg;
  }

  var ICONS = {
    close: ['M6 6l12 12', 'M18 6L6 18'],
    prev: ['M15 5l-7 7 7 7'],
    next: ['M9 5l7 7-7 7'],
    zoom: ['M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z', 'M20 20l-4.2-4.2', 'M8.5 11h5', 'M11 8.5v5'],
    zoomOut: ['M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z', 'M20 20l-4.2-4.2', 'M8.5 11h5'],
    expand: ['M4 4h16v16H4z', 'M4 9h16']
  };

  function makeButton(className, label, iconPaths) {
    var button = document.createElement('button');
    button.type = 'button';
    button.className = className;
    button.setAttribute('aria-label', label);
    button.title = label;
    button.appendChild(svgIcon(iconPaths));
    return button;
  }

  // ── Значок лупы на изображениях страницы ────────────────────────

  /**
   * Значок вешаем на визуальную область медиа: у классической галереи это
   * сцена, а не весь блок вместе с лентой миниатюр.
   */
  function getHintContainer(host) {
    return host.querySelector('.relod-wcvms-stage') || host;
  }

  function ensureZoomHint(host) {
    if (!host || !getLightboxItems(host).length) { return; }

    var container = getHintContainer(host);
    for (var i = 0; i < container.children.length; i++) {
      if (container.children[i].classList.contains('relod-wcvms-zoom-hint')) { return; }
    }

    container.appendChild(
      makeButton('relod-wcvms-zoom-hint', i18n('openGallery', 'Открыть изображение во весь экран'), ICONS.zoom)
    );
  }

  /**
   * Пока активен видео-слайд, лупа не показывается: зумить видео нечего.
   */
  function syncMediaKindState(host, kind) {
    if (!host || !host.classList) { return; }
    host.classList.toggle('relod-wcvms-video-active', kind === 'video');
  }

  function initLightboxHosts(scope) {
    (scope || document).querySelectorAll(LB_HOST_SELECTOR).forEach(function (host) {
      ensureZoomHint(host);
    });
  }

  // ───────────────────────────────────────────────────────────────
  // Экземпляр модального окна
  // ───────────────────────────────────────────────────────────────

  var lb = null;

  function buildLightbox() {
    var root = document.createElement('div');
    root.className = 'relod-wcvms-lb';
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.setAttribute('aria-label', i18n('gallery', 'Галерея товара'));
    root.tabIndex = -1;

    var counter = document.createElement('div');
    counter.className = 'relod-wcvms-lb__counter';
    counter.setAttribute('aria-live', 'polite');

    var tools = document.createElement('div');
    tools.className = 'relod-wcvms-lb__tools';

    var zoomBtn = makeButton('relod-wcvms-lb__btn relod-wcvms-lb__btn--zoom', i18n('zoomIn', 'Увеличить'), ICONS.zoom);
    zoomBtn.appendChild(svgIcon(ICONS.zoomOut, 'relod-wcvms-icon--out'));
    zoomBtn.firstChild.setAttribute('class', 'relod-wcvms-icon--in');
    var fullscreenBtn = makeButton('relod-wcvms-lb__btn relod-wcvms-lb__btn--fullscreen', i18n('fullscreen', 'Во весь экран'), ICONS.expand);
    var closeBtn = makeButton('relod-wcvms-lb__btn relod-wcvms-lb__btn--close', i18n('close', 'Закрыть'), ICONS.close);
    tools.appendChild(zoomBtn);
    tools.appendChild(fullscreenBtn);
    tools.appendChild(closeBtn);

    var prevBtn = makeButton('relod-wcvms-lb__nav relod-wcvms-lb__nav--prev', i18n('prev', 'Предыдущее изображение'), ICONS.prev);
    var nextBtn = makeButton('relod-wcvms-lb__nav relod-wcvms-lb__nav--next', i18n('next', 'Следующее изображение'), ICONS.next);

    var stage = document.createElement('div');
    stage.className = 'relod-wcvms-lb__stage';

    var track = document.createElement('div');
    track.className = 'relod-wcvms-lb__track';
    stage.appendChild(track);

    var thumbs = document.createElement('div');
    thumbs.className = 'relod-wcvms-lb__thumbs';

    root.appendChild(counter);
    root.appendChild(tools);
    root.appendChild(prevBtn);
    root.appendChild(nextBtn);
    root.appendChild(stage);
    root.appendChild(thumbs);

    return {
      root: root,
      counter: counter,
      zoomBtn: zoomBtn,
      fullscreenBtn: fullscreenBtn,
      closeBtn: closeBtn,
      prevBtn: prevBtn,
      nextBtn: nextBtn,
      stage: stage,
      track: track,
      thumbs: thumbs,
      items: [],
      slides: [],
      index: 0,
      zoom: { scale: 1, tx: 0, ty: 0 },
      lastFocus: null,
      scrollY: 0
    };
  }

  function buildSlide(item, index, total) {
    var slide = document.createElement('div');
    slide.className = 'relod-wcvms-lb__slide';
    slide.setAttribute('data-index', String(index));
    slide.setAttribute('data-kind', item.kind || 'image');
    slide.setAttribute('aria-label', (i18n('slideOf', 'Изображение %1$s из %2$s') || '')
      .replace('%1$s', String(index + 1))
      .replace('%2$s', String(total)));

    var figure = document.createElement('div');
    figure.className = 'relod-wcvms-lb__figure';

    if ('video' === item.kind) {
      var video = document.createElement('video');
      video.className = 'relod-wcvms-lb__video';
      video.controls = true;
      video.playsInline = true;
      video.preload = 'none';
      video.setAttribute('playsinline', '');
      video.setAttribute('controlslist', 'nodownload');
      if (item.poster) { video.poster = item.poster; }
      video.setAttribute('data-src', item.src || '');
      figure.appendChild(video);
    } else {
      var img = document.createElement('img');
      img.className = 'relod-wcvms-lb__img';
      img.alt = item.alt || '';
      img.decoding = 'async';
      img.draggable = false;
      if (item.width) { img.width = item.width; }
      if (item.height) { img.height = item.height; }
      img.setAttribute('data-src', item.src || '');
      if (item.srcset) { img.setAttribute('data-srcset', item.srcset); }
      if (item.sizes) { img.setAttribute('data-sizes', item.sizes); }
      figure.appendChild(img);
    }

    slide.appendChild(figure);
    return slide;
  }

  function loadSlideMedia(slide) {
    if (!slide || slide.dataset.relodLoaded === '1') { return; }
    slide.dataset.relodLoaded = '1';

    var img = slide.querySelector('.relod-wcvms-lb__img');
    if (img) {
      var srcset = img.getAttribute('data-srcset');
      var sizes = img.getAttribute('data-sizes');
      if (sizes) { img.sizes = sizes; }
      if (srcset) { img.srcset = srcset; }
      img.src = img.getAttribute('data-src') || '';
      return;
    }

    var video = slide.querySelector('.relod-wcvms-lb__video');
    if (video && !video.src) {
      video.src = video.getAttribute('data-src') || '';
    }
  }

  function preloadAround(index) {
    if (!lb) { return; }
    [index - 1, index, index + 1].forEach(function (i) {
      if (i >= 0 && i < lb.slides.length) {
        loadSlideMedia(lb.slides[i]);
      }
    });
  }

  function buildThumbs(items) {
    if (!lb) { return; }
    lb.thumbs.innerHTML = '';

    var showThumbs = lightboxConf().showThumbs && items.length > 1;
    lb.root.classList.toggle('has-thumbs', showThumbs);
    if (!showThumbs) { return; }

    items.forEach(function (item, index) {
      var button = document.createElement('button');
      button.type = 'button';
      button.className = 'relod-wcvms-lb__thumb';
      button.setAttribute('data-index', String(index));
      button.setAttribute('aria-label', item.title || String(index + 1));

      var img = document.createElement('img');
      img.src = item.thumb || item.src || '';
      img.alt = '';
      img.loading = 'lazy';
      img.decoding = 'async';
      img.draggable = false;
      button.appendChild(img);

      if ('video' === item.kind) {
        var badge = document.createElement('span');
        badge.className = 'relod-wcvms-play-icon';
        badge.setAttribute('aria-hidden', 'true');
        button.appendChild(badge);
      }

      lb.thumbs.appendChild(button);
    });
  }

  function scrollActiveThumbIntoView() {
    if (!lb) { return; }
    var active = lb.thumbs.querySelector('.relod-wcvms-lb__thumb.is-active');
    if (!active || !lb.thumbs.scrollWidth) { return; }

    var target = active.offsetLeft - (lb.thumbs.clientWidth - active.offsetWidth) / 2;
    var max = lb.thumbs.scrollWidth - lb.thumbs.clientWidth;

    try {
      lb.thumbs.scrollTo({
        left: clamp(target, 0, Math.max(0, max)),
        behavior: prefersReducedMotion() ? 'auto' : 'smooth'
      });
    } catch (e) {
      lb.thumbs.scrollLeft = clamp(target, 0, Math.max(0, max));
    }
  }

  // ── Зум ─────────────────────────────────────────────────────────

  function currentSlide() {
    return lb ? lb.slides[lb.index] : null;
  }

  /**
   * Область просмотра активного слайда. Её рамка не зависит от transform
   * картинки, поэтому служит стабильной системой координат для зума и панорамы.
   */
  function currentFigure() {
    var slide = currentSlide();
    return slide ? slide.querySelector('.relod-wcvms-lb__figure') : null;
  }

  function currentZoomTarget() {
    var slide = currentSlide();
    return slide ? slide.querySelector('.relod-wcvms-lb__img') : null;
  }

  function isZoomed() {
    return !!lb && lb.zoom.scale > 1.001;
  }

  function applyZoomTransform(animate) {
    var img = currentZoomTarget();
    if (!img) { return; }

    img.style.transition = (animate && !prefersReducedMotion()) ? 'transform .28s cubic-bezier(.22,.61,.36,1)' : 'none';
    img.style.transform = 'translate3d(' + lb.zoom.tx + 'px,' + lb.zoom.ty + 'px,0) scale(' + lb.zoom.scale + ')';

    lb.root.classList.toggle('is-zoomed', isZoomed());
    if (lb.zoomBtn) {
      var label = isZoomed() ? i18n('zoomOut', 'Уменьшить') : i18n('zoomIn', 'Увеличить');
      lb.zoomBtn.setAttribute('aria-label', label);
      lb.zoomBtn.title = label;
    }
  }

  function clampPan() {
    var img = currentZoomTarget();
    var figure = currentFigure();
    if (!img || !figure) { return; }

    var baseWidth = img.clientWidth || img.offsetWidth || 0;
    var baseHeight = img.clientHeight || img.offsetHeight || 0;

    var maxX = Math.max(0, (baseWidth * lb.zoom.scale - figure.clientWidth) / 2);
    var maxY = Math.max(0, (baseHeight * lb.zoom.scale - figure.clientHeight) / 2);

    lb.zoom.tx = clamp(lb.zoom.tx, -maxX, maxX);
    lb.zoom.ty = clamp(lb.zoom.ty, -maxY, maxY);
  }

  function resetZoom(animate) {
    if (!lb) { return; }
    lb.zoom.scale = 1;
    lb.zoom.tx = 0;
    lb.zoom.ty = 0;
    applyZoomTransform(!!animate);
  }

  /**
   * Меняет масштаб так, чтобы точка (clientX, clientY) осталась под курсором.
   */
  function zoomTo(scale, clientX, clientY, animate) {
    var img = currentZoomTarget();
    if (!lb || !img) { return; }

    var maxZoom = lightboxConf().maxZoom;
    var next = clamp(scale, 1, maxZoom);
    var previous = lb.zoom.scale;

    if (Math.abs(next - previous) < 0.001) { return; }

    // Центр берём у области просмотра: он не зависит от уже применённого
    // transform, поэтому точка под курсором остаётся на месте и при повторных зумах.
    var figure = currentFigure();
    if (!figure) { return; }

    var rect = figure.getBoundingClientRect();
    var centerX = rect.left + rect.width / 2;
    var centerY = rect.top + rect.height / 2;

    var dx = (typeof clientX === 'number' ? clientX : centerX) - centerX;
    var dy = (typeof clientY === 'number' ? clientY : centerY) - centerY;

    if (next === 1) {
      lb.zoom.tx = 0;
      lb.zoom.ty = 0;
    } else {
      lb.zoom.tx = dx - (next / previous) * (dx - lb.zoom.tx);
      lb.zoom.ty = dy - (next / previous) * (dy - lb.zoom.ty);
    }

    lb.zoom.scale = next;
    clampPan();
    applyZoomTransform(animate !== false);
  }

  function toggleZoom(clientX, clientY) {
    if (!currentZoomTarget()) { return; }
    if (isZoomed()) {
      resetZoom(true);
    } else {
      zoomTo(lightboxConf().stepZoom, clientX, clientY, true);
    }
  }

  // ── Навигация ───────────────────────────────────────────────────

  function setTrackOffset(deltaX, animate) {
    if (!lb) { return; }
    var base = -lb.index * 100;
    lb.track.style.transition = (animate && !prefersReducedMotion())
      ? 'transform .32s cubic-bezier(.22,.61,.36,1)'
      : 'none';
    lb.track.style.transform = deltaX
      ? 'translate3d(calc(' + base + '% + ' + deltaX + 'px), 0, 0)'
      : 'translate3d(' + base + '%, 0, 0)';
  }

  function pauseAllLightboxVideos(exceptIndex) {
    if (!lb) { return; }
    lb.slides.forEach(function (slide, index) {
      if (index === exceptIndex) { return; }
      slide.querySelectorAll('video').forEach(function (video) {
        try { video.pause(); } catch (e) {}
      });
    });
  }

  function goToLightboxSlide(index, animate) {
    if (!lb || !lb.items.length) { return; }

    var normalized = clamp(parseInt(index, 10) || 0, 0, lb.items.length - 1);
    var changed = normalized !== lb.index;

    lb.index = normalized;
    if (changed) { resetZoom(false); }

    setTrackOffset(0, animate !== false);
    preloadAround(normalized);
    pauseAllLightboxVideos(normalized);

    lb.slides.forEach(function (slide, i) {
      slide.classList.toggle('is-active', i === normalized);
      slide.setAttribute('aria-hidden', i === normalized ? 'false' : 'true');
    });

    lb.thumbs.querySelectorAll('.relod-wcvms-lb__thumb').forEach(function (thumb, i) {
      var active = i === normalized;
      thumb.classList.toggle('is-active', active);
      thumb.setAttribute('aria-current', active ? 'true' : 'false');
    });

    lb.counter.textContent = (normalized + 1) + ' / ' + lb.items.length;

    var single = lb.items.length < 2;
    lb.root.classList.toggle('is-single', single);
    lb.prevBtn.disabled = single || normalized <= 0;
    lb.nextBtn.disabled = single || normalized >= lb.items.length - 1;
    lb.root.classList.toggle('is-video', (lb.items[normalized] || {}).kind === 'video');

    scrollActiveThumbIntoView();
  }

  function stepLightbox(delta) {
    if (!lb) { return; }
    goToLightboxSlide(lb.index + delta, true);
  }

  // ── Свайп / панорамирование ─────────────────────────────────────

  function bindStageGestures() {
    var pointers = {};
    var pointerCount = 0;
    var mode = null; // 'swipe' | 'pan' | 'pinch'
    var start = { x: 0, y: 0, lastX: 0, tx: 0, ty: 0, scale: 1, distance: 0, midX: 0, midY: 0, target: null };
    var moved = false;
    var lastTapAt = 0;

    function trackPointer(e) {
      if (!Object.prototype.hasOwnProperty.call(pointers, e.pointerId)) { pointerCount += 1; }
      pointers[e.pointerId] = { x: e.clientX, y: e.clientY };
    }

    function forgetPointer(e) {
      if (Object.prototype.hasOwnProperty.call(pointers, e.pointerId)) {
        delete pointers[e.pointerId];
        pointerCount = Math.max(0, pointerCount - 1);
      }
    }

    function pointerList() {
      return Object.keys(pointers).map(function (key) { return pointers[key]; });
    }

    function distanceBetween(a, b) {
      var dx = a.x - b.x;
      var dy = a.y - b.y;
      return Math.sqrt(dx * dx + dy * dy);
    }

    function beginPinch() {
      var list = pointerList();
      mode = 'pinch';
      start.distance = distanceBetween(list[0], list[1]) || 1;
      start.scale = lb.zoom.scale;
      start.tx = lb.zoom.tx;
      start.ty = lb.zoom.ty;
      start.midX = (list[0].x + list[1].x) / 2;
      start.midY = (list[0].y + list[1].y) / 2;
    }

    function endGesture() {
      if (!lb) { return; }

      if (mode === 'swipe') {
        var deltaX = start.lastX - start.x;
        var threshold = Math.max(40, lb.stage.clientWidth * 0.12);
        if (Math.abs(deltaX) >= threshold && lb.items.length > 1) {
          goToLightboxSlide(lb.index + (deltaX < 0 ? 1 : -1), true);
        } else {
          setTrackOffset(0, true);
        }
      }

      mode = null;
    }

    lb.stage.addEventListener('pointerdown', function (e) {
      if (!lb) { return; }
      if (e.target.closest('button, a, video')) { return; }
      if (e.pointerType === 'mouse' && e.button !== 0) { return; }

      trackPointer(e);
      moved = false;

      // Запоминаем цель до захвата указателя: setPointerCapture() подменяет
      // e.target у последующих pointermove/pointerup на саму сцену.
      start.target = e.target;

      try { lb.stage.setPointerCapture(e.pointerId); } catch (err) {}

      if (pointerCount === 2) {
        beginPinch();
        return;
      }

      start.x = e.clientX;
      start.y = e.clientY;
      start.lastX = e.clientX;
      start.tx = lb.zoom.tx;
      start.ty = lb.zoom.ty;
      mode = isZoomed() ? 'pan' : null;
      lb.root.classList.add('is-grabbing');
    });

    lb.stage.addEventListener('pointermove', function (e) {
      if (!lb || !Object.prototype.hasOwnProperty.call(pointers, e.pointerId)) { return; }
      pointers[e.pointerId] = { x: e.clientX, y: e.clientY };

      if (mode === 'pinch' && pointerCount >= 2) {
        var list = pointerList();
        if (list.length >= 2) {
          var ratio = distanceBetween(list[0], list[1]) / start.distance;
          lb.zoom.scale = start.scale;
          lb.zoom.tx = start.tx;
          lb.zoom.ty = start.ty;
          zoomTo(start.scale * ratio, start.midX, start.midY, false);
        }
        e.preventDefault();
        return;
      }

      var dx = e.clientX - start.x;
      var dy = e.clientY - start.y;
      start.lastX = e.clientX;

      if (Math.abs(dx) > 6 || Math.abs(dy) > 6) { moved = true; }

      if (mode === 'pan') {
        lb.zoom.tx = start.tx + dx;
        lb.zoom.ty = start.ty + dy;
        clampPan();
        applyZoomTransform(false);
        e.preventDefault();
        return;
      }

      if (!mode && Math.abs(dx) > 8 && Math.abs(dx) > Math.abs(dy) && lb.items.length > 1) {
        mode = 'swipe';
      }

      if (mode === 'swipe') {
        setTrackOffset(dx, false);
        e.preventDefault();
      }
    }, { passive: false });

    function release(e) {
      if (!lb) { return; }
      forgetPointer(e);
      try { lb.stage.releasePointerCapture(e.pointerId); } catch (err) {}

      if (pointerCount < 2 && mode === 'pinch') {
        mode = null;
        if (lb.zoom.scale <= 1.05) { resetZoom(true); }
        return;
      }

      if (pointerCount === 0) {
        lb.root.classList.remove('is-grabbing');
        endGesture();

        if (!moved && e.type === 'pointerup') {
          handleStageTap(e);
        }
      }
    }

    lb.stage.addEventListener('pointerup', release);
    lb.stage.addEventListener('pointercancel', release);

    function handleStageTap(e) {
      var origin = start.target || e.target;
      if (!origin || !origin.closest || origin.closest('button, a, video')) { return; }

      var onImage = !!origin.closest('.relod-wcvms-lb__img');

      // Клик мимо изображения закрывает модалку — привычное поведение лайтбокса.
      if (!onImage) {
        if (!isZoomed()) { closeLightbox(); }
        else { resetZoom(true); }
        return;
      }

      if (e.pointerType === 'touch') {
        var now = Date.now();
        if (now - lastTapAt < 300) {
          lastTapAt = 0;
          toggleZoom(e.clientX, e.clientY);
        } else {
          lastTapAt = now;
        }
        return;
      }

      toggleZoom(e.clientX, e.clientY);
    }

    lb.stage.addEventListener('wheel', function (e) {
      if (!lb || !currentZoomTarget()) { return; }
      e.preventDefault();
      var factor = Math.exp(-e.deltaY * 0.0015);
      zoomTo(lb.zoom.scale * factor, e.clientX, e.clientY, false);
    }, { passive: false });

    lb.stage.addEventListener('dblclick', function (e) {
      if (e.target.closest('.relod-wcvms-lb__img')) {
        e.preventDefault();
      }
    });
  }

  // ── Полноэкранный режим ─────────────────────────────────────────

  function fullscreenElement() {
    return document.fullscreenElement || document.webkitFullscreenElement || null;
  }

  function toggleFullscreen() {
    if (!lb) { return; }

    if (fullscreenElement()) {
      var exit = document.exitFullscreen || document.webkitExitFullscreen;
      if (exit) { exit.call(document); }
      return;
    }

    var request = lb.root.requestFullscreen || lb.root.webkitRequestFullscreen;
    if (request) {
      var result = request.call(lb.root);
      if (result && typeof result.catch === 'function') {
        result.catch(function () { lb.root.classList.toggle('is-theatre'); });
      }
    } else {
      // Safari на iOS не даёт fullscreen для div — прячем служебные элементы.
      lb.root.classList.toggle('is-theatre');
    }
  }

  function syncFullscreenState() {
    if (!lb) { return; }
    var active = fullscreenElement() === lb.root;
    lb.root.classList.toggle('is-fullscreen', active);
    var label = active ? i18n('exitFullscreen', 'Выйти из полноэкранного режима') : i18n('fullscreen', 'Во весь экран');
    lb.fullscreenBtn.setAttribute('aria-label', label);
    lb.fullscreenBtn.title = label;
  }

  // ── Блокировка прокрутки страницы ───────────────────────────────

  function lockScroll() {
    if (!lb) { return; }
    lb.scrollY = window.pageYOffset || document.documentElement.scrollTop || 0;
    document.documentElement.classList.add('relod-wcvms-popup-open');
  }

  function unlockScroll(scrollY) {
    document.documentElement.classList.remove('relod-wcvms-popup-open');
    if (typeof scrollY === 'number') {
      window.scrollTo(0, scrollY);
    }
  }

  // ── Фокус ───────────────────────────────────────────────────────

  function focusableNodes() {
    if (!lb) { return []; }
    return Array.prototype.slice
      .call(lb.root.querySelectorAll('button:not([disabled])'))
      .filter(function (node) { return node.offsetParent !== null; });
  }

  function trapFocus(e) {
    if (!lb || e.key !== 'Tab') { return; }

    var nodes = focusableNodes();
    if (!nodes.length) { return; }

    var first = nodes[0];
    var last = nodes[nodes.length - 1];

    if (!lb.root.contains(document.activeElement)) {
      e.preventDefault();
      first.focus();
      return;
    }

    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  }

  // ── Открытие / закрытие ─────────────────────────────────────────

  function closeLightbox() {
    if (!lb) { return; }

    var instance = lb;
    lb = null;

    if (fullscreenElement() === instance.root) {
      var exit = document.exitFullscreen || document.webkitExitFullscreen;
      if (exit) { try { exit.call(document); } catch (e) {} }
    }

    instance.root.classList.remove('is-visible');
    unlockScroll(instance.scrollY);

    window.setTimeout(function () {
      if (instance.root && instance.root.parentNode) {
        instance.root.parentNode.removeChild(instance.root);
      }
    }, prefersReducedMotion() ? 0 : 200);

    if (instance.lastFocus && typeof instance.lastFocus.focus === 'function') {
      try { instance.lastFocus.focus({ preventScroll: true }); } catch (e) {}
    }
  }

  function openLightbox(items, startIndex, trigger) {
    if (!Array.isArray(items) || !items.length) { return; }

    closeLightbox();

    lb = buildLightbox();
    lb.items = items;
    lb.lastFocus = trigger || document.activeElement;

    items.forEach(function (item, index) {
      var slide = buildSlide(item, index, items.length);
      lb.slides.push(slide);
      lb.track.appendChild(slide);
    });

    buildThumbs(items);

    document.body.appendChild(lb.root);
    lockScroll();

    bindStageGestures();

    lb.closeBtn.addEventListener('click', closeLightbox);
    lb.fullscreenBtn.addEventListener('click', toggleFullscreen);
    lb.zoomBtn.addEventListener('click', function () { toggleZoom(); });
    lb.prevBtn.addEventListener('click', function () { stepLightbox(-1); });
    lb.nextBtn.addEventListener('click', function () { stepLightbox(1); });

    lb.thumbs.addEventListener('click', function (e) {
      var thumb = e.target.closest('.relod-wcvms-lb__thumb');
      if (thumb) {
        goToLightboxSlide(thumb.getAttribute('data-index'), true);
      }
    });

    goToLightboxSlide(startIndex, false);
    syncFullscreenState();

    window.requestAnimationFrame(function () {
      if (lb) {
        lb.root.classList.add('is-visible');
        // Фокус на самом диалоге: клавиатура работает сразу, но кнопка
        // «Закрыть» не подсвечивается кольцом после клика мышью.
        try { lb.root.focus({ preventScroll: true }); } catch (e) {}
      }
    });
  }

  function openLightboxFromHost(host, mediaElement) {
    var items = getLightboxItems(host);
    if (!items.length) { return false; }

    var element = mediaElement || getActiveMediaElement(host);
    var index = findItemIndexByKey(items, resolveMediaKey(element));

    openLightbox(items, index < 0 ? 0 : index, element || host);
    return true;
  }

  // ── Реакция на клики по галерее товара ──────────────────────────

  var lastOpenAt = 0;

  function shouldIgnoreOpenTarget(target) {
    if (!target || !target.closest) { return true; }
    if (target.closest('.relod-wcvms-lb')) { return true; }
    if (target.closest('.relod-wcvms-zoom-hint')) { return false; }
    return !!target.closest('.relod-wcvms-slider-dot, .relod-wcvms-slider-arrow, .relod-wcvms-thumb, button, a, input, select, textarea, label');
  }

  function resolveOpenContext(target) {
    if (!target || !target.closest) { return null; }

    var host = getLightboxHost(target);
    if (!host) { return null; }

    var element;

    if (target.closest('.relod-wcvms-zoom-hint')) {
      element = getActiveMediaElement(host);
    } else {
      element = target.closest('.relod-wcvms-slider-item, .relod-wcvms-slide') || getActiveMediaElement(host);
    }

    if (!element) { return null; }

    // Видео открываем не в зуме, а оставляем воспроизведение на месте.
    if (element.getAttribute && element.getAttribute('data-kind') === 'video') { return null; }

    if (element.classList &&
      (element.classList.contains('relod-wcvms-slider-item') || element.classList.contains('relod-wcvms-slide')) &&
      !element.classList.contains('is-active')) {
      return null;
    }

    return { host: host, element: element };
  }

  function tryOpenFromEvent(e, allowDefaultPrevented) {
    if (!e || !e.target || (!allowDefaultPrevented && e.defaultPrevented)) { return false; }

    var target = e.target.nodeType === 1 ? e.target : e.target.parentElement;
    if (shouldIgnoreOpenTarget(target)) { return false; }

    var context = resolveOpenContext(target);
    if (!context) { return false; }

    var suppressUntil = parseInt(context.host.dataset.relodSuppressClickUntil || '0', 10);
    var wrap = context.host.closest('[data-relod-slider]') || context.host;
    var wrapSuppress = parseInt((wrap.dataset || {}).relodSuppressClickUntil || '0', 10);

    if ((suppressUntil && Date.now() < suppressUntil) || (wrapSuppress && Date.now() < wrapSuppress)) {
      return false;
    }

    if (lastOpenAt && Date.now() - lastOpenAt < 300) { return false; }

    e.preventDefault();
    e.stopPropagation();

    if (!openLightboxFromHost(context.host, context.element)) { return false; }

    lastOpenAt = Date.now();
    return true;
  }

  document.addEventListener('click', function (e) {
    tryOpenFromEvent(e, false);
  }, true);

  document.addEventListener('pointerup', function (e) {
    if (!e || e.pointerType !== 'mouse' || e.button !== 0) { return; }
    tryOpenFromEvent(e, true);
  }, false);

  document.addEventListener('keydown', function (e) {
    if (!lb) {
      // Открытие с клавиатуры по значку лупы.
      if ((e.key === 'Enter' || e.key === ' ') && document.activeElement &&
        document.activeElement.classList &&
        document.activeElement.classList.contains('relod-wcvms-zoom-hint')) {
        var host = getLightboxHost(document.activeElement);
        if (host) {
          e.preventDefault();
          openLightboxFromHost(host);
        }
      }
      return;
    }

    switch (e.key) {
      case 'Escape':
        e.preventDefault();
        if (isZoomed()) { resetZoom(true); } else { closeLightbox(); }
        break;
      case 'ArrowLeft':
        e.preventDefault();
        stepLightbox(-1);
        break;
      case 'ArrowRight':
        e.preventDefault();
        stepLightbox(1);
        break;
      case 'Home':
        e.preventDefault();
        goToLightboxSlide(0, true);
        break;
      case 'End':
        e.preventDefault();
        goToLightboxSlide(lb.items.length - 1, true);
        break;
      case '+':
      case '=':
        e.preventDefault();
        zoomTo(lb.zoom.scale * 1.4, undefined, undefined, true);
        break;
      case '-':
      case '_':
        e.preventDefault();
        zoomTo(lb.zoom.scale / 1.4, undefined, undefined, true);
        break;
      default:
        trapFocus(e);
    }
  });

  ['fullscreenchange', 'webkitfullscreenchange'].forEach(function (eventName) {
    document.addEventListener(eventName, syncFullscreenState);
  });

  window.addEventListener('resize', function () {
    if (!lb) { return; }
    clampPan();
    applyZoomTransform(false);
    setTrackOffset(0, false);
    scrollActiveThumbIntoView();
  });

  window.addEventListener('orientationchange', function () {
    if (!lb) { return; }
    window.setTimeout(function () {
      if (!lb) { return; }
      resetZoom(false);
      setTrackOffset(0, false);
    }, 150);
  });

  // ── Синхронизация с выбором вариации ────────────────────────────

  var originalApplyPayload = applyPayload;
  applyPayload = function (payload) {
    closeLightbox();
    originalApplyPayload(payload);

    if (payload && Array.isArray(payload.lightbox_items)) {
      updateAllLightboxItems(payload.lightbox_items);
    }

    initLightboxHosts(document);
  };

  document.addEventListener('DOMContentLoaded', function () {
    initGalleries(document);
    initSliders(document);
    initLightboxHosts(document);
    unlockVariationSelectOptions($('form.variations_form').first());
    updateSwatchesAvailability($('form.variations_form').first());
    syncSwatchesWithForm($('form.variations_form').first());
  });

})(jQuery);
