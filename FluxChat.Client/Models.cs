using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluxChat.Shared;

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
    private string _groupOwnerUserId = "";
    private string _groupMembersJson = "";
    private string _currentUserId = "";
    private long _groupVersion;
    private bool _groupIsDeleted;
    private bool _isGroup;
    private int _messagePort;
    private string _verifiedBadgeId = "";
    private string _badgeCertificateJson = "";
    private string _identityPublicKey = "";
    private DateTimeOffset? _badgeVerifiedAtUtc;

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
            OnPropertyChanged(nameof(CanRenameContact));
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
            OnPropertyChanged(nameof(CanRenameContact));
            OnPropertyChanged(nameof(CanRemoveFromFriends));
            OnPropertyChanged(nameof(CanEditGroup));
            OnPropertyChanged(nameof(CanLeaveGroup));
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

    public string GroupOwnerUserId
    {
        get => _groupOwnerUserId;
        set
        {
            if (_groupOwnerUserId == value)
            {
                return;
            }

            _groupOwnerUserId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurrentUserGroupOwner));
            OnPropertyChanged(nameof(CanEditGroup));
            OnPropertyChanged(nameof(CanLeaveGroup));
        }
    }

    public long GroupVersion
    {
        get => _groupVersion;
        set
        {
            if (_groupVersion == value)
            {
                return;
            }

            _groupVersion = value;
            OnPropertyChanged();
        }
    }

    public bool GroupIsDeleted
    {
        get => _groupIsDeleted;
        set
        {
            if (_groupIsDeleted == value)
            {
                return;
            }

            _groupIsDeleted = value;
            OnPropertyChanged();
        }
    }

    public string GroupMembersJson
    {
        get => _groupMembersJson;
        set
        {
            if (_groupMembersJson == value)
            {
                return;
            }

            _groupMembersJson = value;
            OnPropertyChanged();
        }
    }

    public string CurrentUserId
    {
        get => _currentUserId;
        set
        {
            if (_currentUserId == value)
            {
                return;
            }

            _currentUserId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCurrentUserGroupOwner));
            OnPropertyChanged(nameof(CanEditGroup));
            OnPropertyChanged(nameof(CanLeaveGroup));
        }
    }

    public bool IsCurrentUserGroupOwner => IsGroup &&
                                           !string.IsNullOrWhiteSpace(CurrentUserId) &&
                                           string.Equals(GroupOwnerUserId, CurrentUserId, StringComparison.Ordinal);

    public bool CanRenameContact => !IsGroup;

    public bool CanRemoveFromFriends => !IsGroup;

    public bool CanEditGroup => IsGroup && IsCurrentUserGroupOwner;

    public bool CanLeaveGroup => IsGroup && !IsCurrentUserGroupOwner;

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

    public string VerifiedBadgeId
    {
        get => _verifiedBadgeId;
        set
        {
            if (_verifiedBadgeId == value) return;
            _verifiedBadgeId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVerifiedBadge));
            OnPropertyChanged(nameof(BadgeImageSource));
            OnPropertyChanged(nameof(BadgeToolTip));
        }
    }

    public string BadgeCertificateJson { get => _badgeCertificateJson; set { if (_badgeCertificateJson == value) return; _badgeCertificateJson = value; OnPropertyChanged(); } }
    public string IdentityPublicKey { get => _identityPublicKey; set { if (_identityPublicKey == value) return; _identityPublicKey = value; OnPropertyChanged(); } }
    public DateTimeOffset? BadgeVerifiedAtUtc { get => _badgeVerifiedAtUtc; set { if (_badgeVerifiedAtUtc == value) return; _badgeVerifiedAtUtc = value; OnPropertyChanged(); } }
    public bool HasVerifiedBadge => BadgeIds.IsKnown(VerifiedBadgeId);
    public ImageSource? BadgeImageSource => BadgeVisuals.GetImage(VerifiedBadgeId);
    public string BadgeToolTip => VerifiedBadgeId == BadgeIds.Owner ? "Owner" :
                                  VerifiedBadgeId == BadgeIds.Tester ? "Tester" :
                                  VerifiedBadgeId == BadgeIds.Special ? "Special" :
                                  "";

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

