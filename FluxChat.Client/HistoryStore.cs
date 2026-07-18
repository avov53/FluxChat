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
                AvatarVideoDurationSeconds REAL NOT NULL DEFAULT 10,
                IsGroup INTEGER NOT NULL DEFAULT 0,
                GroupMemberIds TEXT NOT NULL DEFAULT '',
                GroupOwnerUserId TEXT NOT NULL DEFAULT '',
                GroupVersion INTEGER NOT NULL DEFAULT 0,
                GroupIsDeleted INTEGER NOT NULL DEFAULT 0,
                GroupMembersJson TEXT NOT NULL DEFAULT '',
                VerifiedBadgeId TEXT NOT NULL DEFAULT '',
                BadgeCertificateJson TEXT NOT NULL DEFAULT '',
                IdentityPublicKey TEXT NOT NULL DEFAULT '',
                BadgeVerifiedAtUtc TEXT NULL
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
        await AddContactColumnAsync(connection, "IsGroup INTEGER NOT NULL DEFAULT 0");
        await AddContactColumnAsync(connection, "GroupMemberIds TEXT NOT NULL DEFAULT ''");
        await AddContactColumnAsync(connection, "GroupOwnerUserId TEXT NOT NULL DEFAULT ''");
        await AddContactColumnAsync(connection, "GroupVersion INTEGER NOT NULL DEFAULT 0");
        await AddContactColumnAsync(connection, "GroupIsDeleted INTEGER NOT NULL DEFAULT 0");
        await AddContactColumnAsync(connection, "GroupMembersJson TEXT NOT NULL DEFAULT ''");
        await AddContactColumnAsync(connection, "VerifiedBadgeId TEXT NOT NULL DEFAULT ''");
        await AddContactColumnAsync(connection, "BadgeCertificateJson TEXT NOT NULL DEFAULT ''");
        await AddContactColumnAsync(connection, "IdentityPublicKey TEXT NOT NULL DEFAULT ''");
        await AddContactColumnAsync(connection, "BadgeVerifiedAtUtc TEXT NULL");
        await AddMessageColumnAsync(connection, "Kind TEXT NOT NULL DEFAULT 'Text'");
        await AddMessageColumnAsync(connection, "Text TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "AttachmentPath TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "AttachmentUrl TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "ReplyToMessageId TEXT NULL");
        await AddMessageColumnAsync(connection, "ReplyPreview TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "ForwardedFrom TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "EditedAtUtc TEXT NULL");
        await AddMessageColumnAsync(connection, "ReactionsJson TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "SenderUserId TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "SenderDisplayName TEXT NOT NULL DEFAULT ''");
        await AddMessageColumnAsync(connection, "SenderAvatarKind TEXT NOT NULL DEFAULT 'color'");
        await AddMessageColumnAsync(connection, "SenderAvatarPath TEXT NOT NULL DEFAULT ''");
        await DeleteEmptyMessagesAsync(connection);
    }

    private static async Task DeleteEmptyMessagesAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
                DELETE FROM Messages
            WHERE length(trim(Body)) = 0
              AND length(trim(Text)) = 0
              AND length(trim(AttachmentPath)) = 0
              AND length(trim(AttachmentUrl)) = 0
              AND length(trim(ReplyPreview)) = 0
              AND length(trim(ForwardedFrom)) = 0
              AND length(trim(ReactionsJson)) = 0
              AND (Kind IS NULL OR Kind = '' OR Kind = 'Text');
            """;
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

    private static async Task AddMessageColumnAsync(SqliteConnection connection, string columnDefinition)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE Messages ADD COLUMN {columnDefinition};";
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
            INSERT OR REPLACE INTO Messages (
                MessageId, PeerUserId, Body, IsOutgoing, SentAtUtc, Kind, Text, AttachmentPath, AttachmentUrl,
                ReplyToMessageId, ReplyPreview, ForwardedFrom, EditedAtUtc, ReactionsJson,
                SenderUserId, SenderDisplayName, SenderAvatarKind, SenderAvatarPath)
            VALUES (
                $messageId, $peerUserId, $body, $isOutgoing, $sentAtUtc, $kind, $text, $attachmentPath, $attachmentUrl,
                $replyToMessageId, $replyPreview, $forwardedFrom, $editedAtUtc, $reactionsJson,
                $senderUserId, $senderDisplayName, $senderAvatarKind, $senderAvatarPath);
            """;
        command.Parameters.AddWithValue("$messageId", message.MessageId.ToString());
        command.Parameters.AddWithValue("$peerUserId", message.PeerUserId);
        command.Parameters.AddWithValue("$body", message.Body);
        command.Parameters.AddWithValue("$isOutgoing", message.IsOutgoing ? 1 : 0);
        command.Parameters.AddWithValue("$sentAtUtc", message.SentAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$kind", message.Kind);
        command.Parameters.AddWithValue("$text", message.Text);
        command.Parameters.AddWithValue("$attachmentPath", message.AttachmentPath);
        command.Parameters.AddWithValue("$attachmentUrl", message.AttachmentUrl);
        command.Parameters.AddWithValue("$replyToMessageId", message.ReplyToMessageId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$replyPreview", message.ReplyPreview);
        command.Parameters.AddWithValue("$forwardedFrom", message.ForwardedFrom);
        command.Parameters.AddWithValue("$editedAtUtc", message.EditedAtUtc?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$reactionsJson", message.ReactionsJson);
        command.Parameters.AddWithValue("$senderUserId", message.SenderUserId);
        command.Parameters.AddWithValue("$senderDisplayName", message.SenderDisplayName);
        command.Parameters.AddWithValue("$senderAvatarKind", message.SenderAvatarKind);
        command.Parameters.AddWithValue("$senderAvatarPath", message.SenderAvatarPath);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateMessageAsync(MessageViewModel message)
        => await SaveAsync(message);

    public async Task DeleteMessageAsync(Guid messageId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Messages
            WHERE MessageId = $messageId;
            """;
        command.Parameters.AddWithValue("$messageId", messageId.ToString());

        await command.ExecuteNonQueryAsync();
    }

    public async Task<string> LoadMessageAttachmentPathAsync(Guid messageId, string peerUserId, bool isOutgoing)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttachmentPath
            FROM Messages
            WHERE MessageId = $messageId
              AND PeerUserId = $peerUserId
              AND IsOutgoing = $isOutgoing
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        command.Parameters.AddWithValue("$peerUserId", peerUserId);
        command.Parameters.AddWithValue("$isOutgoing", isOutgoing ? 1 : 0);

        var result = await command.ExecuteScalarAsync();
        return result as string ?? "";
    }

    public async Task DeleteIncomingMessageAsync(Guid messageId, string peerUserId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Messages
            WHERE MessageId = $messageId
              AND PeerUserId = $peerUserId
              AND IsOutgoing = 0;
            """;
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        command.Parameters.AddWithValue("$peerUserId", peerUserId);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<MessageViewModel>> LoadConversationAsync(string peerUserId)
    {
        var messages = new List<MessageViewModel>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MessageId, PeerUserId, Body, IsOutgoing, SentAtUtc, Kind, Text, AttachmentPath, AttachmentUrl,
                   ReplyToMessageId, ReplyPreview, ForwardedFrom, EditedAtUtc, ReactionsJson,
                   SenderUserId, SenderDisplayName, SenderAvatarKind, SenderAvatarPath
            FROM Messages
            WHERE PeerUserId = $peerUserId
            ORDER BY SentAtUtc ASC;
            """;
        command.Parameters.AddWithValue("$peerUserId", peerUserId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var body = reader.GetString(2);
            var text = reader.IsDBNull(6) ? "" : reader.GetString(6);
            var editedAt = reader.IsDBNull(12)
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(reader.GetString(12));
            messages.Add(new MessageViewModel
            {
                MessageId = Guid.Parse(reader.GetString(0)),
                PeerUserId = reader.GetString(1),
                Body = body,
                IsOutgoing = reader.GetInt32(3) == 1,
                SentAtUtc = DateTimeOffset.Parse(reader.GetString(4)),
                Kind = reader.IsDBNull(5) ? MessageKinds.Text : reader.GetString(5),
                Text = string.IsNullOrWhiteSpace(text) ? body : text,
                AttachmentPath = reader.IsDBNull(7) ? "" : reader.GetString(7),
                AttachmentUrl = reader.IsDBNull(8) ? "" : reader.GetString(8),
                ReplyToMessageId = reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9)),
                ReplyPreview = reader.IsDBNull(10) ? "" : reader.GetString(10),
                ForwardedFrom = reader.IsDBNull(11) ? "" : reader.GetString(11),
                EditedAtUtc = editedAt,
                ReactionsJson = reader.IsDBNull(13) ? "" : reader.GetString(13),
                SenderUserId = reader.IsDBNull(14) ? "" : reader.GetString(14),
                SenderDisplayName = reader.IsDBNull(15) ? "" : reader.GetString(15),
                SenderAvatarKind = reader.IsDBNull(16) ? "color" : reader.GetString(16),
                SenderAvatarPath = reader.IsDBNull(17) ? "" : reader.GetString(17)
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
                                  AvatarScale, AvatarOffsetX, AvatarOffsetY, AvatarVideoStartSeconds, AvatarVideoDurationSeconds,
                                  IsGroup, GroupMemberIds, GroupOwnerUserId, GroupVersion, GroupIsDeleted, GroupMembersJson,
                                  VerifiedBadgeId, BadgeCertificateJson, IdentityPublicKey, BadgeVerifiedAtUtc)
            VALUES ($userId, $displayName, $ipAddress, $messagePort, $status, $lastSeenUtc, $avatarKind, $avatarPath,
                    $avatarScale, $avatarOffsetX, $avatarOffsetY, $avatarVideoStartSeconds, $avatarVideoDurationSeconds,
                    $isGroup, $groupMemberIds, $groupOwnerUserId, $groupVersion, $groupIsDeleted, $groupMembersJson,
                    $verifiedBadgeId, $badgeCertificateJson, $identityPublicKey, $badgeVerifiedAtUtc)
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
                AvatarVideoDurationSeconds = excluded.AvatarVideoDurationSeconds,
                IsGroup = excluded.IsGroup,
                GroupMemberIds = excluded.GroupMemberIds,
                GroupOwnerUserId = excluded.GroupOwnerUserId,
                GroupVersion = excluded.GroupVersion,
                GroupIsDeleted = excluded.GroupIsDeleted,
                GroupMembersJson = excluded.GroupMembersJson,
                VerifiedBadgeId = excluded.VerifiedBadgeId,
                BadgeCertificateJson = excluded.BadgeCertificateJson,
                IdentityPublicKey = excluded.IdentityPublicKey,
                BadgeVerifiedAtUtc = excluded.BadgeVerifiedAtUtc;
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
        command.Parameters.AddWithValue("$isGroup", contact.IsGroup ? 1 : 0);
        command.Parameters.AddWithValue("$groupMemberIds", contact.GroupMemberIds);
        command.Parameters.AddWithValue("$groupOwnerUserId", contact.GroupOwnerUserId);
        command.Parameters.AddWithValue("$groupVersion", contact.GroupVersion);
        command.Parameters.AddWithValue("$groupIsDeleted", contact.GroupIsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$groupMembersJson", contact.GroupMembersJson);
        command.Parameters.AddWithValue("$verifiedBadgeId", contact.VerifiedBadgeId);
        command.Parameters.AddWithValue("$badgeCertificateJson", contact.BadgeCertificateJson);
        command.Parameters.AddWithValue("$identityPublicKey", contact.IdentityPublicKey);
        command.Parameters.AddWithValue("$badgeVerifiedAtUtc", contact.BadgeVerifiedAtUtc?.ToString("O") ?? (object)DBNull.Value);

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
                   AvatarScale, AvatarOffsetX, AvatarOffsetY, AvatarVideoStartSeconds, AvatarVideoDurationSeconds,
                   IsGroup, GroupMemberIds, GroupOwnerUserId, GroupVersion, GroupIsDeleted, GroupMembersJson,
                   VerifiedBadgeId, BadgeCertificateJson, IdentityPublicKey, BadgeVerifiedAtUtc
            FROM Contacts
            WHERE GroupIsDeleted = 0
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
                AvatarVideoDurationSeconds = reader.GetDouble(12),
                IsGroup = reader.GetInt32(13) == 1,
                GroupMemberIds = reader.GetString(14),
                GroupOwnerUserId = reader.IsDBNull(15) ? "" : reader.GetString(15),
                GroupVersion = reader.IsDBNull(16) ? 0 : reader.GetInt64(16),
                GroupIsDeleted = !reader.IsDBNull(17) && reader.GetInt32(17) == 1,
                GroupMembersJson = reader.IsDBNull(18) ? "" : reader.GetString(18),
                VerifiedBadgeId = reader.IsDBNull(19) ? "" : reader.GetString(19),
                BadgeCertificateJson = reader.IsDBNull(20) ? "" : reader.GetString(20),
                IdentityPublicKey = reader.IsDBNull(21) ? "" : reader.GetString(21),
                BadgeVerifiedAtUtc = reader.IsDBNull(22) ? null : DateTimeOffset.Parse(reader.GetString(22))
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
