using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed class ServerHistoryClient(string apiUrl, string sessionToken)
{
    private readonly HttpClient _http = CreateHttpClient(apiUrl, sessionToken);

    public async Task ArchiveAsync(ChatPacket packet, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/history/archive", packet, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ChatPacket>> LoadAsync(string peerUserId, CancellationToken cancellationToken)
    {
        var result = await _http.GetFromJsonAsync<List<ChatPacket>>($"api/v1/history/{Uri.EscapeDataString(peerUserId)}?take=120", cancellationToken);
        return result ?? [];
    }

    public async Task<IReadOnlyList<AccountReadState>> LoadReadStatesAsync(CancellationToken cancellationToken)
    {
        var result = await _http.GetFromJsonAsync<AccountReadStatesResponse>("api/v1/read-states", cancellationToken);
        return result?.Accepted == true ? result.ReadStates ?? [] : [];
    }

    public async Task<AccountResult> MarkReadAsync(AccountMarkReadRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/read-states/mark-read", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken)
               ?? new AccountResult(false, response.ReasonPhrase ?? "Read-state sync did not return a result.");
    }

    public async Task<AccountResult> DeleteConversationAsync(string peerUserId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/history/delete", new AccountHistoryDeleteRequest(peerUserId), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken)
               ?? new AccountResult(false, response.ReasonPhrase ?? "History delete did not return a result.");
    }

    public async Task<AccountResult> DeleteMessageAsync(
        Guid messageId,
        IReadOnlyList<string> mediaIds,
        CancellationToken cancellationToken,
        string serverId = "",
        string channelId = "")
    {
        using var response = await _http.PostAsJsonAsync("api/v1/messages/delete", new AccountMessageDeleteRequest(messageId, mediaIds, serverId, channelId), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken)
               ?? new AccountResult(false, response.ReasonPhrase ?? "Message delete did not return a result.");
    }

    public async Task<AccountResult> EditMessageAsync(Guid messageId, ChatPacket replacementPacket, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/messages/edit", new AccountMessageEditRequest(messageId, replacementPacket), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken)
               ?? new AccountResult(false, response.ReasonPhrase ?? "Message edit did not return a result.");
    }

    public async Task<IReadOnlyList<AccountSyncedContact>> LoadContactsAsync(CancellationToken cancellationToken)
    {
        var result = await _http.GetFromJsonAsync<AccountContactsResponse>("api/v1/contacts", cancellationToken);
        return result?.Accepted == true ? result.Contacts ?? [] : [];
    }

    public async Task<AccountResult> UpsertContactAsync(AccountSyncedContact contact, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/contacts/upsert", new AccountContactUpsertRequest(contact), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken)
               ?? new AccountResult(false, response.ReasonPhrase ?? "Contact sync did not return a result.");
    }

    public async Task<AccountResult> DeleteContactAsync(string userId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/contacts/delete", new AccountContactDeleteRequest(userId), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken)
               ?? new AccountResult(false, response.ReasonPhrase ?? "Contact sync did not return a result.");
    }

    public async Task<AccountMediaUploadResponse> UploadMediaAsync(
        string kind,
        string fileName,
        string mimeType,
        byte[] bytes,
        CancellationToken cancellationToken)
        => await UploadBytesAsync("api/v1/media/upload", kind, fileName, mimeType, bytes, cancellationToken);

    public async Task<AccountResult> DeleteMediaAsync(string mediaId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/media/delete", new AccountMediaDeleteRequest(mediaId), cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountResult>(cancellationToken: cancellationToken)
               ?? new AccountResult(false, response.ReasonPhrase ?? "Media delete did not return a result.");
    }

    public async Task<AccountMediaUploadResponse> UploadAvatarAsync(
        string avatarKind,
        string fileName,
        string mimeType,
        byte[] bytes,
        CancellationToken cancellationToken)
        => await UploadBytesAsync("api/v1/profile/avatar", avatarKind, fileName, mimeType, bytes, cancellationToken);

    public async Task<string?> DownloadMediaAsync(string mediaId, string preferredExtension, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) return null;
        var extension = NormalizeExtension(preferredExtension);
        AppPaths.EnsureMediaCacheDirectoryCreated();
        var path = Path.Combine(AppPaths.MediaCacheDirectory, $"{mediaId}{extension}");
        try
        {
            var cachedFile = new FileInfo(path);
            if (cachedFile.Exists && cachedFile.Length > 0)
            {
                cachedFile.LastAccessTimeUtc = DateTime.UtcNow;
                return path;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        using var response = await _http.GetAsync($"api/v1/media/{Uri.EscapeDataString(mediaId)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var file = File.Create(path);
        await response.Content.CopyToAsync(file, cancellationToken);
        return path;
    }

    public async Task<string?> DownloadAvatarAsync(string userId, string preferredExtension, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        using var response = await _http.GetAsync($"api/v1/profile/{Uri.EscapeDataString(userId)}/avatar", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        AppPaths.EnsureAvatarDirectoryCreated();
        var extension = NormalizeExtension(preferredExtension);
        var version = response.Headers.TryGetValues("X-FluxChat-Avatar-Version", out var values)
            ? values.FirstOrDefault() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var path = Path.Combine(AppPaths.AvatarDirectory, $"server-{userId}-{version}{extension}");
        await using var file = File.Create(path);
        await response.Content.CopyToAsync(file, cancellationToken);
        return path;
    }

    private async Task<AccountMediaUploadResponse> UploadBytesAsync(
        string path,
        string kind,
        string fileName,
        string mimeType,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
        content.Headers.Add("X-FluxChat-Media-Kind", kind);
        content.Headers.Add("X-FluxChat-File-Name", fileName);
        using var response = await _http.PostAsync(path, content, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AccountMediaUploadResponse>(cancellationToken: cancellationToken)
               ?? new AccountMediaUploadResponse(false, response.ReasonPhrase ?? "Media service did not return a result.");
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return ".bin";
        extension = extension.Trim();
        if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension;
        extension = string.Concat(extension.Where(c => char.IsAsciiLetterOrDigit(c) || c == '.'));
        return string.IsNullOrWhiteSpace(extension) || extension == "." ? ".bin" : extension.ToLowerInvariant();
    }

    private static HttpClient CreateHttpClient(string apiUrl, string token)
    {
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Account API URL must use HTTPS.");
        }
        var client = new HttpClient { BaseAddress = uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/"), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
