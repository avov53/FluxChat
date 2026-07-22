using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FluxChat.Client;

internal sealed class GoogleDriveService(HttpClient httpClient, AppSettings settings)
{
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    private const long MaxFileSizeBytes = 5L * 1024 * 1024 * 1024;

    public bool IsConnected => !string.IsNullOrWhiteSpace(settings.GoogleDriveRefreshToken) ||
                               (!string.IsNullOrWhiteSpace(settings.GoogleDriveAccessToken) &&
                                settings.GoogleDriveAccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1));

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var clientId = GetClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Google Drive OAuth is not configured in this build. Set FLUXCHAT_GOOGLE_CLIENT_ID or enter the OAuth client ID in Settings > Data.");
        }

        var port = GetFreeLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/";
        var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var authorizeUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(DriveScope)}" +
            "&access_type=offline&prompt=consent" +
            $"&state={Uri.EscapeDataString(state)}";

        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
        var query = context.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];
        var error = query["error"];

        var responseText = string.IsNullOrWhiteSpace(error) && string.Equals(returnedState, state, StringComparison.Ordinal)
            ? "FluxChat connected to Google Drive. You can close this tab."
            : "FluxChat could not connect to Google Drive. Return to the app and try again.";
        var responseBytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>FluxChat</title><body style=\"font:16px Segoe UI;background:#202225;color:#f2f3f5;padding:32px\">{WebUtility.HtmlEncode(responseText)}</body>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes, timeout.Token);
        context.Response.Close();

        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException($"Google authorization failed: {error}");
        }
        if (!string.Equals(returnedState, state, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Google authorization response was invalid.");
        }

        var token = await ExchangeTokenAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            },
            timeout.Token);
        ApplyToken(token, keepExistingRefreshToken: false);
        settings.GoogleDriveAccountName = "Google Drive connected";
    }

    public void Disconnect()
    {
        settings.GoogleDriveRefreshToken = "";
        settings.GoogleDriveAccessToken = "";
        settings.GoogleDriveAccessTokenExpiresAtUtc = DateTimeOffset.MinValue;
        settings.GoogleDriveAccountName = "";
    }

    public async Task<GoogleDriveUploadResult> UploadAsync(
        string filePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The selected file no longer exists.", filePath);
        }
        if (file.Length <= 0 || file.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException("FluxChat supports Google Drive files from 1 byte up to 5 GB.");
        }

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var mimeType = GetMimeType(file.Extension);
        using var initiate = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,name,size,mimeType,webViewLink,webContentLink");
        initiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        initiate.Headers.TryAddWithoutValidation("X-Upload-Content-Type", mimeType);
        initiate.Headers.TryAddWithoutValidation("X-Upload-Content-Length", file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        initiate.Content = new StringContent(JsonSerializer.Serialize(new { name = file.Name }), Encoding.UTF8, "application/json");
        using var initiateResponse = await httpClient.SendAsync(initiate, cancellationToken);
        var initiateBody = await initiateResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!initiateResponse.IsSuccessStatusCode || initiateResponse.Headers.Location is null)
        {
            throw new InvalidOperationException($"Google Drive could not start the upload ({(int)initiateResponse.StatusCode}): {ReadGoogleError(initiateBody)}");
        }

        await using var source = file.OpenRead();
        using var upload = new HttpRequestMessage(HttpMethod.Put, initiateResponse.Headers.Location)
        {
            Content = new ProgressStreamContent(source, file.Length, progress, cancellationToken)
        };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        upload.Content.Headers.ContentLength = file.Length;
        using var uploadResponse = await httpClient.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Drive upload failed ({(int)uploadResponse.StatusCode}): {ReadGoogleError(uploadBody)}");
        }

        using var document = JsonDocument.Parse(uploadBody);
        var fileId = document.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("Google Drive did not return a file id.");
        await MakePublicReadableAsync(fileId, accessToken, cancellationToken);
        progress?.Report(1);
        return new GoogleDriveUploadResult(
            fileId,
            file.Name,
            file.Length,
            mimeType,
            BuildPublicDownloadUrl(fileId));
    }

    public async Task DownloadFileAsync(
        string fileId,
        string downloadUrl,
        string targetPath,
        long expectedSizeBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppPaths.VideoCacheDirectory);
        var partialPath = targetPath + ".part";
        HttpResponseMessage? response = null;
        try
        {
            if (IsConnected && !string.IsNullOrWhiteSpace(fileId))
            {
                try
                {
                    var accessToken = await GetAccessTokenAsync(cancellationToken);
                    var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    request.Dispose();
                    if (!response.IsSuccessStatusCode)
                    {
                        response.Dispose();
                        response = null;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
                {
                    response?.Dispose();
                    response = null;
                }
            }

            if (response is null)
            {
                var publicUrl = !string.IsNullOrWhiteSpace(fileId)
                    ? BuildPublicDownloadUrl(fileId)
                    : downloadUrl;
                if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                {
                    throw new InvalidOperationException("This video does not have a valid Google Drive link.");
                }

                response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }

            using (response)
            {
                var errorBody = response.IsSuccessStatusCode
                    ? ""
                    : await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Google Drive download failed ({(int)response.StatusCode}): {ReadGoogleError(errorBody)}");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Google Drive returned a web page instead of the video. Open the Drive link and check its sharing access.");
                }

                var totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault(expectedSizeBytes);
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(
                    partialPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    256 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[256 * 1024];
                long received = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (totalBytes > 0)
                    {
                        progress?.Report(Math.Clamp(received / (double)totalBytes, 0, 1));
                    }
                }

                await output.FlushAsync(cancellationToken);
                if (received <= 0 || (expectedSizeBytes > 0 && received != expectedSizeBytes))
                {
                    throw new IOException("The downloaded video is incomplete. Click it to retry.");
                }
            }

            File.Move(partialPath, targetPath, overwrite: true);
            progress?.Report(1);
        }
        catch
        {
            response?.Dispose();
            try
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
            }
            catch (IOException)
            {
            }
            throw;
        }
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new InvalidOperationException("This video no longer has a Google Drive file id.");
        }

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Google Drive delete failed ({(int)response.StatusCode}): {ReadGoogleError(body)}");
        }
    }

    public async Task<string> UploadPrivateSnapshotAsync(
        string filePath,
        string driveName,
        string existingFileId,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length <= 0)
        {
            throw new InvalidOperationException("The backup snapshot is empty.");
        }

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var hasExisting = !string.IsNullOrWhiteSpace(existingFileId);
        var endpoint = hasExisting
            ? $"https://www.googleapis.com/upload/drive/v3/files/{Uri.EscapeDataString(existingFileId)}?uploadType=resumable&fields=id"
            : "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id";
        using var initiate = new HttpRequestMessage(hasExisting ? HttpMethod.Patch : HttpMethod.Post, endpoint);
        initiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        initiate.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "application/zip");
        initiate.Headers.TryAddWithoutValidation("X-Upload-Content-Length", file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        initiate.Content = new StringContent(JsonSerializer.Serialize(new { name = driveName }), Encoding.UTF8, "application/json");
        using var initiateResponse = await httpClient.SendAsync(initiate, cancellationToken);
        var initiateBody = await initiateResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!initiateResponse.IsSuccessStatusCode || initiateResponse.Headers.Location is null)
        {
            throw new InvalidOperationException($"Google Drive could not start backup ({(int)initiateResponse.StatusCode}): {ReadGoogleError(initiateBody)}");
        }

        await using var source = file.OpenRead();
        using var upload = new HttpRequestMessage(HttpMethod.Put, initiateResponse.Headers.Location)
        {
            Content = new ProgressStreamContent(source, file.Length, progress, cancellationToken)
        };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        upload.Content.Headers.ContentLength = file.Length;
        using var uploadResponse = await httpClient.SendAsync(upload, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Drive backup failed ({(int)uploadResponse.StatusCode}): {ReadGoogleError(uploadBody)}");
        }

        using var document = JsonDocument.Parse(uploadBody);
        progress?.Report(1);
        return document.RootElement.GetProperty("id").GetString() ?? existingFileId;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.GoogleDriveAccessToken) &&
            settings.GoogleDriveAccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return settings.GoogleDriveAccessToken;
        }
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveRefreshToken))
        {
            throw new InvalidOperationException("Connect Google Drive in Settings > Data before sending files.");
        }

        var token = await ExchangeTokenAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = GetClientId(),
                ["refresh_token"] = settings.GoogleDriveRefreshToken,
                ["grant_type"] = "refresh_token"
            },
            cancellationToken);
        ApplyToken(token, keepExistingRefreshToken: true);
        await AppSettingsStore.SaveAsync(settings);
        return settings.GoogleDriveAccessToken;
    }

    private async Task<TokenResponse> ExchangeTokenAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        var clientSecret = Environment.GetEnvironmentVariable("FLUXCHAT_GOOGLE_CLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            values["client_secret"] = clientSecret;
        }

        using var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(values), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google token request failed ({(int)response.StatusCode}): {ReadGoogleError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new TokenResponse(
            root.GetProperty("access_token").GetString() ?? "",
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() ?? "" : "",
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600);
    }

    private void ApplyToken(TokenResponse token, bool keepExistingRefreshToken)
    {
        settings.GoogleDriveAccessToken = token.AccessToken;
        settings.GoogleDriveAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresInSeconds));
        if (!keepExistingRefreshToken || !string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            settings.GoogleDriveRefreshToken = token.RefreshToken;
        }
    }

    private async Task MakePublicReadableAsync(string fileId, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}/permissions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new { role = "reader", type = "anyone" }), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Drive sharing failed ({(int)response.StatusCode}): {ReadGoogleError(body)}");
        }
    }

    private string GetClientId()
        => string.IsNullOrWhiteSpace(settings.GoogleDriveClientId)
            ? Environment.GetEnvironmentVariable("FLUXCHAT_GOOGLE_CLIENT_ID") ?? ""
            : settings.GoogleDriveClientId.Trim();

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string ReadGoogleError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "Unknown error";
                if (error.TryGetProperty("message", out var message)) return message.GetString() ?? "Unknown error";
                if (error.TryGetProperty("error_description", out var description)) return description.GetString() ?? "Unknown error";
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(body) ? "Unknown error" : body[..Math.Min(body.Length, 300)];
    }

    private static string GetMimeType(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };

    private static string BuildPublicDownloadUrl(string fileId)
        => $"https://drive.usercontent.google.com/download?id={Uri.EscapeDataString(fileId)}&export=download&confirm=t";

    private sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds);
}

internal sealed record GoogleDriveUploadResult(
    string FileId,
    string FileName,
    long FileSizeBytes,
    string MimeType,
    string DownloadUrl);

internal sealed class ProgressStreamContent(
    Stream source,
    long length,
    IProgress<double>? progress,
    CancellationToken cancellationToken) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[128 * 1024];
        long sent = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sent += read;
            progress?.Report(length == 0 ? 1 : Math.Clamp(sent / (double)length, 0, 1));
        }
    }

    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = length;
        return true;
    }
}
