using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

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
    double AvatarVideoDurationSeconds = 10);

internal static class UserProfileStore
{
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
                    return existing;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or FormatException)
            {
                CrashLog.Write(ex, "Existing profile could not be loaded");
            }

            MoveBrokenProfileAside();
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        var protectedKey = ProtectedData.Protect(privateKey, null, DataProtectionScope.CurrentUser);
        var userId = CreateFingerprint(publicKey);
        var profile = new UserProfile(userId, Environment.UserName, Convert.ToBase64String(protectedKey), publicKey);

        await SaveAsync(profile);
        return profile;
    }

    public static async Task SaveAsync(UserProfile profile)
    {
        AppPaths.EnsureCreated();
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(AppPaths.ProfilePath, JsonSerializer.Serialize(profile, options));
    }

    private static string CreateFingerprint(string publicKey)
    {
        var hash = SHA256.HashData(Convert.FromBase64String(publicKey));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
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
