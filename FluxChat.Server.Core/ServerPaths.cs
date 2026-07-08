using System.Runtime.InteropServices;

namespace FluxChat.Server.Core;

public static class ServerPaths
{
    public static string DataDirectory { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : "/var/lib/fluxchat";

    public static string DatabasePath => Path.Combine(DataDirectory, "fluxchat.db");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
}
