using Microsoft.Data.Sqlite;

namespace FluxChat.Client;

internal sealed class HistoryStore
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = AppPaths.HistoryPath
    }.ToString();

    public async Task InitializeAsync()
    {
        AppPaths.EnsureCreated();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Messages (
                MessageId TEXT PRIMARY KEY,
                PeerUserId TEXT NOT NULL,
                Body TEXT NOT NULL,
                IsOutgoing INTEGER NOT NULL,
                SentAtUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Messages_PeerUserId_SentAtUtc
            ON Messages (PeerUserId, SentAtUtc);

            CREATE TABLE IF NOT EXISTS Contacts (
                UserId TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                IpAddress TEXT NOT NULL,
                MessagePort INTEGER NOT NULL,
                Status TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL,
                AvatarKind TEXT NOT NULL DEFAULT 'color',
                AvatarPath TEXT NOT NULL DEFAULT '',
                AvatarScale REAL NOT NULL DEFAULT 1,
                AvatarOffsetX REAL NOT NULL DEFAULT 0,
                AvatarOffsetY REAL NOT NULL DEFAULT 0,
                AvatarVideoStartSeconds REAL NOT NULL DEFAULT 0,
                AvatarVideoDurationSeconds REAL NOT NULL DEFAULT 10
            );
            """;

        await command.ExecuteNonQueryAsync();
        await AddContactColumnAsync(connection, "AvatarKind TEXT NOT NULL DEFAULT 'color'");
        await AddContactColumnAsync(connection, "AvatarPath TEXT NOT NULL DEFAULT ''");
        await AddContactColumnAsync(connection, "AvatarScale REAL NOT NULL DEFAULT 1");
        await AddContactColumnAsync(connection, "AvatarOffsetX REAL NOT NULL DEFAULT 0");
        await AddContactColumnAsync(connection, "AvatarOffsetY REAL NOT NULL DEFAULT 0");
        await AddContactColumnAsync(connection, "AvatarVideoStartSeconds REAL NOT NULL DEFAULT 0");
        await AddContactColumnAsync(connection, "AvatarVideoDurationSeconds REAL NOT NULL DEFAULT 10");
        await DeleteEmptyMessagesAsync(connection);
    }

    private static async Task DeleteEmptyMessagesAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Messages WHERE length(trim(Body)) = 0;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddContactColumnAsync(SqliteConnection connection, string columnDefinition)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE Contacts ADD COLUMN {columnDefinition};";
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
        }
    }

    public async Task SaveAsync(MessageViewModel message)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO Messages (MessageId, PeerUserId, Body, IsOutgoing, SentAtUtc)
            VALUES ($messageId, $peerUserId, $body, $isOutgoing, $sentAtUtc);
            """;
        command.Parameters.AddWithValue("$messageId", message.MessageId.ToString());
        command.Parameters.AddWithValue("$peerUserId", message.PeerUserId);
        command.Parameters.AddWithValue("$body", message.Body);
        command.Parameters.AddWithValue("$isOutgoing", message.IsOutgoing ? 1 : 0);
        command.Parameters.AddWithValue("$sentAtUtc", message.SentAtUtc.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<MessageViewModel>> LoadConversationAsync(string peerUserId)
    {
        var messages = new List<MessageViewModel>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MessageId, PeerUserId, Body, IsOutgoing, SentAtUtc
            FROM Messages
            WHERE PeerUserId = $peerUserId
            ORDER BY SentAtUtc ASC;
            """;
        command.Parameters.AddWithValue("$peerUserId", peerUserId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(new MessageViewModel
            {
                MessageId = Guid.Parse(reader.GetString(0)),
                PeerUserId = reader.GetString(1),
                Body = reader.GetString(2),
                IsOutgoing = reader.GetInt32(3) == 1,
                SentAtUtc = DateTimeOffset.Parse(reader.GetString(4))
            });
        }

        return messages;
    }

    public async Task SaveContactAsync(ContactViewModel contact)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Contacts (UserId, DisplayName, IpAddress, MessagePort, Status, LastSeenUtc, AvatarKind, AvatarPath,
                                  AvatarScale, AvatarOffsetX, AvatarOffsetY, AvatarVideoStartSeconds, AvatarVideoDurationSeconds)
            VALUES ($userId, $displayName, $ipAddress, $messagePort, $status, $lastSeenUtc, $avatarKind, $avatarPath,
                    $avatarScale, $avatarOffsetX, $avatarOffsetY, $avatarVideoStartSeconds, $avatarVideoDurationSeconds)
            ON CONFLICT(UserId) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                IpAddress = excluded.IpAddress,
                MessagePort = excluded.MessagePort,
                Status = excluded.Status,
                LastSeenUtc = excluded.LastSeenUtc,
                AvatarKind = excluded.AvatarKind,
                AvatarPath = excluded.AvatarPath,
                AvatarScale = excluded.AvatarScale,
                AvatarOffsetX = excluded.AvatarOffsetX,
                AvatarOffsetY = excluded.AvatarOffsetY,
                AvatarVideoStartSeconds = excluded.AvatarVideoStartSeconds,
                AvatarVideoDurationSeconds = excluded.AvatarVideoDurationSeconds;
            """;
        command.Parameters.AddWithValue("$userId", contact.UserId);
        command.Parameters.AddWithValue("$displayName", contact.DisplayName);
        command.Parameters.AddWithValue("$ipAddress", contact.IpAddress);
        command.Parameters.AddWithValue("$messagePort", contact.MessagePort);
        command.Parameters.AddWithValue("$status", contact.Status.ToString());
        command.Parameters.AddWithValue("$lastSeenUtc", contact.LastSeenUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$avatarKind", contact.AvatarKind);
        command.Parameters.AddWithValue("$avatarPath", contact.AvatarPath);
        command.Parameters.AddWithValue("$avatarScale", contact.AvatarScale);
        command.Parameters.AddWithValue("$avatarOffsetX", contact.AvatarOffsetX);
        command.Parameters.AddWithValue("$avatarOffsetY", contact.AvatarOffsetY);
        command.Parameters.AddWithValue("$avatarVideoStartSeconds", contact.AvatarVideoStartSeconds);
        command.Parameters.AddWithValue("$avatarVideoDurationSeconds", contact.AvatarVideoDurationSeconds);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ContactViewModel>> LoadContactsAsync()
    {
        var contacts = new List<ContactViewModel>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UserId, DisplayName, IpAddress, MessagePort, Status, LastSeenUtc, AvatarKind, AvatarPath,
                   AvatarScale, AvatarOffsetX, AvatarOffsetY, AvatarVideoStartSeconds, AvatarVideoDurationSeconds
            FROM Contacts
            ORDER BY DisplayName COLLATE NOCASE ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var status = Enum.TryParse<UserPresenceStatus>(reader.GetString(4), out var parsedStatus)
                ? parsedStatus
                : UserPresenceStatus.Offline;

            contacts.Add(new ContactViewModel
            {
                UserId = reader.GetString(0),
                DisplayName = reader.GetString(1),
                IpAddress = reader.GetString(2),
                MessagePort = reader.GetInt32(3),
                Status = status,
                LastSeenUtc = DateTimeOffset.Parse(reader.GetString(5)),
                AvatarKind = reader.GetString(6),
                AvatarPath = reader.GetString(7),
                AvatarScale = reader.GetDouble(8),
                AvatarOffsetX = reader.GetDouble(9),
                AvatarOffsetY = reader.GetDouble(10),
                AvatarVideoStartSeconds = reader.GetDouble(11),
                AvatarVideoDurationSeconds = reader.GetDouble(12)
            });
        }

        return contacts;
    }

    public async Task DeleteContactAsync(string userId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Contacts
            WHERE UserId = $userId;
            """;
        command.Parameters.AddWithValue("$userId", userId);

        await command.ExecuteNonQueryAsync();
    }
}
