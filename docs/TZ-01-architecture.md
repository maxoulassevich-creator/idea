# ТЗ. Часть 1 — Техническая архитектура

**Версия:** 0.2 · **Статус:** черновик для согласования, код не пишется
**Рабочее имя пакета:** `kursio` · Namespace `Kursio\` · Префикс таблиц `{$wpdb->prefix}krs_`

Имя заменяется в одном месте: константы `KURSIO_SLUG`, `KURSIO_NS`, `KURSIO_TABLE_PREFIX`.

**Изменения относительно v0.1:** разделение зачисления и основания доступа · привязка прогресса к циклу обучения · версионирование учебного контента · единственный источник истины для программы курса · разделение RequestContext и ComponentContext · разделение доменного события и дедупликации потребителей · синхронное обновление агрегата прогресса · явная регистрация REST для CPT · коллация через WordPress API · UTC как единственное представление времени · Action Scheduler как единственный драйвер очереди · типизированный безопасный вывод · расширенный тест паритета.

---

## 0. Исходные условия (зафиксированы владельцем)

1. Зарубежные коммерческие LMS и их платные дополнения официально недоступны целевой аудитории из РФ. Продукт даёт сопоставимый класс функций с легальной покупкой за рубли, обновлениями и поддержкой на русском.
2. Первая публичная версия содержит полный согласованный набор функций. Публичного урезанного MVP нет. Разделение на бесплатное ядро и платные дополнения выполняется после разработки и тестирования всего продукта.
3. Стоимость разработки не ограничивает функциональность. Качество, тестирование и совместимость — ограничивают.
4. Внешние проверки (стенды конкурентов, замеры, интервью) не выполняются. Решения принимаются из худшего предположения: у конкурентов паритет по любой обсуждаемой функции.

**Следствие условия 4:** ни одно проектное решение не зависит от несостоявшегося сравнения. Требование производительности формулируется как проверяемое клиентом на собственном сайте — *отключённый модуль не грузит ничего* — и как бюджеты запросов, проверяемые в CI.

---

## 1. Слои

```
┌──────────────────────────────────────────────────────────┐
│  Adapters:  Elementor │ Gutenberg │ Shortcode │ PHP API   │
├──────────────────────────────────────────────────────────┤
│  Presentation: ComponentRegistry → Component → Renderer   │
│                RequestContext → ComponentContext          │
├──────────────────────────────────────────────────────────┤
│  Domain: core access quiz assignment teacher woo ai       │
│          gamification notify certificate reports import   │
├──────────────────────────────────────────────────────────┤
│  Platform: Container · ModuleRegistry · Migrator · Queue  │
│            EventBus(outbox) · Cache · Policy · Audit      │
│            REST · License · Diagnostics · Privacy         │
├──────────────────────────────────────────────────────────┤
│  WordPress: CPT · Users · Options · REST · $wpdb          │
│  Bundled:   Action Scheduler                              │
└──────────────────────────────────────────────────────────┘
```

Обращение только вниз. Компонент не знает адаптера. Платформа не знает доменных модулей. Домен не знает презентации.

---

## 2. Модульная система

### 2.1. Контракт

```php
interface ModuleInterface {
    public static function id(): string;
    public static function dependsOn(): array;
    public static function requiresPlugins(): array;
    public static function migrations(): string;
    public function register(Container $c): void;   // только определения сервисов
    public function boot(EventBus $bus): void;      // только регистрация хуков
    public function assets(): array;                // декларация, не подключение
}
```

| Правило | Как проверяется |
|---|---|
| `register()` не обращается к БД, не добавляет хуков, не читает опций | статический анализ + тест |
| `boot()` вызывается только при включённом модуле и удовлетворённых зависимостях | тест |
| Отключённый модуль не регистрирует хуков, REST-маршрутов, задач, ассетов и не обращается к своим таблицам | тест изоляции |
| Ассеты подключаются только на страницах, где компонент модуля реально выведен | тест бюджета |
| Модуль не вызывает классы другого модуля напрямую — только через `platform/Contracts` или события | статический анализ |

### 2.2. Загрузка

Единственное чтение конфигурации при старте — автозагружаемая опция `krs_modules`:

```json
{"core":{"v":"1.0.0","on":true},"quiz":{"v":"1.0.0","on":true},"ai":{"v":"1.0.0","on":false}}
```

Классы грузятся PSR-4; отключённый модуль не инстанцируется. Незавершённые части внутри включённого модуля — за feature flags в опции `krs_flags`.

### 2.3. Зависимости

```
platform
└── core
    ├── access
    ├── frontend (core, access)
    ├── quiz (core, access)
    ├── assignment (core, access)
    │   └── teacher (assignment, frontend)
    ├── woo (core, access) + плагин WooCommerce
    ├── certificate (core, access)
    ├── reports (core, access)
    ├── gamification (core, access)   ← только потребитель событий
    ├── notify (core)                 ← только потребитель событий
    ├── ai (platform)                 ← поставщик услуги
    └── import (core, access; опционально quiz, assignment)
