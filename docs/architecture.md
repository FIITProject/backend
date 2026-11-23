# Архитектура и API (2026-11-19)

Требования — [tz.md](tz.md) (актуальное ТЗ), исходные заметки — [requirements.md](requirements.md).

## Компоненты

```
 Преподаватель ──HTTPS──► web (React SPA, статика в nginx) ──/api──► api (ASP.NET Core, .NET 10)
                                                                       ├─► PostgreSQL (Yandex Managed PG) — единственный владелец БД
                                                                       ├─► Object Storage (S3, приватный бакет): сдачи, PDF с пометками
                                                                       ├─► ClamAV (clamd): антивирус для сдаваемых PDF
                                                                       └─► outbox → RabbitMQ (очередь fiit.bot.notifications)
                                                                                         │
 Студент ◄──► Telegram ◄──► бот (C#, .NET) ◄── уведомления из очереди ───────────────────┘
                              └── X-Bot-Key: анкета, задания, сдача, статус доставки ──► api
```

Бот — отдельный процесс, единственный, кто общается с Telegram. Своей БД у него нет: данные — только через `/api/bot/*`, уведомления — из RabbitMQ.

Монорепа:

| Путь | Что |
|---|---|
| `src/Fiit.Contracts` | DTO и enum-ы API — общие для api, бота и сайта; для React — TypeScript-типы по тем же контрактам |
| `src/Fiit.Api` | бэкенд: домен, EF Core, контроллеры, outbox уведомлений и его публикатор, проверка PDF, впечатывание пометок |
| `src/Fiit.Messaging` | топология RabbitMQ (обменник, очереди, повторы) и формат сообщения — общие для api и бота |
| `src/Fiit.Bot.Core` | диалог бота (сценарии раздела 4 ТЗ) и клиент `/api/bot/*`; не знает про Telegram |
| `src/Fiit.Bot` | Telegram-бот: long polling, отправка сообщений, потребитель очереди уведомлений |
| `web/app` | сайт преподавателя на React (Vite + TypeScript), вёрстка — дизайн FIITProject/frontend (`fiit-design`) один в один |
| `web/Fiit.Web` | старый тестовый сайт на Blazor — остался ради эмулятора бота (`web-blazor`, порт 8081), будет удалён |
| `tests/Fiit.Api.Tests` | юнит-тесты правил и проверки PDF, интеграционные тесты сценариев на Postgres + S3 + RabbitMQ (Testcontainers) |
| `tests/Fiit.Bot.Tests` | сценарии диалога, доставка уведомлений и повторы |

Эмулятор бота на сайте использует тот же `Fiit.Bot.Core.Dialog`, что и настоящий бот, — сценарии проверяются без Telegram. Уведомления эмулятор читает из outbox (`/api/bot/students/{tgId}/notifications`), а не из RabbitMQ.

**Фронт — React** (`web/app`: React 19 + TypeScript, Vite, react-router), раздаётся nginx как статика, в API ходит по тому же origin через прокси `/api` — без CORS. Типы — TypeScript-зеркало `Fiit.Contracts` (`src/api/types.ts`).

Вёрстка перенесена из дизайна (FIITProject/frontend, `fiit-design`) без изменений: CSS дизайна лежит в `src/styles` как есть, разметка страниц — те же классы. CSS страниц дизайна пересекаются, поэтому стиль страницы подключается только пока она открыта (`PageStyle`, импорт `?inline`). «Курс» в интерфейсе — предмет в семестре со всеми подгруппами (маршрут `/c/:subjectId/:semester`), в API это несколько `CourseDto`. Код задания (ДЗ-3, ДР-1, Г-2) — начало названия: тип выбирается переключателем, номер присваивается по уже существующим заданиям курса; набор типов хранится в браузере преподавателя. Настройки колокольчика (заявки, работы на проверку, пересдачи, просрочки) — тоже в браузере.

## Решения

