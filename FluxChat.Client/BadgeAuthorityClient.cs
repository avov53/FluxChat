using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed class BadgeAuthorityClient(UserProfile profile, string authorityUrl) : IDisposable
{
    public const string OfficialPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPnMFYHwEBB/Gf6jnJg5gfGc5+azOWYUREkTpZ+FBCQ72iOmC2ftisTgy+jnh4rq2HJDPKJlCvCJgAEZjH3gIwg==";
    private readonly HttpClient _http = new() { BaseAddress = new Uri(authorityUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(8) };
    private readonly IdentitySigner _signer = new(profile);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenIdentityNonces = new(StringComparer.Ordinal);

    public static string CachePath => Path.Combine(AppPaths.DataDirectory, "badge-state.json");

    public async Task<BadgeStateResponse?> LoadVerifiedCacheAsync()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var state = JsonSerializer.Deserialize<BadgeStateResponse>(await File.ReadAllTextAsync(CachePath));
            return state is not null && IsStateValid(state) ? state : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or FormatException)
        {
            AppLog.Write(ex, "Badge state cache could not be loaded");
            return null;
        }
    }

    public async Task<BadgeStateResponse> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = Authorized(HttpMethod.Get, "api/v1/me");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<BadgeStateResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Badge Authority returned an empty state.");
        await AcceptAndCacheAsync(state);
        return state;
    }

    public async Task<BadgeStateResponse> SelectAsync(string? badgeId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = Authorized(HttpMethod.Post, "api/v1/select", new BadgeSelectRequest(badgeId));
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<BadgeStateResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Badge Authority returned an empty state.");
        await AcceptAndCacheAsync(state);
        return state;
    }

    public async Task<BadgeAdminUserResponse> LookupAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var response = await _http.SendAsync(Authorized(HttpMethod.Get, $"api/v1/admin/users/{Uri.EscapeDataString(userId)}"), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BadgeAdminUserResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Badge Authority returned an empty user.");
    }

    public Task<BadgeAdminUserResponse> GrantTesterAsync(string userId, CancellationToken cancellationToken = default)
        => MutateTesterAsync("api/v1/admin/grant-tester", userId, cancellationToken);

    public Task<BadgeAdminUserResponse> RevokeTesterAsync(string userId, CancellationToken cancellationToken = default)
        => MutateTesterAsync("api/v1/admin/revoke-tester", userId, cancellationToken);

    public bool VerifyRemoteCertificate(BadgeCertificate? certificate, string? publicKey, BadgeRevocationSnapshot? revocations)
    {
        if (certificate is null || string.IsNullOrWhiteSpace(publicKey) || revocations is null ||
            !BadgeCrypto.VerifyRevocations(revocations, OfficialPublicKey)) return false;
        return BadgeCrypto.VerifyCertificate(certificate, publicKey, OfficialPublicKey, revocations.RevokedSerials.ToHashSet(StringComparer.Ordinal));
    }

    public bool VerifyPresenceIdentity(RelayPresencePacket packet)
        => Math.Abs((DateTimeOffset.UtcNow - packet.SentAtUtc).TotalMinutes) <= 5 &&
           !string.IsNullOrWhiteSpace(packet.PublicKey) &&
           BadgeCrypto.CreateUserId(packet.PublicKey) == packet.UserId &&
           !string.IsNullOrWhiteSpace(packet.IdentityNonce) &&
           !string.IsNullOrWhiteSpace(packet.IdentitySignature) &&
           BadgeCrypto.Verify(BadgeCrypto.BuildPresenceIdentityPayload(packet), packet.IdentitySignature, packet.PublicKey) &&
           TryUseNonce($"presence:{packet.UserId}:{packet.IdentityNonce}");

    public bool VerifyChatIdentity(ChatPacket packet)
        => !string.IsNullOrWhiteSpace(packet.FromPublicKey) &&
           BadgeCrypto.CreateUserId(packet.FromPublicKey) == packet.FromUserId &&
           !string.IsNullOrWhiteSpace(packet.IdentityNonce) &&
           !string.IsNullOrWhiteSpace(packet.IdentitySignature) &&
           BadgeCrypto.Verify(BadgeCrypto.BuildChatIdentityPayload(packet), packet.IdentitySignature, packet.FromPublicKey) &&
           TryUseNonce($"chat:{packet.FromUserId}:{packet.IdentityNonce}");

    private bool TryUseNonce(string nonce)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_seenIdentityNonces.TryAdd(nonce, now)) return false;
        if (_seenIdentityNonces.Count > 4096)
        {
            var cutoff = now.AddHours(-24);
            foreach (var entry in _seenIdentityNonces.Where(x => x.Value < cutoff))
                _seenIdentityNonces.TryRemove(entry.Key, out _);
        }
        return true;
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(15)) return;
        using var challengeResponse = await _http.PostAsJsonAsync("api/v1/challenge", new BadgeChallengeRequest(profile.UserId, profile.PublicKey, profile.DisplayName), cancellationToken);
        challengeResponse.EnsureSuccessStatusCode();
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<BadgeChallengeResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Badge Authority returned an empty challenge.");
        using var authResponse = await _http.PostAsJsonAsync("api/v1/authenticate", new BadgeAuthenticateRequest(challenge.ChallengeId, _signer.SignChallenge(challenge.Challenge)), cancellationToken);
        authResponse.EnsureSuccessStatusCode();
        var auth = await authResponse.Content.ReadFromJsonAsync<BadgeAuthenticateResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Badge Authority returned an empty session.");
        _accessToken = auth.AccessToken;
        _accessTokenExpiresAtUtc = auth.ExpiresAtUtc;
        await AcceptAndCacheAsync(auth.State);
    }

    private async Task<BadgeAdminUserResponse> MutateTesterAsync(string path, string userId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = Authorized(HttpMethod.Post, path, new BadgeAdminMutationRequest(userId));
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BadgeAdminUserResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Badge Authority returned an empty result.");
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task AcceptAndCacheAsync(BadgeStateResponse state)
    {
        if (!IsStateValid(state)) throw new CryptographicException("Badge Authority response signature is invalid.");
        AppPaths.EnsureCreated();
        await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private bool IsStateValid(BadgeStateResponse state)
    {
        if (!BadgeCrypto.VerifyRevocations(state.Revocations, OfficialPublicKey)) return false;
        var revoked = state.Revocations.RevokedSerials.ToHashSet(StringComparer.Ordinal);
        return state.Certificates.All(c => BadgeCrypto.VerifyCertificate(c, profile.PublicKey, OfficialPublicKey, revoked)) &&
               (state.SelectedBadgeId is null || state.Certificates.Any(c => c.BadgeId == state.SelectedBadgeId));
    }

    public void Dispose() => _http.Dispose();
}
