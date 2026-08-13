using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Threading;

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
    public string UiLanguage { get; set; } = AppLanguage.SystemLanguageCode;
    public bool DetailedLoggingEnabled { get; set; }
    public bool ReducedMotionEnabled { get; set; }
    public DataStorageLocation ChatHistoryStorage { get; set; } = DataStorageLocation.LocalComputer;
    public DataStorageLocation ImageStorage { get; set; } = DataStorageLocation.LocalComputer;
    public DataStorageLocation FileStorage { get; set; } = DataStorageLocation.LocalComputer;
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
    public bool DesktopNotificationsEnabled { get; set; } = true;
    public bool TaskbarFlashEnabled { get; set; } = true;
    public bool FriendRequestNotificationsEnabled { get; set; }
    public bool AutoDndEnabled { get; set; }
    public string AutoDndPreset { get; set; } = "Night";
    public string AutoDndStart { get; set; } = "23:00";
    public string AutoDndEnd { get; set; } = "09:00";
    public string DefaultNotificationMode { get; set; } = "All";
    public Dictionary<string, string> ChatNotificationRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> PinnedContactIds { get; set; } = [];
    public Dictionary<string, long> ContactLastActivityTicks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActivityMode { get; set; } = "Auto";
    public string CustomActivityText { get; set; } = "";
    public bool ActivityEnabled { get; set; } = true;
    public string ActivityVisibility { get; set; } = "Friends";
    public List<string> ActivitySelectedFriendIds { get; set; } = [];
    public string CallActivityVisibility { get; set; } = "Participants";
    public bool ActivityShowRoblox { get; set; } = true;
    public bool ActivityShowCalls { get; set; } = true;
    public bool ActivityShowScreenShare { get; set; } = true;
    public bool ActivityShowMusic { get; set; }
    public string PrivacyMessages { get; set; } = "Friends";
    public string PrivacyCalls { get; set; } = "Friends";
    public string PrivacyFriendRequests { get; set; } = "Everyone";
    public string PrivacyStatus { get; set; } = "Everyone";
    public string PrivacyAvatar { get; set; } = "Everyone";
    public int LocalMessageCacheDays { get; set; } = 30;
    public long LocalMediaCacheMaxBytes { get; set; } = 1024L * 1024 * 1024;
    public long LocalFileCacheMaxBytes { get; set; } = 1024L * 1024 * 1024;
    public bool AutoCleanLocalCache { get; set; } = true;
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
                NormalizeModernSettings(settings);
                settings.UiLanguage = AppLanguage.Normalize(settings.UiLanguage);
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
        NormalizeModernSettings(settings);
        settings.UiLanguage = AppLanguage.Normalize(settings.UiLanguage);
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

    private static void NormalizeModernSettings(AppSettings settings)
    {
        settings.AutoDndPreset = NormalizeChoice(settings.AutoDndPreset, "Night", "Night", "Study", "Work", "Game", "Custom");
        settings.AutoDndStart = NormalizeTime(settings.AutoDndStart, "23:00");
        settings.AutoDndEnd = NormalizeTime(settings.AutoDndEnd, "09:00");
        settings.DefaultNotificationMode = NormalizeChoice(settings.DefaultNotificationMode, "All", "All", "Mentions", "Muted");
        settings.ChatNotificationRules ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in settings.ChatNotificationRules.Keys.ToArray())
        {
            settings.ChatNotificationRules[key] = NormalizeChoice(settings.ChatNotificationRules[key], settings.DefaultNotificationMode, "All", "Mentions", "Muted");
        }

        settings.PinnedContactIds ??= [];
        settings.ContactLastActivityTicks ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        settings.ActivityMode = NormalizeChoice(settings.ActivityMode, "Auto", "Auto", "Custom", "Off");
        settings.CustomActivityText ??= "";
        if (string.Equals(settings.ActivityMode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            settings.ActivityEnabled = false;
        }
        settings.ActivityVisibility = NormalizeChoice(settings.ActivityVisibility, "Friends", "Everyone", "Friends", "Selected");
        settings.CallActivityVisibility = NormalizeChoice(settings.CallActivityVisibility, "Participants", "Everyone", "Friends", "Selected", "NoOne", "Participants");
        settings.ActivitySelectedFriendIds ??= [];
        settings.ActivitySelectedFriendIds = settings.ActivitySelectedFriendIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(500)
            .ToList();
        settings.PrivacyMessages = NormalizeChoice(settings.PrivacyMessages, "Friends", "Everyone", "Friends", "NoOne");
        settings.PrivacyCalls = NormalizeChoice(settings.PrivacyCalls, "Friends", "Everyone", "Friends", "NoOne");
        settings.PrivacyFriendRequests = NormalizeChoice(settings.PrivacyFriendRequests, "Everyone", "Everyone", "Friends", "NoOne");
        settings.PrivacyStatus = NormalizeChoice(settings.PrivacyStatus, "Everyone", "Everyone", "Friends", "NoOne");
        settings.PrivacyAvatar = NormalizeChoice(settings.PrivacyAvatar, "Everyone", "Everyone", "Friends", "NoOne");
        settings.LocalMessageCacheDays = settings.LocalMessageCacheDays switch
        {
            7 or 30 or 90 => settings.LocalMessageCacheDays,
            <= 0 => 30,
            _ => Math.Clamp(settings.LocalMessageCacheDays, 7, 90)
        };
        settings.LocalMediaCacheMaxBytes = NormalizeCacheLimitBytes(settings.LocalMediaCacheMaxBytes);
        settings.LocalFileCacheMaxBytes = NormalizeCacheLimitBytes(settings.LocalFileCacheMaxBytes);
    }

    private static long NormalizeCacheLimitBytes(long value)
    {
        var allowed = new[] { 300L * 1024 * 1024, 1024L * 1024 * 1024, 5L * 1024 * 1024 * 1024 };
        return allowed.Contains(value) ? value : 1024L * 1024 * 1024;
    }

    private static string NormalizeChoice(string? value, string fallback, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return allowed.FirstOrDefault(x => string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static string NormalizeTime(string? value, string fallback)
        => TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out var time)
            ? time.ToString("hh\\:mm", CultureInfo.InvariantCulture)
            : fallback;
}

