# ТЗ. Часть 1 — Техническая архитектура

**Версия:** 0.3 · **Статус:** черновик для согласования, код не пишется
**Пакет:** `kursio` · Namespace `Kursio\` · Префикс таблиц `{$wpdb->prefix}krs_`

Имя заменяется в одном месте: `KURSIO_SLUG`, `KURSIO_NS`, `KURSIO_TABLE_PREFIX`.

**Изменения 0.2 → 0.3:** переработана семантика дедупликации (устранена потеря операции) · единая формула доступа к контенту · правила конкуренции циклов обучения · тринадцать недостающих сущностей · исправлены уникальные индексы с NULL · восстановление основания доступа в машине состояний · версия программы фиксируется за циклом · `Policy::scope()` для коллекций · только WooCommerce CRUD API и HPOS · точная схема начальной загрузки при страничном кэше · переработана стратегия миграций · процедура установки и ротации ключа шифрования · воспроизводимые бюджеты производительности.

---

## 0. Исходные условия (зафиксированы владельцем)

1. Зарубежные коммерческие LMS и их платные дополнения официально недоступны целевой аудитории из РФ. Продукт даёт сопоставимый класс функций с легальной покупкой за рубли, обновлениями и поддержкой на русском.
2. Первая публичная версия содержит полный согласованный набор функций. Публичного урезанного выпуска нет. Разделение на бесплатное ядро и платные дополнения — после разработки и тестирования всего продукта.
3. Стоимость разработки не ограничивает функциональность. Качество, тестирование и совместимость — ограничивают.
4. Внешние проверки (стенды конкурентов, замеры, интервью) не выполняются. Решения принимаются из худшего предположения: у конкурентов паритет по любой обсуждаемой функции.

**Следствие условия 2:** формулировки вида «в первой версии» внутри согласованного функционала запрещены. Ограничение либо постоянное и обоснованное, либо его нет.

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
│            REST · License · Secrets · Diagnostics         │
│            Privacy                                        │
├──────────────────────────────────────────────────────────┤
│  WordPress: CPT · Users · Options · REST · $wpdb          │
│  Bundled:   Action Scheduler                              │
└──────────────────────────────────────────────────────────┘
```

Обращение только вниз. Компонент не знает адаптера. Платформа не знает доменных модулей.

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

| Правило | Проверка |
|---|---|
| `register()` не обращается к БД, не добавляет хуков, не читает опций | статический анализ + тест |
| `boot()` вызывается только при включённом модуле и удовлетворённых зависимостях | тест |
| Отключённый модуль не регистрирует хуков, REST-маршрутов, задач, ассетов и не обращается к своим таблицам | тест изоляции |
| Ассеты подключаются только на страницах, где компонент модуля реально выведен | тест бюджета |
| Модуль не вызывает классы другого модуля напрямую — только через `platform/Contracts` или события | статический анализ |

### 2.2. Загрузка

Единственное чтение конфигурации при старте — автозагружаемая опция `krs_modules`. Классы грузятся PSR-4; отключённый модуль не инстанцируется. Незавершённые части — за feature flags в `krs_flags` (инструмент разработки, в публичном выпуске все флаги включены).

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

### 2.4. Multisite

**Network Activation не поддерживается** — постоянное продуктовое ограничение, а не отложенная функция. При попытке сетевой активации — блокировка с явным сообщением. Плагин активируется отдельно на каждом сайте; таблицы и миграции создаются по сайту.

---

## 3. Модель данных

### 3.1. Принцип разделения

| Что | Где |
|---|---|
| Контент: курсы, разделы, уроки, тесты, вопросы, задания, шаблоны сертификатов, шаблоны уведомлений | CPT + таксономии |
| Связи и состояние: зачисления, основания, прогресс, попытки, ответы, работы, проверки, события, баллы, уведомления | Собственные таблицы |

**Запрещено:** хранить прогресс, попытки и зачисления в `wp_postmeta` или `wp_usermeta`.

### 3.2. Соглашения по всем таблицам

- `ENGINE=InnoDB` — требование; при невозможности установка блокируется с диагностикой.
- Коллация — **`$wpdb->get_charset_collate()`**.
- **Все даты в UTC.** API отдаёт ISO 8601 с зоной. Отображение — в поясе сайта или пользователя. Изменение пояса сайта не меняет сохранённые моменты. Календарные правила (серии дней) считаются в поясе, выбранном в настройках.
- **Ни один уникальный индекс не полагается на NULL-колонки.** MySQL допускает несколько `NULL` в уникальном индексе, поэтому все участвующие в уникальности колонки объявляются `NOT NULL` с сентинельным значением (`0` или пустая строка), либо уникальность обеспечивается отдельной `NOT NULL` колонкой ключа.
- `created_at`, `updated_at DATETIME` — во всех таблицах.

### 3.3. Типы записей

| CPT | Иерархия | REST |
|---|---|---|
| `krs_course` | — | публично, только опубликованные, ограниченный набор полей |
| `krs_section` | **дерево произвольной глубины** (ограничение интерфейса — настраиваемое, по умолчанию 3 уровня) | закрыто |
| `krs_lesson` | parent = section | **закрыто для гостей** |
| `krs_quiz` | parent = section \| course | закрыто |
| `krs_question` | — | закрыто |
| `krs_assignment` | parent = section \| lesson | закрыто |
| `krs_cert_template` | — | закрыто |
| `krs_bundle` | — | публично, ограниченный набор полей |
| `krs_notify_template` | — | закрыто |

Таксономии: `krs_course_cat`, `krs_course_tag`, `krs_qbank`, `krs_level`.

Разделы — полноценное дерево, а не один уровень: ограничение «один уровень» было урезанием первой версии и снято.

### 3.4. Версионирование учебного контента

```
krs_content_version
  subject_type VARCHAR(20) NOT NULL     -- course|curriculum|quiz|question|assignment|rubric
  subject_id BIGINT UNSIGNED NOT NULL
  version INT UNSIGNED NOT NULL
  snapshot_json JSON, hash CHAR(40)
  published_at DATETIME NULL, created_by BIGINT UNSIGNED
  UNIQUE KEY uq (subject_type, subject_id, version)
  KEY idx_subject (subject_type, subject_id, published_at)
```

1. Публикация изменений оцениваемой сущности создаёт новую версию; черновые правки — нет.
2. Попытка теста и попытка работы фиксируют номер версии и читают снимок при показе истории.
3. Удаление опубликованного вопроса или задания переводит его в `archived` и **никогда не удаляет физически**, пока есть ссылающиеся попытки.

### 3.5. Зачисление и основание доступа