```

`gamification`, `notify`, `reports` не имеют обратных зависимостей — работают при любом наборе включённых модулей. Отсутствие `ai` не ломает ни один сценарий.

### 2.4. Multisite

**Network Activation в первой версии не поддерживается.** При попытке сетевой активации — блокировка с явным сообщением. Плагин активируется отдельно на каждом сайте сети; таблицы и миграции создаются по сайту. Частичное молчаливое поведение недопустимо.

---

## 3. Модель данных

### 3.1. Принцип разделения

| Что | Где | Почему |
|---|---|---|
| Контент: курсы, разделы, уроки, тесты, вопросы, задания, шаблоны сертификатов | CPT + таксономии | Редактор, ревизии, права, permalinks, поиск, Gutenberg, REST — бесплатно. Низкая кардинальность |
| Связи и состояние: зачисления, основания доступа, прогресс, попытки, ответы, работы, проверки, события, баллы | Собственные таблицы | Высокая кардинальность, диапазонные запросы, агрегация |

**Запрещено:** хранить прогресс, попытки и зачисления в `wp_postmeta` или `wp_usermeta` в любом виде.

### 3.2. Соглашения по всем таблицам

- `ENGINE=InnoDB` — требование; при невозможности установка блокируется с диагностикой.
- Коллация — **`$wpdb->get_charset_collate()`**, никаких жёстко заданных значений.
- Первичный ключ `id BIGINT UNSIGNED AUTO_INCREMENT`, если не указано иное.
- **Все даты хранятся в UTC.** API отдаёт ISO 8601 с зоной. Отображение — в часовом поясе сайта или пользователя. Изменение часового пояса сайта не меняет сохранённые моменты. Календарные правила (серии дней) считаются в выбранном в настройках поясе школы или пользователя.
- `created_at`, `updated_at DATETIME` — во всех таблицах.

### 3.3. Типы записей

| CPT | Иерархия | REST |
|---|---|---|
| `krs_course` | — | публично, только опубликованные, ограниченный набор полей |
| `krs_section` | parent = course | закрыто |
| `krs_lesson` | parent = section | **закрыто для гостей**, см. §3.9 |
| `krs_quiz` | parent = section \| course | закрыто |
| `krs_question` | — | закрыто |
| `krs_assignment` | parent = section \| lesson | закрыто |
| `krs_cert_template` | — | закрыто |
| `krs_bundle` | — | публично, ограниченный набор полей |

Таксономии: `krs_course_cat`, `krs_course_tag`, `krs_qbank`, `krs_level`.

### 3.4. Версионирование учебного контента

CPT-ревизий недостаточно: изменение вопроса, правильного ответа или веса критерия не должно менять смысл уже выставленных оценок.

```
krs_content_version
  subject_type VARCHAR(20)      -- course|curriculum|quiz|question|assignment|rubric
  subject_id BIGINT UNSIGNED
  version INT UNSIGNED
  snapshot_json JSON            -- неизменяемый снимок значимых для оценивания полей
  hash CHAR(40)
  published_at DATETIME NULL
  created_by BIGINT UNSIGNED
  UNIQUE KEY uq (subject_type, subject_id, version)
  KEY idx_subject (subject_type, subject_id, published_at)
```

Правила:

1. Публикация изменений оцениваемой сущности создаёт новую версию. Черновые правки версию не создают.
2. Попытка теста и сдача работы **фиксируют номер версии** и при отображении истории читают снимок, а не текущее состояние.
3. Удаление опубликованного вопроса или задания переводит его в состояние `archived` и **никогда не удаляет физически**, пока существуют ссылающиеся попытки.
4. Сертификат ссылается на `enrollment_id`, `course_version`, `completion_record_id`.

### 3.5. Зачисление и основание доступа — разделены

Одна сущность не может представлять одновременно «цикл обучения» и «право доступа»: покупка одного курса двумя заказами, комплект плюс отдельная покупка, ручной доступ поверх коммерческого, продление, повторное обучение в новом потоке и частичный возврат — всё это ломает единую строку.

```
krs_enrollment                       -- цикл обучения
  user_id, course_id BIGINT UNSIGNED
  cycle_no SMALLINT UNSIGNED
  group_id BIGINT UNSIGNED NULL
  status VARCHAR(20)          -- pending|active|paused|expired|completed
  started_at, completed_at DATETIME NULL
  UNIQUE KEY uq (user_id, course_id, cycle_no)
  KEY idx_course_status (course_id, status)
  KEY idx_group (group_id, status)
```

```
krs_access_grant                     -- основание доступа
  enrollment_id BIGINT UNSIGNED
  source VARCHAR(20)          -- manual|woo|invite|import|api|bulk
  source_ref VARCHAR(64)
  order_id, order_item_id BIGINT UNSIGNED NULL
  status VARCHAR(20)          -- pending|active|expired|revoked
  starts_at, expires_at, revoked_at DATETIME NULL
  revoke_reason VARCHAR(128) NULL
  UNIQUE KEY uq (source, source_ref, enrollment_id)
  KEY idx_enr_status (enrollment_id, status)
  KEY idx_expiry (status, expires_at)
  KEY idx_order (order_id, order_item_id)
```

**Правило эффективного доступа:** доступ существует, пока активен хотя бы один grant. `krs_enrollment.status` — производное состояние цикла, не источник права.

Повторная покупка не переписывает зачисление вслепую: по политике курса она либо продлевает действующий grant, либо создаёт новое основание. Новый поток создаёт новый `cycle_no`.

```
krs_enrollment_log
  enrollment_id, actor_id NULL, from_status, to_status, reason, meta_json
  KEY idx_enr (enrollment_id, created_at)
```

### 3.6. Программа курса — единственный источник истины

```
krs_curriculum
  course_id, item_id BIGINT UNSIGNED
  item_type VARCHAR(20)        -- section|lesson|quiz|assignment
  parent_id BIGINT UNSIGNED NULL
  position INT UNSIGNED
  depth TINYINT UNSIGNED
  is_gradable, is_required TINYINT(1)
  drip_rule_json, prereq_json JSON NULL
  curriculum_version INT UNSIGNED
  PRIMARY KEY (course_id, item_id)
  UNIQUE KEY uq_pos (course_id, parent_id, position)
  KEY idx_order (course_id, position)
  KEY idx_item (item_id)
