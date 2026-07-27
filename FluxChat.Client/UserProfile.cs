using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FluxChat.Shared;

namespace FluxChat.Client;

internal sealed record UserProfile(
    string UserId,
    string DisplayName,
    string ProtectedPrivateKey,
    string PublicKey,
    string AvatarColor = "#5865f2",
    string AvatarKind = "color",
    string AvatarPath = "",
    double AvatarScale = 1,
    double AvatarOffsetX = 0,
    double AvatarOffsetY = 0,
    double AvatarVideoStartSeconds = 0,
    double AvatarVideoDurationSeconds = 10,
    UserPresenceStatus SelectedStatus = UserPresenceStatus.Online,
    string CustomStatus = "");

internal static class UserProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<UserProfile> LoadOrCreateAsync()
    {
        AppPaths.EnsureCreated();

        if (File.Exists(AppPaths.ProfilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(AppPaths.ProfilePath);
                var existing = JsonSerializer.Deserialize<UserProfile>(json);
                if (existing is not null && IsUsable(existing))
                {
                    await SaveProfileCopyAsync(existing);
                    return existing;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or FormatException)
            {
                CrashLog.Write(ex, "Existing profile could not be loaded");
            }

            MoveBrokenProfileAside();
        }

        var profile = CreateNew(Environment.UserName);
        await SaveAsync(profile);
        return profile;
    }

    public static async Task SaveAsync(UserProfile profile)
    {
        AppPaths.EnsureCreated();
        await File.WriteAllTextAsync(AppPaths.ProfilePath, JsonSerializer.Serialize(profile, JsonOptions));
        await SaveProfileCopyAsync(profile);
    }

    public static async Task<UserProfile> CreateNewAsync(string? displayName = null)
    {
        var profile = CreateNew(displayName);
        await SaveProfileCopyAsync(profile);
        return profile;
    }

    public static async Task<UserProfile?> TryLoadProfileAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        try
        {
            var path = GetProfileCopyPath(userId);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path);
            var profile = JsonSerializer.Deserialize<UserProfile>(json);
            return profile is not null && IsUsable(profile) ? profile : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or FormatException)
        {
            AppLog.Write(ex, "Saved local account profile could not be loaded");
            return null;
        }
    }

    public static Task ActivateAsync(UserProfile profile)
        => SaveAsync(profile);

    private static UserProfile CreateNew(string? displayName)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        var protectedKey = ProtectedData.Protect(privateKey, null, DataProtectionScope.CurrentUser);
        var userId = IdentityCrypto.CreateUserId(publicKey);
        var name = string.IsNullOrWhiteSpace(displayName) ? Environment.UserName : displayName.Trim();
        return new UserProfile(userId, name, Convert.ToBase64String(protectedKey), publicKey);
    }

    private static async Task SaveProfileCopyAsync(UserProfile profile)
    {
        AppPaths.EnsureProfilesDirectoryCreated();
        await File.WriteAllTextAsync(GetProfileCopyPath(profile.UserId), JsonSerializer.Serialize(profile, JsonOptions));
    }

    private static string GetProfileCopyPath(string userId)
    {
        var safeUserId = new string(userId.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            safeUserId = Guid.NewGuid().ToString("N");
        }

        return Path.Combine(AppPaths.ProfilesDirectory, safeUserId + ".json");
    }

    private static bool IsUsable(UserProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.UserId) ||
            string.IsNullOrWhiteSpace(profile.DisplayName) ||
            string.IsNullOrWhiteSpace(profile.ProtectedPrivateKey) ||
            string.IsNullOrWhiteSpace(profile.PublicKey))
        {
            return false;
        }

        ProtectedData.Unprotect(Convert.FromBase64String(profile.ProtectedPrivateKey), null, DataProtectionScope.CurrentUser);
        Convert.FromBase64String(profile.PublicKey);
        return true;
    }

    private static void MoveBrokenProfileAside()
    {
        try
        {
            var brokenPath = Path.Combine(
                AppPaths.DataDirectory,
                $"profile.broken-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
            File.Move(AppPaths.ProfilePath, brokenPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write(ex, "Broken profile could not be moved aside");
        }
    }
}
