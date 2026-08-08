# ТЗ. Часть 1 — Техническая архитектура

**Продукт:** LMS для WordPress, ориентированная на русскоязычные онлайн-школы.
**Рабочее имя пакета:** `kursio` · Namespace `Kursio\` · Префикс таблиц `{$wpdb->prefix}krs_`
**Статус:** черновик v0.1 для согласования. Код не пишется.

Имя заменяется в одном месте — константы `KURSIO_SLUG`, `KURSIO_NS`, `KURSIO_TABLE_PREFIX`.

---

## 0. Исходные условия (зафиксированы владельцем, не пересматриваются)

1. Зарубежные коммерческие LMS и их платные дополнения официально недоступны целевой аудитории из РФ. Продукт даёт сопоставимый класс функций с легальной покупкой за рубли, обновлениями и поддержкой на русском.
2. Первая публичная версия содержит максимально полный согласованный набор функций. Публичного урезанного MVP нет. Разделение на Free и платные дополнения выполняется после разработки и тестирования полного продукта.
3. Стоимость разработки не является аргументом для сокращения функциональности. Качество, тестирование и совместимость — являются требованиями.
4. Внешние проверки (стенды конкурентов, замеры, интервью) не выполняются. Все решения принимаются из худшего предположения: у конкурентов есть паритет по любой обсуждаемой функции.

**Следствие для архитектуры №4:** ни одно проектное решение не должно зависеть от результата несостоявшегося сравнения. Обещание производительности формулируется как проверяемое клиентом на своём сайте: *отключённый модуль не грузит ничего*.

---

## 1. Слои системы

```
┌─────────────────────────────────────────────────────────┐
│  Adapters:  Elementor │ Gutenberg │ Shortcode │ PHP API  │
├─────────────────────────────────────────────────────────┤
│  Presentation:  ComponentRegistry → Component → Renderer │
│                 ContextResolver                          │
├─────────────────────────────────────────────────────────┤
│  Domain modules: core access quiz assignment teacher woo │
│                  ai gamification notify certificate      │
│                  reports import                          │
├─────────────────────────────────────────────────────────┤
│  Platform: Container · ModuleRegistry · Migrator · Queue │
│            EventBus · Cache · Policy · Audit · REST      │
│            License · Diagnostics · Privacy               │
├─────────────────────────────────────────────────────────┤
│  WordPress: CPT · Users · Options · Cron · REST · $wpdb  │
└─────────────────────────────────────────────────────────┘
```

Правило слоёв: обращение только вниз. Модуль домена не знает об адаптерах. Компонент не знает, какой адаптер его вызвал. Платформа не знает о доменных модулях.

---

## 2. Модульная система

### 2.1. Контракт модуля

```php
interface ModuleInterface {
    public static function id(): string;              // 'quiz'
    public static function dependsOn(): array;        // ['core','access']
    public static function requiresPlugins(): array;  // ['woocommerce/woocommerce.php']
    public static function migrations(): string;      // путь к каталогу миграций
    public function register(Container $c): void;     // ТОЛЬКО определения сервисов
    public function boot(EventBus $bus): void;        // ТОЛЬКО регистрация хуков
    public function assets(): array;                  // декларация, не подключение
}
```

**Жёсткие правила (проверяются тестом изоляции):**

| Правило | Проверка |
|---|---|
| `register()` не обращается к БД, не добавляет хуков, не читает опций | статический анализ + тест |
| `boot()` вызывается только если модуль включён и все зависимости удовлетворены | тест |
| Отключённый модуль не регистрирует ни одного хука, REST-маршрута, фоновой задачи, ассета и не обращается к своим таблицам | тест изоляции |
| Ассеты подключаются только на страницах, где компонент модуля реально выведен | тест бюджета |
| Модуль не вызывает классы другого модуля напрямую — только через интерфейсы из `platform/Contracts` или через события | статический анализ |

### 2.2. Загрузка

Единственное чтение конфигурации при старте — автозагружаемая опция `krs_modules`:

```json
{ "core":{"v":"1.0.0","on":true}, "quiz":{"v":"1.0.0","on":true}, "ai":{"v":"1.0.0","on":false} }
```

Классы грузятся PSR-4 автозагрузчиком; отключённый модуль не инстанцируется. Feature flags — отдельная опция `krs_flags` для поэтапного включения незавершённых частей внутри включённого модуля.

### 2.3. Граф зависимостей

```
platform
└── core
    ├── access ──────────────┐
    ├── frontend (core, access)
    ├── quiz (core, access)
    ├── assignment (core, access)
    │   └── teacher (assignment, frontend)
    ├── woo (core, access) + плагин WooCommerce
    ├── certificate (core, access)
    ├── reports (core, access)
    ├── gamification (core, access)   ← только потребитель событий
    ├── notify (core)                 ← только потребитель событий
    ├── ai (platform)                 ← поставщик услуги, потребители: assignment, quiz, core
    └── import (core, access; опционально quiz, assignment)
