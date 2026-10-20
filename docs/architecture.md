# Архитектура и API (2026-10-20)

Требования — [tz.md](tz.md) (актуальное ТЗ), исходные заметки — [requirements.md](requirements.md).

## Компоненты

```
 Преподаватель ──HTTPS──► web (Blazor WebAssembly, статика в nginx) ──/api──► api
                                                                            │
                                    api (ASP.NET Core, .NET 10) ──► PostgreSQL (Yandex Managed PG)
                                                                └─► Object Storage (S3): сдачи, PDF с пометками
                                         ▲
                                         │ X-Bot-Key (анкета, сдача, забор уведомлений)
 Студент ◄──► Telegram ◄──► бот (C#, .NET) — единственный, кто общается с Telegram; ходит ТОЛЬКО в API, своей БД нет
```

Монорепа:

| Путь | Что |
|---|---|
| `src/Fiit.Contracts` | DTO и enum-ы API — общие для api, web и будущего бота |
| `src/Fiit.Api` | бэкенд: домен, EF Core, контроллеры, outbox уведомлений, впечатывание пометок в PDF |
| `web/Fiit.Web` | тестовый сайт преподавателя на Blazor WebAssembly + эмулятор бота |
| `tests/Fiit.Api.Tests` | юнит-тесты правил и интеграционные тесты сценариев на Postgres + S3 (Testcontainers) |

Бота пока нет: его роль на время тестов играет страница «Бот (эмулятор)» на сайте — она ходит в те же `/api/bot/*`, что и настоящий бот.

## Решения

1. **Единственный владелец БД — бэкенд.** Схема через EF Core миграции (`Data/Migrations`), применяются при старте (`Db:MigrateOnStartup`).
2. **Бот на C#**, переиспользует `Fiit.Contracts`. Состояние диалога (шаг анкеты) держит бот; бэкенд отвечает «что можно этому студенту» и принимает готовую анкету/PDF. Коды ошибок для бота — в `ProblemDetails.code` (`ErrorCodes`).
3. **Уведомления — outbox.** В той же транзакции, что и событие (подтверждение заявки, публикация/изменение ДЗ, отправка проверки), создаётся строка `notifications` с готовым текстом. Бот забирает пачку (`claim`: `FOR UPDATE SKIP LOCKED` + аренда 60 с), отправляет и подтверждает `ack`, при ошибке — `fail` (повтор с экспоненциальной задержкой, после 8 попыток или при `permanent`/`botBlocked` — `Failed`; `botBlocked` ставит студенту значок «бот заблокирован»).
4. **Файлы.** Бот скачивает PDF из Telegram (≤ 20 МБ, проверка по `file_size` до скачивания) и отправляет multipart на бэкенд; бэкенд проверяет сигнатуру `%PDF-` и кладёт в приватный бакет. Сайт и бот получают файлы **через API** с проверкой прав (а не presigned-ссылками) — проще и не нужен CORS на бакете.
5. **Пометки на PDF.** Фронт рендерит PDF через pdf.js и рисует слой поверх (перо, маркер, текст). Пометки хранятся JSON-ом в координатах страницы PDF (пункты, начало — левый верхний угол). При «Отправить студенту» **бэкенд** впечатывает слой в копию PDF (PDFsharp 6.2.4 — последняя MIT-версия, 6.5+ под AGPL; шрифт DejaVu Sans с кириллицей вшит в сборку).
6. **Время** хранится в UTC, показывается в часовом поясе Екатеринбурга (UTC+5, без перехода на летнее).
7. **Сессии.** JWT живёт 14 дней. На каждом запросе проверяется, что препод активен и токен выпущен не раньше `teachers.sessions_valid_from` (сдвигается при отключении, смене и сбросе пароля). С временным паролем доступны только `/api/me` и `/api/auth/change-password`.
8. **«Есть неотправленные правки»** — по хешу содержимого черновика проверки, а не по времени: автосохранение без изменений не ломает статус.
9. **Опоздание** не хранится: `submitted_at > deadline` по текущему дедлайну — продление дедлайна снимает опоздание.

## API

Все ответы JSON (enum-ы строками), ошибки — ProblemDetails (`title` — текст для человека, `code` — для бота/фронта). Префикс `/api`.

