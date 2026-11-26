# Деплой

Сервер — ВМ в Yandex Cloud (Ubuntu 24.04), на ней [Dokploy](https://dokploy.com). Dokploy забирает `main` из GitHub, собирает образы на сервере и поднимает `docker-compose.prod.yml`. Каждый пуш в `main` раскатывается сам через вебхук.

## Как устроено

- **Сервисы.** `docker-compose.prod.yml` поднимает db, s3 (RustFS), rabbitmq, clamav, migrate, api, bot, web.
- **Что видно снаружи.** Только сайт (`web`). Он открывается по домену через Traefik Dokploy, а без домена — на порту `WEB_PORT` (8080). БД, S3, RabbitMQ и API наружу не открыты.
- **Миграции.** Это отдельный шаг: сервис `migrate` применяет их и выходит. API стартует только после успеха. Если миграция упала, старый API продолжает работать, а деплой в Dokploy красный.
- **Данные** лежат в именованных томах (`db-data`, `s3-data`, `rabbitmq-data`, `clamav-data`) и переживают передеплой.
- **Telegram.** С ВМ в Yandex Cloud api.telegram.org напрямую недоступен, поэтому бот ходит через прокси из `TELEGRAM_PROXY`.

## Переменные окружения (Dokploy → Environment)

| Переменная | Что |
|---|---|
| `DB_PASSWORD` | пароль Postgres |
| `RABBITMQ_PASSWORD` | пароль RabbitMQ |
| `S3_SECRET_KEY` | секрет RustFS |
| `JWT_KEY` | ключ подписи JWT, не короче 32 символов. Если его сменить, все разлогинятся |
| `BOT_KEY` | общий ключ api и бота (`X-Bot-Key`) |
| `BOT_USERNAME` | имя бота без @, для ссылок-приглашений |
| `TELEGRAM_TOKEN` | токен от @BotFather |
| `TELEGRAM_PROXY` | `socks5://логин:пароль@host:port`; пусто — напрямую |
| `WEB_PORT` | порт сайта на хосте, по умолчанию 8080 |

Секреты генерируются так: `openssl rand -hex 32`. Пароли БД, RabbitMQ и S3 задаются при первом создании томов, после этого менять их нужно и внутри сервиса.

## Первичная настройка

1. Создать ВМ, на ней: swap 4 ГБ (`/swapfile`), затем `curl -sSL https://dokploy.com/install.sh | sudo sh`.
2. В группе безопасности открыть 80, 443 (сайт), 3000 (панель Dokploy, лучше только со своего IP) и 8080, если сайт открывается без домена.
3. Открыть `http://<ip>:3000`, создать администратора Dokploy.
4. Project → Create Service → Compose:
   - Provider: Git, репозиторий `https://github.com/FIITProject/backend.git`, ветка `main`;
   - Compose Path: `./docker-compose.prod.yml`.
5. Environment — переменные из таблицы выше, затем Deploy.
6. Deployments → скопировать Webhook URL. В GitHub: Settings → Webhooks → Add webhook, вставить URL, Content type `application/json`, событие Push. С этого момента пуш в `main` деплоит сам.
7. Первый преподаватель (по SSH на сервере):
   ```bash
   cd /etc/dokploy/compose/fiit-hw-oqmczw/code
   sudo docker compose -p fiit-hw-oqmczw -f docker-compose.prod.yml --env-file .env run --rm migrate add-teacher <логин> <пароль> <Фамилия> <Имя> [Отчество]
   ```
8. Домен: Domains → сервис `web`, порт 80, HTTPS через Let's Encrypt.

## Сервер

`ssh admin1@89.169.145.56`. 2 vCPU, 4 ГБ RAM + 4 ГБ swap, 48 ГБ диск. Сборка образов .NET и сайта на нём занимает несколько минут.

Сейчас развёрнуто: проект Dokploy `fiit`, compose `fiit-hw` (на сервере `fiit-hw-oqmczw`), сайт — http://89.169.145.56:8080, панель — http://89.169.145.56:3000. Вебхук автодеплоя добавлен в репозиторий.

**Один токен — один бот.** Telegram отдаёт обновления только одному процессу: если с тем же токеном запущен бот локально, оба получают 409 Conflict. Для локальной разработки заведите отдельного бота у @BotFather.
