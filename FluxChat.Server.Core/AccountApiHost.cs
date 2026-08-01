using System.Net;
using System.Text;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Server.Core;

public sealed class AccountApiHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxJsonRequestBytes = (3 * 1024 * 1024);
    private const int MaxMediaRequestBytes = 25 * 1024 * 1024;
    private readonly SemaphoreSlim _requestSlots = new(96, 96);
    private readonly RequestRateLimiter _rateLimiter = new();
    private readonly HttpListener _listener = new();
    private readonly AccountStore _store;
    private readonly RelayDatabase _relayDatabase;
    private readonly FederationUsernamePublisher? _federationPublisher;
    private readonly int _retentionDays;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private Task? _federationReplay;
    private Task? _retentionLoop;

    public AccountApiHost(
        AccountStore store,
        RelayDatabase relayDatabase,
        string prefix,
        FederationUsernamePublisher? federationPublisher = null,
        int retentionDays = 730)
    {
        _store = store;
        _relayDatabase = relayDatabase;
        _federationPublisher = federationPublisher;
        _retentionDays = Math.Max(1, retentionDays);
        _listener.Prefixes.Add(prefix.EndsWith('/') ? prefix : prefix + "/");
    }

    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(ListenAsync);
        if (_federationPublisher?.IsEnabled == true)
        {
            _federationReplay = Task.Run(ReplayFederationClaimsAsync);
        }
        _retentionLoop = Task.Run(RunRetentionAsync);
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (HttpListenerException) when (_stop.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            await _requestSlots.WaitAsync(_stop.Token);
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(context, _stop.Token);
                }
                finally
                {
                    _requestSlots.Release();
                }
            });
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            AddSecurityHeaders(context.Response);
            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            var clientIp = GetClientIp(context.Request);
            if (context.Request.HttpMethod == "GET" && path == "/health")
            {
                await WriteJsonAsync(context, HttpStatusCode.OK, new { status = "ok", emailRecovery = false }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/v1/accounts/health")
            {
                await WriteJsonAsync(context, HttpStatusCode.OK, new { status = "ok", emailRecovery = false }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path.StartsWith("/api/v1/history/", StringComparison.Ordinal))
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                if (!await TryRateLimitAsync(context, $"history:{session.UserId}", 120, TimeSpan.FromMinutes(1), cancellationToken)) return;
                var peerUserId = Uri.UnescapeDataString(path["/api/v1/history/".Length..]);
                var take = int.TryParse(context.Request.QueryString["take"], out var value) ? value : 80;
                var messages = await _store.LoadConversationAsync(session.UserId, peerUserId, take, cancellationToken);
                await WriteJsonAsync(context, HttpStatusCode.OK, messages, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/v1/accounts/session")
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    new AccountSessionResponse(true, "Session is valid.", session.UserId, session.DisplayName, session.Login, null, session.ExpiresAtUtc, session.SessionId, session.DeviceName),
                    cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/v1/accounts/sessions")
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                if (!await TryRateLimitAsync(context, $"sessions:{session.UserId}", 60, TimeSpan.FromMinutes(1), cancellationToken)) return;
                var sessions = await _store.ListSessionsAsync(session, cancellationToken);
                await WriteJsonAsync(context, HttpStatusCode.OK, new AccountDeviceSessionsResponse(true, "Device sessions loaded.", sessions), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/v1/contacts")
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                if (!await TryRateLimitAsync(context, $"contacts:{session.UserId}", 90, TimeSpan.FromMinutes(1), cancellationToken)) return;
                var contacts = await _store.ListContactsAsync(session, cancellationToken);
                await WriteJsonAsync(context, HttpStatusCode.OK, new AccountContactsResponse(true, "Contacts loaded.", contacts), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/v1/read-states")
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                if (!await TryRateLimitAsync(context, $"read-states:{session.UserId}", 120, TimeSpan.FromMinutes(1), cancellationToken)) return;
                var states = await _store.LoadReadStatesAsync(session, cancellationToken);
                await WriteJsonAsync(context, HttpStatusCode.OK, new AccountReadStatesResponse(true, "Read states loaded.", states), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/v1/accounts/preferences")
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                if (!await TryRateLimitAsync(context, $"preferences:{session.UserId}", 60, TimeSpan.FromMinutes(1), cancellationToken)) return;
                var preferences = await _store.LoadPreferencesAsync(session, cancellationToken);
                await WriteJsonAsync(context, HttpStatusCode.OK, new AccountPreferencesResponse(true, "Account preferences loaded.", preferences), cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path.StartsWith("/api/v1/media/", StringComparison.Ordinal))
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                if (!Guid.TryParse(path["/api/v1/media/".Length..], out var mediaId))
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new AccountResult(false, "Media id is invalid."), cancellationToken);
                    return;
                }

                var media = await _store.LoadMediaAsync(mediaId, session.UserId, cancellationToken);
                if (media is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.NotFound, new AccountResult(false, "Media not found."), cancellationToken);
                    return;
                }

                await WriteBytesAsync(context, HttpStatusCode.OK, media.MimeType, media.Bytes, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" &&
                path.StartsWith("/api/v1/profile/", StringComparison.Ordinal) &&
                path.EndsWith("/avatar", StringComparison.Ordinal))
            {
                var session = await RequireSessionAsync(context, cancellationToken);
                if (session is null) return;
                var userId = Uri.UnescapeDataString(path["/api/v1/profile/".Length..^"/avatar".Length].Trim('/'));
                var avatar = await _store.LoadAvatarAsync(userId, cancellationToken);
                if (avatar is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.NotFound, new AccountResult(false, "Avatar not found."), cancellationToken);
                    return;
                }

                context.Response.Headers["X-FluxChat-Avatar-Kind"] = avatar.AvatarKind;
                context.Response.Headers["X-FluxChat-Avatar-Version"] = avatar.AvatarVersion.ToString();
                await WriteBytesAsync(context, HttpStatusCode.OK, avatar.MimeType, avatar.Bytes, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod != "POST")
            {
                await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed, new AccountResult(false, "Method not allowed."), cancellationToken);
                return;
            }

            switch (path)
            {
                case "/api/v1/accounts/register":
                {
                    var request = await ReadJsonAsync<AccountRegisterRequest>(context, cancellationToken);
                    if (!await TryRateLimitAsync(context, $"register:{clientIp}", 5, TimeSpan.FromHours(1), cancellationToken)) return;
                    var bootstrapToken = context.Request.Headers["X-FluxChat-Bootstrap-Token"];
                    if (!_relayDatabase.IsCredentialValid(request.UserId, bootstrapToken ?? ""))
                    {
                        await WriteJsonAsync(context, HttpStatusCode.Unauthorized,
                            new AccountResult(false, "A valid VPS invite is required to create an account."), cancellationToken);
                        return;
                    }
                    var result = await _store.RegisterAsync(request.UserId, request.DisplayName, request.Login, request.Email, request.Password, request.PublicKey, cancellationToken);
                    if (result.Accepted && result.ClaimedAtUtc is not null && _federationPublisher?.IsEnabled == true)
                    {
                        await _federationPublisher.PublishAsync(new FederationUsernameClaim(request.Login.Trim().ToLowerInvariant(), request.UserId, Environment.GetEnvironmentVariable("FLUXCHAT_FEDERATION_SERVER_ID") ?? Environment.MachineName, result.ClaimedAtUtc.Value), cancellationToken);
                    }
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, new AccountResult(result.Accepted, result.Message), cancellationToken);
                    return;
                }
                case "/api/v1/accounts/login":
                {
                    var request = await ReadJsonAsync<AccountLoginRequest>(context, cancellationToken);
                    var identifier = request.LoginOrEmail.Trim().ToLowerInvariant();
                    var ipKey = $"login-ip:{clientIp}";
                    var loginKey = $"login-id:{identifier}";
                    if (!await TryCheckRateLimitAsync(context, ipKey, 30, TimeSpan.FromMinutes(15), cancellationToken) ||
                        !await TryCheckRateLimitAsync(context, loginKey, 12, TimeSpan.FromMinutes(15), cancellationToken)) return;
                    var result = await _store.LoginAsync(request.LoginOrEmail, request.Password, request.DeviceName, clientIp, cancellationToken);
                    if (result.Accepted)
                    {
                        _rateLimiter.Reset(ipKey);
                        _rateLimiter.Reset(loginKey);
                    }
                    else
                    {
                        _rateLimiter.Record(ipKey, TimeSpan.FromMinutes(15));
                        _rateLimiter.Record(loginKey, TimeSpan.FromMinutes(15));
                    }
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, ToResponse(result), cancellationToken);
                    return;
                }
                case "/api/v1/accounts/request-code":
                {
                    var request = await ReadJsonAsync<AccountCodeRequest>(context, cancellationToken);
                    if (string.Equals(request.Purpose, "verify-email", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteJsonAsync(
                            context,
                            HttpStatusCode.OK,
                            new AccountResult(true, "Email verification is disabled on this VPS. Continue signing in with your password."),
                            cancellationToken);
                        return;
                    }

                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.BadRequest,
                        new AccountResult(false, "Email recovery is disabled on this VPS. Sign in with your password."),
                        cancellationToken);
                    return;
                }
                case "/api/v1/accounts/login-code":
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.BadRequest,
                        new AccountResult(false, "Email code sign-in is disabled on this VPS. Sign in with your password."),
                        cancellationToken);
                    return;
                }
                case "/api/v1/accounts/reset-password":
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.BadRequest,
                        new AccountResult(false, "Password reset by email is disabled on this VPS."),
                        cancellationToken);
                    return;
                }
                case "/api/v1/accounts/verify-email":
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        new AccountResult(true, "Email verification is disabled on this VPS."),
                        cancellationToken);
                    return;
                }
                case "/api/v1/accounts/sessions/revoke":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"session-revoke:{session.UserId}", 30, TimeSpan.FromMinutes(5), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountSessionRevokeRequest>(context, cancellationToken);
                    var result = await _store.RevokeSessionAsync(session, request.SessionId, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/accounts/sessions/revoke-all":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"session-revoke-all:{session.UserId}", 10, TimeSpan.FromMinutes(10), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountSessionRevokeAllRequest>(context, cancellationToken);
                    var result = await _store.RevokeSessionsAsync(session, request.IncludeCurrent, cancellationToken);
                    await WriteJsonAsync(context, HttpStatusCode.OK, result, cancellationToken);
                    return;
                }
                case "/api/v1/accounts/change-password":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"change-password:{session.UserId}", 10, TimeSpan.FromHours(1), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountChangePasswordRequest>(context, cancellationToken);
                    var result = await _store.ChangePasswordAsync(session, request.CurrentPassword, request.NewPassword, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/accounts/preferences":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"preferences-save:{session.UserId}", 60, TimeSpan.FromMinutes(1), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountPreferencesUpdateRequest>(context, cancellationToken);
                    var result = await _store.SavePreferencesAsync(session, request, cancellationToken);
                    await WriteJsonAsync(context, HttpStatusCode.OK, result, cancellationToken);
                    return;
                }
                case "/api/v1/accounts/delete":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"delete-account:{session.UserId}", 5, TimeSpan.FromHours(1), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountDeleteRequest>(context, cancellationToken);
                    var result = await _store.DeleteAccountAsync(session, request.CurrentPassword, request.Confirmation, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/contacts/upsert":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"contacts-upsert:{session.UserId}", 120, TimeSpan.FromMinutes(1), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountContactUpsertRequest>(context, cancellationToken);
                    var result = await _store.UpsertContactAsync(session, request.Contact, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/contacts/delete":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"contacts-delete:{session.UserId}", 120, TimeSpan.FromMinutes(1), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountContactDeleteRequest>(context, cancellationToken);
                    var result = await _store.DeleteContactAsync(session, request.UserId, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/history/archive":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"archive:{session.UserId}", 240, TimeSpan.FromMinutes(1), cancellationToken)) return;
                    var packet = await ReadJsonAsync<ChatPacket>(context, cancellationToken);
                    await _store.ArchiveMessageAsync(session.UserId, packet, cancellationToken);
                    await WriteJsonAsync(context, HttpStatusCode.Created, new AccountResult(true, "Message archived."), cancellationToken);
                    return;
                }
                case "/api/v1/read-states/mark-read":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"mark-read:{session.UserId}", 240, TimeSpan.FromMinutes(1), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountMarkReadRequest>(context, cancellationToken);
                    var result = await _store.MarkReadAsync(session, request, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/history/delete":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"history-delete:{session.UserId}", 60, TimeSpan.FromMinutes(5), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountHistoryDeleteRequest>(context, cancellationToken);
                    var result = await _store.DeleteConversationAsync(session.UserId, request.PeerUserId, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/media/upload":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"media:{session.UserId}", 120, TimeSpan.FromMinutes(10), cancellationToken)) return;
                    var bytes = await ReadBytesAsync(context, MaxMediaRequestBytes, cancellationToken);
                    var kind = context.Request.Headers["X-FluxChat-Media-Kind"] ?? "message";
                    var fileName = context.Request.Headers["X-FluxChat-File-Name"] ?? "media.bin";
                    var mimeType = context.Request.ContentType ?? "application/octet-stream";
                    var media = await _store.StoreMediaAsync(session.UserId, kind, fileName, mimeType, bytes, cancellationToken);
                    await WriteJsonAsync(context, HttpStatusCode.Created, new AccountMediaUploadResponse(
                        true, "Media uploaded.", media.MediaId.ToString("N"), null, media.MimeType, media.ByteLength), cancellationToken);
                    return;
                }
                case "/api/v1/media/delete":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"media-delete:{session.UserId}", 120, TimeSpan.FromMinutes(10), cancellationToken)) return;
                    var request = await ReadJsonAsync<AccountMediaDeleteRequest>(context, cancellationToken);
                    var result = await _store.DeleteMediaAsync(session, request.MediaId, cancellationToken);
                    await WriteJsonAsync(context, result.Accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result, cancellationToken);
                    return;
                }
                case "/api/v1/profile/avatar":
                {
                    var session = await RequireSessionAsync(context, cancellationToken);
                    if (session is null) return;
                    if (!await TryRateLimitAsync(context, $"avatar:{session.UserId}", 20, TimeSpan.FromMinutes(10), cancellationToken)) return;
                    var bytes = await ReadBytesAsync(context, MaxMediaRequestBytes, cancellationToken);
                    var kind = context.Request.Headers["X-FluxChat-Media-Kind"] ?? "image";
                    var fileName = context.Request.Headers["X-FluxChat-File-Name"] ?? "avatar.bin";
                    var mimeType = context.Request.ContentType ?? "application/octet-stream";
                    var media = await _store.StoreAvatarAsync(session.UserId, kind, fileName, mimeType, bytes, cancellationToken);
                    await WriteJsonAsync(context, HttpStatusCode.Created, new AccountMediaUploadResponse(
                        true, "Avatar uploaded.", media.MediaId.ToString("N"), null, media.MimeType, media.ByteLength), cancellationToken);
                    return;
                }
                default:
                    await WriteJsonAsync(context, HttpStatusCode.NotFound, new AccountResult(false, "Endpoint not found."), cancellationToken);
                    return;
            }
        }
        catch (InvalidDataException ex)
        {
            await WriteJsonAsync(context, HttpStatusCode.RequestEntityTooLarge, new AccountResult(false, ex.Message), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new AccountResult(false, ex.Message), cancellationToken);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(context, HttpStatusCode.BadRequest, new AccountResult(false, "Invalid JSON request."), cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new AccountResult(false, "Account service failed."), cancellationToken);
            Console.Error.WriteLine($"Account API error: {ex}");
        }
    }

    private async Task ReplayFederationClaimsAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            do
            {
                foreach (var claim in await _store.GetFederationClaimsAsync(_stop.Token))
                {
                    await _federationPublisher!.PublishAsync(claim, _stop.Token);
                }
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Federation claim replay failed: {ex.Message}");
        }
    }

    private async Task RunRetentionAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
            do
            {
                var result = await _store.DeleteExpiredAsync(_retentionDays, _stop.Token);
                if (result.MessagesDeleted > 0 || result.MediaDeleted > 0)
                {
                    Console.WriteLine($"Retention cleanup: {result.MessagesDeleted} messages, {result.MediaDeleted} media records deleted.");
                }
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Retention cleanup failed: {ex.Message}");
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength64 > MaxJsonRequestBytes)
        {
            throw new InvalidDataException($"Request exceeds the {MaxJsonRequestBytes / 1024} KB limit.");
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await context.Request.InputStream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxJsonRequestBytes)
            {
                throw new InvalidDataException($"Request exceeds the {MaxJsonRequestBytes / 1024} KB limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(buffer, JsonOptions, cancellationToken)
               ?? throw new JsonException();
    }

    private static async Task<byte[]> ReadBytesAsync(HttpListenerContext context, int maxBytes, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength64 > maxBytes)
        {
            throw new InvalidDataException($"Request exceeds the {maxBytes / 1024 / 1024} MB limit.");
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await context.Request.InputStream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException($"Request exceeds the {maxBytes / 1024 / 1024} MB limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private async Task<AccountSession?> RequireSessionAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var header = context.Request.Headers["Authorization"];
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new AccountResult(false, "Account session is required."), cancellationToken);
            return null;
        }
        var session = await _store.ValidateSessionAsync(header[prefix.Length..].Trim(), GetClientIp(context.Request), cancellationToken);
        if (session is null)
        {
            await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new AccountResult(false, "Account session is invalid or expired."), cancellationToken);
        }
        return session;
    }

    private static AccountSessionResponse ToResponse(AccountLoginResult value)
        => new(value.Accepted, value.Message, value.UserId, value.DisplayName, value.Login, value.Token, value.ExpiresAtUtc, value.SessionId, value.DeviceName);

    private static async Task WriteJsonAsync(HttpListenerContext context, HttpStatusCode status, object body, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private static async Task WriteBytesAsync(HttpListenerContext context, HttpStatusCode status, string contentType, byte[] bytes, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private async Task<bool> TryRateLimitAsync(
        HttpListenerContext context,
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        if (_rateLimiter.TryAcquire(key, limit, window, out var retryAfter)) return true;
        await WriteRateLimitResponseAsync(context, retryAfter, cancellationToken);
        return false;
    }

    private async Task<bool> TryCheckRateLimitAsync(
        HttpListenerContext context,
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        if (_rateLimiter.TryCheck(key, limit, window, out var retryAfter)) return true;
        await WriteRateLimitResponseAsync(context, retryAfter, cancellationToken);
        return false;
    }

    private static async Task WriteRateLimitResponseAsync(
        HttpListenerContext context,
        TimeSpan retryAfter,
        CancellationToken cancellationToken)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers["Retry-After"] = seconds.ToString();
        var wait = seconds >= 60
            ? $"{seconds / 60} min {seconds % 60:D2} sec"
            : $"{seconds} sec";
        await WriteJsonAsync(
            context,
            HttpStatusCode.TooManyRequests,
            new AccountResult(false, $"Too many attempts. Try again in {wait}."),
            cancellationToken);
    }

    private static string GetClientIp(HttpListenerRequest request)
    {
        var remote = request.RemoteEndPoint?.Address;
        if (remote is not null && IPAddress.IsLoopback(remote))
        {
            var forwarded = request.Headers["X-Forwarded-For"]?.Split(',', 2)[0].Trim();
            if (IPAddress.TryParse(forwarded, out var parsed)) return parsed.ToString();
        }
        return remote?.ToString() ?? "unknown";
    }

    private static void AddSecurityHeaders(HttpListenerResponse response)
    {
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_listener.IsListening) _listener.Stop();
        if (_loop is not null) await _loop;
        if (_federationReplay is not null) await _federationReplay;
        if (_retentionLoop is not null) await _retentionLoop;
        _listener.Close();
        _requestSlots.Dispose();
        _stop.Dispose();
    }
}

