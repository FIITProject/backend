# ФИИТ · сдача и проверка ДЗ

Сервис сдачи и проверки домашних заданий: сайт преподавателя + Telegram-бот для студентов. ТЗ — [docs/tz.md](docs/tz.md), архитектура и API — [docs/architecture.md](docs/architecture.md).

| Путь | Что |
|---|---|
| `src/Fiit.Api` | бэкенд (ASP.NET Core, .NET 10, EF Core + PostgreSQL, S3) |
| `src/Fiit.Contracts` | DTO API — общие для бэкенда, сайта и бота |
| `web/Fiit.Web` | тестовый сайт преподавателя (Blazor WebAssembly) + эмулятор бота |
| `tests/Fiit.Api.Tests` | юнит- и интеграционные тесты (Testcontainers: Postgres + S3) |

Telegram-бота (C#) пока нет — его роль играет страница «Бот (эмулятор)» на сайте, она ходит в те же `/api/bot/*`.

## Запуск всего стека

```bash
docker compose up -d --build
# первый преподаватель (дальше преподаватели добавляются на сайте)
docker compose exec api dotnet Fiit.Api.dll add-teacher admin admin-password Иванов Иван Иванович
```

Сайт — http://localhost:8080 (вход `admin` / `admin-password`), API напрямую — http://localhost:5080.

Сценарий для проверки:
1. «Курсы» → создать курс (например, «Тервер», группа `ФИИТ-301-1`).
2. «Приглашения» → создать ссылку, скопировать код.
3. «Бот (эмулятор)» → «+ Новый студент» → ввести код → `/start` → пройти анкету.
4. «Студенты» → подтвердить заявку; в эмуляторе придёт уведомление.
5. «ДЗ» → новое ДЗ → «Опубликовать…»; в эмуляторе — «Сдать» → 📎 PDF.
6. «Таблица» → клик по ячейке → пометки на PDF, баллы, комментарий → «Отправить студенту»; в эмуляторе придёт оценка и PDF с пометками.

## Разработка

```bash
docker compose up -d db s3                 # Postgres и S3 (RustFS)
dotnet run --project src/Fiit.Api          # http://localhost:5080
dotnet run --project web/Fiit.Web          # http://localhost:5173
dotnet test                                # нужен запущенный Docker
```

Миграции: `dotnet ef migrations add <Name> --project src/Fiit.Api --output-dir Data/Migrations`.
