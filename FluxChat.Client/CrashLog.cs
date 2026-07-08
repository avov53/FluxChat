using System.IO;

namespace FluxChat.Client;

internal static class CrashLog
{
    public static string LogPath => Path.Combine(AppPaths.DataDirectory, "crash.log");

    public static void Write(Exception exception, string context)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(
                LogPath,
                $"""
                [{DateTimeOffset.Now:O}] {context}
                {exception}

                """);
        }
        catch
        {
            // Logging must never create a second startup failure.
        }
    }
}