```

`gamification`, `notify`, `reports` **не имеют обратных зависимостей**: они подписаны на шину событий и работают с любым набором включённых модулей. `ai` — сервис, к которому обращаются другие модули через интерфейс `AiProviderInterface`; его отсутствие не ломает ни один сценарий.

---

## 3. Модель данных

### 3.1. Принцип разделения

| Что | Где | Почему |
|---|---|---|
| Контент: курсы, разделы, уроки, тесты, вопросы, задания, шаблоны сертификатов | **CPT + таксономии** | Бесплатно получаем редактор, ревизии, права, permalinks, поиск, Gutenberg, REST. Низкая кардинальность |
| Связи и состояние: зачисления, прогресс, попытки, ответы, сдачи, проверки, события, баллы | **Собственные таблицы с индексами** | Высокая кардинальность (ученики × элементы), диапазонные запросы и агрегация. Хранение этого в `postmeta` — главная причина медленных LMS |

**Запрещено:** хранить прогресс, попытки и зачисления в `wp_postmeta` или `wp_usermeta` в любом виде.

### 3.2. Типы записей (CPT)

| CPT | Иерархия | Примечание |
|---|---|---|
| `krs_course` | — | Курс |
| `krs_section` | parent = course | Раздел |
| `krs_lesson` | parent = section | Урок; тип контента в мета-поле |
| `krs_quiz` | parent = section \| course | Тест |
| `krs_question` | — | Вопрос; принадлежность к тесту и банку — через таксономию `krs_qbank` и связь |
| `krs_assignment` | parent = section \| lesson | Домашнее задание |
| `krs_cert_template` | — | Шаблон сертификата |
| `krs_bundle` | — | Комплект курсов |

Таксономии: `krs_course_cat`, `krs_course_tag`, `krs_qbank`, `krs_level`.

Структурные связи курса (порядок разделов и уроков) дублируются в таблицу `krs_curriculum` для быстрого построения программы одним запросом.

### 3.3. Таблицы

Все таблицы: `ENGINE=InnoDB`, `utf8mb4_unicode_520_ci`, `id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY`, `created_at`/`updated_at DATETIME`.

#### krs_curriculum — плоская структура курса
```
course_id BIGINT UNSIGNED
item_id BIGINT UNSIGNED
item_type VARCHAR(20)        -- section|lesson|quiz|assignment
parent_id BIGINT UNSIGNED NULL
position INT UNSIGNED
depth TINYINT UNSIGNED
is_gradable TINYINT(1)
drip_rule_json JSON NULL      -- {type:date|days_after_enroll|after_item, value:...}
prereq_json JSON NULL
PRIMARY KEY (course_id, item_id)
KEY idx_order (course_id, position)
KEY idx_item (item_id)
```

#### krs_enrollment — зачисление
```
user_id, course_id BIGINT UNSIGNED
status VARCHAR(20)            -- pending|active|paused|expired|revoked|completed
source VARCHAR(20)            -- manual|woo|invite|import|api|bulk
source_ref VARCHAR(64) NULL   -- id заказа и т.п.
group_id BIGINT UNSIGNED NULL
started_at, expires_at, paused_at, completed_at DATETIME NULL
UNIQUE KEY uq_user_course (user_id, course_id)
KEY idx_course_status (course_id, status)
KEY idx_expiry (status, expires_at)
KEY idx_group (group_id, status)
```
Повторная покупка не создаёт вторую строку — обновляет существующую и пишет в `krs_enrollment_log`.

#### krs_enrollment_log
```
enrollment_id, actor_id BIGINT UNSIGNED NULL
from_status, to_status VARCHAR(20)
reason VARCHAR(64), meta_json JSON NULL
KEY idx_enr (enrollment_id, created_at)
```

#### krs_progress — прогресс по элементам
```
user_id, course_id, item_id BIGINT UNSIGNED
item_type VARCHAR(20)
status VARCHAR(20)            -- not_started|in_progress|completed
pct TINYINT UNSIGNED
position_s INT UNSIGNED       -- позиция в видео/аудио
first_seen_at, completed_at DATETIME NULL
UNIQUE KEY uq_user_item (user_id, item_id)
KEY idx_course_user (course_id, user_id, status)
KEY idx_user_recent (user_id, updated_at)
```

#### krs_course_progress — денормализованный агрегат
```
user_id, course_id BIGINT UNSIGNED
items_total, items_done SMALLINT UNSIGNED
pct TINYINT UNSIGNED
last_item_id BIGINT UNSIGNED NULL
last_activity_at, completed_at DATETIME NULL
PRIMARY KEY (user_id, course_id)
KEY idx_recent (user_id, last_activity_at)
KEY idx_course_pct (course_id, pct)
```
**Обязателен.** Вычисление процента курса из `krs_progress` на каждом рендере — классическая причина деградации. Пересчёт — по событию, в фоновой задаче, идемпотентно.

#### krs_quiz_attempt
```
user_id, quiz_id, course_id BIGINT UNSIGNED
attempt_no SMALLINT UNSIGNED
status VARCHAR(20)            -- in_progress|submitted|grading|graded|expired
score_raw, score_max DECIMAL(8,2) NULL
score_pct DECIMAL(5,2) NULL
passed TINYINT(1) NULL
time_spent_s INT UNSIGNED NULL
started_at, submitted_at, graded_at, expires_at DATETIME NULL
UNIQUE KEY uq_attempt (user_id, quiz_id, attempt_no)
KEY idx_quiz_status (quiz_id, status)
KEY idx_course_user (course_id, user_id)
KEY idx_pending (status, submitted_at)
```

#### krs_quiz_answer
```
attempt_id, question_id BIGINT UNSIGNED
question_type VARCHAR(20)
answer_json JSON
is_correct TINYINT(1) NULL    -- NULL = ждёт ручной проверки
points_awarded, points_max DECIMAL(8,2)
grader_id BIGINT UNSIGNED NULL
graded_at DATETIME NULL
remarks LONGTEXT NULL
ai_draft_id BIGINT UNSIGNED NULL
KEY idx_attempt (attempt_id)
KEY idx_question (question_id)
KEY idx_manual_queue (is_correct, graded_at)
```

#### krs_submission — сдача домашней работы
```
user_id, assignment_id, course_id BIGINT UNSIGNED
attempt_no SMALLINT UNSIGNED
status VARCHAR(20)            -- draft|submitted|in_review|returned|accepted|rejected
body LONGTEXT NULL
payload_json JSON NULL        -- файлы, ссылки, метаданные
assignee_id BIGINT UNSIGNED NULL
claimed_at, submitted_at, deadline_at DATETIME NULL
UNIQUE KEY uq_sub (user_id, assignment_id, attempt_no)
KEY idx_queue (status, submitted_at)
KEY idx_assignee (assignee_id, status)
KEY idx_assignment (assignment_id, status)
KEY idx_course (course_id, status)
```

#### krs_review — решение проверяющего
```
submission_id, reviewer_id BIGINT UNSIGNED
decision VARCHAR(20)          -- accepted|returned|rejected
score DECIMAL(8,2) NULL
feedback LONGTEXT NULL
rubric_json JSON NULL         -- пооценочно по критериям
ai_draft_id BIGINT UNSIGNED NULL
ai_edit_ratio DECIMAL(5,4) NULL   -- доля изменённого текста черновика
review_time_s INT UNSIGNED NULL
KEY idx_sub (submission_id)
KEY idx_reviewer (reviewer_id, created_at)
```
`ai_edit_ratio` и `review_time_s` — встроенная метрика полезности ИИ. Собирается с первого дня, без отдельного пилота.

#### krs_rubric / krs_rubric_criterion
```
krs_rubric: subject_type(assignment|question), subject_id, version SMALLINT, title, is_active
  UNIQUE KEY uq (subject_type, subject_id, version)
