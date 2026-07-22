using System.IO;

namespace FluxChat.Client;

internal static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluxChat");

    public static string ProfilePath => Path.Combine(DataDirectory, "profile.json");

    public static string HistoryPath => Path.Combine(DataDirectory, "history.db");

    public static string AvatarDirectory => Path.Combine(DataDirectory, "avatars");

    public static string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");

    public static string VideoCacheDirectory => Path.Combine(DataDirectory, "video-cache");

    public static string GifFavoritesPath => Path.Combine(DataDirectory, "gif-favorites.json");

    public static string SoundboardDirectory => Path.Combine(DataDirectory, "soundboard");

    public static string SoundboardCacheDirectory => Path.Combine(SoundboardDirectory, "cache");

    public static string SoundboardLibraryPath => Path.Combine(DataDirectory, "soundboard-library.json");

    public static string CallAudioPreferencesPath => Path.Combine(DataDirectory, "call-audio-preferences.json");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);

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
}
