using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed class RelayClient : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly UserProfile _profile;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private StreamWriter? _writer;

    public RelayClient(UserProfile profile)
    {
        _profile = profile;
    }

    public bool IsConnected => _client?.Connected == true && _writer is not null;
    public string? ConnectedServer { get; private set; }

    public event Action<ChatPacket>? MessageReceived;
    public event Action<RelayPresencePacket>? PresenceReceived;
    public event Action<string>? StatusChanged;

    public async Task<string?> ConnectAsync(string serverAddress, string credential, CancellationToken cancellationToken)
    {
        await DisposeConnectionAsync();

        var endpoint = ParseEndpoint(serverAddress);
        AppLog.Write($"Relay connect attempt: {endpoint.host}:{endpoint.port}");

        _client = new TcpClient();
        await _client.ConnectAsync(endpoint.host, endpoint.port, cancellationToken);

        var stream = _client.GetStream();
        _writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true)
        {
            AutoFlush = true
        };
        var reader = new StreamReader(stream, Utf8NoBom, leaveOpen: true);

        await SendRawAsync(RelayRegisterPacket.Create(_profile.UserId, _profile.DisplayName, credential), cancellationToken);
        var ackLine = await reader.ReadLineAsync(cancellationToken);
        var ack = ReadPacket<RelayAckPacket>(ackLine, "fluxchat.relay-ack.v1")
            ?? throw new InvalidOperationException("VPS server did not confirm registration.");
        if (!ack.Accepted)
        {
            throw new UnauthorizedAccessException(ack.Message);
        }

        AppLog.Write($"Relay ack: {ack.Message}");
        StatusChanged?.Invoke($"VPS connected: {endpoint.host}:{endpoint.port}");
        AppLog.Write($"Relay connected: {endpoint.host}:{endpoint.port}");
        ConnectedServer = $"{endpoint.host}:{endpoint.port}";

        _ = Task.Run(() => ReadLoopAsync(reader, _stop.Token));
        return ack.ClientToken;
    }

    public async Task SendAsync(ChatPacket packet, CancellationToken cancellationToken, bool log = true)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("VPS server is not connected.");
        }

        if (log)
        {
            AppLog.Write($"Relay send: messageId={packet.MessageId}, to={packet.ToUserId}");
        }

        await SendRawAsync(packet, cancellationToken);
    }

    public async Task SendPresenceAsync(
        string status,
        CancellationToken cancellationToken,
        string? avatarKind = null,
        string? avatarMediaBase64 = null,
        string? avatarExtension = null,
        double avatarScale = 1,
        double avatarOffsetX = 0,
        double avatarOffsetY = 0,
        double avatarVideoStartSeconds = 0,
        double avatarVideoDurationSeconds = 10)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("VPS server is not connected.");
        }

        await SendRawAsync(
            RelayPresencePacket.Create(
                _profile.UserId,
                _profile.DisplayName,
                status,
                avatarKind,
                avatarMediaBase64,
                avatarExtension,
                avatarScale,
                avatarOffsetX,
                avatarOffsetY,
                avatarVideoStartSeconds,
                avatarVideoDurationSeconds),
            cancellationToken);
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var json = await reader.ReadLineAsync(cancellationToken);
                if (json is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(json);
                var type = document.RootElement.TryGetProperty("type", out var typeProperty)
                    ? typeProperty.GetString()
                    : null;

                if (type == "fluxchat.message.v1")
                {
                    var packet = JsonSerializer.Deserialize<ChatPacket>(json);
                    if (packet is not null && packet.ToUserId == _profile.UserId)
                    {
                        AppLog.Write($"Relay message received: messageId={packet.MessageId}, from={packet.FromUserId}");
                        MessageReceived?.Invoke(packet);
                    }
                }
                else if (type == "fluxchat.relay-ack.v1")
                {
                    var ack = JsonSerializer.Deserialize<RelayAckPacket>(json);
                    if (ack is not null)
                    {
                        AppLog.Write($"Relay ack: {ack.Message}");
                    }
                }
                else if (type == "fluxchat.relay-presence.v1")
                {
                    var presence = JsonSerializer.Deserialize<RelayPresencePacket>(json);
                    if (presence is not null && presence.UserId != _profile.UserId)
                    {
                        PresenceReceived?.Invoke(presence);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Relay read loop failed");
            StatusChanged?.Invoke($"VPS disconnected: {ex.Message}");
        }
        finally
        {
            await DisposeConnectionAsync();
        }
    }

    private async Task SendRawAsync<T>(T packet, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_writer is null)
            {
                throw new InvalidOperationException("VPS server is not connected.");
            }

            await _writer.WriteLineAsync(JsonSerializer.Serialize(packet).AsMemory(), cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task DisposeConnectionAsync()
    {
        try
        {
            if (_writer is not null)
            {
                await _writer.DisposeAsync();
            }
        }
        catch
        {
        }

        _writer = null;
        _client?.Dispose();
        _client = null;
        ConnectedServer = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await DisposeConnectionAsync();
        _sendLock.Dispose();
        _stop.Dispose();
    }

    private static (string host, int port) ParseEndpoint(string serverAddress)
    {
        var value = serverAddress.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Enter VPS server address.");
        }

        var parts = value.Split(':', 2);
        if (parts.Length == 1)
        {
            return (parts[0], FluxChatPorts.Relay);
        }

        return int.TryParse(parts[1], out var port) && port > 0
            ? (parts[0], port)
            : throw new InvalidOperationException("Server port is invalid.");
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
}
