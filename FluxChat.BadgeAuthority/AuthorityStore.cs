using System.Security.Cryptography;
using FluxChat.Shared;
using Microsoft.Data.Sqlite;

namespace FluxChat.BadgeAuthority;

internal sealed class AuthorityStore(string databasePath, ECDsa authorityKey)
{
    private readonly object _sync = new();
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS BadgeSubjects (
                    UserId TEXT PRIMARY KEY,
                    PublicKey TEXT NOT NULL,
                    DisplayName TEXT NOT NULL DEFAULT '',
                    SelectedBadgeId TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS BadgeCertificates (
                    Serial TEXT PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    BadgeId TEXT NOT NULL,
                    CertificateJson TEXT NOT NULL,
                    IssuedAtUtc TEXT NOT NULL,
                    GrantedByUserId TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_BadgeCertificates_User_Badge
                ON BadgeCertificates(UserId, BadgeId);
                CREATE TABLE IF NOT EXISTS RevokedCertificates (
                    Serial TEXT PRIMARY KEY,
                    RevokedAtUtc TEXT NOT NULL,
                    RevokedByUserId TEXT NOT NULL,
                    Reason TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS AuthorityAdmins (
                    UserId TEXT PRIMARY KEY,
                    PublicKey TEXT NOT NULL,
                    AddedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS BadgeAuditLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActorUserId TEXT NOT NULL,
                    TargetUserId TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    BadgeId TEXT NULL,
                    Serial TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS AuthenticationChallenges (
                    ChallengeId TEXT PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    PublicKey TEXT NOT NULL,
                    Challenge TEXT NOT NULL,
                    ExpiresAtUtc TEXT NOT NULL,
                    UsedAtUtc TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    public void BootstrapOwner(string userId, string publicKey)
    {
        if (BadgeCrypto.CreateUserId(publicKey) != userId)
        {
            throw new InvalidOperationException("Owner UserId does not match the supplied public key.");
        }

        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            UpsertSubject(connection, transaction, userId, publicKey, "Owner");
            using (var admin = connection.CreateCommand())
            {
                admin.Transaction = transaction;
                admin.CommandText = "INSERT OR REPLACE INTO AuthorityAdmins(UserId, PublicKey, AddedAtUtc) VALUES($userId, $publicKey, $now);";
                admin.Parameters.AddWithValue("$userId", userId);
                admin.Parameters.AddWithValue("$publicKey", publicKey);
                admin.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                admin.ExecuteNonQuery();
            }

            if (GetCertificate(connection, transaction, userId, BadgeIds.Owner) is null)
            {
                InsertCertificate(connection, transaction, Issue(userId, publicKey, BadgeIds.Owner), userId);
            }

            transaction.Commit();
        }
    }

    public BadgeChallengeResponse CreateChallenge(BadgeChallengeRequest request)
    {
        if (BadgeCrypto.CreateUserId(request.PublicKey) != request.UserId)
        {
            throw new InvalidOperationException("UserId does not match public key.");
        }

        var id = Guid.NewGuid().ToString("N");
        var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddSeconds(60);
        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            UpsertSubject(connection, transaction, request.UserId, request.PublicKey, request.DisplayName.Trim());
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO AuthenticationChallenges(ChallengeId, UserId, PublicKey, Challenge, ExpiresAtUtc)
                VALUES($id, $userId, $publicKey, $challenge, $expires);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$userId", request.UserId);
            command.Parameters.AddWithValue("$publicKey", request.PublicKey);
            command.Parameters.AddWithValue("$challenge", challenge);
            command.Parameters.AddWithValue("$expires", expires.ToString("O"));
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        return new BadgeChallengeResponse(id, challenge, expires);
    }

    public AuthenticatedSubject Authenticate(BadgeAuthenticateRequest request)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT UserId, PublicKey, Challenge, ExpiresAtUtc, UsedAtUtc
                FROM AuthenticationChallenges WHERE ChallengeId = $id;
                """;
            command.Parameters.AddWithValue("$id", request.ChallengeId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new UnauthorizedAccessException("Challenge was not found.");
            }

            var userId = reader.GetString(0);
            var publicKey = reader.GetString(1);
            var challenge = reader.GetString(2);
            var expires = DateTimeOffset.Parse(reader.GetString(3));
            var used = !reader.IsDBNull(4);
            reader.Close();
            if (used || expires < DateTimeOffset.UtcNow ||
                !BadgeCrypto.Verify(Convert.FromBase64String(challenge), request.Signature, publicKey))
            {
                throw new UnauthorizedAccessException("Challenge signature is invalid or expired.");
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE AuthenticationChallenges SET UsedAtUtc = $now WHERE ChallengeId = $id AND UsedAtUtc IS NULL;";
            update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$id", request.ChallengeId);
            if (update.ExecuteNonQuery() != 1)
            {
                throw new UnauthorizedAccessException("Challenge has already been used.");
            }

            transaction.Commit();
            return new AuthenticatedSubject(userId, publicKey, IsAdmin(connection, userId));
        }
    }

    public BadgeStateResponse GetState(string userId)
    {
        lock (_sync)
        {
            using var connection = Open();
            var certificates = GetCertificates(connection, userId);
            using var selected = connection.CreateCommand();
            selected.CommandText = "SELECT SelectedBadgeId FROM BadgeSubjects WHERE UserId = $userId;";
            selected.Parameters.AddWithValue("$userId", userId);
            var selectedBadge = selected.ExecuteScalar() as string;
            if (selectedBadge is not null && certificates.All(x => x.BadgeId != selectedBadge))
            {
                selectedBadge = null;
            }

            return new BadgeStateResponse(certificates, selectedBadge, IsAdmin(connection, userId), CreateRevocations(connection));
        }
    }

    public BadgeStateResponse Select(string userId, string? badgeId)
    {
        lock (_sync)
        {
            using var connection = Open();
            if (badgeId is not null && GetCertificate(connection, null, userId, badgeId) is null)
            {
                throw new InvalidOperationException("Badge is not owned by this user.");
            }

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE BadgeSubjects SET SelectedBadgeId = $badgeId, UpdatedAtUtc = $now WHERE UserId = $userId;";
            command.Parameters.AddWithValue("$badgeId", (object?)badgeId ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$userId", userId);
            command.ExecuteNonQuery();
        }

        return GetState(userId);
    }

    public BadgeAdminUserResponse Lookup(string userId)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DisplayName FROM BadgeSubjects WHERE UserId = $userId;";
            command.Parameters.AddWithValue("$userId", userId);
            var name = command.ExecuteScalar() as string ?? throw new KeyNotFoundException("User has not authenticated with Badge Authority yet.");
            return new BadgeAdminUserResponse(userId, name, GetCertificates(connection, userId));
        }
    }

    public BadgeAdminUserResponse GrantTester(string actorUserId, string targetUserId)
        => GrantBadge(actorUserId, targetUserId, BadgeIds.Tester);

    public BadgeAdminUserResponse RevokeTester(string actorUserId, string targetUserId)
        => RevokeBadge(actorUserId, targetUserId, BadgeIds.Tester);

    public BadgeAdminUserResponse GrantSpecial(string actorUserId, string targetUserId)
        => GrantBadge(actorUserId, targetUserId, BadgeIds.Special);

    public BadgeAdminUserResponse RevokeSpecial(string actorUserId, string targetUserId)
        => RevokeBadge(actorUserId, targetUserId, BadgeIds.Special);

    private BadgeAdminUserResponse GrantBadge(string actorUserId, string targetUserId, string badgeId)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var publicKey = GetSubjectPublicKey(connection, transaction, targetUserId);
            if (GetCertificate(connection, transaction, targetUserId, badgeId) is null)
            {
                InsertCertificate(connection, transaction, Issue(targetUserId, publicKey, badgeId), actorUserId);
            }
            transaction.Commit();
        }
        return Lookup(targetUserId);
    }

    private BadgeAdminUserResponse RevokeBadge(string actorUserId, string targetUserId, string badgeId)
    {
        lock (_sync)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            var certificate = GetCertificate(connection, transaction, targetUserId, badgeId);
            if (certificate is not null)
            {
                using var revoke = connection.CreateCommand();
                revoke.Transaction = transaction;
                revoke.CommandText = "INSERT OR IGNORE INTO RevokedCertificates VALUES($serial, $now, $actor, 'Revoked by administrator');";
                revoke.Parameters.AddWithValue("$serial", certificate.Serial);
                revoke.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                revoke.Parameters.AddWithValue("$actor", actorUserId);
                revoke.ExecuteNonQuery();
                Audit(connection, transaction, actorUserId, targetUserId, "revoke", badgeId, certificate.Serial);
                using var clear = connection.CreateCommand();
                clear.Transaction = transaction;
                clear.CommandText = "UPDATE BadgeSubjects SET SelectedBadgeId = NULL WHERE UserId = $userId AND SelectedBadgeId = $badgeId;";
                clear.Parameters.AddWithValue("$userId", targetUserId);
                clear.Parameters.AddWithValue("$badgeId", badgeId);
                clear.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        return Lookup(targetUserId);
    }

    private BadgeCertificate Issue(string userId, string publicKey, string badgeId)
    {
        var unsigned = new BadgeCertificate(
            BadgeCrypto.CertificateVersion,
            Guid.NewGuid().ToString("N"), badgeId, userId,
            BadgeCrypto.PublicKeySha256(publicKey), DateTimeOffset.UtcNow,
            BadgeCrypto.OfficialIssuer, "");
        return unsigned with { Signature = BadgeCrypto.Sign(BadgeCrypto.BuildCertificatePayload(unsigned), authorityKey) };
    }

    private BadgeRevocationSnapshot CreateRevocations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Serial FROM RevokedCertificates ORDER BY Serial;";
        using var reader = command.ExecuteReader();
        var serials = new List<string>();
        while (reader.Read()) serials.Add(reader.GetString(0));
        var unsigned = new BadgeRevocationSnapshot(serials.Count, DateTimeOffset.UtcNow, serials, "");
        return unsigned with { Signature = BadgeCrypto.Sign(BadgeCrypto.BuildRevocationPayload(unsigned), authorityKey) };
    }

    private static void UpsertSubject(SqliteConnection c, SqliteTransaction t, string userId, string publicKey, string displayName)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = """
            INSERT INTO BadgeSubjects(UserId, PublicKey, DisplayName, UpdatedAtUtc)
            VALUES($userId, $publicKey, $name, $now)
            ON CONFLICT(UserId) DO UPDATE SET PublicKey = excluded.PublicKey,
                DisplayName = CASE WHEN excluded.DisplayName = '' THEN BadgeSubjects.DisplayName ELSE excluded.DisplayName END,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$publicKey", publicKey);
        command.Parameters.AddWithValue("$name", displayName);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertCertificate(SqliteConnection c, SqliteTransaction t, BadgeCertificate cert, string actor)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "INSERT INTO BadgeCertificates VALUES($serial,$userId,$badgeId,$json,$issued,$actor);";
        command.Parameters.AddWithValue("$serial", cert.Serial);
        command.Parameters.AddWithValue("$userId", cert.UserId);
        command.Parameters.AddWithValue("$badgeId", cert.BadgeId);
        command.Parameters.AddWithValue("$json", System.Text.Json.JsonSerializer.Serialize(cert));
        command.Parameters.AddWithValue("$issued", cert.IssuedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$actor", actor);
        command.ExecuteNonQuery();
        Audit(c, t, actor, cert.UserId, "grant", cert.BadgeId, cert.Serial);
    }

    private static void Audit(SqliteConnection c, SqliteTransaction t, string actor, string target, string action, string badge, string serial)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "INSERT INTO BadgeAuditLog(ActorUserId,TargetUserId,Action,BadgeId,Serial,CreatedAtUtc) VALUES($a,$t,$action,$badge,$serial,$now);";
        command.Parameters.AddWithValue("$a", actor);
        command.Parameters.AddWithValue("$t", target);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$badge", badge);
        command.Parameters.AddWithValue("$serial", serial);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static BadgeCertificate? GetCertificate(SqliteConnection c, SqliteTransaction? t, string userId, string badgeId)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = """
            SELECT CertificateJson FROM BadgeCertificates bc
            WHERE UserId=$userId AND BadgeId=$badgeId
              AND NOT EXISTS(SELECT 1 FROM RevokedCertificates r WHERE r.Serial=bc.Serial);
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$badgeId", badgeId);
        var json = command.ExecuteScalar() as string;
        return json is null ? null : System.Text.Json.JsonSerializer.Deserialize<BadgeCertificate>(json);
    }

    private static List<BadgeCertificate> GetCertificates(SqliteConnection c, string userId)
    {
        using var command = c.CreateCommand();
        command.CommandText = """
            SELECT CertificateJson FROM BadgeCertificates bc WHERE UserId=$userId
              AND NOT EXISTS(SELECT 1 FROM RevokedCertificates r WHERE r.Serial=bc.Serial)
            ORDER BY IssuedAtUtc;
            """;
        command.Parameters.AddWithValue("$userId", userId);
        using var reader = command.ExecuteReader();
        var result = new List<BadgeCertificate>();
        while (reader.Read())
        {
            var certificate = System.Text.Json.JsonSerializer.Deserialize<BadgeCertificate>(reader.GetString(0));
            if (certificate is not null) result.Add(certificate);
        }
        return result;
    }

    private static string GetSubjectPublicKey(SqliteConnection c, SqliteTransaction t, string userId)
    {
        using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = "SELECT PublicKey FROM BadgeSubjects WHERE UserId=$userId;";
        command.Parameters.AddWithValue("$userId", userId);
        return command.ExecuteScalar() as string ?? throw new KeyNotFoundException("Target user has not authenticated with Badge Authority.");
    }

    private static bool IsAdmin(SqliteConnection c, string userId)
    {
        using var command = c.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AuthorityAdmins WHERE UserId=$userId;";
        command.Parameters.AddWithValue("$userId", userId);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connectionString); c.Open(); return c; }
}

internal sealed record AuthenticatedSubject(string UserId, string PublicKey, bool IsAdmin);
