using FluxChat.Server.Core;
using System.Diagnostics;
using System.Globalization;

LoadAccountEnvironment();

var database = new RelayDatabase();
database.Initialize();

AccountStore? accountStore = null;
var postgresConnection = Environment.GetEnvironmentVariable("FLUXCHAT_POSTGRES_CONNECTION");
if (!string.IsNullOrWhiteSpace(postgresConnection))
{
    accountStore = new AccountStore(postgresConnection, ServerDataProtector.FromEnvironment());
    accountStore.InitializeAsync().GetAwaiter().GetResult();
}

var app = new FluxusApp(database, accountStore);
if (args.Length > 0)
{
    Environment.ExitCode = app.RunCommand(args);
}
else
{
    app.Run();
}

static void LoadAccountEnvironment()
{
    var candidates = OperatingSystem.IsWindows()
        ? Array.Empty<string>()
        : new[] { "/etc/fluxchat/account.env" };

    foreach (var path in candidates)
    {
        if (!File.Exists(path))
        {
            continue;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(key) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }

        return;
    }
}

internal sealed class FluxusApp
{
    private readonly RelayDatabase _database;
    private readonly AccountStore? _accounts;
    private int _selected;

    private readonly MenuItem[] _items;

    public FluxusApp(RelayDatabase database, AccountStore? accounts = null)
    {
        _database = database;
        _accounts = accounts;
        _items =
        [
            new("Создать инвайт-код", CreateInvite),
            new("Показать активные инвайты", ShowInvites),
            new("Удалить инвайт-код", DeleteInvite),
            new("Показать пользователей", ShowUsers),
            new("Заблокировать пользователя", () => SetBanned(true)),
            new("Разблокировать пользователя", () => SetBanned(false)),
            new("Сбросить токен пользователя", ResetToken),
            new("Очередь оффлайн-сообщений", ShowPending),
            new("Очистить очередь пользователя", ClearPending),
            new("Статус сервера", ShowStatus)
        ];
        _items =
        [
            .. _items,
            new("PostgreSQL database", ManagePostgres),
            new("Message retention", ManageRetention),
            new("Server limits", ManageServerLimits),
            new("File upload limit", ManageFileUploadLimit),
            new("Update panel", ManageUpdatePanel)
        ];
    }

