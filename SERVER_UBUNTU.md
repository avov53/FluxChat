# FluxChat на Ubuntu VPS

## Установка или обновление

Подключись к VPS под `root` и выполни одну команду:

```bash
bash <(curl -Ls https://raw.githubusercontent.com/avov53/fluxusui/main/install.sh)
```

Установщик сохраняет `/var/lib/fluxchat`, инвайты, пользователей и историю
relay. После установки он проверит Account Server. Если он ещё не настроен,
будет предложен мастер настройки.

## Мастер аккаунтов

Мастер настраивает PostgreSQL, защищённый Account API, nginx и сертификат
Let's Encrypt. SMTP больше не нужен.

```bash
fluxus setup accounts
```

Во время настройки выбери:

1. Автоматический адрес `VPS_IP.sslip.io` либо собственный домен.
2. Email для Let's Encrypt.

FluxChat выбирает свободный HTTPS-порт из `8443-8499`; порт `443` остаётся
нетронутым для 3x-ui/VLESS. Для выпуска сертификата порт `80/tcp` должен быть
доступен снаружи.

Проверка и исправление:

```bash
fluxus setup accounts status
fluxus setup accounts repair
```

Отключение Account API, без удаления PostgreSQL-данных:

```bash
fluxus setup accounts disable
```

## Инвайты и подключение клиента

Открой серверную панель:

```bash
fluxus
```

Создай одноразовый инвайт и передай его пользователю. В FluxChat пользователь
указывает только:

```text
VPS server: YOUR_VPS_IP:42800
Invite code: code from fluxus
```

HTTPS-адрес Account API передаётся клиенту автоматически после подключения к
relay, поэтому пользователю не нужно вводить URL, домен или SSL-порт.

## Полезные команды

```bash
systemctl status fluxchat
systemctl restart fluxchat
journalctl -u fluxchat -f
fluxus status
```
