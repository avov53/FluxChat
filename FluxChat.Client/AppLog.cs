using System.IO;

namespace FluxChat.Client;

internal static class AppLog
{
    public static bool DetailedLoggingEnabled { get; set; }

    public static string LogPath => Path.Combine(AppPaths.DataDirectory, "app.log");

    public static void Write(string message)
        => WriteInternal(message, force: false);

    public static void Write(Exception exception, string message)
        => WriteInternal($"{message}: {exception.GetType().Name}: {exception.Message}", force: true);

    private static void WriteInternal(string message, bool force)
    {
        if (!force && !DetailedLoggingEnabled)
        {
            return;
        }

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
}