```
krs_enrollment                          -- цикл обучения
  user_id, course_id BIGINT UNSIGNED NOT NULL
  cycle_no SMALLINT UNSIGNED NOT NULL
  group_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  curriculum_version INT UNSIGNED NOT NULL      -- версия программы, по которой учится цикл
  status VARCHAR(20) NOT NULL      -- pending|active|paused|expired|completed
  started_at, completed_at DATETIME NULL
  UNIQUE KEY uq (user_id, course_id, cycle_no)
  KEY idx_open (user_id, course_id, status)
  KEY idx_course_status (course_id, status)
  KEY idx_group (group_id, status)
```

```
krs_access_grant                        -- основание доступа
  enrollment_id BIGINT UNSIGNED NOT NULL
  source VARCHAR(20) NOT NULL      -- manual|woo|invite|import|api|bulk
  source_ref VARCHAR(64) NOT NULL DEFAULT ''
  order_id, order_item_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  status VARCHAR(20) NOT NULL      -- pending|active|expired|revoked
  starts_at, expires_at, revoked_at, restored_at DATETIME NULL
  revoke_reason VARCHAR(128) NOT NULL DEFAULT ''
  frozen_total_s INT UNSIGNED NOT NULL DEFAULT 0   -- суммарная заморозка, учтённая в expires_at
  UNIQUE KEY uq (source, source_ref, enrollment_id)
  KEY idx_enr_status (enrollment_id, status)
  KEY idx_expiry (status, expires_at)
  KEY idx_order (order_id, order_item_id)
```

**Правило эффективного доступа:** право даёт наличие хотя бы одного активного основания. `krs_enrollment.status` — производное состояние цикла, не источник права.

#### Конкуренция циклов

- По умолчанию для пары «пользователь + курс» допускается **один открытый цикл** со статусом `pending`, `active` или `paused`.
- Повторная покупка при открытом цикле продлевает действующее основание либо создаёт новое основание **внутри текущего цикла** — по политике связи товара.
- Новый цикл создаётся: после завершения или истечения предыдущего · при зачислении в новый поток · при политике связи «новый цикл» · вручную администратором.
- Параллельные открытые циклы возможны **только осознанным административным действием** с предупреждением в интерфейсе и записью в аудит.
- `EnrollmentService::openCycle()` выполняет проверку и вставку **под блокировкой строки** (`SELECT ... FOR UPDATE` по паре пользователь+курс либо именованная блокировка), чтобы два одновременных заказа не создали два цикла.

#### Восстановление основания

Машина состояний: `pending → active → expired | revoked`, плюс **`revoked → active`** — исключительно через `AccessGrantService::restore()`.

Выбран вариант с восстановлением той же строки, а не созданием новой: он сохраняет привязку к исходной строке заказа и не требует ослаблять уникальный индекс. Восстановление проставляет `restored_at`, обнуляет `revoked_at` и `revoke_reason`, пишет запись в `krs_state_transition_log` и в аудит. Повторная покупка того же курса создаёт **новую строку заказа**, то есть новый `source_ref`, — конфликта с уникальным индексом не возникает.

### 3.6. Программа курса

```
krs_curriculum
  course_id, item_id BIGINT UNSIGNED NOT NULL
  item_type VARCHAR(20) NOT NULL
  parent_id BIGINT UNSIGNED NOT NULL DEFAULT 0     -- 0 = корень, НЕ NULL
  position INT UNSIGNED NOT NULL
  depth TINYINT UNSIGNED NOT NULL
  is_gradable, is_required TINYINT(1) NOT NULL
  drip_rule_json, prereq_json JSON NULL
  curriculum_version INT UNSIGNED NOT NULL
  PRIMARY KEY (course_id, item_id)
  UNIQUE KEY uq_pos (course_id, parent_id, position)
  KEY idx_order (course_id, position)
  KEY idx_item (item_id)
```

`parent_id NOT NULL DEFAULT 0` — обязательно: с `NULL` уникальность позиции корневых элементов не обеспечивалась бы вовсе.

**`krs_curriculum` — каноническая опубликованная структура.** `post_parent` в CPT — редакторское удобство, не источник истины.

`CurriculumService` — единственный, кому разрешена запись: транзакционное обновление · проверка отсутствия циклов · гарантия уникальности позиции · создание новой `curriculum_version` при публикации · команда сверки `post_parent` ↔ `krs_curriculum` с отчётом · инструмент восстановления структуры из CPT · запрет прямой записи мимо сервиса (статический анализ).

#### Смена версии программы для действующих циклов

Каждый цикл хранит `curriculum_version`. Новая версия **по умолчанию применяется только к новым циклам**.

Для действующих: администратор получает предварительный отчёт — какие элементы добавлены, удалены, стали обязательными, как изменится процент прогресса и готовность сертификатов — и выбирает: оставить прежнюю программу · мигрировать выбранные потоки · мигрировать всех. Завершённый цикл автоматически не переводится. Уже выданный сертификат пересчётом не меняется. Миграция фиксируется в аудите и может быть отменена до появления первых попыток по новой версии.

### 3.7. Прогресс

```
krs_progress
  enrollment_id, item_id BIGINT UNSIGNED NOT NULL
  item_type VARCHAR(20) NOT NULL
  status VARCHAR(20) NOT NULL      -- not_started|in_progress|completed
  pct TINYINT UNSIGNED NOT NULL DEFAULT 0
  position_s INT UNSIGNED NOT NULL DEFAULT 0
  active_time_s INT UNSIGNED NOT NULL DEFAULT 0
  first_seen_at, completed_at DATETIME NULL
  UNIQUE KEY uq (enrollment_id, item_id)
  KEY idx_enr_status (enrollment_id, status)
  KEY idx_recent (enrollment_id, updated_at)
```

```
krs_course_progress
  enrollment_id BIGINT UNSIGNED NOT NULL PRIMARY KEY
  user_id, course_id BIGINT UNSIGNED NOT NULL
  items_total, items_done, items_required, items_required_done SMALLINT UNSIGNED NOT NULL
  pct TINYINT UNSIGNED NOT NULL
  last_item_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  last_activity_at, completed_at DATETIME NULL
  calculated_at DATETIME NOT NULL
  source_version INT UNSIGNED NOT NULL
  is_dirty TINYINT(1) NOT NULL DEFAULT 0
  KEY idx_user_recent (user_id, last_activity_at)
  KEY idx_course_pct (course_id, pct)
```

**Обновление синхронное:** переход состояния элемента в той же транзакции инкрементально обновляет агрегат. Пользователь никогда не видит устаревший процент. Фоновая задача выполняет сверку и восстановление для строк с `is_dirty = 1` или устаревшим `source_version`, плюс есть команда полного пересчёта для администратора и CLI.

### 3.8. Тесты

