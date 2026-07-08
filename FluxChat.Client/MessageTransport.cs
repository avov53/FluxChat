using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed class MessageTransport : IAsyncDisposable
{
    private readonly UserProfile _profile;
    private readonly Func<UserPresenceStatus> _getCurrentStatus;
    private readonly Func<string?> _getCurrentNetworkId;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ChatPacket>> _pendingMessages = new();
    private TcpListener? _tcpListener;
    private UdpClient? _udpListener;

    public MessageTransport(UserProfile profile, Func<UserPresenceStatus> getCurrentStatus, Func<string?> getCurrentNetworkId)
    {
        _profile = profile;
        _getCurrentStatus = getCurrentStatus;
        _getCurrentNetworkId = getCurrentNetworkId;
    }

    public event Action<ChatPacket, IPEndPoint?>? MessageReceived;
    public event Action<ContactViewModel>? PeerDiscovered;
    public event Action<string>? StatusChanged;

    public void Start()
    {
        AppLog.Write($"Transport start requested: messagesPort={FluxChatPorts.Messages}");
        _ = Task.Run(() => RunListenerAsync("TCP", () => ListenTcpAsync(_stop.Token)));
        _ = Task.Run(() => RunListenerAsync("UDP", () => ListenUdpAsync(_stop.Token)));
    }

    private async Task RunListenerAsync(string listenerName, Func<Task> listenAsync)
    {
        try
        {
            await listenAsync();
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"{listenerName} listener stopped unexpectedly");
            StatusChanged?.Invoke($"{listenerName} listener failed: {ex.Message}");
        }
    }

    public async Task<string> SendAsync(ContactViewModel contact, ChatPacket packet, CancellationToken cancellationToken)
    {
        try
        {
            AppLog.Write($"TCP send attempt: messageId={packet.MessageId}, ip={contact.IpAddress}, port={contact.MessagePort}");
            using var tcpTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            tcpTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            await SendTcpPacketAsync(contact.IpAddress, contact.MessagePort, packet, tcpTimeout.Token);
            AppLog.Write($"TCP send succeeded: messageId={packet.MessageId}, ip={contact.IpAddress}, port={contact.MessagePort}");
            return "TCP";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"TCP send failed, trying UDP: messageId={packet.MessageId}, ip={contact.IpAddress}, port={contact.MessagePort}");
            await SendUdpPacketAsync(contact.IpAddress, contact.MessagePort, packet, cancellationToken);
            AppLog.Write($"UDP send completed: messageId={packet.MessageId}, ip={contact.IpAddress}, port={contact.MessagePort}");
            EnqueuePending(packet);
            return "UDP";
        }
    }

    public async Task<ContactViewModel> ResolveContactAsync(
        string ipAddress,
        CancellationToken cancellationToken,
        TimeSpan? timeoutAfter = null)
    {
        try
        {
            return await ResolveContactTcpAsync(ipAddress, cancellationToken, timeoutAfter);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Resolve contact TCP failed, trying UDP: ip={ipAddress}, port={FluxChatPorts.Messages}");
            return await ResolveContactUdpAsync(ipAddress, cancellationToken, timeoutAfter);
        }
    }

    private async Task<ContactViewModel> ResolveContactTcpAsync(
        string ipAddress,
        CancellationToken cancellationToken,
        TimeSpan? timeoutAfter)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutAfter ?? TimeSpan.FromSeconds(4));

        try
        {
            AppLog.Write($"Resolve contact TCP attempt: ip={ipAddress}, port={FluxChatPorts.Messages}");
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(ipAddress), FluxChatPorts.Messages, timeout.Token);

            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var request = CreatePeerInfoRequest();
            var requestPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request) + "\n");
            await stream.WriteAsync(requestPayload, timeout.Token);

            var responseJson = await reader.ReadLineAsync(timeout.Token);
            var response = ReadPeerInfoResponse(responseJson, request.RequestId);
            EnsureAllowedPeerNetwork(response);
            PublishQueuedMessages(response, new IPEndPoint(IPAddress.Parse(ipAddress), FluxChatPorts.Messages));
            AppLog.Write($"Resolve contact TCP succeeded: ip={ipAddress}, userId={response.UserId}, displayName={response.DisplayName}, port={response.MessagePort}, status={response.Status}");
            return CreateContact(ipAddress, response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write($"Resolve contact TCP timed out: ip={ipAddress}, port={FluxChatPorts.Messages}");
            throw new TimeoutException($"No TCP response from {ipAddress}:{FluxChatPorts.Messages}.");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Resolve contact TCP failed: ip={ipAddress}, port={FluxChatPorts.Messages}");
            throw;
        }
    }

    private async Task<ContactViewModel> ResolveContactUdpAsync(
        string ipAddress,
        CancellationToken cancellationToken,
        TimeSpan? timeoutAfter)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutAfter ?? TimeSpan.FromSeconds(4));

        try
        {
            var request = CreatePeerInfoRequest();
            using var client = new UdpClient(AddressFamily.InterNetwork);
            var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), FluxChatPorts.Messages);
            var payload = JsonSerializer.SerializeToUtf8Bytes(request);

            AppLog.Write($"Resolve contact UDP attempt: ip={ipAddress}, port={FluxChatPorts.Messages}, requestId={request.RequestId}");
            await client.SendAsync(payload, endpoint, timeout.Token);

            while (!timeout.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(timeout.Token);
                var responseJson = Encoding.UTF8.GetString(result.Buffer);
                var response = ReadPeerInfoResponse(responseJson, request.RequestId);
                EnsureAllowedPeerNetwork(response);
                PublishQueuedMessages(response, result.RemoteEndPoint);
                AppLog.Write($"Resolve contact UDP succeeded: ip={ipAddress}, remote={result.RemoteEndPoint}, userId={response.UserId}, displayName={response.DisplayName}, port={response.MessagePort}, status={response.Status}");
                return CreateContact(ipAddress, response);
            }

            throw new TimeoutException($"No UDP response from {ipAddress}:{FluxChatPorts.Messages}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write($"Resolve contact UDP timed out: ip={ipAddress}, port={FluxChatPorts.Messages}");
            throw new TimeoutException($"No response from {ipAddress}:{FluxChatPorts.Messages}. Check that FluxChat is running there and firewall allows TCP/UDP {FluxChatPorts.Messages}.");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Resolve contact UDP failed: ip={ipAddress}, port={FluxChatPorts.Messages}");
            throw;
        }
    }

    private static async Task SendTcpPacketAsync(string ipAddress, int port, ChatPacket packet, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(ipAddress), port, cancellationToken);

        await using var stream = client.GetStream();
        var json = JsonSerializer.Serialize(packet);
        var payload = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(payload, cancellationToken);
    }

    private static async Task SendUdpPacketAsync(string ipAddress, int port, ChatPacket packet, CancellationToken cancellationToken)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        var payload = JsonSerializer.SerializeToUtf8Bytes(packet);
        await client.SendAsync(payload, new IPEndPoint(IPAddress.Parse(ipAddress), port), cancellationToken);
    }

    private async Task ListenTcpAsync(CancellationToken cancellationToken)
    {
        _tcpListener = new TcpListener(IPAddress.Any, FluxChatPorts.Messages);
        _tcpListener.Start();
        AppLog.Write($"TCP listener started: port={FluxChatPorts.Messages}");
        StatusChanged?.Invoke($"Listening on TCP {FluxChatPorts.Messages}");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                AppLog.Write($"TCP accepted: remote={remote}");
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var json = await reader.ReadLineAsync(cancellationToken);
                await HandleTcpPayloadAsync(json, remote, stream, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "TCP receive error");
                StatusChanged?.Invoke($"TCP receive error: {ex.Message}");
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private async Task ListenUdpAsync(CancellationToken cancellationToken)
    {
        _udpListener = new UdpClient();
        _udpListener.Client.ExclusiveAddressUse = false;
        _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, FluxChatPorts.Messages));
        AppLog.Write($"UDP listener started: port={FluxChatPorts.Messages}");
        StatusChanged?.Invoke($"Listening on UDP {FluxChatPorts.Messages}");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpListener.ReceiveAsync(cancellationToken);
                AppLog.Write($"UDP received: remote={result.RemoteEndPoint}, bytes={result.Buffer.Length}");
                var json = Encoding.UTF8.GetString(result.Buffer);
                await HandleUdpPayloadAsync(json, result.RemoteEndPoint, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "UDP receive error");
                StatusChanged?.Invoke($"UDP receive error: {ex.Message}");
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private void HandlePacket(string? json, IPEndPoint? remote)
    {
        var packet = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ChatPacket>(json);

        if (packet is null || packet.Type != "fluxchat.message.v1" || packet.ToUserId != _profile.UserId)
        {
            AppLog.Write($"Ignored packet: remote={remote}, empty={string.IsNullOrWhiteSpace(json)}, currentUserId={_profile.UserId}");
            return;
        }

        if (!IsAllowedNetwork(packet.NetworkId))
        {
            AppLog.Write($"Ignored packet from another virtual LAN: messageId={packet.MessageId}, remote={remote}");
            return;
        }

        AppLog.Write($"Packet accepted: messageId={packet.MessageId}, from={packet.FromUserId}, to={packet.ToUserId}, remote={remote}");
        MessageReceived?.Invoke(packet, remote);
    }

    private async Task HandleTcpPayloadAsync(string? json, IPEndPoint? remote, NetworkStream stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString()
            : null;

        if (type == "fluxchat.peer-info-request.v1")
        {
            var request = JsonSerializer.Deserialize<PeerInfoRequest>(json);
            if (request is null)
            {
                AppLog.Write($"Peer info request ignored: invalid json from {remote}");
                return;
            }

            if (!IsAllowedNetwork(request.NetworkId))
            {
                AppLog.Write($"Peer info request ignored: virtual LAN mismatch from {remote}");
                return;
            }

            TryPublishRequesterContact(request, remote);

            var response = PeerInfoResponse.Create(
                request.RequestId,
                _profile.UserId,
                _profile.DisplayName,
                _getCurrentStatus().ToString(),
                TakePendingMessages(request.RequesterUserId),
                _getCurrentNetworkId());
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response) + "\n");
            await stream.WriteAsync(payload, cancellationToken);
            AppLog.Write($"Peer info response sent: requestId={request.RequestId}, remote={remote}, status={_getCurrentStatus()}");
            StatusChanged?.Invoke($"Peer info sent to {remote?.Address}");
            return;
        }

        HandlePacket(json, remote);
    }

    private async Task HandleUdpPayloadAsync(string? json, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        var type = document.RootElement.TryGetProperty("type", out var typeProperty)
            ? typeProperty.GetString()
            : null;

        if (type == "fluxchat.peer-info-request.v1")
        {
            var request = JsonSerializer.Deserialize<PeerInfoRequest>(json);
            if (request is null || _udpListener is null)
            {
                AppLog.Write($"UDP peer info request ignored: invalid request from {remote}");
                return;
            }

            if (!IsAllowedNetwork(request.NetworkId))
            {
                AppLog.Write($"UDP peer info request ignored: virtual LAN mismatch from {remote}");
                return;
            }

            TryPublishRequesterContact(request, remote);

            var response = PeerInfoResponse.Create(
                request.RequestId,
                _profile.UserId,
                _profile.DisplayName,
                _getCurrentStatus().ToString(),
                TakePendingMessages(request.RequesterUserId),
                _getCurrentNetworkId());
            var payload = JsonSerializer.SerializeToUtf8Bytes(response);
            await _udpListener.SendAsync(payload, remote, cancellationToken);
            AppLog.Write($"UDP peer info response sent: requestId={request.RequestId}, remote={remote}, status={_getCurrentStatus()}");
            StatusChanged?.Invoke($"UDP peer info sent to {remote.Address}");
            return;
        }

        HandlePacket(json, remote);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _tcpListener?.Stop();
        _udpListener?.Dispose();
        _stop.Dispose();
    }

    private static UserPresenceStatus ParseStatus(string? status)
    {
        return Enum.TryParse<UserPresenceStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : UserPresenceStatus.Online;
    }

    private static PeerInfoResponse ReadPeerInfoResponse(string? responseJson, Guid requestId)
    {
        var response = string.IsNullOrWhiteSpace(responseJson)
            ? null
            : JsonSerializer.Deserialize<PeerInfoResponse>(responseJson);

        if (response is null || response.Type != "fluxchat.peer-info-response.v1" || response.RequestId != requestId)
        {
            throw new InvalidOperationException("The remote client did not return valid peer info.");
        }

        return response;
    }

    private static ContactViewModel CreateContact(string ipAddress, PeerInfoResponse response)
    {
        return new ContactViewModel
        {
            UserId = response.UserId,
            DisplayName = response.DisplayName,
            IpAddress = ipAddress,
            MessagePort = response.MessagePort,
            Status = ParseStatus(response.Status),
            LastSeenUtc = DateTimeOffset.UtcNow
        };
    }

    private PeerInfoRequest CreatePeerInfoRequest()
        => PeerInfoRequest.Create(_profile.UserId, _profile.DisplayName, _getCurrentStatus().ToString(), _getCurrentNetworkId());

    private void EnqueuePending(ChatPacket packet)
    {
        var queue = _pendingMessages.GetOrAdd(packet.ToUserId, _ => new ConcurrentQueue<ChatPacket>());
        queue.Enqueue(packet);
        AppLog.Write($"Queued message for pull delivery: messageId={packet.MessageId}, to={packet.ToUserId}, pendingCount={queue.Count}");
    }

    private IReadOnlyList<ChatPacket>? TakePendingMessages(string? requesterUserId)
    {
        if (string.IsNullOrWhiteSpace(requesterUserId) ||
            !_pendingMessages.TryGetValue(requesterUserId, out var queue))
        {
            return null;
        }

        var messages = new List<ChatPacket>();
        while (queue.TryDequeue(out var packet))
        {
            messages.Add(packet);
        }

        if (messages.Count == 0)
        {
            return null;
        }

        AppLog.Write($"Returning queued messages: to={requesterUserId}, count={messages.Count}");
        return messages;
    }

    private void PublishQueuedMessages(PeerInfoResponse response, IPEndPoint remote)
    {
        if (response.QueuedMessages is null || response.QueuedMessages.Count == 0)
        {
            return;
        }

        AppLog.Write($"Pulled queued messages: from={response.UserId}, remote={remote}, count={response.QueuedMessages.Count}");
        foreach (var packet in response.QueuedMessages)
        {
            HandlePacket(JsonSerializer.Serialize(packet), remote);
        }
    }

    private void TryPublishRequesterContact(PeerInfoRequest request, IPEndPoint? remote)
    {
        if (remote is null ||
            string.IsNullOrWhiteSpace(request.RequesterUserId) ||
            string.IsNullOrWhiteSpace(request.RequesterDisplayName) ||
            request.RequesterUserId == _profile.UserId ||
            !IsAllowedNetwork(request.NetworkId))
        {
            return;
        }

        var contact = new ContactViewModel
        {
            UserId = request.RequesterUserId,
            DisplayName = request.RequesterDisplayName,
            IpAddress = remote.Address.ToString(),
            MessagePort = request.RequesterMessagePort <= 0 ? FluxChatPorts.Messages : request.RequesterMessagePort,
            Status = ParseStatus(request.RequesterStatus),
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        AppLog.Write($"Peer discovered from request: userId={contact.UserId}, displayName={contact.DisplayName}, ip={contact.IpAddress}, port={contact.MessagePort}, status={contact.Status}");
        PeerDiscovered?.Invoke(contact);
    }

    private bool IsAllowedNetwork(string? packetNetworkId)
    {
        var currentNetworkId = _getCurrentNetworkId();
        return string.IsNullOrWhiteSpace(currentNetworkId) ||
            string.Equals(currentNetworkId, packetNetworkId, StringComparison.Ordinal);
    }

    private void EnsureAllowedPeerNetwork(PeerInfoResponse response)
    {
        if (!IsAllowedNetwork(response.NetworkId))
        {
            throw new InvalidOperationException("The remote client is not in this Virtual LAN.");
        }
    }
}
