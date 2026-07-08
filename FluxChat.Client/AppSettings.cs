using System.IO;
using System.Text.Json;

namespace FluxChat.Client;

internal sealed class AppSettings
{
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Vps;
    public string RelayServer { get; set; } = $"127.0.0.1:{FluxChat.Shared.FluxChatPorts.Relay}";
    public string RelayAccessKey { get; set; } = "";
    public string RelayClientToken { get; set; } = "";
}

internal enum NetworkMode
{
    Lan,
    Vps
}

internal static class AppSettingsStore
{
    private static string SettingsPath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public static bool Exists()
        => File.Exists(SettingsPath);

    public static async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = await File.ReadAllTextAsync(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write(ex, "Settings could not be loaded");
        }

        return new AppSettings();
    }

    public static async Task SaveAsync(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(settings, options));
    }
}
