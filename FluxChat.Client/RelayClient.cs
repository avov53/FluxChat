using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed class RelayClient : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly TimeSpan AudioUdpPreferredAfterReceive = TimeSpan.FromSeconds(5);

    private readonly UserProfile _profile;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _audioSendLock = new(1, 1);
    private readonly SemaphoreSlim _audioConnectLock = new(1, 1);
    private readonly SemaphoreSlim _screenSendLock = new(1, 1);
    private readonly SemaphoreSlim _screenConnectLock = new(1, 1);
    private TcpClient? _client;
    private TcpClient? _audioClient;
    private TcpClient? _screenClient;
    private UdpClient? _audioUdp;
    private StreamWriter? _writer;
    private StreamWriter? _audioWriter;
    private StreamWriter? _screenWriter;
    private string? _audioHost;
    private int _audioPort;
    private string _audioCredential = "";
    private string? _screenHost;
    private int _screenPort;
    private string _screenCredential = "";
    private long _lastUdpAudioReceivedTicks;

    public RelayClient(UserProfile profile)
    {
        _profile = profile;
    }

    public bool IsConnected => _client?.Connected == true && _writer is not null;
    public bool IsScreenChannelConnected => _screenClient?.Connected == true && _screenWriter is not null;
    public string? ConnectedServer { get; private set; }
    public RelayIceConfig? IceConfig { get; private set; }

    public event Action<ChatPacket>? MessageReceived;
    public event Action<RelayAudioPacket>? AudioReceived;
    public event Action<RelayScreenFramePacket>? ScreenFrameReceived;
    public event Action<RelayPresencePacket>? PresenceReceived;
    public event Action<string>? StatusChanged;

    public async Task<string?> ConnectAsync(string serverAddress, string credential, CancellationToken cancellationToken)
    {
        await DisposeConnectionAsync();

        var endpoint = ParseEndpoint(serverAddress);
        AppLog.Write($"Relay connect attempt: {endpoint.host}:{endpoint.port}");

        _client = new TcpClient();
        _client.NoDelay = true;
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
        IceConfig = ack.IceConfig;
        if (IceConfig is not null)
        {
            var turnCount = IceConfig.IceServers.Count(server =>
                server.Urls.Any(url => url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase)));
            AppLog.Write($"Relay ICE config received: iceServers={IceConfig.IceServers.Count}, turnServers={turnCount}, expiresAtUtc={IceConfig.ExpiresAtUtc:O}");
        }

        StatusChanged?.Invoke($"VPS connected: {endpoint.host}:{endpoint.port}");
        AppLog.Write($"Relay connected: {endpoint.host}:{endpoint.port}");
        ConnectedServer = $"{endpoint.host}:{endpoint.port}";
        _audioHost = endpoint.host;
        _audioPort = endpoint.port;
        _audioCredential = credential;
        _screenHost = endpoint.host;
        _screenPort = endpoint.port;
        _screenCredential = credential;

        try
        {
            await ConnectAudioTcpAsync(endpoint.host, endpoint.port, credential, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Relay audio TCP connect failed: {endpoint.host}:{endpoint.port}");
            try
            {
                if (_audioWriter is not null)
                {
                    await _audioWriter.DisposeAsync();
                }
            }
            catch
            {
            }

            _audioWriter = null;
            _audioClient?.Dispose();
            _audioClient = null;
        }

        try
        {
            await ConnectScreenTcpAsync(endpoint.host, endpoint.port, credential, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Relay screen TCP connect failed: {endpoint.host}:{endpoint.port}");
            try
            {
                if (_screenWriter is not null)
                {
                    await _screenWriter.DisposeAsync();
                }
            }
            catch
            {
            }

            _screenWriter = null;
            _screenClient?.Dispose();
            _screenClient = null;
        }

        _audioUdp = new UdpClient();
        _audioUdp.Connect(endpoint.host, endpoint.port);
        _ = Task.Run(() => ReadAudioLoopAsync(_audioUdp, _stop.Token));

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

    public async Task SendAudioAsync(RelayAudioPacket packet, CancellationToken cancellationToken)
    {
        var udp = _audioUdp;
        var preferUdp = string.IsNullOrWhiteSpace(packet.Body) || IsUdpAudioActive();
        if (udp is not null && preferUdp)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));
                await udp.SendAsync(bytes, cancellationToken);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write(ex, "Relay audio UDP send failed, falling back to TCP");
            }
        }

        if (!string.IsNullOrWhiteSpace(packet.Body))
        {
            if (_audioWriter is null)
            {
                await TryEnsureAudioTcpConnectedAsync(cancellationToken);
            }

            if (_audioWriter is not null)
            {
                try
                {
                    await SendAudioRawAsync(packet, cancellationToken);
                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    AppLog.Write(ex, "Relay audio TCP send failed, falling back to UDP");
                    await DisposeAudioTcpAsync();
                }
            }
        }

        if (udp is not null)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet));
                await udp.SendAsync(bytes, cancellationToken);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write(ex, "Relay audio UDP send failed");
            }
        }

        if (_audioWriter is null)
        {
            await TryEnsureAudioTcpConnectedAsync(cancellationToken);
        }

        if (_audioWriter is not null)
        {
            try
            {
                await SendAudioRawAsync(packet, cancellationToken);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write(ex, "Relay audio TCP send failed");
                await DisposeAudioTcpAsync();
            }
        }

        throw new InvalidOperationException("VPS audio relay is not connected.");
    }

    public async Task SendScreenFrameAsync(RelayScreenFramePacket packet, CancellationToken cancellationToken)
    {
        if (_screenWriter is null)
        {
            await TryEnsureScreenTcpConnectedAsync(cancellationToken);
        }

        if (_screenWriter is null)
        {
            throw new InvalidOperationException("VPS screen relay is not connected.");
        }

        try
        {
            await SendScreenRawAsync(packet, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write(ex, "Relay screen TCP send failed");
            await DisposeScreenTcpAsync();
            throw;
        }
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

    private async Task ConnectAudioTcpAsync(string host, int port, string credential, CancellationToken cancellationToken)
    {
        await DisposeAudioTcpAsync();
        var audioClient = new TcpClient();
        audioClient.NoDelay = true;
        _audioClient = audioClient;
        await audioClient.ConnectAsync(host, port, cancellationToken);

        var stream = audioClient.GetStream();
        _audioWriter = new StreamWriter(stream, Utf8NoBom, leaveOpen: true)
        {
            AutoFlush = true
        };
        var audioWriter = _audioWriter;
        var reader = new StreamReader(stream, Utf8NoBom, leaveOpen: true);

        await SendAudioRawAsync(
            RelayAudioRegisterPacket.Create(_profile.UserId, _profile.DisplayName, credential),
            cancellationToken);
        var ackLine = await reader.ReadLineAsync(cancellationToken);
        var ack = ReadPacket<RelayAckPacket>(ackLine, "fluxchat.relay-ack.v1")
            ?? throw new InvalidOperationException("VPS audio relay did not confirm registration.");
        if (!ack.Accepted)
        {
            throw new UnauthorizedAccessException(ack.Message);
        }

        AppLog.Write($"Relay audio TCP connected: {host}:{port}");
        _ = Task.Run(() => ReadAudioTcpLoopAsync(reader, audioWriter, audioClient, _stop.Token));
    }

    private async Task TryEnsureAudioTcpConnectedAsync(CancellationToken cancellationToken)
    {
        if (_audioWriter is not null ||
            string.IsNullOrWhiteSpace(_audioHost) ||
            _audioPort <= 0)
        {
            return;
        }

        await _audioConnectLock.WaitAsync(cancellationToken);
        try
        {
            if (_audioWriter is not null)
            {
                return;
            }

            try
            {
                await ConnectAudioTcpAsync(_audioHost, _audioPort, _audioCredential, cancellationToken);
                AppLog.Write($"Relay audio TCP reconnected: {_audioHost}:{_audioPort}");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write(ex, $"Relay audio TCP reconnect failed: {_audioHost}:{_audioPort}");
            }
        }
        finally
        {
            _audioConnectLock.Release();
        }
    }

    private async Task ConnectScreenTcpAsync(string host, int port, string credential, CancellationToken cancellationToken)
    {
        await DisposeScreenTcpAsync();
        var screenClient = new TcpClient();
        screenClient.NoDelay = true;
        _screenClient = screenClient;
        await screenClient.ConnectAsync(host, port, cancellationToken);

        var stream = screenClient.GetStream();
        _screenWriter = new StreamWriter(stream, Utf8NoBom, leaveOpen: true)
        {
            AutoFlush = true
        };
        var screenWriter = _screenWriter;
        var reader = new StreamReader(stream, Utf8NoBom, leaveOpen: true);

        await SendScreenRawAsync(
            RelayScreenRegisterPacket.Create(_profile.UserId, _profile.DisplayName, credential),
            cancellationToken);
        var ackLine = await reader.ReadLineAsync(cancellationToken);
        var ack = ReadPacket<RelayAckPacket>(ackLine, "fluxchat.relay-ack.v1")
            ?? throw new InvalidOperationException("VPS screen relay did not confirm registration.");
        if (!ack.Accepted)
        {
            throw new UnauthorizedAccessException(ack.Message);
        }

        AppLog.Write($"Relay screen TCP connected: {host}:{port}");
        _ = Task.Run(() => ReadScreenTcpLoopAsync(reader, screenWriter, screenClient, _stop.Token));
    }

    private async Task TryEnsureScreenTcpConnectedAsync(CancellationToken cancellationToken)
    {
        if (_screenWriter is not null ||
            string.IsNullOrWhiteSpace(_screenHost) ||
            _screenPort <= 0)
        {
            return;
        }

        await _screenConnectLock.WaitAsync(cancellationToken);
        try
        {
            if (_screenWriter is not null)
            {
                return;
            }

            try
            {
                await ConnectScreenTcpAsync(_screenHost, _screenPort, _screenCredential, cancellationToken);
                AppLog.Write($"Relay screen TCP reconnected: {_screenHost}:{_screenPort}");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write(ex, $"Relay screen TCP reconnect failed: {_screenHost}:{_screenPort}");
            }
        }
        finally
        {
            _screenConnectLock.Release();
        }
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
                        if (!IsCallAudioPacket(packet))
                        {
                            AppLog.Write($"Relay message received: messageId={packet.MessageId}, from={packet.FromUserId}");
                        }

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

    private async Task ReadAudioLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cancellationToken);
                using var document = JsonDocument.Parse(result.Buffer);
                var type = document.RootElement.TryGetProperty("type", out var typeProperty)
                    ? typeProperty.GetString()
                    : null;

                if (type != "fluxchat.call-audio.v1")
                {
                    continue;
                }

                var packet = JsonSerializer.Deserialize<RelayAudioPacket>(result.Buffer);
                if (packet is not null && packet.ToUserId == _profile.UserId)
                {
                    Interlocked.Exchange(ref _lastUdpAudioReceivedTicks, DateTimeOffset.UtcNow.Ticks);
                    AudioReceived?.Invoke(packet);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Relay UDP audio loop failed");
        }
    }

    private async Task ReadAudioTcpLoopAsync(
        StreamReader reader,
        StreamWriter associatedWriter,
        TcpClient associatedClient,
        CancellationToken cancellationToken)
    {
        var received = 0L;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var json = await reader.ReadLineAsync(cancellationToken);
                if (json is null)
                {
                    break;
                }

                var packet = ReadPacket<RelayAudioPacket>(json, "fluxchat.call-audio.v1");
                if (packet is not null && packet.ToUserId == _profile.UserId)
                {
                    received++;
                    if (received == 1 || received % 100 == 0)
                    {
                        AppLog.Write($"Relay audio TCP packet received: frames={received}, from={packet.FromUserId}, bytes={packet.Body.Length}");
                    }

                    AudioReceived?.Invoke(packet);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Relay TCP audio loop failed");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write($"Relay audio TCP disconnected: receivedFrames={received}");
                await DisposeAudioTcpAsync(associatedWriter, associatedClient);
            }
        }
    }

    private async Task ReadScreenTcpLoopAsync(
        StreamReader reader,
        StreamWriter associatedWriter,
        TcpClient associatedClient,
        CancellationToken cancellationToken)
    {
        var received = 0L;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var json = await reader.ReadLineAsync(cancellationToken);
                if (json is null)
                {
                    break;
                }

                var packet = ReadPacket<RelayScreenFramePacket>(json, "fluxchat.screen-frame.v1");
                if (packet is not null && packet.ToUserId == _profile.UserId)
                {
                    received++;
                    if (received == 1 || received % 100 == 0)
                    {
                        AppLog.Write($"Relay screen TCP packet received: frames={received}, from={packet.FromUserId}, bytes={packet.Body.Length}");
                    }

                    ScreenFrameReceived?.Invoke(packet);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Relay TCP screen loop failed");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write($"Relay screen TCP disconnected: receivedFrames={received}");
                await DisposeScreenTcpAsync(associatedWriter, associatedClient);
            }
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

    private async Task SendAudioRawAsync<T>(T packet, CancellationToken cancellationToken)
    {
        await _audioSendLock.WaitAsync(cancellationToken);
        try
        {
            if (_audioWriter is null)
            {
                throw new InvalidOperationException("VPS audio relay is not connected.");
            }

            await _audioWriter.WriteLineAsync(JsonSerializer.Serialize(packet).AsMemory(), cancellationToken);
        }
        finally
        {
            _audioSendLock.Release();
        }
    }

    private async Task SendScreenRawAsync<T>(T packet, CancellationToken cancellationToken)
    {
        await _screenSendLock.WaitAsync(cancellationToken);
        try
        {
            if (_screenWriter is null)
            {
                throw new InvalidOperationException("VPS screen relay is not connected.");
            }

            await _screenWriter.WriteLineAsync(JsonSerializer.Serialize(packet).AsMemory(), cancellationToken);
        }
        finally
        {
            _screenSendLock.Release();
        }
    }

    private async Task DisposeConnectionAsync()
    {
        await DisposeAudioTcpAsync();
        await DisposeScreenTcpAsync();
        Interlocked.Exchange(ref _lastUdpAudioReceivedTicks, 0);

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
        _audioUdp?.Dispose();
        _audioUdp = null;
        _client?.Dispose();
        _client = null;
        ConnectedServer = null;
        IceConfig = null;
        _audioHost = null;
        _audioPort = 0;
        _audioCredential = "";
        _screenHost = null;
        _screenPort = 0;
        _screenCredential = "";
    }

    private async Task DisposeAudioTcpAsync(StreamWriter? writerToClear = null, TcpClient? clientToClear = null)
    {
        var writer = writerToClear ?? _audioWriter;
        try
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }
        }
        catch
        {
        }

        clientToClear?.Dispose();

        if (writerToClear is null || ReferenceEquals(_audioWriter, writerToClear))
        {
            _audioWriter = null;
        }

        if (clientToClear is null || ReferenceEquals(_audioClient, clientToClear))
        {
            _audioClient?.Dispose();
            _audioClient = null;
        }
    }

    private async Task DisposeScreenTcpAsync(StreamWriter? writerToClear = null, TcpClient? clientToClear = null)
    {
        var writer = writerToClear ?? _screenWriter;
        try
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }
        }
        catch
        {
        }

        clientToClear?.Dispose();

        if (writerToClear is null || ReferenceEquals(_screenWriter, writerToClear))
        {
            _screenWriter = null;
        }

        if (clientToClear is null || ReferenceEquals(_screenClient, clientToClear))
        {
            _screenClient?.Dispose();
            _screenClient = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await DisposeConnectionAsync();
        _sendLock.Dispose();
        _audioSendLock.Dispose();
        _audioConnectLock.Dispose();
        _screenSendLock.Dispose();
        _screenConnectLock.Dispose();
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

    private static bool IsCallAudioPacket(ChatPacket packet)
        => packet.Intent == "call-audio" ||
            packet.Body.StartsWith("fluxchat-control:", StringComparison.Ordinal) &&
            packet.Body.Contains("\"Intent\":\"call-audio\"", StringComparison.Ordinal);

    private bool IsUdpAudioActive()
    {
        var lastTicks = Interlocked.Read(ref _lastUdpAudioReceivedTicks);
        return lastTicks > 0 &&
               DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero) <= AudioUdpPreferredAfterReceive;
    }
}
