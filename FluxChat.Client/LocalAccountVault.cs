using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FluxChat.Client;

internal sealed class LocalAccountVault
{
    public const int MaxAccountsPerDevice = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly List<LocalAccountVaultEntry> _entries;

    private LocalAccountVault(List<LocalAccountVaultEntry> entries)
    {
        _entries = entries;
    }

    public static LocalAccountVault Load()
    {
        try
        {
            if (File.Exists(AppPaths.AccountVaultPath))
            {
                var json = File.ReadAllText(AppPaths.AccountVaultPath);
                var entries = JsonSerializer.Deserialize<List<LocalAccountVaultEntry>>(json) ?? [];
                return new LocalAccountVault(entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Login)).ToList());
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write(ex, "Local account vault could not be loaded");
        }

        return new LocalAccountVault([]);
    }

    public IReadOnlyList<LocalAccountVaultEntry> Entries => _entries
        .OrderByDescending(entry => entry.LastUsedUtc)
        .Take(MaxAccountsPerDevice)
        .ToList();

    public IReadOnlyList<LocalAccountVaultEntry> GetEntriesForRelay(string relayAddress)
    {
        var normalizedRelay = NormalizeRelay(relayAddress);
        return Entries
            .Where(entry => string.Equals(NormalizeRelay(entry.RelayServer), normalizedRelay, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public LocalAccountVaultEntry? Find(string login, string relayAddress)
    {
        var normalizedLogin = NormalizeLogin(login);
        var normalizedRelay = NormalizeRelay(relayAddress);
        return _entries
            .OrderByDescending(entry => entry.LastUsedUtc)
            .FirstOrDefault(entry =>
                string.Equals(NormalizeLogin(entry.Login), normalizedLogin, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeRelay(entry.RelayServer), normalizedRelay, StringComparison.OrdinalIgnoreCase));
    }

    public LocalAccountVaultEntry? FindByUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return _entries
            .OrderByDescending(entry => entry.LastUsedUtc)
            .FirstOrDefault(entry => string.Equals(entry.UserId, userId, StringComparison.Ordinal));
    }

    public bool CanAddLogin(string login, string relayAddress)
    {
        var normalizedLogin = NormalizeLogin(login);
        var normalizedRelay = NormalizeRelay(relayAddress);
        if (string.IsNullOrWhiteSpace(normalizedLogin))
        {
            return true;
        }

        if (_entries.Any(entry =>
                string.Equals(NormalizeLogin(entry.Login), normalizedLogin, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeRelay(entry.RelayServer), normalizedRelay, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return _entries.Count < MaxAccountsPerDevice;
    }

    public string? TryGetPassword(string login, string relayAddress)
    {
        var normalizedLogin = NormalizeLogin(login);
        var normalizedRelay = NormalizeRelay(relayAddress);
        var entry = Find(normalizedLogin, normalizedRelay);

        if (entry is null || string.IsNullOrWhiteSpace(entry.ProtectedPassword))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(entry.ProtectedPassword), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            AppLog.Write(ex, "Local account password could not be decrypted");
            return null;
        }
    }

    public async Task RememberAsync(string login, string password, string relayAddress, UserProfile profile)
    {
        var normalizedLogin = NormalizeLogin(login);
        var normalizedRelay = NormalizeRelay(relayAddress);
        if (string.IsNullOrWhiteSpace(normalizedLogin) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var existing = Find(normalizedLogin, normalizedRelay);

        if (existing is null && _entries.Count >= MaxAccountsPerDevice)
        {
            throw new InvalidOperationException($"This device already has {MaxAccountsPerDevice} saved FluxChat accounts. Remove one before creating another account.");
        }

        var protectedPassword = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
        if (existing is null)
        {
            _entries.Add(new LocalAccountVaultEntry
            {
                Login = login.Trim(),
                RelayServer = normalizedRelay,
                ProtectedPassword = protectedPassword,
                UserId = profile.UserId,
                DisplayName = profile.DisplayName,
                LastUsedUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Login = login.Trim();
            existing.RelayServer = normalizedRelay;
            existing.ProtectedPassword = protectedPassword;
            existing.UserId = profile.UserId;
            existing.DisplayName = profile.DisplayName;
            existing.LastUsedUtc = DateTimeOffset.UtcNow;
        }

        await SaveAsync();
    }

    public async Task RemoveAsync(string login, string relayAddress)
    {
        var normalizedLogin = NormalizeLogin(login);
        var normalizedRelay = NormalizeRelay(relayAddress);
        var removed = _entries.RemoveAll(entry =>
            string.Equals(NormalizeLogin(entry.Login), normalizedLogin, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeRelay(entry.RelayServer), normalizedRelay, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            await SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        AppPaths.EnsureCreated();
        var ordered = _entries
            .OrderByDescending(entry => entry.LastUsedUtc)
            .Take(MaxAccountsPerDevice)
            .ToList();
        await File.WriteAllTextAsync(AppPaths.AccountVaultPath, JsonSerializer.Serialize(ordered, JsonOptions));
        _entries.Clear();
        _entries.AddRange(ordered);
    }

    private static string NormalizeLogin(string value)
        => value.Trim();

    private static string NormalizeRelay(string value)
        => value.Trim();
}

internal sealed class LocalAccountVaultEntry
{
    public string Login { get; set; } = "";
    public string RelayServer { get; set; } = "";
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
    public DateTimeOffset LastUsedUtc { get; set; }

    public override string ToString() => Login;
}
