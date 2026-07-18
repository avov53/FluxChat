using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;
using Microsoft.Data.Sqlite;

namespace FluxChat.Server.Core;

public sealed class RelayDatabase
{
    private readonly object _sync = new();
    private readonly string _connectionString;

    public RelayDatabase(string? databasePath = null)
    {
        ServerPaths.EnsureCreated();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath ?? ServerPaths.DatabasePath
        }.ToString();
    }

    public void Initialize()
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Invites (
                    Code TEXT PRIMARY KEY,
                    CreatedAtUtc TEXT NOT NULL,
                    UsedAtUtc TEXT NULL,
                    UsedByUserId TEXT NULL,
                    Note TEXT NOT NULL DEFAULT ''
                );

                CREATE TABLE IF NOT EXISTS Users (
                    UserId TEXT PRIMARY KEY,
                    DisplayName TEXT NOT NULL,
                    TokenHash TEXT NOT NULL,
                    IsBanned INTEGER NOT NULL DEFAULT 0,
                    CreatedAtUtc TEXT NOT NULL,
                    LastSeenUtc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS PendingMessages (
                    MessageId TEXT PRIMARY KEY,
                    FromUserId TEXT NOT NULL,
                    FromDisplayName TEXT NOT NULL,
                    ToUserId TEXT NOT NULL,
                    Body TEXT NOT NULL,
                    SentAtUtc TEXT NOT NULL,
                    StoredAtUtc TEXT NOT NULL,
                    NetworkId TEXT NULL,
                    FromRelayServer TEXT NULL,
                    ToRelayServer TEXT NULL,
                    Intent TEXT NULL,
                    FromStatus TEXT NULL,
                    FromAvatarKind TEXT NULL,
                    FromAvatarMediaBase64 TEXT NULL,
                    FromAvatarExtension TEXT NULL,
                    FromAvatarScale REAL NOT NULL DEFAULT 1,
                    FromAvatarOffsetX REAL NOT NULL DEFAULT 0,
                    FromAvatarOffsetY REAL NOT NULL DEFAULT 0,
                    FromAvatarVideoStartSeconds REAL NOT NULL DEFAULT 0,
                    FromAvatarVideoDurationSeconds REAL NOT NULL DEFAULT 10,
                    FromPublicKey TEXT NULL,
                    IdentityNonce TEXT NULL,
                    IdentitySignature TEXT NULL,
                    BadgeCertificateJson TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_PendingMessages_ToUserId_StoredAtUtc
                ON PendingMessages (ToUserId, StoredAtUtc);
                """;
            command.ExecuteNonQuery();

            command.CommandText = "ALTER TABLE PendingMessages ADD COLUMN NetworkId TEXT NULL;";
            try
            {
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
            }

            command.CommandText = "ALTER TABLE PendingMessages ADD COLUMN FromRelayServer TEXT NULL;";
            try
            {
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
            }

            command.CommandText = "ALTER TABLE PendingMessages ADD COLUMN ToRelayServer TEXT NULL;";
            try
            {
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
            }

            AddPendingColumn(command, "Intent TEXT NULL");
            AddPendingColumn(command, "FromStatus TEXT NULL");
            AddPendingColumn(command, "FromAvatarKind TEXT NULL");
            AddPendingColumn(command, "FromAvatarMediaBase64 TEXT NULL");
            AddPendingColumn(command, "FromAvatarExtension TEXT NULL");
            AddPendingColumn(command, "FromAvatarScale REAL NOT NULL DEFAULT 1");
            AddPendingColumn(command, "FromAvatarOffsetX REAL NOT NULL DEFAULT 0");
            AddPendingColumn(command, "FromAvatarOffsetY REAL NOT NULL DEFAULT 0");
            AddPendingColumn(command, "FromAvatarVideoStartSeconds REAL NOT NULL DEFAULT 0");
            AddPendingColumn(command, "FromAvatarVideoDurationSeconds REAL NOT NULL DEFAULT 10");
            AddPendingColumn(command, "FromPublicKey TEXT NULL");
            AddPendingColumn(command, "IdentityNonce TEXT NULL");
            AddPendingColumn(command, "IdentitySignature TEXT NULL");
            AddPendingColumn(command, "BadgeCertificateJson TEXT NULL");
        }
    }

    public AuthResult Authenticate(string userId, string displayName, string credential)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            var existing = GetUser(connection, transaction, userId);
            if (existing is not null)
            {
                if (existing.IsBanned)
                {
                    return AuthResult.Denied("User is banned.");
                }

                if (!FixedEquals(existing.TokenHash, HashSecret(credential)))
                {
                    return AuthResult.Denied("Invalid invite or token.");
                }

                UpdateLastSeen(connection, transaction, userId, displayName);
                transaction.Commit();
                return AuthResult.Accepted(null);
            }

            var invite = GetInvite(connection, transaction, credential);
            if (invite is null || invite.UsedAtUtc is not null)
            {
                return AuthResult.Denied("Invite code is invalid or already used.");
            }

            var token = CreateSecret("tok");
            var now = DateTimeOffset.UtcNow.ToString("O");
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO Users (UserId, DisplayName, TokenHash, IsBanned, CreatedAtUtc, LastSeenUtc)
                    VALUES ($userId, $displayName, $tokenHash, 0, $createdAtUtc, $lastSeenUtc);
                    """;
                insert.Parameters.AddWithValue("$userId", userId);
                insert.Parameters.AddWithValue("$displayName", displayName);
                insert.Parameters.AddWithValue("$tokenHash", HashSecret(token));
                insert.Parameters.AddWithValue("$createdAtUtc", now);
                insert.Parameters.AddWithValue("$lastSeenUtc", now);
                insert.ExecuteNonQuery();
            }

            using (var updateInvite = connection.CreateCommand())
            {
                updateInvite.Transaction = transaction;
                updateInvite.CommandText = """
                    UPDATE Invites
                    SET UsedAtUtc = $usedAtUtc,
                        UsedByUserId = $usedByUserId
                    WHERE Code = $code;
                    """;
                updateInvite.Parameters.AddWithValue("$usedAtUtc", now);
                updateInvite.Parameters.AddWithValue("$usedByUserId", userId);
                updateInvite.Parameters.AddWithValue("$code", credential);
                updateInvite.ExecuteNonQuery();
            }

            transaction.Commit();
            return AuthResult.Accepted(token);
        }
    }

    private static void AddPendingColumn(SqliteCommand command, string columnDefinition)
    {
        command.CommandText = $"ALTER TABLE PendingMessages ADD COLUMN {columnDefinition};";
        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
        }
    }

    public string CreateInvite(string note)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            while (true)
            {
                var code = CreateInviteCode();
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = """
                        INSERT INTO Invites (Code, CreatedAtUtc, Note)
                        VALUES ($code, $createdAtUtc, $note);
                        """;
                    command.Parameters.AddWithValue("$code", code);
                    command.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O"));
                    command.Parameters.AddWithValue("$note", note);
                    command.ExecuteNonQuery();
                    return code;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                }
            }
        }
    }

    public IReadOnlyList<InviteRow> GetInvites(bool includeUsed)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = includeUsed
                ? "SELECT Code, CreatedAtUtc, UsedAtUtc, UsedByUserId, Note FROM Invites ORDER BY CreatedAtUtc DESC;"
                : "SELECT Code, CreatedAtUtc, UsedAtUtc, UsedByUserId, Note FROM Invites WHERE UsedAtUtc IS NULL ORDER BY CreatedAtUtc DESC;";
            using var reader = command.ExecuteReader();
            var rows = new List<InviteRow>();
            while (reader.Read())
            {
                rows.Add(new InviteRow(
                    reader.GetString(0),
                    DateTimeOffset.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4)));
            }

            return rows;
        }
    }

    public bool DeleteInvite(string code)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Invites WHERE Code = $code AND UsedAtUtc IS NULL;";
            command.Parameters.AddWithValue("$code", code);
            return command.ExecuteNonQuery() > 0;
        }
    }

    public IReadOnlyList<UserRow> GetUsers()
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT UserId, DisplayName, IsBanned, CreatedAtUtc, LastSeenUtc
                FROM Users
                ORDER BY LastSeenUtc DESC;
                """;
            using var reader = command.ExecuteReader();
            var rows = new List<UserRow>();
            while (reader.Read())
            {
                rows.Add(new UserRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) == 1,
                    DateTimeOffset.Parse(reader.GetString(3)),
                    DateTimeOffset.Parse(reader.GetString(4))));
            }

            return rows;
        }
    }

    public bool SetBanned(string userId, bool banned)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET IsBanned = $isBanned WHERE UserId = $userId;";
            command.Parameters.AddWithValue("$isBanned", banned ? 1 : 0);
            command.Parameters.AddWithValue("$userId", userId);
            return command.ExecuteNonQuery() > 0;
        }
    }

    public string? ResetToken(string userId)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            var user = GetUser(connection, null, userId);
            if (user is null)
            {
                return null;
            }

            var token = CreateSecret("tok");
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Users SET TokenHash = $tokenHash WHERE UserId = $userId;";
            command.Parameters.AddWithValue("$tokenHash", HashSecret(token));
            command.Parameters.AddWithValue("$userId", userId);
            command.ExecuteNonQuery();
            return token;
        }
    }

    public void StorePending(ChatPacket packet)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO PendingMessages
                    (MessageId, FromUserId, FromDisplayName, ToUserId, Body, SentAtUtc, StoredAtUtc, NetworkId, FromRelayServer, ToRelayServer,
                     Intent, FromStatus, FromAvatarKind, FromAvatarMediaBase64, FromAvatarExtension, FromAvatarScale,
                     FromAvatarOffsetX, FromAvatarOffsetY, FromAvatarVideoStartSeconds, FromAvatarVideoDurationSeconds,
                     FromPublicKey, IdentityNonce, IdentitySignature, BadgeCertificateJson)
                VALUES
                    ($messageId, $fromUserId, $fromDisplayName, $toUserId, $body, $sentAtUtc, $storedAtUtc, $networkId, $fromRelayServer, $toRelayServer,
                     $intent, $fromStatus, $fromAvatarKind, $fromAvatarMediaBase64, $fromAvatarExtension, $fromAvatarScale,
                     $fromAvatarOffsetX, $fromAvatarOffsetY, $fromAvatarVideoStartSeconds, $fromAvatarVideoDurationSeconds,
                     $fromPublicKey, $identityNonce, $identitySignature, $badgeCertificateJson);
                """;
            command.Parameters.AddWithValue("$messageId", packet.MessageId.ToString());
            command.Parameters.AddWithValue("$fromUserId", packet.FromUserId);
            command.Parameters.AddWithValue("$fromDisplayName", packet.FromDisplayName);
            command.Parameters.AddWithValue("$toUserId", packet.ToUserId);
            command.Parameters.AddWithValue("$body", packet.Body);
            command.Parameters.AddWithValue("$sentAtUtc", packet.SentAtUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$storedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$networkId", (object?)packet.NetworkId ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromRelayServer", (object?)packet.FromRelayServer ?? DBNull.Value);
            command.Parameters.AddWithValue("$toRelayServer", (object?)packet.ToRelayServer ?? DBNull.Value);
            command.Parameters.AddWithValue("$intent", (object?)packet.Intent ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromStatus", (object?)packet.FromStatus ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromAvatarKind", (object?)packet.FromAvatarKind ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromAvatarMediaBase64", (object?)packet.FromAvatarMediaBase64 ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromAvatarExtension", (object?)packet.FromAvatarExtension ?? DBNull.Value);
            command.Parameters.AddWithValue("$fromAvatarScale", packet.FromAvatarScale);
            command.Parameters.AddWithValue("$fromAvatarOffsetX", packet.FromAvatarOffsetX);
            command.Parameters.AddWithValue("$fromAvatarOffsetY", packet.FromAvatarOffsetY);
            command.Parameters.AddWithValue("$fromAvatarVideoStartSeconds", packet.FromAvatarVideoStartSeconds);
            command.Parameters.AddWithValue("$fromAvatarVideoDurationSeconds", packet.FromAvatarVideoDurationSeconds);
            command.Parameters.AddWithValue("$fromPublicKey", (object?)packet.FromPublicKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$identityNonce", (object?)packet.IdentityNonce ?? DBNull.Value);
            command.Parameters.AddWithValue("$identitySignature", (object?)packet.IdentitySignature ?? DBNull.Value);
            command.Parameters.AddWithValue("$badgeCertificateJson", packet.BadgeCertificate is null ? DBNull.Value : JsonSerializer.Serialize(packet.BadgeCertificate));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ChatPacket> LoadPending(string userId)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MessageId, FromUserId, FromDisplayName, ToUserId, Body, SentAtUtc, NetworkId, FromRelayServer, ToRelayServer,
                       Intent, FromStatus, FromAvatarKind, FromAvatarMediaBase64, FromAvatarExtension, FromAvatarScale,
                       FromAvatarOffsetX, FromAvatarOffsetY, FromAvatarVideoStartSeconds, FromAvatarVideoDurationSeconds,
                       FromPublicKey, IdentityNonce, IdentitySignature, BadgeCertificateJson
                FROM PendingMessages
                WHERE ToUserId = $toUserId
                ORDER BY StoredAtUtc ASC;
                """;
            command.Parameters.AddWithValue("$toUserId", userId);
            using var reader = command.ExecuteReader();
            var rows = new List<ChatPacket>();
            while (reader.Read())
            {
                rows.Add(new ChatPacket(
                    "fluxchat.message.v1",
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    DateTimeOffset.Parse(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetDouble(14),
                    reader.GetDouble(15),
                    reader.GetDouble(16),
                    reader.GetDouble(17),
                    reader.GetDouble(18),
                    reader.IsDBNull(19) ? null : reader.GetString(19),
                    reader.IsDBNull(20) ? null : reader.GetString(20),
                    reader.IsDBNull(21) ? null : reader.GetString(21),
                    reader.IsDBNull(22) ? null : JsonSerializer.Deserialize<BadgeCertificate>(reader.GetString(22))));
            }

            return rows;
        }
    }

    public void DeletePending(Guid messageId)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PendingMessages WHERE MessageId = $messageId;";
            command.Parameters.AddWithValue("$messageId", messageId.ToString());
            command.ExecuteNonQuery();
        }
    }

    public int ClearPending(string userId)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PendingMessages WHERE ToUserId = $toUserId;";
            command.Parameters.AddWithValue("$toUserId", userId);
            return command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<PendingSummaryRow> GetPendingSummary()
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ToUserId, COUNT(*)
                FROM PendingMessages
                GROUP BY ToUserId
                ORDER BY COUNT(*) DESC;
                """;
            using var reader = command.ExecuteReader();
            var rows = new List<PendingSummaryRow>();
            while (reader.Read())
            {
                rows.Add(new PendingSummaryRow(reader.GetString(0), reader.GetInt32(1)));
            }

            return rows;
        }
    }

    public ServerStats GetStats(int onlineCount)
    {
        lock (_sync)
        {
            using var connection = OpenConnection();
            return new ServerStats(
                ServerPaths.DatabasePath,
                Count(connection, "Users"),
                Count(connection, "Invites", "UsedAtUtc IS NULL"),
                Count(connection, "PendingMessages"),
                onlineCount);
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static int Count(SqliteConnection connection, string table, string? where = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = where is null
            ? $"SELECT COUNT(*) FROM {table};"
            : $"SELECT COUNT(*) FROM {table} WHERE {where};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static UserRecord? GetUser(SqliteConnection connection, SqliteTransaction? transaction, string userId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT UserId, DisplayName, TokenHash, IsBanned FROM Users WHERE UserId = $userId;";
        command.Parameters.AddWithValue("$userId", userId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new UserRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1)
            : null;
    }

    private static InviteRow? GetInvite(SqliteConnection connection, SqliteTransaction transaction, string code)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Code, CreatedAtUtc, UsedAtUtc, UsedByUserId, Note FROM Invites WHERE Code = $code;";
        command.Parameters.AddWithValue("$code", code);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new InviteRow(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4))
            : null;
    }

    private static void UpdateLastSeen(SqliteConnection connection, SqliteTransaction transaction, string userId, string displayName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Users
            SET DisplayName = $displayName,
                LastSeenUtc = $lastSeenUtc
            WHERE UserId = $userId;
            """;
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$lastSeenUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$userId", userId);
        command.ExecuteNonQuery();
    }

    public static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedEquals(string leftHex, string rightHex)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(leftHex),
            Encoding.UTF8.GetBytes(rightHex));

    private static string CreateSecret(string prefix)
        => $"{prefix}_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_").TrimEnd('=')}";

    private static string CreateInviteCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(12);
        var text = new string(bytes.Select(x => chars[x % chars.Length]).ToArray());
        return $"FLUX-{text[..4]}-{text[4..8]}-{text[8..12]}";
    }
}

public sealed record AuthResult(bool IsAccepted, string Message, string? ClientToken)
{
    public static AuthResult Accepted(string? clientToken) => new(true, "Registered.", clientToken);
    public static AuthResult Denied(string message) => new(false, message, null);
}

public sealed record InviteRow(string Code, DateTimeOffset CreatedAtUtc, DateTimeOffset? UsedAtUtc, string? UsedByUserId, string Note);
public sealed record UserRow(string UserId, string DisplayName, bool IsBanned, DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenUtc);
public sealed record PendingSummaryRow(string UserId, int Count);
public sealed record ServerStats(string DatabasePath, int Users, int ActiveInvites, int PendingMessages, int OnlineUsers);
internal sealed record UserRecord(string UserId, string DisplayName, string TokenHash, bool IsBanned);