### Бот (`X-Bot-Key`)
| Метод | Путь | Что делает |
|---|---|---|
| GET | `/bot/invites/{code}` | приглашение: предмет, преподаватель, подгруппы; `isOpen=false` — набор закрыт |
| GET | `/bot/recruiting` | открытые наборы без ссылки: «предмет — преподаватель» + подгруппы (С-1.1а) |
| GET | `/bot/students/{tgId}` | известен ли студент, ФИО, заявки по курсам (С-1.2, С-1.9, С-1.10) |
| POST | `/bot/applications` | анкета `{telegramUserId, telegramUsername, inviteCode?, courseId, firstName, lastName, pdConsent}` |
| GET | `/bot/students/{tgId}/homeworks` | «Мои задания»: активные и «Завершённые» |
| GET | `/bot/students/{tgId}/homeworks/{hwId}` | карточка ДЗ: условие, версии, результат последней проверки |
| POST | `/bot/students/{tgId}/homeworks/{hwId}/submissions` | multipart `file` (PDF) → новая версия |
| POST | `/bot/notifications/claim` | `{limit}` → пачка уведомлений с арендой |
| POST | `/bot/notifications/{id}/ack` | доставлено |
| POST | `/bot/notifications/{id}/fail` | `{error, permanent, botBlocked}` |
| GET | `/bot/notifications/{id}/file` | PDF с пометками для уведомления о проверке |

### Сайт (JWT преподавателя)
| Метод | Путь | Что делает |
|---|---|---|
| POST | `/auth/login` | `{login, password}` → `{token, mustChangePassword}` |
| POST | `/auth/change-password` | `{oldPassword, newPassword}` → новый токен, остальные сессии завершаются |
| GET | `/me` | текущий препод |
| GET/POST | `/teachers` | список; добавить → временный пароль (показывается один раз) |
| POST | `/teachers/{id}/reset-password` | новый временный пароль коллеге |
| POST | `/teachers/{id}/disable`, `/enable` | только тот, кто добавил |
| GET/POST | `/subjects?q=` | общий список предметов; POST возвращает существующий при совпадении без учёта регистра |
| GET | `/semesters/current` | текущий семестр по дате |
| GET/POST | `/courses?archived=` | мои курсы; POST `{subjectId, group, subgroup?, semester?}` |
| GET/DELETE | `/courses/{id}` | DELETE — только ведущий и только пустой курс |
| POST | `/courses/archive`, `/courses/unarchive` | `{courseIds[]}` |
| GET | `/courses/{id}/access` | кому выдан доступ |
| POST/DELETE | `/courses/{id}/access/{teacherId}` | выдать/забрать (только ведущий) |
| GET/POST | `/invites` | приглашения; POST `{subjectId, courseIds[], expiresAt?}` → ссылка `t.me/<бот>?start=<код>` |
| PATCH | `/invites/{id}` | `{expiresAt}` или `{close: true}` |
| GET | `/enrollments?courseIds=&status=&tagId=&q=` | студенты и заявки |
| POST | `/enrollments/approve`, `/enrollments/reject` | `{ids[]}` / `{ids[], reason?}` |
| DELETE | `/enrollments/{id}` | удалить подтверждённого студента из курса |
| GET/POST, PUT/DELETE `{id}` | `/tags` | личные метки |
| POST | `/tags/{id}/students`, `/tags/{id}/students/remove` | `{studentIds[]}` |
| GET | `/homeworks?subjectId=&semesterYear=&semesterTerm=` | ДЗ своих курсов |
| GET/POST, PUT/DELETE `{id}` | `/homeworks` | `{subjectId, semester, title, link, deadline, subtasks[], courseIds[], targets[], notifyStudents}` |
| POST | `/homeworks/{id}/publish` | публикация + уведомления адресатам |
| POST | `/homeworks/preview-recipients` | `{courseIds[], targets[]}` → число адресатов |
| GET | `/gradebook?subjectId=&semesterYear=&semesterTerm=&courseIds=&group=&tagId=` | сводная таблица |
| GET | `/gradebook/export.xlsx?...` | экспорт в Excel |
| GET | `/homeworks/{hwId}/students/{studentId}/review` | экран проверки: версии, проверки, подзадачи |
| GET | `/submissions/{id}/file`, `/submissions/{id}/reviewed-file` | оригинал / отправленный PDF с пометками |
| PUT | `/submissions/{id}/review` | черновик `{annotations, comment, subtaskScores[], totalOverride?}` (только последняя версия) |
| POST | `/submissions/{id}/review/send` | впечатать пометки и отправить студенту |

## Права
Препод видит курс, если он ведущий или ему выдан доступ; ДЗ — если имеет доступ хотя бы к одному из курсов ДЗ; студента — если тот в его курсе. Проверка — на каждом эндпоинте (`Services/Access`), не на фронте. Архивный курс — только чтение: изменяющие запросы по нему → 409 `course_archived`.

## Хостинг
Yandex Cloud: одна VM с docker compose (api + web/nginx с HTTPS + бот) + Managed PostgreSQL + Object Storage. Локально `docker compose up` поднимает Postgres, RustFS (S3-совместимое хранилище), api и web.

## Открытые технические вопросы
- Повёрнутые страницы PDF (`/Rotate`) и страницы с CropBox ≠ MediaBox: пометки могут сместиться при впечатывании.
