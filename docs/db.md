# Схема БД

PostgreSQL, схема задаётся миграциями EF Core (`src/Fiit.Api/Data/Migrations`). Имена — snake_case от сущностей `src/Fiit.Api/Domain`. Диаграмма ниже сгенерирована из живой схемы; после новой миграции её пересобирают:

```bash
docker compose run --rm migrate migrate   # локальная БД на последней миграции
python3 scripts/db-diagram.py             # перезаписывает блок диаграммы в этом файле
```

## Таблицы

| Область | Таблицы |
|---|---|
| Преподаватели и доступ | `teachers` — аккаунты (`added_by_id` — кто завёл, `sessions_valid_from` — отзыв JWT); `course_accesses` — кому из коллег открыт курс; `tags` + `student_tags` — личные метки преподавателя на студентах |
| Курсы | `subjects` — справочник предметов; `groups` + `subgroups` — ФТ-201 и её подгруппы; `courses` — предмет × подгруппа × семестр, `admin_teacher_id` — ведущий |
| Набор студентов | `invites` + `invite_courses` — ссылки-приглашения на один или несколько курсов; `students` — анкета из бота (`telegram_user_id`); `enrollments` — заявка студента в курс: Pending / Approved / Rejected, кто и когда решил |
| Задания | `homeworks` — ДЗ (черновик, пока `published_at` пуст; `max_score` — если нет подзадач с баллами); `homework_courses` — в каких курсах; `homework_targets` — сужение адресатов: `kind` (Course / Tag / Student) + `ref_id` (ссылка без FK, тип зависит от `kind`); пусто — все студенты курсов; `homework_recipients` — кому фактически разослано при публикации; `subtasks` — подзадачи с баллами |
| Сдача и проверка | `submissions` — версии работы студента (файл в S3 по `file_key`); `reviews` — проверка версии: черновик (`annotations`, `comment`, `total_override`, `draft_hash`) и отправленное (`sent_*`, `sent_file_key` — PDF с пометками); `subtask_scores` — баллы по подзадачам в черновике |
| Уведомления | `notifications` — outbox сообщений студенту: Pending → Published (в RabbitMQ) → Delivered / Failed |

**Уникальность, которой нет на диаграмме** (составные и частичные индексы):

- `courses (subject_id, subgroup_id, semester_year, semester_term)` — один курс на подгруппу в семестре;
- `enrollments (student_id, course_id) where status in (Pending, Approved)` — одна живая заявка; отклонённых может быть сколько угодно, можно подать снова;
- `submissions (homework_id, student_id, version)` — номер версии; при гонке двух сдач API повторяет с новым номером;
- `subgroups (group_id, number)`, `tags (teacher_id, name)`, `subtask_scores (review_id, subtask_id)`, `homework_recipients (homework_id, student_id)`, а также пары в связующих таблицах `course_accesses`, `homework_courses`, `invite_courses`, `student_tags`;
- `notifications (id) where status = 'Pending'` — частичный индекс для публикатора outbox (не уникальный).

**Связи без FK:** `homework_targets.ref_id` (см. выше), `notifications.homework_id` (для кнопки «Сдать» в боте).

**Чего в схеме нет намеренно:** опоздание не хранится (`submitted_at > deadline` по текущему дедлайну), итог сводной не хранится (сумма по отправленным проверкам), файлы лежат в S3, в БД только ключи.

## Диаграмма

`PK` — первичный ключ, `FK` — внешний, `UK` — уникальный, `"null"` — может быть пустым. Связь `|o` — необязательная ссылка, `o|` — не больше одной записи.