public sealed class MessageViewModel : INotifyPropertyChanged
{
    private string _body = "";
    private string _text = "";
    private string _reactionsJson = "";
    private string _currentUserId = "";
    private string _senderDisplayName = "";
    private string _senderAvatarKind = "color";
    private string _senderAvatarPath = "";
    private DateTimeOffset? _editedAtUtc;
    private double _gifRenderWidth = 300;
    private double _gifRenderHeight = 170;
    private string _driveFileId = "";
    private string _downloadUrl = "";
    private string _storageProvider = "";
    private string _videoLocalPath = "";
    private VideoDownloadState _videoDownloadState;
    private double _videoDownloadProgress;
    private string _videoError = "";
    private double _videoDurationSeconds;
    private double _videoPositionSeconds;
    private double _videoVolume = 0.8;
    private bool _isVideoPlaying;

    public required Guid MessageId { get; init; }
    public required string PeerUserId { get; init; }
    public required bool IsOutgoing { get; init; }
    public required DateTimeOffset SentAtUtc { get; init; }
    public string Kind { get; init; } = MessageKinds.Text;
    public string AttachmentPath { get; init; } = "";
    public string AttachmentUrl { get; init; } = "";
    public string FileName { get; init; } = "";
    public long FileSizeBytes { get; init; }
    public string MimeType { get; init; } = "";
    public string DriveFileId
    {
        get => _driveFileId;
        set
        {
            if (_driveFileId == value) return;
            _driveFileId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDeleteDriveVideo));
            OnPropertyChanged(nameof(HasRemoteVideo));
            OnPropertyChanged(nameof(VideoStatusText));
        }
    }

    public string DownloadUrl
    {
        get => _downloadUrl;
        set
        {
            if (_downloadUrl == value) return;
            _downloadUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDownloadUrl));
            OnPropertyChanged(nameof(HasRemoteVideo));
            OnPropertyChanged(nameof(VideoStatusText));
        }
    }

    public string StorageProvider
    {
        get => _storageProvider;
        set
        {
            if (_storageProvider == value) return;
            _storageProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanDeleteDriveVideo));
        }
    }
    public Guid? ReplyToMessageId { get; init; }
    public string ReplyPreview { get; init; } = "";
    public string ForwardedFrom { get; init; } = "";
    public string SenderUserId { get; set; } = "";

    public double GifRenderWidth => _gifRenderWidth;

    public double GifRenderHeight => _gifRenderHeight;

    public void SetGifRenderSize(double width, double height)
    {
        if (Math.Abs(_gifRenderWidth - width) < 0.5 && Math.Abs(_gifRenderHeight - height) < 0.5)
        {
            return;
        }

        _gifRenderWidth = width;
        _gifRenderHeight = height;
        OnPropertyChanged(nameof(GifRenderWidth));
        OnPropertyChanged(nameof(GifRenderHeight));
    }

    public required string Body
    {
        get => _body;
        set
        {
            _body = value;
            if (string.IsNullOrWhiteSpace(_text))
            {
                _text = value;
            }
        }
    }

    public string Text
    {
        get => string.IsNullOrWhiteSpace(_text) ? Body : _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            _body = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(PreviewText));
            OnPropertyChanged(nameof(HasText));
            OnPropertyChanged(nameof(IsEmojiOnlyText));
            OnPropertyChanged(nameof(ShowsPlainText));
            OnPropertyChanged(nameof(EmojiHtmlSource));
            OnPropertyChanged(nameof(TextSegments));
        }
    }

    public DateTimeOffset? EditedAtUtc
    {
        get => _editedAtUtc;
        set
        {
            if (_editedAtUtc == value)
            {
                return;
            }

            _editedAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEdited));
        }
    }

    public string ReactionsJson
    {
        get => _reactionsJson;
        set
        {
            if (_reactionsJson == value)
            {
                return;
            }

            _reactionsJson = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReactionsText));
            RefreshReactionItems();
            OnPropertyChanged(nameof(HasReactions));
        }
    }

    public string CurrentUserId
    {
        get => _currentUserId;
        set
        {
            if (_currentUserId == value)
            {
                return;
            }

            _currentUserId = value;
            RefreshReactionItems();
        }
    }

    public string SenderDisplayName
    {
        get => _senderDisplayName;
        set
        {
            if (_senderDisplayName == value)
            {
                return;
            }

            _senderDisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SenderInitials));
            OnPropertyChanged(nameof(HasSenderName));
        }
    }

    public string SenderAvatarKind
    {
        get => _senderAvatarKind;
        set
        {
            if (_senderAvatarKind == value)
            {
                return;
            }

            _senderAvatarKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSenderAvatarImage));
            OnPropertyChanged(nameof(IsSenderAvatarVideo));
            OnPropertyChanged(nameof(SenderAvatarImageSource));
            OnPropertyChanged(nameof(HasSenderAvatar));
        }
    }

    public string SenderAvatarPath
    {
        get => _senderAvatarPath;
        set
        {
            if (_senderAvatarPath == value)
            {
                return;
            }

            _senderAvatarPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSenderAvatarImage));
            OnPropertyChanged(nameof(IsSenderAvatarVideo));
            OnPropertyChanged(nameof(SenderAvatarImageSource));
            OnPropertyChanged(nameof(HasSenderAvatar));
        }
    }

    public string TimeText
    {
        get
        {
            var local = SentAtUtc.ToLocalTime();
            return local.Date == DateTimeOffset.Now.Date
                ? local.ToString("HH:mm")
                : local.ToString("dd.MM.yyyy HH:mm");
        }
    }

    public string Side => IsOutgoing ? "Right" : "Left";
    public bool IsTextMessage => Kind == MessageKinds.Text;
    public bool IsImageMessage => Kind == MessageKinds.Image;
    public bool IsGifMessage => Kind == MessageKinds.Gif;
    public bool IsFileMessage => Kind == MessageKinds.File;
    public bool IsVideoFileMessage => IsFileMessage &&
        (MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
         IsVideoExtension(FileName));
    public bool IsGenericFileMessage => IsFileMessage && !IsVideoFileMessage;
    public bool HasText => !string.IsNullOrWhiteSpace(Text) && !IsGifMessage;
    public bool IsEmojiOnlyText => IsTextMessage && HasText && IsEmojiOnly(Text);
    public bool ShowsPlainText => HasText && !IsEmojiOnlyText;
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentPath) || !string.IsNullOrWhiteSpace(AttachmentUrl);
    public bool HasReply => !string.IsNullOrWhiteSpace(ReplyPreview);
    public bool IsForwarded => !string.IsNullOrWhiteSpace(ForwardedFrom);
    public bool IsEdited => EditedAtUtc is not null;
    public bool CanEdit => IsOutgoing && IsTextMessage;
    public bool CanDelete => IsOutgoing;
    public bool CanShowMessageActions => !IsImageMessage && !IsFileMessage;
    public bool HasReactions => ReactionItems.Count > 0;
    public bool HasSenderName => !string.IsNullOrWhiteSpace(SenderDisplayName);
    public bool HasSenderAvatar => !string.IsNullOrWhiteSpace(SenderAvatarPath);
    public bool IsSenderAvatarImage => HasSenderAvatar && SenderAvatarKind == "image";
    public bool IsSenderAvatarVideo => HasSenderAvatar && SenderAvatarKind == "video";
    public ImageSource? SenderAvatarImageSource => IsSenderAvatarImage ? AvatarImageLoader.Load(SenderAvatarPath) : null;
    public string SenderInitials => BuildInitials(SenderDisplayName);
    public string PreviewText => IsImageMessage ? "Image" : IsGifMessage ? "GIF" : IsFileMessage ? FileName : Text;
    public string FileSizeText => FormatFileSize(FileSizeBytes);
    public string FileTypeText => string.IsNullOrWhiteSpace(MimeType) ? "FILE" : MimeType.Split('/').LastOrDefault()?.ToUpperInvariant() ?? "FILE";
    public bool HasDownloadUrl => !string.IsNullOrWhiteSpace(DownloadUrl);
    public string VideoLocalPath
    {
        get => _videoLocalPath;
        private set
        {
            if (_videoLocalPath == value) return;
            _videoLocalPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoSource));
            OnPropertyChanged(nameof(HasLocalVideo));
            OnPropertyChanged(nameof(CanDeleteLocalVideo));
        }
    }
    public Uri? VideoSource => HasLocalVideo ? new Uri(VideoLocalPath, UriKind.Absolute) : null;
    public VideoDownloadState VideoDownloadState
    {
        get => _videoDownloadState;
        private set
        {
            if (_videoDownloadState == value) return;
            _videoDownloadState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVideoReady));
            OnPropertyChanged(nameof(IsVideoDownloading));
            OnPropertyChanged(nameof(IsVideoWaiting));
            OnPropertyChanged(nameof(IsVideoFailed));
            OnPropertyChanged(nameof(VideoStatusText));
            OnPropertyChanged(nameof(CanDeleteDriveVideo));
        }
    }
    public double VideoDownloadProgress
    {
        get => _videoDownloadProgress;
        private set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_videoDownloadProgress - clamped) < 0.05) return;
            _videoDownloadProgress = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoProgressText));
        }
    }
    public string VideoError
    {
        get => _videoError;
        private set
        {
            if (_videoError == value) return;
            _videoError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoStatusText));
        }
    }
    public bool IsVideoReady => VideoDownloadState == VideoDownloadState.Ready && HasLocalVideo;
    public bool IsVideoDownloading => VideoDownloadState == VideoDownloadState.Downloading;
    public bool IsVideoWaiting => VideoDownloadState == VideoDownloadState.NotDownloaded;
    public bool IsVideoFailed => VideoDownloadState == VideoDownloadState.Failed;
    public bool HasLocalVideo => !string.IsNullOrWhiteSpace(VideoLocalPath) && File.Exists(VideoLocalPath);
    public bool CanDeleteLocalVideo => HasLocalVideo;
    public bool CanDeleteDriveVideo => IsVideoFileMessage && IsOutgoing &&
        string.Equals(StorageProvider, "GoogleDrive", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(DriveFileId) && !IsVideoDownloading;
    public bool HasRemoteVideo => !string.IsNullOrWhiteSpace(DriveFileId) || HasDownloadUrl;
    public string VideoProgressText => $"{VideoDownloadProgress:0}%";
    public string VideoStatusText => IsVideoFailed
        ? (string.IsNullOrWhiteSpace(VideoError) ? "Download failed. Click to retry." : VideoError)
        : IsVideoDownloading
            ? "Downloading video..."
            : HasRemoteVideo ? "Click to download video" : "Video was deleted from Google Drive";
    public double VideoDurationSeconds
    {
        get => _videoDurationSeconds;
        set
        {
            var normalized = Math.Max(0, value);
            if (Math.Abs(_videoDurationSeconds - normalized) < 0.01) return;
            _videoDurationSeconds = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoDurationText));
        }
    }
    public double VideoPositionSeconds
    {
        get => _videoPositionSeconds;
        set
        {
            var normalized = Math.Clamp(value, 0, Math.Max(VideoDurationSeconds, value));
            if (Math.Abs(_videoPositionSeconds - normalized) < 0.01) return;
            _videoPositionSeconds = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoPositionText));
        }
    }
    public double VideoVolume
    {
        get => _videoVolume;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (Math.Abs(_videoVolume - normalized) < 0.001) return;
            _videoVolume = normalized;
            OnPropertyChanged();
        }
    }
    public bool IsVideoPlaying
    {
        get => _isVideoPlaying;
        set
        {
            if (_isVideoPlaying == value) return;
            _isVideoPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoPlayPauseIcon));
        }
    }
    public string VideoPositionText => FormatMediaTime(VideoPositionSeconds);
    public string VideoDurationText => FormatMediaTime(VideoDurationSeconds);
    public string VideoPlayPauseIcon => IsVideoPlaying ? "Pause" : "Play";

    public void MarkVideoReady(string path)
    {
        VideoLocalPath = path;
        VideoError = "";
        VideoDownloadProgress = 100;
        VideoDownloadState = VideoDownloadState.Ready;
    }

    public void MarkVideoDownloading(double progressPercent = 0)
    {
        VideoError = "";
        VideoDownloadProgress = progressPercent;
        VideoDownloadState = VideoDownloadState.Downloading;
    }

    public void UpdateVideoDownloadProgress(double progressPercent)
        => VideoDownloadProgress = progressPercent;

    public void MarkVideoDownloadFailed(string error)
    {
        VideoLocalPath = "";
        VideoError = error;
        VideoDownloadState = VideoDownloadState.Failed;
    }

    public void MarkVideoPlaybackFailed(string error)
    {
        IsVideoPlaying = false;
        VideoError = error;
        VideoDownloadState = VideoDownloadState.Failed;
    }

    public void ClearLocalVideo()
    {
        IsVideoPlaying = false;
        VideoPositionSeconds = 0;
        VideoDurationSeconds = 0;
        VideoLocalPath = "";
        VideoError = "";
        VideoDownloadProgress = 0;
        VideoDownloadState = VideoDownloadState.NotDownloaded;
    }

    public void MarkDriveVideoDeleted()
    {
        DriveFileId = "";
        DownloadUrl = "";
        StorageProvider = "";
    }
    public Uri EmojiHtmlSource => IsEmojiOnlyText
        ? new Uri($"data:text/html;charset=utf-8,{Uri.EscapeDataString(BuildEmojiHtml(Text))}")
        : new Uri("about:blank");
    public IReadOnlyList<MessageTextSegment> TextSegments => MessageTextSegment.Build(Text, IsEmojiOnlyText ? 28 : 14);
    public ImageSource? AttachmentImageSource => HasAttachment ? AvatarImageLoader.Load(AttachmentPath) : null;
    public ObservableCollection<ReactionViewModel> ReactionItems { get; } = [];

    public string ReactionsText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ReactionsJson))
            {
                return "";
            }

            try
            {
                var reactions = JsonSerializer.Deserialize<Dictionary<string, string>>(ReactionsJson);
                return reactions is null || reactions.Count == 0
                    ? ""
                    : string.Join(" ", reactions.Values.GroupBy(x => x).Select(x => $"{x.Key} {x.Count()}"));
            }
            catch (JsonException)
            {
                return "";
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RefreshReactionItems()
    {
        ReactionItems.Clear();
        if (string.IsNullOrWhiteSpace(ReactionsJson))
        {
            return;
        }

        try
        {
            var reactions = JsonSerializer.Deserialize<Dictionary<string, string>>(ReactionsJson);
            if (reactions is null || reactions.Count == 0)
            {
                return;
            }

            foreach (var group in reactions
                         .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                         .GroupBy(x => x.Value))
            {
                var userIds = group.Select(x => x.Key).ToArray();
                var emoji = group.Key;
                ReactionItems.Add(new ReactionViewModel
                {
                    MessageId = MessageId,
                    Emoji = emoji,
                    Count = userIds.Length,
                    IsMine = !string.IsNullOrWhiteSpace(CurrentUserId) && userIds.Contains(CurrentUserId),
                    ImageUrl = EmojiToTwemojiUrl(emoji)
                });
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string EmojiToTwemojiUrl(string emoji)
    {
        var codepoints = emoji.EnumerateRunes()
            .Where(rune => rune.Value != 0xFE0F)
            .Select(rune => rune.Value.ToString("x"))
            .ToArray();
        return codepoints.Length == 0
            ? ""
            : $"https://cdn.jsdelivr.net/gh/jdecked/twemoji@latest/assets/72x72/{string.Join("-", codepoints)}.png";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {suffixes[index]}";
    }

    private static bool IsVideoExtension(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() is ".mp4" or ".webm" or ".mov" or ".m4v" or ".avi" or ".mkv";

    private static string FormatMediaTime(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private static bool IsEmojiOnly(string text)
    {
        var hasEmoji = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                continue;
            }

            if (IsEmojiRune(rune))
            {
                hasEmoji = true;
                continue;
            }

            return false;
        }

        return hasEmoji;
    }

    private static bool IsEmojiRune(Rune rune)
    {
        var value = rune.Value;
        return value == 0x200D ||
               value == 0xFE0F ||
               value == 0x20E3 ||
               value is >= 0x1F000 and <= 0x1FAFF ||
               value is >= 0x2600 and <= 0x27BF;
    }

    private static string BuildInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        var initials = string.Concat(displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(initials))
        {
            return "?";
        }

        return initials.Length <= 2 ? initials : initials[..2];
    }

    private static string BuildEmojiHtml(string emoji)
    {
        var encoded = WebUtility.HtmlEncode(emoji);
        return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: transparent; }
    body { display: flex; align-items: center; justify-content: flex-start; font-family: "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", sans-serif; font-size: 28px; line-height: 1.15; color: #f2f3f5; user-select: none; -webkit-user-select: none; }
  </style>
</head>
<body>{{encoded}}</body>
</html>
""";
    }
}

public sealed class ReactionViewModel
{
    public required Guid MessageId { get; init; }
    public required string Emoji { get; init; }
    public required string ImageUrl { get; init; }
    public required int Count { get; init; }
    public required bool IsMine { get; init; }
    public string CountText => Count > 1 ? Count.ToString() : "";
}

public sealed class MessageTextSegment
{
    public string Text { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public double Size { get; init; } = 14;
    public bool IsEmoji => !string.IsNullOrWhiteSpace(ImageUrl);
    public bool IsText => !IsEmoji;
    public double ImageSize => Size >= 24 ? 30 : 20;
    public double TextLineHeight => Size >= 24 ? 34 : 21;

    public static IReadOnlyList<MessageTextSegment> Build(string text, double size)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var segments = new List<MessageTextSegment>();
        var textBuffer = new StringBuilder();
        var runes = text.EnumerateRunes().ToArray();

        for (var i = 0; i < runes.Length; i++)
        {
            var rune = runes[i];
            if (!IsEmojiStart(rune))
            {
                textBuffer.Append(rune.ToString());
                continue;
            }

            FlushText(segments, textBuffer, size);
            var emoji = new StringBuilder(rune.ToString());

            while (i + 1 < runes.Length && IsEmojiContinuation(runes[i + 1]))
            {
                i++;
                emoji.Append(runes[i].ToString());
                if (runes[i].Value == 0x200D && i + 1 < runes.Length)
                {
                    i++;
                    emoji.Append(runes[i].ToString());
                }
            }

            var emojiText = emoji.ToString();
            segments.Add(new MessageTextSegment
            {
                Text = emojiText,
                ImageUrl = EmojiToTwemojiUrl(emojiText),
                Size = size
            });
        }

        FlushText(segments, textBuffer, size);
        return segments;
    }

    private static void FlushText(ICollection<MessageTextSegment> segments, StringBuilder textBuffer, double size)
    {
        if (textBuffer.Length == 0)
        {
            return;
        }

        segments.Add(new MessageTextSegment
        {
            Text = textBuffer.ToString(),
            Size = size
        });
        textBuffer.Clear();
    }

    private static bool IsEmojiStart(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x1F000 and <= 0x1FAFF ||
               value is >= 0x2600 and <= 0x27BF ||
               value is >= 0x2300 and <= 0x23FF ||
               value is >= 0x2B00 and <= 0x2BFF ||
               value is 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x3030 or 0x303D or 0x3297 or 0x3299;
    }

    private static bool IsEmojiContinuation(Rune rune)
    {
        var value = rune.Value;
        return value == 0x200D ||
               value == 0xFE0F ||
               value == 0xFE0E ||
               value == 0x20E3 ||
               value is >= 0x1F3FB and <= 0x1F3FF ||
               value is >= 0xE0020 and <= 0xE007F;
    }

    private static string EmojiToTwemojiUrl(string emoji)
    {
        var codepoints = emoji.EnumerateRunes()
            .Where(rune => rune.Value != 0xFE0F)
            .Select(rune => rune.Value.ToString("x"))
            .ToArray();
        return codepoints.Length == 0
            ? ""
            : $"https://cdn.jsdelivr.net/gh/jdecked/twemoji@latest/assets/72x72/{string.Join("-", codepoints)}.png";
    }
}

public static class MessageKinds
{
    public const string Text = "Text";
    public const string Image = "Image";
    public const string Gif = "Gif";
    public const string File = "File";
}

public enum VideoDownloadState
{
    NotDownloaded,
    Downloading,
    Ready,
    Failed
}

public sealed class TenorGifViewModel : INotifyPropertyChanged
{
    private bool _isFavorite;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string PreviewUrl { get; init; }
    public required string GifUrl { get; init; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteText));
        }
    }

    public string FavoriteText => IsFavorite ? "★" : "☆";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class EmojiItemViewModel
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string SearchText => $"{Name} {Category} {Symbol}";
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

public sealed class GroupMemberViewModel : INotifyPropertyChanged
{
    private string _displayName = "";
    private string _relayServer = "";
    private string _avatarKind = "color";
    private string _avatarPath = "";
    private UserPresenceStatus _status = UserPresenceStatus.Offline;
    private bool _isOwner;
    private bool _isFriend;
    private bool _isSelf;
    private bool _canManageGroup;

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

    public string RelayServer
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

    public bool IsOwner
    {
        get => _isOwner;
        set
        {
            if (_isOwner == value)
            {
                return;
            }

            _isOwner = value;
            OnPropertyChanged();
        }
    }

    public bool IsFriend
    {
        get => _isFriend;
        set
        {
            if (_isFriend == value)
            {
                return;
            }

            _isFriend = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAddFriend));
            OnPropertyChanged(nameof(CanRenameContact));
            OnPropertyChanged(nameof(CanWriteMessage));
        }
    }

    public bool IsSelf
    {
        get => _isSelf;
        set
        {
            if (_isSelf == value)
            {
                return;
            }

            _isSelf = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAddFriend));
            OnPropertyChanged(nameof(CanMakeOwner));
            OnPropertyChanged(nameof(CanRemoveFromGroup));
        }
    }

    public bool CanManageGroup
    {
        get => _canManageGroup;
        set
        {
            if (_canManageGroup == value)
            {
                return;
            }

            _canManageGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanMakeOwner));
            OnPropertyChanged(nameof(CanRemoveFromGroup));
        }
    }

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarPath);
    public bool IsAvatarImage => HasAvatar && AvatarKind == "image";
    public bool IsAvatarVideo => HasAvatar && AvatarKind == "video";
    public ImageSource? AvatarImageSource => IsAvatarImage ? AvatarImageLoader.Load(AvatarPath) : null;
    public string ShortId => UserId.Length <= 8 ? UserId : UserId[..8];
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

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return "?";
            }

            var initials = string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(initials))
            {
                return "?";
            }

            return initials.Length <= 2 ? initials : initials[..2];
        }
    }
    public bool CanAddFriend => !IsSelf && !IsFriend;
    public bool CanRenameContact => IsFriend;
    public bool CanWriteMessage => IsFriend;
    public bool CanMakeOwner => CanManageGroup && !IsSelf && !IsOwner;
    public bool CanRemoveFromGroup => CanManageGroup && !IsSelf;

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
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Write(ex, $"Avatar image stat failed: path={path}");
            return null;
        }

        var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            TrimCacheIfNeeded();
            var image = LoadUncached(path);
            if (image is not null)
            {
                Cache.TryAdd(key, image);
            }

            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            AppLog.Write(ex, $"Avatar image load failed: path={path}");
            return null;
        }
    }

    private static ImageSource? LoadUncached(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.None;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void TrimCacheIfNeeded()
    {
        if (Cache.Count < 256)
        {
            return;
        }

        Cache.Clear();
    }
}