```
krs_quiz_attempt
  enrollment_id, quiz_id BIGINT UNSIGNED NOT NULL
  attempt_no SMALLINT UNSIGNED NOT NULL
  quiz_version INT UNSIGNED NOT NULL
  status VARCHAR(20) NOT NULL      -- in_progress|submitted|grading|graded|expired
  score_raw, score_max, score_pct DECIMAL(8,2) NULL
  passed TINYINT(1) NULL
  time_spent_s INT UNSIGNED NULL
  started_at, submitted_at, graded_at, expires_at DATETIME NULL
  UNIQUE KEY uq (enrollment_id, quiz_id, attempt_no)
  KEY idx_quiz_status (quiz_id, status)
  KEY idx_pending (status, submitted_at)
```

```
krs_quiz_result                         -- агрегат по тесту в рамках цикла
  enrollment_id, quiz_id BIGINT UNSIGNED NOT NULL
  credited_attempt_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  credited_score DECIMAL(8,2) NULL      -- по выбранной политике first|last|best|average
  ever_passed TINYINT(1) NOT NULL DEFAULT 0
  prerequisite_passed TINYINT(1) NOT NULL DEFAULT 0
  certificate_score DECIMAL(8,2) NULL
  attempts_used SMALLINT UNSIGNED NOT NULL DEFAULT 0
  PRIMARY KEY (enrollment_id, quiz_id)
```

Разделение четырёх величин устраняет противоречие «политика last» и «худшая попытка не отменяет прохождение»: `credited_score` считается по политике школы, `ever_passed` фиксируется навсегда при первом успехе, `prerequisite_passed` по умолчанию равен `ever_passed` («липкое» прохождение). Строгий режим, где доступ закрывается при неудачной последней попытке, — отдельная настройка с явным предупреждением при включении.

```
krs_quiz_answer
  attempt_id, question_id BIGINT UNSIGNED NOT NULL
  question_version INT UNSIGNED NOT NULL
  question_snapshot_json JSON, question_type VARCHAR(20) NOT NULL
  answer_json JSON
  is_correct TINYINT(1) NULL           -- NULL = ждёт ручной проверки
  points_awarded, points_max DECIMAL(8,2)
  UNIQUE KEY uq (attempt_id, question_id)
  KEY idx_manual_queue (is_correct, graded_at)
```

**Неизменяемость попытки означает неизменяемость ответов ученика.** Результаты ручной проверки хранятся отдельно и версионируются:

```
krs_answer_review
  quiz_answer_id BIGINT UNSIGNED NOT NULL
  revision SMALLINT UNSIGNED NOT NULL
  grader_id BIGINT UNSIGNED NOT NULL
  points_awarded DECIMAL(8,2), remarks LONGTEXT
  ai_draft_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  UNIQUE KEY uq (quiz_answer_id, revision)
```

Исправление оценки создаёт новую ревизию, а не переписывает предыдущую.

### 3.9. Домашние работы

```
krs_submission                          -- корень
  enrollment_id, assignment_id BIGINT UNSIGNED NOT NULL
  course_id BIGINT UNSIGNED NOT NULL
  status VARCHAR(20) NOT NULL      -- draft|submitted|in_review|returned|accepted|rejected
  current_attempt_no SMALLINT UNSIGNED NOT NULL DEFAULT 0
  assignee_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  claimed_at, claim_expires_at DATETIME NULL
  deadline_at, first_submitted_at, closed_at DATETIME NULL
  UNIQUE KEY uq (enrollment_id, assignment_id)
  KEY idx_queue (status, first_submitted_at)
  KEY idx_assignee (assignee_id, status)
  KEY idx_claim (claim_expires_at)
```

```
krs_submission_attempt                  -- неизменяема
  submission_id BIGINT UNSIGNED NOT NULL
  attempt_no SMALLINT UNSIGNED NOT NULL
  assignment_version INT UNSIGNED NOT NULL
  body LONGTEXT NULL, payload_json JSON NULL
  submitted_at DATETIME NOT NULL
  UNIQUE KEY uq (submission_id, attempt_no)
```

```
krs_submission_draft                    -- черновик, не виден проверяющему
  submission_id BIGINT UNSIGNED NOT NULL PRIMARY KEY
  body LONGTEXT NULL, payload_json JSON NULL
  draft_version INT UNSIGNED NOT NULL    -- для разрешения конфликта двух вкладок
  editor_session CHAR(32) NOT NULL DEFAULT ''
  updated_at DATETIME NOT NULL
```

```
krs_temp_attachment                     -- загружено, но не отправлено
  owner_id BIGINT UNSIGNED NOT NULL
  submission_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  file_path VARCHAR(255) NOT NULL, file_hash CHAR(64) NOT NULL
  size_bytes BIGINT UNSIGNED NOT NULL, mime VARCHAR(64) NOT NULL
  expires_at DATETIME NOT NULL
  KEY idx_cleanup (expires_at)
  KEY idx_owner (owner_id)
```

Брошенные временные файлы удаляются фоновой задачей по `expires_at`; удаление фиксируется в аудите.

**Захват работы (`claim`) атомарен:**
`UPDATE krs_submission SET assignee_id = :me, claimed_at = NOW(), claim_expires_at = ... WHERE id = :id AND (assignee_id = 0 OR claim_expires_at < NOW())` — работу получает только один проверяющий, брошенный захват освобождается по истечении аренды либо административно.

```
krs_review
  submission_attempt_id, reviewer_id BIGINT UNSIGNED NOT NULL
  revision SMALLINT UNSIGNED NOT NULL DEFAULT 1
  decision VARCHAR(20) NOT NULL      -- accepted|returned|rejected
  score DECIMAL(8,2) NULL, feedback LONGTEXT NULL
  rubric_version INT UNSIGNED NOT NULL DEFAULT 0, rubric_json JSON NULL
  ai_draft_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  ai_edit_ratio DECIMAL(5,4) NULL
  active_review_time_s INT UNSIGNED NULL
  UNIQUE KEY uq (submission_attempt_id, revision)
  KEY idx_reviewer (reviewer_id, created_at)
```

### 3.10. Рубрики

```
krs_rubric               subject_type, subject_id, version, title, is_active
                         UNIQUE (subject_type, subject_id, version)
krs_rubric_criterion     rubric_id, position, title, weight, levels_json, common_errors_json
                         KEY (rubric_id, position)
```

### 3.11. События, доставка, дедупликация

```
krs_event
  name VARCHAR(64) NOT NULL
  user_id, course_id, enrollment_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  subject_type VARCHAR(20) NOT NULL DEFAULT '', subject_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  payload_json JSON NULL
  occurred_at DATETIME(3) NOT NULL
  dispatched_at DATETIME NULL
  KEY idx_name_time (name, occurred_at)
  KEY idx_outbox (dispatched_at, occurred_at)
```

