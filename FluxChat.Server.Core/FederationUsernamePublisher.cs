using System.Net.Sockets;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Server.Core;

public sealed class FederationUsernamePublisher
{
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new(false);
    private readonly string _serverId;
    private readonly string _key;
    private readonly string[] _peers;

    public FederationUsernamePublisher(string serverId, string key, IEnumerable<string> peers)
    {
        _serverId = serverId;
        _key = key;
        _peers = peers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_key) && _peers.Length > 0;

    public async Task PublishAsync(FederationUsernameClaim claim, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;
        var packet = ChatPacket.Create(_serverId, _serverId, "__federation__", JsonSerializer.Serialize(claim), intent: "federation-username-claim");
        var sentAtUtc = DateTimeOffset.UtcNow;
        var federation = FederationEnvelopeCrypto.Seal(packet, sentAtUtc, _key);

        foreach (var peer in _peers)
        {
            try
            {
                var (host, port) = ParseEndpoint(peer);
                using var client = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(host, port, timeout.Token);
                await using var stream = client.GetStream();
                await using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(federation));
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
            {
                Console.Error.WriteLine($"Federation username claim to {peer} failed: {ex.Message}");
            }
        }
    }

    private static (string Host, int Port) ParseEndpoint(string input)
    {
        var parts = input.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Federation peer must use host:port.");
        }
        return (parts[0], port);
    }
}
