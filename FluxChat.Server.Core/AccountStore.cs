using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;
using Konscious.Security.Cryptography;
using Npgsql;

namespace FluxChat.Server.Core;

public sealed class AccountStore
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CodeCooldown = TimeSpan.FromMinutes(1);
    private const int MaxCodeAttempts = 5;
    private const int MaxArchivedPacketBytes = 2 * 1024 * 1024;
    private const long MaxArchiveBytesPerUser = 1024L * 1024 * 1024;
    private const int MaxPendingPacketsPerRecipient = 5000;
    private const int MaxMediaBytes = 25 * 1024 * 1024;
    private const int MaxAvatarBytes = 8 * 1024 * 1024;
    private readonly string _connectionString;
    private readonly ServerDataProtector _protector;
    private readonly string _serverId;
    private readonly string _mediaRoot;

    public AccountStore(string connectionString, ServerDataProtector protector, string? serverId = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("FLUXCHAT_POSTGRES_CONNECTION is required for account storage.");
        }

        _connectionString = connectionString;
        _protector = protector;
        _serverId = string.IsNullOrWhiteSpace(serverId) ? Environment.MachineName : serverId.Trim();
        _mediaRoot = Environment.GetEnvironmentVariable("FLUXCHAT_MEDIA_DIR") ?? "/var/lib/fluxchat/media";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS fc_accounts (
                user_id TEXT PRIMARY KEY,
                login_normalized TEXT NOT NULL UNIQUE,
                login_display TEXT NOT NULL,
                display_name TEXT NOT NULL,
                email_normalized TEXT NOT NULL UNIQUE,
                email_display TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                public_key TEXT NOT NULL,
                recovery_vault BYTEA NOT NULL,
                is_email_verified BOOLEAN NOT NULL DEFAULT FALSE,
                is_banned BOOLEAN NOT NULL DEFAULT FALSE,
                is_login_conflicted BOOLEAN NOT NULL DEFAULT FALSE,
                username_claimed_at_utc TIMESTAMPTZ NOT NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,
                last_seen_at_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS fc_account_codes (
                code_id UUID PRIMARY KEY,
                user_id TEXT NOT NULL REFERENCES fc_accounts(user_id) ON DELETE CASCADE,
                purpose TEXT NOT NULL,
                code_hash TEXT NOT NULL,
                expires_at_utc TIMESTAMPTZ NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                consumed_at_utc TIMESTAMPTZ NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,
                requested_ip TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_fc_account_codes_active
                ON fc_account_codes(user_id, purpose, created_at_utc DESC);

            CREATE TABLE IF NOT EXISTS fc_account_sessions (
                token_hash TEXT PRIMARY KEY,
                user_id TEXT NOT NULL REFERENCES fc_accounts(user_id) ON DELETE CASCADE,
                device_name TEXT NOT NULL,
                expires_at_utc TIMESTAMPTZ NOT NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,
                last_seen_at_utc TIMESTAMPTZ NOT NULL,
                revoked_at_utc TIMESTAMPTZ NULL
            );
            CREATE INDEX IF NOT EXISTS ix_fc_account_sessions_user_id ON fc_account_sessions(user_id);

            CREATE TABLE IF NOT EXISTS fc_federation_claims (
                login_normalized TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                server_id TEXT NOT NULL,
                claimed_at_utc TIMESTAMPTZ NOT NULL,
                signature TEXT NOT NULL,
                received_at_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS fc_message_archive (
                message_id UUID PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                sender_user_id TEXT NOT NULL,
                recipient_user_id TEXT NOT NULL,
                encrypted_payload BYTEA NOT NULL,
                metadata_json TEXT NOT NULL DEFAULT '',
                created_at_utc TIMESTAMPTZ NOT NULL,
                deleted_at_utc TIMESTAMPTZ NULL
            );
            CREATE INDEX IF NOT EXISTS ix_fc_message_archive_conversation
                ON fc_message_archive(conversation_id, created_at_utc DESC);

            CREATE TABLE IF NOT EXISTS fc_pending_packets (
                message_id UUID PRIMARY KEY,
                to_user_id TEXT NOT NULL,
                packet_json BYTEA NOT NULL,
                stored_at_utc TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_fc_pending_packets_recipient
                ON fc_pending_packets(to_user_id, stored_at_utc ASC);

            CREATE TABLE IF NOT EXISTS fc_contacts (
                owner_user_id TEXT NOT NULL REFERENCES fc_accounts(user_id) ON DELETE CASCADE,
                contact_user_id TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                updated_at_utc TIMESTAMPTZ NOT NULL,
                is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                PRIMARY KEY(owner_user_id, contact_user_id)
            );
            CREATE INDEX IF NOT EXISTS ix_fc_contacts_owner_updated
                ON fc_contacts(owner_user_id, updated_at_utc DESC);

            CREATE TABLE IF NOT EXISTS fc_legacy_relay_users (
                user_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                is_banned BOOLEAN NOT NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,
                last_seen_at_utc TIMESTAMPTZ NOT NULL,
                imported_at_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS fc_media_objects (
                media_id UUID PRIMARY KEY,
                owner_user_id TEXT NOT NULL,
                storage_key TEXT NOT NULL UNIQUE,
                encrypted_metadata BYTEA NOT NULL,
                mime_type TEXT NOT NULL,
                byte_length BIGINT NOT NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,
                deleted_at_utc TIMESTAMPTZ NULL
            );

            CREATE TABLE IF NOT EXISTS fc_server_settings (
                setting_key TEXT PRIMARY KEY,
                protected_value BYTEA NOT NULL,
                updated_at_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS fc_audit_log (
                audit_id UUID PRIMARY KEY,
                event_type TEXT NOT NULL,
                actor_user_id TEXT NULL,
                target_user_id TEXT NULL,
                detail TEXT NOT NULL DEFAULT '',
                created_at_utc TIMESTAMPTZ NOT NULL
            );
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var migration = new NpgsqlCommand("ALTER TABLE fc_accounts ADD COLUMN IF NOT EXISTS is_login_conflicted BOOLEAN NOT NULL DEFAULT FALSE;", connection);
        await migration.ExecuteNonQueryAsync(cancellationToken);
        await using var avatarMigration = new NpgsqlCommand("""
            ALTER TABLE fc_accounts ADD COLUMN IF NOT EXISTS avatar_media_id UUID NULL;
            ALTER TABLE fc_accounts ADD COLUMN IF NOT EXISTS avatar_kind TEXT NOT NULL DEFAULT 'color';
            ALTER TABLE fc_accounts ADD COLUMN IF NOT EXISTS avatar_version BIGINT NOT NULL DEFAULT 0;
            ALTER TABLE fc_accounts ADD COLUMN IF NOT EXISTS avatar_updated_at_utc TIMESTAMPTZ NULL;
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS media_kind TEXT NOT NULL DEFAULT 'message';
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS file_name TEXT NOT NULL DEFAULT '';
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS sha256 TEXT NOT NULL DEFAULT '';
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS thumbnail_media_id UUID NULL;
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS width INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS height INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS duration_ms INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE fc_media_objects ADD COLUMN IF NOT EXISTS owner_message_id UUID NULL;
            ALTER TABLE fc_account_sessions ADD COLUMN IF NOT EXISTS session_id UUID NULL;
            ALTER TABLE fc_account_sessions ADD COLUMN IF NOT EXISTS created_ip TEXT NOT NULL DEFAULT '';
            ALTER TABLE fc_account_sessions ADD COLUMN IF NOT EXISTS last_ip TEXT NOT NULL DEFAULT '';
            """, connection);
        await avatarMigration.ExecuteNonQueryAsync(cancellationToken);
        await using var sessionMigration = new NpgsqlCommand("""
            UPDATE fc_account_sessions
            SET session_id = md5(random()::text || clock_timestamp()::text || token_hash)::uuid
            WHERE session_id IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ix_fc_account_sessions_session_id
                ON fc_account_sessions(session_id)
                WHERE session_id IS NOT NULL;
            """, connection);
        await sessionMigration.ExecuteNonQueryAsync(cancellationToken);
        Directory.CreateDirectory(_mediaRoot);
    }

    public async Task<AccountRegistrationResult> RegisterAsync(
        string userId,
        string displayName,
        string login,
        string email,
        string password,
        string publicKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedLogin = NormalizeLogin(login);
        var normalizedEmail = NormalizeEmail(email);
        ValidateRegistration(userId, displayName, normalizedLogin, normalizedEmail, password, publicKey);
        if (!string.Equals(IdentityCrypto.CreateUserId(publicKey), userId, StringComparison.Ordinal))
        {
            throw new ArgumentException("UserId does not match the supplied public key.");
        }

        var now = DateTimeOffset.UtcNow;
        var vault = RandomNumberGenerator.GetBytes(32);
        await using var connection = await OpenAsync(cancellationToken);

        // A client profile represents one encrypted identity and can only be linked
        // to one account. Report that separately from a taken login so the client
        // does not suggest that every new login is unavailable.
        await using (var byUserId = new NpgsqlCommand("SELECT login_display FROM fc_accounts WHERE user_id=@userId LIMIT 1;", connection))
        {
            byUserId.Parameters.AddWithValue("userId", userId.Trim());
            var existingLogin = await byUserId.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.IsNullOrWhiteSpace(existingLogin))
            {
                return AccountRegistrationResult.Denied(
                    $"This FluxChat profile is already linked to '{existingLogin}'. Sign in with that account instead of creating another one.");
            }
        }

        await using (var byLogin = new NpgsqlCommand("SELECT 1 FROM fc_accounts WHERE login_normalized=@loginNormalized LIMIT 1;", connection))
        {
            byLogin.Parameters.AddWithValue("loginNormalized", normalizedLogin);
            if (await byLogin.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return AccountRegistrationResult.Denied("This login is already registered. Choose another login.");
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO fc_accounts (
                    user_id, login_normalized, login_display, display_name, email_normalized, email_display,
                    password_hash, public_key, recovery_vault, is_email_verified, username_claimed_at_utc, created_at_utc, last_seen_at_utc)
                VALUES (
                    @userId, @loginNormalized, @loginDisplay, @displayName, @emailNormalized, @emailDisplay,
                    @passwordHash, @publicKey, @recoveryVault, TRUE, @now, @now, @now);
                """, connection, transaction);
            command.Parameters.AddWithValue("userId", userId.Trim());
            command.Parameters.AddWithValue("loginNormalized", normalizedLogin);
            command.Parameters.AddWithValue("loginDisplay", login.Trim());
            command.Parameters.AddWithValue("displayName", displayName.Trim());
            command.Parameters.AddWithValue("emailNormalized", normalizedEmail);
            command.Parameters.AddWithValue("emailDisplay", email.Trim());
            command.Parameters.AddWithValue("passwordHash", PasswordHasher.Hash(password));
            command.Parameters.AddWithValue("publicKey", publicKey.Trim());
            command.Parameters.AddWithValue("recoveryVault", vault);
            command.Parameters.AddWithValue("now", now);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await using (var claim = new NpgsqlCommand("""
                INSERT INTO fc_federation_claims (login_normalized, user_id, server_id, claimed_at_utc, signature, received_at_utc)
                VALUES (@loginNormalized, @userId, @serverId, @claimedAtUtc, 'local', @receivedAtUtc);
                """, connection, transaction))
            {
                claim.Parameters.AddWithValue("loginNormalized", normalizedLogin);
                claim.Parameters.AddWithValue("userId", userId.Trim());
                claim.Parameters.AddWithValue("serverId", _serverId);
                claim.Parameters.AddWithValue("claimedAtUtc", now);
                claim.Parameters.AddWithValue("receivedAtUtc", now);
                await claim.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditAsync(connection, transaction, "account-registered", userId, userId, normalizedLogin, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AccountRegistrationResult.Success(now);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccountRegistrationResult.Denied("This login is already registered. Choose another login.");
        }
    }

    public async Task<AccountLoginResult> LoginAsync(string loginOrEmail, string password, string deviceName, string? clientIp = null, CancellationToken cancellationToken = default)
    {
        var identifier = NormalizeIdentifier(loginOrEmail);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT user_id, display_name, login_display, password_hash, is_banned, is_email_verified, is_login_conflicted
            FROM fc_accounts
            WHERE login_normalized = @identifier OR email_normalized = @identifier;
            """, connection);
        command.Parameters.AddWithValue("identifier", identifier);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return AccountLoginResult.Denied("Invalid login or password.");

        var userId = reader.GetString(0);
        var displayName = reader.GetString(1);
        var login = reader.GetString(2);
        var passwordHash = reader.GetString(3);
        var isBanned = reader.GetBoolean(4);
        var isLoginConflicted = reader.GetBoolean(6);
        if (isBanned) return AccountLoginResult.Denied("This account is banned.");
        if (isLoginConflicted) return AccountLoginResult.Denied("This login belongs to another federation user. Choose a new login.");
        if (!PasswordHasher.Verify(password, passwordHash)) return AccountLoginResult.Denied("Invalid login or password.");

        await reader.CloseAsync();
        return await CreateSessionAsync(connection, userId, displayName, login, deviceName, clientIp, cancellationToken);
    }

    public async Task<AccountCodeResult> CreateCodeAsync(string loginOrEmail, string purpose, string? requestedIp, CancellationToken cancellationToken = default)
    {
        if (purpose is not ("login" or "reset" or "verify-email")) return AccountCodeResult.Denied("Unsupported code purpose.");

        var identifier = NormalizeIdentifier(loginOrEmail);
        await using var connection = await OpenAsync(cancellationToken);
        var account = await FindAccountAsync(connection, identifier, cancellationToken);
        if (account is null) return AccountCodeResult.Denied("Account was not found.");
        if (account.IsBanned) return AccountCodeResult.Denied("This account is banned.");

        await using (var cooldown = new NpgsqlCommand("""
            SELECT created_at_utc FROM fc_account_codes
            WHERE user_id = @userId AND purpose = @purpose
            ORDER BY created_at_utc DESC LIMIT 1;
            """, connection))
        {
            cooldown.Parameters.AddWithValue("userId", account.UserId);
            cooldown.Parameters.AddWithValue("purpose", purpose);
            var previous = await cooldown.ExecuteScalarAsync(cancellationToken);
            if (previous is DateTimeOffset previousUtc && previousUtc > DateTimeOffset.UtcNow - CodeCooldown)
            {
                return AccountCodeResult.Denied("Please wait before requesting another code.");
            }
        }

        var code = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO fc_account_codes (code_id, user_id, purpose, code_hash, expires_at_utc, created_at_utc, requested_ip)
            VALUES (@codeId, @userId, @purpose, @codeHash, @expiresAtUtc, @createdAtUtc, @requestedIp);
            """, connection))
        {
            insert.Parameters.AddWithValue("codeId", Guid.NewGuid());
            insert.Parameters.AddWithValue("userId", account.UserId);
            insert.Parameters.AddWithValue("purpose", purpose);
            insert.Parameters.AddWithValue("codeHash", PasswordHasher.Hash(code));
            insert.Parameters.AddWithValue("expiresAtUtc", DateTimeOffset.UtcNow.Add(CodeLifetime));
            insert.Parameters.AddWithValue("createdAtUtc", DateTimeOffset.UtcNow);
            insert.Parameters.AddWithValue("requestedIp", (object?)requestedIp ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditAsync(connection, null, "account-code-requested", account.UserId, account.UserId, purpose, cancellationToken);
        return AccountCodeResult.Success(account.UserId, account.Email, code, DateTimeOffset.UtcNow.Add(CodeLifetime));
    }

    public async Task<AccountLoginResult> LoginByCodeAsync(string loginOrEmail, string code, string deviceName, CancellationToken cancellationToken = default)
    {
        var account = await VerifyCodeAsync(loginOrEmail, code, "login", cancellationToken);
        if (account is null) return AccountLoginResult.Denied("The code is invalid or expired.");
        await using var connection = await OpenAsync(cancellationToken);
        return await CreateSessionAsync(connection, account.UserId, account.DisplayName, account.Login, deviceName, null, cancellationToken);
    }

    public async Task<AccountResult> ResetPasswordAsync(string loginOrEmail, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        var account = await VerifyCodeAsync(loginOrEmail, code, "reset", cancellationToken);
        if (account is null) return AccountResult.Denied("The code is invalid or expired.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = new NpgsqlCommand("UPDATE fc_accounts SET password_hash=@passwordHash WHERE user_id=@userId;", connection, transaction))
        {
            update.Parameters.AddWithValue("passwordHash", PasswordHasher.Hash(newPassword));
            update.Parameters.AddWithValue("userId", account.UserId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var revoke = new NpgsqlCommand("UPDATE fc_account_sessions SET revoked_at_utc=@now WHERE user_id=@userId AND revoked_at_utc IS NULL;", connection, transaction))
        {
            revoke.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            revoke.Parameters.AddWithValue("userId", account.UserId);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }
        await AuditAsync(connection, transaction, "password-reset", account.UserId, account.UserId, "email-code", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountResult.Success("Password updated. Sign in again.");
    }

    public async Task<AccountResult> VerifyEmailAsync(string loginOrEmail, string code, CancellationToken cancellationToken = default)
    {
        var account = await VerifyCodeAsync(loginOrEmail, code, "verify-email", cancellationToken);
        if (account is null) return AccountResult.Denied("The code is invalid or expired.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE fc_accounts SET is_email_verified=TRUE WHERE user_id=@userId;", connection);
        command.Parameters.AddWithValue("userId", account.UserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(connection, null, "email-verified", account.UserId, account.UserId, "", cancellationToken);
        return AccountResult.Success("Email verified.");
    }

    public async Task<AccountSession?> ValidateSessionAsync(string token, string? clientIp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT s.session_id, a.user_id, a.display_name, a.login_display, s.expires_at_utc, s.device_name
            FROM fc_account_sessions s
            INNER JOIN fc_accounts a ON a.user_id=s.user_id
            WHERE s.token_hash=@tokenHash AND s.revoked_at_utc IS NULL AND s.expires_at_utc>@now AND a.is_banned=FALSE;
            """, connection);
        command.Parameters.AddWithValue("tokenHash", HashToken(token));
        var now = DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var session = new AccountSession(
            reader.IsDBNull(0) ? "" : reader.GetGuid(0).ToString("N"),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetString(5));
        await reader.CloseAsync();

        await using var update = new NpgsqlCommand("""
            UPDATE fc_account_sessions
            SET last_seen_at_utc=@now, last_ip=COALESCE(NULLIF(@clientIp, ''), last_ip)
            WHERE token_hash=@tokenHash;
            """, connection);
        update.Parameters.AddWithValue("now", now);
        update.Parameters.AddWithValue("clientIp", clientIp ?? "");
        update.Parameters.AddWithValue("tokenHash", HashToken(token));
        await update.ExecuteNonQueryAsync(cancellationToken);
        return session;
    }

    public async Task<IReadOnlyList<AccountDeviceSession>> ListSessionsAsync(AccountSession current, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT session_id, device_name, created_at_utc, last_seen_at_utc, expires_at_utc, created_ip, last_ip, revoked_at_utc
            FROM fc_account_sessions
            WHERE user_id=@userId
            ORDER BY (session_id::text=@currentSessionId) DESC, last_seen_at_utc DESC
            LIMIT 40;
            """, connection);
        command.Parameters.AddWithValue("userId", current.UserId);
        command.Parameters.AddWithValue("currentSessionId", NormalizeSessionId(current.SessionId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sessions = new List<AccountDeviceSession>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var sessionId = reader.IsDBNull(0) ? "" : reader.GetGuid(0).ToString("N");
            var lastIp = reader.GetString(6);
            var createdIp = reader.GetString(5);
            var isActive = reader.IsDBNull(7) && reader.GetFieldValue<DateTimeOffset>(4) > DateTimeOffset.UtcNow;
            sessions.Add(new AccountDeviceSession(
                sessionId,
                reader.GetString(1),
                BuildApproximateLocation(string.IsNullOrWhiteSpace(lastIp) ? createdIp : lastIp),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                string.Equals(sessionId, current.SessionId, StringComparison.OrdinalIgnoreCase),
                isActive));
        }

        return sessions;
    }

    public async Task<IReadOnlyList<AccountSyncedContact>> ListContactsAsync(AccountSession current, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT payload_json
            FROM fc_contacts
            WHERE owner_user_id=@ownerUserId AND is_deleted=FALSE
            ORDER BY updated_at_utc DESC;
            """, connection);
        command.Parameters.AddWithValue("ownerUserId", current.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var contacts = new List<AccountSyncedContact>();
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                var contact = JsonSerializer.Deserialize<AccountSyncedContact>(reader.GetString(0));
                if (contact is not null && !string.IsNullOrWhiteSpace(contact.UserId))
                {
                    contacts.Add(contact);
                }
            }
            catch (JsonException)
            {
            }
        }

        return contacts;
    }

    public async Task<AccountResult> UpsertContactAsync(AccountSession current, AccountSyncedContact contact, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contact.UserId))
        {
            return AccountResult.Denied("Contact user id is required.");
        }

        if (string.Equals(contact.UserId, current.UserId, StringComparison.Ordinal))
        {
            return AccountResult.Denied("Self contact sync is ignored.");
        }

        var updated = contact.UpdatedAtUtc == default
            ? contact with { UpdatedAtUtc = DateTimeOffset.UtcNow }
            : contact;
        var payload = JsonSerializer.Serialize(updated);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_contacts (owner_user_id, contact_user_id, payload_json, updated_at_utc, is_deleted)
            VALUES (@ownerUserId, @contactUserId, @payloadJson, @updatedAtUtc, FALSE)
            ON CONFLICT (owner_user_id, contact_user_id)
            DO UPDATE SET payload_json=EXCLUDED.payload_json,
                          updated_at_utc=EXCLUDED.updated_at_utc,
                          is_deleted=FALSE
            WHERE fc_contacts.updated_at_utc <= EXCLUDED.updated_at_utc OR fc_contacts.is_deleted=TRUE;
            """, connection);
        command.Parameters.AddWithValue("ownerUserId", current.UserId);
        command.Parameters.AddWithValue("contactUserId", updated.UserId);
        command.Parameters.AddWithValue("payloadJson", payload);
        command.Parameters.AddWithValue("updatedAtUtc", updated.UpdatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return AccountResult.Success("Contact synced.");
    }

    public async Task<AccountResult> DeleteContactAsync(AccountSession current, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return AccountResult.Denied("Contact user id is required.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_contacts (owner_user_id, contact_user_id, payload_json, updated_at_utc, is_deleted)
            VALUES (@ownerUserId, @contactUserId, '', @updatedAtUtc, TRUE)
            ON CONFLICT (owner_user_id, contact_user_id)
            DO UPDATE SET updated_at_utc=EXCLUDED.updated_at_utc,
                          is_deleted=TRUE;
            """, connection);
        command.Parameters.AddWithValue("ownerUserId", current.UserId);
        command.Parameters.AddWithValue("contactUserId", userId.Trim());
        command.Parameters.AddWithValue("updatedAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return AccountResult.Success("Contact removed.");
    }

    public async Task<AccountResult> RevokeSessionAsync(AccountSession current, string sessionId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sessionId, out var parsedSessionId))
        {
            return AccountResult.Denied("Device session is invalid.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE fc_account_sessions
            SET revoked_at_utc=@now
            WHERE user_id=@userId AND session_id=@sessionId AND revoked_at_utc IS NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("userId", current.UserId);
        command.Parameters.AddWithValue("sessionId", parsedSessionId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected < 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccountResult.Denied("Device session was not found.");
        }

        await AuditAsync(connection, transaction, "session-revoked", current.UserId, current.UserId, parsedSessionId.ToString("N"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountResult.Success("Device signed out.");
    }

    public async Task<AccountResult> RevokeSessionsAsync(AccountSession current, bool includeCurrent, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(includeCurrent
            ? """
              UPDATE fc_account_sessions
              SET revoked_at_utc=@now
              WHERE user_id=@userId AND revoked_at_utc IS NULL;
              """
            : """
              UPDATE fc_account_sessions
              SET revoked_at_utc=@now
              WHERE user_id=@userId AND session_id::text<>@currentSessionId AND revoked_at_utc IS NULL;
              """, connection, transaction);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("userId", current.UserId);
        if (!includeCurrent)
        {
            command.Parameters.AddWithValue("currentSessionId", NormalizeSessionId(current.SessionId));
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(connection, transaction, includeCurrent ? "sessions-revoked-all" : "sessions-revoked-other", current.UserId, current.UserId, "", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountResult.Success(includeCurrent ? "Signed out on all devices." : "Other devices were signed out.");
    }

    public async Task<AccountResult> ChangePasswordAsync(AccountSession current, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var select = new NpgsqlCommand("""
            SELECT password_hash
            FROM fc_accounts
            WHERE user_id=@userId AND is_banned=FALSE
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("userId", current.UserId);
            var hash = await select.ExecuteScalarAsync(cancellationToken) as string;
            if (string.IsNullOrWhiteSpace(hash) || !PasswordHasher.Verify(currentPassword, hash))
            {
                await transaction.RollbackAsync(cancellationToken);
                return AccountResult.Denied("Current password is incorrect.");
            }
        }

        await using (var update = new NpgsqlCommand("UPDATE fc_accounts SET password_hash=@passwordHash WHERE user_id=@userId;", connection, transaction))
        {
            update.Parameters.AddWithValue("passwordHash", PasswordHasher.Hash(newPassword));
            update.Parameters.AddWithValue("userId", current.UserId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var revoke = new NpgsqlCommand("""
            UPDATE fc_account_sessions
            SET revoked_at_utc=@now
            WHERE user_id=@userId AND session_id::text<>@currentSessionId AND revoked_at_utc IS NULL;
            """, connection, transaction))
        {
            revoke.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            revoke.Parameters.AddWithValue("userId", current.UserId);
            revoke.Parameters.AddWithValue("currentSessionId", NormalizeSessionId(current.SessionId));
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditAsync(connection, transaction, "password-changed", current.UserId, current.UserId, "other-sessions-revoked", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountResult.Success("Password updated. Other devices were signed out.");
    }

    public async Task<AccountResult> DeleteAccountAsync(AccountSession current, string currentPassword, string confirmation, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            return AccountResult.Denied("Type DELETE to confirm account deletion.");
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            return AccountResult.Denied("Current password is required.");
        }

        var storageKeys = new List<string>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var select = new NpgsqlCommand("""
            SELECT password_hash
            FROM fc_accounts
            WHERE user_id=@userId AND is_banned=FALSE
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("userId", current.UserId);
            var hash = await select.ExecuteScalarAsync(cancellationToken) as string;
            if (string.IsNullOrWhiteSpace(hash) || !PasswordHasher.Verify(currentPassword, hash))
            {
                await transaction.RollbackAsync(cancellationToken);
                return AccountResult.Denied("Current password is incorrect.");
            }
        }

        await using (var selectMedia = new NpgsqlCommand("""
            SELECT storage_key
            FROM fc_media_objects
            WHERE owner_user_id=@userId;
            """, connection, transaction))
        {
            selectMedia.Parameters.AddWithValue("userId", current.UserId);
            await using var reader = await selectMedia.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                storageKeys.Add(reader.GetString(0));
            }
        }

        await using (var messages = new NpgsqlCommand("""
            DELETE FROM fc_message_archive
            WHERE sender_user_id=@userId OR recipient_user_id=@userId;
            """, connection, transaction))
        {
            messages.Parameters.AddWithValue("userId", current.UserId);
            await messages.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var pending = new NpgsqlCommand("DELETE FROM fc_pending_packets WHERE to_user_id=@userId;", connection, transaction))
        {
            pending.Parameters.AddWithValue("userId", current.UserId);
            await pending.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var contacts = new NpgsqlCommand("""
            DELETE FROM fc_contacts
            WHERE owner_user_id=@userId OR contact_user_id=@userId;
            """, connection, transaction))
        {
            contacts.Parameters.AddWithValue("userId", current.UserId);
            await contacts.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var codes = new NpgsqlCommand("DELETE FROM fc_account_codes WHERE user_id=@userId;", connection, transaction))
        {
            codes.Parameters.AddWithValue("userId", current.UserId);
            await codes.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var sessions = new NpgsqlCommand("DELETE FROM fc_account_sessions WHERE user_id=@userId;", connection, transaction))
        {
            sessions.Parameters.AddWithValue("userId", current.UserId);
            await sessions.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var claims = new NpgsqlCommand("DELETE FROM fc_federation_claims WHERE user_id=@userId;", connection, transaction))
        {
            claims.Parameters.AddWithValue("userId", current.UserId);
            await claims.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var legacy = new NpgsqlCommand("DELETE FROM fc_legacy_relay_users WHERE user_id=@userId;", connection, transaction))
        {
            legacy.Parameters.AddWithValue("userId", current.UserId);
            await legacy.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var media = new NpgsqlCommand("DELETE FROM fc_media_objects WHERE owner_user_id=@userId;", connection, transaction))
        {
            media.Parameters.AddWithValue("userId", current.UserId);
            await media.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var audit = new NpgsqlCommand("""
            DELETE FROM fc_audit_log
            WHERE actor_user_id=@userId OR target_user_id=@userId;
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("userId", current.UserId);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var account = new NpgsqlCommand("DELETE FROM fc_accounts WHERE user_id=@userId;", connection, transaction))
        {
            account.Parameters.AddWithValue("userId", current.UserId);
            var deleted = await account.ExecuteNonQueryAsync(cancellationToken);
            if (deleted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AccountResult.Denied("Account was not found.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        foreach (var key in storageKeys.Distinct(StringComparer.Ordinal))
        {
            TryDeleteMediaFile(key);
        }

        Console.WriteLine($"Account deleted: userId={current.UserId}");
        return AccountResult.Success("Account deleted.");
    }

    public async Task<RetentionStats> DeleteExpiredAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays < 1) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long messages;
        await using (var command = new NpgsqlCommand("DELETE FROM fc_message_archive WHERE created_at_utc < @cutoff;", connection, transaction))
        {
            command.Parameters.AddWithValue("cutoff", cutoff);
            messages = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var storageKeys = new List<string>();
        await using (var selectMedia = new NpgsqlCommand("SELECT storage_key FROM fc_media_objects WHERE created_at_utc < @cutoff;", connection, transaction))
        {
            selectMedia.Parameters.AddWithValue("cutoff", cutoff);
            await using var reader = await selectMedia.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                storageKeys.Add(reader.GetString(0));
            }
        }
        long media;
        await using (var command = new NpgsqlCommand("DELETE FROM fc_media_objects WHERE created_at_utc < @cutoff;", connection, transaction))
        {
            command.Parameters.AddWithValue("cutoff", cutoff);
            media = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await AuditAsync(connection, transaction, "retention-cleanup", null, null, $"days={retentionDays};messages={messages};media={media}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var key in storageKeys)
        {
            TryDeleteMediaFile(key);
        }
        return new RetentionStats(messages, media, cutoff);
    }

    public async Task<AccountDatabaseStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT
              (SELECT count(*) FROM fc_accounts),
              (SELECT count(*) FROM fc_account_sessions WHERE revoked_at_utc IS NULL AND expires_at_utc > now()),
              (SELECT count(*) FROM fc_message_archive WHERE deleted_at_utc IS NULL),
              (SELECT count(*) FROM fc_media_objects WHERE deleted_at_utc IS NULL),
              pg_database_size(current_database());
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new AccountDatabaseStats(0, 0, 0, 0, 0);
        return new AccountDatabaseStats(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }

    public async Task<StoredMediaResult> StoreMediaAsync(
        string ownerUserId,
        string mediaKind,
        string fileName,
        string mimeType,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length is 0 or > MaxMediaBytes)
        {
            throw new InvalidDataException($"Media must be 1 byte to {MaxMediaBytes / 1024 / 1024} MB.");
        }

        mediaKind = NormalizeMediaKind(mediaKind);
        mimeType = NormalizeMimeType(mimeType);
        fileName = SanitizeFileName(fileName);
        var mediaId = Guid.NewGuid();
        var storageKey = CreateStorageKey(mediaId);
        var protectedBytes = _protector.Protect(bytes);
        var path = GetMediaPath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, protectedBytes, cancellationToken);

        var metadata = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new
        {
            fileName,
            mediaKind,
            mimeType,
            originalLength = bytes.Length
        }));
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_media_objects
              (media_id, owner_user_id, storage_key, encrypted_metadata, mime_type, byte_length, created_at_utc,
               media_kind, file_name, sha256)
            VALUES
              (@mediaId, @ownerUserId, @storageKey, @metadata, @mimeType, @byteLength, @createdAtUtc,
               @mediaKind, @fileName, @sha256);
            """, connection);
        command.Parameters.AddWithValue("mediaId", mediaId);
        command.Parameters.AddWithValue("ownerUserId", ownerUserId);
        command.Parameters.AddWithValue("storageKey", storageKey);
        command.Parameters.AddWithValue("metadata", metadata);
        command.Parameters.AddWithValue("mimeType", mimeType);
        command.Parameters.AddWithValue("byteLength", bytes.LongLength);
        command.Parameters.AddWithValue("createdAtUtc", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("mediaKind", mediaKind);
        command.Parameters.AddWithValue("fileName", fileName);
        command.Parameters.AddWithValue("sha256", sha256);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new StoredMediaResult(mediaId, mediaKind, fileName, mimeType, bytes.LongLength);
    }

    public async Task<StoredMediaDownload?> LoadMediaAsync(Guid mediaId, string requesterUserId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT storage_key, mime_type, file_name, byte_length
            FROM fc_media_objects
            WHERE media_id=@mediaId AND deleted_at_utc IS NULL;
            """, connection);
        command.Parameters.AddWithValue("mediaId", mediaId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var storageKey = reader.GetString(0);
        var mimeType = reader.GetString(1);
        var fileName = reader.GetString(2);
        var byteLength = reader.GetInt64(3);
        var path = GetMediaPath(storageKey);
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var bytes = _protector.Unprotect(protectedBytes);
        return new StoredMediaDownload(mediaId, fileName, mimeType, byteLength, bytes);
    }

    public async Task<StoredMediaResult> StoreAvatarAsync(
        string userId,
        string avatarKind,
        string fileName,
        string mimeType,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length is 0 or > MaxAvatarBytes)
        {
            throw new InvalidDataException($"Avatar must be 1 byte to {MaxAvatarBytes / 1024 / 1024} MB.");
        }

        StoredMediaResult media;
        string? oldStorageKey = null;
        Guid? oldMediaId = null;
        await using var connection = await OpenAsync(cancellationToken);
        await using (var old = new NpgsqlCommand("""
            SELECT a.avatar_media_id, m.storage_key
            FROM fc_accounts a
            LEFT JOIN fc_media_objects m ON m.media_id=a.avatar_media_id
            WHERE a.user_id=@userId;
            """, connection))
        {
            old.Parameters.AddWithValue("userId", userId);
            await using var reader = await old.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                oldMediaId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
                oldStorageKey = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        media = await StoreMediaAsync(userId, avatarKind == "video" ? "avatar-video" : "avatar-image", fileName, mimeType, bytes, cancellationToken);
        await using (var update = new NpgsqlCommand("""
            UPDATE fc_accounts
            SET avatar_media_id=@mediaId,
                avatar_kind=@avatarKind,
                avatar_version=avatar_version + 1,
                avatar_updated_at_utc=@now
            WHERE user_id=@userId;
            """, connection))
        {
            update.Parameters.AddWithValue("mediaId", media.MediaId);
            update.Parameters.AddWithValue("avatarKind", string.IsNullOrWhiteSpace(avatarKind) ? "image" : avatarKind);
            update.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            update.Parameters.AddWithValue("userId", userId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (oldMediaId is not null)
        {
            await MarkMediaDeletedAsync(connection, oldMediaId.Value, cancellationToken);
            if (!string.IsNullOrWhiteSpace(oldStorageKey)) TryDeleteMediaFile(oldStorageKey);
        }

        return media;
    }

    public async Task<StoredAvatarDownload?> LoadAvatarAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT a.avatar_media_id, a.avatar_kind, a.avatar_version, m.storage_key, m.mime_type, m.file_name, m.byte_length
            FROM fc_accounts a
            LEFT JOIN fc_media_objects m ON m.media_id=a.avatar_media_id AND m.deleted_at_utc IS NULL
            WHERE a.user_id=@userId;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(3)) return null;
        var mediaId = reader.GetGuid(0);
        var avatarKind = reader.GetString(1);
        var version = reader.GetInt64(2);
        var storageKey = reader.GetString(3);
        var mimeType = reader.GetString(4);
        var fileName = reader.GetString(5);
        var byteLength = reader.GetInt64(6);
        var path = GetMediaPath(storageKey);
        if (!File.Exists(path)) return null;
        var bytes = _protector.Unprotect(await File.ReadAllBytesAsync(path, cancellationToken));
        return new StoredAvatarDownload(mediaId, avatarKind, version, fileName, mimeType, byteLength, bytes);
    }

    public async Task ArchiveMessageAsync(string authenticatedUserId, ChatPacket packet, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(packet.FromUserId, authenticatedUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the sender may archive a message.");
        }
        if (string.IsNullOrWhiteSpace(packet.ToUserId))
        {
            throw new ArgumentException("Message recipient is required.");
        }

        var serialized = JsonSerializer.SerializeToUtf8Bytes(packet);
        if (serialized.Length > MaxArchivedPacketBytes)
        {
            throw new InvalidDataException($"Archived message exceeds the {MaxArchivedPacketBytes / 1024 / 1024} MB limit.");
        }

        var conversationId = CreateConversationId(packet.FromUserId, packet.ToUserId);
        var payload = _protector.Protect(serialized);
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureArchiveQuotaAsync(connection, authenticatedUserId, payload.Length, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_message_archive (message_id, conversation_id, sender_user_id, recipient_user_id, encrypted_payload, metadata_json, created_at_utc)
            VALUES (@messageId, @conversationId, @senderUserId, @recipientUserId, @payload, @metadata, @createdAtUtc)
            ON CONFLICT (message_id) DO NOTHING;
            """, connection);
        command.Parameters.AddWithValue("messageId", packet.MessageId);
        command.Parameters.AddWithValue("conversationId", conversationId);
        command.Parameters.AddWithValue("senderUserId", packet.FromUserId);
        command.Parameters.AddWithValue("recipientUserId", packet.ToUserId);
        command.Parameters.AddWithValue("payload", payload);
        command.Parameters.AddWithValue("metadata", string.IsNullOrWhiteSpace(packet.Intent) ? "" : packet.Intent);
        command.Parameters.AddWithValue("createdAtUtc", packet.SentAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task StorePendingPacketAsync(ChatPacket packet, CancellationToken cancellationToken = default)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(packet);
        if (serialized.Length > MaxArchivedPacketBytes)
        {
            throw new InvalidDataException($"Pending message exceeds the {MaxArchivedPacketBytes / 1024 / 1024} MB limit.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using (var quota = new NpgsqlCommand(
                         "SELECT count(*) FROM fc_pending_packets WHERE to_user_id=@toUserId;",
                         connection))
        {
            quota.Parameters.AddWithValue("toUserId", packet.ToUserId);
            var count = Convert.ToInt32(await quota.ExecuteScalarAsync(cancellationToken));
            if (count >= MaxPendingPacketsPerRecipient)
            {
                throw new InvalidOperationException("Recipient pending-message quota is full.");
            }
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_pending_packets (message_id, to_user_id, packet_json, stored_at_utc)
            VALUES (@messageId, @toUserId, @packetJson, @storedAtUtc)
            ON CONFLICT (message_id) DO NOTHING;
            """, connection);
        command.Parameters.AddWithValue("messageId", packet.MessageId);
        command.Parameters.AddWithValue("toUserId", packet.ToUserId);
        command.Parameters.AddWithValue("packetJson", _protector.Protect(serialized));
        command.Parameters.AddWithValue("storedAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatPacket>> LoadPendingPacketsAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT packet_json FROM fc_pending_packets WHERE to_user_id=@userId ORDER BY stored_at_utc ASC;", connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var packets = new List<ChatPacket>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var packet = JsonSerializer.Deserialize<ChatPacket>(_protector.Unprotect(reader.GetFieldValue<byte[]>(0)));
            if (packet is not null) packets.Add(packet);
        }
        return packets;
    }

    public async Task DeletePendingPacketAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM fc_pending_packets WHERE message_id=@messageId;", connection);
        command.Parameters.AddWithValue("messageId", messageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredPendingPacketsAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM fc_pending_packets WHERE stored_at_utc < @cutoff;", connection);
        command.Parameters.AddWithValue("cutoff", cutoffUtc);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ImportLegacyUserAsync(LegacyRelayUser user, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_legacy_relay_users (user_id, display_name, is_banned, created_at_utc, last_seen_at_utc, imported_at_utc)
            VALUES (@userId, @displayName, @isBanned, @createdAtUtc, @lastSeenAtUtc, @importedAtUtc)
            ON CONFLICT (user_id) DO UPDATE SET display_name=EXCLUDED.display_name, is_banned=EXCLUDED.is_banned,
                created_at_utc=EXCLUDED.created_at_utc, last_seen_at_utc=EXCLUDED.last_seen_at_utc, imported_at_utc=EXCLUDED.imported_at_utc;
            """, connection);
        command.Parameters.AddWithValue("userId", user.UserId);
        command.Parameters.AddWithValue("displayName", user.DisplayName);
        command.Parameters.AddWithValue("isBanned", user.IsBanned);
        command.Parameters.AddWithValue("createdAtUtc", user.CreatedAtUtc);
        command.Parameters.AddWithValue("lastSeenAtUtc", user.LastSeenAtUtc);
        command.Parameters.AddWithValue("importedAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatPacket>> LoadConversationAsync(string authenticatedUserId, string peerUserId, int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var conversationId = CreateConversationId(authenticatedUserId, peerUserId);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT encrypted_payload
            FROM fc_message_archive
            WHERE conversation_id=@conversationId AND deleted_at_utc IS NULL
            ORDER BY created_at_utc DESC LIMIT @take;
            """, connection);
        command.Parameters.AddWithValue("conversationId", conversationId);
        command.Parameters.AddWithValue("take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var packets = new List<ChatPacket>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var packet = JsonSerializer.Deserialize<ChatPacket>(_protector.Unprotect(reader.GetFieldValue<byte[]>(0)));
            if (packet is not null) packets.Add(packet);
        }
        packets.Reverse();
        return packets;
    }

    public async Task<bool> ApplyFederationClaimAsync(FederationUsernameClaim claim, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(claim.LoginNormalized) || string.IsNullOrWhiteSpace(claim.UserId) || string.IsNullOrWhiteSpace(claim.ServerId)) return false;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        FederationUsernameClaim? current = null;
        await using (var select = new NpgsqlCommand("""
            SELECT login_normalized, user_id, server_id, claimed_at_utc
            FROM fc_federation_claims WHERE login_normalized=@login FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("login", claim.LoginNormalized);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                current = new FederationUsernameClaim(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3));
            }
        }

        if (current is not null && !WinsOver(claim, current))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using (var upsert = new NpgsqlCommand("""
            INSERT INTO fc_federation_claims (login_normalized, user_id, server_id, claimed_at_utc, signature, received_at_utc)
            VALUES (@login, @userId, @serverId, @claimedAtUtc, 'federated', @receivedAtUtc)
            ON CONFLICT (login_normalized) DO UPDATE SET user_id=EXCLUDED.user_id, server_id=EXCLUDED.server_id,
                claimed_at_utc=EXCLUDED.claimed_at_utc, signature=EXCLUDED.signature, received_at_utc=EXCLUDED.received_at_utc;
            """, connection, transaction))
        {
            upsert.Parameters.AddWithValue("login", claim.LoginNormalized);
            upsert.Parameters.AddWithValue("userId", claim.UserId);
            upsert.Parameters.AddWithValue("serverId", claim.ServerId);
            upsert.Parameters.AddWithValue("claimedAtUtc", claim.ClaimedAtUtc);
            upsert.Parameters.AddWithValue("receivedAtUtc", DateTimeOffset.UtcNow);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var conflict = new NpgsqlCommand("""
            UPDATE fc_accounts SET is_login_conflicted=TRUE
            WHERE login_normalized=@login AND user_id<>@winner;
            """, connection, transaction))
        {
            conflict.Parameters.AddWithValue("login", claim.LoginNormalized);
            conflict.Parameters.AddWithValue("winner", claim.UserId);
            await conflict.ExecuteNonQueryAsync(cancellationToken);
        }
        await AuditAsync(connection, transaction, "federation-username-claim", null, claim.UserId, claim.LoginNormalized, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<FederationUsernameClaim>> GetFederationClaimsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT login_normalized, user_id, server_id, claimed_at_utc FROM fc_federation_claims ORDER BY claimed_at_utc ASC;", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var claims = new List<FederationUsernameClaim>();
        while (await reader.ReadAsync(cancellationToken))
        {
            claims.Add(new FederationUsernameClaim(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return claims;
    }

    private async Task<AccountLoginResult> CreateSessionAsync(NpgsqlConnection connection, string userId, string displayName, string login, string deviceName, string? clientIp, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_account_sessions (token_hash, session_id, user_id, device_name, expires_at_utc, created_at_utc, last_seen_at_utc, created_ip, last_ip)
            VALUES (@tokenHash, @sessionId, @userId, @deviceName, @expiresAtUtc, @now, @now, @clientIp, @clientIp);
            UPDATE fc_accounts SET last_seen_at_utc=@now WHERE user_id=@userId;
            """, connection);
        command.Parameters.AddWithValue("tokenHash", HashToken(token));
        command.Parameters.AddWithValue("sessionId", sessionId);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("deviceName", string.IsNullOrWhiteSpace(deviceName) ? "FluxChat" : deviceName[..Math.Min(deviceName.Length, 120)]);
        command.Parameters.AddWithValue("expiresAtUtc", expires);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("clientIp", clientIp ?? "");
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(connection, null, "account-login", userId, userId, "password-or-code", cancellationToken);
        return AccountLoginResult.Success(userId, displayName, login, token, expires, sessionId.ToString("N"), string.IsNullOrWhiteSpace(deviceName) ? "FluxChat" : deviceName);
    }

    private static string NormalizeSessionId(string value)
        => Guid.TryParse(value, out var guid) ? guid.ToString("D") : value;

    private static string BuildApproximateLocation(string ip)
        => string.IsNullOrWhiteSpace(ip) ? "Unknown location" : $"IP {ip}";

    private async Task<AccountRecord?> VerifyCodeAsync(string loginOrEmail, string code, string purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 8 || !code.All(char.IsDigit)) return null;
        var identifier = NormalizeIdentifier(loginOrEmail);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var account = await FindAccountAsync(connection, identifier, cancellationToken);
        if (account is null || account.IsBanned) return null;

        await using var command = new NpgsqlCommand("""
            SELECT code_id, code_hash, attempts, expires_at_utc
            FROM fc_account_codes
            WHERE user_id=@userId AND purpose=@purpose AND consumed_at_utc IS NULL
            ORDER BY created_at_utc DESC LIMIT 1 FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("userId", account.UserId);
        command.Parameters.AddWithValue("purpose", purpose);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var codeId = reader.GetGuid(0);
        var hash = reader.GetString(1);
        var attempts = reader.GetInt32(2);
        var expires = reader.GetFieldValue<DateTimeOffset>(3);
        await reader.CloseAsync();

        if (attempts >= MaxCodeAttempts || expires <= DateTimeOffset.UtcNow || !PasswordHasher.Verify(code, hash))
        {
            await using var fail = new NpgsqlCommand("UPDATE fc_account_codes SET attempts=attempts+1 WHERE code_id=@codeId;", connection, transaction);
            fail.Parameters.AddWithValue("codeId", codeId);
            await fail.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await using var consume = new NpgsqlCommand("UPDATE fc_account_codes SET consumed_at_utc=@now WHERE code_id=@codeId;", connection, transaction);
        consume.Parameters.AddWithValue("codeId", codeId);
        consume.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        await consume.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return account;
    }

    private static async Task<AccountRecord?> FindAccountAsync(NpgsqlConnection connection, string identifier, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT user_id, display_name, login_display, email_display, is_banned, is_email_verified
            FROM fc_accounts WHERE login_normalized=@identifier OR email_normalized=@identifier;
            """, connection);
        command.Parameters.AddWithValue("identifier", identifier);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AccountRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5))
            : null;
    }

    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string type, string? actor, string? target, string detail, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO fc_audit_log (audit_id, event_type, actor_user_id, target_user_id, detail, created_at_utc)
            VALUES (@id, @type, @actor, @target, @detail, @createdAtUtc);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("actor", (object?)actor ?? DBNull.Value);
        command.Parameters.AddWithValue("target", (object?)target ?? DBNull.Value);
        command.Parameters.AddWithValue("detail", detail);
        command.Parameters.AddWithValue("createdAtUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkMediaDeletedAsync(NpgsqlConnection connection, Guid mediaId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE fc_media_objects SET deleted_at_utc=@now WHERE media_id=@mediaId;", connection);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("mediaId", mediaId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureArchiveQuotaAsync(
        NpgsqlConnection connection,
        string userId,
        int incomingBytes,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(sum(octet_length(encrypted_payload)), 0)
            FROM fc_message_archive
            WHERE sender_user_id=@userId AND deleted_at_utc IS NULL;
            """, connection);
        command.Parameters.AddWithValue("userId", userId);
        var used = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        if (used + incomingBytes > MaxArchiveBytesPerUser)
        {
            throw new InvalidOperationException("Account message-history quota is full.");
        }
    }

    private static string NormalizeLogin(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized;
    }

    private static string NormalizeEmail(string value) => value.Trim().ToLowerInvariant();
    private static string NormalizeIdentifier(string value) => value.Trim().ToLowerInvariant();

    private static void ValidateRegistration(string userId, string displayName, string login, string email, string password, string publicKey)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(publicKey)) throw new ArgumentException("User identity is required.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 48) throw new ArgumentException("Display name must be 1-48 characters.");
        if (login.Length is < 3 or > 32 || !login.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) throw new ArgumentException("Login must be 3-32 Latin letters, digits, _ or -.");
        if (!email.Contains('@', StringComparison.Ordinal) || email.Length > 254) throw new ArgumentException("Email address is invalid.");
        ValidatePassword(password);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 10 || password.Length > 256) throw new ArgumentException("Password must be 10-256 characters.");
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CreateConversationId(string firstUserId, string secondUserId)
        => string.CompareOrdinal(firstUserId, secondUserId) <= 0
            ? $"{firstUserId}:{secondUserId}"
            : $"{secondUserId}:{firstUserId}";

    private static bool WinsOver(FederationUsernameClaim candidate, FederationUsernameClaim current)
        => candidate.ClaimedAtUtc < current.ClaimedAtUtc ||
           (candidate.ClaimedAtUtc == current.ClaimedAtUtc && string.CompareOrdinal(candidate.ServerId, current.ServerId) < 0);

    private static string NormalizeMediaKind(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "image" => "image",
            "gif" => "gif",
            "avatar-image" => "avatar-image",
            "avatar-video" => "avatar-video",
            "file" => "file",
            _ => "message"
        };

    private static string NormalizeMimeType(string value)
        => string.IsNullOrWhiteSpace(value) || value.Length > 120 ? "application/octet-stream" : value.Trim().ToLowerInvariant();

    private static string SanitizeFileName(string value)
    {
        value = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(value)) return "media.bin";
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Where(ch => !invalid.Contains(ch)).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "media.bin" : safe[..Math.Min(safe.Length, 160)];
    }

    private static string CreateStorageKey(Guid mediaId)
    {
        var id = mediaId.ToString("N");
        return $"{id[..2]}/{id}.bin";
    }

    private string GetMediaPath(string storageKey)
    {
        var normalized = storageKey.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Stored media path is invalid.");
        }

        return Path.Combine(_mediaRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    private void TryDeleteMediaFile(string storageKey)
    {
        try
        {
            var path = GetMediaPath(storageKey);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Media cleanup failed: {ex.Message}");
        }
    }
}

internal static class PasswordHasher
{
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Derive(password, salt);
        return $"argon2id$v=1$m=19456,t=2,p=1${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != "argon2id") return false;
        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Derive(password, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = 19 * 1024,
            Iterations = 2,
            DegreeOfParallelism = 1
        };
        return argon.GetBytes(HashLength);
    }
}

public sealed record AccountRegistrationResult(bool Accepted, string Message, DateTimeOffset? ClaimedAtUtc = null)
{
    public static AccountRegistrationResult Success(DateTimeOffset claimedAtUtc) => new(true, "Account created.", claimedAtUtc);
    public static AccountRegistrationResult Denied(string message) => new(false, message);
}

public sealed record AccountLoginResult(bool Accepted, string Message, string? UserId = null, string? DisplayName = null, string? Login = null, string? Token = null, DateTimeOffset? ExpiresAtUtc = null, string? SessionId = null, string? DeviceName = null)
{
    public static AccountLoginResult Success(string userId, string displayName, string login, string token, DateTimeOffset expires, string sessionId, string deviceName) => new(true, "Signed in.", userId, displayName, login, token, expires, sessionId, deviceName);
    public static AccountLoginResult Denied(string message) => new(false, message);
}

public sealed record AccountCodeResult(bool Accepted, string Message, string? UserId = null, string? Email = null, string? Code = null, DateTimeOffset? ExpiresAtUtc = null)
{
    public static AccountCodeResult Success(string userId, string email, string code, DateTimeOffset expires) => new(true, "Code issued.", userId, email, code, expires);
    public static AccountCodeResult Denied(string message) => new(false, message);
}

public sealed record AccountResult(bool Accepted, string Message)
{
    public static AccountResult Success(string message) => new(true, message);
    public static AccountResult Denied(string message) => new(false, message);
}

public sealed record AccountSession(string SessionId, string UserId, string DisplayName, string Login, DateTimeOffset ExpiresAtUtc, string DeviceName);
public sealed record RetentionStats(long MessagesDeleted, long MediaDeleted, DateTimeOffset CutoffUtc);
public sealed record AccountDatabaseStats(long Accounts, long ActiveSessions, long Messages, long MediaObjects, long DatabaseSizeBytes);
public sealed record LegacyRelayUser(string UserId, string DisplayName, bool IsBanned, DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenAtUtc);
public sealed record StoredMediaResult(Guid MediaId, string MediaKind, string FileName, string MimeType, long ByteLength);
public sealed record StoredMediaDownload(Guid MediaId, string FileName, string MimeType, long ByteLength, byte[] Bytes);
public sealed record StoredAvatarDownload(Guid MediaId, string AvatarKind, long AvatarVersion, string FileName, string MimeType, long ByteLength, byte[] Bytes);
internal sealed record AccountRecord(string UserId, string DisplayName, string Login, string Email, bool IsBanned, bool IsEmailVerified);
