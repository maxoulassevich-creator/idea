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
  // Fullscreen gallery popup
  // ───────────────────────────────────────────────────────────────

  var lightboxEl = null;

  function getPopupImageFromContainer(container) {
    if (!container || container.getAttribute('data-kind') === 'video') { return null; }
    var img = container.querySelector('img');
    return img || null;
  }

  function getLargeImageSrc(img) {
    if (!img) { return ''; }
    return img.currentSrc || img.getAttribute('src') || '';
  }

  function closeImagePopup() {
    if (!lightboxEl) { return; }
    lightboxEl.classList.remove('is-visible');
    var node = lightboxEl;
    lightboxEl = null;
    window.setTimeout(function () {
      if (node && node.parentNode) {
        node.parentNode.removeChild(node);
      }
    }, 160);
    document.documentElement.classList.remove('relod-wcvms-popup-open');
  }

  function openImagePopup(img) {
    var src = getLargeImageSrc(img);
    if (!src) { return; }

    closeImagePopup();

    lightboxEl = document.createElement('div');
    lightboxEl.className = 'relod-wcvms-lightbox';
    lightboxEl.setAttribute('role', 'dialog');
    lightboxEl.setAttribute('aria-modal', 'true');

    var alt = img.getAttribute('alt') || '';
    lightboxEl.innerHTML =
      '<button type="button" class="relod-wcvms-lightbox__close" aria-label="Закрыть">×</button>' +
      '<div class="relod-wcvms-lightbox__inner">' +
        '<img class="relod-wcvms-lightbox__img" src="' + src.replace(/"/g, '&quot;') + '" alt="' + alt.replace(/"/g, '&quot;') + '">' +
      '</div>';

    document.body.appendChild(lightboxEl);
    document.documentElement.classList.add('relod-wcvms-popup-open');

    window.requestAnimationFrame(function () {
      if (lightboxEl) {
        lightboxEl.classList.add('is-visible');
      }
    });
  }

  var lastPopupOpenAt = 0;

  function shouldIgnorePopupTarget(target) {
    return !target || !target.closest ||
      target.closest('.relod-wcvms-lightbox') ||
      target.closest('.relod-wcvms-slider-dot, .relod-wcvms-slider-arrow, .relod-wcvms-thumb, button, a, input, select, textarea, label');
  }

  function getPopupContextFromTarget(target) {
    if (!target || !target.closest) { return null; }

    var slide = target.closest('.relod-wcvms-slider-item[data-kind="image"], .relod-wcvms-slide[data-kind="image"]');
    var galleryRoot = null;

    if (slide) {
      galleryRoot = slide.closest('[data-relod-slider], .relod-wcvms-gallery');
    } else {
      galleryRoot = target.closest('[data-relod-slider], .relod-wcvms-gallery');

      if (galleryRoot) {
        slide = galleryRoot.matches('[data-relod-slider]') ?
          getActiveSliderItem(galleryRoot) :
          getActiveGallerySlide(galleryRoot);

        if (!slide || slide.getAttribute('data-kind') !== 'image') {
          slide = null;
        }
      }
    }

    if (!slide || !galleryRoot || !slide.classList.contains('is-active')) {
      return null;
    }

    return {
      slide: slide,
      galleryRoot: galleryRoot
    };
  }

  function tryOpenImagePopupFromEvent(e, allowDefaultPrevented) {
    if (!e || !e.target || (!allowDefaultPrevented && e.defaultPrevented)) { return false; }

    var target = e.target.nodeType === 1 ? e.target : e.target.parentElement;
    if (shouldIgnorePopupTarget(target)) { return false; }

    var context = getPopupContextFromTarget(target);
    if (!context) { return false; }

    var suppressUntil = parseInt(context.galleryRoot.dataset.relodSuppressClickUntil || '0', 10);
    if (suppressUntil && Date.now() < suppressUntil) { return false; }

    if (lastPopupOpenAt && Date.now() - lastPopupOpenAt < 300) {
      return false;
    }

    var img = getPopupImageFromContainer(context.slide);
    if (!img) { return false; }

    e.preventDefault();
    e.stopPropagation();
    openImagePopup(img);
    lastPopupOpenAt = Date.now();
    return true;
  }

  function handleImagePopupClick(e) {
    tryOpenImagePopupFromEvent(e, false);
  }

  function handleImagePopupPointerUp(e) {
    if (!e || e.pointerType !== 'mouse' || e.button !== 0) { return; }
    tryOpenImagePopupFromEvent(e, true);
  }

  document.addEventListener('click', handleImagePopupClick, true);
  document.addEventListener('pointerup', handleImagePopupPointerUp, false);

  $(document)
    .on('click', '.relod-wcvms-lightbox, .relod-wcvms-lightbox__close', function (e) {
      if (e.target === this || e.target.classList.contains('relod-wcvms-lightbox__close')) {
        closeImagePopup();
      }
    });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
      closeImagePopup();
    }
  });

  var originalApplyPayload = applyPayload;
  applyPayload = function (payload) {
    closeImagePopup();
    originalApplyPayload(payload);
  };

  document.addEventListener('DOMContentLoaded', function () {
    initGalleries(document);
    initSliders(document);
    unlockVariationSelectOptions($('form.variations_form').first());
    updateSwatchesAvailability($('form.variations_form').first());
    syncSwatchesWithForm($('form.variations_form').first());
  });

})(jQuery);