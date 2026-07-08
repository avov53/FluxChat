using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluxChat.Server.Core;
using FluxChat.Shared;

var database = new RelayDatabase();
database.Initialize();

var server = new RelayServer(database);
await server.RunAsync();

internal sealed class RelayServer
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly TimeSpan FederationClockSkew = TimeSpan.FromMinutes(5);
    private readonly RelayDatabase _database;
    private readonly ConcurrentDictionary<string, ClientSession> _online = new();
    private readonly string? _federationKey = Environment.GetEnvironmentVariable("FLUXCHAT_FEDERATION_KEY");

    public RelayServer(RelayDatabase database)
    {
        _database = database;
    }

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Any, FluxChatPorts.Relay);
        listener.Start();
        Console.WriteLine($"FluxChat relay listening on TCP {FluxChatPorts.Relay}");
        Console.WriteLine($"Database: {ServerPaths.DatabasePath}");
        Console.WriteLine(string.IsNullOrWhiteSpace(_federationKey)
            ? "Federation disabled. Set FLUXCHAT_FEDERATION_KEY to enable server-to-server delivery."
            : "Federation enabled.");
        Console.WriteLine("Use `fluxus` on the VPS to create invites and manage users.");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        ClientSession? session = null;
        var remote = client.Client.RemoteEndPoint;

        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Utf8NoBom, leaveOpen: true);
            using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true)
            {
                AutoFlush = true
            };

            var firstLine = await reader.ReadLineAsync();
            var firstType = GetPacketType(firstLine);
            if (firstType == "fluxchat.relay-federation.v1")
            {
                var federation = ReadPacket<RelayFederationPacket>(firstLine, "fluxchat.relay-federation.v1")
                    ?? throw new InvalidOperationException("Invalid federation packet.");
                await RouteFederatedMessageAsync(federation, remote);
                return;
            }

            var register = ReadPacket<RelayRegisterPacket>(firstLine, "fluxchat.relay-register.v1")
                ?? throw new InvalidOperationException("First packet must be relay registration.");

            var auth = _database.Authenticate(register.UserId, register.DisplayName, register.Credential);
            if (!auth.IsAccepted)
            {
                Console.WriteLine($"Rejected {remote}: {auth.Message}");
                await SendAsync(writer, RelayAckPacket.Denied(auth.Message));
                return;
            }

            await SendAsync(writer, RelayAckPacket.AcceptedResult(auth.Message, auth.ClientToken));

            session = new ClientSession(register.UserId, register.DisplayName, writer);
            _online[register.UserId] = session;
            Console.WriteLine($"{register.DisplayName} ({register.UserId}) connected from {remote}");
            await SendPresenceSnapshotAsync(session);
            await BroadcastPresenceAsync(session, "Online");
            await FlushPendingAsync(register.UserId, session);

            while (client.Connected)
            {
                var line = await reader.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                var type = GetPacketType(line);
                if (type == "fluxchat.relay-presence.v1")
                {
                    var presence = ReadPacket<RelayPresencePacket>(line, "fluxchat.relay-presence.v1");
                    if (presence is not null)
                    {
                        session.DisplayName = presence.DisplayName;
                        session.Status = presence.Status;
                        session.AvatarKind = presence.AvatarKind;
                        session.AvatarMediaBase64 = presence.AvatarMediaBase64;
                        session.AvatarExtension = presence.AvatarExtension;
                        session.AvatarScale = presence.AvatarScale;
                        session.AvatarOffsetX = presence.AvatarOffsetX;
                        session.AvatarOffsetY = presence.AvatarOffsetY;
                        session.AvatarVideoStartSeconds = presence.AvatarVideoStartSeconds;
                        session.AvatarVideoDurationSeconds = presence.AvatarVideoDurationSeconds;
                        session.LastPresenceUtc = DateTimeOffset.UtcNow;
                        await BroadcastPresenceAsync(session, presence.Status);
                    }

                    continue;
                }

                if (type == "fluxchat.message.v1")
                {
                    var packet = ReadPacket<ChatPacket>(line, "fluxchat.message.v1");
                    if (packet is not null)
                    {
                        await RouteMessageAsync(packet);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client {remote} disconnected: {ex.Message}");
        }
        finally
        {
            if (session is not null)
            {
                _online.TryRemove(new KeyValuePair<string, ClientSession>(session.UserId, session));
                Console.WriteLine($"{session.DisplayName} ({session.UserId}) offline");
                await BroadcastPresenceAsync(session, "Offline");
            }

            client.Dispose();
        }
    }

    private async Task RouteMessageAsync(ChatPacket packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.ToRelayServer))
        {
            await ForwardToRelayAsync(packet);
            return;
        }

        if (_online.TryGetValue(packet.ToUserId, out var recipient))
        {
            try
            {
                await recipient.SendAsync(packet);
                Console.WriteLine($"Delivered {packet.MessageId} from {packet.FromUserId} to {packet.ToUserId}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Delivery failed, queued {packet.MessageId}: {ex.Message}");
            }
        }

        if (packet.Intent is "presence" or "call-audio")
        {
            Console.WriteLine($"Dropped offline {packet.Intent} from {packet.FromUserId} to {packet.ToUserId}");
            return;
        }

        _database.StorePending(packet);
        Console.WriteLine($"Queued {packet.MessageId} for {packet.ToUserId}");
    }

    private async Task SendPresenceSnapshotAsync(ClientSession recipient)
    {
        foreach (var session in _online.Values)
        {
            if (session.UserId == recipient.UserId)
            {
                continue;
            }

            try
            {
                await recipient.SendAsync(CreatePresencePacket(session, session.Status));
            }
            catch
            {
            }
        }
    }

    private async Task BroadcastPresenceAsync(ClientSession changed, string status)
    {
        var packet = CreatePresencePacket(changed, status);
        foreach (var session in _online.Values)
        {
            if (session.UserId == changed.UserId)
            {
                continue;
            }

            try
            {
                await session.SendAsync(packet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Presence delivery failed to {session.UserId}: {ex.Message}");
            }
        }
    }

    private async Task RouteFederatedMessageAsync(RelayFederationPacket federation, EndPoint? remote)
    {
        if (!IsFederationAccepted(federation))
        {
            Console.WriteLine($"Rejected federation packet from {remote}");
            return;
        }

        var localPacket = federation.Message with { ToRelayServer = null };
        Console.WriteLine($"Federated message {localPacket.MessageId} accepted from {remote}: {localPacket.FromUserId} -> {localPacket.ToUserId}");
        await RouteMessageAsync(localPacket);
    }

    private async Task ForwardToRelayAsync(ChatPacket packet)
    {
        var targetServer = packet.ToRelayServer?.Trim();
        if (string.IsNullOrWhiteSpace(targetServer))
        {
            Console.WriteLine($"Federation skipped for {packet.MessageId}: target relay missing");
            return;
        }

        if (string.IsNullOrWhiteSpace(_federationKey))
        {
            Console.WriteLine($"Federation disabled, cannot forward {packet.MessageId} to {targetServer}");
            return;
        }

        var forwarded = packet with { ToRelayServer = null };
        try
        {
            var endpoint = ParseEndpoint(targetServer);
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await client.ConnectAsync(endpoint.host, endpoint.port, timeout.Token);

            await using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true)
            {
                AutoFlush = true
            };

            var sentAtUtc = DateTimeOffset.UtcNow;
            var signature = CreateFederationSignature(forwarded, sentAtUtc, _federationKey);
            await SendAsync(writer, RelayFederationPacket.Create(forwarded, sentAtUtc, signature));
            Console.WriteLine($"Federated {packet.MessageId} from {packet.FromUserId} to {packet.ToUserId} via {targetServer}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Federation failed for {packet.MessageId} via {targetServer}: {ex.Message}");
        }
    }

    private async Task FlushPendingAsync(string userId, ClientSession session)
    {
        var messages = _database.LoadPending(userId);
        var count = 0;
        foreach (var packet in messages)
        {
            await session.SendAsync(packet);
            _database.DeletePending(packet.MessageId);
            count++;
        }

        if (count > 0)
        {
            Console.WriteLine($"Flushed {count} pending messages for {userId}");
        }
    }

    private static T? ReadPacket<T>(string? json, string expectedType)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString()
            : null;

        return type == expectedType ? JsonSerializer.Deserialize<T>(json) : default;
    }

    private bool IsFederationAccepted(RelayFederationPacket federation)
    {
        if (string.IsNullOrWhiteSpace(_federationKey))
        {
            return false;
        }

        var age = DateTimeOffset.UtcNow - federation.SentAtUtc.ToUniversalTime();
        if (age.Duration() > FederationClockSkew)
        {
            return false;
        }

        var expected = CreateFederationSignature(federation.Message, federation.SentAtUtc, _federationKey);
        return FixedEquals(expected, federation.Signature);
    }

    private static string CreateFederationSignature(ChatPacket message, DateTimeOffset sentAtUtc, string federationKey)
    {
        var payload = $"{sentAtUtc.UtcDateTime:O}\n{JsonSerializer.Serialize(message)}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(federationKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool FixedEquals(string leftHex, string rightHex)
    {
        var left = Encoding.UTF8.GetBytes(leftHex);
        var right = Encoding.UTF8.GetBytes(rightHex);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string? GetPacketType(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString()
            : null;
    }

    private static (string host, int port) ParseEndpoint(string serverAddress)
    {
        var value = serverAddress.Trim();
        var parts = value.Split(':', 2);
        if (parts.Length == 1)
        {
            return (parts[0], FluxChatPorts.Relay);
        }

        return int.TryParse(parts[1], out var port) && port > 0
            ? (parts[0], port)
            : throw new InvalidOperationException("Server port is invalid.");
    }

    private static Task SendAsync<T>(StreamWriter writer, T packet)
        => writer.WriteLineAsync(JsonSerializer.Serialize(packet));

    private static RelayPresencePacket CreatePresencePacket(ClientSession session, string status)
        => RelayPresencePacket.Create(
            session.UserId,
            session.DisplayName,
            status,
            session.AvatarKind,
            session.AvatarMediaBase64,
            session.AvatarExtension,
            session.AvatarScale,
            session.AvatarOffsetX,
            session.AvatarOffsetY,
            session.AvatarVideoStartSeconds,
            session.AvatarVideoDurationSeconds);
}

internal sealed class ClientSession(string userId, string displayName, StreamWriter writer)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public string UserId { get; } = userId;
    public string DisplayName { get; set; } = displayName;
    public StreamWriter Writer { get; } = writer;
    public string Status { get; set; } = "Online";
    public string? AvatarKind { get; set; }
    public string? AvatarMediaBase64 { get; set; }
    public string? AvatarExtension { get; set; }
    public double AvatarScale { get; set; } = 1;
    public double AvatarOffsetX { get; set; }
    public double AvatarOffsetY { get; set; }
    public double AvatarVideoStartSeconds { get; set; }
    public double AvatarVideoDurationSeconds { get; set; } = 10;
    public DateTimeOffset LastPresenceUtc { get; set; } = DateTimeOffset.UtcNow;

    public async Task SendAsync<T>(T packet)
    {
        await _sendLock.WaitAsync();
        try
        {
            await Writer.WriteLineAsync(JsonSerializer.Serialize(packet));
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
