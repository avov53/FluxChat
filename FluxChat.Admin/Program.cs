using FluxChat.Server.Core;

var database = new RelayDatabase();
database.Initialize();

var app = new FluxusApp(database);
app.Run();

internal sealed class FluxusApp
{
    private readonly RelayDatabase _database;
    private int _selected;

    private readonly MenuItem[] _items;

    public FluxusApp(RelayDatabase database)
    {
        _database = database;
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
        Console.WriteLine($"Port: 42800");
        Console.WriteLine($"Database: {stats.DatabasePath}");
        Console.WriteLine($"Users: {stats.Users}");
        Console.WriteLine($"Active invites: {stats.ActiveInvites}");
        Console.WriteLine($"Pending messages: {stats.PendingMessages}");
        Console.WriteLine("Online users: shown in relay server console");
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("Нажми любую клавишу...");
        Console.ReadKey(intercept: true);
    }
}

internal sealed record MenuItem(string Title, Action Action);
