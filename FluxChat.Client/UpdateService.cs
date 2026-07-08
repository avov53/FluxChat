using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxChat.Client;

internal static class UpdateService
{
    private const string RepositoryOwner = "avov53";
    private const string RepositoryName = "FluxChat";
    private const string ClientAssetName = "FluxChat.exe";
    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");

    public static async Task<UpdateInfo?> CheckLatestAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(LatestReleaseUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            AppLog.Write($"Update check skipped: GitHub returned {(int)response.StatusCode}");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
        if (release is null || release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName))
        {
            return null;
        }

        var latestVersion = ParseVersion(release.TagName);
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        if (latestVersion <= currentVersion)
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(x =>
            string.Equals(x.Name, ClientAssetName, StringComparison.OrdinalIgnoreCase));
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            AppLog.Write($"Update check found {release.TagName}, but {ClientAssetName} asset is missing.");
            return new UpdateInfo(latestVersion, release.HtmlUrl, null);
        }

        return new UpdateInfo(latestVersion, release.HtmlUrl, new Uri(asset.BrowserDownloadUrl));
    }

    public static async Task DownloadAndInstallAsync(UpdateInfo update, CancellationToken cancellationToken)
    {
        if (update.DownloadUrl is null)
        {
            OpenReleasePage(update.ReleasePage);
            return;
        }

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            throw new InvalidOperationException("Current executable path is unavailable.");
        }
        var currentExePath = currentExe;

        var updateDirectory = Path.Combine(Path.GetTempPath(), "FluxChatUpdate");
        Directory.CreateDirectory(updateDirectory);
        var downloadedExe = Path.Combine(updateDirectory, ClientAssetName);

        using (var client = CreateClient())
        await using (var source = await client.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (var destination = File.Create(downloadedExe))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        var scriptPath = Path.Combine(updateDirectory, "install-fluxchat-update.ps1");
        var processId = Environment.ProcessId;
        var script = $"""
$ErrorActionPreference = 'Stop'
$processId = {processId}
$source = '{EscapePowerShell(downloadedExe)}'
$target = '{EscapePowerShell(currentExePath)}'
Wait-Process -Id $processId -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Copy-Item -LiteralPath $source -Destination $target -Force
Start-Process -FilePath $target
Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
""";
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FluxChat-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static Version ParseVersion(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var metadataIndex = value.IndexOfAny(['-', '+']);
        if (metadataIndex >= 0)
        {
            value = value[..metadataIndex];
        }

        return Version.TryParse(value, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static void OpenReleasePage(Uri releasePage)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = releasePage.ToString(),
            UseShellExecute = true
        });
    }

    private static string EscapePowerShell(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] Uri HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}

internal sealed record UpdateInfo(Version Version, Uri ReleasePage, Uri? DownloadUrl);