krs_rubric_criterion: rubric_id, position, title, weight DECIMAL(5,2),
  levels_json JSON,            -- выполнено/частично/не выполнено с признаками
  common_errors_json JSON
  KEY idx_rubric (rubric_id, position)
```
Рубрика версионируется: изменение критериев не переписывает историю уже выставленных оценок.

#### krs_event — шина событий (журнал)
```
name VARCHAR(64)
user_id BIGINT UNSIGNED NULL
course_id BIGINT UNSIGNED NULL
subject_type VARCHAR(20) NULL, subject_id BIGINT UNSIGNED NULL
payload_json JSON NULL
idempotency_key CHAR(40)
occurred_at DATETIME(3)
UNIQUE KEY uq_idem (idempotency_key)
KEY idx_name_time (name, occurred_at)
KEY idx_user_time (user_id, occurred_at)
KEY idx_course_name (course_id, name)
```
`idempotency_key = sha1(name|user_id|subject_type|subject_id|bucket)` — единственный механизм, гарантирующий отсутствие двойных начислений и повторных уведомлений.

#### krs_points_ledger — начисления опыта, только добавление
```
user_id BIGINT UNSIGNED
delta INT
reason VARCHAR(64)
event_id BIGINT UNSIGNED NULL
balance_after INT
UNIQUE KEY uq_event_reason (event_id, reason)
KEY idx_user_time (user_id, created_at)
```
Реестр, а не счётчик в поле: даёт аудит, исключает потерянные обновления и делает защиту от накрутки структурной, а не проверочной.

#### krs_user_stats — денормализация геймификации
```
user_id BIGINT UNSIGNED PRIMARY KEY
xp_total INT, level SMALLINT
streak_days SMALLINT, streak_last_date DATE NULL
achievements_count SMALLINT
KEY idx_xp (xp_total)
```

#### krs_achievement / krs_user_achievement
```
krs_achievement: slug UNIQUE, title, description, icon_id, rule_json, points INT, is_active
krs_user_achievement: user_id, achievement_id, event_id, unlocked_at
  UNIQUE KEY uq (user_id, achievement_id)
  KEY idx_user_time (user_id, unlocked_at)
