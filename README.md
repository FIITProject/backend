# ФИИТ · сдача и проверка ДЗ

Сервис сдачи и проверки домашних заданий: сайт преподавателя + Telegram-бот для студентов. Обзор проекта — [OVERVIEW.md](OVERVIEW.md), ТЗ — [docs/tz.md](docs/tz.md), решения и API — [docs/architecture.md](docs/architecture.md).

| Путь | Что |
|---|---|
| `src/Fiit.Api` | бэкенд (ASP.NET Core, .NET 10, EF Core + PostgreSQL, S3, ClamAV) |
| `src/Fiit.Contracts` | DTO API — общие для бэкенда, сайта и бота |
| `src/Fiit.Messaging` | очереди уведомлений в RabbitMQ — общие для бэкенда и бота |
| `src/Fiit.Bot.Core` | диалог бота и клиент `/api/bot/*` (без Telegram) |
| `src/Fiit.Bot` | Telegram-бот (Telegram.Bot, long polling) |
| `web/app` | сайт преподавателя на React по дизайну fiit-design |
| `web/Fiit.Web` | старый тестовый сайт (Blazor) — только ради эмулятора бота, порт 8081 |
| `tests/Fiit.Api.Tests` | юнит- и интеграционные тесты API (Testcontainers: Postgres + S3 + RabbitMQ) |
| `tests/Fiit.Bot.Tests` | тесты диалога и доставки уведомлений |

## Запуск всего стека

```bash
docker compose up -d --build
# первый преподаватель (дальше преподаватели добавляются на сайте)
docker compose run --rm migrate add-teacher admin admin-password Иванов Иван Иванович
```

Сайт — http://localhost:8080 (вход `admin` / `admin-password`), эмулятор бота — http://localhost:8081/bot, API напрямую — http://localhost:5080, RabbitMQ — http://localhost:15672 (`fiit` / `fiit`).

Что поднимается: Postgres, RustFS (S3), RabbitMQ, ClamAV, `migrate` (применяет миграции и выходит), api, web. ClamAV при первом запуске минуты 1–3 качает базы — до этого сдача PDF отвечает «попробуйте позже».

Настоящий Telegram-бот — с токеном от @BotFather:

```bash
TELEGRAM_TOKEN=123:abc BOT_USERNAME=имя_бота docker compose --profile bot up -d --build
```

`BOT_USERNAME` нужен API для ссылок-приглашений `t.me/<бот>?start=<код>`. Без токена бот не запускается, а уведомления копятся в очереди `fiit.bot.notifications` — бот разошлёт их, когда стартует.

Если Telegram недоступен напрямую, добавьте в `.env` прокси: `TELEGRAM_PROXY=socks5://логин:пароль@host:port` (или `http://...`). Через неё идут только запросы к Telegram.

Прод — Dokploy на ВМ Yandex Cloud, автодеплой из `main`: [docs/deploy.md](docs/deploy.md).

Сценарий для проверки:
1. «Курсы» → создать курс (например, «Тервер», группа `ФИИТ-301-1`).
2. «Приглашения» → создать ссылку, скопировать код.
3. Эмулятор бота (http://localhost:8081/bot) → «+ Новый студент» → ввести код → `/start` → пройти анкету (или в Telegram — открыть ссылку-приглашение).
4. «Студенты» → подтвердить заявку; в эмуляторе придёт уведомление.
5. «ДЗ» → новое ДЗ → «Опубликовать…»; в эмуляторе — «Сдать» → 📎 PDF.
6. «Таблица» → клик по ячейке → пометки на PDF, баллы, комментарий → «Отправить студенту»; в эмуляторе придёт оценка и PDF с пометками.

## Разработка

```bash
docker compose up -d db s3 rabbitmq        # Postgres, S3 (RustFS), RabbitMQ
dotnet run --project src/Fiit.Api          # http://localhost:5080, в Development миграции применяются при старте
cd web/app && npm install && npm run dev  # сайт, http://localhost:5173 (прокси /api на :5080)
Telegram__Token=... dotnet run --project src/Fiit.Bot
dotnet test                                # нужен запущенный Docker
```

Миграции: `dotnet ef migrations add <Name> --project src/Fiit.Api --output-dir Data/Migrations`. В деплое они применяются отдельным шагом `dotnet Fiit.Api.dll migrate` до запуска новой версии API; API с неприменёнными миграциями не стартует.
