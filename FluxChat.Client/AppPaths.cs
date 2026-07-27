using System.IO;

namespace FluxChat.Client;

internal static class AppPaths
{
    private static string _activeUserId = "";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluxChat");

    public static string ProfilePath => Path.Combine(DataDirectory, "profile.json");

    public static string ProfilesDirectory => Path.Combine(DataDirectory, "profiles");

    public static string AccountsDirectory => Path.Combine(DataDirectory, "accounts");

    public static string AccountDataDirectory => string.IsNullOrWhiteSpace(_activeUserId)
        ? DataDirectory
        : Path.Combine(AccountsDirectory, SanitizePathSegment(_activeUserId));

    public static string HistoryPath => Path.Combine(AccountDataDirectory, "history.db");

    public static string AvatarDirectory => Path.Combine(AccountDataDirectory, "avatars");

    public static string AttachmentsDirectory => Path.Combine(AccountDataDirectory, "attachments");

    public static string MediaCacheDirectory => Path.Combine(AccountDataDirectory, "media-cache");

    public static string VideoCacheDirectory => Path.Combine(AccountDataDirectory, "video-cache");

    public static string GifFavoritesPath => Path.Combine(AccountDataDirectory, "gif-favorites.json");

    public static string AccountVaultPath => Path.Combine(DataDirectory, "account-vault.json");

    public static string SoundboardDirectory => Path.Combine(DataDirectory, "soundboard");

    public static string SoundboardCacheDirectory => Path.Combine(SoundboardDirectory, "cache");

    public static string SoundboardLibraryPath => Path.Combine(DataDirectory, "soundboard-library.json");

    public static string CallAudioPreferencesPath => Path.Combine(DataDirectory, "call-audio-preferences.json");

    public static string CustomizationDirectory => Path.Combine(DataDirectory, "customization");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);

    public static void UseAccountData(string userId)
    {
        _activeUserId = userId.Trim();
        EnsureAccountDataDirectoryCreated();
    }

    public static void EnsureAccountDataDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(AccountDataDirectory);
    }

    public static void EnsureProfilesDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(ProfilesDirectory);
    }

    public static void EnsureAvatarDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(AvatarDirectory);
    }

    public static void EnsureAttachmentsDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(AttachmentsDirectory);
    }

    public static void EnsureMediaCacheDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(MediaCacheDirectory);
    }

    public static void EnsureVideoCacheDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(VideoCacheDirectory);
    }

    public static void EnsureSoundboardDirectoriesCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(SoundboardDirectory);
        Directory.CreateDirectory(SoundboardCacheDirectory);
    }

    public static void EnsureCustomizationDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(CustomizationDirectory);
    }

    public static void ClearMessageCache()
    {
        try
        {
            if (Directory.Exists(AttachmentsDirectory)) Directory.Delete(AttachmentsDirectory, recursive: true);
            if (Directory.Exists(MediaCacheDirectory)) Directory.Delete(MediaCacheDirectory, recursive: true);
            if (Directory.Exists(VideoCacheDirectory)) Directory.Delete(VideoCacheDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "Temporary message cache cleanup failed");
        }
    }

    public static void DeleteLocalHistoryDatabase()
    {
        try
        {
            DeleteIfExists(HistoryPath);
            DeleteIfExists(HistoryPath + "-wal");
            DeleteIfExists(HistoryPath + "-shm");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "Legacy local history database cleanup failed");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var safe = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "default" : safe;
    }
}