```

**`krs_curriculum` — каноническая опубликованная структура. `post_parent` в CPT — только редакторское удобство и не является источником истины.**

Требования к `CurriculumService` — единственному, кому разрешено писать в эту таблицу:

- обновление в транзакции;
- проверка отсутствия циклов;
- гарантия уникальности позиции;
- создание новой `curriculum_version` при публикации;
- команда сверки `post_parent` ↔ `krs_curriculum` с отчётом о расхождениях;
- инструмент восстановления структуры из CPT;
- запрет прямой записи структурных связей мимо сервиса (проверяется статическим анализом).

### 3.7. Прогресс — принадлежит циклу обучения

Уникальность по `user_id` ломается, когда урок используется в двух курсах, тест переиспользуется или ученик проходит курс повторно.

```
krs_progress
  enrollment_id, item_id BIGINT UNSIGNED
  item_type VARCHAR(20)
  status VARCHAR(20)           -- not_started|in_progress|completed
  pct TINYINT UNSIGNED
  position_s INT UNSIGNED
  first_seen_at, completed_at DATETIME NULL
  UNIQUE KEY uq (enrollment_id, item_id)
  KEY idx_enr_status (enrollment_id, status)
  KEY idx_recent (enrollment_id, updated_at)
```

```
krs_course_progress
  enrollment_id BIGINT UNSIGNED PRIMARY KEY
  user_id, course_id BIGINT UNSIGNED
  items_total, items_done, items_required, items_required_done SMALLINT UNSIGNED
  pct TINYINT UNSIGNED
  last_item_id BIGINT UNSIGNED NULL
  last_activity_at, completed_at DATETIME NULL
  calculated_at DATETIME
  source_version INT UNSIGNED       -- curriculum_version, по которой считали
  is_dirty TINYINT(1)
  KEY idx_user_recent (user_id, last_activity_at)
  KEY idx_course_pct (course_id, pct)
```

**Обновление агрегата — синхронное.** Переход состояния элемента в той же транзакции инкрементально обновляет `items_done`, `pct`, `last_item_id`, `last_activity_at`, `calculated_at`. Пользователь никогда не видит устаревший процент и устаревшую кнопку.

Фоновая задача выполняет **сверку и восстановление**: полный пересчёт для строк с `is_dirty = 1`, с устаревшим `source_version` или по расписанию. После массового изменения программы курса все затронутые строки помечаются `is_dirty`. Дополнительно — команда полного пересчёта для администратора и CLI.

### 3.8. Тесты

```
krs_quiz_attempt
  enrollment_id, quiz_id BIGINT UNSIGNED
  attempt_no SMALLINT UNSIGNED
  quiz_version INT UNSIGNED
  status VARCHAR(20)           -- in_progress|submitted|grading|graded|expired
  score_raw, score_max DECIMAL(8,2) NULL
  score_pct DECIMAL(5,2) NULL
  passed TINYINT(1) NULL
  time_spent_s INT UNSIGNED NULL
  started_at, submitted_at, graded_at, expires_at DATETIME NULL
  UNIQUE KEY uq (enrollment_id, quiz_id, attempt_no)
  KEY idx_quiz_status (quiz_id, status)
  KEY idx_pending (status, submitted_at)
```

```
krs_quiz_answer
  attempt_id, question_id BIGINT UNSIGNED
  question_version INT UNSIGNED
  question_snapshot_json JSON       -- формулировка и правила оценки на момент попытки
  question_type VARCHAR(20)
  answer_json JSON
  is_correct TINYINT(1) NULL        -- NULL = ждёт ручной проверки
  points_awarded, points_max DECIMAL(8,2)
  grader_id BIGINT UNSIGNED NULL
  graded_at DATETIME NULL
  remarks LONGTEXT NULL
  ai_draft_id BIGINT UNSIGNED NULL
  UNIQUE KEY uq (attempt_id, question_id)
  KEY idx_manual_queue (is_correct, graded_at)
```

Попытка неизменяема после `submitted`. Изменяются только поля проверки.

### 3.9. Домашние работы — корень и попытки разделены

Переход `returned → submitted(attempt_no+1)` в v0.1 означал одновременное изменение старой строки и создание новой, что не было определено. Разделяю.

```
krs_submission                        -- корень работы
  enrollment_id, assignment_id BIGINT UNSIGNED
  course_id BIGINT UNSIGNED
  status VARCHAR(20)           -- draft|submitted|in_review|returned|accepted|rejected
  current_attempt_no SMALLINT UNSIGNED
  assignee_id BIGINT UNSIGNED NULL
  claimed_at, deadline_at, first_submitted_at, closed_at DATETIME NULL
  UNIQUE KEY uq (enrollment_id, assignment_id)
  KEY idx_queue (status, first_submitted_at)
  KEY idx_assignee (assignee_id, status)
  KEY idx_assignment (assignment_id, status)
```

```
krs_submission_attempt                -- неизменяемая попытка
  submission_id BIGINT UNSIGNED
  attempt_no SMALLINT UNSIGNED
  assignment_version INT UNSIGNED
  body LONGTEXT NULL
  payload_json JSON NULL       -- файлы, ссылки, метаданные
  submitted_at DATETIME
  UNIQUE KEY uq (submission_id, attempt_no)
  KEY idx_time (submitted_at)
```

```
krs_review                            -- относится к попытке, не к корню
  submission_attempt_id, reviewer_id BIGINT UNSIGNED
  decision VARCHAR(20)         -- accepted|returned|rejected
  score DECIMAL(8,2) NULL
  feedback LONGTEXT NULL
  rubric_version INT UNSIGNED NULL
  rubric_json JSON NULL
  ai_draft_id BIGINT UNSIGNED NULL
  ai_edit_ratio DECIMAL(5,4) NULL
  active_review_time_s INT UNSIGNED NULL
  KEY idx_attempt (submission_attempt_id)
  KEY idx_reviewer (reviewer_id, created_at)
```

### 3.10. Рубрики

```
krs_rubric
  subject_type VARCHAR(20)     -- assignment|question
  subject_id BIGINT UNSIGNED
  version INT UNSIGNED
  title VARCHAR(255), is_active TINYINT(1)
  UNIQUE KEY uq (subject_type, subject_id, version)