<!-- db-diagram:begin -->
```mermaid
erDiagram
    course_accesses {
        int course_id PK,FK
        int teacher_id PK,FK
    }
    courses {
        int id PK
        int subject_id FK
        int subgroup_id FK
        int semester_year
        text semester_term
        int admin_teacher_id FK
        timestamptz created_at
        timestamptz archived_at "null"
    }
    enrollments {
        int id PK
        int student_id FK
        int course_id FK
        int invite_id FK "null"
        text status
        text reject_reason "null"
        timestamptz created_at
        timestamptz decided_at "null"
        int decided_by_id FK "null"
    }
    groups {
        int id PK
        text name UK
    }
    homework_courses {
        int courses_id PK,FK
        int homework_id PK,FK
    }
    homework_recipients {
        int homework_id PK,FK
        int student_id PK,FK
        timestamptz notified_at
    }
    homework_targets {
        int id PK
        int homework_id FK
        text kind
        int ref_id
    }
    homeworks {
        int id PK
        int subject_id FK
        int semester_year
        text semester_term
        int author_id FK
        text title
        text link
        timestamptz deadline
        timestamptz created_at
        timestamptz published_at "null"
        decimal max_score "null"
    }
    invite_courses {
        int courses_id PK,FK
        int invite_id PK,FK
    }
    invites {
        int id PK
        text code UK
        int author_id FK
        int subject_id FK
        timestamptz expires_at "null"
        timestamptz closed_at "null"
        timestamptz created_at
    }
    notifications {
        int id PK
        int student_id FK
        text kind
        text text
        int homework_id "null"
        text file_key "null"
        text file_name "null"
        text status
        timestamptz created_at
        timestamptz delivered_at "null"
        text last_error "null"
        timestamptz published_at "null"
    }
    reviews {
        int id PK
        int submission_id FK,UK
        int updated_by_id FK
        jsonb annotations
        text comment "null"
        decimal total_override "null"
        timestamptz updated_at
        text draft_hash
        timestamptz sent_at "null"
        text sent_hash "null"
        decimal sent_total "null"
        text sent_comment "null"
        jsonb sent_subtasks "null"
        text sent_file_key "null"
        int sent_by_id FK "null"
    }
    student_tags {
        int tag_id PK,FK
        int student_id PK,FK
    }
    students {
        int id PK
        bigint telegram_user_id UK
        text telegram_username "null"
        text first_name
        text last_name
        timestamptz pd_consent_at
        timestamptz created_at
        timestamptz bot_blocked_at "null"
    }
    subgroups {
        int id PK
        int group_id FK
        int number "null"
    }
    subjects {
        int id PK
        text name
        text normalized_name UK
    }
    submissions {
        int id PK
        int homework_id FK
        int student_id FK
        int version
        text file_key
        text file_name
        bigint file_size
        timestamptz submitted_at
    }
    subtask_scores {
        int review_id PK,FK
        int subtask_id PK,FK
        decimal score
    }
    subtasks {
        int id PK
        int homework_id FK
        int order
        text title
        decimal max_score "null"
    }
    tags {
        int id PK
        int teacher_id FK
        text name
    }
    teachers {
        int id PK
        text login UK
        text password_hash
        text full_name
        bool must_change_password
        int added_by_id FK "null"
        timestamptz created_at
        timestamptz disabled_at "null"
        timestamptz sessions_valid_from
    }
    courses ||--o{ course_accesses : course_id
    courses ||--o{ enrollments : course_id
    courses ||--o{ homework_courses : courses_id
    courses ||--o{ invite_courses : courses_id
    groups ||--o{ subgroups : group_id
    homeworks ||--o{ homework_courses : homework_id
    homeworks ||--o{ homework_recipients : homework_id
    homeworks ||--o{ homework_targets : homework_id
    homeworks ||--o{ submissions : homework_id
    homeworks ||--o{ subtasks : homework_id
    invites |o--o{ enrollments : invite_id
    invites ||--o{ invite_courses : invite_id
    reviews ||--o{ subtask_scores : review_id
    students ||--o{ enrollments : student_id
    students ||--o{ homework_recipients : student_id
    students ||--o{ notifications : student_id
    students ||--o{ student_tags : student_id
    students ||--o{ submissions : student_id
    subgroups ||--o{ courses : subgroup_id
    subjects ||--o{ courses : subject_id
    subjects ||--o{ homeworks : subject_id
    subjects ||--o{ invites : subject_id
    submissions ||--o| reviews : submission_id
    subtasks ||--o{ subtask_scores : subtask_id
    tags ||--o{ student_tags : tag_id
    teachers |o--o{ enrollments : decided_by_id
    teachers |o--o{ reviews : sent_by_id
    teachers |o--o{ teachers : added_by_id
    teachers ||--o{ course_accesses : teacher_id
    teachers ||--o{ courses : admin_teacher_id
    teachers ||--o{ homeworks : author_id
    teachers ||--o{ invites : author_id
    teachers ||--o{ reviews : updated_by_id
    teachers ||--o{ tags : teacher_id
```
<!-- db-diagram:end -->
