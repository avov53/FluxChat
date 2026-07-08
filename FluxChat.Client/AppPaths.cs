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

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);

    public static void EnsureAvatarDirectoryCreated()
    {
        EnsureCreated();
        Directory.CreateDirectory(AvatarDirectory);
    }
}