krs_rubric_criterion
  rubric_id BIGINT UNSIGNED, position SMALLINT
  title VARCHAR(255), weight DECIMAL(5,2)
  levels_json JSON              -- выполнено / частично / не выполнено, с признаками
  common_errors_json JSON
  KEY idx_rubric (rubric_id, position)
```

### 3.11. События — transactional outbox

```
krs_event
  name VARCHAR(64)
  user_id, course_id, enrollment_id BIGINT UNSIGNED NULL
  subject_type VARCHAR(20) NULL, subject_id BIGINT UNSIGNED NULL
  payload_json JSON NULL
  occurred_at DATETIME(3)
  dispatched_at DATETIME NULL
  KEY idx_name_time (name, occurred_at)
  KEY idx_user_time (user_id, occurred_at)
  KEY idx_outbox (dispatched_at, occurred_at)
```

```
krs_consumer_dedupe
  consumer VARCHAR(32)          -- points|notification|ai|report
  dedupe_key CHAR(64)
  event_id BIGINT UNSIGNED
  processed_at DATETIME
  UNIQUE KEY uq (consumer, dedupe_key)
  KEY idx_event (event_id)
```

**Доменное событие и дедупликация потребителя — разные вещи.** Единый ключ идемпотентности в v0.1 не мог одновременно обслуживать баллы, уведомления и ИИ.

| Потребитель | Ключ дедупликации |
|---|---|
| Баллы | `event_id + rule_id + beneficiary_id` |
| Уведомление | `event_id + notification_rule_id + recipient_id + channel` |
| ИИ | `submission_attempt_id + rubric_version + provider + model + prompt_version + generation_no` |
| Отчёты | `event_id + report_slug` |

Ключ ИИ включает `generation_no` — **осознанная повторная генерация разрешена**, случайная исключена. Повторная генерация ограничена лимитом на работу и общим лимитом расходов школы.

**Outbox:** запись события и изменение доменного состояния происходят в одной транзакции. После фиксации диспетчер публикует событие как минимум один раз; каждый потребитель идемпотентен по своему ключу.

### 3.12. Баллы

```
krs_points_ledger
  user_id BIGINT UNSIGNED
  delta INT, reason VARCHAR(64)
  event_id BIGINT UNSIGNED NULL, rule_id BIGINT UNSIGNED NULL
  balance_after INT
  UNIQUE KEY uq (user_id, reason, event_id, rule_id)
  KEY idx_user_time (user_id, created_at)
```

```
krs_user_stats
  user_id BIGINT UNSIGNED PRIMARY KEY
  xp_total INT, level SMALLINT
  streak_days SMALLINT, streak_last_date DATE NULL
  achievements_count SMALLINT
  KEY idx_xp (xp_total)
```

**Порядок в одной транзакции** (`balance_after` сам по себе гонку не исключает):

1. `SELECT ... FOR UPDATE` либо атомарный `UPDATE krs_user_stats SET xp_total = xp_total + :delta`.
2. Вставка строки реестра.
3. Фиксация `balance_after` из обновлённого значения.
4. Запись события в outbox.

Повтор операции отсекается ключом дедупликации потребителя до входа в транзакцию.

### 3.13. Группы и проверяющие

```
krs_group
  course_id BIGINT UNSIGNED NULL
  title VARCHAR(255)
  starts_at, ends_at DATETIME NULL
  capacity SMALLINT UNSIGNED NULL
  status VARCHAR(20)
  KEY idx_course (course_id, status)

krs_group_member
  group_id, user_id BIGINT UNSIGNED, joined_at
  UNIQUE KEY uq (group_id, user_id)
  KEY idx_user (user_id)

krs_group_staff                      -- проверяющих может быть несколько
  group_id, user_id BIGINT UNSIGNED
  role VARCHAR(20)             -- curator|instructor|observer
  priority SMALLINT, is_active TINYINT(1)
  UNIQUE KEY uq (group_id, user_id, role)
  KEY idx_user_active (user_id, is_active)
```

Одиночное поле `curator_id` удалено.

### 3.14. Приглашения

```
krs_invite
  code_prefix CHAR(8)          -- публичная часть для поиска
  code_hash CHAR(64)           -- хеш полного кода
  target_type VARCHAR(20), target_id BIGINT UNSIGNED
  uses_max, uses_count SMALLINT UNSIGNED
  duration_days SMALLINT NULL
  expires_at DATETIME NULL
  revoked_at DATETIME NULL
  created_by BIGINT UNSIGNED
  UNIQUE KEY uq_prefix (code_prefix)
  KEY idx_target (target_type, target_id)

krs_invite_redemption
  invite_id, user_id BIGINT UNSIGNED, access_grant_id BIGINT UNSIGNED NULL
  ip_hash CHAR(64) NULL, redeemed_at
  UNIQUE KEY uq (invite_id, user_id)
```

Рабочий код открытым текстом не хранится. Счётчик использований обновляется атомарно (`UPDATE ... SET uses_count = uses_count + 1 WHERE id = ? AND uses_count < uses_max`), результат проверяется по числу затронутых строк.

### 3.15. Коммерция

```
krs_woo_link
  product_id, variation_id BIGINT UNSIGNED NULL
  target_type VARCHAR(20)      -- course|bundle
  target_id BIGINT UNSIGNED
  grant_statuses, revoke_statuses JSON
  duration_days SMALLINT NULL, group_id BIGINT UNSIGNED NULL
  renewal_policy VARCHAR(20)   -- extend|new_grant|new_cycle
  KEY idx_product (product_id, variation_id)

krs_woo_grant                        -- аудит операций
  order_id, order_item_id BIGINT UNSIGNED
  user_id, course_id BIGINT UNSIGNED
  access_grant_id BIGINT UNSIGNED NULL
  action VARCHAR(20)           -- grant|revoke|restore|failed|manual_review
  reason VARCHAR(128) NULL
  order_status VARCHAR(32)
  KEY idx_order (order_id)
  KEY idx_user_course (user_id, course_id)
  KEY idx_failed (action, created_at)
