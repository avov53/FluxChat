using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace FluxChat.Client;

internal static class VideoCacheStore
{
    private const long MaxCacheBytes = 20L * 1024 * 1024 * 1024;

    public static string GetPath(MessageViewModel message)
    {
        AppPaths.EnsureVideoCacheDirectoryCreated();
        var identity = string.IsNullOrWhiteSpace(message.DriveFileId)
            ? $"{message.DownloadUrl}|{message.FileName}|{message.FileSizeBytes}"
            : message.DriveFileId;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var extension = Path.GetExtension(message.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12)
        {
            extension = message.MimeType.ToLowerInvariant() switch
            {
                "video/mp4" => ".mp4",
                "video/webm" => ".webm",
                "video/quicktime" => ".mov",
                _ => ".video"
            };
        }

        return Path.Combine(AppPaths.VideoCacheDirectory, hash + extension.ToLowerInvariant());
    }

    public static string? TryGetExistingPath(MessageViewModel message)
    {
        var path = GetPath(message);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 ||
                (message.FileSizeBytes > 0 && file.Length != message.FileSizeBytes))
            {
                return null;
            }

            file.LastAccessTimeUtc = DateTime.UtcNow;
            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Delete(MessageViewModel message)
    {
        var path = GetPath(message);
        var partialPath = path + ".part";
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(partialPath)) File.Delete(partialPath);
    }

    public static void Trim()
    {
        AppPaths.EnsureVideoCacheDirectoryCreated();
        try
        {
            var directory = new DirectoryInfo(AppPaths.VideoCacheDirectory);
            foreach (var partial in directory.EnumerateFiles("*.part").Where(x => x.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)))
            {
                try
                {
                    partial.Delete();
                }
                catch (IOException)
                {
                }
            }

            var files = directory.EnumerateFiles()
                .Where(x => !x.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastAccessTimeUtc)
                .ToList();
            var total = files.Sum(x => x.Length);
            foreach (var file in files.AsEnumerable().Reverse())
            {
                if (total <= MaxCacheBytes) break;
                try
                {
                    var length = file.Length;
                    file.Delete();
                    total -= length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
