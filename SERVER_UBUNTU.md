# FluxChat Relay на Ubuntu VPS

`root@IP_ТВОЕГО_VPS` означает: `root` - имя пользователя, `@` оставляешь, `IP_ТВОЕГО_VPS` заменяешь на настоящий IP сервера.

## 1. Сборка на Windows ПК

```powershell
.\dist-server-linux.bat
```

Появятся два Linux-файла:

```text
dist-server-linux\FluxChat.Server
dist-server-linux\fluxus
```

## 2. Загрузка на VPS через PowerShell/SSH

Эти команды выполняются на Windows ПК:

```powershell
scp .\dist-server-linux\FluxChat.Server root@IP_ТВОЕГО_VPS:/root/FluxChat.Server
scp .\dist-server-linux\fluxus root@IP_ТВОЕГО_VPS:/root/fluxus
ssh root@IP_ТВОЕГО_VPS
```

## 3. Через Termius или VNC

В Termius создай Host:

```text
Address: IP_ТВОЕГО_VPS
Username: root
Port: 22
```

Через SFTP загрузи:

```text
FluxChat.Server -> /root/FluxChat.Server
fluxus -> /root/fluxus
```

VNC-консоль обычно не умеет загрузить файл с твоего ПК, поэтому сначала загрузи файлы через `scp`, Termius SFTP или файловый менеджер хостинга.

## 4. Быстрый ручной запуск

```bash
chmod +x /root/FluxChat.Server /root/fluxus
sudo ufw allow 42800/tcp
/root/FluxChat.Server
```

Во второй SSH-сессии:

```bash
/root/fluxus
```

В меню выбери `1. Создать инвайт-код`, отправь код другу. Друг вводит его в клиенте в поле `Invite / token`.

## 5. Установка как постоянный сервис

```bash
sudo mkdir -p /opt/fluxchat /var/lib/fluxchat
sudo mv /root/FluxChat.Server /opt/fluxchat/FluxChat.Server
sudo mv /root/fluxus /usr/local/bin/fluxus
sudo chmod +x /opt/fluxchat/FluxChat.Server /usr/local/bin/fluxus
sudo tee /etc/systemd/system/fluxchat.service >/dev/null <<'EOF'
[Unit]
Description=FluxChat Relay Server
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=/opt/fluxchat/FluxChat.Server
Restart=always
RestartSec=3
User=root

[Install]
WantedBy=multi-user.target
EOF
sudo systemctl daemon-reload
sudo systemctl enable --now fluxchat
sudo systemctl status fluxchat
```

После установки админ-панель запускается так:

```bash
fluxus
```

## 6. Полное удаление с VPS

```bash
sudo systemctl disable --now fluxchat
sudo rm -f /etc/systemd/system/fluxchat.service
sudo rm -rf /opt/fluxchat
sudo rm -f /usr/local/bin/fluxus
sudo rm -rf /var/lib/fluxchat
sudo rm -f /root/FluxChat.Server /root/fluxus
sudo systemctl daemon-reload
sudo ufw delete allow 42800/tcp
```

## Важно

Пользователей больше не нужно пускать по общему ключу. Создавай одноразовые инвайты через `fluxus`. После первого успешного входа клиент сам сохранит постоянный token.