```

`krs_woo_grant` — журнал `заказ → пользователь → курс → доступ` и основа диагностики «оплата прошла, ученик не зачислен». Ссылается на `access_grant_id`, а не на зачисление.

### 3.16. ИИ

```
krs_ai_request
  module, operation VARCHAR(32)
  subject_type VARCHAR(20), subject_id BIGINT UNSIGNED
  provider VARCHAR(32), model VARCHAR(64), prompt_version VARCHAR(16)
  status VARCHAR(20), error_code VARCHAR(40) NULL
  tokens_in, tokens_out, cost_minor, latency_ms INT UNSIGNED NULL
  KEY idx_subject (subject_type, subject_id)
  KEY idx_time (created_at)

krs_ai_draft
  subject_type VARCHAR(20), subject_id BIGINT UNSIGNED
  rubric_version INT UNSIGNED, generation_no SMALLINT UNSIGNED
  draft_text LONGTEXT, structure_json JSON
  confidence VARCHAR(10), needs_attention TINYINT(1)
  ai_request_id BIGINT UNSIGNED
  KEY idx_subject (subject_type, subject_id, generation_no)
```

**Тексты промптов и ответов модели не сохраняются.** Хранится черновик, привязанный к попытке работы и удаляемый вместе с ней.

### 3.17. Сертификаты

```
krs_certificate
  enrollment_id BIGINT UNSIGNED
  user_id, course_id, template_id BIGINT UNSIGNED
  course_version INT UNSIGNED
  completion_record_id BIGINT UNSIGNED
  serial VARCHAR(32) UNIQUE
  status VARCHAR(20)           -- pending|issued|expired|revoked
  file_path VARCHAR(255) NULL, file_hash CHAR(64) NULL
  issued_by BIGINT UNSIGNED NULL
  issued_at, expires_at, revoked_at DATETIME NULL
  revoke_reason VARCHAR(128) NULL
  KEY idx_user (user_id)
  KEY idx_enr (enrollment_id)
  KEY idx_status (status, expires_at)
```

Состояния: `pending → issued → expired | revoked`.

### 3.18. Уведомления, миграции, аудит

```
krs_notification         event_name, channel, audience, template_id, delay_s, conditions_json, is_active
krs_notification_log     notification_id, user_id, channel, status, dedupe_key CHAR(64) UNIQUE, error, sent_at
krs_migration            module, version, checksum CHAR(40), applied_at  UNIQUE (module, version)
krs_audit_log            actor_id, action, subject_type, subject_id, ip_hash, ua_hash, meta_json
                         KEY (action, created_at), KEY (subject_type, subject_id), KEY (actor_id, created_at)
```

### 3.19. Ретенция — предварительные значения по умолчанию

| Таблица | По умолчанию | Правило |
|---|---|---|
| `krs_event` | 180 дней | не удаляются недоставленные (`dispatched_at IS NULL`) |
| `krs_consumer_dedupe` | **не раньше окна повторной доставки** | иначе повторная доставка создаст дубль |
| `krs_ai_request` | 365 дней | нужна для сверки расходов |
| `krs_audit_log` | 365 дней, ниже не опускается | |
| `krs_notification_log` | 90 дней | |
| Незавершённые работы, попытки, задачи | **не удаляются ретенцией никогда** | |

Значения настраиваемые, **предварительные, не являются юридическими требованиями**. Очистка — фоновой задачей пакетами.

---

## 4. Роли, права, политики

### 4.1. Роли

`krs_student` · `krs_curator` · `krs_instructor` · `krs_author` · `administrator`

### 4.2. Возможности

```
Контент:     krs_edit_courses · krs_edit_others_courses · krs_publish_courses
             krs_delete_courses · krs_manage_curriculum
Зачисления:  krs_manage_enrollments · krs_view_enrollments · krs_bulk_enroll
             krs_manage_invites · krs_manage_groups
Проверка:    krs_view_submissions · krs_grade_submissions · krs_reassign_submissions
Тесты:       krs_manage_questions · krs_manage_qbank · krs_grade_quizzes
Отчёты:      krs_view_reports · krs_view_all_reports · krs_export_reports
ИИ:          krs_use_ai · krs_manage_ai_settings
Коммерция:   krs_manage_woo_links · krs_view_grant_log · krs_manual_grant
Сертификаты: krs_issue_certificates · krs_revoke_certificates
Система:     krs_manage_settings · krs_manage_modules · krs_manage_license
             krs_view_audit · krs_export_user_data · krs_erase_user_data