```
krs_consumer_dedupe
  consumer VARCHAR(32) NOT NULL
  dedupe_key CHAR(64) NOT NULL
  event_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  state VARCHAR(16) NOT NULL          -- processing|succeeded|failed|unknown
  lease_until DATETIME NULL
  attempts SMALLINT UNSIGNED NOT NULL DEFAULT 0
  external_ref VARCHAR(128) NOT NULL DEFAULT ''
  last_error VARCHAR(255) NOT NULL DEFAULT ''
  UNIQUE KEY uq (consumer, dedupe_key)
  KEY idx_lease (state, lease_until)
```

#### Семантика доставки — переработана

Формулировки «дедупликация до постановки задачи» и «до входа в транзакцию» из версии 0.2 **удалены**: они создавали окно, в котором падение процесса между записью ключа и выполнением эффекта блокировало операцию навсегда.

Действующие правила:

1. Доставка события и постановка задачи — **как минимум один раз**. Повторная постановка допустима и нормальна.
2. **Для эффекта внутри базы данных** запись дедупликации, изменение доменного состояния и новое событие выполняются **в одной транзакции**. Конфликт по уникальному ключу означает, что эффект уже успешно зафиксирован ранее — обработчик завершается успешно, ничего не делая.
3. **Для внешних вызовов** (ИИ, Telegram, почта) используется явная машина: `processing → succeeded | failed | unknown`.
   - `processing` ставится с арендой (`lease_until`) до вызова;
   - `succeeded` — **только после подтверждённого результата**;
   - `unknown` при обрыве связи или таймауте: операция **не считается выполненной автоматически**, попадает на повторную попытку с проверкой по `external_ref`, а при невозможности установить исход — в очередь ручного разбора;
   - брошенный `processing` восстанавливается по истечении аренды;
   - если внешний провайдер поддерживает ключ идемпотентности, передаётся наш стабильный ключ, и повтор не создаёт второго эффекта у провайдера.

| Потребитель | Ключ дедупликации |
|---|---|
| Баллы | `event_id + rule_id + beneficiary_id` |
| Уведомление | `event_id + notification_rule_id + recipient_id + channel` |
| ИИ | `submission_attempt_id + rubric_version + provider + model + prompt_version + generation_no` |
| Отчёты | `event_id + report_slug` |

**Outbox:** запись события и изменение доменного состояния — в одной транзакции; после фиксации диспетчер публикует событие.

### 3.12. Баллы

```
krs_points_ledger
  user_id BIGINT UNSIGNED NOT NULL
  delta INT NOT NULL
  reason VARCHAR(64) NOT NULL
  idempotency_key CHAR(64) NOT NULL          -- единственная защита от дубля
  event_id, rule_id BIGINT UNSIGNED NOT NULL DEFAULT 0   -- только для аудита
  balance_after INT NOT NULL
  UNIQUE KEY uq_idem (idempotency_key)
  KEY idx_user_time (user_id, created_at)
```

Прежний составной уникальный индекс с NULL-колонками защиты не давал. Теперь уникальность держится на одной `NOT NULL` колонке.

**Порядок в одной транзакции:** атомарное обновление `krs_user_stats` → вставка строки реестра (конфликт ключа = операция уже выполнена) → фиксация `balance_after` → запись события в outbox.

Отмена действия списывает опыт **компенсирующей записью**, а не удалением исходной: реестр остаётся только-добавляемым.

### 3.13. Персонал курса и групп

```
krs_course_staff
  course_id, user_id BIGINT UNSIGNED NOT NULL
  role VARCHAR(20) NOT NULL         -- instructor|curator|observer
  is_active TINYINT(1) NOT NULL DEFAULT 1
  UNIQUE KEY uq (course_id, user_id, role)
  KEY idx_user (user_id, is_active)

krs_group          course_id, title, starts_at, ends_at, capacity, status
krs_group_member   group_id, user_id, joined_at   UNIQUE (group_id, user_id)
krs_group_staff    group_id, user_id, role, priority, is_active
                   UNIQUE (group_id, user_id, role)

krs_student_reviewer                    -- постоянный проверяющий конкретного ученика
  user_id, reviewer_id BIGINT UNSIGNED NOT NULL
  course_id BIGINT UNSIGNED NOT NULL DEFAULT 0   -- 0 = для всех курсов
  is_active TINYINT(1) NOT NULL DEFAULT 1
  UNIQUE KEY uq (user_id, course_id)
  KEY idx_reviewer (reviewer_id, is_active)
```

`krs_submission.assignee_id` покрывает назначение конкретной работы; `krs_student_reviewer` — постоянное закрепление ученика за проверяющим. Это разные вещи, и вторая в версии 0.2 отсутствовала.

### 3.14. Комплекты

```
krs_bundle_item
  bundle_id, course_id BIGINT UNSIGNED NOT NULL
  position SMALLINT UNSIGNED NOT NULL
  duration_days SMALLINT UNSIGNED NOT NULL DEFAULT 0
  UNIQUE KEY uq (bundle_id, course_id)
  KEY idx_course (course_id)
```

### 3.15. Приглашения

```
krs_invite
  code_prefix CHAR(8) NOT NULL, code_hash CHAR(64) NOT NULL
  target_type VARCHAR(20) NOT NULL, target_id BIGINT UNSIGNED NOT NULL
  uses_max, uses_count SMALLINT UNSIGNED NOT NULL
  duration_days SMALLINT UNSIGNED NOT NULL DEFAULT 0
  expires_at, revoked_at DATETIME NULL
  created_by BIGINT UNSIGNED NOT NULL
  UNIQUE KEY uq_prefix (code_prefix)

krs_invite_redemption
  invite_id, user_id BIGINT UNSIGNED NOT NULL
  access_grant_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  ip_hash CHAR(64) NOT NULL DEFAULT '', redeemed_at DATETIME NOT NULL
  UNIQUE KEY uq (invite_id, user_id)
```

Рабочий код открытым текстом не хранится. Счётчик обновляется атомарно с проверкой числа затронутых строк.

### 3.16. Коммерция

```
krs_woo_link
  product_id, variation_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  target_type VARCHAR(20) NOT NULL, target_id BIGINT UNSIGNED NOT NULL
  grant_statuses, revoke_statuses JSON NOT NULL
  duration_days SMALLINT UNSIGNED NOT NULL DEFAULT 0
  group_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  renewal_policy VARCHAR(20) NOT NULL   -- extend|new_grant|new_cycle
  UNIQUE KEY uq (product_id, variation_id, target_type, target_id)

krs_woo_grant                            -- аудит операций
  order_id, order_item_id BIGINT UNSIGNED NOT NULL
  user_id, course_id BIGINT UNSIGNED NOT NULL
  access_grant_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  action VARCHAR(20) NOT NULL      -- grant|revoke|restore|failed|manual_review
  reason VARCHAR(128) NOT NULL DEFAULT '', order_status VARCHAR(32) NOT NULL
  KEY idx_order (order_id), KEY idx_user_course (user_id, course_id)
  KEY idx_failed (action, created_at)
```

