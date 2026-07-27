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
    public string AccountApiUrl { get; set; } = "";
    public string AccountLogin { get; set; } = "";
    public string AccountSessionTokenProtected { get; set; } = "";
    [JsonIgnore]
    public string AccountSessionToken { get; set; } = "";
    public int AudioInputDeviceId { get; set; } = -1;
    public int AudioOutputDeviceId { get; set; } = -1;
    public bool NoiseSuppressionEnabled { get; set; } = true;
    public bool PushToTalkEnabled { get; set; }
    public string PushToTalkKey { get; set; } = "LeftCtrl";
    public bool KeepScreenShareMiniPlayerVisible { get; set; } = true;
    public string ScreenShareMiniPlayerCorner { get; set; } = "TopRight";
    public bool DetailedLoggingEnabled { get; set; }
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
    public Dictionary<string, SectionWallpaperSettings> SectionWallpapers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string IncomingCallSoundPath { get; set; } = "";
    public string IncomingMessageSoundPath { get; set; } = "";
    public bool NotificationSoundsEnabled { get; set; } = true;
}

internal sealed class SectionWallpaperSettings
{
    public string Path { get; set; } = "";
    public string Mode { get; set; } = "Fill";
    public bool IsVideo { get; set; }
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
                ClearRetiredGoogleDriveCredentials(settings);
                NormalizeCustomizationSettings(settings);
                settings.AccountSessionToken = Unprotect(settings.AccountSessionTokenProtected);
                AppLog.DetailedLoggingEnabled = settings.DetailedLoggingEnabled;
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write(ex, "Settings could not be loaded");
        }

        var fallback = new AppSettings();
        AppLog.DetailedLoggingEnabled = fallback.DetailedLoggingEnabled;
        return fallback;
    }

    public static async Task SaveAsync(AppSettings settings)
    {
        AppLog.DetailedLoggingEnabled = settings.DetailedLoggingEnabled;
        AppPaths.EnsureCreated();
        ClearRetiredGoogleDriveCredentials(settings);
        NormalizeCustomizationSettings(settings);
        settings.AccountSessionTokenProtected = Protect(settings.AccountSessionToken);
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
            AppLog.Write(ex, "Protected application setting could not be decrypted");
            return "";
        }
    }

    private static void ClearRetiredGoogleDriveCredentials(AppSettings settings)
    {
        settings.GoogleDriveClientId = "";
        settings.GoogleDriveRefreshToken = "";
        settings.GoogleDriveAccessToken = "";
        settings.GoogleDriveRefreshTokenProtected = "";
        settings.GoogleDriveAccessTokenProtected = "";
        settings.GoogleDriveAccessTokenExpiresAtUtc = DateTimeOffset.MinValue;
        settings.GoogleDriveAccountName = "";
        settings.GoogleDriveBackupFileId = "";
        settings.ChatHistoryStorage = DataStorageLocation.LocalComputer;
        settings.ImageStorage = DataStorageLocation.LocalComputer;
        settings.FileStorage = DataStorageLocation.LocalComputer;
    }

    private static void NormalizeCustomizationSettings(AppSettings settings)
    {
        settings.SectionWallpapers ??= new Dictionary<string, SectionWallpaperSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.SectionWallpapers.ToArray())
        {
            if (pair.Value is null || string.IsNullOrWhiteSpace(pair.Value.Path))
            {
                settings.SectionWallpapers.Remove(pair.Key);
                continue;
            }

            pair.Value.Mode = string.IsNullOrWhiteSpace(pair.Value.Mode) ? "Fill" : pair.Value.Mode;
        }
    }
}