```

### 4.3. Политики — обязательный слой

```php
interface PolicyInterface {
    public function can(int $userId, string $action, ?object $subject = null): bool;
}
```

| Действие | Что проверяется помимо capability |
|---|---|
| `grade_submission` | назначенный проверяющий, либо активный `krs_group_staff` группы ученика, либо преподаватель курса |
| `view_submission` | то же, либо автор работы |
| `view_lesson` | активный grant + правило капельной выдачи + предусловия (логическое И) |
| `download_attachment` | право на элемент, к которому относится файл |
| `view_report` | своя группа или курс, если нет `krs_view_all_reports` |
| `edit_course` | автор курса, если нет `krs_edit_others_courses` |
| `use_ai` | модуль включён + согласие школы получено + лимит не исчерпан |

**`current_user_can()` без объекта запрещён для любых операций с чужими данными.** Каждый REST-маршрут и AJAX-обработчик вызывает `Policy::can()` с объектом.

---

## 5. Состояния

Переходы — только через методы сервисов. Прямая запись поля `status` запрещена и ловится тестом. Недопустимый переход — исключение.

```
Enrollment (цикл)      pending → active ⇄ paused → expired | completed
AccessGrant            pending → active → expired | revoked
QuizAttempt            in_progress → submitted → grading → graded | expired
Submission (корень)    draft → submitted → in_review → returned → submitted | accepted | rejected
SubmissionAttempt      неизменяема после создания
Certificate            pending → issued → expired | revoked
```

Каждый переход публикует событие в outbox в той же транзакции и пишет в лог сущности.

---

## 6. Шина событий

Именование: `kursio/{module}.{entity}.{action}`, глагол в прошедшем времени.

| Модуль | События |
|---|---|
| `access` | `enrollment.created` `.activated` `.paused` `.resumed` `.expired` `.completed` · `grant.created` `.activated` `.expired` `.revoked` · `invite.redeemed` · `group.member_added` `.member_removed` |
| `core` | `lesson.started` `.completed` · `course.started` `.progress_changed` `.completed` · `curriculum.published` |
| `quiz` | `attempt.started` `.submitted` `.graded` · `quiz.passed` `.failed` · `answer.manually_graded` |
| `assignment` | `submission.created` · `attempt.submitted` · `submission.claimed` `.returned` `.accepted` `.rejected` · `deadline.approaching` `.missed` |
| `ai` | `draft.requested` `.ready` `.failed` · `quota.exceeded` |
| `woo` | `order.linked` · `grant.issued` `.revoked` `.restored` · `grant.failed` |
| `gamification` | `points.awarded` · `level.reached` · `streak.extended` `.broken` · `achievement.unlocked` |
| `certificate` | `certificate.issued` `.revoked` `.expired` |

**Доставка:** синхронно — только то, что влияет на текущий ответ (доступность следующего шага, агрегат прогресса). Всё остальное — через очередь. Медленный Telegram никогда не задерживает завершение урока. Ошибка потребителя не откатывает событие и не ломает соседей.

---

## 7. Контекст

### 7.1. Два уровня — обязательное разделение

Один мемоизированный на запрос объект контекста возвращал бы неверный курс уже второй карточке в каталоге из двадцати курсов. Это дефект v0.1.

```php
final class RequestContext {          // вычисляется один раз за запрос
    public ?WP_User $user;
    public RouteSignature $route;     // тип страницы, query vars, главный объект
    public ?int $primaryCourseId;     // если маршрут однозначно указывает на курс
}

final class ComponentContext {        // вычисляется для конкретного вызова компонента
    public ?WP_User $user;
    public ?Course $course;
    public ?Section $section;
    public ?Lesson $lesson;
    public ?Quiz $quiz;
    public ?Assignment $assignment;
    public ?Enrollment $enrollment;
    public ?AccessGrant $effectiveGrant;
    public ?CourseProgress $progress;
    public ?NextStep $nextStep;
    public string $source;            // explicit|route|loop|request|activity|single|none
    public string $confidence;        // exact|inferred|none
}
```

Кэш `ComponentContext` — по нормализованному ключу:
`component_id + course_id + lesson_id + user_id + route_signature`.

### 7.2. Источники разрешения

| № | Источник | `source` | confidence |
|---|---|---|---|
| 1 | Явные свойства компонента | `explicit` | exact |
| 2 | Маршрут и query vars | `route` | exact |
| 3 | **Текущая запись цикла** (`the_post()` внутри каталога или произвольного цикла) | `loop` | exact |
| 4 | Параметр запроса с проверкой прав | `request` | exact |
| 5 | Последняя активность пользователя | `activity` | inferred |
| 6 | Единственный курс на сайте | `single` | inferred |
| 7 | Ничего | `none` | none |

Источник 3 добавлен отдельно и **переоценивается для каждого экземпляра компонента**: именно он делает корректным каталог из двадцати карточек.

### 7.3. Политика контекста компонента — обязательное свойство схемы

| Политика | Разрешённые источники | Примеры компонентов |
|---|---|---|
| `explicit_required` | 1 | встраивание конкретного курса в лендинг |
| `route_or_explicit` | 1, 2 | `lesson_content`, `quiz` |
| `loop_or_route_or_explicit` | 1, 2, 3 | `course_price`, `course_curriculum`, `course_card` |
| `personal_activity_allowed` | 1–5 | `continue_learning`, `my_deadlines` |
| `global_allowed` | 1–6 | `catalog`, `points_badge` |

Последняя активность допустима для «Продолжить обучение» и **недопустима** для цены курса, программы и карточки — это ровно та ошибка, которая давала бы посетителю цену чужого курса.

### 7.4. Правила

- `RequestContext` — один раз за запрос. `ComponentContext` — по ключу, с кэшем.
- Ленивая загрузка по группам полей: пока компонент не спросил прогресс, запрос не выполняется. Разрешение контекста без обращения к полям — ноль запросов к БД.
- Деградация по `confidence` обязательна: при `inferred` — обобщённая формулировка, при `none` — запасной вариант или пустой вывод. Никогда ошибка, никогда чужие данные.
- Явное всегда перекрывает автоматику; обратное запрещено.
- Для анонимного пользователя контекст разрешается без обращения к пользовательским таблицам.

---

## 8. Компоненты и рендеринг

### 8.1. Схема — единственный источник истины

```php
final class ComponentSchema {
    public string $id;
    public string $title;
    public string $category;
    public array  $requires;         // ['course']
    public string $contextPolicy;    // см. §7.3
    public array  $props;            // PropSchema[]
    public array  $states;
    public bool   $dynamic;          // зависит от пользователя
    public array  $assets;
}
```

### 8.2. Генерация адаптеров — на этапе сборки

Из схемы **во время сборки** генерируются: `block.json` и метаданные PHP для Gutenberg, описание элементов управления Elementor, атрибуты и валидация шорткода, сигнатура PHP API, документация.

**Во время обычного запроса WordPress файлы не создаются.** Регистрация блока идёт через `block.json`, как предписывает Block API.

### 8.3. Разделение ответственности

```php
interface ComponentInterface {
    public static function schema(): ComponentSchema;
    public function resolve(ComponentContext $ctx, array $props): ViewModel;
}

