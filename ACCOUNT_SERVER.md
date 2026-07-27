# FluxChat Account Server

## Recommended setup: Fluxus wizard

For Ubuntu/Debian VPS servers, use the latest `fluxusui` installer and accept
the account setup prompt. It installs and configures PostgreSQL, nginx, HTTPS,
the protected loopback Account API without manual edits to systemd or
nginx files.

```bash
bash <(curl -Ls https://raw.githubusercontent.com/avov53/fluxusui/main/install.sh)
```

You can reopen the wizard later:

```bash
fluxus setup accounts
```

Diagnostics and repair:

```bash
fluxus setup accounts status
fluxus setup accounts repair
```

The wizard offers either `VPS_IP.sslip.io` or a domain you own, selects a free
HTTPS port from `8443-8499`, and never uses port `443`. This keeps it separate
from 3x-ui/VLESS. Keep port `80/tcp` available for Let's Encrypt certificate
issuance and renewal.

Account mode is enabled only when the relay service has all of these environment variables:

```ini
FLUXCHAT_POSTGRES_CONNECTION=Host=127.0.0.1;Port=5432;Database=fluxchat;Username=fluxchat;Password=CHANGE_ME
FLUXCHAT_DATA_KEY=BASE64_32_BYTE_KEY
FLUXCHAT_ACCOUNT_API_PREFIX=http://127.0.0.1:42801/
FLUXCHAT_PUBLIC_ACCOUNT_URL=https://YOUR_DOMAIN_OR_IP.sslip.io:8443/
FLUXCHAT_RETENTION_DAYS=730
FLUXCHAT_FEDERATION_SERVER_ID=vps-1
FLUXCHAT_FEDERATION_KEY=AT_LEAST_32_RANDOM_BYTES
FLUXCHAT_FEDERATION_PEERS=trusted-vps.example.com:42800
```

Keep this file root-readable only, for example at `/etc/fluxchat/account.env` with mode `600`, and add it as `EnvironmentFile` to `fluxchat.service`.

The account API binds to loopback by default. Put it behind a TLS reverse proxy and expose it through an HTTPS URL. Set that public URL in `FLUXCHAT_PUBLIC_ACCOUNT_URL`; FluxChat sends it automatically after the user connects to the relay with an invite, so users enter only `VPS_IP:42800` and their invite code. Passwords are deliberately refused over HTTP by the client.

The server refuses a public plain-HTTP `FLUXCHAT_ACCOUNT_API_PREFIX`. Keep port
`42801` closed in the public firewall and proxy only the required `/api/v1`
paths through nginx or Caddy with a valid certificate. PostgreSQL port `5432`
must also remain private.

Federation packets are authenticated and encrypted with AES-GCM before they are
sent over the relay connection. Every trusted peer must use the same strong
federation key and run the same server version. Rotate that key immediately if
one peer is compromised.

Account and history endpoints enforce body limits, request throttling and
per-user storage quotas. Add an IP rate limit at the reverse proxy as another
layer of protection.

FluxChat message bodies are encrypted on the client with ECDH and AES-GCM. The
VPS still sees sender/recipient identifiers, timestamps and packet sizes, but
it does not receive plaintext message bodies.

Create PostgreSQL objects once:

```bash
sudo -u postgres createuser --pwprompt fluxchat
sudo -u postgres createdb --owner=fluxchat fluxchat
```

The server creates its tables automatically on first start. Use `fluxus` items 11 and 12 for PostgreSQL status and retention cleanup. Back up with `pg_dump --format=custom --file /root/fluxchat-backup.dump fluxchat` and restore with `pg_restore --clean --if-exists --dbname fluxchat /root/fluxchat-backup.dump`.

## Signed client releases

Run `dist.bat`, then create the release archive and detached signature:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-signed-release.ps1 -Version 1.1.5
```

Upload both generated files to the same GitHub release. The updater refuses an
archive when the matching `<archive-name>.sig` asset is missing or invalid.

## Import a legacy relay database

Make a filesystem backup before migration. The importer is read-only with respect to the SQLite source and keeps legacy `UserId` values. It imports relay users and pending packets; legacy relay tokens cannot become passwords, so each existing user completes the mandatory account registration after the upgrade.

```bash
cp /var/lib/fluxchat/fluxchat.db /var/lib/fluxchat/fluxchat.db.before-postgres
fluxus migrate-sqlite /var/lib/fluxchat/fluxchat.db
```
