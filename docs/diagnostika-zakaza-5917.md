# Заказ #5917 не попал в Яндекс Доставку — разбор

## Коротко

Заказ оформлен и оплачен нормально, но плагин «Яндекс доставка для Woocommerce» (Woodev, v1.3.6)
**не записал в заказ свои служебные мета-поля**. Без них заказ для плагина «не существует»:

| Мета-поле | Что делает | Состояние у #5917 |
|---|---|---|
| `_yandex_delivery_state_status` | по нему заказ попадает в раздел «Заказы Я.Доставки» и в автоэкспорт | **пусто** |
| `_yandex_delivery_destination_station_id` | ПВЗ, который выбрал покупатель | **пусто** (почти наверняка) |
| `_yandex_delivery_request_id` | ID заявки в Яндексе после экспорта | пусто (экспорта не было) |

Всё, что вы видите, — следствия одного этого факта.

## Как симптомы связаны с кодом

### 1. Заказа нет в разделе «Заказы Я.Доставки»

`includes/admin/class-order-list-table.php`, `prepare_items()` — список строится запросом,
в котором жёстко задано условие «у заказа есть мета `_yandex_delivery_state_status`»:

```php
$this->order_query_args['meta_query'][] = [
    'key'     => '_yandex_delivery_state_status',
    'compare' => 'EXISTS'
];
```

Меты нет → заказа в списке нет. Никакой фильтр («Все», «Отменён», …) его не покажет.

### 2. В блоке «Яндекс доставка — информация» пустой статус и нет кнопки «Экспортировать»

Блок вообще показывается (`includes/admin/class-admin.php::order_meta_boxes()`), потому что
условие для него другое — «в заказе есть строка доставки Яндекса». Строка доставки на месте,
поэтому блок есть, а статус пустой.

Кнопка «Экспортировать» из `WC_Yandex_Delivery_Order::get_allowed_actions()` выводится только так:

```php
if ( ! $this->is_exported() && in_array( $this->get_state_status(), [ 'NEW', 'EXPORT_INVALID' ], true ) ) {
```

Статус пустой → в массив `NEW`/`EXPORT_INVALID` не попадает → кнопки нет. Остаётся только
«Выбрать и экспортировать», она выводится в шаблоне без проверки статуса.

### 3. Заказ не экспортировался автоматически

`woocommerce-yandex-delivery.php::export_order()` и `wc_yandex_delivery_export_order()`
ставят задачу в очередь только при условии:

```php
if ( $order->get_yandex_shipping_method() && ! $order->is_exported() && 'NEW' === $order->get_state_status() ) {
```

Статуса `NEW` нет → автоэкспорт по смене статуса заказа (настройка «Статусы заказа для экспорта»)
молча пропустил заказ.

### 4. Окно «Выбор варианта доставки» пустое, дат нет

Здесь две проблемы, наложившиеся друг на друга.

**Причина.** `WC_Yandex_Delivery_Order::prepare_order_data()` для тарифа «до ПВЗ» кладёт в запрос
пункт выдачи из меты заказа:

```php
$data['destination']['platform_station']['platform_id'] = $this->get_destination_station_id();
```

Мета пустая → в Яндекс уходит заявка без пункта назначения → API отвечает ошибкой
(или пустым списком офферов), и вариантов доставки нет.

**Почему не видно ошибки.** `assets/js/admin/admin-order.js` разбирает ответ так:

```js
parse: function ( response ) {
    if ( response.success ) {
        return response.data
    }
}
```

При `success: false` функция возвращает `undefined`, коллекция остаётся пустой, и окно просто
показывает пустой список — без единого слова об ошибке. Если же на стороне PHP случился фатальный
`TypeError` (а он там возможен: `Abstract_WC_Yandex_Delivery_API_Response::get_error_message(): string`
вернёт `null`, если Яндекс прислал ошибку без поля `message`; то же с `get_offers(): array` и
`wc_yandex_delivery_get_actual_datetime()`, который может вернуть `null`), ответ вообще не JSON —
и результат для пользователя тот же самый: пустое окно.

## Почему мета не записалась

Единственное место в плагине, где эти поля создаются, — `includes/class-checkout.php`:

```php
add_filter( 'woocommerce_checkout_posted_data',      [ $this, 'prepare_checkout_posted_data' ] );
add_action( 'woocommerce_checkout_update_order_meta', [ $this, 'checkout_update_order' ], 10, 2 );
```

```php
public function checkout_update_order( int $order_id, array $data ) {
    ...
    if ( ! empty( $data['yandex_delivery'] ) ) {
        ...
        $order->set_state_status( 'NEW' );
        $order->save();
    }
}
```

То есть данные пишутся, только если заказ прошёл **через классический чекаут WooCommerce** и
в `$data` попал ключ `yandex_delivery`. Сам класс чекаута к тому же подключается с условием:

```php
if ( ! is_admin() ) {
    $this->frontend_includes();   // ← здесь создаётся WC_Yandex_Delivery_Checkout
}
```

Отсюда реальные сценарии, при которых мета не появится (любой из них даёт ровно вашу картину):

1. **Блочный чекаут / Store API.** Плагин прямо объявляет о несовместимости с блоками
   (`'blocks' => [ 'cart' => false, 'checkout' => false ]` в `woocommerce-yandex-delivery.php`).
   Хуки `woocommerce_checkout_posted_data` и `woocommerce_checkout_update_order_meta` в блочном
   оформлении не вызываются вообще.
2. **Оформление через `admin-ajax.php`** (быстрый заказ / купить в один клик / всплывающая корзина —
   на сайте установлена «Быстрая корзина»). В таком запросе `is_admin()` возвращает `true`,
   класс чекаута плагина не подключается, и его хуки не регистрируются.
3. **Заказ создан программно** — из личного кабинета, из обмена с 1С, повторный заказ и т. п.
4. **Фатальная ошибка другого плагина** на `woocommerce_checkout_update_order_meta` с приоритетом
   ниже 10 — тогда обработчик Яндекса до выполнения просто не доходит.

Что при этом важно: покупатель ПВЗ действительно выбирал, но выбор хранится в **сессии
WooCommerce** (`chosen_yandex_pickup_point`) и в скрытых полях формы (`yandex_pickup_point`),
а в заказ переносится только этим хуком. Сессия покупателя давно очищена, поэтому ПВЗ по заказу
восстановить автоматически уже нельзя — его придётся указать руками.

## Что проверить на сайте, чтобы подтвердить диагноз

WP-CLI (HPOS или классическое хранилище — команда одна):

```bash
wp wc order meta list 5917 --user=admin | grep yandex
# либо
wp eval '$o = wc_get_order(5917); foreach ($o->get_meta_data() as $m) { $d = $m->get_data(); if (strpos($d["key"], "yandex") !== false) { echo $d["key"], " = ", var_export($d["value"], true), PHP_EOL; } }'
```

SQL, если включён HPOS:

```sql
SELECT meta_key, meta_value FROM wp_wc_orders_meta
WHERE order_id = 5917 AND meta_key LIKE '%yandex%';
```

SQL для классического хранилища:

```sql
SELECT meta_key, meta_value FROM wp_postmeta
WHERE post_id = 5917 AND meta_key LIKE '%yandex%';
```

Ожидаемый результат: `_yandex_delivery_state_status` и `_yandex_delivery_destination_station_id`
отсутствуют совсем.

Дополнительно: включите **WooCommerce → Настройки → Интеграция → Яндекс доставка → Отладка**
(опция `debug`), после чего лог запросов к API будет в WooCommerce → Статус → Журналы. Там будет
видно точный ответ Яндекса на `offers/create`.

## Что делать

### Сейчас, чтобы отправить заказ #5917

1. Узнать у покупателя (или из письма/переписки), какой ПВЗ он выбирал.
2. Проставить заказу `_yandex_delivery_destination_station_id` (ID ПВЗ), адрес ПВЗ и
   `_yandex_delivery_state_status = NEW` — это делает плагин из этого репозитория
   (блок «Яндекс доставка — восстановление данных» на странице заказа).
3. После этого заказ появится в разделе «Заказы Я.Доставки», а кнопка
   «Выбрать и экспортировать» вернёт нормальный список дат.

Альтернатива без плагина — вручную, через WP-CLI:

```bash
wp eval '
$o = wc_get_order(5917);
$o->update_meta_data("_yandex_delivery_destination_station_id", "ID_ПВЗ_ИЗ_ЛК_ЯНДЕКСА");
$o->update_meta_data("_yandex_delivery_destination_station_address", "Москва, ул. ...");
$o->update_meta_data("_yandex_delivery_state_status", "NEW");
$o->save();
'
```

### Чтобы это не повторялось

1. **Проверить, каким чекаутом реально оформляются заказы.** Если страница оформления собрана
   на блоках (`WooCommerce Checkout` block) — вернуть классический шорткод `[woocommerce_checkout]`
   или отключить блочное оформление, иначе плагин Яндекс доставки работать не будет by design.
2. **Проверить «Быструю корзину» и подобные плагины быстрого заказа.** Если заказы оформляются
   через них, нужно либо отключить их для доставки Яндексом, либо оставить страховку из этого
   репозитория.
3. Держать включённой страховку `wc-yandex-delivery-order-repair` — она дописывает статус (и, если
   получится, ПВЗ) сразу после создания заказа любым способом, так что заказ как минимум
   не потеряется в админке.
4. Написать в поддержку Woodev (https://woodev.ru/support): в `admin-order.js` ошибка окна
   «Выбор варианта доставки» проглатывается, а в `get_error_message(): string` / `get_offers(): array`
   возможен фатальный `TypeError` — из-за этого причина сбоя не видна вообще нигде.