internal sealed class RequestRateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _attempts = new(StringComparer.Ordinal);

    public bool TryAcquire(string key, int limit, TimeSpan window, out TimeSpan retryAfter)
    {
        if (!TryCheck(key, limit, window, out retryAfter)) return false;
        Record(key, window);
        return true;
    }

    public bool TryCheck(string key, int limit, TimeSpan window, out TimeSpan retryAfter)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!_attempts.TryGetValue(key, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _attempts[key] = queue;
            }

            var cutoff = now - window;
            while (queue.TryPeek(out var oldest) && oldest <= cutoff) queue.Dequeue();
            if (queue.Count >= limit)
            {
                retryAfter = queue.Peek().Add(window) - now;
                return false;
            }

            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public void Record(string key, TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (!_attempts.TryGetValue(key, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _attempts[key] = queue;
            }

            var cutoff = now - window;
            while (queue.TryPeek(out var oldest) && oldest <= cutoff) queue.Dequeue();
            queue.Enqueue(now);

            if (_attempts.Count > 20_000)
            {
                var staleCutoff = now.AddDays(-1);
                foreach (var stale in _attempts
                             .Where(x => x.Value.Count == 0 || x.Value.Last() < staleCutoff)
                             .Select(x => x.Key)
                             .Take(10_000)
                             .ToArray())
                {
                    _attempts.Remove(stale);
                }
            }
        }
    }

    public void Reset(string key)
    {
        lock (_gate)
        {
            _attempts.Remove(key);
        }
    }
}