    public void Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        while (true)
        {
            RenderMenu();
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _selected = (_selected - 1 + _items.Length) % _items.Length;
                    break;
                case ConsoleKey.DownArrow:
                    _selected = (_selected + 1) % _items.Length;
                    break;
                case ConsoleKey.Enter:
                    Console.Clear();
                    _items[_selected].Action();
                    Pause();
                    break;
                case ConsoleKey.R:
                    break;
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    Console.Clear();
                    return;
            }
        }
    }

    public int RunCommand(IReadOnlyList<string> arguments)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (arguments.Count is >= 2 and <= 3 &&
            string.Equals(arguments[0], "setup", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(arguments[1], "accounts", StringComparison.OrdinalIgnoreCase))
        {
            var action = arguments.Count == 3 ? arguments[2].ToLowerInvariant() : "setup";
            if (action is not ("setup" or "status" or "repair" or "disable"))
            {
                Console.Error.WriteLine("Usage: fluxus setup accounts [status|repair|disable]");
                return 2;
            }

            const string setupScript = "/opt/fluxchat/fluxchat-account-setup.sh";
            if (!File.Exists(setupScript))
            {
                Console.Error.WriteLine("Account setup script is missing. Run the latest fluxusui installer again.");
                return 3;
            }

            var startInfo = new ProcessStartInfo("/usr/bin/env")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("bash");
            startInfo.ArgumentList.Add(setupScript);
            startInfo.ArgumentList.Add(action);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Console.Error.WriteLine("Could not start the account setup script.");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }

        if (arguments.Count == 1 && string.Equals(arguments[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus();
            return 0;
        }

        if (arguments.Count == 2 && string.Equals(arguments[0], "migrate-sqlite", StringComparison.OrdinalIgnoreCase))
        {
            if (_accounts is null)
            {
                Console.Error.WriteLine("PostgreSQL account storage is not configured. Set FLUXCHAT_POSTGRES_CONNECTION and FLUXCHAT_DATA_KEY first.");
                return 3;
            }

            try
            {
                var result = LegacySqliteMigration.ImportAsync(_accounts, arguments[1]).GetAwaiter().GetResult();
                Console.WriteLine($"Imported {result.UsersImported} legacy users and {result.PendingPacketsImported} pending packets. The SQLite source was not changed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Migration failed: {exception.Message}");
                return 1;
            }
        }

        Console.WriteLine("Usage: fluxus status | fluxus migrate-sqlite <path-to-fluxchat.db> | fluxus setup accounts [status|repair|disable]");
        return 2;
    }

    private void RenderMenu()
    {
        Console.Clear();
        Console.WriteLine("Fluxus admin panel");
        Console.WriteLine($"Database: {ServerPaths.DatabasePath}");
        Console.WriteLine("Use ↑/↓, Enter, R refresh, Q/Esc exit");
        Console.WriteLine();

        for (var i = 0; i < _items.Length; i++)
        {
            Console.Write(i == _selected ? ">" : " ");
            Console.WriteLine($" {i + 1}. {_items[i].Title}");
        }
    }

    private void CreateInvite()
    {
        Console.WriteLine("Комментарий для инвайта (можно оставить пустым):");
        var note = Console.ReadLine() ?? "";
        var code = _database.CreateInvite(note.Trim());
        Console.WriteLine();
        Console.WriteLine("Создан инвайт:");
        Console.WriteLine(code);
        Console.WriteLine();
        Console.WriteLine("Отправь этот код другу. Он вводит его в клиенте в поле Invite / token.");
    }

    private void ShowInvites()
    {
        var invites = _database.GetInvites(includeUsed: false);
        if (invites.Count == 0)
        {
            Console.WriteLine("Активных инвайтов нет.");
            return;
        }

        foreach (var invite in invites)
        {
            Console.WriteLine($"{invite.Code} | {invite.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC | {invite.Note}");
        }
    }

    private void DeleteInvite()
    {
        Console.WriteLine("Введи инвайт-код для удаления:");
        var code = (Console.ReadLine() ?? "").Trim();
        Console.WriteLine(_database.DeleteInvite(code)
            ? "Инвайт удалён."
            : "Инвайт не найден или уже использован.");
    }

    private void ShowUsers()
    {
        var users = _database.GetUsers();
        if (users.Count == 0)
        {
            Console.WriteLine("Пользователей пока нет.");
            return;
        }

        foreach (var user in users)
        {
            var state = user.IsBanned ? "BANNED" : "OK";
            Console.WriteLine($"{state} | {user.DisplayName} | {user.UserId} | last seen {user.LastSeenUtc:yyyy-MM-dd HH:mm} UTC");
        }
    }

    private void SetBanned(bool banned)
    {
        Console.WriteLine(banned ? "Введи UserId для блокировки:" : "Введи UserId для разблокировки:");
        var userId = (Console.ReadLine() ?? "").Trim();
        Console.WriteLine(_database.SetBanned(userId, banned)
            ? banned ? "Пользователь заблокирован." : "Пользователь разблокирован."
            : "Пользователь не найден.");
    }

    private void ResetToken()
    {
        Console.WriteLine("Введи UserId для сброса токена:");
        var userId = (Console.ReadLine() ?? "").Trim();
        var token = _database.ResetToken(userId);
        if (token is null)
        {
            Console.WriteLine("Пользователь не найден.");
            return;
        }

        Console.WriteLine("Новый token пользователя:");
        Console.WriteLine(token);
        Console.WriteLine("Передай его пользователю вместо инвайта.");
    }

    private void ShowPending()
    {
        var rows = _database.GetPendingSummary();
        if (rows.Count == 0)
        {
            Console.WriteLine("Очередь оффлайн-сообщений пустая.");
            return;
        }

        foreach (var row in rows)
        {
            Console.WriteLine($"{row.UserId}: {row.Count}");
        }
    }

    private void ClearPending()
    {
        Console.WriteLine("Введи UserId, чью очередь очистить:");
        var userId = (Console.ReadLine() ?? "").Trim();
        var count = _database.ClearPending(userId);
        Console.WriteLine($"Удалено сообщений: {count}");
    }

    private void ShowStatus()
    {
        var stats = _database.GetStats(onlineCount: 0);
        var relay = FindRelayProcess();
        Console.WriteLine($"Port: 42800");
        Console.WriteLine($"Database: {stats.DatabasePath}");
        Console.WriteLine($"Database size: {FormatBytes(stats.DatabaseSizeBytes)}");
        Console.WriteLine($"Users: {stats.Users}");
        Console.WriteLine($"Active invites: {stats.ActiveInvites}");
        Console.WriteLine($"Pending messages: {stats.PendingMessages}");
        if (relay is null)
        {
            Console.WriteLine("Relay process: not found");
            return;
        }

        Console.WriteLine($"Relay process: PID {relay.Id}");
        Console.WriteLine($"Memory RSS: {FormatBytes(relay.WorkingSet64)}");
        try
        {
            Console.WriteLine($"Uptime: {FormatDuration(DateTimeOffset.Now - relay.StartTime)}");
        }
        catch
        {
            Console.WriteLine("Uptime: unavailable");
        }
        Console.WriteLine("Online users and call-route loss are printed once per minute in: journalctl -u fluxchat -f");
    }

    private void ManagePostgres()
    {
        if (_accounts is null)
        {
            Console.WriteLine("PostgreSQL account storage is not configured. Set FLUXCHAT_POSTGRES_CONNECTION and FLUXCHAT_DATA_KEY in /etc/fluxchat/account.env.");
            return;
        }

        var stats = _accounts.GetStatsAsync().GetAwaiter().GetResult();
        Console.WriteLine("PostgreSQL account database");
        Console.WriteLine($"Accounts: {stats.Accounts}");
        Console.WriteLine($"Active sessions: {stats.ActiveSessions}");
        Console.WriteLine($"Archived messages: {stats.Messages}");
        Console.WriteLine($"Media objects: {stats.MediaObjects}");
        Console.WriteLine($"Database size: {FormatBytes(stats.DatabaseSizeBytes)}");
        Console.WriteLine();
        Console.WriteLine("Backup: pg_dump --format=custom --file /root/fluxchat-backup.dump fluxchat");
        Console.WriteLine("Restore: pg_restore --clean --if-exists --dbname fluxchat /root/fluxchat-backup.dump");
    }

    private void ManageRetention()
    {
        if (_accounts is null)
        {
            Console.WriteLine("PostgreSQL account storage is not configured.");
            return;
        }

        Console.WriteLine("Retention in days (for example 730 for 2 years):");
        if (!int.TryParse(Console.ReadLine(), out var days) || days < 1)
        {
            Console.WriteLine("Enter a positive whole number.");
            return;
        }

        Console.WriteLine($"Delete archived messages and media older than {days} days? Type DELETE to confirm:");
        if (!string.Equals(Console.ReadLine(), "DELETE", StringComparison.Ordinal))
        {
            Console.WriteLine("Canceled.");
            return;
        }

        var result = _accounts.DeleteExpiredAsync(days).GetAwaiter().GetResult();
        Console.WriteLine($"Deleted {result.MessagesDeleted} messages and {result.MediaDeleted} media records older than {result.CutoffUtc:yyyy-MM-dd HH:mm} UTC.");
    }

    private void ManageServerLimits()
    {
        if (_accounts is null)
        {
            Console.WriteLine("PostgreSQL account storage is not configured.");
            return;
        }

        try
        {
            var currentLimit = _accounts.GetMaxServersPerVpsAsync().GetAwaiter().GetResult();
            var currentCount = _accounts.CountCreatedServersAsync().GetAwaiter().GetResult();

            Console.WriteLine("FluxChat server limits");
            Console.WriteLine();
            Console.WriteLine($"Created servers on this VPS: {currentCount}");
            Console.WriteLine($"Current server limit: {currentLimit}");
            Console.WriteLine("Allowed range: 1-1000");
            Console.WriteLine();
            Console.Write("New limit (blank to keep current): ");
            var input = (Console.ReadLine() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Server limit was not changed.");
                return;
            }

            if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
                value is < 1 or > 1000)
            {
                Console.WriteLine("Invalid limit. Use a number from 1 to 1000.");
                return;
            }

            _accounts.SetMaxServersPerVpsAsync(value).GetAwaiter().GetResult();
            Console.WriteLine($"Server limit updated to {value}.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Server limit update failed: {exception.Message}");
        }
    }

    private void ManageFileUploadLimit()
    {
        if (_accounts is null)
        {
            Console.WriteLine("PostgreSQL account storage is not configured.");
            return;
        }

        try
        {
            var currentLimit = _accounts.GetMaxFileUploadBytesAsync().GetAwaiter().GetResult();

            Console.WriteLine("FluxChat file upload limit");
            Console.WriteLine();
            Console.WriteLine($"Current upload limit: {FormatBytes(currentLimit)}");
            Console.WriteLine("Allowed range: 1 MB - 1024 MB");
            Console.WriteLine("Examples: 10 mb, 25MB, 100m");
            Console.WriteLine();
            Console.Write("New limit (blank to keep current): ");
            var input = (Console.ReadLine() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("File upload limit was not changed.");
                return;
            }

            if (!TryParseFileUploadLimit(input, out var bytes) ||
                bytes is < 1L * 1024 * 1024 or > 1024L * 1024 * 1024)
            {
                Console.WriteLine("Invalid limit. Use a value from 1 MB to 1024 MB.");
                return;
            }

            _accounts.SetMaxFileUploadBytesAsync(bytes).GetAwaiter().GetResult();
            Console.WriteLine($"File upload limit updated to {FormatBytes(bytes)}.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"File upload limit update failed: {exception.Message}");
        }
    }

    private void ManageUpdatePanel()
    {
        Console.WriteLine("FluxChat update panel");
        Console.WriteLine();
        Console.WriteLine("1. Update with current saved domain and Let's Encrypt HTTPS settings");
        Console.WriteLine("   Downloads the latest fluxusui from GitHub and reuses /etc/fluxchat/account.env.");
        Console.WriteLine("2. Interactive update/setup");
        Console.WriteLine("   Runs the installer as usual and lets you enter domain, Let's Encrypt email and setup answers.");
        Console.WriteLine();
        Console.Write("Choice [1]: ");

        var choice = (Console.ReadLine() ?? string.Empty).Trim();
        switch (string.IsNullOrWhiteSpace(choice) ? "1" : choice)
        {
            case "1":
                RunInstaller(
                    "curl -fsSL https://raw.githubusercontent.com/avov53/fluxusui/main/install.sh | FLUXCHAT_UPDATE_CURRENT_ACCOUNT=1 bash");
                break;
            case "2":
                RunInstaller(
                    "bash <(curl -Ls https://raw.githubusercontent.com/avov53/fluxusui/main/install.sh)");
                break;
            default:
                Console.WriteLine("Choose 1 or 2.");
                break;
        }
    }

    private static void RunInstaller(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("This update panel is available on the VPS Linux server.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Starting update...");
        Console.WriteLine();

        var startInfo = new ProcessStartInfo("/usr/bin/env")
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("bash");
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.WriteLine("Could not start the update command.");
            return;
        }

        process.WaitForExit();
        Console.WriteLine();
        Console.WriteLine(process.ExitCode == 0
            ? "Update finished."
            : $"Update failed with exit code {process.ExitCode}.");
    }

    private static bool TryParseFileUploadLimit(string input, out long bytes)
    {
        bytes = 0;
        var normalized = input.Trim().ToLowerInvariant().Replace(" ", "");
        if (normalized.Length == 0)
        {
            return false;
        }

        var multiplier = 1024L * 1024;
        if (normalized.EndsWith("gb", StringComparison.Ordinal) ||
            normalized.EndsWith("g", StringComparison.Ordinal))
        {
            normalized = normalized.EndsWith("gb", StringComparison.Ordinal)
                ? normalized[..^2]
                : normalized[..^1];
            multiplier = 1024L * 1024 * 1024;
        }
        else if (normalized.EndsWith("mb", StringComparison.Ordinal) ||
                 normalized.EndsWith("m", StringComparison.Ordinal))
        {
            normalized = normalized.EndsWith("mb", StringComparison.Ordinal)
                ? normalized[..^2]
                : normalized[..^1];
            multiplier = 1024L * 1024;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
        {
            return false;
        }

        bytes = (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
        return bytes > 0;
    }

    private static System.Diagnostics.Process? FindRelayProcess()
    {
        foreach (var name in new[] { "FluxChat.Server", "fluxchat-server", "FluxChatServer" })
        {
            var process = System.Diagnostics.Process.GetProcessesByName(name)
                .OrderByDescending(candidate => candidate.StartTime)
                .FirstOrDefault();
            if (process is not null)
            {
                return process;
            }
        }

        return null;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.0} {suffixes[index]}";
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}d {duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("Нажми любую клавишу...");
        Console.ReadKey(intercept: true);
    }
}

internal sealed record MenuItem(string Title, Action Action);