1. **Единственный владелец БД — бэкенд. Миграции — отдельный шаг деплоя.** Схема через EF Core миграции (`Data/Migrations`). Применяет их команда того же образа `dotnet Fiit.Api.dll migrate` — до запуска новой версии API (в compose — сервис `migrate`, api стартует после его успешного завершения; в проде — job/шаг пайплайна перед раскаткой). API со схемой старее своего кода не стартует. Миграцию можно запускать под пользователем БД с правами на схему, а API — под пользователем только с правами на данные. Правило для миграций: схема после миграции должна работать и со старой версией кода, пока та ещё запущена (удаление колонки — следующим релизом). `Db:MigrateOnStartup` — только для тестов и `dotnet run`.
2. **Бот на C#**, переиспользует `Fiit.Contracts`. Состояние диалога (шаг анкеты) держит бот в памяти: после перезапуска незаконченная анкета начинается заново, всё остальное хранит API. Long polling Telegram допускает один экземпляр бота. Бэкенд отвечает «что можно этому студенту» и принимает готовую анкету/PDF. Коды ошибок для бота — в `ProblemDetails.code` (`ErrorCodes`), текст ошибки бот показывает студенту как есть.
3. **Уведомления — transactional outbox + RabbitMQ.** В той же транзакции, что и событие (подтверждение заявки, публикация/изменение ДЗ, отправка проверки), создаётся строка `notifications` с готовым текстом — так уведомление не теряется и не появляется без события. Фоновый публикатор в API (`NotificationOutboxPublisher`) берёт пачку `Pending` (`FOR UPDATE SKIP LOCKED`), публикует в обменник `fiit.notifications` с подтверждением брокера и помечает `Published`. Бот читает очередь `fiit.bot.notifications`, отправляет в Telegram и сообщает API `delivered` / `failed`.
   - Доставка «хотя бы один раз»: перед отправкой бот проверяет статус в API и пропускает уже доставленное.
   - Временная ошибка (сеть, 5xx, 429) — копия уходит в очередь повтора с TTL (10 с … 2 ч, около 4 часов суммарно) и потом возвращается в основную очередь.
   - Постоянная ошибка или попытки кончились — в `fiit.bot.notifications.dead` для ручного разбора, API получает `failed`.
   - Студент заблокировал бота (403) — `failed` с `botBlocked`, у преподавателя значок «бот заблокирован».
   - Топологию объявляют и API, и бот, поэтому порядок запуска не важен; публикация с `mandatory`, так что неразосланное сообщение не теряется молча.
4. **Файлы.** Бот скачивает PDF из Telegram (≤ 20 МБ, размер и тип проверяются по метаданным до скачивания) и отправляет multipart на бэкенд. Бэкенд кладёт файл в приватный бакет только после проверки (`SubmissionFileGuard`):
   - файл должен открываться PDFsharp в режиме изменения — тем же, которым потом впечатываются пометки; без пароля; до 300 страниц;
   - без активного содержимого (`PdfSafety`): JavaScript, запуск программ, отправка форм, импорт данных, вложенные файлы, RichMedia, XFA. Проверяются все объекты документа, включая сжатые потоки объектов; имена с `#`-экранированием (`/J#61vaScript`) декодирует парсер. Эвристика как у pdfid: в сканах и выгрузках из Word/LaTeX такого не бывает, а преподаватель может открыть оригинал в Acrobat, где скрипты выполняются;
   - антивирус ClamAV по TCP (`INSTREAM`). Если clamd недоступен, файл не принимается (503 `scan_unavailable`, «попробуйте позже») — fail closed;
   - отказ — 400 с кодом `not_pdf`, `pdf_encrypted`, `pdf_too_many_pages`, `pdf_unsafe` или `pdf_infected`; текст бот показывает студенту.

   Сайт и бот получают файлы **через API** с проверкой прав (а не presigned-ссылками) — проще и не нужен CORS на бакете. Все ответы API — с `X-Content-Type-Options: nosniff`.