```

#### krs_group / krs_group_member — потоки
```
krs_group: course_id NULL, title, starts_at, ends_at, curator_id, capacity SMALLINT NULL, status
krs_group_member: group_id, user_id, role VARCHAR(20), joined_at
  UNIQUE KEY uq (group_id, user_id)
  KEY idx_user (user_id)
```

#### krs_invite
```
code VARCHAR(32) UNIQUE
target_type VARCHAR(20)       -- course|bundle|group
target_id BIGINT UNSIGNED
uses_max, uses_count SMALLINT UNSIGNED
duration_days SMALLINT NULL
expires_at DATETIME NULL
created_by BIGINT UNSIGNED
KEY idx_target (target_type, target_id)
```

#### krs_woo_link / krs_woo_grant
```
krs_woo_link: product_id, variation_id NULL, target_type(course|bundle), target_id,
  grant_statuses JSON,        -- какие статусы заказа считать успешными
  revoke_statuses JSON,
  duration_days SMALLINT NULL, group_id NULL
  KEY idx_product (product_id, variation_id)

krs_woo_grant: order_id, order_item_id, user_id, course_id, enrollment_id NULL,
  action VARCHAR(20),         -- grant|revoke|restore|failed
  reason VARCHAR(128) NULL, order_status VARCHAR(32)
  KEY idx_order (order_id)
  KEY idx_user_course (user_id, course_id)
  KEY idx_failed (action, created_at)
```
`krs_woo_grant` — это и есть требуемый журнал `заказ → пользователь → курс → доступ` и основа диагностики «оплата прошла, ученик не зачислен».

#### krs_ai_request — учёт обращений к моделям
```
module VARCHAR(20), operation VARCHAR(32)
subject_type VARCHAR(20), subject_id BIGINT UNSIGNED
provider VARCHAR(32), model VARCHAR(64)
status VARCHAR(20), error_code VARCHAR(40) NULL
tokens_in, tokens_out INT UNSIGNED NULL
cost_minor INT UNSIGNED NULL, latency_ms INT UNSIGNED NULL
request_hash CHAR(40)
KEY idx_subject (subject_type, subject_id)
KEY idx_time (created_at)
KEY idx_provider (provider, status)
```
**Тексты промптов и ответов не сохраняются.** Хранится только черновик, привязанный к сдаче (в `krs_ai_draft`), и он удаляется вместе с ней.

#### krs_ai_draft
```
subject_type, subject_id, rubric_version SMALLINT
draft_text LONGTEXT, structure_json JSON,   -- привязка замечаний к фрагментам ответа
confidence VARCHAR(10), needs_attention TINYINT(1)
ai_request_id BIGINT UNSIGNED
KEY idx_subject (subject_type, subject_id)
```

#### krs_certificate
```
user_id, course_id, template_id BIGINT UNSIGNED
serial VARCHAR(32) UNIQUE
file_path VARCHAR(255) NULL, file_hash CHAR(64) NULL
issued_at, revoked_at DATETIME NULL, revoke_reason VARCHAR(128) NULL
KEY idx_user (user_id)
KEY idx_course (course_id, issued_at)
```

#### krs_notification / krs_notification_log
```
krs_notification: event_name, channel VARCHAR(20), audience VARCHAR(20),
  template_id, delay_s INT, conditions_json, is_active

krs_notification_log: notification_id, user_id, channel, status,
  dedupe_key CHAR(40) UNIQUE, error VARCHAR(255) NULL, sent_at
  KEY idx_user_time (user_id, created_at)
```

#### krs_job / krs_job_dead — фоновые задачи (собственный драйвер)
```
krs_job: queue VARCHAR(32), handler VARCHAR(64), payload_json JSON,
  unique_key VARCHAR(64) NULL UNIQUE, available_at DATETIME,
  attempts TINYINT, reserved_at DATETIME NULL, reserved_by CHAR(32) NULL
  KEY idx_ready (queue, available_at, reserved_at)