interface RendererInterface {
    public function render(ViewModel $vm, string $template): string;
}
```

1. `resolve()` не содержит HTML, не вызывает `echo`, не знает адаптера. Ни одной ветки «если Elementor».
2. Адаптер не содержит бизнес-логики: нормализует свойства и вызывает компонент.
3. Шаблоны переопределяются темой по пути `theme/kursio/{component}.php`; ViewModel при этом не меняется.

### 8.4. Типизированный безопасный вывод

Формулировка v0.1 «ViewModel хранит сырые данные, экранирование в шаблоне» создаёт риск XSS в переопределённых темой шаблонах. Вводятся типы:

```php
PlainText       — экранируется esc_html при выводе
AttributeValue  — esc_attr
UrlValue        — esc_url, с проверкой схемы
SafeHtml        — единственный тип, выводимый без экранирования;
                  создаётся ТОЛЬКО фабрикой после wp_kses с явной схемой тегов
```

Всё, что не помечено `SafeHtml`, считается недоверенным и экранируется при выводе. Шаблон, выводящий значение без типа, — ошибка статического анализа.

### 8.5. Тест паритета — пять уровней

Сравнения ViewModel недостаточно: ViewModel строит общий компонент, а не адаптер. Проверяются:

1. Нормализация свойств каждым адаптером (одинаковый вход → одинаковые нормализованные props).
2. Результирующий ViewModel.
3. Итоговый HTML общего рендерера.
4. Регистрация элементов управления и атрибутов (Elementor controls, block attributes, shortcode atts).
5. Минимальный сквозной тест в реальном Elementor и реальном редакторе блоков.

Расхождение на любом уровне — падение сборки.

### 8.6. Состав компонентов v1

| Группа | Компоненты |
|---|---|
| Курс | `course_action` · `course_progress` · `course_curriculum` · `course_meta` · `course_price` · `course_instructor` |
| Урок | `lesson_content` · `lesson_nav` · `lesson_complete` · `lesson_attachments` |
| Каталог | `catalog` · `catalog_filters` · `course_card` |
| Кабинет | `my_courses` · `continue_learning` · `my_deadlines` · `my_certificates` · `my_results` |
| Оценивание | `quiz` · `quiz_results` · `assignment_form` · `assignment_status` · `submission_history` |
| Преподаватель | `teacher_queue` · `teacher_review` · `teacher_stats` |
| Геймификация | `points_badge` · `level_bar` · `streak` · `achievements` · `leaderboard` |
| Служебные | `conditional_wrap` · `enroll_button` · `invite_form` |

Полная спецификация — в `TZ-03-components.md`.

---

## 9. Очередь

**Action Scheduler поставляется в составе платформенного модуля** — он предназначен для распространения внутри плагинов, даёт журналируемую очередь, аренду задач, атомарный захват и повторные попытки. Собственный табличный драйвер из v0.1 удалён: две реализации с разной семантикой — источник расхождений, который нам не нужен.

Тонкая абстракция сохраняется, чтобы не быть привязанными навсегда:

```php
interface QueueInterface {
    public function push(string $handler, array $payload, ?string $uniqueKey = null, int $delay = 0): void;
    public function isScheduled(string $handler, array $payload): bool;
}
```

**Дедупликация задач не полагается на уникальный индекс** — она выполняется через `krs_consumer_dedupe` до постановки задачи, поэтому завершённая задача никогда не блокирует законную повторную.

**Запуск:** системный cron рекомендуется, инструкция выдаётся мастером установки. Диагностика показывает фактический режим, время последнего прогона и предупреждает, если задачи не выполняются или недоступны loopback-запросы. Дополнительно — защищённый REST-эндпоинт для внешнего триггера.

**Задачи:** сверка агрегата прогресса, начисление баллов и достижений, отправка уведомлений, генерация ИИ-черновика, генерация PDF сертификата, истечение оснований доступа, пересчёт серий, импорт пакетами, очистка по ретенции.

Правила: ни одна тяжёлая операция не выполняется в веб-запросе; задача, не завершившаяся за 30 секунд, продолжается с сохранённой позиции; после исчерпания попыток — уведомление администратору.

---

## 10. Кэширование

### 10.1. Уровни

| Уровень | Что |
|---|---|
| Денормализация в БД | `krs_course_progress`, `krs_user_stats`, `krs_curriculum` — часть модели, не кэш |
| Объектный кэш | группа `kursio`, per-request всегда, персистентный при наличии |
| Фрагментный | каталог, программа, карточки — только для не-пользовательских ViewModel |

### 10.2. Инвалидация

Ключ включает метку версии сущности: `krs:course:{id}:v{n}:curriculum`. Запись увеличивает `n`; старые ключи становятся недостижимыми и умирают по TTL. Ручной сброс не требуется и не предусмотрен.

### 10.3. Динамические компоненты и страничный кэш

Отдавать всем посетителям страницу из общего кэша со встроенным `wp_rest` nonce нельзя — nonce привязан к пользователю, иначе запрос обрабатывается как анонимный.

Правила:

1. **Для авторизованных пользователей страничный кэш обходится** — это поведение по умолчанию у распространённых кэширующих плагинов и оно объявляется требованием. Диагностика предупреждает, если кэш настроен иначе.
2. Для анонимных динамические компоненты рендерят гостевое состояние **статически**, без обращений к REST.
3. Если сайт всё же кэширует для авторизованных, включается режим начальной загрузки: один некэшируемый запрос отдаёт nonce и состояние пользователя, затем **один батч-запрос** наполняет все динамические компоненты страницы сразу.
4. Батч-эндпоинт: обязательный `permission_callback`, заголовки `Cache-Control: private, no-store`, запрет кэширования на CDN, ответ никогда не содержит данных другого пользователя.

---

## 11. Миграции

### 11.1. Механика

`modules/{id}/migrations/{version}_{name}.php`. `Migrator` сверяется с `krs_migration`, применяет недостающие по порядку, пишет контрольную сумму. Изменение уже применённой миграции — ошибка установки, а не тихое расхождение схемы. Установка и обновление выполняются под блокировкой.

### 11.2. Изменение больших таблиц

Формулировка v0.1 «`ALTER` выполняется фоновой задачей пакетами» технически неверна: сам `ALTER TABLE` на пакеты не делится. Правильная последовательность:

1. Создать новую колонку или таблицу (быстрая, неблокирующая операция).
2. Включить двойную запись.
3. Скопировать данные пакетами фоновой задачей.
4. Проверить совпадение.
5. Переключить чтение.
6. Удалить старую структуру в следующем релизе.

Для особо крупных изменений — теневая таблица с последующим атомарным переименованием.

### 11.3. Деактивация и удаление

Деактивация: снимаются хуки и расписания, данные сохраняются. Удаление: только по явному подтверждению с перечнем удаляемого и предложением выгрузки; по умолчанию данные остаются.

---

## 12. Безопасность

| Область | Требование |
|---|---|
| REST для CPT | Для каждого типа явно задаются `public`, `publicly_queryable`, `show_in_rest`, `supports`, `capability_type`, `map_meta_cap`, REST-контроллер и доступ к ревизиям. **`krs_lesson`, `krs_question`, `krs_assignment`, рубрики и ответы закрыты для гостей**; платный урок не раскрывает `post_content` через стандартный маршрут |
| REST | `permission_callback` обязателен и никогда `__return_true`; внутри — `Policy::can()` с объектом |
| AJAX | nonce + capability + политика; отдельные nonce для изменяющих действий |
| SQL | только `$wpdb->prepare`; имена таблиц и колонок — из белого списка |
| Загрузки | белый список MIME **и** расширения, лимит размера, переименование файла |
| Хранение файлов | вне корня сайта либо в защищённом каталоге; выдача через PHP по подписанной ссылке с проверкой политики. Прямые ссылки на работы запрещены |
| Видео | подписанные ссылки с коротким сроком жизни |
| Лимиты | сдача работы, попытка теста, обращение к ИИ, активация приглашения, вход |
| Сериализация | `unserialize()` запрещён; только JSON с проверкой схемы |
| Вывод | типизированный, см. §8.4 |
| Секреты | ключи ИИ и лицензии зашифрованы, ключ шифрования из константы `wp-config.php`; никогда в автозагружаемых опциях, выгрузках и журналах |
| Аудит | обязателен для изменения зачислений, оснований доступа, оценок, ролей, настроек, лицензий, выгрузки и удаления данных |
| Приватность | зарегистрированы экспортёр и стиратель WordPress для всех таблиц; выгрузка по белому списку полей |
| ИИ | к модели уходят только необходимые фрагменты урока, задание, версия рубрики, ответ и настройки отзыва. Имя, почта, телефон, IP, номер заказа и прочие идентификаторы не передаются. Передача оригинальных файлов включается отдельным согласием |

**Отказ модуля `ai`, недоступность провайдера или лицензионного сервера не блокируют ни один учебный или платёжный сценарий.** Проверяется тестом с недоступной сетью.

---

## 13. Тестовая стратегия

| Вид | Что покрывает | Порог |
|---|---|---|
| Модульные | сервисы, машины состояний, политики, капельная выдача, начисление баллов | покрытие доменной логики ≥ 80% |
| Интеграционные | каждый модуль с БД в тестовом окружении WordPress | все сценарии функциональной части |
| Контрактные | паритет адаптеров, пять уровней (§8.5) | 100%, падение сборки при расхождении |
| Изоляции | включён только модуль X → ноль хуков, маршрутов, задач, ассетов остальных | 100% модулей |
| Миграционные | чистая установка + обновление с каждой выпущенной версии | все версии |
| Конкурентности | одновременное начисление баллов, повторная доставка события, параллельная миграция | ноль дублей и потерянных обновлений |
| Бюджет производительности | лимит запросов и памяти на тип страницы | превышение = падение сборки |
| Совместимости | с/без WooCommerce, объектного кэша, страничного кэша, Elementor | матрица |
| Безопасности | неэкранированный вывод, неподготовленные запросы, отсутствие `permission_callback`, прямой доступ к файлам, REST-утечка закрытых CPT | ноль нарушений |
| Отказоустойчивости | недоступны сеть, ИИ-провайдер, лицензионный сервер, очередь | LMS полностью работоспособна |

### Бюджеты запросов — предварительные, уточняются после первых замеров

| Страница | Запросов к БД | Пиковая память |
|---|---|---|
| Каталог, 20 карточек | ≤ 25 | ≤ 24 МБ |
| Курс, гость | ≤ 20 | ≤ 24 МБ |
| Курс, зачисленный | ≤ 30 | ≤ 32 МБ |
| Урок | ≤ 25 | ≤ 32 МБ |
| Кабинет ученика | ≤ 30 | ≤ 32 МБ |
| Очередь преподавателя, 50 работ | ≤ 35 | ≤ 40 МБ |

Эти цифры, а не сравнение с конкурентами, операционализируют требование лёгкости. Проверяются в CI на каждом коммите.

### Матрица окружений

**PHP 8.2 / 8.3 / 8.4 / 8.5** — ветка 8.1 завершила поддержку 31 декабря 2025 года. Минимальная версия на момент релиза = старейшая ветка, получающая обновления безопасности; матрица пересматривается перед выпуском.
WordPress — две последние мажорные версии · MySQL 5.7 и 8.0, MariaDB 10.6+ · с объектным кэшем и без · с WooCommerce и без · с Elementor и без · с страничным кэшем и без.

### Телеметрия

`ai_edit_ratio` — приблизительная метрика, `active_review_time_s` учитывает только активное время и отсекает простой. **Локальная статистика доступна школе всегда; передача агрегированных данных разработчику — только по явному включению.** Эти метрики измеряют использование установленного продукта и не являются измерением рыночного спроса.