5. **Пометки на PDF.** Фронт рендерит PDF через pdf.js (`pdfjs-dist`) и рисует слой поверх на canvas (перо, маркер, текст, ластик). Пометки хранятся JSON-ом в координатах страницы PDF (пункты, начало — левый верхний угол). При «Отправить студенту» **бэкенд** впечатывает слой в копию PDF (PDFsharp 6.2.4 — последняя MIT-версия, 6.5+ под AGPL; шрифт DejaVu Sans с кириллицей вшит в сборку).
6. **Время** хранится в UTC, показывается в часовом поясе Екатеринбурга (UTC+5, без перехода на летнее).
7. **Сессии.** JWT живёт 14 дней. На каждом запросе проверяется, что препод активен и токен выпущен не раньше `teachers.sessions_valid_from` (сдвигается при отключении, смене и сбросе пароля). С временным паролем доступны только `/api/me` и `/api/auth/change-password`.
8. **«Есть неотправленные правки»** — по хешу содержимого черновика проверки, а не по времени: автосохранение без изменений не ломает статус.
10. **Проверяющий не закреплён.** Черновик проверки общий для преподавателей курса: `updated_by_id` — кто последним правил, `sent_by_id` — кто отправил (его подпись видит студент). Сохранение передаёт `baseUpdatedAt`; если черновик тем временем сохранил коллега — 409 `review_changed`, чтобы не затереть его правки.
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
| POST | `/bot/students/{tgId}/homeworks/{hwId}/submissions` | multipart `file` (PDF) → проверка файла → новая версия |
| GET | `/bot/notifications/{id}` | статус уведомления (`Pending` / `Published` / `Delivered` / `Failed`) — отсев повторной доставки |
| POST | `/bot/notifications/{id}/delivered` | отправлено в Telegram (повторный вызов ничего не меняет) |
| POST | `/bot/notifications/{id}/failed` | `{error, botBlocked}` — доставить не удалось |
| GET | `/bot/notifications/{id}/file` | PDF с пометками для уведомления о проверке |
| GET | `/bot/students/{tgId}/notifications?afterId=` | уведомления студента по порядку — для эмулятора бота |

Сами уведомления бот получает из RabbitMQ: обменник `fiit.notifications` (fanout) → очередь `fiit.bot.notifications`, тело — `BotNotificationDto` в JSON, `MessageId` — id уведомления, заголовок `x-attempt` — номер попытки.

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
| POST | `/homeworks/{id}/publish?notify=` | публикация; `notify=false` — без рассылки в бот (ДЗ просто появится в «Моих заданиях») |
| POST | `/homeworks/preview-recipients` | `{courseIds[], targets[]}` → число адресатов |
| GET | `/gradebook?subjectId=&semesterYear=&semesterTerm=&courseIds=&group=&tagId=&homeworkId=` | сводная таблица (`homeworkId` — один столбец, для страницы задания); в ячейке — статус, оценка, версии, кто проверил, когда сдано |
| GET | `/gradebook/export.xlsx?...` | экспорт в Excel с теми же фильтрами |
| GET | `/homeworks/{hwId}/students/{studentId}/review` | экран проверки: версии, проверки, подзадачи |
| GET | `/submissions/{id}/file`, `/submissions/{id}/reviewed-file` | оригинал / отправленный PDF с пометками |
| PUT | `/submissions/{id}/review` | черновик `{annotations, comment, subtaskScores[], totalOverride?, baseUpdatedAt?}` (только последняя версия) |
| POST | `/submissions/{id}/review/send` | впечатать пометки и отправить студенту |

## Права
Препод видит курс, если он ведущий или ему выдан доступ; ДЗ — если имеет доступ хотя бы к одному из курсов ДЗ; студента — если тот в его курсе. Проверка — на каждом эндпоинте (`Services/Access`), не на фронте. Архивный курс — только чтение: изменяющие запросы по нему → 409 `course_archived`.

## Хостинг
Yandex Cloud: одна VM с docker compose (migrate → api, web/nginx с HTTPS, бот, RabbitMQ, ClamAV) + Managed PostgreSQL + Object Storage. Порядок деплоя: собрать образы → `migrate` → api → бот. Локально `docker compose up` поднимает Postgres, RustFS (S3-совместимое хранилище), RabbitMQ, ClamAV, migrate, api и web; бот — с профилем `bot` и токеном.

## Открытые технические вопросы
- ClamAV при первом запуске качает базы сигнатур (1–3 минуты, ~1 ГБ памяти); пока clamd не готов, сдача отвечает «попробуйте позже». Базы обновляет сам контейнер (freshclam).
- Состояние диалога бота в памяти: незаконченная анкета теряется при перезапуске бота. Если станет мешать — хранить в Redis или в API.
- Повёрнутые страницы PDF (`/Rotate`) и страницы с CropBox ≠ MediaBox: пометки могут сместиться при впечатывании.
