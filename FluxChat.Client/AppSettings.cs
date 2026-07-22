using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace FluxChat.Client;

internal sealed class AppSettings
{
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Vps;
    public string RelayServer { get; set; } = $"127.0.0.1:{FluxChat.Shared.FluxChatPorts.Relay}";
    public string RelayAccessKey { get; set; } = "";
    public string RelayClientToken { get; set; } = "";
    public int AudioInputDeviceId { get; set; } = -1;
    public int AudioOutputDeviceId { get; set; } = -1;
    public bool NoiseSuppressionEnabled { get; set; } = true;
    public bool ReducedMotionEnabled { get; set; }
    public DataStorageLocation ChatHistoryStorage { get; set; } = DataStorageLocation.LocalComputer;
    public DataStorageLocation ImageStorage { get; set; } = DataStorageLocation.LocalComputer;
    public DataStorageLocation FileStorage { get; set; } = DataStorageLocation.GoogleDrive;
    public string GoogleDriveClientId { get; set; } = "";
    [JsonIgnore]
    public string GoogleDriveRefreshToken { get; set; } = "";
    [JsonIgnore]
    public string GoogleDriveAccessToken { get; set; } = "";
    public string GoogleDriveRefreshTokenProtected { get; set; } = "";
    public string GoogleDriveAccessTokenProtected { get; set; } = "";
    public DateTimeOffset GoogleDriveAccessTokenExpiresAtUtc { get; set; } = DateTimeOffset.MinValue;
    public string GoogleDriveAccountName { get; set; } = "";
    public string GoogleDriveBackupFileId { get; set; } = "";
    public string TenorApiKey { get; set; } = "";
    public string BadgeAuthorityUrl { get; set; } = "https://badges.91-186-217-186.sslip.io:8443";
}

internal enum NetworkMode
{
    Lan,
    Vps
}

internal enum DataStorageLocation
{
    LocalComputer,
    GoogleDrive
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
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                settings.GoogleDriveRefreshToken = Unprotect(settings.GoogleDriveRefreshTokenProtected);
                settings.GoogleDriveAccessToken = Unprotect(settings.GoogleDriveAccessTokenProtected);
                return settings;
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
        settings.GoogleDriveRefreshTokenProtected = Protect(settings.GoogleDriveRefreshToken);
        settings.GoogleDriveAccessTokenProtected = Protect(settings.GoogleDriveAccessToken);
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(settings, options));
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            AppLog.Write(ex, "Google Drive token could not be decrypted");
            return "";
        }
    }
}