krs_job_dead: те же поля + last_error, failed_at
```

#### krs_migration
```
module VARCHAR(20), version VARCHAR(20), checksum CHAR(40), applied_at
UNIQUE KEY uq (module, version)
```

#### krs_audit_log
```
actor_id BIGINT UNSIGNED NULL, action VARCHAR(64)
subject_type VARCHAR(20), subject_id BIGINT UNSIGNED NULL
ip_hash CHAR(64) NULL, ua_hash CHAR(64) NULL
meta_json JSON NULL
KEY idx_action_time (action, created_at)
KEY idx_subject (subject_type, subject_id)
KEY idx_actor (actor_id, created_at)
```

### 3.4. Бюджеты объёма

| Таблица | Порядок роста | Мера |
|---|---|---|
| `krs_progress` | ученики × элементы курса | партиционирование не нужно до ~10 млн строк; индексы покрывающие |
| `krs_event` | все действия | ретенция 180 дней по умолчанию, настраивается; агрегаты в `reports` живут отдельно |
| `krs_ai_request` | обращения к моделям | ретенция 365 дней (нужна для биллинга) |
| `krs_audit_log` | критические действия | ретенция 365 дней, не удаляется автоматически ниже |
| `krs_notification_log` | отправки | ретенция 90 дней |

Очистка — фоновой задачей пакетами, никогда одним `DELETE`.

---

## 4. Роли и права

### 4.1. Роли

| Роль | Назначение |
|---|---|
| `krs_student` | Ученик |
| `krs_curator` | Проверяет работы своих групп, не редактирует контент |
| `krs_instructor` | Преподаватель: свои курсы, свои ученики, проверка |
| `krs_author` | Создаёт и редактирует курсы, не управляет чужими |
| `administrator` | Всё |

### 4.2. Возможности (capabilities)

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

### 4.3. Слой политик — обязателен

Возможности отвечают на вопрос «что этой роли вообще разрешено». Они **не** отвечают на «разрешено ли этому пользователю с этим объектом». Второе — самая частая дыра в LMS-плагинах.

```php
interface PolicyInterface {
    public function can(int $userId, string $action, ?object $subject = null): bool;
}
```

Правила, проверяемые тестами:

| Действие | Помимо capability проверяется |
|---|---|
| `grade_submission` | проверяющий — куратор группы ученика ИЛИ преподаватель курса |
| `view_submission` | то же, либо автор сдачи |
| `view_lesson` | активное зачисление + правило капельной выдачи + предусловия |
| `download_attachment` | право на элемент, к которому файл относится |
| `view_report` | своя группа/курс, если нет `krs_view_all_reports` |
| `edit_course` | автор курса, если нет `krs_edit_others_courses` |
| `use_ai` | модуль включён + лимит не исчерпан + согласие школы на передачу данных получено |

**Ни один REST-маршрут и ни один AJAX-обработчик не выполняет действия без вызова `Policy::can()`.** Проверка `current_user_can()` без объекта — недостаточна и запрещена для всех операций с чужими данными.

---

## 5. Состояния сущностей

Переходы выполняются **только** через методы сервисов. Прямая запись поля `status` запрещена и ловится тестом.

**Enrollment**
```
              ┌──────────┐
  создано ──▶ │ pending  │──▶ active ──┬──▶ paused ──▶ active
              └──────────┘             ├──▶ expired
                                       ├──▶ revoked ──▶ active (восстановление)
                                       └──▶ completed
```

**QuizAttempt**
```
in_progress ──▶ submitted ──▶ grading ──▶ graded
      └──────────────────────────────────▶ expired (таймер)
```

**Submission**
```
draft ──▶ submitted ──▶ in_review ──┬──▶ accepted
                                    ├──▶ rejected
                                    └──▶ returned ──▶ submitted (attempt_no+1)
