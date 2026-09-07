/**
 * RELOD Wishlist × UE Woo Product Grid Bridge — v1.2.0
 *
 * Что изменилось:
 *  • Мостик слушает событие relod_wl:changed из ядра вместо разбора тела
 *    запроса в jQuery.ajaxSuccess. Ядро с 1.2.0 ходит на сервер без jQuery
 *    (иначе «Delay JavaScript execution» в WP Rocket съедал первый клик),
 *    и старый способ перехвата больше не срабатывал бы.
 *  • После гидрации из грида избранного убираются карточки товаров, которых
 *    нет в реальном списке. Это страховка от страницы, отданной кешем с
 *    чужим или устаревшим содержимым.
 */
(function ($, cfg) {
  'use strict';

  if (!$) {
    return;
  }

  cfg = cfg || {};

  var GRID_CLASS = cfg.gridClass || 'relod-wl-ue-wishlist-grid';

  /**
   * Сетки, которыми управляет мостик, — ТОЛЬКО гриды избранного.
   *
   * Класс проставляется на стороне PHP тому виджету, который реально
   * использовал Query ID избранного. Второй вариант селектора нужен,
   * если класс добавлен вручную в Elementor (Advanced → CSS Classes):
   * там он попадает на обёртку виджета, а не на сам грид.
   */
  var GRID_SELECTOR = '.woocommerce_product_grid.' + GRID_CLASS + ', .' + GRID_CLASS + ' .woocommerce_product_grid';

  function getEmptyMarkup() {
    var button = '';

    if (cfg.buttonUrl && cfg.buttonText) {
      button = '<div class="relod-wl-ue-empty__actions">'
        + '<a class="relod-wl-ue-empty__button" href="' + escapeHtml(cfg.buttonUrl) + '">' + escapeHtml(cfg.buttonText) + '</a>'
        + '</div>';
    }

    return ''
      + '<div class="relod-wl-ue-empty__inner">'
      +   '<div class="relod-wl-ue-empty__icon" aria-hidden="true">'
      +     '<svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">'
      +       '<path d="M12.1 20.3 5.6 13.8a4.6 4.6 0 0 1 6.5-6.5l.4.4.4-.4a4.6 4.6 0 1 1 6.5 6.5l-6.5 6.5a.6.6 0 0 1-.8 0Z"></path>'
      +     '</svg>'
      +   '</div>'
      +   '<h3 class="relod-wl-ue-empty__title">' + escapeHtml(cfg.emptyTitle || 'В избранном пока нет товаров') + '</h3>'
      +   '<div class="relod-wl-ue-empty__text">' + escapeHtml(cfg.emptyText || 'Добавляйте понравившиеся товары в избранное — они появятся здесь автоматически.') + '</div>'
      +   button
      + '</div>';
  }

  function escapeHtml(value) {
    return String(value == null ? '' : value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  /**
   * Все гриды избранного внутри контекста, включая сам контекст.
   */
  function findWishlistGrids(context) {
    var $scope = context ? $(context) : $(document.body);

    return $scope.find(GRID_SELECTOR).addBack(GRID_SELECTOR);
  }

  function transformEmptyMessage($empty) {
    if (!$empty.length) {
      return;
    }

    $empty.addClass('relod-wl-ue-empty');

    if ($empty.find('.relod-wl-ue-empty__inner').length) {
      return;
    }

    $empty.html(getEmptyMarkup());
  }

  /**
   * Грид считается пустым, только если в нём нет ни элементов сетки,
   * ни кнопок избранного. Двойная проверка страхует от случая, когда
   * у элементов сетки другой класс: иначе непустой грид был бы скрыт.
   */
  function isGridEmpty($grid) {
    return 0 === $grid.find('.ue-item').length && 0 === $grid.find('.relod-wcwl-btn').length;
  }

  function syncWidgetEmptyState($grid) {
    if (!$grid.length) {
      return;
    }

    var $empty = $grid.next('.ue-no-posts-found');

    if (isGridEmpty($grid)) {
      if (!$empty.length) {
        $empty = $('<div class="ue-no-posts-found"></div>').insertAfter($grid);
      }

      if (!$empty.text().trim() && !$empty.children().length) {
        $empty.text(cfg.emptyInline || 'Товаров в избранном пока нет.');
      }

      $empty.show();
      transformEmptyMessage($empty);
      $grid.addClass('relod-wl-ue-grid-hidden');
      return;
    }

    $grid.removeClass('relod-wl-ue-grid-hidden');

    if ($empty.length) {
      $empty.hide();
    }
  }

  function scanAllWidgets(context) {
    findWishlistGrids(context).each(function () {
      var $grid = $(this);
      var $empty = $grid.next('.ue-no-posts-found');

      if ($empty.length && $empty.is(':visible') && isGridEmpty($grid)) {
        transformEmptyMessage($empty);
        $grid.addClass('relod-wl-ue-grid-hidden');
      } else {
        syncWidgetEmptyState($grid);
      }
    });
  }

  /**
   * Убирает карточку товара из гридов избранного.
   *
   * Карточка убирается ТОЛЬКО из гридов избранного: в 1.0.0 селектор
   * захватывал и обычный каталог, из-за чего снятие метки удаляло товар
   * из витрины до перезагрузки страницы.
   */
  function removeCard(pid, animate) {
    if (!pid) {
      return;
    }

    findWishlistGrids(document.body).each(function () {
      var $grid = $(this);

      $grid.find('.relod-wcwl-btn[data-product-id="' + pid + '"]').each(function () {
        var $item = $(this).closest('.ue-item');

        if (!$item.length) {
          return;
        }

        if (!animate) {
          $item.remove();
          syncWidgetEmptyState($grid);
          return;
        }

        $item.addClass('relod-wl-ue-removing');
        window.setTimeout(function () {
          $item.remove();
          syncWidgetEmptyState($grid);
        }, 280);
      });
    });
  }

  /**
   * Выбрасывает из грида карточки, которых нет в реальном избранном.
   *
   * Нужно, если страница всё-таки пришла из кеша (например, из CDN перед
   * WP Rocket) и содержит чужой список: без этого посетитель увидел бы
   * товары, которых у него в избранном нет, — те самые «лишние» позиции
   * при несовпадении со счётчиком.
   */
  function pruneStaleCards(ids) {
    if (!ids) {
      return;
    }

    var allowed = {};
    for (var i = 0; i < ids.length; i++) {
      allowed[parseInt(ids[i], 10)] = true;
    }

    findWishlistGrids(document.body).each(function () {
      var $grid = $(this);

      $grid.find('.relod-wcwl-btn[data-product-id]').each(function () {
        var pid = parseInt($(this).attr('data-product-id'), 10);
        if (!pid || allowed[pid]) {
          return;
        }

        var $item = $(this).closest('.ue-item');
        if ($item.length) {
          $item.remove();
        }
      });

      syncWidgetEmptyState($grid);
    });
  }

  /* ───────────────────────────────────────────
     События ядра
     ─────────────────────────────────────────── */

  document.addEventListener('relod_wl:changed', function (e) {
    var detail = e && e.detail ? e.detail : {};

    if ('removed' === detail.action) {
      removeCard(detail.productId, true);
    } else if ('hydrated' === detail.action && detail.ok) {
      /* Чистим только если состояние подтверждено сервером: иначе можно
         вычистить корректный грид из-за сетевой ошибки. */
      pruneStaleCards(detail.ids);
    }

    window.setTimeout(function () {
      scanAllWidgets(document.body);
    }, 30);
  });

  $(function () {
    scanAllWidgets(document.body);

    if ('MutationObserver' in window) {
      var observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (mutation) {
          var added = mutation.addedNodes;

          if (!added || !added.length) {
            return;
          }

          for (var i = 0; i < added.length; i++) {
            if (added[i] && added[i].nodeType === 1) {
              scanAllWidgets(added[i]);
            }
          }
        });
      });

      observer.observe(document.body, { childList: true, subtree: true });
    }
  });

})(window.jQuery, window.relodWishlistUEBridge || {});
