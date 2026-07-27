using FluxChat.Shared;
using Microsoft.Data.Sqlite;

namespace FluxChat.Server.Core;

public static class LegacySqliteMigration
{
    public static async Task<LegacyMigrationResult> ImportAsync(AccountStore destination, string sqlitePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sqlitePath)) throw new FileNotFoundException("SQLite source database was not found.", sqlitePath);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await source.OpenAsync(cancellationToken);
        var users = await ImportUsersAsync(destination, source, cancellationToken);
        var pending = await ImportPendingAsync(destination, source, cancellationToken);
        return new LegacyMigrationResult(users, pending);
    }

    private static async Task<int> ImportUsersAsync(AccountStore destination, SqliteConnection source, CancellationToken cancellationToken)
    {
        await using var command = new SqliteCommand("SELECT UserId, DisplayName, IsBanned, CreatedAtUtc, LastSeenUtc FROM Users;", source);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            await destination.ImportLegacyUserAsync(new LegacyRelayUser(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0,
                DateTimeOffset.Parse(reader.GetString(3)), DateTimeOffset.Parse(reader.GetString(4))), cancellationToken);
            count++;
        }
        return count;
    }

    private static async Task<int> ImportPendingAsync(AccountStore destination, SqliteConnection source, CancellationToken cancellationToken)
    {
        await using var command = new SqliteCommand("""
            SELECT MessageId, FromUserId, FromDisplayName, ToUserId, Body, SentAtUtc, NetworkId, FromRelayServer, ToRelayServer,
                   Intent, FromStatus, FromAvatarKind, FromAvatarMediaBase64, FromAvatarExtension, FromAvatarScale,
                   FromAvatarOffsetX, FromAvatarOffsetY, FromAvatarVideoStartSeconds, FromAvatarVideoDurationSeconds,
                   FromPublicKey, IdentityNonce, IdentitySignature
            FROM PendingMessages;
            """, source);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var packet = new ChatPacket(
                "fluxchat.message.v1", Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5)),
                ReadString(reader, 6), ReadString(reader, 7), ReadString(reader, 8), ReadString(reader, 9), ReadString(reader, 10),
                ReadString(reader, 11), ReadString(reader, 12), ReadString(reader, 13), reader.IsDBNull(14) ? 1 : reader.GetDouble(14),
                reader.IsDBNull(15) ? 0 : reader.GetDouble(15), reader.IsDBNull(16) ? 0 : reader.GetDouble(16), reader.IsDBNull(17) ? 0 : reader.GetDouble(17),
                reader.IsDBNull(18) ? 10 : reader.GetDouble(18), ReadString(reader, 19), ReadString(reader, 20), ReadString(reader, 21));
            await destination.StorePendingPacketAsync(packet, cancellationToken);
            count++;
        }
        return count;
    }

    private static string? ReadString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public sealed record LegacyMigrationResult(int UsersImported, int PendingPacketsImported);