**Доступ к заказам — только через WooCommerce CRUD API** (`wc_get_order`, `wc_get_orders`, `WC_Order`, `WC_Order_Item`). Прямое чтение и запись `wp_posts` / `wp_postmeta` / таблиц заказов запрещено и ловится статическим анализом. Объявляется совместимость с HPOS через `FeaturesUtil::declare_compatibility('custom_order_tables', …)`. Тесты выполняются на HPOS и на устаревшем хранилище.

### 3.17. ИИ

```
krs_ai_request    module, operation, subject_type, subject_id, provider, model,
                  prompt_version, status, error_code, tokens_in, tokens_out,
                  cost_minor, latency_ms
krs_ai_draft      subject_type, subject_id, rubric_version, generation_no,
                  draft_text, structure_json, confidence, needs_attention, ai_request_id
```

Тексты промптов и ответов модели не сохраняются. Черновик привязан к попытке работы и удаляется вместе с ней.

### 3.18. Сертификаты

```
krs_completion_record                    -- неизменяемый снимок основания выдачи
  enrollment_id BIGINT UNSIGNED NOT NULL
  user_display_name VARCHAR(255) NOT NULL     -- имя на момент выдачи
  course_title VARCHAR(255) NOT NULL
  course_version, curriculum_version INT UNSIGNED NOT NULL
  final_score DECIMAL(8,2) NULL
  requirements_json JSON NOT NULL              -- какие обязательные требования выполнены
  completed_at DATETIME NOT NULL
  approved_by BIGINT UNSIGNED NOT NULL DEFAULT 0
  UNIQUE KEY uq (enrollment_id)

krs_certificate
  enrollment_id, completion_record_id BIGINT UNSIGNED NOT NULL
  user_id, course_id, template_id BIGINT UNSIGNED NOT NULL
  serial VARCHAR(32) NOT NULL, public_token CHAR(32) NOT NULL
  status VARCHAR(20) NOT NULL      -- pending|issued|expired|revoked|replaced
  replaces_id, replaced_by_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  file_path VARCHAR(255) NOT NULL DEFAULT '', file_hash CHAR(64) NOT NULL DEFAULT ''
  issued_by BIGINT UNSIGNED NOT NULL DEFAULT 0
  issued_at, expires_at, revoked_at DATETIME NULL
  revoke_reason VARCHAR(128) NOT NULL DEFAULT ''
  UNIQUE KEY uq_serial (serial)
  UNIQUE KEY uq_token (public_token)
  KEY idx_status (status, expires_at)
```

Состояния: `pending → issued → expired | revoked | replaced`.

Публичная страница проверки открывается по **непредсказуемому токену**, отдаёт `noindex`, показывает имя в настраиваемом виде (полное, инициалы, скрытое) и не раскрывает иных персональных данных.

### 3.19. Уведомления

```
krs_notify_rule        event_name, channel, audience, template_id, delay_s,
                       conditions_json, category, is_active
                       -- category: mandatory_inbox | operational | optional
krs_notify_pref        user_id, category, channel, is_enabled
                       UNIQUE (user_id, category, channel)
krs_telegram_link      user_id, chat_id, link_token CHAR(32), linked_at, revoked_at
                       UNIQUE (user_id), UNIQUE (chat_id)
krs_inbox_message      user_id, subject, body, is_read, related_type, related_id
                       KEY (user_id, is_read, created_at)
krs_announcement       course_id, group_id, audience, title, body, publish_at, expires_at
                       KEY (course_id, publish_at)
krs_notification_log   rule_id, user_id, channel, status, dedupe_key CHAR(64) NOT NULL,
                       fallback_of_id, error, sent_at
                       UNIQUE (dedupe_key)
```

### 3.20. Геймификация

```
krs_achievement        slug VARCHAR(64) NOT NULL UNIQUE, title, description, icon_id,
                       rule_json, points INT, is_active
krs_user_achievement   user_id, achievement_id BIGINT UNSIGNED NOT NULL
                       idempotency_key CHAR(64) NOT NULL UNIQUE
                       unlocked_at
                       UNIQUE KEY uq (user_id, achievement_id)
krs_level_rule         level SMALLINT NOT NULL UNIQUE, xp_required INT NOT NULL,
                       title, reward_json
krs_goal               scope VARCHAR(20) NOT NULL,   -- personal|group|cohort
                       course_id, group_id BIGINT UNSIGNED NOT NULL DEFAULT 0,
                       title, metric VARCHAR(32), target_value INT,
                       period_start, period_end, reward_json, is_active
krs_goal_participant   goal_id, user_id BIGINT UNSIGNED NOT NULL,
                       current_value INT NOT NULL DEFAULT 0,
                       achieved_at DATETIME NULL, rewarded_at DATETIME NULL
                       UNIQUE KEY uq (goal_id, user_id)
krs_user_stats         user_id PK, xp_total, level, streak_days, streak_last_date,
                       streak_freeze_left, achievements_count
```

### 3.21. Журнал переходов и аудит

```
krs_state_transition_log            -- единый журнал для всех машин состояний
  subject_type VARCHAR(20) NOT NULL   -- enrollment|grant|quiz_attempt|submission|certificate
  subject_id BIGINT UNSIGNED NOT NULL
  from_status, to_status VARCHAR(20) NOT NULL
  actor_id BIGINT UNSIGNED NOT NULL DEFAULT 0
  reason VARCHAR(64) NOT NULL DEFAULT '', meta_json JSON NULL
  occurred_at DATETIME(3) NOT NULL
  KEY idx_subject (subject_type, subject_id, occurred_at)
  KEY idx_actor (actor_id, occurred_at)

krs_audit_log                       -- действия людей и системы, значимые вне машин состояний
  actor_id, action, subject_type, subject_id, ip_hash, ua_hash, meta_json
  KEY (action, created_at), KEY (subject_type, subject_id), KEY (actor_id, created_at)

krs_freeze_period                   -- заморозка обучения
  enrollment_id BIGINT UNSIGNED NOT NULL
  starts_at DATETIME NOT NULL, ends_at DATETIME NULL
  applied_seconds INT UNSIGNED NOT NULL DEFAULT 0
  shift_fixed_dates TINYINT(1) NOT NULL DEFAULT 0
  actor_id BIGINT UNSIGNED NOT NULL
  KEY idx_enr (enrollment_id, starts_at)
```

Пересекающиеся периоды заморозки объединяются перед применением, чтобы срок не продлевался дважды.

### 3.22. Прочее

```
krs_migration     module, version, checksum CHAR(40), applied_at  UNIQUE (module, version)
krs_secret        name VARCHAR(64) NOT NULL UNIQUE, ciphertext BLOB, key_id VARCHAR(32),
                  rotated_at
```

