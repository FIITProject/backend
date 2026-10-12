# Архитектура и API (черновик, 2026-10-05)

Требования — см. [requirements.md](requirements.md).

## Компоненты

```
 Преподаватель ──HTTPS──► Frontend (React, статика в Object Storage / nginx)
                               │ JWT
 Студент (форма) ──HTTPS──────►│
                               ▼
                         Backend (ASP.NET Core, .NET 10) ──► PostgreSQL (Yandex Managed PG)
                               ▲                         └──► Object Storage (S3): сдачи, PDF с пометками
                               │ X-Bot-Key (команды студента, забор задач на уведомления)
                               │
 Студент ◄──► Telegram ◄──► Бот (Python, aiogram 3) — единственный, кто общается с Telegram;
                            ходит ТОЛЬКО в API бэкенда, своей БД нет
```

Решения:
1. **Единственный владелец БД — бэкенд.** Схема через EF Core миграции. Бот к БД не подключается.
2. **Входящее (студент → бот)** обрабатывает бот и вызывает `/bot/*` эндпоинты бэкенда с сервисным ключом `X-Bot-Key`.
3. **Уведомления.** С Telegram общается только бот. Бэкенд при событии (публикация ДЗ, отправка проверки) в той же транзакции создаёт задачи в таблице `Notification` (outbox). Бот в цикле забирает задачи (`claim` с арендой на N секунд, чтобы при падении бота задача вернулась в очередь), отправляет в Telegram и подтверждает (`ack`) или сообщает об ошибке (`fail` → ретрай с backoff, после K попыток — `failed`). Если студент заблокировал бота — `fail` с признаком «недоставляемо», без ретраев.
4. **Файлы.** Бот скачивает PDF из Telegram (≤ 20 МБ, проверка до скачивания по `file_size`) и отправляет multipart на бэкенд; бэкенд кладёт в S3. Сайт открывает PDF по presigned GET-ссылке.
5. **Пометки на PDF.** Фронт рендерит PDF через pdf.js, рисование — слой поверх (Konva/Fabric). Пометки хранятся как JSON в координатах страницы PDF (не пикселях). При «Отправить студенту» фронт тем же кодом «впечатывает» слой через pdf-lib и загружает итоговый PDF на бэкенд → S3 → бэкенд шлёт его студенту. Один рендерер = что видит препод, то получит студент.
6. **Время** везде хранится в UTC, отображается в часовом поясе препода.
7. **Сессии.** JWT живёт 14 дней. Чтобы отключение преподавателя и смена/сброс пароля сразу завершали сессии, на каждом запросе проверяется, что препод активен и токен выпущен не раньше `Teacher.sessions_valid_from` (поле сдвигается при отключении и смене пароля).

## API

Все ответы JSON, ошибки — ProblemDetails. Префикс `/api`.

### Публичная форма (без авторизации)
| Метод | Путь | Что делает |
|---|---|---|
| GET | `/api/public/forms/{token}` | предмет + список подгрупп для выпадающего списка; 410 если истекла |
| POST | `/api/public/forms/{token}/applications` | `{firstName, lastName, courseId, tgHint, pdConsent:true}` → `{botLink: "https://t.me/<bot>?start=<bindCode>"}` |

### Бот (`X-Bot-Key`)
| Метод | Путь | Что делает |
|---|---|---|
| POST | `/api/bot/bind` | `{bindCode, telegramUserId, username}` → привязка/склейка студента |
| GET | `/api/bot/students/{tgId}/homeworks` | доступные ДЗ со статусами |
| GET | `/api/bot/students/{tgId}/homeworks/{hwId}` | ДЗ + история версий + результат проверки |
| POST | `/api/bot/students/{tgId}/homeworks/{hwId}/submissions` | multipart PDF → новая версия |
| POST | `/api/bot/notifications/claim` | `{limit}` → пачка задач на отправку (с арендой); для PDF проверки — presigned URL |
| POST | `/api/bot/notifications/{id}/ack` | доставлено |
| POST | `/api/bot/notifications/{id}/fail` | `{error, permanent}` → ретрай или `failed` |

### Сайт (JWT преподавателя)
| Метод | Путь | Что делает |
|---|---|---|
| POST | `/api/auth/login` | логин → токен |
| GET | `/api/me` | текущий препод |
| GET/POST | `/api/subjects` | предметы |
| GET/POST | `/api/courses?archived=false` | мои курсы; POST `{subjectId, groupName, subgroupNumber, semesterYear, semesterTerm}` создаёт группу/подгруппу при необходимости, создатель = админ |
| POST | `/api/courses/archive`, `/api/courses/unarchive` | `{courseIds[]}` — отправить в архив / вернуть |
| GET | `/api/teachers` | список преподов (для выдачи доступа) |
| POST/DELETE | `/api/courses/{id}/access/{teacherId}` | выдать/забрать доступ (только админ) |
| GET/POST | `/api/forms` | формы; POST `{subjectId, courseIds[], expiresAt}` |
| PATCH | `/api/forms/{id}` | изменить срок / закрыть |
| GET | `/api/enrollments?courseId=&status=` | список заявок/студентов (с флагом «бот не привязан») |
| POST | `/api/enrollments/approve` | `{ids[]}` или `{courseId, all:true}` |
| DELETE | `/api/enrollments/{id}` | удалить |
| GET/POST/DELETE | `/api/tags`, `/api/students/{id}/tags/{tagId}` | метки |
| GET/POST/PUT/DELETE | `/api/homeworks` | ДЗ (+ подзадачи, правила адресатов); редактирование адресатов — только до публикации |
| POST | `/api/homeworks/{id}/publish` | публикация + рассылка |
| GET | `/api/gradebook?subjectId=&semester=2026-autumn&courseIds=&tagIds=` | сводная таблица студенты×ДЗ |
| GET | `/api/gradebook/export.xlsx?...` | экспорт в Excel |
| GET | `/api/homeworks/{hwId}/students/{studentId}/submissions` | версии сдач + проверки |
| GET | `/api/submissions/{id}/file` | presigned URL на PDF |
| PUT | `/api/submissions/{id}/review` | `{annotations, comment, subtaskScores[], totalOverride?}` — черновик, сохраняется без отправки |
| POST | `/api/submissions/{id}/review/send` | multipart: PDF с пометками → S3 → задача на уведомление студенту |

## Права
Препод видит студента/ДЗ, если у него есть админство или доступ к соответствующему `Course`. Проверка — на каждом эндпоинте (политика авторизации по `courseId`), не на фронте. Архивный курс — только чтение: любые изменяющие запросы по нему отвечают 409.

## Хостинг
Yandex Cloud: одна VM с docker compose (backend + bot + nginx с HTTPS) + Managed PostgreSQL + Object Storage. Фронт — статика через тот же nginx.

## Открытые технические вопросы
- Шрифты с кириллицей для текстовых пометок при впечатывании в PDF (pdf-lib + встроенный шрифт).
