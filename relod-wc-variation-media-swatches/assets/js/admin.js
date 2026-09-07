(function ($) {
  'use strict';

  function settings() {
    return window.relodWcvmsAdmin || { i18n: {}, palette: [] };
  }

  var i18n = function () { return settings().i18n || {}; };

  function esc(text) {
    return String(text || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  // ───────────────────────────────────────────────────────────────
  // Build media row HTML
  // ───────────────────────────────────────────────────────────────

  function buildRow(item, kind, fieldName) {
    var preview = '';

    if (kind === 'image' && item.url) {
      preview = '<img class="relod-wcvms-thumb-image" src="' + esc(item.url) + '" alt="">';
    } else if (kind === 'video') {
      var poster = item.image && item.image.src ? item.image.src : '';
      preview = poster
        ? '<img class="relod-wcvms-thumb-image" src="' + esc(poster) + '" alt="">'
        : '<span class="relod-wcvms-thumb-video-icon">▶</span>';
    }

    var label = item.title || item.filename || ('ID ' + item.id);
    var mime  = item.mime || '';

    return '<div class="relod-wcvms-media-row" data-id="' + esc(item.id) + '">' +
      '<div class="relod-wcvms-media-row-left">' +
        '<span class="relod-wcvms-drag dashicons dashicons-move" title="' + esc(i18n().dragHint || 'Перетащить') + '"></span>' +
        '<span class="relod-wcvms-thumb">' + preview + '</span>' +
        '<span class="relod-wcvms-media-row-text">' +
          '<strong>' + esc(label) + '</strong>' +
          '<span class="relod-wcvms-media-row-meta">' + esc(mime) + '</span>' +
        '</span>' +
      '</div>' +
      '<button type="button" class="button-link-delete relod-wcvms-remove-media" title="' + esc(i18n().remove || 'Удалить') + '">' +
        '<span class="dashicons dashicons-no-alt"></span>' +
      '</button>' +
      '<input type="hidden" name="' + esc(fieldName) + '" value="' + esc(item.id) + '">' +
    '</div>';
  }

  // ───────────────────────────────────────────────────────────────
  // Count badges
  // ───────────────────────────────────────────────────────────────

  function updateCountBadge($list) {
    var badgeKey  = $list.data('badge-key');
    var mediaKind = $list.data('media-kind');
    if (!badgeKey) { return; }

    var count   = $list.find('.relod-wcvms-media-row').length;
    var unit    = mediaKind === 'video'
      ? (i18n().video || 'видео')
      : (i18n().photo || 'фото');

    $('[data-count-badge="' + badgeKey + '"]').each(function () {
      var $b = $(this);
      if (count > 0) {
        $b.text(count + ' ' + unit).removeClass('relod-wcvms-badge--empty');
      } else {
        $b.text('').addClass('relod-wcvms-badge--empty');
      }
    });
  }

  // Update the "Очистить" button visibility
  function updateClearButton($manager) {
    var $list = $manager.find('.relod-wcvms-media-list').first();
    var count = $list.find('.relod-wcvms-media-row').length;
    var $clear = $manager.find('.relod-wcvms-clear-media');
    if (count > 0) {
      $clear.show();
    } else {
      $clear.hide();
    }
  }

  // ───────────────────────────────────────────────────────────────
  // Color fields init
  // ───────────────────────────────────────────────────────────────

  function initColorFields(scope) {
    (scope || $(document)).find('.relod-wcvms-color-field').each(function () {
      var $input = $(this);
      if ($input.data('relod-color-init')) { return; }
      $input.data('relod-color-init', true);

      $input.wpColorPicker({
        change: function (event, ui) {
          var hex  = ui.color.toString();
          var vid  = $input.data('variation-id');
          if (vid) {
            $('[data-color-preview="' + vid + '"]')
              .css('background', hex)
              .attr('title', hex);
          }
        },
        clear: function () {
          var vid = $input.data('variation-id');
          if (vid) {
            $('[data-color-preview="' + vid + '"]')
              .css('background', '')
              .attr('title', i18n().noColor || 'Цвет не задан');
          }
        }
      });

      // Color palette chips
      var $palette = $('<div class="relod-wcvms-color-palette"></div>');
      (settings().palette || []).forEach(function (hex) {
        $('<button type="button" class="relod-wcvms-color-chip" aria-label="' + esc(hex) + '"></button>')
          .css('background', hex)
          .attr('data-color', hex)
          .appendTo($palette);
      });
      var $paletteTarget = $input.closest('p, td, .form-field').first();
      if (!$paletteTarget.length) {
        $paletteTarget = $input.parent();
      }
      $paletteTarget.append($palette);
    });
  }

  // ───────────────────────────────────────────────────────────────
  // Sortable
  // ───────────────────────────────────────────────────────────────

  function initSortable(scope) {
    (scope || $(document)).find('.relod-wcvms-media-list').each(function () {
      var $list = $(this);
      if ($list.data('relod-sortable-init')) { return; }
      $list.data('relod-sortable-init', true);
      $list.sortable({
        items: '.relod-wcvms-media-row',
        handle: '.relod-wcvms-drag',
        placeholder: 'relod-wcvms-sort-placeholder',
        tolerance: 'pointer',
        stop: function () {
          updateCountBadge($list);
        }
      });
    });
  }

  // ───────────────────────────────────────────────────────────────
  // Media library frame
  // ───────────────────────────────────────────────────────────────

  function openFrame($manager) {
    var kind     = $manager.data('kind') === 'video' ? 'video' : 'image';
    var multiple = !$manager.hasClass('relod-wcvms-media-manager--single');

    var frame = wp.media({
      title:   kind === 'video'
        ? (i18n().chooseVideos || 'Выберите видео')
        : (i18n().chooseImages || 'Выберите изображения'),
      button:  { text: kind === 'video'
        ? (i18n().useVideos || 'Использовать видео')
        : (i18n().useImages || 'Использовать изображения') },
      library: { type: kind },
      multiple: multiple
    });

    frame.on('select', function () {
      var selection  = frame.state().get('selection').toJSON();
      var $list      = $manager.find('.relod-wcvms-media-list').first();
      if (!$list.length) { return; }

      // Resolve field name
      var firstInputName = $list.find('input[type="hidden"]').first().attr('name')
        || $list.data('field-name')
        || $manager.data('field-name')
        || '';

      if (!firstInputName) {
        if ($manager.hasClass('relod-wcvms-media-manager--single')) {
          firstInputName = $manager.closest('.options_group, .relod-wcvms-field-group')
            .find('input[name="relod_wcvms_main_video_id"]').attr('name')
            || 'relod_wcvms_main_video_id';
        } else {
          return;
        }
      }

      if ($manager.hasClass('relod-wcvms-media-manager--single')) {
        var item = selection[0];
        if (!item) { return; }
        // Remove placeholder if present
        $list.find('.relod-wcvms-empty-placeholder').remove();
        $list.html(buildRow(item, kind, firstInputName));
        updateClearButton($manager);
        return;
      }

      // Build list of existing IDs to avoid duplicates
      var existing = {};
      $list.find('.relod-wcvms-media-row').each(function () {
        existing[String($(this).data('id'))] = true;
      });

      // Remove empty-placeholder before adding rows
      $list.find('.relod-wcvms-empty-placeholder').remove();

      selection.forEach(function (item) {
        if (existing[String(item.id)]) { return; }
        existing[String(item.id)] = true;
        $list.append(buildRow(item, kind, firstInputName));
      });

      updateCountBadge($list);
      updateClearButton($manager);
      initSortable($manager);
    });

    frame.open();
  }

  // ───────────────────────────────────────────────────────────────
  // Single-item placeholder (for main video)
  // ───────────────────────────────────────────────────────────────

  function ensureSingleInputPlaceholder($manager) {
    if (!$manager.hasClass('relod-wcvms-media-manager--single')) { return; }
    var $list = $manager.find('.relod-wcvms-media-list').first();
    if ($list.find('.relod-wcvms-media-row').length) { return; }
    var name = $list.closest('.relod-wcvms-field-group, .options_group')
      .find('input[name="relod_wcvms_main_video_id"]').attr('name')
      || 'relod_wcvms_main_video_id';
    $list.html(
      '<div class="relod-wcvms-empty-placeholder">' + esc(i18n().emptyVideo || 'Видео не выбрано') + '</div>' +
      '<input type="hidden" name="' + esc(name) + '" value="">'
    );
  }

  // ───────────────────────────────────────────────────────────────
  // Init on DOM ready
  // ───────────────────────────────────────────────────────────────

  $(function () {
    initColorFields($(document));
    initSortable($(document));

    // Init clear button visibility
    $('.relod-wcvms-media-manager').each(function () {
      updateClearButton($(this));
    });

    $(document)

      // Palette chip click
      .on('click', '.relod-wcvms-color-chip', function () {
        var color  = $(this).data('color');
        var $input = $(this).closest('p, td, .form-field').find('.relod-wcvms-color-field').first();
        if (!$input.length) {
          $input = $(this).closest('.relod-wcvms-color-palette').siblings('.relod-wcvms-color-field').first();
        }
        if ($input.length) {
          $input.val(color).trigger('change');
          try { $input.wpColorPicker('color', color); } catch (e) {}
          // Update preview
          var vid = $input.data('variation-id');
          if (vid) {
            $('[data-color-preview="' + vid + '"]')
              .css('background', color)
              .attr('title', color);
          }
        }
      })

      // Pick media button
      .on('click', '.relod-wcvms-pick-media', function () {
        openFrame($(this).closest('.relod-wcvms-media-manager'));
      })

      // Remove single media row
      .on('click', '.relod-wcvms-remove-media', function () {
        var $row     = $(this).closest('.relod-wcvms-media-row');
        var $manager = $(this).closest('.relod-wcvms-media-manager');
        var $list    = $manager.find('.relod-wcvms-media-list').first();

        $row.remove();
        ensureSingleInputPlaceholder($manager);
        updateCountBadge($list);
        updateClearButton($manager);
      })

      // Clear all media in a list
      .on('click', '.relod-wcvms-clear-media', function () {
        var confirmMsg = $(this).data('confirm') || 'Удалить всё?';
        if (!window.confirm(confirmMsg)) { return; }
        var $manager = $(this).closest('.relod-wcvms-media-manager');
        var $list    = $manager.find('.relod-wcvms-media-list').first();
        $list.find('.relod-wcvms-media-row').remove();
        updateCountBadge($list);
        updateClearButton($manager);
      })

      // WooCommerce: new variations loaded (e.g. after "Add variation")
      .on('woocommerce_variations_loaded', function () {
        initColorFields($('.woocommerce_variations'));
        initSortable($('.woocommerce_variations'));
        $('.woocommerce_variations .relod-wcvms-media-manager').each(function () {
          updateClearButton($(this));
        });
      })

      // WooCommerce: variation added
      .on('woocommerce_variation_added', function () {
        setTimeout(function () {
          initColorFields($('.woocommerce_variations'));
          initSortable($('.woocommerce_variations'));
        }, 200);
      });
  });

})(jQuery);
