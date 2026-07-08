using System.IO;

namespace FluxChat.Client;

internal static class AppLog
{
    public static string LogPath => Path.Combine(AppPaths.DataDirectory, "app.log");

    public static void Write(string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not affect messenger behavior.
        }
    }

    public static void Write(Exception exception, string message)
        => Write($"{message}: {exception.GetType().Name}: {exception.Message}");
}
