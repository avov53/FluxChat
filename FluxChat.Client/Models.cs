using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluxChat.Client;

public sealed class ContactViewModel : INotifyPropertyChanged
{
    private DateTimeOffset _lastSeenUtc;
    private UserPresenceStatus _status = UserPresenceStatus.Offline;
    private string _displayName = "";
    private string _ipAddress = "";
    private string _avatarKind = "color";
    private string _avatarPath = "";
    private string _groupMemberIds = "";
    private bool _isGroup;
    private int _messagePort;

    public required string UserId { get; init; }

    public required string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
            {
                return;
            }

            _displayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Initials));
        }
    }

    public required string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (_ipAddress == value)
            {
                return;
            }

            _ipAddress = value;
            OnPropertyChanged();
        }
    }

    public required int MessagePort
    {
        get => _messagePort;
        set
        {
            if (_messagePort == value)
            {
                return;
            }

            _messagePort = value;
            OnPropertyChanged();
        }
    }

    public string AvatarKind
    {
        get => _avatarKind;
        set
        {
            if (_avatarKind == value)
            {
                return;
            }

            _avatarKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAvatarImage));
            OnPropertyChanged(nameof(IsAvatarVideo));
            OnPropertyChanged(nameof(HasAvatar));
            OnPropertyChanged(nameof(AvatarImageSource));
        }
    }

    public string AvatarPath
    {
        get => _avatarPath;
        set
        {
            if (_avatarPath == value)
            {
                return;
            }

            _avatarPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAvatar));
            OnPropertyChanged(nameof(IsAvatarImage));
            OnPropertyChanged(nameof(IsAvatarVideo));
            OnPropertyChanged(nameof(AvatarImageSource));
        }
    }

    public bool IsGroup
    {
        get => _isGroup;
        set
        {
            if (_isGroup == value)
            {
                return;
            }

            _isGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirectContact));
            OnPropertyChanged(nameof(GroupMemberCountText));
        }
    }

    public bool IsDirectContact => !IsGroup;

    public string GroupMemberIds
    {
        get => _groupMemberIds;
        set
        {
            if (_groupMemberIds == value)
            {
                return;
            }

            _groupMemberIds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupMemberIdsList));
            OnPropertyChanged(nameof(GroupMemberCount));
            OnPropertyChanged(nameof(GroupMemberCountText));
        }
    }

    public IReadOnlyList<string> GroupMemberIdsList => string.IsNullOrWhiteSpace(GroupMemberIds)
        ? []
        : GroupMemberIds.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public int GroupMemberCount => GroupMemberIdsList.Count;

    public string GroupMemberCountText => IsGroup ? $"{GroupMemberCount} participants" : "";

    private double _avatarScale = 1;
    private double _avatarOffsetX;
    private double _avatarOffsetY;
    private double _avatarVideoStartSeconds;
    private double _avatarVideoDurationSeconds = 10;

    public double AvatarScale
    {
        get => _avatarScale;
        set
        {
            if (_avatarScale == value)
            {
                return;
            }

            _avatarScale = value;
            OnPropertyChanged();
        }
    }

    public double AvatarOffsetX
    {
        get => _avatarOffsetX;
        set
        {
            if (_avatarOffsetX == value)
            {
                return;
            }

            _avatarOffsetX = value;
            OnPropertyChanged();
        }
    }

    public double AvatarOffsetY
    {
        get => _avatarOffsetY;
        set
        {
            if (_avatarOffsetY == value)
            {
                return;
            }

            _avatarOffsetY = value;
            OnPropertyChanged();
        }
    }

    public double AvatarVideoStartSeconds
    {
        get => _avatarVideoStartSeconds;
        set
        {
            if (_avatarVideoStartSeconds == value)
            {
                return;
            }

            _avatarVideoStartSeconds = value;
            OnPropertyChanged();
        }
    }

    public double AvatarVideoDurationSeconds
    {
        get => _avatarVideoDurationSeconds;
        set
        {
            if (_avatarVideoDurationSeconds == value)
            {
                return;
            }

            _avatarVideoDurationSeconds = value;
            OnPropertyChanged();
        }
    }

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath);
    public bool IsAvatarImage => AvatarKind == "image" && HasAvatar;
    public bool IsAvatarVideo => AvatarKind == "video" && HasAvatar;
    public ImageSource? AvatarImageSource => IsAvatarImage ? AvatarImageLoader.Load(AvatarPath) : null;

    public DateTimeOffset LastSeenUtc
    {
        get => _lastSeenUtc;
        set
        {
            if (_lastSeenUtc == value)
            {
                return;
            }

            _lastSeenUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastSeenText));
        }
    }

    public UserPresenceStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public string ShortId => UserId.Length <= 12 ? UserId : UserId[..12];

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return "FC";
            }

            var initials = string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant();
            return initials.Length <= 2 ? initials : initials[..2];
        }
    }

    public string StatusText => Status switch
    {
        UserPresenceStatus.Online => "online",
        UserPresenceStatus.Idle => "idle",
        _ => "offline"
    };

    public string StatusColor => Status switch
    {
        UserPresenceStatus.Online => "#35d07f",
        UserPresenceStatus.Idle => "#f2b84b",
        _ => "#687080"
    };

    public string LastSeenText => LastSeenUtc == default ? "not seen yet" : LastSeenUtc.ToLocalTime().ToString("HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public enum UserPresenceStatus
{
    Online,
    Idle,
    Offline
}

public sealed class MessageViewModel
{
    public required Guid MessageId { get; init; }
    public required string PeerUserId { get; init; }
    public required string Body { get; init; }
    public required bool IsOutgoing { get; init; }
    public required DateTimeOffset SentAtUtc { get; init; }

    public string TimeText => SentAtUtc.ToLocalTime().ToString("HH:mm");
    public string Side => IsOutgoing ? "Right" : "Left";
}

public sealed class GroupCandidateViewModel : INotifyPropertyChanged
{
    private bool _isAdded;

    public required ContactViewModel Contact { get; init; }

    public string UserId => Contact.UserId;
    public string DisplayName => Contact.DisplayName;
    public string ShortId => Contact.ShortId;
    public string Initials => Contact.Initials;
    public string AvatarPath => Contact.AvatarPath;
    public bool HasAvatar => Contact.HasAvatar;
    public bool IsAvatarImage => Contact.IsAvatarImage;
    public bool IsAvatarVideo => Contact.IsAvatarVideo;
    public ImageSource? AvatarImageSource => Contact.AvatarImageSource;
    public bool CanAdd => !IsAdded;
    public string GroupActionText => IsAdded ? "Added" : "Add";
    public string ActionText => IsAdded ? "добавлен" : "добавить";

    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (_isAdded == value)
            {
                return;
            }

            _isAdded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(GroupActionText));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FriendRequestViewModel : INotifyPropertyChanged
{
    private string _displayName = "";
    private string _relayServer = "";
    private string _avatarKind = "color";
    private string _avatarPath = "";

    public required string UserId { get; init; }

    public required string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
            {
                return;
            }

            _displayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Initials));
        }
    }

    public required string RelayServer
    {
        get => _relayServer;
        set
        {
            if (_relayServer == value)
            {
                return;
            }

            _relayServer = value;
            OnPropertyChanged();
        }
    }

    public string AvatarKind
    {
        get => _avatarKind;
        set
        {
            if (_avatarKind == value)
            {
                return;
            }

            _avatarKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAvatarImage));
            OnPropertyChanged(nameof(IsAvatarVideo));
            OnPropertyChanged(nameof(HasAvatar));
            OnPropertyChanged(nameof(AvatarImageSource));
        }
    }

    public string AvatarPath
    {
        get => _avatarPath;
        set
        {
            if (_avatarPath == value)
            {
                return;
            }

            _avatarPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAvatar));
            OnPropertyChanged(nameof(IsAvatarImage));
            OnPropertyChanged(nameof(IsAvatarVideo));
            OnPropertyChanged(nameof(AvatarImageSource));
        }
    }

    public double AvatarScale { get; set; } = 1;
    public double AvatarOffsetX { get; set; }
    public double AvatarOffsetY { get; set; }
    public double AvatarVideoStartSeconds { get; set; }
    public double AvatarVideoDurationSeconds { get; set; } = 10;

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath);
    public bool IsAvatarImage => AvatarKind == "image" && HasAvatar;
    public bool IsAvatarVideo => AvatarKind == "video" && HasAvatar;
    public ImageSource? AvatarImageSource => IsAvatarImage ? AvatarImageLoader.Load(AvatarPath) : null;

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return "FC";
            }

            var initials = string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant();
            return initials.Length <= 2 ? initials : initials[..2];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal static class AvatarImageLoader
{
    public static ImageSource? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            AppLog.Write(ex, $"Avatar image load failed: path={path}");
            return null;
        }
    }
}
