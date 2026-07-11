using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    private const string CompanionFileName = "ffmpeg.exe";
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
        var archiveAsset = FindArchiveAsset(release.Assets, latestVersion);
        var exeAsset = release.Assets.FirstOrDefault(x =>
            string.Equals(x.Name, ClientAssetName, StringComparison.OrdinalIgnoreCase));

        if (latestVersion < currentVersion)
        {
            return null;
        }

        if (latestVersion == currentVersion)
        {
            if (archiveAsset is not null
                && !string.IsNullOrWhiteSpace(archiveAsset.BrowserDownloadUrl)
                && IsCompanionFileMissing())
            {
                return new UpdateInfo(latestVersion, release.HtmlUrl, new Uri(archiveAsset.BrowserDownloadUrl), archiveAsset.Name, true, true);
            }

            return null;
        }

        var preferredAsset = archiveAsset ?? exeAsset;
        if (preferredAsset is null || string.IsNullOrWhiteSpace(preferredAsset.BrowserDownloadUrl))
        {
            AppLog.Write($"Update check found {release.TagName}, but a FluxChat release asset is missing.");
            return new UpdateInfo(latestVersion, release.HtmlUrl, null, string.Empty, false, false);
        }

        var isArchive = preferredAsset == archiveAsset;
        return new UpdateInfo(latestVersion, release.HtmlUrl, new Uri(preferredAsset.BrowserDownloadUrl), preferredAsset.Name, isArchive, false);
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
        if (Directory.Exists(updateDirectory))
        {
            Directory.Delete(updateDirectory, true);
        }

        Directory.CreateDirectory(updateDirectory);
        var downloadName = update.IsArchive ? "FluxChatUpdate.zip" : ClientAssetName;
        var downloadedFile = Path.Combine(updateDirectory, downloadName);

        using (var client = CreateClient())
        await using (var source = await client.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (var destination = File.Create(downloadedFile))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        var sourcePath = downloadedFile;
        var copyWholeDirectory = false;
        if (update.IsArchive)
        {
            var extractDirectory = Path.Combine(updateDirectory, "extracted");
            ZipFile.ExtractToDirectory(downloadedFile, extractDirectory, true);
            sourcePath = FindExtractedAppDirectory(extractDirectory);
            copyWholeDirectory = true;
        }

        var scriptPath = Path.Combine(updateDirectory, "install-fluxchat-update.ps1");
        var processId = Environment.ProcessId;
        var targetDirectory = Path.GetDirectoryName(currentExePath)
            ?? throw new InvalidOperationException("Current executable directory is unavailable.");
        var script = copyWholeDirectory
            ? BuildDirectoryUpdateScript(processId, sourcePath, targetDirectory, updateDirectory)
            : BuildExecutableUpdateScript(processId, sourcePath, currentExePath, updateDirectory);
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static GitHubAsset? FindArchiveAsset(IReadOnlyList<GitHubAsset> assets, Version version)
    {
        var versionText = version.ToString(3);
        return assets.FirstOrDefault(x =>
            x.Name.StartsWith("FluxChat", StringComparison.OrdinalIgnoreCase)
            && x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            && x.Name.Contains(versionText, StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(x =>
                x.Name.StartsWith("FluxChat", StringComparison.OrdinalIgnoreCase)
                && x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCompanionFileMissing()
        => !File.Exists(Path.Combine(AppContext.BaseDirectory, CompanionFileName));

    private static string FindExtractedAppDirectory(string extractDirectory)
    {
        var executable = Directory.EnumerateFiles(extractDirectory, ClientAssetName, SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();
        if (executable is null)
        {
            throw new InvalidOperationException($"The update archive does not contain {ClientAssetName}.");
        }

        return Path.GetDirectoryName(executable)
            ?? throw new InvalidOperationException("The update archive layout is invalid.");
    }

    private static string BuildDirectoryUpdateScript(int processId, string sourceDirectory, string targetDirectory, string updateDirectory)
    {
        var targetExe = Path.Combine(targetDirectory, ClientAssetName);
        var script = $"""
$ErrorActionPreference = 'Stop'
$processId = {processId}
$source = '{EscapePowerShell(sourceDirectory)}'
$targetDirectory = '{EscapePowerShell(targetDirectory)}'
$targetExe = '{EscapePowerShell(targetExe)}'
$updateDirectory = '{EscapePowerShell(updateDirectory)}'
Wait-Process -Id $processId -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $targetDirectory -Recurse -Force
Start-Process -FilePath $targetExe
Start-Sleep -Milliseconds 300
Remove-Item -LiteralPath $updateDirectory -Recurse -Force -ErrorAction SilentlyContinue
""";
        return script;
    }

    private static string BuildExecutableUpdateScript(int processId, string sourceExe, string targetExe, string updateDirectory)
    {
        var script = $"""
$ErrorActionPreference = 'Stop'
$processId = {processId}
$source = '{EscapePowerShell(sourceExe)}'
$target = '{EscapePowerShell(targetExe)}'
$updateDirectory = '{EscapePowerShell(updateDirectory)}'
Wait-Process -Id $processId -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Copy-Item -LiteralPath $source -Destination $target -Force
Start-Process -FilePath $target
Start-Sleep -Milliseconds 300
Remove-Item -LiteralPath $updateDirectory -Recurse -Force -ErrorAction SilentlyContinue
""";
        return script;
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

internal sealed record UpdateInfo(Version Version, Uri ReleasePage, Uri? DownloadUrl, string AssetName, bool IsArchive, bool IsRepair);
