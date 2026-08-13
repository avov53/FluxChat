using System.IO;

namespace FluxChat.Client;

internal static class LocalCacheManager
{
    public static async Task<LocalCacheSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return new LocalCacheSnapshot(
            GetFileLength(AppPaths.HistoryPath),
            GetDirectoryLength(AppPaths.AttachmentsDirectory, cancellationToken),
            GetDirectoryLength(AppPaths.MediaCacheDirectory, cancellationToken),
            GetDirectoryLength(AppPaths.VideoCacheDirectory, cancellationToken));
    }

    public static async Task<LocalCacheCleanupResult> CleanAsync(
        AppSettings settings,
        HistoryStore history,
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (!force && !settings.AutoCleanLocalCache)
        {
            return new LocalCacheCleanupResult(0, 0, 0);
        }

        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(settings.LocalMessageCacheDays, 7, 90));
        var removedMessages = await history.DeleteCachedMessagesOlderThanAsync(cutoffUtc, cancellationToken);
        var removedFiles = 0;
        long removedBytes = 0;

        var attachments = TrimDirectory(AppPaths.AttachmentsDirectory, settings.LocalFileCacheMaxBytes, cancellationToken);
        removedFiles += attachments.Files;
        removedBytes += attachments.Bytes;

        var media = TrimDirectory(AppPaths.MediaCacheDirectory, settings.LocalMediaCacheMaxBytes, cancellationToken);
        removedFiles += media.Files;
        removedBytes += media.Bytes;

        var video = TrimDirectory(AppPaths.VideoCacheDirectory, settings.LocalMediaCacheMaxBytes, cancellationToken);
        removedFiles += video.Files;
        removedBytes += video.Bytes;

        if (removedMessages > 0 || removedFiles > 0)
        {
            await history.OptimizeAsync(cancellationToken);
        }

        return new LocalCacheCleanupResult(removedMessages, removedFiles, removedBytes);
    }

    private static (int Files, long Bytes) TrimDirectory(string directory, long maxBytes, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }

        var removedFiles = 0;
        long removedBytes = 0;
        var files = EnumerateCacheFiles(directory, cancellationToken)
            .OrderBy(file => SafeLastAccessTimeUtc(file))
            .ToList();

        foreach (var file in files.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Length > 0 &&
                !file.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryDelete(file, out var bytes))
            {
                removedFiles++;
                removedBytes += bytes;
                files.Remove(file);
            }
        }

        var total = files.Sum(file => file.Exists ? file.Length : 0);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total <= maxBytes)
            {
                break;
            }

            if (TryDelete(file, out var bytes))
            {
                removedFiles++;
                removedBytes += bytes;
                total -= bytes;
            }
        }

        return (removedFiles, removedBytes);
    }

    private static IEnumerable<FileInfo> EnumerateCacheFiles(string directory, CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file;
            try
            {
                file = new FileInfo(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool TryDelete(FileInfo file, out long bytes)
    {
        bytes = 0;
        try
        {
            if (!file.Exists)
            {
                return false;
            }

            bytes = file.Length;
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, $"Local cache file cleanup failed: {file.FullName}");
            return false;
        }
    }

    private static DateTime SafeLastAccessTimeUtc(FileInfo file)
    {
        try
        {
            return file.LastAccessTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long GetDirectoryLength(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return EnumerateCacheFiles(directory, cancellationToken)
            .Where(file => file.Exists)
            .Sum(file => file.Length);
    }
}

internal sealed record LocalCacheSnapshot(
    long HistoryBytes,
    long AttachmentsBytes,
    long MediaBytes,
    long VideoBytes)
{
    public long TotalBytes => HistoryBytes + AttachmentsBytes + MediaBytes + VideoBytes;
}

internal sealed record LocalCacheCleanupResult(int MessagesDeleted, int FilesDeleted, long BytesDeleted);
