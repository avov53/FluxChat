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
    private static readonly TimeSpan TurnCredentialLifetime = TimeSpan.FromHours(24);
    private readonly RelayDatabase _database;
    private readonly ConcurrentDictionary<string, ClientSession> _online = new();
    private readonly string? _federationKey = Environment.GetEnvironmentVariable("FLUXCHAT_FEDERATION_KEY");
    private readonly string? _turnHost = Environment.GetEnvironmentVariable("FLUXCHAT_TURN_HOST");
    private readonly string? _turnSecret = Environment.GetEnvironmentVariable("FLUXCHAT_TURN_SECRET");
    private readonly string _turnRealm = Environment.GetEnvironmentVariable("FLUXCHAT_TURN_REALM") ?? "fluxchat";
    private readonly int _turnPort = ParseEnvInt("FLUXCHAT_TURN_PORT", 3478);

    public RelayServer(RelayDatabase database)
    {
        _database = database;
    }

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Any, FluxChatPorts.Relay);
        using var audioUdp = new UdpClient(FluxChatPorts.Relay);
        listener.Start();
        _ = Task.Run(SweepStaleSessionsAsync);
        _ = Task.Run(() => RunAudioUdpRelayAsync(audioUdp));
        Console.WriteLine($"FluxChat relay listening on TCP {FluxChatPorts.Relay}");
        Console.WriteLine($"FluxChat call audio relay listening on UDP {FluxChatPorts.Relay}");
        Console.WriteLine($"Database: {ServerPaths.DatabasePath}");
        Console.WriteLine(string.IsNullOrWhiteSpace(_federationKey)
            ? "Federation disabled. Set FLUXCHAT_FEDERATION_KEY to enable server-to-server delivery."
            : "Federation enabled.");
        Console.WriteLine(IsTurnEnabled()
            ? $"TURN enabled: {_turnHost}:{_turnPort}, realm={_turnRealm}"
            : "TURN disabled. Set FLUXCHAT_TURN_HOST and FLUXCHAT_TURN_SECRET to enable WebRTC relay.");
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
            client.NoDelay = true;
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

            if (firstType == "fluxchat.audio-register.v1")
            {
                var audioRegister = ReadPacket<RelayAudioRegisterPacket>(firstLine, "fluxchat.audio-register.v1")
                    ?? throw new InvalidOperationException("Invalid audio registration packet.");
                await HandleAudioClientAsync(audioRegister, reader, writer, client, remote);
                return;
            }

            if (firstType == "fluxchat.screen-register.v1")
            {
                var screenRegister = ReadPacket<RelayScreenRegisterPacket>(firstLine, "fluxchat.screen-register.v1")
                    ?? throw new InvalidOperationException("Invalid screen registration packet.");
                await HandleScreenClientAsync(screenRegister, reader, writer, client, remote);
                return;
            }

            var register = ReadPacket<RelayRegisterPacket>(firstLine, "fluxchat.relay-register.v1")
                ?? throw new InvalidOperationException("First packet must be relay registration.");

            if (!VerifyRegistrationIdentity(register))
            {
                await SendAsync(writer, RelayAckPacket.Denied("Identity signature is invalid."));
                return;
            }

            var auth = _database.Authenticate(register.UserId, register.DisplayName, register.Credential);
            if (!auth.IsAccepted)
            {
                Console.WriteLine($"Rejected {remote}: {auth.Message}");
                await SendAsync(writer, RelayAckPacket.Denied(auth.Message));
                return;
            }

            await SendAsync(writer, RelayAckPacket.AcceptedResult(auth.Message, auth.ClientToken, CreateIceConfig(register.UserId)));

            session = new ClientSession(register.UserId, register.DisplayName, writer, client)
            {
                PublicKey = register.PublicKey
            };
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
                        if (!VerifyPresenceIdentity(presence, session))
                        {
                            Console.WriteLine($"Rejected invalid signed presence from {session.UserId}");
                            continue;
                        }

                        session.DisplayName = presence.DisplayName;
                        session.Status = presence.Status;
                        if (!string.IsNullOrWhiteSpace(presence.AvatarKind))
                        {
                            session.AvatarKind = presence.AvatarKind;
                            session.AvatarScale = presence.AvatarScale;
                            session.AvatarOffsetX = presence.AvatarOffsetX;
                            session.AvatarOffsetY = presence.AvatarOffsetY;
                            session.AvatarVideoStartSeconds = presence.AvatarVideoStartSeconds;
                            session.AvatarVideoDurationSeconds = presence.AvatarVideoDurationSeconds;
                        }

                        if (!string.IsNullOrWhiteSpace(presence.AvatarMediaBase64))
                        {
                            session.AvatarMediaBase64 = presence.AvatarMediaBase64;
                            session.AvatarExtension = presence.AvatarExtension;
                        }

                        session.LastPresenceUtc = DateTimeOffset.UtcNow;
                        session.LatestPresence = presence;
                        await BroadcastPresenceAsync(session, presence.Status, presence);
                    }

                    continue;
                }

                if (type == "fluxchat.message.v1")
                {
                    var packet = ReadPacket<ChatPacket>(line, "fluxchat.message.v1");
                    if (packet is not null)
                    {
                        if (!VerifyChatIdentity(packet, session))
                        {
                            Console.WriteLine($"Rejected invalid signed message {packet.MessageId} from {session.UserId}");
                            continue;
                        }
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
                if (_online.TryRemove(new KeyValuePair<string, ClientSession>(session.UserId, session)))
                {
                    Console.WriteLine($"{session.DisplayName} ({session.UserId}) offline");
                    await BroadcastPresenceAsync(session, "Offline");
                }
            }

            client.Dispose();
        }
    }

    private async Task HandleAudioClientAsync(
        RelayAudioRegisterPacket register,
        StreamReader reader,
        StreamWriter writer,
        TcpClient client,
        EndPoint? remote)
    {
        ClientSession? session = null;
        try
        {
            var auth = _database.Authenticate(register.UserId, register.DisplayName, register.Credential);
            if (!auth.IsAccepted)
            {
                Console.WriteLine($"Rejected audio {remote}: {auth.Message}");
                await SendAsync(writer, RelayAckPacket.Denied(auth.Message));
                return;
            }

            if (!_online.TryGetValue(register.UserId, out session))
            {
                Console.WriteLine($"Rejected audio {remote}: primary session missing for {register.UserId}");
                await SendAsync(writer, RelayAckPacket.Denied("Primary session is not connected."));
                return;
            }

            session.SetAudioWriter(writer);
            await SendAsync(writer, RelayAckPacket.AcceptedResult("Audio registered."));
            Console.WriteLine($"{register.DisplayName} ({register.UserId}) audio connected from {remote}");

            while (client.Connected)
            {
                var line = await reader.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                var packet = ReadPacket<RelayAudioPacket>(line, "fluxchat.call-audio.v1");
                if (packet is null || packet.FromUserId != session.UserId)
                {
                    continue;
                }

                session.LastPresenceUtc = DateTimeOffset.UtcNow;
                await RouteAudioPacketAsync(packet);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio client {remote} disconnected: {ex.Message}");
        }
        finally
        {
            session?.ClearAudioWriter(writer);
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

        if (packet.Intent == "call-screen-frame")
        {
            Console.WriteLine($"Dropped control-channel screen frame from {packet.FromUserId} to {packet.ToUserId}");
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

        if (IsTransientMessage(packet))
        {
            Console.WriteLine($"Dropped offline {packet.Intent} from {packet.FromUserId} to {packet.ToUserId}");
            return;
        }

        _database.StorePending(packet);
        Console.WriteLine($"Queued {packet.MessageId} for {packet.ToUserId}");
    }

    private RelayIceConfig CreateIceConfig(string userId)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(TurnCredentialLifetime);
        var servers = new List<RelayIceServer>
        {
            new(new[] { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" })
        };

        if (IsTurnEnabled())
        {
            var host = _turnHost!.Trim();
            var username = $"{expiresAt.ToUnixTimeSeconds()}:{userId}";
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_turnSecret!));
            var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));

            servers.Add(new(new[] { $"stun:{host}:{_turnPort}" }));
            servers.Add(new(
                new[]
                {
                    $"turn:{host}:{_turnPort}?transport=udp",
                    $"turn:{host}:{_turnPort}?transport=tcp"
                },
                username,
                credential));
        }

        return new RelayIceConfig(servers, expiresAt);
    }

    private bool IsTurnEnabled()
        => !string.IsNullOrWhiteSpace(_turnHost) &&
            !string.IsNullOrWhiteSpace(_turnSecret);

    private static int ParseEnvInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;

    private async Task RouteAudioPacketAsync(RelayAudioPacket packet)
    {
        if (_online.TryGetValue(packet.ToUserId, out var recipient))
        {
            try
            {
                await recipient.SendAudioAsync(packet);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TCP audio delivery failed from {packet.FromUserId} to {packet.ToUserId}: {ex.Message}");
            }
        }
    }

    private async Task HandleScreenClientAsync(
        RelayScreenRegisterPacket register,
        StreamReader reader,
        StreamWriter writer,
        TcpClient client,
        EndPoint? remote)
    {
        ClientSession? session = null;
        try
        {
            var auth = _database.Authenticate(register.UserId, register.DisplayName, register.Credential);
            if (!auth.IsAccepted)
            {
                Console.WriteLine($"Rejected screen {remote}: {auth.Message}");
                await SendAsync(writer, RelayAckPacket.Denied(auth.Message));
                return;
            }

            if (!_online.TryGetValue(register.UserId, out session))
            {
                Console.WriteLine($"Rejected screen {remote}: primary session missing for {register.UserId}");
                await SendAsync(writer, RelayAckPacket.Denied("Primary session is not connected."));
                return;
            }

            session.SetScreenWriter(writer);
            await SendAsync(writer, RelayAckPacket.AcceptedResult("Screen registered."));
            Console.WriteLine($"{register.DisplayName} ({register.UserId}) screen connected from {remote}");

            while (client.Connected)
            {
                var line = await reader.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                var packet = ReadPacket<RelayScreenFramePacket>(line, "fluxchat.screen-frame.v1");
                if (packet is null || packet.FromUserId != session.UserId)
                {
                    continue;
                }

                session.LastPresenceUtc = DateTimeOffset.UtcNow;
                await RouteScreenPacketAsync(packet);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Screen client {remote} disconnected: {ex.Message}");
        }
        finally
        {
            session?.ClearScreenWriter(writer);
            client.Dispose();
        }
    }

    private Task RouteScreenPacketAsync(RelayScreenFramePacket packet)
    {
        if (_online.TryGetValue(packet.ToUserId, out var recipient))
        {
            recipient.EnqueueScreenFrame(packet);
        }

        return Task.CompletedTask;
    }

    private async Task RunAudioUdpRelayAsync(UdpClient udp)
    {
        var receivedPackets = 0L;
        var forwardedPackets = 0L;
        var missingSenderPackets = 0L;
        var missingRecipientEndpointPackets = 0L;

        while (true)
        {
            try
            {
                var result = await udp.ReceiveAsync();
                var json = Encoding.UTF8.GetString(result.Buffer);
                var packet = ReadPacket<RelayAudioPacket>(json, "fluxchat.call-audio.v1");
                if (packet is null ||
                    string.IsNullOrWhiteSpace(packet.FromUserId) ||
                    string.IsNullOrWhiteSpace(packet.ToUserId) ||
                    !_online.TryGetValue(packet.FromUserId, out var sender))
                {
                    missingSenderPackets++;
                    if (missingSenderPackets == 1 || missingSenderPackets % 100 == 0)
                    {
                        Console.WriteLine($"UDP audio dropped: sender offline or packet invalid, count={missingSenderPackets}");
                    }

                    continue;
                }

                sender.AudioEndPoint = result.RemoteEndPoint;
                sender.LastPresenceUtc = DateTimeOffset.UtcNow;
                receivedPackets++;
                if (receivedPackets == 1 || receivedPackets % 500 == 0)
                {
                    Console.WriteLine($"UDP audio received: count={receivedPackets}, from={packet.FromUserId}, to={packet.ToUserId}, bytes={result.Buffer.Length}, endpoint={result.RemoteEndPoint}");
                }

                if (!_online.TryGetValue(packet.ToUserId, out var recipient) ||
                    recipient.AudioEndPoint is null)
                {
                    missingRecipientEndpointPackets++;
                    if (missingRecipientEndpointPackets == 1 || missingRecipientEndpointPackets % 100 == 0)
                    {
                        Console.WriteLine($"UDP audio waiting for recipient endpoint: count={missingRecipientEndpointPackets}, from={packet.FromUserId}, to={packet.ToUserId}, recipientOnline={_online.ContainsKey(packet.ToUserId)}");
                    }

                    continue;
                }

                await udp.SendAsync(result.Buffer, result.Buffer.Length, recipient.AudioEndPoint);
                forwardedPackets++;
                if (forwardedPackets == 1 || forwardedPackets % 500 == 0)
                {
                    Console.WriteLine($"UDP audio forwarded: count={forwardedPackets}, from={packet.FromUserId}, to={packet.ToUserId}, endpoint={recipient.AudioEndPoint}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP audio relay error: {ex.Message}");
            }
        }
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

    private async Task BroadcastPresenceAsync(ClientSession changed, string status, RelayPresencePacket? signedPacket = null)
    {
        var packet = signedPacket ?? CreatePresencePacket(changed, status);
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
            if (IsTransientMessage(packet))
            {
                _database.DeletePending(packet.MessageId);
                continue;
            }

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

    private async Task SweepStaleSessionsAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync())
        {
            var staleBefore = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(40);
            foreach (var session in _online.Values)
            {
                if (session.LastPresenceUtc >= staleBefore)
                {
                    continue;
                }

                if (!_online.TryRemove(new KeyValuePair<string, ClientSession>(session.UserId, session)))
                {
                    continue;
                }

                Console.WriteLine($"{session.DisplayName} ({session.UserId}) timed out");
                session.Close();
                await BroadcastPresenceAsync(session, "Offline");
            }
        }
    }

    private static bool IsTransientMessage(ChatPacket packet)
        => packet.Intent is "presence"
            or "call-audio"
            or "call-audio-state"
            or "call-invite"
            or "call-accept"
            or "call-decline"
            or "call-end"
            or "call-leave"
            or "call-join"
            or "call-screen-start"
            or "call-screen-frame"
            or "call-screen-stop";

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
        => session.LatestPresence is not null && session.LatestPresence.Status == status
            ? session.LatestPresence
            : RelayPresencePacket.Create(
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

    private static bool VerifyRegistrationIdentity(RelayRegisterPacket packet)
    {
        var hasAny = packet.PublicKey is not null || packet.IdentityNonce is not null ||
                     packet.IdentityTimestampUtc is not null || packet.IdentitySignature is not null;
        if (!hasAny) return true;
        if (string.IsNullOrWhiteSpace(packet.PublicKey) || string.IsNullOrWhiteSpace(packet.IdentityNonce) ||
            packet.IdentityTimestampUtc is null || string.IsNullOrWhiteSpace(packet.IdentitySignature) ||
            Math.Abs((DateTimeOffset.UtcNow - packet.IdentityTimestampUtc.Value).TotalMinutes) > 5 ||
            BadgeCrypto.CreateUserId(packet.PublicKey) != packet.UserId)
        {
            return false;
        }
        return BadgeCrypto.Verify(BadgeCrypto.BuildRegisterIdentityPayload(packet), packet.IdentitySignature, packet.PublicKey);
    }

    private static bool VerifyPresenceIdentity(RelayPresencePacket packet, ClientSession session)
    {
        var hasAny = packet.PublicKey is not null || packet.IdentityNonce is not null || packet.IdentitySignature is not null || packet.BadgeCertificate is not null;
        if (!hasAny) return packet.UserId == session.UserId;
        return packet.UserId == session.UserId && packet.PublicKey == session.PublicKey &&
               !string.IsNullOrWhiteSpace(packet.IdentityNonce) && !string.IsNullOrWhiteSpace(packet.IdentitySignature) &&
               Math.Abs((DateTimeOffset.UtcNow - packet.SentAtUtc).TotalMinutes) <= 5 &&
               BadgeCrypto.Verify(BadgeCrypto.BuildPresenceIdentityPayload(packet), packet.IdentitySignature, packet.PublicKey!) &&
               session.TryUseIdentityNonce(packet.IdentityNonce);
    }

    private static bool VerifyChatIdentity(ChatPacket packet, ClientSession session)
    {
        var hasAny = packet.FromPublicKey is not null || packet.IdentityNonce is not null || packet.IdentitySignature is not null || packet.BadgeCertificate is not null;
        if (!hasAny) return packet.FromUserId == session.UserId;
        return packet.FromUserId == session.UserId && packet.FromPublicKey == session.PublicKey &&
               !string.IsNullOrWhiteSpace(packet.IdentityNonce) && !string.IsNullOrWhiteSpace(packet.IdentitySignature) &&
               Math.Abs((DateTimeOffset.UtcNow - packet.SentAtUtc).TotalMinutes) <= 5 &&
               BadgeCrypto.Verify(BadgeCrypto.BuildChatIdentityPayload(packet), packet.IdentitySignature, packet.FromPublicKey!) &&
               session.TryUseIdentityNonce(packet.IdentityNonce);
    }
}

internal sealed class ClientSession(string userId, string displayName, StreamWriter writer, TcpClient client)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _audioSendLock = new(1, 1);
    private readonly SemaphoreSlim _screenSendLock = new(1, 1);
    private readonly object _audioGate = new();
    private readonly object _screenGate = new();
    private RelayScreenFramePacket? _pendingScreenFrame;
    private int _screenSendLoopActive;
    private long _droppedScreenFrames;
    private readonly Queue<string> _identityNonceOrder = new();
    private readonly HashSet<string> _identityNonces = new(StringComparer.Ordinal);
    private readonly object _identityNonceGate = new();

    public string UserId { get; } = userId;
    public string DisplayName { get; set; } = displayName;
    public StreamWriter Writer { get; } = writer;
    public TcpClient Client { get; } = client;
    public StreamWriter? AudioWriter { get; private set; }
    public StreamWriter? ScreenWriter { get; private set; }
    public IPEndPoint? AudioEndPoint { get; set; }
    public string Status { get; set; } = "Online";
    public string? PublicKey { get; set; }
    public RelayPresencePacket? LatestPresence { get; set; }
    public string? AvatarKind { get; set; }
    public string? AvatarMediaBase64 { get; set; }
    public string? AvatarExtension { get; set; }
    public double AvatarScale { get; set; } = 1;
    public double AvatarOffsetX { get; set; }
    public double AvatarOffsetY { get; set; }
    public double AvatarVideoStartSeconds { get; set; }
    public double AvatarVideoDurationSeconds { get; set; } = 10;

    public bool TryUseIdentityNonce(string nonce)
    {
        lock (_identityNonceGate)
        {
            if (!_identityNonces.Add(nonce)) return false;
            _identityNonceOrder.Enqueue(nonce);
            while (_identityNonceOrder.Count > 2048)
            {
                _identityNonces.Remove(_identityNonceOrder.Dequeue());
            }
            return true;
        }
    }
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

    public void SetAudioWriter(StreamWriter writer)
    {
        lock (_audioGate)
        {
            AudioWriter = writer;
        }
    }

    public void ClearAudioWriter(StreamWriter writer)
    {
        lock (_audioGate)
        {
            if (ReferenceEquals(AudioWriter, writer))
            {
                AudioWriter = null;
            }
        }
    }

    public async Task SendAudioAsync(RelayAudioPacket packet)
    {
        StreamWriter? writer;
        lock (_audioGate)
        {
            writer = AudioWriter;
        }

        if (writer is null)
        {
            throw new InvalidOperationException("Recipient audio channel is not connected.");
        }

        await _audioSendLock.WaitAsync();
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(packet));
        }
        finally
        {
            _audioSendLock.Release();
        }
    }

    public void SetScreenWriter(StreamWriter writer)
    {
        lock (_screenGate)
        {
            ScreenWriter = writer;
            _pendingScreenFrame = null;
            _droppedScreenFrames = 0;
        }
    }

    public void ClearScreenWriter(StreamWriter writer)
    {
        lock (_screenGate)
        {
            if (ReferenceEquals(ScreenWriter, writer))
            {
                ScreenWriter = null;
                _pendingScreenFrame = null;
            }
        }
    }

    public void EnqueueScreenFrame(RelayScreenFramePacket packet)
    {
        lock (_screenGate)
        {
            if (_pendingScreenFrame is not null)
            {
                _droppedScreenFrames++;
            }

            _pendingScreenFrame = packet;
        }

        if (Interlocked.CompareExchange(ref _screenSendLoopActive, 1, 0) == 0)
        {
            _ = Task.Run(SendPendingScreenFramesAsync);
        }
    }

    private async Task SendPendingScreenFramesAsync()
    {
        var sent = 0L;
        try
        {
            while (true)
            {
                RelayScreenFramePacket? packet;
                StreamWriter? writer;
                long dropped;
                lock (_screenGate)
                {
                    packet = _pendingScreenFrame;
                    _pendingScreenFrame = null;
                    writer = ScreenWriter;
                    dropped = _droppedScreenFrames;
                }

                if (packet is null || writer is null)
                {
                    break;
                }

                try
                {
                    await _screenSendLock.WaitAsync();
                    try
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(packet));
                    }
                    finally
                    {
                        _screenSendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Screen delivery failed from {packet.FromUserId} to {packet.ToUserId}: {ex.Message}");
                    ClearScreenWriter(writer);
                    break;
                }

                sent++;
                if (dropped > 0 && (sent == 1 || sent % 100 == 0))
                {
                    Console.WriteLine($"Screen latest-frame relay: to={packet.ToUserId}, sent={sent}, dropped={dropped}");
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _screenSendLoopActive, 0);

            var shouldRestart = false;
            lock (_screenGate)
            {
                shouldRestart = _pendingScreenFrame is not null && ScreenWriter is not null;
            }

            if (shouldRestart && Interlocked.CompareExchange(ref _screenSendLoopActive, 1, 0) == 0)
            {
                _ = Task.Run(SendPendingScreenFramesAsync);
            }
        }
    }

    public void Close()
    {
        try
        {
            Client.Close();
        }
        catch
        {
        }
    }
}