### 3.23. Ретенция — предварительные значения по умолчанию

| Таблица | По умолчанию | Правило |
|---|---|---|
| `krs_event` | 180 дней | недоставленные не удаляются |
| `krs_consumer_dedupe` | не раньше окна повторной доставки | записи в состоянии `processing` и `unknown` не удаляются |
| `krs_ai_request` | 365 дней | |
| `krs_state_transition_log` | 365 дней | |
| `krs_audit_log` | 365 дней, ниже не опускается | |
| `krs_notification_log` | 90 дней | |
| `krs_temp_attachment` | 7 дней | удаление файла фиксируется в аудите |
| Незавершённые работы, попытки, задачи | не удаляются ретенцией никогда | |

Значения настраиваемые, **предварительные, не являются юридическими требованиями** и подлежат проверке юристом.

---

## 4. Права и политики

### 4.1. Роли

`krs_student` · `krs_curator` · `krs_instructor` · `krs_author` · `administrator`

### 4.2. Возможности

```
Контент:     krs_edit_courses · krs_edit_others_courses · krs_publish_courses
             krs_delete_courses · krs_manage_curriculum
Зачисления:  krs_manage_enrollments · krs_view_enrollments · krs_bulk_enroll
             krs_manage_invites · krs_manage_groups · krs_freeze_enrollment
Проверка:    krs_view_submissions · krs_grade_submissions · krs_reassign_submissions
Тесты:       krs_manage_questions · krs_manage_qbank · krs_grade_quizzes
Отчёты:      krs_view_reports · krs_view_all_reports · krs_export_reports
ИИ:          krs_use_ai · krs_manage_ai_settings
Коммерция:   krs_manage_woo_links · krs_view_grant_log · krs_manual_grant
Сертификаты: krs_issue_certificates · krs_revoke_certificates
Система:     krs_manage_settings · krs_manage_modules · krs_manage_license
             krs_manage_secrets · krs_view_audit
             krs_export_user_data · krs_erase_user_data
```

### 4.3. Два механизма политики

```php
interface PolicyInterface {
    public function can(int $userId, string $action, ?object $subject = null): bool;
    public function scope(int $userId, string $resourceType, QuerySpec $q): QuerySpec;
}
```

`can()` проверяет конкретный объект. **`scope()` ограничивает коллекцию** — каталог, очередь работ, поиск, отчёт: у них нет единственного объекта, и без этого механизма проверка прав в списках неизбежно окажется в запросах и разъедется. `scope()` дописывает ограничения по курсам, группам, авторству и статусу публикации.

**После получения объекта из коллекции перед любым изменением снова выполняется `can()`.** `scope()` не заменяет проверку.

### 4.4. Единая формула доступа к контенту

```
can_view_lesson =
      enrollment.status IN (active, completed)
  AND EXISTS(access_grant WHERE status = active)
  AND drip_allowed
  AND prerequisites_completed
```

`pending`, `paused` и `expired` контент **не открывают**. `completed` открывает, пока действует хотя бы одно основание. Формула — единственный источник истины; интерфейс, REST и выдача файлов используют её, а не собственные проверки.

Прочие правила политики: проверка работы — назначенный проверяющий, либо постоянный проверяющий ученика, либо активный персонал группы, либо персонал курса · выдача файла — право на элемент, к которому он относится · отчёт — своя группа или курс, если нет `krs_view_all_reports` · использование ИИ — модуль включён, согласие получено, лимит не исчерпан.

**`current_user_can()` без объекта запрещён для любых операций с чужими данными.**

---

## 5. Состояния

```
Enrollment         pending → active ⇄ paused → expired | completed
AccessGrant        pending → active → expired | revoked → active (только restore())
QuizAttempt        in_progress → submitted → grading → graded | expired
Submission         draft → submitted → in_review → returned → submitted | accepted | rejected
SubmissionAttempt  неизменяема
Certificate        pending → issued → expired | revoked | replaced
```

Переходы — только через сервисы, прямая запись `status` запрещена и ловится тестом. Каждый переход публикует событие в outbox в той же транзакции и пишет строку в `krs_state_transition_log`.

---

## 6. Шина событий

Именование: `kursio/{module}.{entity}.{action}`, прошедшее время.

| Модуль | События |
|---|---|
| `access` | `enrollment.created` `.activated` `.paused` `.resumed` `.expired` `.completed` `.cycle_opened` · `grant.created` `.activated` `.expired` `.revoked` `.restored` · `invite.redeemed` · `group.member_added` `.member_removed` |
| `core` | `lesson.started` `.completed` · `course.started` `.progress_changed` `.completed` · `curriculum.published` `.migrated` |
| `quiz` | `attempt.started` `.submitted` `.graded` · `quiz.passed` `.failed` · `answer.manually_graded` |
| `assignment` | `submission.created` · `attempt.submitted` · `submission.claimed` `.released` `.returned` `.accepted` `.rejected` `.reassigned` · `deadline.approaching` `.missed` `.shifted` |
| `ai` | `draft.requested` `.ready` `.failed` · `quota.exceeded` |
| `woo` | `order.linked` · `grant.issued` `.revoked` `.restored` `.failed` · `order.manual_review` |
| `gamification` | `points.awarded` `.reverted` · `level.reached` · `streak.extended` `.broken` · `achievement.unlocked` · `goal.achieved` |
| `certificate` | `certificate.issued` `.revoked` `.expired` `.replaced` |

Синхронно — только влияющее на текущий ответ. Остальное через очередь. Ошибка потребителя не откатывает событие и не ломает соседей.

---

## 7. Контекст

### 7.1. Два уровня

```php
final class RequestContext {           // один раз за запрос
    public ?WP_User $user;
    public RouteSignature $route;
    public ?int $primaryCourseId;
}

final class ComponentContext {         // для конкретного вызова компонента
    public ?WP_User $user;
    public ?Course $course; public ?Section $section; public ?Lesson $lesson;
    public ?Quiz $quiz; public ?Assignment $assignment;
    public ?Enrollment $enrollment; public ?AccessGrant $effectiveGrant;
    public ?CourseProgress $progress; public ?NextStep $nextStep;
    public string $source;      // explicit|route|loop|request|activity|single|none
    public string $confidence;  // exact|inferred|none
}
```

Кэш `ComponentContext` — по ключу `component_id + course_id + lesson_id + user_id + route_signature`.

### 7.2. Источники

| № | Источник | `source` | confidence |
|---|---|---|---|
| 1 | Явные свойства компонента | `explicit` | exact |
| 2 | Маршрут и query vars | `route` | exact |
| 3 | Текущая запись цикла (`the_post()`) | `loop` | exact |
| 4 | Параметр запроса с проверкой прав | `request` | exact |
| 5 | Последняя активность пользователя | `activity` | inferred |
| 6 | Единственный курс на сайте | `single` | inferred |
| 7 | Ничего | `none` | none |