```

**Certificate:** `issued ──▶ revoked` (необратимо, выдаётся новый серийный номер при перевыпуске).

Каждый переход публикует событие. Каждый переход пишет в лог сущности. Недопустимый переход — исключение, а не тихое игнорирование.

---

## 6. Шина событий

### 6.1. Именование

`kursio/{module}.{entity}.{action}` — глагол в прошедшем времени. Событие констатирует свершившийся факт, а не команду.

### 6.2. Каталог событий v1

| Модуль | События |
|---|---|
| `access` | `enrollment.created` `.activated` `.paused` `.resumed` `.expired` `.revoked` `.completed` · `invite.redeemed` · `group.member_added` `.member_removed` |
| `core` | `lesson.started` `.completed` · `course.started` `.progress_changed` `.completed` · `content.published` `.updated` |
| `quiz` | `attempt.started` `.submitted` `.graded` · `quiz.passed` `.failed` · `answer.manually_graded` |
| `assignment` | `submission.created` `.submitted` `.claimed` `.returned` `.accepted` `.rejected` · `deadline.approaching` `.missed` |
| `ai` | `draft.requested` `.ready` `.failed` · `quota.exceeded` |
| `woo` | `order.linked` · `access.granted` `.revoked` `.restored` · `grant.failed` |
| `gamification` | `points.awarded` · `level.reached` · `streak.extended` `.broken` · `achievement.unlocked` |
| `certificate` | `certificate.issued` `.revoked` |

### 6.3. Правила доставки

1. **Синхронно** — только то, что влияет на текущий ответ пользователю (пересчёт доступности следующего шага).
2. **Асинхронно через очередь** — всё остальное: уведомления, начисление опыта, пересчёт агрегатов, отчёты, ИИ. Медленный Telegram никогда не задерживает завершение урока.
3. Каждый обработчик идемпотентен по `idempotency_key`. Повторная доставка не создаёт второго эффекта.
4. Ошибка обработчика не откатывает событие и не ломает соседних подписчиков — она попадает в `krs_job_dead`.
5. Событие пишется в `krs_event` **до** постановки задач: журнал — источник истины, очередь — механизм доставки.

---

## 7. Context Resolver

Решает ровно ту проблему, из которой вырос проект: компонент на произвольной странице должен сам понимать, о ком и о чём речь.

### 7.1. Структура

```php
final class Context {
    public ?WP_User $user;
    public ?Course $course;
    public ?Section $section;
    public ?Lesson $lesson;
    public ?Quiz $quiz;
    public ?Assignment $assignment;
    public ?Enrollment $enrollment;
    public ?CourseProgress $progress;
    public ?NextStep $nextStep;      // {type, id, url, label, reason}
    public string $source;           // explicit|query|post|request|history|single|none
    public string $confidence;       // exact|inferred|none
}
```

### 7.2. Цепочка разрешения (первое совпадение выигрывает)

| № | Источник | confidence |
|---|---|---|
| 1 | Явные свойства компонента (`course_id`, `lesson_id` в виджете/шорткоде) | `exact` |
| 2 | Query vars и правила перезаписи (страница курса, страница урока) | `exact` |
| 3 | Связи текущей записи (открыт урок → его курс и раздел) | `exact` |
| 4 | Параметры запроса (`?krs_course=`) с проверкой прав | `exact` |
| 5 | **Последняя активность пользователя** (`krs_course_progress.last_activity_at`) | `inferred` |
| 6 | Единственный курс на сайте | `inferred` |
| 7 | Ничего | `none` |

Пункт 5 — то, что позволяет положить «Продолжить обучение» на главную страницу.

### 7.3. Правила

- Резолвер выполняется **один раз за запрос**, результат мемоизируется.
- **Ленивая загрузка по группам полей:** пока компонент не спросил `progress`, запрос за прогрессом не выполняется. Бюджет: разрешение контекста без обращения к полям — 0 запросов к БД.
- `confidence` доступен компоненту. Правило деградации обязательно: при `inferred` компонент вправе изменить формулировку («Продолжить обучение» вместо «Продолжить урок 3»), при `none` — показать запасной вариант или ничего, но **никогда не показывать ошибку и не выводить чужие данные**.
- Явное свойство всегда перекрывает автоматику. Автоматика никогда не перекрывает явное.
- Для анонимного пользователя контекст разрешается без обращения к пользовательским таблицам вовсе.

---

## 8. Component / Renderer

### 8.1. Схема компонента — единственный источник истины

```php
final class ComponentSchema {
    public string $id;            // 'course_action'
    public string $title;         // 'Действие курса'
    public string $category;      // 'course'
    public array  $requires;      // ['course']
    public array  $props;         // PropSchema[]: name,type,default,label,control,group
    public array  $states;        // ['guest','not_enrolled','enrolled','in_progress','completed','expired','locked']
    public bool   $dynamic;       // зависит от пользователя → не кэшируется страничным кэшем
    public array  $assets;        // css/js handles
}
```

Из схемы **автоматически генерируются**: элементы управления виджета Elementor, `block.json` и inspector Gutenberg, атрибуты шорткода с валидацией, сигнатура PHP API, документация. Добавление компонента — один файл схемы плюс один класс, а не четыре реализации.

### 8.2. Разделение ответственности

```php
interface ComponentInterface {
    public static function schema(): ComponentSchema;
    public function resolve(Context $ctx, array $props): ViewModel;  // чистая функция, без HTML
}

