using System.Net.Http.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed class AccountClient(string apiUrl)
{
    private readonly HttpClient _http = new() { BaseAddress = EnsureBaseAddress(apiUrl), Timeout = TimeSpan.FromSeconds(25) };

    public async Task<AccountResult> RegisterAsync(
        UserProfile profile,
        string login,
        string password,
        string bootstrapToken,
        CancellationToken cancellationToken)
    {
        var syntheticEmail = BuildSyntheticEmail(login, profile.UserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/accounts/register")
        {
            Content = JsonContent.Create(new AccountRegisterRequest(profile.UserId, profile.DisplayName, login, syntheticEmail, password, profile.PublicKey))
        };
        request.Headers.Add("X-FluxChat-Bootstrap-Token", bootstrapToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountSessionResponse> LoginAsync(string loginOrEmail, string password, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/v1/accounts/login",
            new AccountLoginRequest(loginOrEmail, password, Environment.MachineName),
            cancellationToken);
        return await ReadSessionAsync(response, cancellationToken);
    }

    public async Task<AccountResult> RequestCodeAsync(string loginOrEmail, string purpose, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/v1/accounts/request-code",
            new AccountCodeRequest(loginOrEmail, purpose),
            cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountSessionResponse> LoginByCodeAsync(string loginOrEmail, string code, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/v1/accounts/login-code",
            new AccountCodeLoginRequest(loginOrEmail, code, Environment.MachineName),
            cancellationToken);
        return await ReadSessionAsync(response, cancellationToken);
    }

    public async Task<AccountResult> ResetPasswordAsync(string loginOrEmail, string code, string newPassword, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/v1/accounts/reset-password",
            new AccountResetPasswordRequest(loginOrEmail, code, newPassword),
            cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountResult> VerifyEmailAsync(string loginOrEmail, string code, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/v1/accounts/verify-email",
            new AccountVerifyEmailRequest(loginOrEmail, code),
            cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountSessionResponse> ValidateSessionAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/accounts/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadSessionAsync(response, cancellationToken);
    }

    public async Task<AccountDeviceSessionsResponse> ListSessionsAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/accounts/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadDeviceSessionsAsync(response, cancellationToken);
    }

    public async Task<AccountResult> RevokeSessionAsync(string token, string sessionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/accounts/sessions/revoke")
        {
            Content = JsonContent.Create(new AccountSessionRevokeRequest(sessionId))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountResult> RevokeAllSessionsAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/accounts/sessions/revoke-all")
        {
            Content = JsonContent.Create(new AccountSessionRevokeAllRequest(true))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountResult> ChangePasswordAsync(string token, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/accounts/change-password")
        {
            Content = JsonContent.Create(new AccountChangePasswordRequest(currentPassword, newPassword))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountPreferencesResponse> GetPreferencesAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/accounts/preferences");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        try
        {
            return await response.Content.ReadFromJsonAsync<AccountPreferencesResponse>(cancellationToken: cancellationToken)
                   ?? new AccountPreferencesResponse(false, response.ReasonPhrase ?? "Account service did not return preferences.");
        }
        catch (JsonException)
        {
            return new AccountPreferencesResponse(false, await BuildNonJsonErrorAsync(response, cancellationToken));
        }
    }

    public async Task<AccountResult> SavePreferencesAsync(
        string token,
        AccountPreferencesUpdateRequest preferences,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/accounts/preferences")
        {
            Content = JsonContent.Create(preferences)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    public async Task<AccountResult> DeleteAccountAsync(string token, string currentPassword, string confirmation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/accounts/delete")
        {
            Content = JsonContent.Create(new AccountDeleteRequest(currentPassword, confirmation))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    private static async Task<AccountResult> ReadResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken);
            return result ?? new AccountResult(false, response.ReasonPhrase ?? "Account service did not return a result.");
        }
        catch (JsonException)
        {
            return new AccountResult(false, await BuildNonJsonErrorAsync(response, cancellationToken));
        }
    }

    private static async Task<AccountSessionResponse> ReadSessionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<AccountSessionResponse>(cancellationToken: cancellationToken);
            return result ?? new AccountSessionResponse(false, response.ReasonPhrase ?? "Account service did not return a result.");
        }
        catch (JsonException)
        {
            return new AccountSessionResponse(false, await BuildNonJsonErrorAsync(response, cancellationToken));
        }
    }

    private static async Task<AccountDeviceSessionsResponse> ReadDeviceSessionsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<AccountDeviceSessionsResponse>(cancellationToken: cancellationToken);
            return result ?? new AccountDeviceSessionsResponse(false, response.ReasonPhrase ?? "Account service did not return device sessions.");
        }
        catch (JsonException)
        {
            return new AccountDeviceSessionsResponse(false, await BuildNonJsonErrorAsync(response, cancellationToken));
        }
    }

    private static async Task<string> BuildNonJsonErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body = body.Replace("\r", " ").Replace("\n", " ").Trim();
        if (body.Length > 160)
        {
            body = body[..160] + "...";
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"Account service returned {(int)response.StatusCode} {response.ReasonPhrase} instead of JSON."
            : $"Account service returned {(int)response.StatusCode} {response.ReasonPhrase} instead of JSON: {body}";
    }

    private static Uri EnsureBaseAddress(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Account API URL must use HTTPS.");
        }
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    private static string BuildSyntheticEmail(string login, string userId)
    {
        var normalizedLogin = new string(login
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '-')
            .ToArray())
            .Trim('.', '-', '_');
        if (string.IsNullOrWhiteSpace(normalizedLogin))
        {
            normalizedLogin = "user";
        }

        var suffix = string.IsNullOrWhiteSpace(userId) ? Guid.NewGuid().ToString("N")[..8] : userId.Trim()[..Math.Min(8, userId.Trim().Length)];
        return $"{normalizedLogin}.{suffix}@fluxchat.local";
    }
}

internal static class AccountEndpointResolver
{
    public static string FromRelayAddress(string relayAddress)
    {
        var value = relayAddress.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Enter your VPS address, for example 91.186.217.186:42800.");
        }

        var host = value;
        var separator = value.LastIndexOf(':');
        if (separator > 0 && value.IndexOf(':') == separator)
        {
            host = value[..separator];
        }

        if (System.Net.IPAddress.TryParse(host, out var address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            host = $"{host}.sslip.io";
        }

        return $"https://{host}:8444/";
    }
}