### 7.3. Политика контекста компонента

| Политика | Источники | Примеры |
|---|---|---|
| `explicit_required` | 1 | встраивание конкретного курса в лендинг |
| `route_or_explicit` | 1, 2 | `lesson_content`, `quiz` |
| `loop_or_route_or_explicit` | 1, 2, 3 | `course_price`, `course_curriculum`, `course_card` |
| `personal_activity_allowed` | 1–5 | `continue_learning`, `my_deadlines` |
| `global_allowed` | 1–6 | `catalog`, `points_badge` |

### 7.4. Правила

`RequestContext` — один раз за запрос, `ComponentContext` — по ключу с кэшем · ленивая загрузка по группам полей, разрешение контекста без обращения к полям = ноль запросов · деградация по `confidence` обязательна, при `none` — запасной вариант или пусто, никогда ошибка и никогда чужие данные · явное перекрывает автоматику, обратное запрещено · для анонимного пользователя контекст разрешается без обращения к пользовательским таблицам.

---

## 8. Компоненты и рендеринг

### 8.1. Схема — единственный источник истины

```php
final class ComponentSchema {
    public string $id, $title, $category;
    public array  $requires;
    public string $contextPolicy;
    public array  $props;          // PropSchema[]
    public array  $states;
    public bool   $dynamic;
    public array  $assets;
}
```

### 8.2. Генерация адаптеров — на этапе сборки

Из схемы **во время сборки** генерируются `block.json` и метаданные PHP, описание элементов управления Elementor, атрибуты и валидация шорткода, сигнатура PHP API, документация. **Во время обычного запроса WordPress файлы не создаются.**

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

`resolve()` не содержит HTML и не знает адаптера · адаптер не содержит бизнес-логики · шаблоны переопределяются темой по пути `theme/kursio/{component}.php`, ViewModel не меняется.

### 8.4. Типизированный безопасный вывод

```
PlainText       → esc_html при выводе
AttributeValue  → esc_attr
UrlValue        → esc_url с проверкой схемы
SafeHtml        → единственный тип без экранирования; создаётся только фабрикой
                  после wp_kses с явной схемой тегов
```

Всё, что не помечено `SafeHtml`, считается недоверенным. Вывод значения без типа — ошибка статического анализа.

### 8.5. Тест паритета — пять уровней

1. Нормализация свойств каждым адаптером. 2. Результирующий ViewModel. 3. Итоговый HTML общего рендерера. 4. Регистрация элементов управления и атрибутов. 5. Минимальный сквозной тест в реальном Elementor и редакторе блоков.

Расхождение на любом уровне — падение сборки.

---

## 9. Очередь

**Action Scheduler поставляется в составе платформенного модуля.** Собственный табличный драйвер отсутствует: две реализации с разной семантикой — источник расхождений.

```php
interface QueueInterface {
    public function push(string $handler, array $payload, ?string $uniqueKey = null, int $delay = 0): void;
    public function isScheduled(string $handler, array $payload): bool;
}
```

Повторная постановка задачи допустима и не является ошибкой — защита от повторного эффекта обеспечивается семантикой §3.11, а не уникальностью задачи в очереди.

**Запуск:** системный cron рекомендуется, инструкция в мастере. Диагностика показывает фактический режим, время последнего прогона, доступность loopback-запросов и предупреждает при простое. Дополнительно — защищённый REST-эндпоинт для внешнего триггера.

Правила: ни одна тяжёлая операция не выполняется в веб-запросе; задача, не завершившаяся за 30 секунд, продолжается с сохранённой позиции; после исчерпания попыток — уведомление администратору.

---

## 10. Кэширование

### 10.1. Уровни

Денормализация в БД (`krs_course_progress`, `krs_quiz_result`, `krs_user_stats`, `krs_curriculum`) — часть модели · объектный кэш, группа `kursio` · фрагментный кэш только для не-пользовательских ViewModel.

### 10.2. Инвалидация

Ключ включает метку версии сущности: `krs:course:{id}:v{n}:curriculum`. Запись увеличивает `n`. Ручной сброс не требуется.

### 10.3. Динамические компоненты и страничный кэш

Отдавать всем посетителям страницу из общего кэша со встроенным `wp_rest` nonce нельзя: nonce привязан к пользователю, а запрос без него обрабатывается как анонимный. Nonce при этом **не является авторизацией** — он защищает от подделки запроса, а права проверяются отдельно.

**Режим по умолчанию — страничный кэш не применяется к авторизованным пользователям.** Это поведение большинства кэширующих плагинов; диагностика проверяет его и предупреждает при отклонении. Для анонимных динамические компоненты рендерят гостевое состояние статически, без обращений к REST.

**Резервный режим** (если сайт всё же кэширует для авторизованных):

1. Некэшируемый same-origin эндпоинт начальной загрузки **вне REST**, проверяющий cookie входа и capability, возвращающий `wp_rest` nonce и начальное состояние пользователя. Заголовки `Cache-Control: private, no-store`, запрет кэширования на CDN.
2. Затем **один** REST batch-запрос наполняет все динамические компоненты страницы. Обязательный `permission_callback`, те же заголовки, ответ никогда не содержит данных другого пользователя.

---

## 11. Миграции

### 11.1. Механика

`modules/{id}/migrations/{version}_{name}.php`. `Migrator` сверяется с `krs_migration`, применяет недостающие по порядку, пишет контрольную сумму. Изменение применённой миграции — ошибка установки. Установка и обновление выполняются под блокировкой.

### 11.2. Изменение схемы

Утверждение версии 0.2 о том, что создание колонки — «быстрая неблокирующая операция», **удалено**: онлайн-DDL поддерживается не для всех операций и не на всех версиях сервера, а запрос более слабой блокировки, чем допускает операция, завершается ошибкой.

Обязательная последовательность для изменений существующих таблиц:

1. Проверить версию и возможности сервера (поддержка `ALGORITHM=INPLACE`, `LOCK=NONE`).
2. Оценить размер таблицы.
3. Если операция безопасна на этом сервере и объёме — выполнить напрямую.
4. Иначе: создать новую таблицу или колонку → включить двойную запись → скопировать данные пакетами фоновой задачей → сверить → переключить чтение → удалить старую структуру **в следующем релизе**.
5. При невозможности безопасного выполнения — явный отказ с инструкцией либо режим обслуживания с явным подтверждением администратора. Тихая блокирующая операция на боевой базе недопустима.

Для особо крупных изменений — теневая таблица с атомарным переименованием.

### 11.3. Деактивация и удаление