interface RendererInterface {
    public function render(ViewModel $vm, string $template): string; // единственное место экранирования
}
```

**Правила, нарушение которых ломает всю концепцию:**

1. `resolve()` не содержит ни одной строки HTML, не вызывает `echo`, не знает об адаптере. Ни одной ветки вида «если Elementor».
2. Адаптер не содержит бизнес-логики. Он только собирает свойства и вызывает компонент.
3. ViewModel — чистые данные. Экранирование происходит исключительно в шаблоне рендерера.
4. Шаблоны переопределяются темой по пути `theme/kursio/{component}.php`, ViewModel при этом не меняется.

### 8.3. Тест паритета — обязателен в CI

Для каждого компонента × каждое состояние × каждый адаптер строится ViewModel и сравнивается. Любое расхождение — падение сборки. Это техническая гарантия того, что через год Elementor, Gutenberg и шорткоды не разъедутся.

### 8.4. Состав компонентов v1

| Группа | Компоненты |
|---|---|
| Курс | `course_action` · `course_progress` · `course_curriculum` · `course_meta` · `course_price` · `course_instructor` |
| Урок | `lesson_content` · `lesson_nav` · `lesson_complete` · `lesson_attachments` |
| Каталог | `catalog` · `catalog_filters` · `course_card` |
| Кабинет | `my_courses` · `continue_learning` · `my_deadlines` · `my_certificates` · `my_results` |
| Оценивание | `quiz` · `quiz_results` · `assignment_form` · `assignment_status` · `submission_history` |
| Преподаватель | `teacher_queue` · `teacher_review` · `teacher_stats` |
| Геймификация | `points_badge` · `level_bar` · `streak` · `achievements` · `leaderboard` |
| Служебные | `conditional_wrap` (показ по состоянию контекста) · `enroll_button` · `invite_form` |

`conditional_wrap` закрывает требование «расширенные условия отображения» без отдельного механизма в каждом виджете.

---

## 9. Фоновые задачи

### 9.1. Абстракция и драйверы

```php
interface QueueInterface {
    public function push(string $handler, array $payload, ?string $uniqueKey = null, int $delay = 0): void;
}
```

| Драйвер | Условие выбора |
|---|---|
| `ActionSchedulerDriver` | Action Scheduler присутствует (поставляется с WooCommerce) |
| `TableDriver` | иначе — собственная таблица `krs_job` |

Запуск `TableDriver`: системный cron (рекомендуется, инструкция в мастере установки), WP-Cron (по умолчанию), REST-эндпоинт для внешнего вызова. Диагностика показывает фактический режим и предупреждает, если WP-Cron не срабатывает.

### 9.2. Задачи

| Задача | Триггер | Идемпотентность |
|---|---|---|
| Пересчёт `krs_course_progress` | `lesson.completed`, `quiz.passed`, изменение программы | ключ `user:course` |
| Начисление опыта и достижений | события | ключ `event_id:reason` |
| Отправка уведомления | событие + правило | `dedupe_key` |
| Генерация ИИ-черновика | `submission.submitted` | ключ `submission:attempt` |
| Генерация PDF сертификата | `course.completed` | ключ `user:course` |
| Истечение зачислений | ежечасно | по строкам с `expires_at <= now` |
| Пересчёт серий | ежесуточно | ключ `user:date` |
| Импорт | пакетами по 100 | ключ пакета |
| Очистка по ретенции | ежесуточно | пакетами |

### 9.3. Правила

- Ни одна тяжёлая операция не выполняется в веб-запросе. Максимум в запросе — постановка задачи.
- Каждая задача: `unique_key`, до 5 попыток, экспоненциальная пауза, затем `krs_job_dead` с уведомлением администратору.
- Задача, не завершившаяся за 30 секунд, обязана уметь продолжаться с сохранённой позиции.

---

## 10. Кэширование

### 10.1. Три уровня

| Уровень | Что |
|---|---|
| Денормализация в БД | `krs_course_progress`, `krs_user_stats`, `krs_curriculum` — основной механизм, а не кэш |
| Объектный кэш | группа `kursio`, per-request всегда, персистентный при наличии Redis/Memcached |
| Фрагментный кэш | каталог, программа курса, карточки — только для не-пользовательских ViewModel |

### 10.2. Инвалидация версионными метками

Ключ включает метку версии сущности: `krs:course:{id}:v{n}:curriculum`. Запись в курс увеличивает `n` — старые ключи становятся недостижимыми и умирают по TTL. Ручной сброс кэша не требуется и не предусмотрен.

### 10.3. Совместимость со страничным кэшем — обязательное требование

Компонент со `dynamic: true` (зависящий от пользователя) при обнаружении страничного кэша рендерится как плейсхолдер, наполняемый одним лёгким REST-запросом после загрузки. Один запрос на страницу для всех динамических компонентов сразу, а не по одному на каждый.

Определение наличия кэша: известные константы и заголовки популярных плагинов, плюс ручное переключение в настройках. Совместимость проверяется тестом с включённым и выключенным страничным кэшем.

---

## 11. Миграции

### 11.1. Механика

Файлы миграций внутри модуля: `modules/{id}/migrations/{version}_{name}.php`. Применение: `Migrator` сверяется с `krs_migration`, применяет недостающие по порядку, пишет контрольную сумму файла. Изменение уже применённой миграции — ошибка установки, а не тихое расхождение схемы.

### 11.2. Правила

1. **Только вперёд.** Обратные миграции существуют лишь для схемы, добавленной в том же релизе.
2. **Сначала добавление.** Новая колонка → заполнение фоновой задачей → переключение чтения → удаление старой в следующем релизе. Никогда не в один шаг.
3. **Никаких блокирующих `ALTER` в веб-запросе.** Изменение больших таблиц — фоновой задачей пакетами, с флагом «схема в переходном состоянии» и корректной работой кода на обеих версиях.
4. Установка и обновление выполняются под блокировкой, чтобы параллельные запросы не запустили миграцию дважды.
5. Данные никогда не откатываются автоматически.

### 11.3. Деактивация и удаление

- Деактивация: снимаются хуки и планировщик, данные сохраняются полностью.
- Удаление: только по явному подтверждению в интерфейсе, с указанием, что именно будет удалено, и с предложением выгрузки. По умолчанию данные сохраняются.

---

## 12. Безопасность

| Область | Требование |
|---|---|
| REST | `permission_callback` обязателен и никогда не `__return_true`; внутри — `Policy::can()` с объектом |
| AJAX | nonce + capability + политика; отдельные nonce для действий, изменяющих данные |
| SQL | только `$wpdb->prepare`; имена таблиц и колонок — из белого списка, никогда из ввода |
| Загрузки | белый список MIME **и** расширения, лимит размера, антивирусная проверка при наличии, переименование файла |
| Хранение файлов | вне корня сайта либо в защищённом каталоге; выдача только через PHP по подписанной ссылке с проверкой политики. Прямые ссылки на файлы работ запрещены |
| Видео | подписанные ссылки с коротким сроком жизни |
| Лимиты | на сдачу работы, попытку теста, обращение к ИИ, активацию приглашения, вход в кабинет |
| Сериализация | `unserialize()` запрещён; только JSON с проверкой схемы |
| Экранирование | исключительно в шаблонах рендерера; ViewModel хранит сырые данные |
| Секреты | ключи ИИ и лицензии — зашифрованы, ключ шифрования из константы `wp-config.php`; никогда в автозагружаемых опциях, никогда в выгрузках, никогда в журналах |
| Аудит | обязателен для: изменения зачислений, оценок, ролей, настроек, лицензий, выгрузки и удаления данных |
| Приватность | зарегистрированы экспортёр и стиратель WordPress для всех наших таблиц; выгрузка по белому списку полей |
| Данные учеников и ИИ | к модели уходят: текст задания, рубрика, ответ, одобренные примеры. Не уходят: имя, почта, телефон, идентификаторы, название курса. Передача — только после явного включения школой |
| Обновления | подписанные пакеты, проверка целостности перед распаковкой |

Отдельно: **отказ модуля `ai` или недоступность лицензионного сервера не блокируют ни один учебный или платёжный сценарий.** Проверяется тестом с недоступной сетью.

---

## 13. Тестовая стратегия

| Вид | Что покрывает | Порог |
|---|---|---|
| Модульные | сервисы, машины состояний, политики, правила капельной выдачи, начисление опыта | покрытие доменной логики ≥ 80% |
| Интеграционные | каждый модуль с БД в тестовом окружении WordPress | все сценарии из функциональной части ТЗ |
| Контрактные | **паритет адаптеров**: компонент × состояние × адаптер | 100%, падение сборки при расхождении |
| Изоляции | включён только модуль X → ноль хуков, маршрутов, задач и ассетов остальных | 100% модулей |
| Миграционные | чистая установка + обновление с каждой выпущенной версии | все версии |
| Бюджет производительности | лимит запросов к БД и пиковой памяти на тип страницы | превышение = падение сборки |
| Совместимости | с/без WooCommerce, с/без объектного кэша, с/без страничного кэша, с/без Elementor | матрица |
| Безопасности | неэкранированный вывод, неподготовленные запросы, отсутствие `permission_callback`, прямой доступ к файлам | ноль нарушений |
| Отказоустойчивости | недоступны: сеть, ИИ-провайдер, лицензионный сервер, очередь | LMS полностью работоспособна |

### Бюджеты запросов (предварительные, уточняются после первых замеров)

| Страница | Запросов к БД | Пиковая память |
|---|---|---|
| Каталог курсов (20 карточек) | ≤ 25 | ≤ 24 МБ |
| Страница курса, гость | ≤ 20 | ≤ 24 МБ |
| Страница курса, зачисленный | ≤ 30 | ≤ 32 МБ |
| Страница урока | ≤ 25 | ≤ 32 МБ |
| Кабинет ученика | ≤ 30 | ≤ 32 МБ |
| Очередь преподавателя (50 работ) | ≤ 35 | ≤ 40 МБ |

Именно эти цифры, а не сравнение с конкурентами, операционализируют требование лёгкости. Они проверяются в CI на каждом коммите и не зависят от чужих продуктов.

### Матрица окружений

PHP 8.1 / 8.2 / 8.3 / 8.4 · WordPress две последние мажорные версии · MySQL 5.7 и 8.0, MariaDB 10.6+ · с объектным кэшем и без · с WooCommerce и без · с Elementor и без.

---

## 14. Открытые вопросы к функциональной части (зона Sol)

1. Правила зачисления при частичном возврате и при заказе с несколькими курсами.
2. Поведение при истечении доступа: сохраняется ли прогресс, виден ли контент только для чтения, что происходит с сертификатом.
3. Порядок применения капельной выдачи и предусловий при одновременном действии (что приоритетнее).
4. Правила пересдачи теста: обнуляется ли прогресс, какая попытка идёт в зачёт.
5. Кто получает работу в очередь при отсутствии куратора у группы.
6. Политика начисления опыта при возврате работы на доработку и повторной сдаче.
7. Условия автоматической выдачи сертификата и правила его отзыва.
8. Точный состав данных, передаваемых каждому провайдеру ИИ, и текст согласия школы.