internal static class AppLanguage
{
    public const string SystemLanguageCode = "system";
    public static string CurrentLanguageCode { get; private set; } = SystemLanguageCode;

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["settings.title"] = "Settings",
        ["settings.search"] = "Search settings",
        ["settings.account"] = "Account",
        ["settings.voice"] = "Voice",
        ["settings.language"] = "Language",
        ["settings.signout"] = "Sign out",
        ["settings.customization"] = "Customization",
        ["settings.privacy"] = "Privacy",
        ["settings.activity"] = "Activity",
        ["settings.notifications"] = "Notifications",
        ["settings.data"] = "Data",
        ["settings.privacy.subtitle"] = "Control who can contact you and what profile data they can see.",
        ["settings.activity.subtitle"] = "Game activity and scheduled Do Not Disturb.",
        ["settings.notifications.subtitle"] = "Choose how FluxChat alerts you.",
        ["settings.close"] = "Close",
        ["settings.account.subtitle"] = "Your profile and VPS relay connection.",
        ["settings.voice.subtitle"] = "Microphone, headphones and local voice check.",
        ["settings.language.subtitle"] = "Choose the interface language for this device.",
        ["settings.signout.subtitle"] = "Leave this account on this device.",
        ["settings.customization.subtitle"] = "Local wallpapers, message sounds and call ringtones.",
        ["settings.language.card.title"] = "Interface language",
        ["settings.language.card.body"] = "FluxChat will translate supported interface text immediately. Unsupported languages fall back to English.",
        ["settings.language.system"] = "FluxChat will follow the Windows language where supported.",
        ["settings.language.saved"] = "Language saved for this device.",
        ["settings.language.saveFailed"] = "Language could not be saved.",
        ["settings.profile"] = "Profile",
        ["settings.nickname"] = "Nickname",
        ["settings.image"] = "Image",
        ["settings.video"] = "Video",
        ["settings.saveProfile"] = "Save profile",
        ["settings.animatedAvatars"] = "Animated avatars loop up to 10 seconds.",
        ["settings.screenShareMiniPlayer"] = "Screen share mini player",
        ["settings.screenShareMiniPlayer.body"] = "Keep the screen share mini-player visible outside FluxChat.",
        ["settings.diagnosticLogging"] = "Diagnostic logging",
        ["settings.diagnosticLogging.body"] = "Save detailed app logs while debugging. Crash logs are always saved.",
        ["settings.vpsServer"] = "VPS server",
        ["settings.link"] = "Link",
        ["settings.vpsHelp"] = "VPS help",
        ["settings.avatar.zoom"] = "Zoom",
        ["settings.avatar.horizontal"] = "Horizontal",
        ["settings.avatar.vertical"] = "Vertical",
        ["settings.avatar.start"] = "Start",
        ["settings.avatar.length"] = "Length",
        ["settings.security"] = "Security",
        ["settings.security.body"] = "Active devices and account password.",
        ["settings.security.loadingDevices"] = "Loading devices...",
        ["settings.security.signInToViewDevices"] = "Sign in to view active devices.",
        ["settings.security.loadDevicesFailed"] = "Could not load devices: {0}",
        ["settings.security.signOutDeviceTitle"] = "Sign out device",
        ["settings.security.signOutThisDeviceConfirm"] = "Sign out this device now?",
        ["settings.security.signOutDeviceConfirm"] = "Sign out {0}?",
        ["settings.security.signOutAction"] = "Sign out",
        ["settings.security.cancelAction"] = "Cancel",
        ["settings.security.signOutDeviceFailed"] = "Could not sign out device: {0}",
        ["settings.security.noActiveSession"] = "No active account session.",
        ["settings.security.signOutEverywhereTitle"] = "Sign out everywhere",
        ["settings.security.signOutEverywhereConfirm"] = "Sign out this account on all devices?",
        ["settings.security.signOutAllAction"] = "Sign out all",
        ["settings.security.signOutDevicesFailed"] = "Could not sign out devices: {0}",
        ["settings.refresh"] = "Refresh",
        ["settings.changePassword"] = "Change password",
        ["settings.changePassword.signInFirst"] = "Sign in before changing password.",
        ["settings.changePassword.enterPasswords"] = "Enter current and new password.",
        ["settings.changePassword.tooShort"] = "New password must be at least 10 characters.",
        ["settings.changePassword.mismatch"] = "New passwords do not match.",
        ["settings.changePassword.updating"] = "Updating password...",
        ["settings.changePassword.failed"] = "Could not change password: {0}",
        ["settings.currentPassword"] = "Current password",
        ["settings.newPassword"] = "New password",
        ["settings.repeatNewPassword"] = "Repeat new password",
        ["settings.signOutAllDevices"] = "Sign out on all devices",
        ["settings.signOutDevice"] = "Sign out this device",
        ["settings.device.defaultName"] = "FluxChat device",
        ["settings.device.unknownLocation"] = "Unknown location",
        ["settings.device.current"] = "Current device",
        ["settings.device.active"] = "Active",
        ["settings.device.signedOut"] = "Signed out",
        ["settings.device.activeNow"] = "Active now",
        ["settings.device.lastActiveTime"] = "Last active {0}",
        ["settings.device.none"] = "No device sessions were returned by the server.",
        ["settings.device.count"] = "{0} device session(s).",
        ["settings.voice.section"] = "Voice",
        ["settings.voice.input"] = "Input device",
        ["settings.voice.output"] = "Output device",
        ["settings.voice.check"] = "Check voice",
        ["settings.voice.testHint"] = "Speak to hear yourself.",
        ["settings.voice.noiseSuppression"] = "Noise suppression",
        ["settings.voice.noiseSuppression.body"] = "Reduces keyboard and steady background noise.",
        ["privacy.contactPermissions"] = "Contact permissions",
        ["privacy.messages"] = "Who can send you messages",
        ["privacy.calls"] = "Who can call you",
        ["privacy.friendRequests"] = "Who can add you as friend",
        ["privacy.profileVisibility"] = "Profile visibility",
        ["privacy.status"] = "Who can see your status",
        ["privacy.avatar"] = "Who can see your avatar",
        ["privacy.everyone"] = "Everyone",
        ["privacy.friendsOnly"] = "Friends only",
        ["privacy.noOne"] = "No one",
        ["privacy.preview.friendButton"] = "Preview as friend",
        ["privacy.preview.strangerButton"] = "Preview as stranger",
        ["privacy.preview.friend"] = "Friend preview",
        ["privacy.preview.stranger"] = "Stranger preview",
        ["privacy.preview.access"] = "Messages: {0}  Calls: {1}  Friend requests: {2}",
        ["privacy.allowed"] = "allowed",
        ["privacy.blocked"] = "blocked",
        ["privacy.hidden"] = "Hidden",
        ["activity.profile"] = "Activity profile",
        ["activity.profile.body"] = "Automatically show the game or FluxChat activity you are using.",
        ["activity.games"] = "Games",
        ["activity.calls"] = "Calls",
        ["activity.screenShare.label"] = "Screen share",
        ["activity.visibility"] = "Who can see your activity",
        ["activity.visibility.friends"] = "Friends",
        ["activity.visibility.everyone"] = "Everyone",
        ["activity.visibility.selected"] = "Selected friends",
        ["activity.visibility.noOne"] = "No one",
        ["activity.visibility.participants"] = "Call participants",
        ["activity.callVisibility"] = "Who can see call and screen-share activity",
        ["activity.searchFriends"] = "Search friends",
        ["activity.preview"] = "Current published activity: {0}",
        ["activity.none"] = "none",
        ["activity.inCall"] = "In a call",
        ["activity.screenShare"] = "Watching a screen share",
        ["activity.playing"] = "Playing {0} {1}",
        ["activity.dnd.title"] = "Auto-DND rules",
        ["activity.dnd.body"] = "Suppress sounds and popups while the schedule is active.",
        ["activity.dnd.active"] = "Auto-DND is active now. Sounds and popup notifications are muted.",
        ["activity.dnd.inactive"] = "Auto-DND is not active right now.",
        ["notifications.desktop.title"] = "Desktop notifications",
        ["notifications.desktop"] = "Desktop notifications",
        ["notifications.taskbarFlash"] = "Flash FluxChat on the taskbar for new messages",
        ["notifications.friendRequests"] = "Notify when someone sends a friend request",
        ["notifications.friendRequest.received"] = "Sent you a friend request",
        ["notifications.sounds.title"] = "Notification sounds",
        ["notifications.sounds.body"] = "Custom local sounds for incoming calls and messages.",
        ["notifications.callSound"] = "Incoming call ringtone",
        ["notifications.callSound.hint"] = "If the file is longer, FluxChat will let you choose a 20-second part.",
        ["notifications.messageSound"] = "Incoming message sound",
        ["settings.data.subtitle"] = "Choose how FluxChat stores local cache.",
        ["settings.data.storage"] = "Storage",
        ["settings.data.chatHistory"] = "Chat history",
        ["settings.data.chatHistory.body"] = "Stored locally as a temporary cache. VPS remains the main source.",
        ["settings.data.images"] = "Images",
        ["settings.data.images.body"] = "Cached on this device and cleaned automatically.",
        ["settings.data.files"] = "Videos and files",
        ["settings.data.files.body"] = "Cached locally with the file cache limit below.",
        ["settings.data.localCache"] = "Local cache",
        ["settings.data.localCache.title"] = "Local cache",
        ["settings.data.autoClean"] = "Auto clean",
        ["settings.data.messageCacheAge"] = "Message cache age",
        ["settings.data.mediaCacheLimit"] = "Media cache limit",
        ["settings.data.fileCacheLimit"] = "File cache limit",
        ["settings.data.days"] = "{0} days",
        ["settings.data.clearCache"] = "Clear cache",
        ["settings.data.reducedMotion"] = "Reduced motion",
        ["settings.data.reducedMotion.body"] = "Disables hover and press animations throughout FluxChat.",
        ["profile.status.online"] = "Online",
        ["profile.status.idle"] = "Idle",
        ["profile.status.dnd"] = "Do not disturb",
        ["profile.status.offline"] = "Offline",
        ["profile.customStatus"] = "Custom status",
        ["profile.set"] = "Set",
        ["profile.copyUserId"] = "Copy User ID",
        ["miniProfile.commonGroups"] = "Common groups: {0}",
        ["miniProfile.writeMessage"] = "Write a message",
        ["miniProfile.self"] = "This is your profile",
        ["server.roles.add"] = "Add role",
        ["sidebar.directMessages"] = "DIRECT MESSAGES",
        ["sidebar.emptyContacts"] = "Add another FluxChat user by UserId.",
        ["addFriend.title"] = "Add Friend",
        ["addFriend.subtitle"] = "Enter a User ID or UserId@host:port.",
        ["addFriend.requests"] = "Incoming friend requests",
        ["common.add"] = "Add",
        ["common.send"] = "Send",
        ["common.choose"] = "Choose",
        ["common.reset"] = "Reset",
        ["common.play"] = "Play",
        ["common.systemSound"] = "System sound",
        ["common.defaultBackground"] = "Default background",
        ["createServer.button"] = "Create server",
        ["account.private"] = "Private conversations, one account.",
        ["account.welcome.title"] = "Welcome to FluxChat",
        ["account.welcome.subtitle"] = "Sign in to see your conversations, or create your account.",
        ["account.welcome.signIn"] = "Sign in",
        ["account.welcome.create"] = "Create account",
        ["account.welcome.privacy"] = "Your contacts and status stay hidden until you sign in.",
        ["account.signin.title"] = "Sign in",
        ["account.signin.subtitle"] = "Continue to your FluxChat account.",
        ["account.create.title"] = "Create account",
        ["account.create.subtitle"] = "One account for all your FluxChat devices.",
        ["account.login"] = "Login",
        ["account.password"] = "Password",
        ["account.passwordLong"] = "Password (10 or more characters)",
        ["account.repeatPassword"] = "Repeat password",
        ["account.showPassword"] = "Show password",
        ["account.hidePassword"] = "Hide password",
        ["account.vpsServer"] = "VPS server",
        ["account.inviteCode"] = "Invite code",
        ["account.back"] = "Back",
        ["account.wait"] = "Please wait..."
    };

    private static readonly IReadOnlyDictionary<string, string> Russian = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["settings.title"] = "Настройки",
        ["settings.search"] = "Поиск по настройкам",
        ["settings.account"] = "Аккаунт",
        ["settings.voice"] = "Голос",
        ["settings.language"] = "Язык",
        ["settings.signout"] = "Выйти",
        ["settings.customization"] = "Кастомизация",
        ["settings.privacy"] = "Приватность",
        ["settings.activity"] = "Активность",
        ["settings.notifications"] = "Уведомления",
        ["settings.privacy.subtitle"] = "Настройте, кто может связаться с вами и видеть данные профиля.",
        ["settings.activity.subtitle"] = "Активность в играх и расписание режима «Не беспокоить».",
        ["settings.notifications.subtitle"] = "Выберите, как FluxChat будет уведомлять вас.",
        ["settings.close"] = "Закрыть",
        ["settings.account.subtitle"] = "Профиль и подключение к VPS.",
        ["settings.voice.subtitle"] = "Микрофон, наушники и локальная проверка голоса.",
        ["settings.language.subtitle"] = "Выберите язык интерфейса для этого устройства.",
        ["settings.signout.subtitle"] = "Выйти из аккаунта на этом устройстве.",
        ["settings.customization.subtitle"] = "Локальные фоны, звуки сообщений и мелодии звонков.",
        ["settings.language.card.title"] = "Язык интерфейса",
        ["settings.language.card.body"] = "FluxChat сразу переводит поддерживаемый текст интерфейса. Неподдерживаемые языки используют английский.",
        ["settings.language.system"] = "FluxChat будет использовать язык Windows там, где он поддерживается.",
        ["settings.language.saved"] = "Язык сохранён для этого устройства.",
        ["settings.language.saveFailed"] = "Не удалось сохранить язык.",
        ["settings.profile"] = "Профиль",
        ["settings.nickname"] = "Никнейм",
        ["settings.image"] = "Фото",
        ["settings.video"] = "Видео",
        ["settings.saveProfile"] = "Сохранить профиль",
        ["settings.animatedAvatars"] = "Анимированные аватарки повторяются до 10 секунд.",
        ["settings.screenShareMiniPlayer"] = "Мини-плеер демонстрации экрана",
        ["settings.screenShareMiniPlayer.body"] = "Показывать мини-плеер демонстрации экрана вне FluxChat.",
        ["settings.diagnosticLogging"] = "Диагностические логи",
        ["settings.diagnosticLogging.body"] = "Сохранять подробные логи приложения для отладки. Краш-логи сохраняются всегда.",
        ["settings.vpsServer"] = "VPS сервер",
        ["settings.link"] = "Подключить",
        ["settings.vpsHelp"] = "Помощь по VPS",
        ["settings.avatar.zoom"] = "Масштаб",
        ["settings.avatar.horizontal"] = "Горизонтально",
        ["settings.avatar.vertical"] = "Вертикально",
        ["settings.avatar.start"] = "Начало",
        ["settings.avatar.length"] = "Длина",
        ["settings.security"] = "Безопасность",
        ["settings.security.body"] = "Активные устройства и пароль аккаунта.",
        ["settings.security.loadingDevices"] = "Загрузка устройств...",
        ["settings.security.signInToViewDevices"] = "Войдите в аккаунт, чтобы посмотреть активные устройства.",
        ["settings.security.loadDevicesFailed"] = "Не удалось загрузить устройства: {0}",
        ["settings.security.signOutDeviceTitle"] = "Выход с устройства",
        ["settings.security.signOutThisDeviceConfirm"] = "Выйти с этого устройства сейчас?",
        ["settings.security.signOutDeviceConfirm"] = "Выйти с устройства {0}?",
        ["settings.security.signOutAction"] = "Выйти",
        ["settings.security.cancelAction"] = "Отмена",
        ["settings.security.signOutDeviceFailed"] = "Не удалось выйти с устройства: {0}",
        ["settings.security.noActiveSession"] = "Нет активной сессии аккаунта.",
        ["settings.security.signOutEverywhereTitle"] = "Выйти везде",
        ["settings.security.signOutEverywhereConfirm"] = "Выйти из этого аккаунта на всех устройствах?",
        ["settings.security.signOutAllAction"] = "Выйти на всех",
        ["settings.security.signOutDevicesFailed"] = "Не удалось выйти на устройствах: {0}",
        ["settings.refresh"] = "Обновить",
        ["settings.changePassword"] = "Изменить пароль",
        ["settings.changePassword.signInFirst"] = "Войдите в аккаунт перед сменой пароля.",
        ["settings.changePassword.enterPasswords"] = "Введите текущий и новый пароль.",
        ["settings.changePassword.tooShort"] = "Новый пароль должен быть не короче 10 символов.",
        ["settings.changePassword.mismatch"] = "Новые пароли не совпадают.",
        ["settings.changePassword.updating"] = "Обновление пароля...",
        ["settings.changePassword.failed"] = "Не удалось изменить пароль: {0}",
        ["settings.currentPassword"] = "Текущий пароль",
        ["settings.newPassword"] = "Новый пароль",
        ["settings.repeatNewPassword"] = "Повторите новый пароль",
        ["settings.signOutAllDevices"] = "Выйти на всех устройствах",
        ["settings.signOutDevice"] = "Выйти с этого устройства",
        ["settings.device.defaultName"] = "Устройство FluxChat",
        ["settings.device.unknownLocation"] = "Местоположение неизвестно",
        ["settings.device.current"] = "Текущее устройство",
        ["settings.device.active"] = "Активно",
        ["settings.device.signedOut"] = "Выполнен выход",
        ["settings.device.activeNow"] = "Активно сейчас",
        ["settings.device.lastActiveTime"] = "Последняя активность {0}",
        ["settings.device.none"] = "Сервер не вернул активные устройства.",
        ["settings.device.count"] = "Сессий устройств: {0}.",
        ["settings.voice.section"] = "Голос",
        ["settings.voice.input"] = "Устройство ввода",
        ["settings.voice.output"] = "Устройство вывода",
        ["settings.voice.check"] = "Проверить голос",
        ["settings.voice.testHint"] = "Говорите, чтобы услышать себя.",
        ["settings.voice.noiseSuppression"] = "Шумоподавление",
        ["settings.voice.noiseSuppression.body"] = "Уменьшает шум клавиатуры и постоянный фоновый шум.",
        ["privacy.contactPermissions"] = "Разрешения контактов",
        ["privacy.messages"] = "Кто может писать вам",
        ["privacy.calls"] = "Кто может звонить вам",
        ["privacy.friendRequests"] = "Кто может добавлять вас в друзья",
        ["privacy.profileVisibility"] = "Видимость профиля",
        ["privacy.status"] = "Кто может видеть ваш статус",
        ["privacy.avatar"] = "Кто может видеть вашу аватарку",
        ["privacy.everyone"] = "Все",
        ["privacy.friendsOnly"] = "Только друзья",
        ["privacy.noOne"] = "Никто",
        ["privacy.preview.friendButton"] = "Как видит друг",
        ["privacy.preview.strangerButton"] = "Как видит незнакомец",
        ["privacy.preview.friend"] = "Просмотр для друга",
        ["privacy.preview.stranger"] = "Просмотр для незнакомца",
        ["privacy.preview.access"] = "Сообщения: {0}  Звонки: {1}  Заявки в друзья: {2}",
        ["privacy.allowed"] = "разрешено",
        ["privacy.blocked"] = "запрещено",
        ["privacy.hidden"] = "Скрыто",
        ["activity.profile"] = "Профиль активности",
        ["activity.profile.body"] = "Автоматически показывает игру или активность FluxChat.",
        ["activity.games"] = "Игры",
        ["activity.calls"] = "Звонки",
        ["activity.screenShare.label"] = "Демонстрация экрана",
        ["activity.visibility"] = "Кто видит вашу активность",
        ["activity.visibility.friends"] = "Друзья",
        ["activity.visibility.everyone"] = "Все",
        ["activity.visibility.selected"] = "Выбранные друзья",
        ["activity.visibility.noOne"] = "Никто",
        ["activity.visibility.participants"] = "Участники звонка",
        ["activity.callVisibility"] = "Кто видит активности: в звонке, демонстрирует экран",
        ["activity.searchFriends"] = "Поиск друзей",
        ["activity.preview"] = "Текущая активность: {0}",
        ["activity.none"] = "нет",
        ["activity.inCall"] = "В звонке",
        ["activity.screenShare"] = "Смотрит демонстрацию",
        ["activity.playing"] = "Играет в {0} {1}",
        ["activity.dnd.title"] = "Правила авто-DND",
        ["activity.dnd.body"] = "Отключает звуки и всплывающие окна по расписанию.",
        ["activity.dnd.active"] = "Авто-DND сейчас активен. Звуки и всплывающие уведомления отключены.",
        ["activity.dnd.inactive"] = "Авто-DND сейчас не активен.",
        ["notifications.desktop.title"] = "Уведомления на рабочем столе",
        ["notifications.desktop"] = "Уведомления на рабочем столе",
        ["notifications.taskbarFlash"] = "Мигать значком FluxChat на панели задач при новых сообщениях",
        ["notifications.friendRequests"] = "Уведомлять, когда отправляют заявку в друзья",
        ["notifications.friendRequest.received"] = "Отправил вам заявку в друзья",
        ["profile.status.online"] = "Онлайн",
        ["profile.status.idle"] = "Отошёл",
        ["profile.status.dnd"] = "Не беспокоить",
        ["profile.status.offline"] = "Оффлайн",
        ["profile.customStatus"] = "Свой статус",
        ["profile.set"] = "Задать",
        ["profile.copyUserId"] = "Скопировать User ID",
        ["miniProfile.commonGroups"] = "Общих групп: {0}",
        ["miniProfile.writeMessage"] = "Написать сообщение",
        ["miniProfile.self"] = "Это ваш профиль",
        ["server.roles.add"] = "Добавить роль",
        ["sidebar.directMessages"] = "ЛИЧНЫЕ СООБЩЕНИЯ",
        ["sidebar.emptyContacts"] = "Добавьте другого пользователя FluxChat по UserId.",
        ["addFriend.title"] = "Добавить друга",
        ["addFriend.subtitle"] = "Введите User ID или UserId@host:port.",
        ["addFriend.requests"] = "Входящие запросы в друзья",
        ["common.add"] = "Добавить",
        ["common.send"] = "Отправить",
        ["common.choose"] = "Выбрать",
        ["common.reset"] = "Сбросить",
        ["common.play"] = "Прослушать",
        ["common.systemSound"] = "Системный звук",
        ["common.defaultBackground"] = "Стандартный фон",
        ["createServer.button"] = "Создать сервер",
        ["account.private"] = "Приватные переписки, один аккаунт.",
        ["account.welcome.title"] = "Добро пожаловать в FluxChat",
        ["account.welcome.subtitle"] = "Войдите, чтобы увидеть переписки, или создайте аккаунт.",
        ["account.welcome.signIn"] = "Войти",
        ["account.welcome.create"] = "Создать аккаунт",
        ["account.welcome.privacy"] = "Контакты и статус скрыты, пока вы не войдёте.",
        ["account.signin.title"] = "Вход",
        ["account.signin.subtitle"] = "Продолжите в свой аккаунт FluxChat.",
        ["account.create.title"] = "Создать аккаунт",
        ["account.create.subtitle"] = "Один аккаунт для всех устройств FluxChat.",
        ["account.login"] = "Логин",
        ["account.password"] = "Пароль",
        ["account.passwordLong"] = "Пароль (10 или больше символов)",
        ["account.repeatPassword"] = "Повторите пароль",
        ["account.vpsServer"] = "VPS сервер",
        ["account.inviteCode"] = "Код приглашения",
        ["account.back"] = "Назад",
        ["account.wait"] = "Подождите..."
    };

    private static readonly IReadOnlyDictionary<string, string> RussianOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["settings.data"] = "\u0414\u0430\u043d\u043d\u044b\u0435",
        ["settings.data.subtitle"] = "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u0442\u0435, \u043a\u0430\u043a FluxChat \u0445\u0440\u0430\u043d\u0438\u0442 \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0439 \u043a\u044d\u0448.",
        ["settings.data.storage"] = "\u0425\u0440\u0430\u043d\u0438\u043b\u0438\u0449\u0435",
        ["settings.data.chatHistory"] = "\u0418\u0441\u0442\u043e\u0440\u0438\u044f \u0447\u0430\u0442\u043e\u0432",
        ["settings.data.chatHistory.body"] = "\u0425\u0440\u0430\u043d\u0438\u0442\u0441\u044f \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u043e \u043a\u0430\u043a \u0432\u0440\u0435\u043c\u0435\u043d\u043d\u044b\u0439 \u043a\u044d\u0448. VPS \u043e\u0441\u0442\u0430\u0451\u0442\u0441\u044f \u0433\u043b\u0430\u0432\u043d\u044b\u043c \u0438\u0441\u0442\u043e\u0447\u043d\u0438\u043a\u043e\u043c.",
        ["settings.data.images"] = "\u0418\u0437\u043e\u0431\u0440\u0430\u0436\u0435\u043d\u0438\u044f",
        ["settings.data.images.body"] = "\u041a\u044d\u0448\u0438\u0440\u0443\u044e\u0442\u0441\u044f \u043d\u0430 \u044d\u0442\u043e\u043c \u0443\u0441\u0442\u0440\u043e\u0439\u0441\u0442\u0432\u0435 \u0438 \u043e\u0447\u0438\u0449\u0430\u044e\u0442\u0441\u044f \u0430\u0432\u0442\u043e\u043c\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438.",
        ["settings.data.files"] = "\u0412\u0438\u0434\u0435\u043e \u0438 \u0444\u0430\u0439\u043b\u044b",
        ["settings.data.files.body"] = "\u041a\u044d\u0448\u0438\u0440\u0443\u044e\u0442\u0441\u044f \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u043e \u0441 \u043b\u0438\u043c\u0438\u0442\u043e\u043c \u0444\u0430\u0439\u043b\u043e\u0432\u043e\u0433\u043e \u043a\u044d\u0448\u0430 \u043d\u0438\u0436\u0435.",
        ["settings.data.localCache"] = "\u041b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0439 \u043a\u044d\u0448",
        ["settings.data.localCache.title"] = "\u041b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0439 \u043a\u044d\u0448",
        ["settings.data.autoClean"] = "\u0410\u0432\u0442\u043e\u043e\u0447\u0438\u0441\u0442\u043a\u0430",
        ["settings.data.messageCacheAge"] = "\u0421\u0440\u043e\u043a \u043a\u044d\u0448\u0430 \u0441\u043e\u043e\u0431\u0449\u0435\u043d\u0438\u0439",
        ["settings.data.mediaCacheLimit"] = "\u041b\u0438\u043c\u0438\u0442 \u043a\u044d\u0448\u0430 \u043c\u0435\u0434\u0438\u0430",
        ["settings.data.fileCacheLimit"] = "\u041b\u0438\u043c\u0438\u0442 \u043a\u044d\u0448\u0430 \u0444\u0430\u0439\u043b\u043e\u0432",
        ["settings.data.days"] = "{0} \u0434\u043d.",
        ["settings.data.clearCache"] = "\u041e\u0447\u0438\u0441\u0442\u0438\u0442\u044c \u043a\u044d\u0448",
        ["settings.data.reducedMotion"] = "\u041c\u0435\u043d\u044c\u0448\u0435 \u0430\u043d\u0438\u043c\u0430\u0446\u0438\u0439",
        ["settings.data.reducedMotion.body"] = "\u041e\u0442\u043a\u043b\u044e\u0447\u0430\u0435\u0442 \u0430\u043d\u0438\u043c\u0430\u0446\u0438\u0438 \u043d\u0430\u0432\u0435\u0434\u0435\u043d\u0438\u044f \u0438 \u043d\u0430\u0436\u0430\u0442\u0438\u044f \u0432 FluxChat.",
        ["notifications.sounds.title"] = "\u0417\u0432\u0443\u043a\u0438 \u0443\u0432\u0435\u0434\u043e\u043c\u043b\u0435\u043d\u0438\u0439",
        ["notifications.sounds.body"] = "\u041b\u043e\u043a\u0430\u043b\u044c\u043d\u044b\u0435 \u0437\u0432\u0443\u043a\u0438 \u0434\u043b\u044f \u0432\u0445\u043e\u0434\u044f\u0449\u0438\u0445 \u0437\u0432\u043e\u043d\u043a\u043e\u0432 \u0438 \u0441\u043e\u043e\u0431\u0449\u0435\u043d\u0438\u0439.",
        ["notifications.callSound"] = "\u041c\u0435\u043b\u043e\u0434\u0438\u044f \u0432\u0445\u043e\u0434\u044f\u0449\u0435\u0433\u043e \u0437\u0432\u043e\u043d\u043a\u0430",
        ["notifications.callSound.hint"] = "\u0415\u0441\u043b\u0438 \u0444\u0430\u0439\u043b \u0434\u043b\u0438\u043d\u043d\u0435\u0435, FluxChat \u043f\u0440\u0435\u0434\u043b\u043e\u0436\u0438\u0442 \u0432\u044b\u0431\u0440\u0430\u0442\u044c 20-\u0441\u0435\u043a\u0443\u043d\u0434\u043d\u044b\u0439 \u0444\u0440\u0430\u0433\u043c\u0435\u043d\u0442.",
        ["notifications.messageSound"] = "\u0417\u0432\u0443\u043a \u0432\u0445\u043e\u0434\u044f\u0449\u0435\u0433\u043e \u0441\u043e\u043e\u0431\u0449\u0435\u043d\u0438\u044f",
        ["account.showPassword"] = "\u041f\u043e\u043a\u0430\u0437\u0430\u0442\u044c \u043f\u0430\u0440\u043e\u043b\u044c",
        ["account.hidePassword"] = "\u0421\u043a\u0440\u044b\u0442\u044c \u043f\u0430\u0440\u043e\u043b\u044c"
    };

    public static readonly IReadOnlyList<(string Code, string DisplayName)> Supported =
    [
        (SystemLanguageCode, "System default"),
        ("ru", "Русский"),
        ("en", "English"),
        ("uk", "Українська"),
        ("de", "Deutsch"),
        ("es", "Español"),
        ("fr", "Français"),
        ("pt-BR", "Português"),
        ("tr", "Türkçe"),
        ("pl", "Polski"),
        ("zh-CN", "中文"),
        ("ja", "日本語"),
        ("ko", "한국어")
    ];

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return SystemLanguageCode;
        }

        return Supported.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))
            ? Supported.First(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)).Code
            : SystemLanguageCode;
    }

    public static void Apply(string? code)
    {
        code = Normalize(code);
        CurrentLanguageCode = code;
        if (string.Equals(code, SystemLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            CultureInfo.DefaultThreadCurrentCulture = null;
            CultureInfo.DefaultThreadCurrentUICulture = null;
            return;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            AppLog.Write(ex, $"Unsupported UI language: {code}");
            CultureInfo.DefaultThreadCurrentCulture = null;
            CultureInfo.DefaultThreadCurrentUICulture = null;
        }
    }

    public static string Text(string key)
    {
        if (ShouldUseRussian() && RussianOverrides.TryGetValue(key, out var overrideValue))
        {
            return overrideValue;
        }

        var dictionary = ShouldUseRussian() ? Russian : English;
        return dictionary.TryGetValue(key, out var value)
            ? value
            : English.TryGetValue(key, out var fallback)
                ? fallback
                : key;
    }

    private static bool ShouldUseRussian()
    {
        var code = CurrentLanguageCode;
        if (string.Equals(code, SystemLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }

        return code.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
    }
}