Деактивация: снимаются хуки и расписания, данные сохраняются. Удаление: только по явному подтверждению с перечнем удаляемого и предложением выгрузки.

---

## 12. Секреты

Ключ шифрования задаётся константой в `wp-config.php`.

- Если константа отсутствует, мастер требует её создать и показывает готовую строку для вставки.
- **До создания ключа сохранение ключей ИИ, токена Telegram и лицензии невозможно.** Диагностика показывает блокирующую ошибку.
- Незашифрованное хранение как запасной вариант **не предусмотрено ни при каких условиях**.

**Ротация:** расшифровать старым ключом → зашифровать новым → проверить чтение → переключить активный `key_id`. Оба ключа сосуществуют на время ротации; после проверки старый удаляется. Ротация фиксируется в аудите.

Секреты никогда не попадают в автозагружаемые опции, выгрузки данных, экспорт диагностики и журналы.

---

## 13. Безопасность

| Область | Требование |
|---|---|
| REST для CPT | Для каждого типа явно задаются `public`, `publicly_queryable`, `show_in_rest`, `supports`, `capability_type`, `map_meta_cap`, контроллер и доступ к ревизиям. Уроки, вопросы, задания, рубрики и ответы закрыты для гостей |
| REST | `permission_callback` обязателен и никогда `__return_true`; внутри — `Policy::can()` с объектом либо `Policy::scope()` для коллекции |
| AJAX | nonce + capability + политика |
| SQL | только `$wpdb->prepare`; имена таблиц и колонок из белого списка |
| WooCommerce | только CRUD API, никакого прямого доступа к таблицам заказов; объявленная совместимость с HPOS |
| Загрузки | белый список MIME **и** расширения, лимит размера и количества, переименование файла |
| Хранение файлов | вне корня сайта либо в защищённом каталоге; выдача через PHP по подписанной ссылке с проверкой политики |
| Видео | подписанные ссылки с коротким сроком жизни |
| Лимиты | сдача работы, попытка теста, обращение к ИИ, активация приглашения, вход, генерация черновика |
| Сериализация | `unserialize()` запрещён; только JSON с проверкой схемы |
| Вывод | типизированный, см. §8.4 |
| Секреты | см. §12 |
| Аудит | зачисления, основания, оценки, роли, настройки, лицензии, заморозка, переназначения, изменение дедлайнов, выгрузка и удаление данных, удаление файлов |
| Приватность | экспортёр и стиратель WordPress для всех таблиц; выгрузка по белому списку полей |

**Отказ модуля `ai`, недоступность провайдера, очереди или лицензионного сервера не блокируют ни один учебный или платёжный сценарий.** Проверяется тестом с недоступной сетью.

---

## 14. Тестовая стратегия

| Вид | Что покрывает | Порог |
|---|---|---|
| Модульные | сервисы, машины состояний, политики, капельная выдача, начисление баллов, формула доступа | покрытие доменной логики ≥ 80% |
| Интеграционные | каждый модуль с БД | все сценарии функциональной части |
| Контрактные | паритет адаптеров, пять уровней | 100%, падение сборки при расхождении |
| Изоляции | включён только модуль X → ноль хуков, маршрутов, задач, ассетов остальных | 100% модулей |
| Миграционные | чистая установка + обновление с каждой выпущенной версии | все версии |
| Конкурентности | одновременное начисление баллов, повторная доставка события, параллельный `claim`, два одновременных заказа на один курс, параллельная миграция | ноль дублей и потерянных обновлений |
| Отказа посередине | падение процесса между записью дедупликации и эффектом; обрыв внешнего вызова | ноль заблокированных навсегда операций |
| Бюджет производительности | см. ниже | превышение = падение сборки |
| Совместимости | HPOS и устаревшее хранилище заказов, с/без WooCommerce, объектного кэша, страничного кэша, Elementor | матрица |
| Безопасности | неэкранированный вывод, неподготовленные запросы, отсутствие `permission_callback`, прямой доступ к файлам, REST-утечка закрытых CPT, прямой доступ к таблицам заказов | ноль нарушений |
| Отказоустойчивости | недоступны сеть, ИИ-провайдер, лицензионный сервер, очередь | LMS работоспособна |

### 14.1. Воспроизводимые бюджеты производительности

Замер сравнивает **одну и ту же среду с включённым и отключённым модулем** — общий расход страницы нельзя приписывать нам целиком.

Фиксируется: объём тестовых данных (число курсов, уроков, учеников, записей прогресса) · версии WordPress, WooCommerce, Elementor · тема · наличие объектного кэша · холодный и тёплый прогон · общий бюджет страницы · **отдельно прирост запросов, памяти и времени от Kursio** · суммарное время SQL · самый долгий запрос · отсутствие непредусмотренных полных сканов таблиц · p95 времени серверного ответа по серии прогонов.

Предварительные ориентиры прироста (уточняются после первых замеров):

| Страница | Прирост запросов | Прирост памяти |
|---|---|---|
| Каталог, 20 карточек | ≤ 15 | ≤ 8 МБ |
| Курс, гость | ≤ 12 | ≤ 8 МБ |
| Курс, зачисленный | ≤ 20 | ≤ 12 МБ |
| Урок | ≤ 15 | ≤ 12 МБ |
| Кабинет ученика | ≤ 20 | ≤ 12 МБ |
| Очередь преподавателя, 50 работ | ≤ 25 | ≤ 16 МБ |

### 14.2. Матрица окружений

PHP 8.2 / 8.3 / 8.4 / 8.5 · WordPress — две последние мажорные версии · MySQL 5.7 и 8.0, MariaDB 10.6+ · WooCommerce с HPOS и с устаревшим хранилищем · с объектным кэшем и без · со страничным кэшем и без · с Elementor и без.

### 14.3. Телеметрия

`ai_edit_ratio` — приблизительная метрика; `active_review_time_s` учитывает только активное время. Локальная статистика доступна школе всегда; передача агрегатов разработчику — только по явному включению. Метрики измеряют использование установленного продукта и не измеряют рыночный спрос.

---

## 15. Требует решения владельца

**Прокси для доступа к ИИ-провайдерам.** Ранее обсуждался вариант с собственным ключом OpenAI и частным SOCKS5-прокси; в функциональной части средства обхода региональных ограничений исключены. Это не решено молча и требует ответа:

- **Вариант А** — прокси исключён полностью.
- **Вариант Б (рекомендую)** — поддерживается обычный настраиваемый HTTP/SOCKS-прокси как транспорт: это штатная потребность корпоративных сетей и закрытых контуров. При этом мы **не поставляем средств обхода**, не рекомендуем их и не обещаем доступности какого-либо провайдера в какой-либо юрисдикции; ответственность за законность использования несёт школа. Учётные данные прокси хранятся по правилам §12.

До решения прокси не включается ни в код, ни в описание продукта.
