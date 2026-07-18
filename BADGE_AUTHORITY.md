# Official Badge Authority

`FluxChat.BadgeAuthority` signs the global Owner and Tester badges. A normal FluxChat VPS is only a transport and cannot issue badges trusted by official clients.

## Build

Run `dist-badge-authority-linux.bat`. The result contains only public application code. It never contains the authority private key or database.

## Official deployment for this build

The official public key is already pinned in `BadgeAuthorityClient.OfficialPublicKey`. Its matching private key and the Owner certificate were created outside the repository in:

```text
%AppData%\FluxChat\badge-authority\authority-key.pem
%AppData%\FluxChat\badge-authority\badges.db
```

Transfer both files to `/var/lib/fluxchat-badge-authority` through a secure channel, then run:

```bash
sudo chown -R fluxchat:fluxchat /var/lib/fluxchat-badge-authority
sudo chmod 700 /var/lib/fluxchat-badge-authority
sudo chmod 600 /var/lib/fluxchat-badge-authority/authority-key.pem
```

Do not run `--init` again for this client build: a newly generated key would not match the public key pinned in official clients.

## Creating a new authority before a future release

Set these only on the official server:

```bash
export FLUXCHAT_BADGE_DATA=/var/lib/fluxchat-badge-authority
export FLUXCHAT_BADGE_OWNER_USER_ID='<owner FluxChat UserId>'
export FLUXCHAT_BADGE_OWNER_PUBLIC_KEY='<owner profile public key>'
./FluxChat.BadgeAuthority --init
unset FLUXCHAT_BADGE_OWNER_USER_ID FLUXCHAT_BADGE_OWNER_PUBLIC_KEY
```

The command creates `authority-key.pem` with mode `600`, initializes SQLite and issues the immutable Owner certificate. Put the printed public key into `BadgeAuthorityClient.OfficialPublicKey` before building that release. Keep the private key outside Git, release archives, logs and repository backups.

## Run

```bash
export FLUXCHAT_BADGE_DATA=/var/lib/fluxchat-badge-authority
export ASPNETCORE_URLS=http://127.0.0.1:42900
./FluxChat.BadgeAuthority
```

The current official deployment is exposed as
`https://badges.91-186-217-186.sslip.io:8443` through an Nginx TLS reverse proxy.
Port `443` remains available for the VPS Reality endpoint. Official clients pin
the authority public key, so changing DNS or running a lookalike service cannot
create trusted badges.

Run `./FluxChat.BadgeAuthority --self-test` after updates to verify certificate tamper, copied-certificate and revocation checks.
