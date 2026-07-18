using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Concentus;
using Concentus.Enums;
using FluxChat.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace FluxChat.Client;

public partial class MainWindow : Window
{
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ContactOfflineAfter = TimeSpan.FromSeconds(45);
    private const string RelayContactPrefix = "VPS ";
    private const string FriendRequestIntent = "friend-request";
    private const string FriendAcceptIntent = "friend-accept";
    private const string FriendRemoveIntent = "friend-remove";
    private const string ProfileUpdateIntent = "profile-update";
    private const string ProfileRequestIntent = "profile-request";
    private const string GroupUpsertIntent = "group-upsert";
    private const string GroupDeleteIntent = "group-delete";
    private const string GroupLeaveIntent = "group-leave";
    private const string GroupKickIntent = "group-kick";
    private const string GroupTransferOwnerIntent = "group-transfer-owner";
    private const string GroupMessageIntent = "group-message";
    private const string ChatRichIntent = "chat-rich";
    private const string ChatEditIntent = "chat-edit";
    private const string ChatReactionIntent = "chat-reaction";
    private const string ChatDeleteIntent = "chat-delete";
    private const string CallInviteIntent = "call-invite";
    private const string CallAcceptIntent = "call-accept";
    private const string CallDeclineIntent = "call-decline";
    private const string CallEndIntent = "call-end";
    private const string CallLeaveIntent = "call-leave";
    private const string CallJoinIntent = "call-join";
    private const string CallAudioIntent = "call-audio";
    private const string CallAudioStateIntent = "call-audio-state";
    private const string CallPingIntent = "call-ping";
    private const string CallPongIntent = "call-pong";
    private const string CallScreenStartIntent = "call-screen-start";
    private const string CallScreenFrameIntent = "call-screen-frame";
    private const string CallScreenStopIntent = "call-screen-stop";
    private const string CallScreenWebRtcOfferIntent = "call-screen-webrtc-offer";
    private const string CallScreenWebRtcAnswerIntent = "call-screen-webrtc-answer";
    private const string CallScreenWebRtcIceIntent = "call-screen-webrtc-ice";
    private const string CallScreenWebRtcFallbackIntent = "call-screen-webrtc-fallback";
    private const string LegacyFriendRequestBody = "Friend request";
    private const string LegacyFriendAcceptBody = "Friend request accepted";
    private const string ControlBodyPrefix = "fluxchat-control:";
    private const int MaxAvatarSyncBytes = 5_000_000;
    private const int ScreenShareMaxFrameBodyChars = 1_800_000;
    private const long ScreenShareJpegQuality = 74L;
    private const long ScreenShareHighFrameRateJpegQuality = 62L;
    private const long ScreenShareHighLoadJpegQuality = 58L;
    private const int ScreenShareMaxCompactHeight = 150;
    private static readonly TimeSpan ScreenShareSelfPreviewInterval = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan ScreenShareEncodedLocalPreviewInterval = TimeSpan.FromMilliseconds(500);
    private const int ScreenShareMinAdaptiveHeight = 720;
    private const int ScreenShareHighResolutionMinAdaptiveHeight = 1080;
    private const int ScreenShareAdaptiveStep = 120;
    private const int ScreenShareHighResolutionMaxFrameRate = 30;
    private const int ScreenShareFallbackMaxHeight = 720;
    private const int ScreenShareFallbackMaxFrameRate = 15;
    private const int ScreenShareVoiceProtectedMaxHeight = 1080;
    private const int ScreenShareVoiceProtectedMaxFrameRate = 30;
    private const int ScreenShareVoiceProtectedWebRtcMaxBitrate = 4_500_000;
    private const int ScreenShareVoiceProtectedH264MaxBitrateKbps = 4_500;
    private const int ScreenShareMaxPeerRenderFrameRate = 60;
    private const int ScreenShareFullscreenMaxPeerRenderFrameRate = 60;
    private const int ScreenShareEncodedChunkSize = 32 * 1024;
    private const int ScreenShareEncodedDataChannelBufferLimit = 4 * 1024 * 1024;
    private static readonly TimeSpan ScreenShareSendTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly bool ScreenSharePreferWebRtc = true;
    private static readonly bool ScreenSharePreferEncodedWebRtc = true;
    private static readonly TimeSpan ScreenShareDuplicateFrameInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ScreenShareWebRtcDuplicateFrameInterval = TimeSpan.Zero;
    private const string ScreenShareCodecJpeg = "jpeg";
    private const string ScreenShareCodecH264Fmp4 = "h264-fmp4";
    private const string CallAudioCodecOpus = "opus";
    private const int CallAudioOpusPayloadVersion = 1;
    private const int CallAudioSampleRate = 16000;
    private const int CallAudioChannels = 1;
    private const int CallAudioOpusFrameSize = CallAudioSampleRate / 50;
    private const int CallAudioOpusBitrate = 32000;
    private const int CallAudioOpusExpectedLossPercent = 5;
    private const int CallAudioOpusMaxPacketBytes = 512;
    private const int CallAudioMixPayloadVersion = 1;
    private const int SoundboardMaxDurationSeconds = 30;
    private const int CallAudioMinDecodedBytes = 64;
    private const int CallAudioMaxDecodedBytes = 2560;
    private const int CallAudioTargetPeak = 7000;
    private const int CallAudioOutputLimitPeak = 20000;
    private const double CallAudioMicrophoneGain = 1.65;
    private const int CallAudioSilencePeak = 24;
    private const int CallAudioVoiceFloorPeak = 140;
    private const int CallAudioMaxCaptureQueueFrames = 12;
    private const int CallAudioMaxPlaybackQueueFrames = 12;
    private const int CallAudioLossSequenceDelay = 8;
    private const int CallAudioJitterWarmupFrames = 4;
    private const int CallAudioJitterLossDelay = 2;
    private const int CallAudioJitterMaxFrames = 32;
    private const int CallAudioMaxConcealmentFrames = 3;
    private const double CallAudioMaxGain = 2.4;
    private static readonly TimeSpan CallAudioSendTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan CallNetworkPingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CallNetworkPingTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CallAudioLossReportDelay = TimeSpan.FromSeconds(4);
    private const double AvatarEditorPreviewSize = 350;
    private const double AvatarEditorCircleSize = 344;
    private const double ProfileAvatarSize = 44;
    private const double SettingsAvatarSize = 64;
    private const double PickerGap = 10;
    private const double PickerDefaultWorkspaceHeight = 350;
    private const double PickerMinWidth = 250;
    private const double PickerMinHeight = 210;

    private enum ScreenShareFocusTarget
    {
        Auto,
        Local,
        Peer
    }

    private readonly ObservableCollection<ContactViewModel> _contacts = [];
    private readonly ObservableCollection<MessageViewModel> _messages = [];
    private readonly ObservableCollection<FriendRequestViewModel> _friendRequests = [];
    private readonly ObservableCollection<TenorGifViewModel> _gifResults = [];
    private readonly ObservableCollection<TenorGifViewModel> _favoriteGifs = [];
    private readonly ObservableCollection<SoundboardClipViewModel> _soundboardClips = [];
    private readonly HttpClient _httpClient = new();
    private readonly ObservableCollection<GroupCandidateViewModel> _groupCandidates = [];
    private readonly ObservableCollection<GroupMemberViewModel> _groupMembers = [];
    private readonly ObservableCollection<ScreenShareSourceItem> _screenShareSources = [];
    private readonly ObservableCollection<ScreenShareSourceItem> _visibleScreenShareSources = [];
    private readonly HashSet<string> _selectedGroupMemberIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeCallPeerUserIds = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _activeCallTargetUserIds = [];
    private readonly Dictionary<string, DateTimeOffset> _profileRequestAttempts = [];
    private readonly Dictionary<CoreWebView2, WeakReference<Microsoft.Web.WebView2.Wpf.WebView2CompositionControl>> _messageGifViews = [];
    private readonly Dictionary<string, GifRenderDimensions> _messageGifDimensions = new(StringComparer.Ordinal);
    private readonly HistoryStore _history = new();
    private readonly CancellationTokenSource _stop = new();
    private AppSettings _settings = new();
    private UserProfile? _profile;
    private RelayClient? _relayClient;
    private BadgeAuthorityClient? _badgeAuthority;
    private BadgeStateResponse? _badgeState;
    private BadgeAdminUserResponse? _badgeAdminTarget;
    private bool _badgeAdminSessionAuthenticated;
    private Forms.NotifyIcon? _notifyIcon;
    private ContactViewModel? _selectedContact;
    private ContactViewModel? _draftGroupContact;
    private MessageViewModel? _replyTarget;
    private MessageViewModel? _editingMessage;
    private MessageViewModel? _forwardTarget;
    private MessageViewModel? _reactionTarget;
    private MessageViewModel? _imageViewerMessage;
    private double _imageViewerSourceWidth;
    private double _imageViewerSourceHeight;
    private double _imageViewerZoom = 1;
    private double _imageViewerFitZoom = 1;
    private string _draftImagePath = "";
    private bool _emojiWebViewReady;
    private Task? _emojiWebViewInitializationTask;
    private bool _emojiViewportUpdatePending;
    private double _emojiViewportWidth;
    private double _emojiViewportHeight;
    private int _pickerZIndex = 1;
    private Border? _activePickerResizePanel;
    private Rect _pickerResizeStartRect;
    private Border? _pendingPickerResizePanel;
    private Rect _pendingPickerResizeRect;
    private bool _pickerArrangePending;
    private UserPresenceStatus _selectedStatus = UserPresenceStatus.Online;
    private string _selectedAvatarColor = "#5865f2";
    private string _selectedAvatarKind = "color";
    private string _selectedAvatarPath = "";
    private double _selectedAvatarScale = 1;
    private double _selectedAvatarOffsetX;
    private double _selectedAvatarOffsetY;
    private double _selectedAvatarVideoStartSeconds;
    private double _selectedAvatarVideoDurationSeconds = 10;
    private string _pendingAvatarKind = "color";
    private string _pendingAvatarPath = "";
    private double _pendingAvatarScale = 1;
    private double _pendingAvatarOffsetX;
    private double _pendingAvatarOffsetY;
    private double _pendingAvatarVideoStartSeconds;
    private double _pendingAvatarVideoDurationSeconds = 10;
    private bool _isDraggingAvatar;
    private System.Windows.Point _lastAvatarDragPoint;
    private readonly DispatcherTimer _avatarVideoLoopTimer;
    private readonly DispatcherTimer _presenceTimer;
    private readonly DispatcherTimer _badgeRefreshTimer;
    private readonly DispatcherTimer _callRingtoneTimer;
    private readonly DispatcherTimer _callNetworkMetricsTimer;
    private readonly Queue<CallAudioSendReport> _peerAudioSendReports = new();
    private UserPresenceStatus _lastPublishedStatus = UserPresenceStatus.Offline;
    private bool _isWindowActive = true;
    private ContactViewModel? _activeCallContact;
    private string _activeCallState = "";
    private bool _selfInCall;
    private bool _peerInCall;
    private bool _isMicrophoneMuted;
    private bool _isHeadphonesMuted;
    private bool _peerMicrophoneMuted;
    private bool _peerHeadphonesMuted;
    private bool _isScreenSharing;
    private bool _peerScreenSharing;
    private bool _isWatchingPeerScreen;
    private bool _isScreenShareFocusMode;
    private bool _isScreenShareFullscreenMode;
    private bool _screenShareWindowFullscreenApplied;
    private bool _screenSharePickerSuppressesStage;
    private ScreenShareFocusTarget _screenShareFocusTarget = ScreenShareFocusTarget.Auto;
    private WindowStyle _screenSharePreviousWindowStyle;
    private WindowState _screenSharePreviousWindowState;
    private ResizeMode _screenSharePreviousResizeMode;
    private int _screenShareResolution = 1080;
    private int _screenShareFrameRate = 30;
    private int _screenShareAdaptiveHeight = 1080;
    private bool _screenShareMuteAudio = true;
    private string _screenShareQualityPreset = "Balanced";
    private string _screenShareSourceFilter = "All";
    private int _pendingScreenShareFrame;
    private int _pendingPeerScreenShareFrame;
    private int _pendingNativeWebRtcWebViewFrame;
    private int _screenShareJpegLoopStarted;
    private bool _screenShareUsingNativeWebRtc;
    private bool _screenShareWebRtcReady;
    private bool _screenShareWebRtcActive;
    private bool _screenShareWebRtcInitializing;
    private bool _screenShareWebRtcSettingsMode;
    private bool _screenShareStartSignalSent;
    private bool _screenShareUsingEncodedWebRtc;
    private bool _screenShareEncodedChannelOpen;
    private bool _peerScreenShareUsingWebRtc;
    private long _sentScreenShareFrames;
    private long _sentEncodedScreenShareChunks;
    private long _receivedScreenShareFrames;
    private long _droppedReceivedScreenShareFrames;
    private long _lastSelfScreenSharePreviewTicks;
    private long _lastEncodedScreenSharePreviewTicks;
    private long _lastPeerScreenShareFrameAcceptedTicks;
    private ulong _lastScreenShareFrameHash;
    private long _lastScreenShareFrameHashSentTicks;
    private ScreenShareSourceItem? _activeScreenShareSource;
    private CancellationTokenSource? _screenShareStop;
    private Process? _screenShareEncoderProcess;
    private readonly object _peerScreenShareFrameGate = new();
    private QueuedScreenShareFrame? _latestPeerScreenShareFrame;
    private AudioCallSession? _audioCall;
    private AudioCallSession? _voiceTestSession;
    private CancellationTokenSource? _audioSendLoopStop;
    private CancellationTokenSource? _audioPlaybackLoopStop;
    private readonly object _audioStartGate = new();
    private string? _audioStartingPeerId;
    private int _audioStartGeneration;
    private bool _isVoiceTestActive;
    private int _isStoppingVoiceTest;
    private bool _isRefreshingAudioDevices;
    private long _lastVoiceTestUiTicks;
    private readonly object _audioFrameGate = new();
    private readonly Queue<byte[]> _pendingAudioCaptureFrames = new();
    private int _pendingAudioFrame;
    private long _pendingAudioFrameStartedUtcTicks;
    private long _lastAudioPingTicks;
    private long _lastRelayAudioReceivedTicks;
    private int _udpAudioWarningShown;
    private readonly Dictionary<long, DateTimeOffset> _pendingCallPings = [];
    private long _callPingSequence;
    private long _lastCallPingSentTicks;
    private double _currentCallPingMs = double.NaN;
    private double _averageCallPingMs;
    private int _callPingSamples;
    private long _callAudioSendSequence;
    private readonly object _callAudioLossGate = new();
    private readonly SortedSet<long> _receivedCallAudioSequences = [];
    private long _callAudioLossCursor;
    private long _sequencedCallAudioPackets;
    private long _lostCallAudioPackets;
    private long _peerAudioSentFramesBaseline = -1;
    private long _localAudioReceivedFramesBaseline = -1;
    private long _peerAudioSentFramesLatest;
    private long _peerAudioSentFramesMaturedLatest;
    private long _capturedAudioFrames;
    private long _droppedAudioFrames;
    private long _sentAudioFrames;
    private long _relayReceivedAudioFrames;
    private long _tcpReceivedAudioFrames;
    private long _receivedAudioFrames;
    private long _legacyAudioFrames;
    private long _failedPlaybackFrames;
    private long _droppedPlaybackQueueFrames;
    private long _quietPlaybackFrames;
    private long _quietCaptureFrames;
    private long _audioSendTimeouts;
    private readonly object _soundboardAudioGate = new();
    private byte[]? _activeSoundboardPcm;
    private int _activeSoundboardOffset;
    private SoundboardClipViewModel? _activeSoundboardClip;
    private double _soundboardVolume = 0.8;
    private CallAudioPreferences _callAudioPreferences = new();
    private bool _isInitializingAudioFeatureControls;
    private double _noiseFloorRms = 90;
    private double _noiseGateGain = 1;
    private readonly object _callOpusGate = new();
    private IOpusEncoder? _callOpusEncoder;
    private IOpusDecoder? _callOpusDecoder;
    private IOpusEncoder? _callSoundboardOpusEncoder;
    private IOpusDecoder? _callSoundboardOpusDecoder;
    private long _opusEncodedAudioFrames;
    private long _opusDecodedAudioFrames;
    private long _opusFecRecoveredAudioFrames;
    private long _opusConcealedAudioFrames;
    private readonly ConcurrentQueue<CallPlaybackFrame> _callPlaybackQueue = new();
    private readonly object _callJitterGate = new();
    private readonly SortedDictionary<long, CallPlaybackFrame> _callJitterFrames = [];
    private long _callJitterExpectedSequence;
    private long _callJitterHighestSequence;
    private bool _callJitterWarmedUp;
    private byte[]? _lastCallPlaybackPcm;
    private int _consecutiveCallConcealmentFrames;
    private readonly SemaphoreSlim _callPlaybackSignal = new(0, int.MaxValue);
    private readonly ObservableCollection<MessageTextSegment> _messageInputTextSegments = [];
    private string? _notificationContactUserId;

    public MainWindow()
    {
        InitializeComponent();
        ScreenShareSourceList.ItemsSource = _visibleScreenShareSources;
        ScreenShareStreamAudioCheck.IsChecked = !_screenShareMuteAudio;
        UpdateScreenSharePickerState();
        _avatarVideoLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _avatarVideoLoopTimer.Tick += AvatarVideoLoopTimer_OnTick;
        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _presenceTimer.Tick += async (_, _) =>
        {
            MarkStaleContactsOffline();
            await PublishPresenceAsync();
        };
        _badgeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _badgeRefreshTimer.Tick += async (_, _) => await RefreshBadgeStateAsync();
        _callRingtoneTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _callRingtoneTimer.Tick += (_, _) => System.Media.SystemSounds.Exclamation.Play();
        _callNetworkMetricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _callNetworkMetricsTimer.Tick += CallNetworkMetricsTimer_OnTick;
        ProfileAvatarVideo.MediaOpened += AvatarVideo_OnMediaOpened;
        ProfileAvatarVideo.MediaEnded += AvatarVideo_OnMediaEnded;
        SettingsAvatarVideo.MediaOpened += AvatarVideo_OnMediaOpened;
        SettingsAvatarVideo.MediaEnded += AvatarVideo_OnMediaEnded;
        AvatarEditorVideo.MediaOpened += AvatarEditorVideo_OnMediaOpened;
        AvatarEditorVideo.MediaEnded += AvatarVideo_OnMediaEnded;
        ContactsList.ItemsSource = _contacts;
        MessagesList.ItemsSource = _messages;
        MessageInputEmojiPreview.ItemsSource = _messageInputTextSegments;
        GifResultsList.ItemsSource = _gifResults;
        GifFavoritesList.ItemsSource = _favoriteGifs;
        SoundboardClipsList.ItemsSource = _soundboardClips;
        ForwardContactsList.ItemsSource = _contacts;
        FriendRequestsList.ItemsSource = _friendRequests;
        GroupFriendsList.ItemsSource = _groupCandidates;
        GroupMembersList.ItemsSource = _groupMembers;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FluxChat/1.0");
        Loaded += OnLoaded;
        Closed += OnClosed;
        KeyDown += MainWindow_OnKeyDown;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        PreviewTextInput += MainWindow_OnPreviewTextInput;
        Activated += (_, _) => _isWindowActive = true;
        Deactivated += (_, _) => _isWindowActive = false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Main window startup failed");
            NetworkStatusText.Text = "Startup failed. See crash.log.";
            MessageBox.Show(
                $"FluxChat could not start.\n\n{ex.Message}\n\nLog: {CrashLog.LogPath}",
                "FluxChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void MainWindow_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (ImageViewerOverlay.Visibility == Visibility.Visible)
        {
            CloseImageViewer();
            e.Handled = true;
            return;
        }

        if (_isScreenShareFullscreenMode)
        {
            SetScreenShareFullscreenMode(false);
            UpdateScreenShareStageVisibility();
            e.Handled = true;
            return;
        }

        if (_isScreenShareFocusMode)
        {
            ExitScreenShareFocusMode();
            e.Handled = true;
        }
    }

    private void MainWindow_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Handled ||
            string.IsNullOrEmpty(e.Text) ||
            _selectedContact is null ||
            ComposerPanel.Visibility != Visibility.Visible ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ||
            IsTextEntryFocus())
        {
            return;
        }

        InsertTextIntoMessageInput(e.Text);
        e.Handled = true;
    }

    private static bool IsTextEntryFocus()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused is not null)
        {
            if (focused is System.Windows.Controls.TextBox ||
                focused is PasswordBox ||
                focused is System.Windows.Controls.RichTextBox ||
                focused is System.Windows.Controls.ComboBox)
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private async void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (IsTextEntryFocus())
        {
            return;
        }

        if (await TryStageImageFromClipboardAsync())
        {
            e.Handled = true;
        }
    }

    private async Task InitializeAsync()
    {
        AppLog.Write("Main window initialization started");
        var isFirstRun = !AppSettingsStore.Exists();
        _settings = await AppSettingsStore.LoadAsync();
        _isInitializingAudioFeatureControls = true;
        SettingsNoiseSuppressionCheck.IsChecked = _settings.NoiseSuppressionEnabled;
        _isInitializingAudioFeatureControls = false;
        await LoadCallAudioFeaturesAsync();
        _profile = await UserProfileStore.LoadOrCreateAsync();
        AppLog.Write($"Profile loaded: userId={_profile.UserId}, displayName={_profile.DisplayName}");
        _badgeAuthority = new BadgeAuthorityClient(_profile, _settings.BadgeAuthorityUrl);
        _badgeState = await _badgeAuthority.LoadVerifiedCacheAsync();
        ApplyBadgeState(_badgeState, "Using last verified badge state.");
        await _history.InitializeAsync();
        AppLog.Write("History store initialized");

        LoadAvatarSelectionFromProfile();
        RefreshProfileUi();
        InitializeNotifications();
        ServerAddressInput.Text = _settings.RelayServer;
        ServerAccessKeyInput.Text = _settings.RelayAccessKey;
        SettingsServerAddressInput.Text = _settings.RelayServer;
        SettingsServerAccessKeyInput.Text = _settings.RelayAccessKey;
        SettingsTenorApiKeyInput.Text = _settings.TenorApiKey;
        LoadFavoriteGifs();
        RefreshAudioDeviceSelectors();
        FirstRunServerAddressInput.Text = _settings.RelayServer;
        FirstRunServerAccessKeyInput.Text = _settings.RelayAccessKey;
        AddContactModeText.Text = "User ID";
        ManualIpInput.ToolTip = "Friend UserId or UserId@host:port";
        AddFriendInput.ToolTip = "Friend UserId or UserId@host:port";

        var loadedContacts = await _history.LoadContactsAsync();
        AppLog.Write($"Loaded contacts: count={loadedContacts.Count}");
        foreach (var contact in loadedContacts)
        {
            contact.Status = UserPresenceStatus.Offline;
            ValidateStoredContactBadge(contact);
            EnsureGroupMetadata(contact);
            AddOrUpdateContact(contact);
            if (contact.IsGroup)
            {
                _ = _history.SaveContactAsync(contact);
            }
        }

        _relayClient = new RelayClient(_profile);
        _relayClient.ActiveBadgeCertificate = GetActiveBadgeCertificate();
        _relayClient.MessageReceived += OnRelayMessageReceived;
        _relayClient.AudioReceived += OnRelayAudioReceived;
        _relayClient.ScreenFrameReceived += OnRelayScreenFrameReceived;
        _relayClient.PresenceReceived += OnRelayPresenceReceived;
        _relayClient.StatusChanged += OnNetworkStatusChanged;
        await ConnectRelayAsync();
        _ = RefreshBadgeStateAsync();
        _presenceTimer.Start();
        _badgeRefreshTimer.Start();

        NetworkStatusText.Text = "VPS mode ready. Add a contact by UserId.";
        if (ScreenSharePreferWebRtc)
        {
            _ = InitializeScreenShareWebRtcAsync();
        }
        else
        {
            AppLog.Write("Screen share WebRTC browser capture disabled; using native capture path.");
        }
        _ = CheckForUpdatesAsync();
        if (isFirstRun)
        {
            FirstRunVpsOverlay.Visibility = Visibility.Visible;
            FirstRunServerAddressInput.Focus();
            NetworkStatusText.Text = "Connect your VPS server to start.";
        }
    }

    private async Task InitializeScreenShareWebRtcAsync()
    {
        if (_screenShareWebRtcInitializing || _screenShareWebRtcReady)
        {
            return;
        }

        _screenShareWebRtcInitializing = true;
        try
        {
            var webRoot = Path.Combine(AppPaths.DataDirectory, "webrtc");
            Directory.CreateDirectory(webRoot);
            await File.WriteAllTextAsync(Path.Combine(webRoot, "screen-share.html"), ScreenShareWebRtcHtml, _stop.Token);
            await ScreenShareWebView.EnsureCoreWebView2Async();
            ScreenShareWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            ScreenShareWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            ScreenShareWebView.CoreWebView2.WebMessageReceived += ScreenShareWebView_OnWebMessageReceived;
            ScreenShareWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "fluxchat.local",
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            ScreenShareWebView.CoreWebView2.Navigate("https://fluxchat.local/screen-share.html");
            AppLog.Write("Screen share WebRTC view initialized");
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Screen share WebRTC initialization failed");
            _screenShareWebRtcReady = false;
        }
        finally
        {
            _screenShareWebRtcInitializing = false;
        }
    }

    private void ScreenShareWebView_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = GetJsonString(root, "type");
            switch (type)
            {
                case "ready":
                    _screenShareWebRtcReady = true;
                    AppLog.Write("Screen share WebRTC ready");
                    ConfigureScreenShareWebRtc();
                    UpdateScreenSharePickerState();
                    break;
                case "offer":
                    SendScreenShareWebRtcSignal(CallScreenWebRtcOfferIntent, e.WebMessageAsJson);
                    break;
                case "answer":
                    SendScreenShareWebRtcSignal(CallScreenWebRtcAnswerIntent, e.WebMessageAsJson);
                    break;
                case "ice":
                    SendScreenShareWebRtcSignal(CallScreenWebRtcIceIntent, e.WebMessageAsJson);
                    break;
                case "local-started":
                    var localSource = _activeScreenShareSource;
                    if (!_isScreenSharing || localSource is null)
                    {
                        PostScreenShareWebRtcMessage(new { type = "stop-local" });
                        break;
                    }

                    _screenShareWebRtcActive = true;
                    SetScreenShareWebRtcVisible(true);
                    SendScreenShareStartSignal(localSource, useWebRtc: true);

                    if (!_isScreenShareFocusMode)
                    {
                        EnterScreenShareFocusMode();
                    }

                    UpdateScreenShareStageVisibility();
                    AppLog.Write("Screen share WebRTC local stream started");
                    break;
                case "encoded-local-started":
                    var encodedSource = _activeScreenShareSource;
                    if (!_isScreenSharing || encodedSource is null)
                    {
                        PostScreenShareWebRtcMessage(new { type = "stop-local" });
                        break;
                    }

                    _screenShareWebRtcActive = true;
                    SetScreenShareWebRtcVisible(true);
                    SendScreenShareStartSignal(encodedSource, useWebRtc: true);
                    if (!_isScreenShareFocusMode)
                    {
                        EnterScreenShareFocusMode();
                    }

                    UpdateScreenShareStageVisibility();
                    AppLog.Write("Screen share encoded WebRTC signaling started");
                    break;
                case "encoded-channel-open":
                    _screenShareEncodedChannelOpen = true;
                    StartEncodedScreenShareEncoderIfReady();
                    AppLog.Write("Screen share encoded WebRTC data channel opened");
                    break;
                case "encoded-channel-closed":
                    _screenShareEncodedChannelOpen = false;
                    if (_screenShareUsingEncodedWebRtc)
                    {
                        FallbackScreenShareFromWebRtc("encoded WebRTC data channel closed");
                    }
                    break;
                case "remote-started":
                    _peerScreenSharing = true;
                    _isWatchingPeerScreen = true;
                    _peerScreenShareUsingWebRtc = true;
                    _screenShareWebRtcActive = true;
                    SetScreenShareWebRtcVisible(true);
                    if (!_isScreenShareFocusMode)
                    {
                        EnterScreenShareFocusMode();
                    }

                    UpdateScreenShareStageVisibility();
                    if (_activeCallContact is { IsGroup: false } remoteContact)
                    {
                        ApplyRemoteStreamAudioPreference(remoteContact.UserId);
                    }
                    AppLog.Write("Screen share WebRTC remote stream started");
                    break;
                case "remote-playing":
                    _peerScreenSharing = true;
                    _isWatchingPeerScreen = true;
                    _peerScreenShareUsingWebRtc = true;
                    _screenShareWebRtcActive = true;
                    SetScreenShareWebRtcVisible(true);
                    UpdateScreenShareStageVisibility();
                    AppLog.Write($"Screen share WebRTC remote video playing: width={GetJsonDouble(root, "width"):0}, height={GetJsonDouble(root, "height"):0}, readyState={GetJsonDouble(root, "readyState"):0}");
                    break;
                case "remote-video-ready":
                    AppLog.Write($"Screen share WebRTC remote video ready: width={GetJsonDouble(root, "width"):0}, height={GetJsonDouble(root, "height"):0}, readyState={GetJsonDouble(root, "readyState"):0}");
                    break;
                case "remote-decode-failed":
                    var decodeReason = GetJsonString(root, "reason");
                    AppLog.Write($"Screen share WebRTC remote decode failed: {decodeReason}");
                    RequestPeerScreenShareFallback(decodeReason);
                    break;
                case "focus-request":
                    FocusScreenShareFromPreview(ParseScreenShareFocusTarget(GetJsonString(root, "target")));
                    break;
                case "stream-context-request":
                    ShowActiveStreamAudioMenu(CallScreenShareStage);
                    break;
                case "local-ended":
                    if (_isScreenSharing)
                    {
                        StopScreenShare(sendSignal: true);
                    }
                    break;
                case "local-stopped":
                    _screenShareUsingNativeWebRtc = false;
                    _screenShareUsingEncodedWebRtc = false;
                    _screenShareEncodedChannelOpen = false;
                    StopScreenShareEncoderProcess();
                    _screenShareWebRtcActive = _peerScreenShareUsingWebRtc;
                    SetScreenShareWebRtcVisible(_screenShareWebRtcActive);
                    break;
                case "remote-stopped":
                    _peerScreenShareUsingWebRtc = false;
                    _screenShareWebRtcActive = _screenShareUsingNativeWebRtc || _screenShareUsingEncodedWebRtc;
                    SetScreenShareWebRtcVisible(_screenShareWebRtcActive);
                    break;
                case "stopped":
                    _screenShareWebRtcActive = false;
                    _peerScreenShareUsingWebRtc = false;
                    SetScreenShareWebRtcVisible(false);
                    break;
                case "state":
                    var state = GetJsonString(root, "value");
                    AppLog.Write($"Screen share WebRTC state: {state}");
                    if (state.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                        state.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                        state.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        FallbackScreenShareFromWebRtc(state);
                    }
                    break;
                case "error":
                    var message = GetJsonString(root, "message");
                    AppLog.Write($"Screen share WebRTC error: {message}");
                    if (IsScreenSharePickerCancellation(message))
                    {
                        NetworkStatusText.Text = "Screen share cancelled.";
                        StopScreenShare(sendSignal: false);
                        break;
                    }

                    FallbackScreenShareFromWebRtc(message);
                    break;
                case "stats":
                    LogScreenShareWebRtcStats(root);
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            AppLog.Write(ex, "Screen share WebRTC host message failed");
        }
    }

    private void SendScreenShareWebRtcSignal(string intent, string body)
    {
        if (_activeCallContact is null || string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        _ = SendScreenShareSignalAsync(intent, body);
    }

    private void HandleScreenShareWebRtcSignal(ChatPacket packet)
    {
        if (!ScreenSharePreferWebRtc || ScreenShareWebView.CoreWebView2 is null)
        {
            AppLog.Write($"Screen share WebRTC signal ignored because native capture mode is active: intent={packet.Intent}, from={packet.FromUserId}");
            return;
        }

        if (!IsActiveCallPeer(packet.FromUserId))
        {
            return;
        }

        var messageType = packet.Intent switch
        {
            CallScreenWebRtcOfferIntent => "remote-offer",
            CallScreenWebRtcAnswerIntent => "remote-answer",
            CallScreenWebRtcIceIntent => "remote-ice",
            _ => ""
        };
        if (messageType.Length == 0)
        {
            return;
        }

        if (packet.Intent == CallScreenWebRtcOfferIntent)
        {
            _peerScreenSharing = true;
            _isWatchingPeerScreen = true;
            _peerScreenShareUsingWebRtc = true;
            SetScreenShareWebRtcVisible(true);
            UpdateScreenShareStageVisibility();
        }
        else if (!_isScreenSharing && !_peerScreenSharing)
        {
            AppLog.Write($"Screen share WebRTC signal ignored without active share: intent={packet.Intent}, from={packet.FromUserId}");
            return;
        }

        PostScreenShareWebRtcMessageJson(WrapScreenShareWebRtcSignal(messageType, packet.Body));
    }

    private static string WrapScreenShareWebRtcSignal(string type, string signalJson)
    {
        using var document = JsonDocument.Parse(signalJson);
        return JsonSerializer.Serialize(new
        {
            type,
            signal = document.RootElement
        });
    }

    private void PostScreenShareWebRtcMessage(object message)
        => PostScreenShareWebRtcMessageJson(JsonSerializer.Serialize(message));

    private void PostScreenShareWebRtcMessageJson(string json)
    {
        try
        {
            ScreenShareWebView.CoreWebView2?.PostWebMessageAsJson(json);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            AppLog.Write(ex, "Screen share WebRTC post message failed");
        }
    }

    private void ConfigureScreenShareWebRtc()
    {
        if (!_screenShareWebRtcReady || ScreenShareWebView.CoreWebView2 is null)
        {
            return;
        }

        var iceServers = GetScreenShareWebRtcIceServers();
        PostScreenShareWebRtcMessage(new
        {
            type = "configure",
            iceServers,
            connectTimeoutMs = 9000,
            polite = IsPoliteScreenSharePeer()
        });

        var turnCount = iceServers.Count(server =>
            server.Urls.Any(url => url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase)));
        AppLog.Write($"Screen share WebRTC configured: iceServers={iceServers.Count}, turnServers={turnCount}");
    }

    private bool IsPoliteScreenSharePeer()
        => _profile is not null &&
           _activeCallContact is not null &&
           string.CompareOrdinal(_profile.UserId, _activeCallContact.UserId) > 0;

    private IReadOnlyList<RelayIceServer> GetScreenShareWebRtcIceServers()
    {
        if (_relayClient?.IceConfig?.IceServers is { Count: > 0 } servers)
        {
            return servers;
        }

        return new[]
        {
            new RelayIceServer(new[] { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" })
        };
    }

    private int GetScreenShareWebRtcMaxBitrate()
    {
        var voiceProtected = IsScreenShareVoiceProtectionActive();
        if (_screenShareResolution >= 1440)
        {
            var bitrate = _screenShareFrameRate >= 60 ? 30_000_000 : 18_000_000;
            return voiceProtected ? Math.Min(bitrate, ScreenShareVoiceProtectedWebRtcMaxBitrate) : bitrate;
        }

        if (_screenShareResolution >= 1080)
        {
            var bitrate = _screenShareFrameRate >= 60 ? 14_000_000 : 8_000_000;
            return voiceProtected ? Math.Min(bitrate, ScreenShareVoiceProtectedWebRtcMaxBitrate) : bitrate;
        }

        var lowBitrate = _screenShareFrameRate >= 60 ? 6_000_000 : 3_500_000;
        return voiceProtected ? Math.Min(lowBitrate, ScreenShareVoiceProtectedWebRtcMaxBitrate) : lowBitrate;
    }

    private bool IsScreenShareVoiceProtectionActive()
        => _activeCallState == "connected" && _selfInCall;

    private void ApplyScreenShareVoiceProtectionIfNeeded()
    {
        if (!IsScreenShareVoiceProtectionActive())
        {
            return;
        }

        var previousResolution = _screenShareResolution;
        var previousFrameRate = _screenShareFrameRate;
        _screenShareResolution = Math.Min(_screenShareResolution, ScreenShareVoiceProtectedMaxHeight);
        _screenShareFrameRate = Math.Min(
            ScreenShareVoiceProtectedMaxFrameRate,
            ClampScreenShareFrameRate(_screenShareResolution, _screenShareFrameRate));
        _screenShareAdaptiveHeight = Math.Min(_screenShareAdaptiveHeight, _screenShareResolution);

        if (previousResolution == _screenShareResolution && previousFrameRate == _screenShareFrameRate)
        {
            return;
        }

        _screenShareQualityPreset = "Custom";
        NetworkStatusText.Text = $"Voice protected screen share: {_screenShareResolution}p {_screenShareFrameRate} fps.";
        AppLog.Write($"Screen share voice protection applied: requested={previousResolution}p {previousFrameRate}fps, effective={_screenShareResolution}p {_screenShareFrameRate}fps");
    }

    private void LogScreenShareWebRtcStats(JsonElement root)
    {
        var direction = GetJsonString(root, "direction");
        var fps = GetJsonDouble(root, "fps");
        var bitrateKbps = GetJsonDouble(root, "bitrateKbps");
        var dropped = GetJsonDouble(root, "framesDropped");
        var packetsLost = GetJsonDouble(root, "packetsLost");
        var rttMs = GetJsonDouble(root, "rttMs");
        var packetLoss = GetJsonDouble(root, "packetLoss");
        var quality = GetJsonString(root, "qualityLimitationReason");
        AppLog.Write($"Screen share WebRTC stats: direction={direction}, fps={fps:0.#}, bitrateKbps={bitrateKbps:0}, dropped={dropped:0}, packetsLost={packetsLost:0}, rttMs={rttMs:0}, packetLoss={packetLoss:0.###}, quality={quality}");
    }

    private void SetScreenShareWebRtcVisible(bool visible)
    {
        var keepWebViewAlive =
            visible ||
            _screenShareUsingNativeWebRtc ||
            _screenShareUsingEncodedWebRtc ||
            _peerScreenShareUsingWebRtc;
        var showWebViewStage = keepWebViewAlive && _peerScreenShareUsingWebRtc && !_screenSharePickerSuppressesStage;

        ScreenShareWebView.Visibility = keepWebViewAlive
            ? (_screenSharePickerSuppressesStage ? Visibility.Hidden : Visibility.Visible)
            : Visibility.Collapsed;
        ScreenShareWebView.IsHitTestVisible = showWebViewStage;
        ScreenShareWebView.Width = showWebViewStage ? double.NaN : 1;
        ScreenShareWebView.Height = showWebViewStage ? double.NaN : 1;
        ScreenShareWebView.HorizontalAlignment = showWebViewStage
            ? System.Windows.HorizontalAlignment.Stretch
            : System.Windows.HorizontalAlignment.Left;
        ScreenShareWebView.VerticalAlignment = showWebViewStage
            ? System.Windows.VerticalAlignment.Stretch
            : System.Windows.VerticalAlignment.Top;
        CallScreenShareGrid.Visibility = showWebViewStage ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool IsScreenShareWebRtcPreferred()
        => ScreenSharePreferWebRtc && _screenShareWebRtcReady;

    private bool ShouldUseCompatibleScreenShareForSimultaneousStart()
        => _peerScreenSharing && _peerScreenShareUsingWebRtc;

    private void ApplyCompatibleScreenShareFallbackQuality()
    {
        _screenShareResolution = Math.Min(_screenShareResolution, ScreenShareFallbackMaxHeight);
        _screenShareFrameRate = Math.Min(
            ScreenShareFallbackMaxFrameRate,
            ClampScreenShareFrameRate(_screenShareResolution, _screenShareFrameRate));
        _screenShareAdaptiveHeight = Math.Min(_screenShareResolution, ScreenShareFallbackMaxHeight);
    }

    private void FallbackScreenShareFromWebRtc(string reason)
    {
        var source = _activeScreenShareSource;
        var stop = _screenShareStop;
        if (!_isScreenSharing || source is null || stop is null)
        {
            return;
        }

        _screenShareUsingNativeWebRtc = false;
        _screenShareUsingEncodedWebRtc = false;
        _screenShareEncodedChannelOpen = false;
        StopScreenShareEncoderProcess();
        if (Interlocked.Exchange(ref _screenShareJpegLoopStarted, 1) != 0)
        {
            return;
        }

        AppLog.Write($"Screen share WebRTC fallback to JPEG: {reason}");
        PostScreenShareWebRtcMessage(new { type = "stop-local" });
        _screenShareWebRtcActive = _peerScreenShareUsingWebRtc;
        SetScreenShareWebRtcVisible(_screenShareWebRtcActive);
        ApplyCompatibleScreenShareFallbackQuality();
        UpdateScreenSharePickerState();
        NetworkStatusText.Text = "WebRTC failed. Using compatible 720p screen share.";
        SendScreenShareStartSignal(source, useWebRtc: false);
        _ = Task.Run(() => RunScreenShareLoopAsync(source, stop.Token));
    }

    private static bool IsScreenSharePickerCancellation(string message)
        => message.Contains("NotAllowedError", StringComparison.OrdinalIgnoreCase) ||
           message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
           message.Contains("Permission dismissed", StringComparison.OrdinalIgnoreCase) ||
           message.Contains("Permission denied by system", StringComparison.OrdinalIgnoreCase);

    private static string GetJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";

    private static double GetJsonDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : 0;
    }

    private void RequestPeerScreenShareFallback(string reason)
    {
        if (!_peerScreenSharing || !_peerScreenShareUsingWebRtc)
        {
            return;
        }

        NetworkStatusText.Text = "Friend screen decode failed. Asking for compatible screen share.";
        _ = SendScreenShareSignalAsync(CallScreenWebRtcFallbackIntent, reason);
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _presenceTimer.Stop();
        _badgeRefreshTimer.Stop();
        _callRingtoneTimer.Stop();
        StopVoiceTest(restoreCallAudio: false);
        var activeCallContact = _activeCallContact;
        if (activeCallContact is not null)
        {
            await SendCallSignalAsync(activeCallContact, CallEndIntent);
        }

        await PublishOfflinePresenceAsync();
        StopScreenShare(sendSignal: false);
        StopAudioCall();
        await _stop.CancelAsync();

        if (_relayClient is not null)
        {
            await _relayClient.DisposeAsync();
        }

        _notifyIcon?.Dispose();
        _notifyIcon = null;
        _httpClient.Dispose();
        _badgeAuthority?.Dispose();
        _stop.Dispose();
    }

    private void OnNetworkStatusChanged(string status)
    {
        AppLog.Write($"Network status: {status}");
        Dispatcher.Invoke(() => NetworkStatusText.Text = status);
    }

    private async void OnRelayMessageReceived(ChatPacket packet)
    {
        try
        {
            await HandleIncomingMessageAsync(packet, "Received message from VPS", "VPS");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Incoming packet handling failed: messageId={packet.MessageId}, intent={packet.Intent}");
        }
    }

    private void OnRelayAudioReceived(RelayAudioPacket packet)
    {
        try
        {
            HandleCallAudioPacket(packet);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Incoming UDP audio handling failed: messageId={packet.MessageId}");
        }
    }

    private void OnRelayScreenFrameReceived(RelayScreenFramePacket packet)
    {
        try
        {
            HandleRelayScreenFramePacket(packet);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Incoming screen frame handling failed: messageId={packet.MessageId}");
        }
    }

    private void OnRelayPresenceReceived(RelayPresencePacket presence)
    {
        Dispatcher.Invoke(() =>
        {
            var contact = _contacts.FirstOrDefault(x => x.UserId == presence.UserId);
            if (contact is null)
            {
                return;
            }

            contact.DisplayName = presence.DisplayName;
            contact.Status = ParsePresenceStatus(presence.Status);
            contact.LastSeenUtc = presence.SentAtUtc;
            ApplyRelayPresenceAvatarToContact(presence, contact);
            ApplyVerifiedBadge(contact, presence.PublicKey, presence.BadgeCertificate,
                _badgeAuthority?.VerifyPresenceIdentity(presence) == true);
            _ = _history.SaveContactAsync(contact);
            if (_selectedContact is { IsGroup: true } selectedGroup &&
                GroupMembersPanel.Visibility == Visibility.Visible &&
                LoadGroupMembers(selectedGroup).Any(x => string.Equals(x.UserId, contact.UserId, StringComparison.Ordinal)))
            {
                RefreshGroupMembersPanel();
            }

            if (contact.Status == UserPresenceStatus.Offline &&
                _activeCallContact?.UserId == contact.UserId)
            {
                HideCallPanel();
                NetworkStatusText.Text = $"{contact.DisplayName} went offline";
            }
        });
    }

    private Task HandleIncomingMessageAsync(ChatPacket packet, string statusText, string source)
    {
        var badgeContact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId && !x.IsGroup);
        if (badgeContact is not null)
        {
            ApplyVerifiedBadge(badgeContact, packet.FromPublicKey, packet.BadgeCertificate,
                _badgeAuthority?.VerifyChatIdentity(packet) == true);
        }
        packet = ApplyControlBodyFallback(packet);
        if (packet.Intent == CallAudioIntent)
        {
            HandleCallAudioPacket(packet);
            return Task.CompletedTask;
        }

        if (TryHandleLegacyCallAudioPacket(packet))
        {
            return Task.CompletedTask;
        }

        if (packet.Intent == CallScreenFrameIntent)
        {
            QueuePeerScreenShareFrame(packet);
            return Task.CompletedTask;
        }

        AppLog.Write($"Incoming message: from={packet.FromUserId}, source={source}, messageId={packet.MessageId}, intent={packet.Intent}, bodyLength={packet.Body.Length}, avatarKind={packet.FromAvatarKind}, avatarBytes={packet.FromAvatarMediaBase64?.Length ?? 0}");
        Dispatcher.Invoke(() =>
        {
            if (IsFriendRequestPacket(packet))
            {
                UpsertFriendRequest(packet);
                _ = RequestProfileFromPacketAsync(packet);
                NetworkStatusText.Text = $"Friend request from {packet.FromDisplayName}";
                ShowIncomingNotificationIfNeeded(packet.FromDisplayName, packet);
                return;
            }

            if (packet.Intent == ProfileRequestIntent)
            {
                var requester = CreateRelayContactFromPacket(packet);
                var existingRequester = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId);
                if (existingRequester is not null)
                {
                    ApplyPacketProfileToContact(packet, existingRequester);
                    _ = _history.SaveContactAsync(existingRequester);
                }

                _ = SendProfileUpdateToContactAsync(requester);
                return;
            }

            if (packet.Intent is CallInviteIntent or CallAcceptIntent or CallDeclineIntent or CallEndIntent or CallLeaveIntent or CallJoinIntent or CallAudioStateIntent or CallPingIntent or CallPongIntent or CallScreenStartIntent or CallScreenFrameIntent or CallScreenStopIntent or CallScreenWebRtcOfferIntent or CallScreenWebRtcAnswerIntent or CallScreenWebRtcIceIntent or CallScreenWebRtcFallbackIntent)
            {
                HandleCallPacket(packet);
                return;
            }

            if (packet.Intent == "presence")
            {
                var presenceContact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId);
                if (presenceContact is not null)
                {
                    ApplyPacketProfileToContact(packet, presenceContact);
                    _ = _history.SaveContactAsync(presenceContact);
                    if (_selectedContact is { IsGroup: true } selectedGroup &&
                        GroupMembersPanel.Visibility == Visibility.Visible &&
                        LoadGroupMembers(selectedGroup).Any(x => string.Equals(x.UserId, presenceContact.UserId, StringComparison.Ordinal)))
                    {
                        RefreshGroupMembersPanel();
                    }
                }

                return;
            }

            if (packet.Intent == ProfileUpdateIntent)
            {
                var profileContact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId);
                if (profileContact is not null)
                {
                    ApplyPacketProfileToContact(packet, profileContact);
                    _ = _history.SaveContactAsync(profileContact);
                    RequestProfileIfAvatarMissing(packet, profileContact);
                }

                NetworkStatusText.Text = $"Updated profile for {packet.FromDisplayName}";
                return;
            }

            if (packet.Intent == ChatEditIntent)
            {
                HandleIncomingChatEdit(packet);
                return;
            }

            if (packet.Intent == ChatReactionIntent)
            {
                HandleIncomingChatReaction(packet);
                return;
            }

            if (packet.Intent == ChatDeleteIntent)
            {
                HandleIncomingChatDelete(packet);
                return;
            }

            if (packet.Intent == FriendRemoveIntent)
            {
                HandleIncomingFriendRemove(packet);
                return;
            }

            if (packet.Intent is GroupUpsertIntent or GroupDeleteIntent or GroupLeaveIntent or GroupKickIntent or GroupTransferOwnerIntent)
            {
                _ = HandleIncomingGroupActionAsync(packet);
                return;
            }

            if (packet.Intent == GroupMessageIntent)
            {
                HandleIncomingGroupMessage(packet, statusText);
                return;
            }

            if (IsFriendAcceptPacket(packet))
            {
                var accepted = CreateContactFromPacket(packet);
                AddOrUpdateContact(accepted);
                _ = _history.SaveContactAsync(accepted);
                _ = SendProfileUpdateToContactAsync(accepted);
                RequestProfileIfAvatarMissing(packet, accepted);
                NetworkStatusText.Text = $"{packet.FromDisplayName} accepted your friend request";
                ShowIncomingNotificationIfNeeded(packet.FromDisplayName, packet);
                return;
            }

            if (packet.Intent == ChatRichIntent)
            {
                HandleIncomingRichMessage(packet, statusText);
                return;
            }

            if (string.IsNullOrWhiteSpace(packet.Body))
            {
                var emptyContact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId);
                if (emptyContact is not null)
                {
                    ApplyPacketProfileToContact(packet, emptyContact);
                    _ = _history.SaveContactAsync(emptyContact);
                    RequestProfileIfAvatarMissing(packet, emptyContact);
                }

                AppLog.Write($"Empty incoming packet ignored: from={packet.FromUserId}, messageId={packet.MessageId}, intent missing");
                return;
            }

            var message = new MessageViewModel
            {
                MessageId = packet.MessageId,
                PeerUserId = packet.FromUserId,
                Body = packet.Body,
                Text = packet.Body,
                IsOutgoing = false,
                SentAtUtc = packet.SentAtUtc,
                SenderUserId = packet.FromUserId,
                SenderDisplayName = packet.FromDisplayName
            };
            PrepareMessageForUi(message);
            _ = _history.SaveAsync(message);

            var contact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId);
            if (contact is null)
            {
                UpsertFriendRequest(packet);
                _ = RequestProfileFromPacketAsync(packet);
                NetworkStatusText.Text = $"Message request from {packet.FromDisplayName}";
                ShowIncomingNotificationIfNeeded(packet.FromDisplayName, packet);
                return;
            }

            ApplyPacketProfileToContact(packet, contact);
            _ = _history.SaveContactAsync(contact);
            RequestProfileIfAvatarMissing(packet, contact);

            if (_selectedContact?.UserId == packet.FromUserId)
            {
                _messages.Add(message);
                ScrollMessagesToEnd();
            }

            NetworkStatusText.Text = $"{statusText}: {contact.DisplayName}";
            ShowIncomingNotificationIfNeeded(contact.DisplayName, packet);
        });

        return Task.CompletedTask;
    }

    private async void ContactsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContactsList.SelectedItem is not ContactViewModel contact)
        {
            return;
        }

        if (_selectedContact?.UserId == contact.UserId)
        {
            return;
        }

        await OpenContactAsync(contact);
    }

    private async Task OpenContactAsync(ContactViewModel contact)
    {
        ExitScreenShareFocusMode();
        _selectedContact = contact;
        ContactsList.SelectedItem = contact;
        AddFriendPanel.Visibility = Visibility.Collapsed;
        ChatTitle.Text = contact.DisplayName;
        ChatSubtitle.Text = contact.IsGroup
            ? $"{contact.GroupMemberCount} participants"
            : $"{contact.IpAddress} | {contact.ShortId}";
        ChatBadgeImage.Source = contact.BadgeImageSource;
        ChatBadgeImage.ToolTip = contact.BadgeToolTip;
        ChatBadgeImage.Visibility = !contact.IsGroup && contact.HasVerifiedBadge
            ? Visibility.Visible
            : Visibility.Collapsed;
        ComposerPanel.Visibility = Visibility.Visible;
        EmptyChatHint.Visibility = Visibility.Collapsed;
        StartCallButton.Visibility = Visibility.Visible;
        GroupMembersButton.Visibility = contact.IsGroup ? Visibility.Visible : Visibility.Collapsed;
        SetGroupMembersPanelVisible(false);
        RefreshGroupMembersPanel();
        _ = RefreshEmojiOpenButtonAsync();
        ClearImageDraft();

        _messages.Clear();
        foreach (var message in await _history.LoadConversationAsync(contact.UserId))
        {
            PrepareMessageForUi(message);
            _messages.Add(message);
        }

        ScrollMessagesToEnd();
        MessageInput.Focus();
    }

    private Task RefreshEmojiOpenButtonAsync()
    {
        var emojis = AllEmojis();
        if (emojis.Count == 0)
        {
            return Task.CompletedTask;
        }

        var emoji = emojis[Random.Shared.Next(emojis.Count)].Symbol;
        EmojiOpenButtonFallback.Text = emoji;
        EmojiOpenButtonFallback.Visibility = Visibility.Visible;
        EmojiOpenButtonImage.Source = null;
        EmojiOpenButtonImage.Visibility = Visibility.Collapsed;
        var twemojiUrl = TryBuildTwemojiPngUrl(emoji);
        if (twemojiUrl is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = twemojiUrl;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            EmojiOpenButtonImage.Source = image;
            EmojiOpenButtonImage.Visibility = Visibility.Visible;
            EmojiOpenButtonFallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            EmojiOpenButtonImage.Source = null;
            EmojiOpenButtonImage.Visibility = Visibility.Collapsed;
            EmojiOpenButtonFallback.Visibility = Visibility.Visible;
            AppLog.Write(ex, "Emoji button image failed");
        }

        return Task.CompletedTask;
    }

    private async void StartCallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedContact is null)
        {
            return;
        }

        if (_activeCallContact?.UserId == _selectedContact.UserId && CallPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        _activeCallContact = _selectedContact;
        _activeCallState = "outgoing";
        _selfInCall = true;
        _peerInCall = false;
        ShowCallPanel(_activeCallContact, "Calling...", showIncomingActions: false);
        await SendCallSignalAsync(_activeCallContact, CallInviteIntent);
    }

    private async void AcceptCallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeCallContact is null)
        {
            return;
        }

        StopCallRingtone();
        _activeCallState = "connected";
        _selfInCall = true;
        _peerInCall = true;
        ShowCallPanel(_activeCallContact, "Connected", showIncomingActions: false);
        await OpenContactAsync(_activeCallContact);
        await SendCallSignalAsync(_activeCallContact, CallAcceptIntent);
        StartAudioCall(_activeCallContact);
    }

    private async void DeclineCallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeCallContact is null)
        {
            HideCallPanel();
            return;
        }

        var contact = _activeCallContact;
        HideCallPanel();
        await SendCallSignalAsync(contact, CallDeclineIntent);
    }

    private async void EndCallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeCallContact is null)
        {
            HideCallPanel();
            return;
        }

        var contact = _activeCallContact;
        HideCallPanel();
        await SendCallEndBurstAsync(contact);
    }

    private async void JoinCallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeCallContact is null)
        {
            return;
        }

        var contact = _activeCallContact;
        _selfInCall = true;
        _peerInCall = true;
        _activeCallState = "connected";
        ShowCallPanel(contact, "Connected", showIncomingActions: false);
        await SendCallSignalAsync(contact, CallJoinIntent);
        StartAudioCall(contact);
    }

    private void MicMuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isVoiceTestActive)
        {
            StopVoiceTest(restoreCallAudio: true);
            return;
        }

        if (_isHeadphonesMuted)
        {
            return;
        }

        _isMicrophoneMuted = !_isMicrophoneMuted;
        UpdateCallAudioControlVisuals(animate: true);
        _ = SendCallAudioStateAsync();
    }

    private void HeadphonesMuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isVoiceTestActive)
        {
            StopVoiceTest(restoreCallAudio: true);
            return;
        }

        _isHeadphonesMuted = !_isHeadphonesMuted;
        if (_isHeadphonesMuted)
        {
            _isMicrophoneMuted = true;
        }

        UpdateCallAudioControlVisuals(animate: true);
        _ = SendCallAudioStateAsync();
    }

    private void ScreenShareButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isScreenSharing)
        {
            StopScreenShare(sendSignal: true);
            return;
        }

        if (_activeCallContact is null || _activeCallState != "connected" || !_selfInCall)
        {
            return;
        }

        ShowScreenSharePicker(webRtcSettingsOnly: false);
    }

    private void CallPeerParticipant_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_activeCallContact is null || _activeCallContact.IsGroup)
        {
            return;
        }

        ShowParticipantAudioMenu(CallPeerParticipant, _activeCallContact.UserId, _activeCallContact.DisplayName);
    }

    private void CallScreenShareStage_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowActiveStreamAudioMenu(CallScreenShareStage);
    }

    private void ShowParticipantAudioMenu(FrameworkElement placementTarget, string userId, string displayName)
    {
        var preference = _callAudioPreferences.Get(userId);
        var menu = CreateAudioControlMenu(placementTarget);
        var panel = new StackPanel { Width = 260 };
        panel.Children.Add(new TextBlock
        {
            Text = displayName,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 243, 245)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(2, 0, 2, 10)
        });
        panel.Children.Add(CreateVolumeControl(
            "User volume",
            preference.Volume,
            value => preference.Volume = value,
            () => _ = SaveCallAudioPreferencesAsync(),
            maximumMultiplier: 5));
        panel.Children.Add(CreateAudioCheckBox(
            "Mute",
            preference.IsMuted,
            value =>
            {
                preference.IsMuted = value;
                UpdateCallAudioControlVisuals(animate: true);
                _ = SaveCallAudioPreferencesAsync();
            }));
        panel.Children.Add(CreateAudioCheckBox(
            "Mute soundboard",
            preference.IsSoundboardMuted,
            value =>
            {
                preference.IsSoundboardMuted = value;
                _ = SaveCallAudioPreferencesAsync();
            }));
        menu.Items.Add(CreateAudioMenuContainer(new Border
        {
            Padding = new Thickness(8, 6, 8, 5),
            Child = panel
        }));
        menu.IsOpen = true;
    }

    private void ShowActiveStreamAudioMenu(FrameworkElement placementTarget)
    {
        if (_activeCallContact is null || _activeCallContact.IsGroup || !_peerScreenSharing)
        {
            return;
        }

        var preference = _callAudioPreferences.Get(_activeCallContact.UserId);
        var menu = CreateAudioControlMenu(placementTarget);
        var panel = new StackPanel { Width = 260 };
        panel.Children.Add(new TextBlock
        {
            Text = "Stream audio",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 243, 245)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(2, 0, 2, 10)
        });
        panel.Children.Add(CreateVolumeControl(
            "Stream volume",
            preference.StreamVolume,
            value =>
            {
                preference.StreamVolume = value;
                ApplyRemoteStreamAudioPreference(_activeCallContact.UserId);
            },
            () => _ = SaveCallAudioPreferencesAsync()));
        panel.Children.Add(CreateAudioCheckBox(
            "Mute stream",
            preference.IsStreamMuted,
            value =>
            {
                preference.IsStreamMuted = value;
                ApplyRemoteStreamAudioPreference(_activeCallContact.UserId);
                _ = SaveCallAudioPreferencesAsync();
            }));
        menu.Items.Add(CreateAudioMenuContainer(new Border
        {
            Padding = new Thickness(8, 6, 8, 5),
            Child = panel
        }));
        menu.IsOpen = true;
    }

    private static System.Windows.Controls.MenuItem CreateAudioMenuContainer(FrameworkElement content)
        => new()
        {
            Header = content,
            Height = double.NaN,
            MinHeight = 0,
            MinWidth = 284,
            Padding = new Thickness(0),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 243, 245)),
            StaysOpenOnClick = true,
            Focusable = false
        };

    private static ContextMenu CreateAudioControlMenu(FrameworkElement placementTarget)
        => new()
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.MousePoint,
            HasDropShadow = true,
            StaysOpen = false
        };

    private FrameworkElement CreateVolumeControl(
        string title,
        double initialValue,
        Action<double> valueChanged,
        Action save,
        double maximumMultiplier = 1)
    {
        maximumMultiplier = Math.Clamp(maximumMultiplier, 1, 5);
        var clampedValue = Math.Clamp(initialValue, 0, maximumMultiplier);
        var valueText = new TextBlock
        {
            Text = $"{Math.Round(clampedValue * 100):0}%",
            Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#b5bac1")),
            FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 243, 245))
        });
        Grid.SetColumn(valueText, 1);
        titleRow.Children.Add(valueText);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = maximumMultiplier * 100,
            Value = clampedValue * 100,
            Margin = new Thickness(0, 6, 0, 10),
            Style = (Style)FindResource("AudioSlider")
        };
        slider.ValueChanged += (_, args) =>
        {
            var normalized = Math.Clamp(args.NewValue / 100d, 0, maximumMultiplier);
            valueText.Text = $"{Math.Round(args.NewValue):0}%";
            valueChanged(normalized);
        };
        slider.PreviewMouseLeftButtonUp += (_, _) => save();
        slider.LostKeyboardFocus += (_, _) => save();

        var panel = new StackPanel();
        panel.Children.Add(titleRow);
        panel.Children.Add(slider);
        return panel;
    }

    private System.Windows.Controls.CheckBox CreateAudioCheckBox(string title, bool isChecked, Action<bool> changed)
    {
        var checkBox = new System.Windows.Controls.CheckBox
        {
            Content = title,
            IsChecked = isChecked,
            Margin = new Thickness(0, 2, 0, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 243, 245)),
            Style = (Style)FindResource("SmoothCheckBox")
        };
        checkBox.Checked += (_, _) => changed(true);
        checkBox.Unchecked += (_, _) => changed(false);
        return checkBox;
    }

    private async Task SaveCallAudioPreferencesAsync()
    {
        try
        {
            await CallAudioPreferencesStore.SaveAsync(_callAudioPreferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "Call audio preferences could not be saved");
        }
    }

    private void ApplyRemoteStreamAudioPreference(string userId)
    {
        var preference = _callAudioPreferences.Get(userId);
        PostScreenShareWebRtcMessage(new
        {
            type = "set-remote-audio",
            muted = preference.IsStreamMuted,
            volume = preference.StreamVolume
        });
    }

    private void ScreenSharePickerCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _screenShareWebRtcSettingsMode = false;
        ScreenSharePickerOverlay.Visibility = Visibility.Collapsed;
        ScreenShareSourceList.SelectedItem = null;
        SetScreenSharePickerStageSuppression(false);
        UpdateScreenSharePickerState();
    }

    private void ShowScreenSharePicker(bool webRtcSettingsOnly)
    {
        _screenShareWebRtcSettingsMode = webRtcSettingsOnly;
        SetScreenSharePickerStageSuppression(!webRtcSettingsOnly);
        ScreenSharePickerOverlay.Width = webRtcSettingsOnly ? 390 : 860;
        ScreenSharePickerSubtitleText.Text = webRtcSettingsOnly
            ? "Choose quality, then pick a source in the Windows screen picker."
            : "Choose a monitor or app window. FluxChat captures it natively without the browser sharing banner.";
        ScreenShareSourcePickerPanel.Visibility = webRtcSettingsOnly ? Visibility.Collapsed : Visibility.Visible;
        ScreenSharePickerSourceColumn.Width = webRtcSettingsOnly ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ScreenSharePickerSpacerColumn.Width = webRtcSettingsOnly ? new GridLength(0) : new GridLength(18);
        ScreenSharePickerSettingsColumn.Width = webRtcSettingsOnly ? new GridLength(1, GridUnitType.Star) : new GridLength(250);

        if (webRtcSettingsOnly)
        {
            ScreenShareSourceList.SelectedItem = null;
        }
        else
        {
            RefreshScreenShareSources();
        }

        ScreenSharePickerOverlay.Visibility = Visibility.Visible;
        UpdateScreenSharePickerState();
    }

    private void ScreenShareSourceList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateScreenSharePickerState();

    private void ScreenShareSourceList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScreenShareSourceList.SelectedItem is not ScreenShareSourceItem source)
        {
            return;
        }

        StartSelectedScreenShare(source);
    }

    private void ScreenShareStartSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_screenShareWebRtcSettingsMode && IsScreenShareWebRtcPreferred() && ScreenShareWebView.CoreWebView2 is not null)
        {
            StartSelectedScreenShare(CreateDefaultWebRtcScreenShareSource());
            return;
        }

        if (ScreenShareSourceList.SelectedItem is ScreenShareSourceItem source)
        {
            StartSelectedScreenShare(source);
        }
    }

    private void StartSelectedScreenShare(ScreenShareSourceItem source)
    {
        _screenShareWebRtcSettingsMode = false;
        ScreenShareSourceList.SelectedItem = null;
        ScreenSharePickerOverlay.Visibility = Visibility.Collapsed;
        SetScreenSharePickerStageSuppression(false);
        StartScreenShare(source);
        UpdateScreenSharePickerState();
    }

    private void SetScreenSharePickerStageSuppression(bool suppress)
    {
        if (_screenSharePickerSuppressesStage == suppress)
        {
            return;
        }

        _screenSharePickerSuppressesStage = suppress;
        SetScreenShareWebRtcVisible(_screenShareWebRtcActive);
        UpdateScreenShareStageVisibility();
        CallScreenShareStage.UpdateLayout();
        ScreenShareWebView.UpdateLayout();
    }

    private void ScreenShareSourceTab_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string filter })
        {
            _screenShareSourceFilter = filter;
            ApplyScreenShareSourceFilter();
            UpdateScreenSharePickerState();
        }
    }

    private void ScreenShareQualityOption_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string preset })
        {
            return;
        }

        _screenShareQualityPreset = preset;
        switch (preset)
        {
            case "Smooth":
                _screenShareResolution = 720;
                _screenShareFrameRate = 60;
                break;
            case "Balanced":
                _screenShareResolution = 1080;
                _screenShareFrameRate = 30;
                break;
            case "Sharp":
                _screenShareResolution = 1440;
                _screenShareFrameRate = 30;
                break;
        }

        _screenShareFrameRate = ClampSelectedScreenShareFrameRate(_screenShareResolution, _screenShareFrameRate);
        UpdateScreenSharePickerState();
    }

    private void ScreenShareResolutionOption_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value } && int.TryParse(value, out var resolution))
        {
            _screenShareResolution = resolution;
            _screenShareFrameRate = ClampSelectedScreenShareFrameRate(_screenShareResolution, _screenShareFrameRate);
            UpdateScreenShareQualityPresetFromManualOptions();
            UpdateScreenSharePickerState();
        }
    }

    private void ScreenShareFpsOption_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value } && int.TryParse(value, out var frameRate))
        {
            _screenShareFrameRate = ClampSelectedScreenShareFrameRate(_screenShareResolution, frameRate);
            UpdateScreenShareQualityPresetFromManualOptions();
            UpdateScreenSharePickerState();
        }
    }

    private int ClampSelectedScreenShareFrameRate(int resolution, int frameRate)
        => IsScreenShareWebRtcPreferred()
            ? Math.Clamp(frameRate, 15, 60)
            : ClampScreenShareFrameRate(resolution, frameRate);

    private static int ClampScreenShareFrameRate(int resolution, int frameRate)
        => resolution >= 1440
            ? Math.Min(frameRate, ScreenShareHighResolutionMaxFrameRate)
            : Math.Clamp(frameRate, 15, 60);

    private void ScreenShareStreamAudioCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        _screenShareMuteAudio = ScreenShareStreamAudioCheck.IsChecked != true;
        UpdateScreenSharePickerState();
    }

    private void ApplyScreenShareSourceFilter()
    {
        var selected = ScreenShareSourceList.SelectedItem as ScreenShareSourceItem;
        _visibleScreenShareSources.Clear();
        foreach (var source in _screenShareSources)
        {
            var include = _screenShareSourceFilter switch
            {
                "Screens" => source.IsScreen,
                "Apps" => !source.IsScreen,
                _ => true
            };

            if (include)
            {
                _visibleScreenShareSources.Add(source);
            }
        }

        if (selected is not null && _visibleScreenShareSources.Contains(selected))
        {
            ScreenShareSourceList.SelectedItem = selected;
        }
    }

    private void UpdateScreenShareQualityPresetFromManualOptions()
    {
        _screenShareQualityPreset = (_screenShareResolution, _screenShareFrameRate) switch
        {
            (720, 60) => "Smooth",
            (1080, 30) => "Balanced",
            (1440, 30) => "Sharp",
            _ => "Custom"
        };
    }

    private void UpdateScreenSharePickerState()
    {
        var selected = ScreenShareSourceList.SelectedItem as ScreenShareSourceItem;
        var webRtcSettingsOnly = _screenShareWebRtcSettingsMode && IsScreenShareWebRtcPreferred();
        ScreenShareStartSelectedButton.IsEnabled = webRtcSettingsOnly || selected is not null;
        ScreenShareStartSelectedButton.Content = webRtcSettingsOnly ? "Choose source" : "Share";
        ScreenShareSelectedSourceText.Text = webRtcSettingsOnly
            ? $"WebRTC: {_screenShareResolution}p {_screenShareFrameRate} fps. Source is selected next."
            : selected is null
                ? "No source selected"
                : $"Native capture: {selected.Title}";
        ScreenShareStreamAudioCheck.IsChecked = !_screenShareMuteAudio;
        ScreenShareQualityWarningText.Text = IsScreenShareWebRtcPreferred()
            ? "1440p60 is available in WebRTC mode, but it needs a strong GPU and network. Voice stays on a separate audio path."
            : "Native capture avoids the browser sharing banner. 1440p is capped at 30 fps to keep voice stable.";
        ScreenShareQualityWarningText.Visibility = _screenShareResolution >= 1440 || _screenShareFrameRate >= 60
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScreenShareCustomQualityText.Visibility = _screenShareQualityPreset == "Custom"
            ? Visibility.Visible
            : Visibility.Collapsed;

        SetScreenSharePickerButtonState(ScreenShareAllTabButton, _screenShareSourceFilter == "All");
        SetScreenSharePickerButtonState(ScreenShareScreensTabButton, _screenShareSourceFilter == "Screens");
        SetScreenSharePickerButtonState(ScreenShareAppsTabButton, _screenShareSourceFilter == "Apps");

        SetScreenSharePickerButtonState(ScreenShareSmoothQualityButton, _screenShareQualityPreset == "Smooth");
        SetScreenSharePickerButtonState(ScreenShareBalancedQualityButton, _screenShareQualityPreset == "Balanced");
        SetScreenSharePickerButtonState(ScreenShareSharpQualityButton, _screenShareQualityPreset == "Sharp");

        SetScreenSharePickerButtonState(ScreenShare720Button, _screenShareResolution == 720);
        SetScreenSharePickerButtonState(ScreenShare1080Button, _screenShareResolution == 1080);
        SetScreenSharePickerButtonState(ScreenShare1440Button, _screenShareResolution == 1440);

        SetScreenSharePickerButtonState(ScreenShare15FpsButton, _screenShareFrameRate == 15);
        SetScreenSharePickerButtonState(ScreenShare30FpsButton, _screenShareFrameRate == 30);
        SetScreenSharePickerButtonState(ScreenShare60FpsButton, _screenShareFrameRate == 60);
        ScreenShare60FpsButton.IsEnabled = _screenShareResolution < 1440 || IsScreenShareWebRtcPreferred();
    }

    private static void SetScreenSharePickerButtonState(System.Windows.Controls.Button button, bool active)
    {
        button.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#5865f2" : "#3a3c43"));
        button.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#ffffff" : "#f2f3f5"));
    }

    private void CallScreenShareJoinOverlay_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_peerScreenSharing)
        {
            return;
        }

        _isWatchingPeerScreen = true;
        UpdateScreenShareStageVisibility();
    }

    private void CallScreenShareTile_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || (!_isScreenSharing && !_peerScreenSharing))
        {
            return;
        }

        e.Handled = true;
        var target = ReferenceEquals(sender, CallSelfScreenTile)
            ? ScreenShareFocusTarget.Local
            : ReferenceEquals(sender, CallPeerScreenTile)
                ? ScreenShareFocusTarget.Peer
                : ScreenShareFocusTarget.Auto;
        FocusScreenShareFromPreview(target);
    }

    private void FocusScreenShareFromPreview(ScreenShareFocusTarget target)
    {
        if (!_isScreenSharing && !_peerScreenSharing)
        {
            return;
        }

        SetScreenShareFocusTarget(target);
        if (!_isScreenShareFocusMode)
        {
            EnterScreenShareFocusMode();
        }

        SetScreenShareFullscreenMode(true);
        UpdateScreenShareStageVisibility();
    }

    private static ScreenShareFocusTarget ParseScreenShareFocusTarget(string target)
        => target.Equals("local", StringComparison.OrdinalIgnoreCase)
            ? ScreenShareFocusTarget.Local
            : target.Equals("remote", StringComparison.OrdinalIgnoreCase) ||
              target.Equals("peer", StringComparison.OrdinalIgnoreCase)
                ? ScreenShareFocusTarget.Peer
                : ScreenShareFocusTarget.Auto;

    private ScreenShareFocusTarget ResolveScreenShareFocusTarget(ScreenShareFocusTarget target)
        => target switch
        {
            ScreenShareFocusTarget.Local when _isScreenSharing => ScreenShareFocusTarget.Local,
            ScreenShareFocusTarget.Peer when _peerScreenSharing => ScreenShareFocusTarget.Peer,
            _ => ScreenShareFocusTarget.Auto
        };

    private void SetScreenShareFocusTarget(ScreenShareFocusTarget target)
    {
        var resolvedTarget = ResolveScreenShareFocusTarget(target);
        if (_screenShareFocusTarget == resolvedTarget)
        {
            PostScreenShareFocusTarget();
            return;
        }

        _screenShareFocusTarget = resolvedTarget;
        PostScreenShareFocusTarget();
    }

    private void NormalizeScreenShareFocusTarget()
    {
        var resolvedTarget = ResolveScreenShareFocusTarget(_screenShareFocusTarget);
        if (_screenShareFocusTarget == resolvedTarget)
        {
            return;
        }

        _screenShareFocusTarget = resolvedTarget;
        PostScreenShareFocusTarget();
    }

    private void PostScreenShareFocusTarget()
    {
        var target = _screenShareFocusTarget switch
        {
            ScreenShareFocusTarget.Local => "local",
            ScreenShareFocusTarget.Peer => "remote",
            _ => "auto"
        };
        PostScreenShareWebRtcMessage(new { type = "focus-target", target });
    }

    private void CallScreenShareFocusExitButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExitScreenShareFocusMode();
    }

    private void CallScreenShareFullscreenButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_isScreenShareFocusMode)
        {
            EnterScreenShareFocusMode();
        }

        SetScreenShareFullscreenMode(!_isScreenShareFullscreenMode);
        UpdateScreenShareStageVisibility();
    }

    private void CallPanel_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isScreenShareFocusMode)
        {
            UpdateScreenShareStageVisibility();
        }
    }

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void AddContactButton_OnClick(object sender, RoutedEventArgs e)
        => await AddVpsContactAsync();

    private async Task AddVpsContactAsync()
    {
        var activeInput = AddFriendPanel.Visibility == Visibility.Visible ? AddFriendInput : ManualIpInput;
        var userId = activeInput.Text.Trim();
        AppLog.Write($"Add VPS contact requested: userId={userId}");

        if (string.IsNullOrWhiteSpace(userId))
        {
            NetworkStatusText.Text = "Enter friend UserId";
            return;
        }

        if (TryParseRemoteAddress(userId, out var remoteUserId, out var remoteServer))
        {
            await AddRelayContactAsync(remoteUserId, remoteServer);
            return;
        }

        await AddRelayContactAsync(userId, NormalizeRelayServer(_settings.RelayServer));
    }

    private async Task AddRelayContactAsync(string userId, string relayServer)
    {
        relayServer = NormalizeRelayServer(relayServer);
        var contact = new ContactViewModel
        {
            UserId = userId,
            DisplayName = userId.Length <= 12 ? userId : userId[..12],
            IpAddress = $"{RelayContactPrefix}{relayServer}",
            MessagePort = FluxChatPorts.Relay,
            Status = UserPresenceStatus.Online,
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        ManualIpInput.Clear();
        AddFriendInput.Clear();
        AddFriendPanel.Visibility = Visibility.Visible;
        EmptyChatHint.Visibility = Visibility.Visible;
        NetworkStatusText.Text = $"Friend request sent to {contact.DisplayName}";

        if (_profile is not null)
        {
            var request = CreateProfilePacket(contact.UserId, "Friend request", FriendRequestIntent, relayServer);
            await SendOverRelayAsync(request, contact);
        }
    }

    private async void AcceptFriendRequestButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FriendRequestViewModel request } || _profile is null)
        {
            return;
        }

        var contact = new ContactViewModel
        {
            UserId = request.UserId,
            DisplayName = request.DisplayName,
            IpAddress = $"{RelayContactPrefix}{request.RelayServer}",
            MessagePort = FluxChatPorts.Relay,
            Status = UserPresenceStatus.Online,
            LastSeenUtc = DateTimeOffset.UtcNow,
            AvatarKind = request.AvatarKind,
            AvatarPath = request.AvatarPath,
            AvatarScale = request.AvatarScale,
            AvatarOffsetX = request.AvatarOffsetX,
            AvatarOffsetY = request.AvatarOffsetY,
            AvatarVideoStartSeconds = request.AvatarVideoStartSeconds,
            AvatarVideoDurationSeconds = request.AvatarVideoDurationSeconds
        };

        AddOrUpdateContact(contact);
        await _history.SaveContactAsync(contact);
        _friendRequests.Remove(request);

        var packet = CreateProfilePacket(contact.UserId, "Friend request accepted", FriendAcceptIntent);
        await SendOverRelayAsync(packet, contact);
        await SendProfileUpdateToContactAsync(contact);
        NetworkStatusText.Text = $"Added {contact.DisplayName}";
    }

    private void DeclineFriendRequestButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FriendRequestViewModel request })
        {
            return;
        }

        _friendRequests.Remove(request);
        DeleteAvatarFileIfOwned(request.AvatarPath);
        NetworkStatusText.Text = $"Declined friend request from {request.DisplayName}";
    }

    private void StatusSelector_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
    }

    private void ProfilePanel_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ProfileFlyout.Visibility = ProfileFlyout.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ProfileStatusButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !Enum.TryParse<UserPresenceStatus>(tag, out var status))
        {
            return;
        }

        _selectedStatus = status;
        UpdateProfileStatusVisuals();
        ProfileFlyout.Visibility = Visibility.Collapsed;
        NetworkStatusText.Text = $"Your status is {GetCurrentStatusText()}";
        _ = PublishPresenceAsync(force: true);
    }

    private async void CopyUserIdButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_profile is null)
        {
            return;
        }

        if (await TrySetClipboardTextAsync(_profile.UserId))
        {
            ProfileFlyout.Visibility = Visibility.Collapsed;
            NetworkStatusText.Text = "User ID copied";
            return;
        }

        NetworkStatusText.Text = "Clipboard is busy. Try again.";
    }

    private async void ConnectServerButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveRelaySettingsAndConnectAsync(SettingsServerAddressInput.Text, SettingsServerAccessKeyInput.Text);
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void AddFriendPanelButton_OnClick(object sender, RoutedEventArgs e)
    {
        _selectedContact = null;
        ContactsList.SelectedItem = null;
        _messages.Clear();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        ProfileFlyout.Visibility = Visibility.Collapsed;
        ComposerPanel.Visibility = Visibility.Collapsed;
        EmptyChatHint.Visibility = Visibility.Collapsed;
        AddFriendPanel.Visibility = Visibility.Visible;
        StartCallButton.Visibility = Visibility.Collapsed;
        GroupMembersButton.Visibility = Visibility.Collapsed;
        SetGroupMembersPanelVisible(false);
        ChatTitle.Text = "Add Friend";
        ChatSubtitle.Text = "Add a friend by User ID";
        AddFriendInput.Focus();
    }

    private void CreateGroupButton_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        ProfileFlyout.Visibility = Visibility.Collapsed;
        AddFriendPanel.Visibility = Visibility.Collapsed;
        ScreenSharePickerOverlay.Visibility = Visibility.Collapsed;
        SetGroupMembersPanelVisible(false);
        SetScreenSharePickerStageSuppression(false);
        _draftGroupContact = null;
        _selectedGroupMemberIds.Clear();
        GroupSearchInput.Clear();
        RefreshGroupCandidates();
        GroupCreateStatusText.Text = "Можно добавить до 10 друзей.";
        CreateGroupOverlay.Visibility = Visibility.Visible;
        GroupSearchInput.Focus();
    }

    private void CreateGroupCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        CreateGroupOverlay.Visibility = Visibility.Collapsed;
        _draftGroupContact = null;
        _selectedGroupMemberIds.Clear();
        _groupCandidates.Clear();
    }

    private void GroupSearchInput_OnTextChanged(object sender, TextChangedEventArgs e)
        => RefreshGroupCandidates();

    private async void AddGroupMemberButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GroupCandidateViewModel candidate } ||
            candidate.IsAdded)
        {
            return;
        }

        if (_selectedGroupMemberIds.Count >= 10)
        {
            GroupCreateStatusText.Text = "В группе может быть максимум 10 друзей.";
            return;
        }

        _selectedGroupMemberIds.Add(candidate.UserId);
        candidate.IsAdded = true;

        var group = _draftGroupContact ?? CreateLocalGroupContact();
        _draftGroupContact = group;
        var members = LoadGroupMembers(group);
        if (members.All(x => _profile is null || !string.Equals(x.UserId, _profile.UserId, StringComparison.Ordinal)))
        {
            members.Insert(0, CreateSelfGroupMember());
        }

        if (members.All(x => !string.Equals(x.UserId, candidate.UserId, StringComparison.Ordinal)))
        {
            members.Add(CreateGroupMemberPayload(candidate.Contact));
        }

        SaveGroupMembers(group, members);
        group.DisplayName = BuildGroupDisplayName(group.GroupMemberIdsList);
        group.Status = UserPresenceStatus.Online;
        group.LastSeenUtc = DateTimeOffset.UtcNow;
        group.GroupVersion++;

        AddOrUpdateContact(group);
        await _history.SaveContactAsync(group);
        await BroadcastGroupSnapshotAsync(group);
        ContactsList.SelectedItem = group;
        await OpenContactAsync(group);

        GroupCreateStatusText.Text = $"Добавлено: {_selectedGroupMemberIds.Count}/10.";
        RefreshGroupCandidates();
    }

    private ContactViewModel CreateLocalGroupContact()
        => new()
        {
            UserId = $"group:{Guid.NewGuid():N}",
            DisplayName = "Group",
            IpAddress = "GROUP",
            MessagePort = 0,
            Status = UserPresenceStatus.Online,
            LastSeenUtc = DateTimeOffset.UtcNow,
            IsGroup = true,
            AvatarKind = "color",
            GroupOwnerUserId = _profile?.UserId ?? "",
            GroupVersion = 1
        };

    private string BuildGroupDisplayName(IReadOnlyList<string> memberIds)
    {
        var names = memberIds
            .Where(id => _profile is null || !string.Equals(id, _profile.UserId, StringComparison.Ordinal))
            .Select(id => _contacts.FirstOrDefault(x => x.UserId == id)?.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToArray();

        return names.Length == 0
            ? "Group"
            : string.Join(", ", names);
    }

    private void EnsureGroupMetadata(ContactViewModel group)
    {
        if (_profile is null || !group.IsGroup)
        {
            return;
        }

        group.CurrentUserId = _profile.UserId;
        if (string.IsNullOrWhiteSpace(group.GroupOwnerUserId))
        {
            group.GroupOwnerUserId = _profile.UserId;
        }

        var members = LoadGroupMembers(group);
        if (members.Count == 0)
        {
            members.Add(CreateSelfGroupMember());
            foreach (var memberId in group.GroupMemberIdsList)
            {
                if (members.Any(x => string.Equals(x.UserId, memberId, StringComparison.Ordinal)))
                {
                    continue;
                }

                var contact = _contacts.FirstOrDefault(x => x.UserId == memberId && !x.IsGroup);
                members.Add(contact is null
                    ? new GroupMemberPayload(memberId, memberId.Length <= 12 ? memberId : memberId[..12], NormalizeRelayServer(_settings.RelayServer), "color", "", "", 1, 0, 0, 0, 10, DateTimeOffset.UtcNow)
                    : CreateGroupMemberPayload(contact));
            }
        }

        if (members.All(x => !string.Equals(x.UserId, _profile.UserId, StringComparison.Ordinal)))
        {
            members.Insert(0, CreateSelfGroupMember());
        }

        if (members.All(x => !string.Equals(x.UserId, group.GroupOwnerUserId, StringComparison.Ordinal)))
        {
            group.GroupOwnerUserId = _profile.UserId;
        }

        SaveGroupMembers(group, members);
        if (group.GroupVersion <= 0)
        {
            group.GroupVersion = 1;
        }
    }

    private List<GroupMemberPayload> LoadGroupMembers(ContactViewModel group)
    {
        if (!string.IsNullOrWhiteSpace(group.GroupMembersJson))
        {
            try
            {
                return JsonSerializer.Deserialize<List<GroupMemberPayload>>(group.GroupMembersJson)?
                           .Where(x => !string.IsNullOrWhiteSpace(x.UserId))
                           .GroupBy(x => x.UserId, StringComparer.Ordinal)
                           .Select(x => x.First())
                           .ToList()
                       ?? [];
            }
            catch (JsonException ex)
            {
                AppLog.Write(ex, $"Group members parse failed: group={group.UserId}");
            }
        }

        return [];
    }

    private void SaveGroupMembers(ContactViewModel group, IReadOnlyList<GroupMemberPayload> members)
    {
        var uniqueMembers = members
            .Where(x => !string.IsNullOrWhiteSpace(x.UserId))
            .GroupBy(x => x.UserId, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
        group.GroupMembersJson = JsonSerializer.Serialize(uniqueMembers);
        group.GroupMemberIds = string.Join('|', uniqueMembers.Select(x => x.UserId));
    }

    private GroupMemberPayload CreateSelfGroupMember()
    {
        if (_profile is null)
        {
            throw new InvalidOperationException("Profile is not loaded.");
        }

        return new GroupMemberPayload(
            _profile.UserId,
            _profile.DisplayName,
            NormalizeRelayServer(_settings.RelayServer),
            _profile.AvatarKind,
            _profile.AvatarPath,
            "",
            _profile.AvatarScale,
            _profile.AvatarOffsetX,
            _profile.AvatarOffsetY,
            _profile.AvatarVideoStartSeconds,
            _profile.AvatarVideoDurationSeconds,
            DateTimeOffset.UtcNow);
    }

    private GroupMemberPayload CreateGroupMemberPayload(ContactViewModel contact)
        => new(
            contact.UserId,
            contact.DisplayName,
            GetRelayServer(contact),
            contact.AvatarKind,
            contact.AvatarPath,
            "",
            contact.AvatarScale,
            contact.AvatarOffsetX,
            contact.AvatarOffsetY,
            contact.AvatarVideoStartSeconds,
            contact.AvatarVideoDurationSeconds,
            DateTimeOffset.UtcNow);

    private ContactViewModel CreateContactFromGroupMember(GroupMemberPayload member)
        => new()
        {
            UserId = member.UserId,
            DisplayName = string.IsNullOrWhiteSpace(member.DisplayName) ? member.UserId : member.DisplayName,
            IpAddress = $"{RelayContactPrefix}{NormalizeRelayServer(member.RelayServer)}",
            MessagePort = FluxChatPorts.Relay,
            Status = UserPresenceStatus.Online,
            LastSeenUtc = DateTimeOffset.UtcNow,
            AvatarKind = member.AvatarKind,
            AvatarPath = member.AvatarPath,
            AvatarScale = member.AvatarScale,
            AvatarOffsetX = member.AvatarOffsetX,
            AvatarOffsetY = member.AvatarOffsetY,
            AvatarVideoStartSeconds = member.AvatarVideoStartSeconds,
            AvatarVideoDurationSeconds = member.AvatarVideoDurationSeconds
        };

    private GroupSnapshotPayload CreateGroupSnapshot(ContactViewModel group)
    {
        EnsureGroupMetadata(group);
        return new GroupSnapshotPayload(
            group.UserId,
            group.DisplayName,
            group.GroupOwnerUserId,
            group.GroupVersion,
            group.GroupIsDeleted,
            group.AvatarKind,
            EncodeFileToBase64(group.AvatarPath),
            Path.GetExtension(group.AvatarPath),
            group.AvatarScale,
            group.AvatarOffsetX,
            group.AvatarOffsetY,
            group.AvatarVideoStartSeconds,
            group.AvatarVideoDurationSeconds,
            LoadGroupMembers(group));
    }

    private static string EncodeFileToBase64(string path)
        => string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? ""
            : Convert.ToBase64String(File.ReadAllBytes(path));

    private async Task BroadcastGroupSnapshotAsync(ContactViewModel group)
    {
        if (_profile is null || !group.IsGroup)
        {
            return;
        }

        var snapshot = CreateGroupSnapshot(group);
        var body = JsonSerializer.Serialize(snapshot);
        foreach (var member in snapshot.Members.Where(x => !string.Equals(x.UserId, _profile.UserId, StringComparison.Ordinal)))
        {
            try
            {
                var contact = _contacts.FirstOrDefault(x => x.UserId == member.UserId && !x.IsGroup)
                              ?? CreateContactFromGroupMember(member);
                var packet = CreateProfilePacket(member.UserId, body, GroupUpsertIntent, member.RelayServer);
                await SendOverRelayAsync(packet, contact, log: false);
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested)
            {
                AppLog.Write(ex, $"Group snapshot send failed: group={group.UserId}, to={member.UserId}");
            }
        }
    }

    private async Task SendGroupActionAsync(string intent, GroupActionPayload action, GroupMemberPayload target)
    {
        try
        {
            var contact = _contacts.FirstOrDefault(x => x.UserId == target.UserId && !x.IsGroup)
                          ?? CreateContactFromGroupMember(target);
            var packet = CreateProfilePacket(target.UserId, JsonSerializer.Serialize(action), intent, target.RelayServer);
            await SendOverRelayAsync(packet, contact, log: false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Group action send failed: intent={intent}, group={action.GroupId}, to={target.UserId}");
        }
    }

    private async Task HandleIncomingGroupActionAsync(ChatPacket packet)
    {
        try
        {
            if (packet.Intent == GroupUpsertIntent)
            {
                var snapshot = JsonSerializer.Deserialize<GroupSnapshotPayload>(packet.Body);
                if (snapshot is not null)
                {
                    await ApplyGroupSnapshotAsync(snapshot, packet);
                }

                return;
            }

            var action = JsonSerializer.Deserialize<GroupActionPayload>(packet.Body);
            if (action is null || string.IsNullOrWhiteSpace(action.GroupId))
            {
                return;
            }

            var group = _contacts.FirstOrDefault(x => x.UserId == action.GroupId && x.IsGroup);
            if (packet.Intent == GroupDeleteIntent)
            {
                if (group is not null && action.GroupVersion > group.GroupVersion)
                {
                    group.GroupVersion = action.GroupVersion;
                    group.GroupIsDeleted = true;
                    await _history.SaveContactAsync(group);
                    RemoveContactFromUi(group);
                }

                return;
            }

            if (packet.Intent == GroupKickIntent)
            {
                if (_profile is not null &&
                    string.Equals(action.TargetUserId, _profile.UserId, StringComparison.Ordinal) &&
                    group is not null &&
                    action.GroupVersion > group.GroupVersion)
                {
                    group.GroupVersion = action.GroupVersion;
                    group.GroupIsDeleted = true;
                    await _history.SaveContactAsync(group);
                    RemoveContactFromUi(group);
                }

                return;
            }

            if (packet.Intent == GroupLeaveIntent &&
                group is not null &&
                group.IsCurrentUserGroupOwner &&
                action.GroupVersion > group.GroupVersion)
            {
                var members = LoadGroupMembers(group)
                    .Where(x => !string.Equals(x.UserId, action.ActorUserId, StringComparison.Ordinal))
                    .ToList();
                SaveGroupMembers(group, members);
                group.GroupVersion = action.GroupVersion;
                await _history.SaveContactAsync(group);
                await BroadcastGroupSnapshotAsync(group);
                RefreshGroupMembersPanel();
                return;
            }

            if (packet.Intent == GroupTransferOwnerIntent &&
                group is not null &&
                action.GroupVersion > group.GroupVersion &&
                !string.IsNullOrWhiteSpace(action.TargetUserId))
            {
                group.GroupOwnerUserId = action.TargetUserId;
                group.GroupVersion = action.GroupVersion;
                await _history.SaveContactAsync(group);
                RefreshGroupMembersPanel();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            AppLog.Write(ex, $"Incoming group action failed: intent={packet.Intent}, from={packet.FromUserId}");
        }
    }

    private async Task ApplyGroupSnapshotAsync(GroupSnapshotPayload snapshot, ChatPacket packet)
    {
        if (_profile is null || snapshot.GroupVersion <= 0)
        {
            return;
        }

        var existing = _contacts.FirstOrDefault(x => x.UserId == snapshot.GroupId && x.IsGroup);
        if (existing is not null && snapshot.GroupVersion <= existing.GroupVersion)
        {
            return;
        }

        var group = existing ?? new ContactViewModel
        {
            UserId = snapshot.GroupId,
            DisplayName = snapshot.DisplayName,
            IpAddress = "GROUP",
            MessagePort = 0,
            Status = UserPresenceStatus.Online,
            LastSeenUtc = DateTimeOffset.UtcNow,
            IsGroup = true
        };

        group.DisplayName = snapshot.DisplayName;
        group.GroupOwnerUserId = snapshot.OwnerUserId;
        group.GroupVersion = snapshot.GroupVersion;
        group.GroupIsDeleted = snapshot.IsDeleted;
        group.AvatarKind = string.IsNullOrWhiteSpace(snapshot.AvatarKind) ? "color" : snapshot.AvatarKind;
        group.AvatarScale = snapshot.AvatarScale;
        group.AvatarOffsetX = snapshot.AvatarOffsetX;
        group.AvatarOffsetY = snapshot.AvatarOffsetY;
        group.AvatarVideoStartSeconds = snapshot.AvatarVideoStartSeconds;
        group.AvatarVideoDurationSeconds = snapshot.AvatarVideoDurationSeconds;
        if (!string.IsNullOrWhiteSpace(snapshot.AvatarMediaBase64))
        {
            group.AvatarPath = SaveContactAvatar(snapshot.GroupId, snapshot.AvatarExtension, snapshot.AvatarMediaBase64);
        }

        SaveGroupMembers(group, snapshot.Members);
        if (group.GroupIsDeleted)
        {
            await _history.SaveContactAsync(group);
            if (existing is not null)
            {
                RemoveContactFromUi(existing);
            }

            return;
        }

        AddOrUpdateContact(group);
        await _history.SaveContactAsync(group);
        if (_selectedContact?.UserId == group.UserId)
        {
            ChatTitle.Text = group.DisplayName;
            ChatSubtitle.Text = $"{group.GroupMemberCount} participants";
            RefreshGroupMembersPanel();
        }

        NetworkStatusText.Text = $"Group updated: {group.DisplayName}";
    }

    private async void HandleIncomingGroupMessage(ChatPacket packet, string statusText)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<GroupMessagePayload>(packet.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.GroupId))
            {
                return;
            }

            var group = _contacts.FirstOrDefault(x => x.UserId == payload.GroupId && x.IsGroup && !x.GroupIsDeleted);
            if (group is null)
            {
                AppLog.Write($"Incoming group message ignored: missing group={payload.GroupId}, from={packet.FromUserId}");
                return;
            }

            var message = new MessageViewModel
            {
                MessageId = packet.MessageId,
                PeerUserId = payload.GroupId,
                Body = payload.Text,
                Text = payload.Text,
                IsOutgoing = false,
                SentAtUtc = packet.SentAtUtc,
                SenderUserId = packet.FromUserId,
                SenderDisplayName = packet.FromDisplayName
            };
            PrepareMessageForUi(message);
            await _history.SaveAsync(message);

            if (_selectedContact?.UserId == payload.GroupId)
            {
                _messages.Add(message);
                ScrollMessagesToEnd();
                NetworkStatusText.Text = statusText;
            }
            else
            {
                ShowIncomingNotificationIfNeeded(group.DisplayName, packet);
                NetworkStatusText.Text = $"Group message in {group.DisplayName}";
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            AppLog.Write(ex, $"Incoming group message failed: from={packet.FromUserId}");
        }
    }

    private void RefreshGroupCandidates()
    {
        var query = GroupSearchInput.Text.Trim();
        _groupCandidates.Clear();
        foreach (var contact in _contacts
                     .Where(x => !x.IsGroup)
                     .Where(x => string.IsNullOrWhiteSpace(query) ||
                                 x.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                 x.UserId.Contains(query, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _groupCandidates.Add(new GroupCandidateViewModel
            {
                Contact = contact,
                IsAdded = _selectedGroupMemberIds.Contains(contact.UserId)
            });
        }
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_profile is not null)
        {
            SettingsDisplayNameInput.Text = _profile.DisplayName;
            SettingsAvatarInitials.Text = GetInitials(_profile.DisplayName);
        }

        ApplyAvatarVisuals();
        SettingsServerAddressInput.Text = _settings.RelayServer;
        SettingsServerAccessKeyInput.Text = _settings.RelayAccessKey;
        _isInitializingAudioFeatureControls = true;
        SettingsNoiseSuppressionCheck.IsChecked = _settings.NoiseSuppressionEnabled;
        _isInitializingAudioFeatureControls = false;
        RefreshAudioDeviceSelectors();
        ShowSettingsTab("account");
        ProfileFlyout.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsServerAddressInput.Focus();
    }

    private void SettingsCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SettingsAccountTabButton_OnClick(object sender, RoutedEventArgs e)
        => ShowSettingsTab("account");

    private void SettingsVoiceTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshAudioDeviceSelectors();
        ShowSettingsTab("voice");
    }

    private void SettingsBadgeAdminTabButton_OnClick(object sender, RoutedEventArgs e)
        => ShowSettingsTab("badges");

    private void ShowSettingsTab(string tab)
    {
        var isVoice = string.Equals(tab, "voice", StringComparison.OrdinalIgnoreCase);
        var isBadges = string.Equals(tab, "badges", StringComparison.OrdinalIgnoreCase) && _badgeState?.CanManageBadges == true;
        var isAccount = !isVoice && !isBadges;
        SettingsAccountHeader.Visibility = isAccount ? Visibility.Visible : Visibility.Collapsed;
        SettingsAccountContent.Visibility = isAccount ? Visibility.Visible : Visibility.Collapsed;
        SettingsVoiceHeader.Visibility = isVoice ? Visibility.Visible : Visibility.Collapsed;
        SettingsVoiceContent.Visibility = isVoice ? Visibility.Visible : Visibility.Collapsed;
        SettingsBadgeAdminHeader.Visibility = isBadges ? Visibility.Visible : Visibility.Collapsed;
        SettingsBadgeAdminContent.Visibility = isBadges ? Visibility.Visible : Visibility.Collapsed;
        SettingsAccountTabButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isAccount ? "#404249" : "#00000000"));
        SettingsVoiceTabButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isVoice ? "#404249" : "#00000000"));
        SettingsBadgeAdminTabButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isBadges ? "#404249" : "#00000000"));
    }

    private async void BadgeSelectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || _badgeAuthority is null) return;
        var badgeId = button.Tag as string;
        badgeId = string.IsNullOrWhiteSpace(badgeId) ? null : badgeId;
        if (badgeId is not null && _badgeState?.Certificates.Any(x => x.BadgeId == badgeId) != true)
        {
            BadgeStatusText.Text = "This badge is locked.";
            return;
        }

        try
        {
            SetBadgeButtonsEnabled(false);
            var state = await _badgeAuthority.SelectAsync(badgeId, _stop.Token);
            _badgeAdminSessionAuthenticated = true;
            ApplyBadgeState(state, badgeId is null ? "Profile badge disabled." : $"{badgeId} badge selected.");
            if (_relayClient is not null) _relayClient.ActiveBadgeCertificate = GetActiveBadgeCertificate();
            await PublishPresenceAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Badge selection failed");
            BadgeStatusText.Text = $"Badge Authority is unavailable: {ex.Message}";
        }
        finally
        {
            SetBadgeButtonsEnabled(true);
        }
    }

    private async void BadgeAdminSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_badgeAuthority is null || _badgeState?.CanManageBadges != true) return;
        var userId = BadgeAdminUserIdInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(userId)) return;
        try
        {
            BadgeAdminStatusText.Text = "Searching Official Badge Authority...";
            _badgeAdminTarget = await _badgeAuthority.LookupAsync(userId, _stop.Token);
            RefreshBadgeAdminTarget();
            BadgeAdminStatusText.Text = "Verified Authority record loaded.";
        }
        catch (Exception ex)
        {
            _badgeAdminTarget = null;
            BadgeAdminResultPanel.Visibility = Visibility.Collapsed;
            BadgeAdminStatusText.Text = ex.Message;
        }
    }

    private async void BadgeAdminTesterButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_badgeAuthority is null || _badgeAdminTarget is null || _badgeState?.CanManageBadges != true) return;
        try
        {
            BadgeAdminTesterButton.IsEnabled = false;
            var revoke = Equals(BadgeAdminTesterButton.Tag, "revoke");
            _badgeAdminTarget = revoke
                ? await _badgeAuthority.RevokeTesterAsync(_badgeAdminTarget.UserId, _stop.Token)
                : await _badgeAuthority.GrantTesterAsync(_badgeAdminTarget.UserId, _stop.Token);
            RefreshBadgeAdminTarget();
            BadgeAdminStatusText.Text = revoke ? "Tester certificate revoked and audited." : "Tester certificate granted and audited.";
        }
        catch (Exception ex)
        {
            BadgeAdminStatusText.Text = ex.Message;
        }
        finally
        {
            BadgeAdminTesterButton.IsEnabled = true;
        }
    }

    private void RefreshBadgeAdminTarget()
    {
        if (_badgeAdminTarget is null) return;
        BadgeAdminResultPanel.Visibility = Visibility.Visible;
        BadgeAdminResultName.Text = string.IsNullOrWhiteSpace(_badgeAdminTarget.DisplayName) ? "Registered user" : _badgeAdminTarget.DisplayName;
        BadgeAdminResultId.Text = _badgeAdminTarget.UserId;
        var hasTester = _badgeAdminTarget.Certificates.Any(x => x.BadgeId == BadgeIds.Tester);
        BadgeAdminTesterButton.Tag = hasTester ? "revoke" : "grant";
        BadgeAdminTesterButton.Content = hasTester ? "Revoke Tester" : "Grant Tester";
        BadgeAdminTesterButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hasTester ? "#b83a42" : "#5865f2"));
    }

    private async Task RefreshBadgeStateAsync()
    {
        if (_badgeAuthority is null) return;
        try
        {
            var state = await _badgeAuthority.RefreshAsync(_stop.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                _badgeAdminSessionAuthenticated = true;
                ApplyBadgeState(state, "Official badge state verified.");
                if (_relayClient is not null) _relayClient.ActiveBadgeCertificate = GetActiveBadgeCertificate();
            });
            await PublishPresenceAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Official Badge Authority refresh failed");
            await Dispatcher.InvokeAsync(() =>
            {
                _badgeAdminSessionAuthenticated = false;
                SettingsBadgeAdminTabButton.Visibility = Visibility.Collapsed;
                if (SettingsBadgeAdminContent.Visibility == Visibility.Visible) ShowSettingsTab("account");
                BadgeStatusText.Text = _badgeState is null
                    ? "Official Badge Authority is unavailable. Badges are not trusted yet."
                    : "Authority is offline. Using the last signed badge state.";
            });
        }
    }

    private void ApplyBadgeState(BadgeStateResponse? state, string status)
    {
        _badgeState = state;
        var hasTester = state?.Certificates.Any(x => x.BadgeId == BadgeIds.Tester) == true;
        var hasOwner = state?.Certificates.Any(x => x.BadgeId == BadgeIds.Owner) == true;
        BadgeTesterButton.IsEnabled = hasTester;
        BadgeTesterButton.Opacity = hasTester ? 1 : 0.48;
        BadgeTesterLock.Visibility = hasTester ? Visibility.Collapsed : Visibility.Visible;
        BadgeOwnerButton.Visibility = hasOwner ? Visibility.Visible : Visibility.Collapsed;
        var canManageNow = state?.CanManageBadges == true && _badgeAdminSessionAuthenticated;
        SettingsBadgeAdminTabButton.Visibility = canManageNow ? Visibility.Visible : Visibility.Collapsed;
        if (!canManageNow && SettingsBadgeAdminContent.Visibility == Visibility.Visible) ShowSettingsTab("account");
        BadgeStatusText.Text = state is null ? "No verified badge state is available." : status;

        var selected = state?.SelectedBadgeId;
        var normal = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#363940"));
        var accent = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4b57b7"));
        BadgeNoneButton.Background = selected is null ? accent : normal;
        BadgeTesterButton.Background = selected == BadgeIds.Tester ? accent : normal;
        BadgeOwnerButton.Background = selected == BadgeIds.Owner ? accent : normal;
    }

    private void SetBadgeButtonsEnabled(bool enabled)
    {
        BadgeNoneButton.IsEnabled = enabled;
        BadgeTesterButton.IsEnabled = enabled && _badgeState?.Certificates.Any(x => x.BadgeId == BadgeIds.Tester) == true;
        BadgeOwnerButton.IsEnabled = enabled;
    }

    private BadgeCertificate? GetActiveBadgeCertificate()
        => _badgeState?.SelectedBadgeId is { } selected
            ? _badgeState.Certificates.FirstOrDefault(x => x.BadgeId == selected)
            : null;

    private void ValidateStoredContactBadge(ContactViewModel contact)
    {
        var certificate = BadgeCrypto.DeserializeCertificate(contact.BadgeCertificateJson);
        if (_badgeAuthority is null || _badgeState is null ||
            !_badgeAuthority.VerifyRemoteCertificate(certificate, contact.IdentityPublicKey, _badgeState.Revocations))
        {
            ClearVerifiedBadge(contact);
            return;
        }

        contact.VerifiedBadgeId = certificate!.BadgeId;
    }

    private void ApplyVerifiedBadge(ContactViewModel contact, string? publicKey, BadgeCertificate? certificate, bool identityVerified)
    {
        if (!identityVerified)
        {
            if (string.IsNullOrWhiteSpace(publicKey) && certificate is null)
            {
                ClearVerifiedBadge(contact);
            }
            return;
        }

        if (_badgeAuthority is null || _badgeState is null ||
            !_badgeAuthority.VerifyRemoteCertificate(certificate, publicKey, _badgeState.Revocations))
        {
            ClearVerifiedBadge(contact);
        }
        else
        {
            contact.VerifiedBadgeId = certificate!.BadgeId;
            contact.BadgeCertificateJson = BadgeCrypto.SerializeCertificate(certificate) ?? "";
            contact.IdentityPublicKey = publicKey ?? "";
            contact.BadgeVerifiedAtUtc = DateTimeOffset.UtcNow;
        }

        if (_selectedContact?.UserId == contact.UserId)
        {
            ChatBadgeImage.Source = contact.BadgeImageSource;
            ChatBadgeImage.ToolTip = contact.BadgeToolTip;
            ChatBadgeImage.Visibility = contact.HasVerifiedBadge ? Visibility.Visible : Visibility.Collapsed;
        }
        _ = _history.SaveContactAsync(contact);
    }

    private static void ClearVerifiedBadge(ContactViewModel contact)
    {
        contact.VerifiedBadgeId = "";
        contact.BadgeCertificateJson = "";
        contact.IdentityPublicKey = "";
        contact.BadgeVerifiedAtUtc = null;
    }

    private void RefreshAudioDeviceSelectors()
    {
        _isRefreshingAudioDevices = true;
        try
        {
            var inputDevices = AudioCallSession.GetInputDevices();
            var outputDevices = AudioCallSession.GetOutputDevices();

            SettingsAudioInputDeviceCombo.ItemsSource = inputDevices;
            SettingsAudioInputDeviceCombo.SelectedItem = inputDevices.FirstOrDefault(x => x.Id == _settings.AudioInputDeviceId)
                ?? inputDevices.FirstOrDefault();

            SettingsAudioOutputDeviceCombo.ItemsSource = outputDevices;
            SettingsAudioOutputDeviceCombo.SelectedItem = outputDevices.FirstOrDefault(x => x.Id == _settings.AudioOutputDeviceId)
                ?? outputDevices.FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Audio devices could not be listed");
            VoiceTestStatusText.Text = "Audio devices are not available.";
        }
        finally
        {
            _isRefreshingAudioDevices = false;
        }
    }

    private async void SettingsAudioDeviceCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingAudioDevices)
        {
            return;
        }

        if (SettingsAudioInputDeviceCombo.SelectedItem is AudioDeviceInfo input)
        {
            _settings.AudioInputDeviceId = input.Id;
        }

        if (SettingsAudioOutputDeviceCombo.SelectedItem is AudioDeviceInfo output)
        {
            _settings.AudioOutputDeviceId = output.Id;
        }

        try
        {
            await AppSettingsStore.SaveAsync(_settings);
            VoiceTestStatusText.Text = "Voice devices saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "Audio device settings could not be saved");
            VoiceTestStatusText.Text = "Voice devices could not be saved.";
        }
    }

    private async Task LoadCallAudioFeaturesAsync()
    {
        _callAudioPreferences = await CallAudioPreferencesStore.LoadAsync();
        var library = await SoundboardLibraryStore.LoadAsync();
        _isInitializingAudioFeatureControls = true;
        _soundboardVolume = Math.Clamp(library.Volume, 0, 1);
        SoundboardVolumeSlider.Value = _soundboardVolume * 100;
        SoundboardVolumeText.Text = $"{Math.Round(_soundboardVolume * 100):0}%";
        _soundboardClips.Clear();
        foreach (var clip in library.Clips
                     .Where(x => File.Exists(x.PcmPath))
                     .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _soundboardClips.Add(new SoundboardClipViewModel
            {
                Id = clip.Id,
                DisplayName = clip.DisplayName,
                SourcePath = clip.SourcePath,
                PcmPath = clip.PcmPath,
                DurationSeconds = clip.DurationSeconds,
                AddedAtUtc = clip.AddedAtUtc
            });
        }

        _isInitializingAudioFeatureControls = false;
        UpdateSoundboardEmptyState();
    }

    private void SoundboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeCallState != "connected" || !_selfInCall)
        {
            NetworkStatusText.Text = "Join a call to use the soundboard.";
            return;
        }

        SoundboardPopup.IsOpen = !SoundboardPopup.IsOpen;
        if (SoundboardPopup.IsOpen)
        {
            SoundboardSearchInput.Focus();
        }
    }

    private async void SoundboardAddButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add sound",
            Filter = "Audio files|*.wav;*.mp3;*.ogg;*.m4a;*.aac;*.flac;*.wma|All files|*.*",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var ffmpegPath = FindFfmpegExecutable();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            NetworkStatusText.Text = "ffmpeg.exe is required to add soundboard sounds.";
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        AppPaths.EnsureSoundboardDirectoriesCreated();
        var extension = Path.GetExtension(dialog.FileName);
        var sourcePath = Path.Combine(AppPaths.SoundboardDirectory, id + extension);
        var pcmPath = Path.Combine(AppPaths.SoundboardCacheDirectory, id + ".pcm");
        try
        {
            NetworkStatusText.Text = "Preparing sound...";
            File.Copy(dialog.FileName, sourcePath, overwrite: true);
            await ConvertSoundboardAudioAsync(ffmpegPath, sourcePath, pcmPath, _stop.Token);
            var pcmBytes = new FileInfo(pcmPath).Length;
            if (pcmBytes < CallAudioOpusFrameSize * 2)
            {
                throw new InvalidDataException("The selected audio file is empty.");
            }

            var clip = new SoundboardClipViewModel
            {
                Id = id,
                DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName),
                SourcePath = sourcePath,
                PcmPath = pcmPath,
                DurationSeconds = pcmBytes / (double)(CallAudioSampleRate * CallAudioChannels * 2),
                AddedAtUtc = DateTimeOffset.UtcNow
            };
            _soundboardClips.Add(clip);
            await SaveSoundboardLibraryAsync();
            ApplySoundboardFilter();
            UpdateSoundboardEmptyState();
            NetworkStatusText.Text = $"Sound added: {clip.DisplayName}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException)
        {
            TryDeleteFile(sourcePath);
            TryDeleteFile(pcmPath);
            AppLog.Write(ex, $"Soundboard sound could not be added: source={dialog.FileName}");
            NetworkStatusText.Text = $"Sound could not be added: {ex.Message}";
        }
    }

    private static async Task ConvertSoundboardAudioAsync(
        string ffmpegPath,
        string sourcePath,
        string pcmPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(SoundboardMaxDurationSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add(CallAudioChannels.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add(CallAudioSampleRate.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("s16le");
        startInfo.ArgumentList.Add(pcmPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg.exe did not start.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0 || !File.Exists(pcmPath))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Audio conversion failed." : error.Split('\n').LastOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim());
        }
    }

    private void SoundboardSearchInput_OnTextChanged(object sender, TextChangedEventArgs e)
        => ApplySoundboardFilter();

    private void ApplySoundboardFilter()
    {
        var query = SoundboardSearchInput.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(_soundboardClips);
        view.Filter = item => item is SoundboardClipViewModel clip &&
                              (query.Length == 0 || clip.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        view.Refresh();
    }

    private async void SoundboardVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _soundboardVolume = Math.Clamp(e.NewValue / 100d, 0, 1);
        if (SoundboardVolumeText is not null)
        {
            SoundboardVolumeText.Text = $"{Math.Round(e.NewValue):0}%";
        }

        if (IsLoaded && !_isInitializingAudioFeatureControls)
        {
            await SaveSoundboardLibraryAsync();
        }
    }

    private void SoundboardPlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: SoundboardClipViewModel clip })
        {
            return;
        }

        try
        {
            var pcm = File.ReadAllBytes(clip.PcmPath);
            lock (_soundboardAudioGate)
            {
                if (ReferenceEquals(_activeSoundboardClip, clip))
                {
                    StopActiveSoundboardLocked();
                    return;
                }

                StopActiveSoundboardLocked();
                _activeSoundboardPcm = pcm;
                _activeSoundboardOffset = 0;
                _activeSoundboardClip = clip;
                clip.IsPlaying = true;
            }

            NetworkStatusText.Text = $"Playing sound: {clip.DisplayName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, $"Soundboard playback failed: clip={clip.Id}");
            NetworkStatusText.Text = "Sound could not be opened.";
        }
    }

    private async void SoundboardDeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: SoundboardClipViewModel clip })
        {
            return;
        }

        lock (_soundboardAudioGate)
        {
            if (ReferenceEquals(_activeSoundboardClip, clip))
            {
                StopActiveSoundboardLocked();
            }
        }

        _soundboardClips.Remove(clip);
        TryDeleteFile(clip.SourcePath);
        TryDeleteFile(clip.PcmPath);
        await SaveSoundboardLibraryAsync();
        ApplySoundboardFilter();
        UpdateSoundboardEmptyState();
    }

    private void StopActiveSoundboardLocked()
    {
        if (_activeSoundboardClip is not null)
        {
            _activeSoundboardClip.IsPlaying = false;
        }

        _activeSoundboardClip = null;
        _activeSoundboardPcm = null;
        _activeSoundboardOffset = 0;
    }

    private void StopActiveSoundboard()
    {
        lock (_soundboardAudioGate)
        {
            StopActiveSoundboardLocked();
        }
    }

    private async Task SaveSoundboardLibraryAsync()
    {
        try
        {
            await SoundboardLibraryStore.SaveAsync(_soundboardVolume, _soundboardClips);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "Soundboard library could not be saved");
        }
    }

    private void UpdateSoundboardEmptyState()
        => SoundboardEmptyText.Visibility = _soundboardClips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async void SettingsNoiseSuppressionCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializingAudioFeatureControls)
        {
            return;
        }

        _settings.NoiseSuppressionEnabled = SettingsNoiseSuppressionCheck.IsChecked == true;
        try
        {
            await AppSettingsStore.SaveAsync(_settings);
            VoiceTestStatusText.Text = _settings.NoiseSuppressionEnabled
                ? "Noise suppression enabled."
                : "Noise suppression disabled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "Noise suppression setting could not be saved");
        }
    }

    private void VoiceTestButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isVoiceTestActive)
        {
            StopVoiceTest(restoreCallAudio: true);
            return;
        }

        StartVoiceTest();
    }

    private void StartVoiceTest()
    {
        StopVoiceTest(restoreCallAudio: false);

        try
        {
            if (_activeCallState == "connected" && _selfInCall && _audioCall is not null)
            {
                _isVoiceTestActive = true;
                Interlocked.Exchange(ref _isStoppingVoiceTest, 0);
                Interlocked.Exchange(ref _lastVoiceTestUiTicks, 0);
                VoiceTestButton.Content = "Stop voice check";
                VoiceTestStatusText.Text = "Voice check is running.";
                AppLog.Write($"Voice test started in active call: input={_settings.AudioInputDeviceId}, output={_settings.AudioOutputDeviceId}");

                _isMicrophoneMuted = true;
                _isHeadphonesMuted = true;
                UpdateCallAudioControlVisuals(animate: true);
                _ = SendCallAudioStateAsync();
                return;
            }

            var session = new AudioCallSession(_settings.AudioInputDeviceId, _settings.AudioOutputDeviceId);
            _noiseFloorRms = 90;
            _noiseGateGain = 1;
            session.AudioCaptured += VoiceTestAudioCaptured;
            session.Start();
            _voiceTestSession = session;
            _isVoiceTestActive = true;
            Interlocked.Exchange(ref _lastVoiceTestUiTicks, 0);
            VoiceTestButton.Content = "Stop voice check";
            VoiceTestStatusText.Text = "Voice check is running.";
            AppLog.Write($"Voice test started: input={_settings.AudioInputDeviceId}, output={_settings.AudioOutputDeviceId}");
        }
        catch (Exception ex)
        {
            StopVoiceTest(restoreCallAudio: false);
            AppLog.Write(ex, "Voice test failed");
            VoiceTestStatusText.Text = $"Voice check failed: {ex.Message}";
        }
    }

    private void StopVoiceTest(bool restoreCallAudio)
    {
        var session = Interlocked.Exchange(ref _voiceTestSession, null);

        if (!_isVoiceTestActive)
        {
            DisposeVoiceTestSessionAsync(session);
            return;
        }

        _isVoiceTestActive = false;
        Interlocked.Exchange(ref _isStoppingVoiceTest, session is null ? 0 : 1);
        Interlocked.Exchange(ref _lastVoiceTestUiTicks, 0);
        VoiceTestButton.Content = "Check voice";
        VoiceTestStatusText.Text = session is null ? "Voice check stopped." : "Stopping voice check...";
        AppLog.Write("Voice test stopped");
        DisposeVoiceTestSessionAsync(session);

        if (restoreCallAudio && _activeCallState == "connected" && _selfInCall)
        {
            _isMicrophoneMuted = false;
            _isHeadphonesMuted = false;
            UpdateCallAudioControlVisuals(animate: true);
            _ = SendCallAudioStateAsync();
        }
    }

    private void DisposeVoiceTestSessionAsync(AudioCallSession? session)
    {
        if (session is null)
        {
            return;
        }

        session.AudioCaptured -= VoiceTestAudioCaptured;
        _ = Task.Run(() =>
        {
            try
            {
                session.Dispose();
                AppLog.Write("Voice test audio session disposed");
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Voice test audio session dispose failed");
            }
            finally
            {
                Interlocked.Exchange(ref _isStoppingVoiceTest, 0);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_isVoiceTestActive)
                    {
                        VoiceTestStatusText.Text = "Voice check stopped.";
                    }
                }));
            }
        });
    }

    private void VoiceTestAudioCaptured(byte[] pcm)
    {
        var session = _voiceTestSession;
        if (session is null || !_isVoiceTestActive)
        {
            return;
        }

        var peak = GetPcmPeak(pcm);
        var playbackPcm = ProcessMicrophonePcm(pcm);
        if (Interlocked.CompareExchange(ref _isStoppingVoiceTest, 0, 0) != 0)
        {
            return;
        }

        if (!session.Play(playbackPcm, out var error))
        {
            Dispatcher.BeginInvoke(new Action(() => VoiceTestStatusText.Text = $"Playback failed: {error}"));
            return;
        }

        UpdateVoiceTestLevel(peak);
    }

    private void UpdateVoiceTestLevel(int peak)
    {
        if (peak <= CallAudioSilencePeak)
        {
            return;
        }

        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastVoiceTestUiTicks);
        if ((lastTicks == 0 ||
             TimeSpan.FromTicks(nowTicks - lastTicks) >= TimeSpan.FromMilliseconds(250)) &&
            Interlocked.CompareExchange(ref _lastVoiceTestUiTicks, nowTicks, lastTicks) == lastTicks)
        {
            Dispatcher.BeginInvoke(new Action(() => VoiceTestStatusText.Text = $"Voice level: {peak}"));
        }
    }

    private async void AvatarColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string color })
        {
            return;
        }

        _selectedAvatarColor = color;
        _selectedAvatarKind = "color";
        DeleteAvatarFileIfOwned(_selectedAvatarPath);
        _selectedAvatarPath = "";
        ApplyAvatarVisuals();
        await SaveProfileAsync();
    }

    private void ChooseAvatarImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose avatar image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ShowAvatarEditor("image", CopyAvatarFile(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write(ex, "Avatar image could not be loaded");
            NetworkStatusText.Text = $"Avatar image failed: {ex.Message}";
        }
    }

    private void ChooseAvatarVideoButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose animated avatar video",
            Filter = "Videos|*.mp4;*.mov;*.m4v;*.wmv;*.avi;*.mkv;*.webm|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ShowAvatarEditor("video", CopyAvatarFile(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write(ex, "Avatar video could not be loaded");
            NetworkStatusText.Text = $"Avatar video failed: {ex.Message}";
        }
    }

    private void AvatarCropSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _selectedAvatarScale = SettingsAvatarZoomSlider.Value;
        _selectedAvatarOffsetX = SettingsAvatarOffsetXSlider.Value;
        _selectedAvatarOffsetY = SettingsAvatarOffsetYSlider.Value;
        ClampSelectedAvatarOffset();
        ApplyAvatarTransforms();
    }

    private void AvatarVideoSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _selectedAvatarVideoStartSeconds = SettingsAvatarVideoStartSlider.Value;
        _selectedAvatarVideoDurationSeconds = Math.Min(10, Math.Max(1, SettingsAvatarVideoDurationSlider.Value));
        RestartAvatarVideos();
    }

    private void AvatarEditorZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _pendingAvatarScale = AvatarEditorZoomSlider.Value;
        ClampPendingAvatarOffset();
        ApplyAvatarEditorTransform();
    }

    private void AvatarEditorVideoSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _pendingAvatarVideoStartSeconds = AvatarEditorVideoStartSlider.Value;
        _pendingAvatarVideoDurationSeconds = Math.Min(10, Math.Max(1, AvatarEditorVideoDurationSlider.Value));
        RestartEditorVideo();
    }

    private void AvatarEditorStage_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingAvatar = true;
        _lastAvatarDragPoint = e.GetPosition(AvatarEditorStage);
        AvatarEditorStage.CaptureMouse();
    }

    private void AvatarEditorStage_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDraggingAvatar)
        {
            return;
        }

        var current = e.GetPosition(AvatarEditorStage);
        _pendingAvatarOffsetX += current.X - _lastAvatarDragPoint.X;
        _pendingAvatarOffsetY += current.Y - _lastAvatarDragPoint.Y;
        ClampPendingAvatarOffset();
        _lastAvatarDragPoint = current;
        ApplyAvatarEditorTransform();
    }

    private void AvatarEditorStage_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingAvatar = false;
        AvatarEditorStage.ReleaseMouseCapture();
    }

    private void AvatarEditorResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _pendingAvatarScale = 1;
        _pendingAvatarOffsetX = 0;
        _pendingAvatarOffsetY = 0;
        _pendingAvatarVideoStartSeconds = 0;
        _pendingAvatarVideoDurationSeconds = 10;
        AvatarEditorZoomSlider.Value = 1;
        AvatarEditorVideoStartSlider.Value = 0;
        AvatarEditorVideoDurationSlider.Value = Math.Min(10, AvatarEditorVideoDurationSlider.Maximum);
        ApplyAvatarEditorTransform();
        RestartEditorVideo();
    }

    private void AvatarEditorCancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        AvatarEditorVideo.Stop();
        AvatarEditorOverlay.Visibility = Visibility.Collapsed;
        DeleteAvatarFileIfOwned(_pendingAvatarPath);
        _pendingAvatarPath = "";
    }

    private async void AvatarEditorApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        var previousAvatarPath = _selectedAvatarPath;
        _selectedAvatarKind = _pendingAvatarKind;
        _selectedAvatarPath = _pendingAvatarPath;
        _selectedAvatarScale = _pendingAvatarScale;
        _selectedAvatarOffsetX = _pendingAvatarOffsetX;
        _selectedAvatarOffsetY = _pendingAvatarOffsetY;
        ClampSelectedAvatarOffset();
        _selectedAvatarVideoStartSeconds = _pendingAvatarVideoStartSeconds;
        _selectedAvatarVideoDurationSeconds = Math.Min(10, Math.Max(1, _pendingAvatarVideoDurationSeconds));

        SettingsAvatarZoomSlider.Value = _selectedAvatarScale;
        SettingsAvatarOffsetXSlider.Value = Math.Clamp(_selectedAvatarOffsetX, -180, 180);
        SettingsAvatarOffsetYSlider.Value = Math.Clamp(_selectedAvatarOffsetY, -180, 180);
        SettingsAvatarVideoStartSlider.Value = _selectedAvatarVideoStartSeconds;
        SettingsAvatarVideoDurationSlider.Value = _selectedAvatarVideoDurationSeconds;
        AvatarEditorVideo.Stop();
        AvatarEditorOverlay.Visibility = Visibility.Collapsed;
        ApplyAvatarVisuals();
        await SaveProfileAsync();
        DeleteAvatarFileIfOwned(previousAvatarPath, _selectedAvatarPath);
        _pendingAvatarPath = "";
        NetworkStatusText.Text = _selectedAvatarKind == "video" ? "Animated avatar saved" : "Avatar saved";
    }

    private async void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (await SaveProfileAsync())
        {
            NetworkStatusText.Text = "Profile saved";
        }
    }

    private async Task<bool> SaveProfileAsync()
    {
        if (_profile is null)
        {
            return false;
        }

        var displayName = SettingsDisplayNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            NetworkStatusText.Text = "Nickname cannot be empty";
            return false;
        }

        _profile = _profile with
        {
            DisplayName = displayName,
            AvatarColor = _selectedAvatarColor,
            AvatarKind = _selectedAvatarKind,
            AvatarPath = _selectedAvatarPath,
            AvatarScale = _selectedAvatarScale,
            AvatarOffsetX = _selectedAvatarOffsetX,
            AvatarOffsetY = _selectedAvatarOffsetY,
            AvatarVideoStartSeconds = _selectedAvatarVideoStartSeconds,
            AvatarVideoDurationSeconds = _selectedAvatarVideoDurationSeconds
        };

        await UserProfileStore.SaveAsync(_profile);
        _settings.TenorApiKey = SettingsTenorApiKeyInput.Text.Trim();
        await AppSettingsStore.SaveAsync(_settings);
        RefreshProfileUi();
        await BroadcastProfileUpdateAsync();
        return true;
    }

    private async void FirstRunLinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        ServerAddressInput.Text = FirstRunServerAddressInput.Text.Trim();
        ServerAccessKeyInput.Text = FirstRunServerAccessKeyInput.Text.Trim();
        SettingsServerAddressInput.Text = FirstRunServerAddressInput.Text.Trim();
        SettingsServerAccessKeyInput.Text = FirstRunServerAccessKeyInput.Text.Trim();
        FirstRunVpsOverlay.Visibility = Visibility.Collapsed;
        await SaveRelaySettingsAndConnectAsync(SettingsServerAddressInput.Text, SettingsServerAccessKeyInput.Text);
    }

    private async void FirstRunLaterButton_OnClick(object sender, RoutedEventArgs e)
    {
        FirstRunVpsOverlay.Visibility = Visibility.Collapsed;
        await AppSettingsStore.SaveAsync(_settings);
        NetworkStatusText.Text = "VPS setup skipped. You can link it later.";
    }

    private async Task SaveRelaySettingsAndConnectAsync(string relayServer, string relayAccessKey)
    {
        _settings.RelayServer = relayServer.Trim();
        _settings.RelayAccessKey = relayAccessKey.Trim();
        if (_settings.RelayAccessKey != _settings.RelayClientToken)
        {
            _settings.RelayClientToken = "";
        }

        ServerAddressInput.Text = _settings.RelayServer;
        ServerAccessKeyInput.Text = _settings.RelayAccessKey;
        SettingsServerAddressInput.Text = _settings.RelayServer;
        SettingsServerAccessKeyInput.Text = _settings.RelayAccessKey;
        FirstRunServerAddressInput.Text = _settings.RelayServer;
        FirstRunServerAccessKeyInput.Text = _settings.RelayAccessKey;
        await AppSettingsStore.SaveAsync(_settings);
        await ConnectRelayAsync();
    }

    private static async Task<bool> TrySetClipboardTextAsync(string text)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException ex) when ((uint)ex.ErrorCode == 0x800401D0)
            {
                await Task.Delay(45 + attempt * 35);
            }
        }

        return false;
    }

    private async Task ConnectRelayAsync(string? serverOverride = null)
    {
        if (_relayClient is null)
        {
            return;
        }

        var relayServer = NormalizeRelayServer(serverOverride ?? _settings.RelayServer);
        if (_relayClient.IsConnected && string.Equals(_relayClient.ConnectedServer, relayServer, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            NetworkStatusText.Text = $"Connecting to VPS server {relayServer}...";
            var credential = string.IsNullOrWhiteSpace(_settings.RelayClientToken)
                ? _settings.RelayAccessKey
                : _settings.RelayClientToken;
            var clientToken = await _relayClient.ConnectAsync(relayServer, credential, _stop.Token);
            if (!string.IsNullOrWhiteSpace(clientToken))
            {
                _settings.RelayClientToken = clientToken;
                _settings.RelayAccessKey = clientToken;
                ServerAccessKeyInput.Text = clientToken;
                SettingsServerAccessKeyInput.Text = clientToken;
                _settings.RelayServer = relayServer;
                ServerAddressInput.Text = relayServer;
                SettingsServerAddressInput.Text = relayServer;
                await AppSettingsStore.SaveAsync(_settings);
                NetworkStatusText.Text = "VPS connected. Token saved.";
                await PublishPresenceAsync(force: true);
            }
            else
            {
                await PublishPresenceAsync(force: true);
            }

            ConfigureScreenShareWebRtc();
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Relay connect failed: server={relayServer}");
            NetworkStatusText.Text = $"VPS connect failed: {ex.Message}";
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task BroadcastProfileUpdateAsync()
    {
        if (_profile is null || _contacts.Count == 0)
        {
            return;
        }

        foreach (var contact in _contacts.Where(x => !x.IsGroup).ToArray())
        {
            try
            {
                var packet = CreateProfilePacket(contact.UserId, "", ProfileUpdateIntent);
                await SendOverRelayAsync(packet, contact);
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested)
            {
                AppLog.Write(ex, $"Profile update failed: to={contact.UserId}");
            }
        }
    }

    private async Task SendProfileUpdateToContactAsync(ContactViewModel contact)
    {
        if (_profile is null || contact.IsGroup)
        {
            return;
        }

        try
        {
            var packet = CreateProfilePacket(contact.UserId, "", ProfileUpdateIntent);
            await SendOverRelayAsync(packet, contact);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Profile sync failed: to={contact.UserId}");
        }
    }

    private async Task PublishPresenceAsync(bool force = false)
    {
        if (_profile is null || _relayClient is null || !_relayClient.IsConnected)
        {
            return;
        }

        var status = GetCurrentStatus();
        var statusChanged = force || status != _lastPublishedStatus;
        _lastPublishedStatus = status;
        try
        {
            (string kind, string? mediaBase64, string? extension) avatarPayload = (_profile.AvatarKind, null, null);
            await _relayClient.SendPresenceAsync(
                status.ToString(),
                _stop.Token,
                avatarPayload.kind,
                avatarPayload.mediaBase64,
                avatarPayload.extension,
                _profile.AvatarScale,
                _profile.AvatarOffsetX,
                _profile.AvatarOffsetY,
                _profile.AvatarVideoStartSeconds,
                _profile.AvatarVideoDurationSeconds);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Presence heartbeat failed");
            return;
        }

        if (!statusChanged)
        {
            return;
        }

        foreach (var contact in _contacts.Where(x => !x.IsGroup).ToArray())
        {
            try
            {
                var packet = CreatePresencePacket(contact, includeAvatar: false);
                await SendOverRelayAsync(packet, contact);
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested)
            {
                AppLog.Write(ex, $"Contact presence failed: to={contact.UserId}");
            }
        }
    }

    private async Task PublishOfflinePresenceAsync()
    {
        if (_profile is null || _relayClient is null || !_relayClient.IsConnected)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _relayClient.SendPresenceAsync(UserPresenceStatus.Offline.ToString(), timeout.Token);
            AppLog.Write("Offline presence sent");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Offline presence failed");
        }

        foreach (var contact in _contacts.Where(x => !x.IsGroup).ToArray())
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var packet = CreatePresencePacket(contact, UserPresenceStatus.Offline, includeAvatar: false);
                await SendOverRelayAsync(packet, contact, log: false);
                AppLog.Write($"Offline contact presence sent: to={contact.UserId}");
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Offline contact presence failed: to={contact.UserId}");
            }
        }
    }

    private void MarkStaleContactsOffline()
    {
        var staleBefore = DateTimeOffset.UtcNow - ContactOfflineAfter;
        foreach (var contact in _contacts.Where(x => !x.IsGroup && x.Status != UserPresenceStatus.Offline).ToArray())
        {
            if (contact.LastSeenUtc == default || contact.LastSeenUtc >= staleBefore)
            {
                continue;
            }

            contact.Status = UserPresenceStatus.Offline;
            _ = _history.SaveContactAsync(contact);
            AppLog.Write($"Contact marked offline by stale presence: userId={contact.UserId}, lastSeenUtc={contact.LastSeenUtc:O}");

            if (_activeCallContact?.UserId == contact.UserId)
            {
                HideCallPanel();
                NetworkStatusText.Text = $"{contact.DisplayName} went offline";
            }
        }
    }

    private void VpsHelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        new VpsHelpWindow
        {
            Owner = this
        }.ShowDialog();
    }

    private void ContextMenu_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ContextMenu menu)
        {
            return;
        }

        menu.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

        var scale = new ScaleTransform(0.98, 0.98);
        menu.RenderTransform = scale;
        var animation = new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private async void RenameContactMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ContactViewModel contact })
        {
            return;
        }

        var dialog = new RenameContactWindow(contact)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        contact.DisplayName = dialog.ContactName;
        await _history.SaveContactAsync(contact);

        if (_selectedContact?.UserId == contact.UserId)
        {
            ChatTitle.Text = contact.DisplayName;
        }

        NetworkStatusText.Text = $"Renamed contact to {contact.DisplayName}";
    }

    private async void DeleteContactMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ContactViewModel contact })
        {
            return;
        }

        var result = MessageBox.Show(
            $"Remove {contact.DisplayName} from friends?",
            "FluxChat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RemoveContactFromFriendsAsync(contact, notifyPeer: true);
    }

    private async void EditGroupNameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ContactViewModel { CanEditGroup: true } group })
        {
            return;
        }

        var dialog = new RenameContactWindow(group)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        group.DisplayName = dialog.ContactName;
        group.GroupVersion++;
        await _history.SaveContactAsync(group);
        await BroadcastGroupSnapshotAsync(group);
        if (_selectedContact?.UserId == group.UserId)
        {
            ChatTitle.Text = group.DisplayName;
        }

        NetworkStatusText.Text = $"Group renamed to {group.DisplayName}";
    }

    private async void EditGroupPictureMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ContactViewModel { CanEditGroup: true } group })
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        group.AvatarKind = "image";
        group.AvatarPath = dialog.FileName;
        group.GroupVersion++;
        await _history.SaveContactAsync(group);
        await BroadcastGroupSnapshotAsync(group);
        NetworkStatusText.Text = "Group picture updated";
    }

    private async void DeleteGroupMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ContactViewModel { CanEditGroup: true } group })
        {
            return;
        }

        var result = MessageBox.Show(
            $"Delete group {group.DisplayName} for all participants?",
            "FluxChat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        group.GroupIsDeleted = true;
        group.GroupVersion++;
        await _history.SaveContactAsync(group);
        var snapshot = CreateGroupSnapshot(group);
        var action = new GroupActionPayload(group.UserId, group.GroupVersion, _profile?.UserId ?? "", "");
        foreach (var member in snapshot.Members.Where(x => _profile is null || !string.Equals(x.UserId, _profile.UserId, StringComparison.Ordinal)))
        {
            await SendGroupActionAsync(GroupDeleteIntent, action, member);
        }

        RemoveContactFromUi(group);
        NetworkStatusText.Text = "Group deleted";
    }

    private async void LeaveGroupMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ContactViewModel { CanLeaveGroup: true } group } ||
            _profile is null)
        {
            return;
        }

        var members = LoadGroupMembers(group);
        var owner = members.FirstOrDefault(x => string.Equals(x.UserId, group.GroupOwnerUserId, StringComparison.Ordinal));
        var action = new GroupActionPayload(group.UserId, group.GroupVersion + 1, _profile.UserId, _profile.UserId);
        if (owner is not null)
        {
            await SendGroupActionAsync(GroupLeaveIntent, action, owner);
        }

        group.GroupIsDeleted = true;
        group.GroupVersion++;
        await _history.SaveContactAsync(group);
        RemoveContactFromUi(group);
        NetworkStatusText.Text = "Left group";
    }

    private async Task RemoveContactFromFriendsAsync(ContactViewModel contact, bool notifyPeer)
    {
        if (notifyPeer)
        {
            try
            {
                var packet = CreateProfilePacket(contact.UserId, "Removed from friends", FriendRemoveIntent);
                await SendOverRelayAsync(packet, contact);
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested)
            {
                AppLog.Write(ex, $"Friend remove notification failed: to={contact.UserId}");
            }
        }

        RemoveContactFromUi(contact);
        await _history.DeleteContactAsync(contact.UserId);
        DeleteAvatarFileIfOwned(contact.AvatarPath);
        NetworkStatusText.Text = notifyPeer
            ? $"Removed {contact.DisplayName} from friends"
            : $"{contact.DisplayName} removed you from friends";
    }

    private void RemoveContactFromUi(ContactViewModel contact)
    {
        _contacts.Remove(contact);
        if (_contacts.Count == 0)
        {
            EmptyContactsHint.Visibility = Visibility.Visible;
        }

        if (_selectedContact?.UserId == contact.UserId)
        {
            _selectedContact = null;
            ContactsList.SelectedItem = null;
            _messages.Clear();
            ChatTitle.Text = "Choose a contact";
            ChatSubtitle.Text = "Click a contact to open conversation";
            ComposerPanel.Visibility = Visibility.Collapsed;
            StartCallButton.Visibility = Visibility.Collapsed;
            GroupMembersButton.Visibility = Visibility.Collapsed;
            SetGroupMembersPanelVisible(false);
            EmptyChatHint.Visibility = Visibility.Visible;
        }
    }

    private void HandleIncomingFriendRemove(ChatPacket packet)
    {
        var contact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId);
        if (contact is null)
        {
            return;
        }

        _ = RemoveContactFromFriendsAsync(contact, notifyPeer: false);
    }

    private void GroupMembersButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedContact is not { IsGroup: true })
        {
            return;
        }

        SetGroupMembersPanelVisible(GroupMembersPanel.Visibility != Visibility.Visible);
    }

    private void GroupMembersCloseButton_OnClick(object sender, RoutedEventArgs e)
        => SetGroupMembersPanelVisible(false);

    private void SetGroupMembersPanelVisible(bool visible)
    {
        if (visible && _selectedContact is not { IsGroup: true })
        {
            visible = false;
        }

        if (visible)
        {
            RefreshGroupMembersPanel();
            GroupMembersPanel.Visibility = Visibility.Visible;
            GroupMembersGapColumn.Width = new GridLength(14);
            GroupMembersColumn.Width = new GridLength(320);
            GroupMembersPanel.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            return;
        }

        GroupMembersPanel.Visibility = Visibility.Collapsed;
        GroupMembersPanel.Opacity = 1;
        GroupMembersGapColumn.Width = new GridLength(0);
        GroupMembersColumn.Width = new GridLength(0);
    }

    private void RefreshGroupMembersPanel()
    {
        _groupMembers.Clear();
        if (_selectedContact is not { IsGroup: true } group || _profile is null)
        {
            return;
        }

        EnsureGroupMetadata(group);
        var canManage = group.IsCurrentUserGroupOwner;
        foreach (var member in LoadGroupMembers(group)
                     .OrderByDescending(x => string.Equals(x.UserId, group.GroupOwnerUserId, StringComparison.Ordinal))
                     .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var contact = _contacts.FirstOrDefault(x => x.UserId == member.UserId && !x.IsGroup);
            _groupMembers.Add(new GroupMemberViewModel
            {
                UserId = member.UserId,
                DisplayName = contact?.DisplayName ?? member.DisplayName,
                RelayServer = NormalizeRelayServer(member.RelayServer),
                AvatarKind = contact?.AvatarKind ?? member.AvatarKind,
                AvatarPath = contact?.AvatarPath ?? member.AvatarPath,
                AvatarScale = contact?.AvatarScale ?? member.AvatarScale,
                AvatarOffsetX = contact?.AvatarOffsetX ?? member.AvatarOffsetX,
                AvatarOffsetY = contact?.AvatarOffsetY ?? member.AvatarOffsetY,
                AvatarVideoStartSeconds = contact?.AvatarVideoStartSeconds ?? member.AvatarVideoStartSeconds,
                AvatarVideoDurationSeconds = contact?.AvatarVideoDurationSeconds ?? member.AvatarVideoDurationSeconds,
                Status = string.Equals(member.UserId, _profile.UserId, StringComparison.Ordinal)
                    ? GetCurrentStatus()
                    : contact?.Status ?? UserPresenceStatus.Offline,
                IsOwner = string.Equals(member.UserId, group.GroupOwnerUserId, StringComparison.Ordinal),
                IsFriend = contact is not null,
                IsSelf = string.Equals(member.UserId, _profile.UserId, StringComparison.Ordinal),
                CanManageGroup = canManage
            });
        }
    }

    private async void GroupMemberAddFriendMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: GroupMemberViewModel { CanAddFriend: true } member })
        {
            return;
        }

        await AddRelayContactAsync(member.UserId, member.RelayServer);
        RefreshGroupMembersPanel();
    }

    private async void GroupMemberRenameContactMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: GroupMemberViewModel { CanRenameContact: true } member })
        {
            return;
        }

        var contact = _contacts.FirstOrDefault(x => x.UserId == member.UserId && !x.IsGroup);
        if (contact is null)
        {
            return;
        }

        var dialog = new RenameContactWindow(contact)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        contact.DisplayName = dialog.ContactName;
        await _history.SaveContactAsync(contact);
        RefreshGroupMembersPanel();
        NetworkStatusText.Text = $"Renamed contact to {contact.DisplayName}";
    }

    private async void GroupMemberWriteMessageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: GroupMemberViewModel { CanWriteMessage: true } member })
        {
            return;
        }

        var contact = _contacts.FirstOrDefault(x => x.UserId == member.UserId && !x.IsGroup);
        if (contact is not null)
        {
            SetGroupMembersPanelVisible(false);
            await OpenContactAsync(contact);
        }
    }

    private async void GroupMemberMakeOwnerMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: GroupMemberViewModel { CanMakeOwner: true } member } ||
            _selectedContact is not { IsGroup: true, CanEditGroup: true } group)
        {
            return;
        }

        group.GroupOwnerUserId = member.UserId;
        group.GroupVersion++;
        await _history.SaveContactAsync(group);
        await BroadcastGroupSnapshotAsync(group);
        RefreshGroupMembersPanel();
        NetworkStatusText.Text = $"{member.DisplayName} is now group owner";
    }

    private async void GroupMemberRemoveMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: GroupMemberViewModel { CanRemoveFromGroup: true } member } ||
            _selectedContact is not { IsGroup: true, CanEditGroup: true } group)
        {
            return;
        }

        var members = LoadGroupMembers(group);
        var removed = members.FirstOrDefault(x => string.Equals(x.UserId, member.UserId, StringComparison.Ordinal));
        members = members.Where(x => !string.Equals(x.UserId, member.UserId, StringComparison.Ordinal)).ToList();
        SaveGroupMembers(group, members);
        group.GroupVersion++;
        await _history.SaveContactAsync(group);
        await BroadcastGroupSnapshotAsync(group);
        if (removed is not null)
        {
            await SendGroupActionAsync(GroupKickIntent, new GroupActionPayload(group.UserId, group.GroupVersion, _profile?.UserId ?? "", member.UserId), removed);
        }

        RefreshGroupMembersPanel();
        NetworkStatusText.Text = $"Removed {member.DisplayName} from group";
    }

    private async void MessageInput_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendCurrentMessageAsync();
        }
    }

    private void MessageInput_OnTextChanged(object sender, TextChangedEventArgs e)
        => RefreshMessageInputEmojiPreview();

    private void MessageInput_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var normalized = NormalizeEmojiPresentation(e.Text);
        if (string.Equals(normalized, e.Text, StringComparison.Ordinal))
        {
            return;
        }

        e.Handled = true;
        InsertTextIntoMessageInput(normalized);
    }

    private void EmojiButton_OnClick(object sender, RoutedEventArgs e)
    {
        ForwardPanel.Visibility = Visibility.Collapsed;
        var show = EmojiPanel.Visibility != Visibility.Visible;
        SetPickerPanelVisible(EmojiPanel, show);
        if (show)
        {
            _ = InitializeEmojiWebViewAsync();
        }
    }

    private void PickerCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string panelName })
        {
            return;
        }

        SetPickerPanelVisible(GetPickerPanel(panelName), false);
    }

    private void SetPickerPanelVisible(Border panel, bool visible)
    {
        panel.BeginAnimation(OpacityProperty, null);
        panel.BeginAnimation(RenderTransformProperty, null);

        if (visible)
        {
            panel.Visibility = Visibility.Visible;
            panel.IsHitTestVisible = true;
            System.Windows.Controls.Panel.SetZIndex(panel, ++_pickerZIndex);
            panel.Opacity = 1;
            panel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1);
            panel.RenderTransform = scale;

            panel.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            var scaleAnimation = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation.Clone());
        }
        else
        {
            CommitPickerResize(panel);
            panel.Visibility = Visibility.Hidden;
            panel.IsHitTestVisible = false;
        }

        UpdatePickerWorkspace();
        if (visible)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    ArrangePickerPanels(
                        EmojiPanel.Visibility == Visibility.Visible && GifPanel.Visibility == Visibility.Visible,
                        animate: true);
                    if (EmojiPanel.Visibility == Visibility.Visible)
                    {
                        EnsureEmojiWebViewSurfaceSize();
                        var emojiRect = GetPickerRect(EmojiPanel);
                        QueueEmojiViewportUpdate(emojiRect.Width, emojiRect.Height);
                    }
                }),
                DispatcherPriority.Loaded);
        }
    }

    private void UpdatePickerWorkspace()
    {
        var hasVisiblePicker = EmojiPanel.Visibility == Visibility.Visible || GifPanel.Visibility == Visibility.Visible;
        PickerWorkspace.BeginAnimation(HeightProperty, null);
        PickerWorkspace.Visibility = Visibility.Visible;

        if (hasVisiblePicker)
        {
            var targetHeight = Math.Clamp(ActualHeight * 0.42, 240, PickerDefaultWorkspaceHeight);
            PickerWorkspace.Height = targetHeight;
            return;
        }

        PickerWorkspace.Height = 0;
    }

    private void PickerWorkspace_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5 || e.NewSize.Width <= 0)
        {
            return;
        }

        if (_pickerArrangePending)
        {
            return;
        }

        _pickerArrangePending = true;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _pickerArrangePending = false;
                ArrangePickerPanels(
                    EmojiPanel.Visibility == Visibility.Visible && GifPanel.Visibility == Visibility.Visible,
                    animate: false);
                if (EmojiPanel.Visibility == Visibility.Visible)
                {
                    EnsureEmojiWebViewSurfaceSize();
                    var emojiRect = GetPickerRect(EmojiPanel);
                    QueueEmojiViewportUpdate(emojiRect.Width, emojiRect.Height);
                }
            }),
            DispatcherPriority.Background);
    }

    private void ArrangePickerPanels(bool forcePairLayout, bool animate)
    {
        var workspaceWidth = PickerWorkspace.ActualWidth;
        var workspaceHeight = PickerWorkspace.Height;
        if (workspaceWidth <= 0 || workspaceHeight <= 0)
        {
            return;
        }

        var emojiVisible = EmojiPanel.Visibility == Visibility.Visible;
        var gifVisible = GifPanel.Visibility == Visibility.Visible;
        var pairMinWidth = Math.Min(PickerMinWidth, Math.Max(150, (workspaceWidth - PickerGap) / 2));
        EmojiPanel.MinWidth = emojiVisible && gifVisible ? pairMinWidth : Math.Min(PickerMinWidth, workspaceWidth);
        GifPanel.MinWidth = emojiVisible && gifVisible ? pairMinWidth : Math.Min(PickerMinWidth, workspaceWidth);
        EmojiPanel.MinHeight = Math.Min(PickerMinHeight, workspaceHeight);
        GifPanel.MinHeight = Math.Min(PickerMinHeight, workspaceHeight);

        if (emojiVisible && gifVisible && forcePairLayout)
        {
            var availableWidth = Math.Max(0, workspaceWidth - PickerGap);
            var gifWidth = Math.Clamp(
                Math.Min(GifPanel.Width, availableWidth * 0.55),
                pairMinWidth,
                Math.Max(pairMinWidth, availableWidth - pairMinWidth));
            var emojiWidth = Math.Max(pairMinWidth, availableWidth - gifWidth);
            gifWidth = Math.Max(pairMinWidth, availableWidth - emojiWidth);

            ApplyPickerRect(
                EmojiPanel,
                new Rect(0, 0, emojiWidth, Math.Min(Math.Max(EmojiPanel.Height, EmojiPanel.MinHeight), workspaceHeight)),
                animate);
            ApplyPickerRect(
                GifPanel,
                new Rect(emojiWidth + PickerGap, 0, gifWidth, Math.Min(Math.Max(GifPanel.Height, GifPanel.MinHeight), workspaceHeight)),
                animate);
            return;
        }

        if (emojiVisible)
        {
            ApplyPickerRect(EmojiPanel, ClampPickerRect(GetPickerRect(EmojiPanel), EmojiPanel), animate);
        }

        if (gifVisible)
        {
            ApplyPickerRect(GifPanel, ClampPickerRect(GetPickerRect(GifPanel), GifPanel), animate);
        }
    }

    private void PickerThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (!TryGetPickerDrag(sender, out var panel, out var action))
        {
            return;
        }

        var current = GetPickerRect(panel);
        ClearPickerRectAnimations(panel);
        ApplyPickerRect(panel, current, animate: false);
        if (!string.Equals(action, "Move", StringComparison.Ordinal))
        {
            _activePickerResizePanel = panel;
            _pickerResizeStartRect = current;
            _pendingPickerResizePanel = panel;
            _pendingPickerResizeRect = current;
            panel.BeginAnimation(RenderTransformProperty, null);
            panel.RenderTransformOrigin = new System.Windows.Point(0, 0);
            panel.RenderTransform = ReferenceEquals(panel, EmojiPanel)
                ? Transform.Identity
                : new ScaleTransform(1, 1);
            if (ReferenceEquals(panel, EmojiPanel))
            {
                EnsureEmojiWebViewSurfaceSize();
                QueueEmojiViewportUpdate(current.Width, current.Height);
            }
        }
        System.Windows.Controls.Panel.SetZIndex(panel, ++_pickerZIndex);
    }

    private void PickerThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!TryGetPickerDrag(sender, out var panel, out var action))
        {
            return;
        }

        var original = ReferenceEquals(_pendingPickerResizePanel, panel)
            ? _pendingPickerResizeRect
            : GetPickerRect(panel);
        var x = original.X;
        var y = original.Y;
        var width = original.Width;
        var height = original.Height;
        var changesLeft = action is "Left" or "TopLeft" or "BottomLeft";
        var changesRight = action is "Right" or "TopRight" or "BottomRight";
        var changesTop = action is "Top" or "TopLeft" or "TopRight";
        var changesBottom = action is "Bottom" or "BottomLeft" or "BottomRight";

        if (action == "Move")
        {
            x += e.HorizontalChange;
            y += e.VerticalChange;
        }
        else
        {
            if (changesLeft)
            {
                x += e.HorizontalChange;
                width -= e.HorizontalChange;
            }
            if (changesRight)
            {
                width += e.HorizontalChange;
            }
            if (changesTop)
            {
                y += e.VerticalChange;
                height -= e.VerticalChange;
            }
            if (changesBottom)
            {
                height += e.VerticalChange;
            }
        }

        var minWidth = Math.Min(panel.MinWidth, PickerWorkspace.ActualWidth);
        var minHeight = Math.Min(panel.MinHeight, PickerWorkspace.Height);
        if (width < minWidth)
        {
            if (changesLeft)
            {
                x -= minWidth - width;
            }
            width = minWidth;
        }
        if (height < minHeight)
        {
            if (changesTop)
            {
                y -= minHeight - height;
            }
            height = minHeight;
        }

        var proposed = ClampPickerRect(new Rect(x, y, width, height), panel);
        var other = ReferenceEquals(panel, EmojiPanel) ? GifPanel : EmojiPanel;
        if (other.Visibility == Visibility.Visible)
        {
            var protectedOtherRect = GetPickerRect(other);
            protectedOtherRect.Inflate(PickerGap, PickerGap);
            if (proposed.IntersectsWith(protectedOtherRect))
            {
                return;
            }
        }

        if (action == "Move")
        {
            ApplyPickerRect(panel, proposed, animate: false);
            return;
        }

        _pendingPickerResizePanel = panel;
        _pendingPickerResizeRect = proposed;
        ApplyPickerResizePreview(panel, proposed);
    }

    private void PickerThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.OriginalSource is not Thumb thumb || !TryGetPickerDrag(thumb, out var panel, out var action))
        {
            return;
        }

        if (!string.Equals(action, "Move", StringComparison.Ordinal))
        {
            CommitPickerResize(panel);
        }

        e.Handled = true;
    }

    private void ApplyPickerResizePreview(Border panel, Rect target)
    {
        if (!ReferenceEquals(_activePickerResizePanel, panel) ||
            _pickerResizeStartRect.Width <= 0 ||
            _pickerResizeStartRect.Height <= 0)
        {
            return;
        }

        if (ReferenceEquals(panel, EmojiPanel))
        {
            panel.RenderTransform = Transform.Identity;
            ApplyPickerRect(panel, target, animate: false);
            QueueEmojiViewportUpdate(target.Width, target.Height);
            return;
        }

        Canvas.SetLeft(panel, target.X);
        Canvas.SetTop(panel, target.Y);
        if (panel.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            panel.RenderTransform = scale;
        }

        scale.ScaleX = target.Width / _pickerResizeStartRect.Width;
        scale.ScaleY = target.Height / _pickerResizeStartRect.Height;
    }

    private void CommitPickerResize(Border panel)
    {
        if (!ReferenceEquals(_pendingPickerResizePanel, panel))
        {
            return;
        }

        var target = _pendingPickerResizeRect;
        panel.RenderTransform = Transform.Identity;
        _activePickerResizePanel = null;
        _pendingPickerResizePanel = null;
        ApplyPickerRect(panel, target, animate: false);
    }

    internal void RecoverFromWebViewCompositionResizeFault()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                ResetPickerResizePreview(EmojiPanel);
                ResetPickerResizePreview(GifPanel);
                _activePickerResizePanel = null;
                _pendingPickerResizePanel = null;
                EmojiPanel.IsHitTestVisible = false;
                GifPanel.IsHitTestVisible = false;
                EmojiPanel.Visibility = Visibility.Hidden;
                GifPanel.Visibility = Visibility.Hidden;
                UpdatePickerWorkspace();
                NetworkStatusText.Text = "Picker rendering recovered. Open it again.";
            }),
            DispatcherPriority.ContextIdle);
    }

    private void ResetPickerResizePreview(Border panel)
    {
        if (ReferenceEquals(_activePickerResizePanel, panel))
        {
            Canvas.SetLeft(panel, _pickerResizeStartRect.X);
            Canvas.SetTop(panel, _pickerResizeStartRect.Y);
        }

        panel.RenderTransform = Transform.Identity;
    }

    private bool TryGetPickerDrag(object sender, out Border panel, out string action)
    {
        panel = EmojiPanel;
        action = "";
        if (sender is not Thumb { Tag: string tag })
        {
            return false;
        }

        var separator = tag.IndexOf('|');
        if (separator <= 0 || separator >= tag.Length - 1)
        {
            return false;
        }

        panel = GetPickerPanel(tag[..separator]);
        action = tag[(separator + 1)..];
        return true;
    }

    private Border GetPickerPanel(string panelName)
        => panelName.Equals("Gif", StringComparison.OrdinalIgnoreCase) ? GifPanel : EmojiPanel;

    private Rect GetPickerRect(Border panel)
    {
        var x = Canvas.GetLeft(panel);
        var y = Canvas.GetTop(panel);
        if (double.IsNaN(x))
        {
            x = 0;
        }
        if (double.IsNaN(y))
        {
            y = 0;
        }

        var width = panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width;
        var height = panel.ActualHeight > 0 ? panel.ActualHeight : panel.Height;
        return new Rect(x, y, width, height);
    }

    private Rect ClampPickerRect(Rect rect, Border panel)
    {
        var workspaceWidth = Math.Max(0, PickerWorkspace.ActualWidth);
        var workspaceHeight = Math.Max(0, PickerWorkspace.Height);
        var minWidth = Math.Min(panel.MinWidth, workspaceWidth);
        var minHeight = Math.Min(panel.MinHeight, workspaceHeight);
        var width = Math.Clamp(rect.Width, minWidth, Math.Max(minWidth, workspaceWidth));
        var height = Math.Clamp(rect.Height, minHeight, Math.Max(minHeight, workspaceHeight));
        var x = Math.Clamp(rect.X, 0, Math.Max(0, workspaceWidth - width));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, workspaceHeight - height));
        return new Rect(x, y, width, height);
    }

    private static void ClearPickerRectAnimations(Border panel)
    {
        panel.BeginAnimation(Canvas.LeftProperty, null);
        panel.BeginAnimation(Canvas.TopProperty, null);
        panel.BeginAnimation(WidthProperty, null);
        panel.BeginAnimation(HeightProperty, null);
    }

    private static void ApplyPickerRect(Border panel, Rect rect, bool animate)
    {
        if (!double.IsFinite(rect.X) ||
            !double.IsFinite(rect.Y) ||
            !double.IsFinite(rect.Width) ||
            !double.IsFinite(rect.Height))
        {
            return;
        }

        rect = new Rect(
            Math.Round(Math.Max(0, rect.X)),
            Math.Round(Math.Max(0, rect.Y)),
            Math.Round(Math.Max(1, rect.Width)),
            Math.Round(Math.Max(1, rect.Height)));
        var from = new Rect(
            double.IsNaN(Canvas.GetLeft(panel)) ? 0 : Canvas.GetLeft(panel),
            double.IsNaN(Canvas.GetTop(panel)) ? 0 : Canvas.GetTop(panel),
            panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width,
            panel.ActualHeight > 0 ? panel.ActualHeight : panel.Height);
        ClearPickerRectAnimations(panel);
        Canvas.SetLeft(panel, rect.X);
        Canvas.SetTop(panel, rect.Y);
        if (Math.Abs(panel.Width - rect.Width) >= 0.5)
        {
            panel.Width = rect.Width;
        }
        if (Math.Abs(panel.Height - rect.Height) >= 0.5)
        {
            panel.Height = rect.Height;
        }

        if (!animate)
        {
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        panel.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(from.X, rect.X, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
        panel.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(from.Y, rect.Y, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing, FillBehavior = FillBehavior.Stop });
    }

    private void EmojiPickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        var emoji = sender switch
        {
            System.Windows.Controls.Button { Tag: EmojiItemViewModel item } => item.Symbol,
            System.Windows.Controls.Button { Content: string content } => content,
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(emoji))
        {
            return;
        }

        if (_reactionTarget is not null)
        {
            _ = AddReactionAsync(_reactionTarget, emoji);
            _reactionTarget = null;
            SetPickerPanelVisible(EmojiPanel, false);
            return;
        }

        InsertTextIntoMessageInput(emoji);
    }

    private Task InitializeEmojiWebViewAsync()
    {
        if (_emojiWebViewReady)
        {
            return Task.CompletedTask;
        }

        return _emojiWebViewInitializationTask ??= InitializeEmojiWebViewCoreAsync();
    }

    private void EnsureEmojiWebViewSurfaceSize()
    {
        var workspaceWidth = Math.Max(EmojiPanel.Width, PickerWorkspace.ActualWidth);
        var workspaceHeight = Math.Max(EmojiPanel.Height, PickerWorkspace.Height);
        var surfaceWidth = Math.Max(1, workspaceWidth - 16);
        var surfaceHeight = Math.Max(1, workspaceHeight - 54);
        if (double.IsFinite(surfaceWidth) &&
            (!double.IsFinite(EmojiWebView.Width) || Math.Abs(EmojiWebView.Width - surfaceWidth) >= 0.5))
        {
            EmojiWebView.Width = surfaceWidth;
        }
        if (double.IsFinite(surfaceHeight) &&
            (!double.IsFinite(EmojiWebView.Height) || Math.Abs(EmojiWebView.Height - surfaceHeight) >= 0.5))
        {
            EmojiWebView.Height = surfaceHeight;
        }
    }

    private void QueueEmojiViewportUpdate(double panelWidth, double panelHeight)
    {
        _emojiViewportWidth = Math.Max(180, panelWidth - 16);
        _emojiViewportHeight = Math.Max(120, panelHeight - 54);
        if (!_emojiWebViewReady || EmojiWebView.CoreWebView2 is null || _emojiViewportUpdatePending)
        {
            return;
        }

        _emojiViewportUpdatePending = true;
        Dispatcher.BeginInvoke(
            new Action(async () =>
            {
                _emojiViewportUpdatePending = false;
                if (!_emojiWebViewReady || EmojiWebView.CoreWebView2 is null)
                {
                    return;
                }

                var width = _emojiViewportWidth.ToString("0.##", CultureInfo.InvariantCulture);
                var height = _emojiViewportHeight.ToString("0.##", CultureInfo.InvariantCulture);
                try
                {
                    await EmojiWebView.CoreWebView2.ExecuteScriptAsync(
                        $"if (window.setPickerViewport) window.setPickerViewport({width}, {height});");
                }
                catch (Exception ex) when (ex is InvalidOperationException or COMException)
                {
                    AppLog.Write(ex, "Emoji viewport update failed");
                }
            }),
            DispatcherPriority.Render);
    }

    private async Task InitializeEmojiWebViewCoreAsync()
    {
        try
        {
            EnsureEmojiWebViewSurfaceSize();
            await EmojiWebView.EnsureCoreWebView2Async();
            if (_emojiWebViewReady)
            {
                return;
            }

            EmojiWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            EmojiWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            EmojiWebView.CoreWebView2.WebMessageReceived -= EmojiWebView_OnWebMessageReceived;
            EmojiWebView.CoreWebView2.WebMessageReceived += EmojiWebView_OnWebMessageReceived;
            EmojiWebView.CoreWebView2.NavigationCompleted -= EmojiWebView_OnNavigationCompleted;
            EmojiWebView.CoreWebView2.NavigationCompleted += EmojiWebView_OnNavigationCompleted;
            EmojiWebView.NavigateToString(BuildEmojiPickerHtml());
            _emojiWebViewReady = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException)
        {
            AppLog.Write(ex, "Emoji WebView initialization failed");
            NetworkStatusText.Text = "Emoji panel could not be initialized. Try opening it again.";
        }
        finally
        {
            if (!_emojiWebViewReady)
            {
                _emojiWebViewInitializationTask = null;
            }
        }
    }

    private void EmojiWebView_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            return;
        }

        var emojiRect = GetPickerRect(EmojiPanel);
        QueueEmojiViewportUpdate(emojiRect.Width, emojiRect.Height);
    }

    private void EmojiWebView_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (!doc.RootElement.TryGetProperty("type", out var typeElement) ||
                typeElement.GetString() != "emoji")
            {
                return;
            }

            var emoji = doc.RootElement.TryGetProperty("value", out var valueElement)
                ? valueElement.GetString() ?? ""
                : "";
            InsertEmoji(emoji);
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, "Emoji picker message parse failed");
        }
    }

    private void InsertEmoji(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return;
        }

        if (_reactionTarget is not null)
        {
            _ = AddReactionAsync(_reactionTarget, emoji);
            _reactionTarget = null;
            SetPickerPanelVisible(EmojiPanel, false);
            return;
        }

        InsertTextIntoMessageInput(emoji);
    }

    private void InsertTextIntoMessageInput(string text)
    {
        var normalized = NormalizeEmojiPresentation(text);
        MessageInput.Focus();
        MessageInput.SelectedText = normalized;
        MessageInput.CaretIndex += normalized.Length;
    }

    private void RefreshMessageInputEmojiPreview()
    {
        _messageInputTextSegments.Clear();
        foreach (var segment in MessageTextSegment.Build(MessageInput.Text, 14))
        {
            _messageInputTextSegments.Add(segment);
        }
    }

    private static string NormalizeEmojiPresentation(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            var needsEmojiPresentation = IsEmojiPresentationCandidate(current) &&
                                         next != '\ufe0e' &&
                                         next != '\ufe0f';
            if (!needsEmojiPresentation)
            {
                builder?.Append(current);
                continue;
            }

            builder ??= new StringBuilder(text.Length + 2).Append(text, 0, i);
            builder.Append(current);
            builder.Append('\ufe0f');
        }

        return builder?.ToString() ?? text;
    }

    private static bool IsEmojiPresentationCandidate(char value)
        => value is '\u203c' or '\u2049' or '\u2122' or '\u2139' or '\u3030' or '\u303d' or '\u3297' or '\u3299' ||
           value is >= '\u2600' and <= '\u27bf';

    private void GifButton_OnClick(object sender, RoutedEventArgs e)
    {
        ForwardPanel.Visibility = Visibility.Collapsed;
        SetPickerPanelVisible(GifPanel, GifPanel.Visibility != Visibility.Visible);
    }

    private async void GifSearchButton_OnClick(object sender, RoutedEventArgs e)
        => await SearchGifsAsync(GifSearchInput.Text.Trim());

    private async void GifSearchInput_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SearchGifsAsync(GifSearchInput.Text.Trim());
    }

    private static string BuildEmojiPickerHtml()
    {
        var emojisJson = JsonSerializer.Serialize(AllEmojis());
        return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    * { box-sizing: border-box; }
    html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: #202225; color: #f2f3f5; font-family: "Segoe UI", sans-serif; }
    body { padding: 10px; display: flex; flex-direction: column; }
    input { width: 100%; height: 34px; border: 1px solid #45474f; border-radius: 6px; background: #303239; color: #f2f3f5; padding: 0 10px; outline: none; font-family: "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", "Segoe UI", sans-serif; font-size: 18px; }
    input:focus { border-color: #5865f2; }
    .tabs { display: flex; gap: 6px; overflow-x: auto; padding: 8px 0; }
    .tab { border: 0; border-radius: 6px; background: #303239; color: #d6d9df; padding: 7px 10px; cursor: pointer; font-weight: 600; white-space: nowrap; }
    .tab.active, .tab:hover { background: #5865f2; color: #fff; }
    #grid { flex: 1; min-height: 0; overflow-y: auto; display: grid; grid-template-columns: repeat(auto-fill, 42px); grid-auto-rows: 42px; align-content: start; justify-content: start; gap: 4px; padding-right: 2px; }
    .emoji { width: 42px; height: 38px; border: 0; border-radius: 7px; background: transparent; cursor: pointer; font-family: "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", sans-serif; font-size: 23px; line-height: 38px; }
    .emoji:hover { background: #343741; }
    .empty { grid-column: 1 / -1; color: #b5bac1; font-size: 12px; padding: 20px 6px; }
    ::-webkit-scrollbar { width: 8px; height: 8px; }
    ::-webkit-scrollbar-thumb { background: #45474f; border-radius: 8px; }
  </style>
</head>
<body>
  <input id="search" placeholder="Search emoji" autocomplete="off">
  <div id="tabs" class="tabs"></div>
  <div id="grid"></div>
  <script>
    const emojis = {{emojisJson}};
    const categories = ['Recent', 'Smileys', 'People', 'Nature', 'Food', 'Activity', 'Objects', 'Symbols'];
    let category = 'Recent';
    const tabs = document.getElementById('tabs');
    const grid = document.getElementById('grid');
    const search = document.getElementById('search');
    window.setPickerViewport = (width, height) => {
      document.body.style.width = Math.max(180, Number(width) || 180) + 'px';
      document.body.style.height = Math.max(120, Number(height) || 120) + 'px';
    };
    function postEmoji(value) {
      if (window.chrome && chrome.webview) chrome.webview.postMessage({ type: 'emoji', value });
    }
    function renderTabs() {
      tabs.innerHTML = '';
      for (const name of categories) {
        const button = document.createElement('button');
        button.className = 'tab' + (name === category ? ' active' : '');
        button.textContent = name;
        button.onclick = () => { category = name; search.value = ''; render(); };
        tabs.appendChild(button);
      }
    }
    function render() {
      renderTabs();
      const query = search.value.trim().toLowerCase();
      const filtered = emojis.filter(e => query ? e.SearchText.toLowerCase().includes(query) : (category === 'Recent' ? emojis.indexOf(e) < 48 : e.Category === category)).slice(0, 120);
      grid.innerHTML = '';
      if (!filtered.length) {
        const empty = document.createElement('div');
        empty.className = 'empty';
        empty.textContent = 'No emoji found';
        grid.appendChild(empty);
        return;
      }
      for (const emoji of filtered) {
        const button = document.createElement('button');
        button.className = 'emoji';
        button.textContent = emoji.Symbol;
        button.title = emoji.Name;
        button.onclick = () => postEmoji(emoji.Symbol);
        grid.appendChild(button);
      }
    }
    function withEmojiPresentation(value) {
      return value.replace(/([\u203C-\u3299])(?!(\uFE0E|\uFE0F))/gu, '$1\uFE0F');
    }
    search.addEventListener('input', () => {
      const normalized = withEmojiPresentation(search.value);
      if (normalized !== search.value) {
        const caret = search.selectionStart || normalized.length;
        search.value = normalized;
        search.setSelectionRange(Math.min(caret + 1, normalized.length), Math.min(caret + 1, normalized.length));
      }
      render();
    });
    render();
  </script>
</body>
</html>
""";
    }

    private static Uri? TryBuildTwemojiPngUrl(string emoji)
    {
        var codepoints = emoji.EnumerateRunes()
            .Where(rune => rune.Value != 0xFE0F)
            .Select(rune => rune.Value.ToString("x", CultureInfo.InvariantCulture))
            .ToArray();
        if (codepoints.Length == 0)
        {
            return null;
        }

        return new Uri($"https://cdn.jsdelivr.net/gh/jdecked/twemoji@latest/assets/72x72/{string.Join("-", codepoints)}.png");
    }

    private static IReadOnlyList<EmojiItemViewModel> AllEmojis()
        =>
        [
            Emoji("👍", "thumbs up like approve", "Recent"),
            Emoji("❤️", "heart love", "Recent"),
            Emoji("😂", "joy laugh tears", "Recent"),
            Emoji("🔥", "fire hot", "Recent"),
            Emoji("🎉", "party celebrate", "Recent"),
            Emoji("😎", "cool sunglasses", "Recent"),
            Emoji("🙏", "pray please thanks", "Recent"),
            Emoji("✅", "check done", "Recent"),
            Emoji("👀", "eyes look", "Recent"),
            Emoji("💀", "skull dead laugh", "Recent"),
            Emoji("😀", "grinning smile", "Smileys"),
            Emoji("😃", "smile happy", "Smileys"),
            Emoji("😄", "smile happy eyes", "Smileys"),
            Emoji("😁", "beaming grin", "Smileys"),
            Emoji("😆", "laugh squint", "Smileys"),
            Emoji("🤣", "rolling laugh", "Smileys"),
            Emoji("🙂", "slight smile", "Smileys"),
            Emoji("😉", "wink", "Smileys"),
            Emoji("😊", "blush smile", "Smileys"),
            Emoji("😍", "heart eyes love", "Smileys"),
            Emoji("😘", "kiss", "Smileys"),
            Emoji("😋", "yum tasty", "Smileys"),
            Emoji("🤔", "thinking", "Smileys"),
            Emoji("🤨", "raised eyebrow", "Smileys"),
            Emoji("😐", "neutral", "Smileys"),
            Emoji("😴", "sleep", "Smileys"),
            Emoji("😢", "cry sad", "Smileys"),
            Emoji("😭", "sob crying", "Smileys"),
            Emoji("😡", "angry mad", "Smileys"),
            Emoji("😳", "flushed", "Smileys"),
            Emoji("🥳", "party face", "Smileys"),
            Emoji("🤯", "mind blown", "Smileys"),
            Emoji("🤝", "handshake", "People"),
            Emoji("👏", "clap applause", "People"),
            Emoji("🙌", "raised hands", "People"),
            Emoji("💪", "strong flex", "People"),
            Emoji("🫡", "salute", "People"),
            Emoji("🤌", "pinched fingers", "People"),
            Emoji("👌", "ok hand", "People"),
            Emoji("✌️", "peace", "People"),
            Emoji("👋", "wave hello", "People"),
            Emoji("🤦", "facepalm", "People"),
            Emoji("🐱", "cat", "Nature"),
            Emoji("🐶", "dog", "Nature"),
            Emoji("🌙", "moon night", "Nature"),
            Emoji("⭐", "star", "Nature"),
            Emoji("⚡", "lightning", "Nature"),
            Emoji("❄️", "snow", "Nature"),
            Emoji("🌈", "rainbow", "Nature"),
            Emoji("🌊", "wave water", "Nature"),
            Emoji("🍕", "pizza", "Food"),
            Emoji("🍔", "burger", "Food"),
            Emoji("🍟", "fries", "Food"),
            Emoji("🍣", "sushi", "Food"),
            Emoji("🍩", "donut", "Food"),
            Emoji("🍪", "cookie", "Food"),
            Emoji("☕", "coffee", "Food"),
            Emoji("🥤", "drink", "Food"),
            Emoji("🎮", "game controller", "Activity"),
            Emoji("🎧", "headphones music", "Activity"),
            Emoji("🎬", "movie", "Activity"),
            Emoji("🏆", "trophy win", "Activity"),
            Emoji("⚽", "soccer ball", "Activity"),
            Emoji("🏀", "basketball", "Activity"),
            Emoji("🚗", "car", "Objects"),
            Emoji("💻", "laptop computer", "Objects"),
            Emoji("📱", "phone", "Objects"),
            Emoji("📷", "camera", "Objects"),
            Emoji("💡", "idea light", "Objects"),
            Emoji("🎁", "gift", "Objects"),
            Emoji("🔒", "lock", "Objects"),
            Emoji("📌", "pin", "Objects"),
            Emoji("💯", "hundred", "Symbols"),
            Emoji("✨", "sparkles", "Symbols"),
            Emoji("❗", "exclamation", "Symbols"),
            Emoji("❓", "question", "Symbols"),
            Emoji("🚫", "blocked no", "Symbols"),
            Emoji("⬆️", "up arrow", "Symbols"),
            Emoji("⬇️", "down arrow", "Symbols"),
            Emoji("🔁", "repeat", "Symbols")
        ];

    private static EmojiItemViewModel Emoji(string symbol, string name, string category)
        => new() { Symbol = symbol, Name = name, Category = category };

    private async void GifItemButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: TenorGifViewModel gif })
        {
            return;
        }

        SetPickerPanelVisible(GifPanel, false);
        await SendRichMessageAsync(MessageKinds.Gif, "", attachmentUrl: gif.GifUrl, replyTarget: _replyTarget);
    }

    private async void MessageGifWebView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl webView)
        {
            await EnsureMessageGifAsync(webView);
        }
    }

    private async void MessageGifWebView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl { IsLoaded: true } webView)
        {
            await EnsureMessageGifAsync(webView);
        }
    }

    private async Task EnsureMessageGifAsync(Microsoft.Web.WebView2.Wpf.WebView2CompositionControl webView)
    {
        if (webView.DataContext is not MessageViewModel { IsGifMessage: true } message ||
            string.IsNullOrWhiteSpace(message.AttachmentUrl))
        {
            return;
        }

        var gifUrl = message.AttachmentUrl;

        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.WebMessageReceived -= MessageGifWebView_OnWebMessageReceived;
            webView.CoreWebView2.WebMessageReceived += MessageGifWebView_OnWebMessageReceived;
            _messageGifViews[webView.CoreWebView2] = new WeakReference<Microsoft.Web.WebView2.Wpf.WebView2CompositionControl>(webView);

            if (_messageGifDimensions.TryGetValue(gifUrl, out var cachedDimensions))
            {
                ApplyMessageGifDimensions(message, cachedDimensions);
            }

            if (string.Equals(webView.Tag as string, gifUrl, StringComparison.Ordinal))
            {
                webView.Visibility = Visibility.Visible;
                return;
            }

            webView.Tag = gifUrl;
            webView.Visibility = Visibility.Hidden;
            webView.NavigateToString(BuildMessageGifHtml(gifUrl));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or COMException or NotSupportedException)
        {
            webView.Visibility = Visibility.Visible;
            AppLog.Write(ex, $"Message GIF WebView failed: url={gifUrl}");
        }
    }

    private void MessageGifWebView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.Web.WebView2.Wpf.WebView2CompositionControl webView)
        {
            return;
        }

        try
        {
            if (webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.WebMessageReceived -= MessageGifWebView_OnWebMessageReceived;
                _messageGifViews.Remove(webView.CoreWebView2);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            AppLog.Write(ex, "Message GIF WebView unload failed");
        }
    }

    private void MessageGifWebView_OnWebMessageReceived(
        object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (sender is not Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            return;
        }

        if (!_messageGifViews.TryGetValue(core, out var reference) ||
            !reference.TryGetTarget(out var webView) ||
            webView.DataContext is not MessageViewModel { IsGifMessage: true } message ||
            !string.Equals(webView.Tag as string, message.AttachmentUrl, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var dimensions = JsonSerializer.Deserialize<GifRenderDimensions>(e.WebMessageAsJson);
            if (dimensions is null || dimensions.Width <= 0 || dimensions.Height <= 0)
            {
                return;
            }

            _messageGifDimensions[message.AttachmentUrl] = dimensions;
            ApplyMessageGifDimensions(message, dimensions);
            webView.Visibility = Visibility.Visible;
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, "Message GIF dimensions could not be read");
        }
    }

    private void ApplyMessageGifDimensions(MessageViewModel message, GifRenderDimensions dimensions)
    {
        var maxWidth = Math.Min(420d, Math.Max(180d, MessagesList.ActualWidth - 120d));
        var maxHeight = Math.Min(220d, Math.Max(120d, MessagesList.ActualHeight * 0.45d));
        var scale = Math.Min(1d, Math.Min(maxWidth / dimensions.Width, maxHeight / dimensions.Height));
        message.SetGifRenderSize(
            Math.Max(96, Math.Round(dimensions.Width * scale)),
            Math.Max(72, Math.Round(dimensions.Height * scale)));
    }

    private sealed record GifRenderDimensions(double Width, double Height);

    private void RoundedMedia_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            element.Clip = new RectangleGeometry(new Rect(e.NewSize), 10, 10);
        }
    }

    private static string BuildMessageGifHtml(string gifUrl)
    {
        var safeUrl = System.Net.WebUtility.HtmlEncode(gifUrl);
        return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
  <style>
    html, body {
      width: 100%;
      height: 100%;
      margin: 0;
      overflow: hidden;
      background: transparent;
    }

    body {
      display: flex;
      align-items: flex-start;
      justify-content: flex-start;
    }

    img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: contain;
      object-position: left top;
      border-radius: 10px;
      border: 0;
      background: transparent;
    }
  </style>
</head>
<body>
  <img id="gif" src="{{safeUrl}}" alt="">
  <script>
    const gif = document.getElementById('gif');
    gif.addEventListener('load', () => {
      chrome.webview.postMessage({ Width: gif.naturalWidth, Height: gif.naturalHeight });
    });
  </script>
</body>
</html>
""";
    }

    private void GifFavoriteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: TenorGifViewModel gif })
        {
            return;
        }

        ToggleFavoriteGif(gif);
        e.Handled = true;
    }

    private void AttachImageButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|All files|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        StageImageDraft(dialog.FileName);
    }

    private void ClearImageDraftButton_OnClick(object sender, RoutedEventArgs e)
        => ClearImageDraft();

    private void ComposerPanel_OnPreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = TryGetImagePathFromDrop(e.Data, out _)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void ComposerPanel_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (TryGetImagePathFromDrop(e.Data, out var path))
        {
            StageImageDraft(path);
            e.Handled = true;
        }
    }

    private async void MessageInput_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendCurrentMessageAsync();
            return;
        }

        if (e.Handled || e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (await TryStageImageFromClipboardAsync())
        {
            e.Handled = true;
        }
    }

    private void StageImageDraft(string path)
    {
        if (!IsSupportedImagePath(path) || !File.Exists(path))
        {
            NetworkStatusText.Text = "Unsupported image file.";
            return;
        }

        _draftImagePath = path;
        var preview = AvatarImageLoader.Load(path);
        if (preview is null)
        {
            NetworkStatusText.Text = "Could not load image preview.";
            return;
        }

        ImageDraftPreview.Source = preview;
        ImageDraftBar.Visibility = Visibility.Visible;
        MessageInput.Focus();
        NetworkStatusText.Text = "Image added. Type a caption and press Send.";
    }

    private void ClearImageDraft()
    {
        _draftImagePath = "";
        ImageDraftPreview.Source = null;
        ImageDraftBar.Visibility = Visibility.Collapsed;
    }

    private async Task<bool> TryStageImageFromClipboardAsync()
    {
        if (_selectedContact is null || ComposerPanel.Visibility != Visibility.Visible)
        {
            return false;
        }

        try
        {
            var dataObject = Clipboard.GetDataObject();
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                return await StageClipboardBitmapSourceAsync(image);
            }

            if (dataObject is not null)
            {
                if (await TryStageClipboardStreamImageAsync(dataObject, "PNG", ".png") ||
                    await TryStageClipboardStreamImageAsync(dataObject, "Portable Network Graphics", ".png") ||
                    await TryStageClipboardBitmapDataAsync(dataObject))
                {
                    return true;
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                foreach (var item in Clipboard.GetFileDropList())
                {
                    var path = item?.ToString() ?? "";
                    if (!IsSupportedImagePath(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    StageImageDraft(path);
                    return true;
                }
            }

            if (dataObject is not null)
            {
                AppLog.Write($"Clipboard image paste skipped: formats={string.Join(", ", dataObject.GetFormats(autoConvert: true))}");
            }
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            AppLog.Write(ex, "Stage image from clipboard failed");
            NetworkStatusText.Text = "Could not read image from clipboard.";
        }

        return false;
    }

    private async Task<bool> StageClipboardBitmapSourceAsync(BitmapSource image)
    {
        AppPaths.EnsureAttachmentsDirectoryCreated();
        var path = Path.Combine(AppPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}.png");
        await using (var stream = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
        }

        StageImageDraft(path);
        return true;
    }

    private async Task<bool> TryStageClipboardStreamImageAsync(System.Windows.IDataObject dataObject, string format, string extension)
    {
        if (!dataObject.GetDataPresent(format, autoConvert: true))
        {
            return false;
        }

        var data = dataObject.GetData(format, autoConvert: true);
        if (data is Stream stream)
        {
            AppPaths.EnsureAttachmentsDirectoryCreated();
            var path = Path.Combine(AppPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}{extension}");
            await using (var output = File.Create(path))
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                await stream.CopyToAsync(output);
            }

            StageImageDraft(path);
            return true;
        }

        if (data is byte[] bytes)
        {
            AppPaths.EnsureAttachmentsDirectoryCreated();
            var path = Path.Combine(AppPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}{extension}");
            await File.WriteAllBytesAsync(path, bytes);
            StageImageDraft(path);
            return true;
        }

        return false;
    }

    private async Task<bool> TryStageClipboardBitmapDataAsync(System.Windows.IDataObject dataObject)
    {
        if (!dataObject.GetDataPresent(System.Windows.DataFormats.Bitmap, autoConvert: true))
        {
            return false;
        }

        var data = dataObject.GetData(System.Windows.DataFormats.Bitmap, autoConvert: true);
        if (data is BitmapSource bitmapSource)
        {
            return await StageClipboardBitmapSourceAsync(bitmapSource);
        }

        if (data is Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            memory.Position = 0;
            AppPaths.EnsureAttachmentsDirectoryCreated();
            var path = Path.Combine(AppPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}.png");
            await using (var output = File.Create(path))
            {
                await memory.CopyToAsync(output);
            }

            StageImageDraft(path);
            return true;
        }

        return false;
    }

    private static bool TryGetImagePathFromDrop(System.Windows.IDataObject data, out string path)
    {
        path = "";
        if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
            data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        path = files.FirstOrDefault(file => File.Exists(file) && IsSupportedImagePath(file)) ?? "";
        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool IsSupportedImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearReplyEditButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearReplyTarget();
        ClearEditingMessage();
    }

    private void MessageReplyMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is { } message)
        {
            SetReplyTarget(message);
        }
    }

    private void MessageForwardMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is not { } message)
        {
            return;
        }

        BeginForwardMessage(message);
    }

    private void BeginForwardMessage(MessageViewModel message)
    {
        _forwardTarget = message;
        SetPickerPanelVisible(EmojiPanel, false);
        SetPickerPanelVisible(GifPanel, false);
        ForwardPanel.Visibility = Visibility.Visible;
    }

    private void MessageEditMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is not { CanEdit: true } message)
        {
            return;
        }

        _editingMessage = message;
        MessageInput.Text = message.Text;
        ReplyEditTitleText.Text = "Editing message";
        ReplyEditPreviewText.Text = message.Text;
        ReplyEditBar.Visibility = Visibility.Visible;
        MessageInput.Focus();
        MessageInput.CaretIndex = MessageInput.Text.Length;
    }

    private void MessageAddReactionMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is not { } message)
        {
            return;
        }

        _reactionTarget = message;
        ForwardPanel.Visibility = Visibility.Collapsed;
        SetPickerPanelVisible(EmojiPanel, true);
        _ = InitializeEmojiWebViewAsync();
    }

    private async void ReactionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: ReactionViewModel { IsMine: true } reaction } ||
            _profile is null ||
            _selectedContact is null)
        {
            return;
        }

        var message = FindMessage(reaction.MessageId);
        if (message is null)
        {
            return;
        }

        await RemoveReactionAsync(message);
        e.Handled = true;
    }

    private async void MessageCopyTextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is { } message &&
            await TrySetClipboardTextAsync(message.Text))
        {
            NetworkStatusText.Text = "Message text copied";
        }
    }

    private async void MessageDeleteMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is not { CanDelete: true } message ||
            _profile is null ||
            _selectedContact is null)
        {
            return;
        }

        await DeleteOwnMessageAsync(message);
    }

    private void MessageCopyImageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is { IsImageMessage: true } message)
        {
            CopyMessageImage(message);
        }
    }

    private void CopyMessageImage(MessageViewModel message)
    {
        if (string.IsNullOrWhiteSpace(message.AttachmentPath) ||
            !File.Exists(message.AttachmentPath) ||
            AvatarImageLoader.Load(message.AttachmentPath) is not BitmapSource image)
        {
            NetworkStatusText.Text = "Could not copy image.";
            return;
        }

        try
        {
            Clipboard.SetImage(image);
            NetworkStatusText.Text = "Image copied";
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            AppLog.Write(ex, "Image could not be copied to clipboard");
            NetworkStatusText.Text = "Could not copy image.";
        }
    }

    private void MessageSaveImageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromActionSender(sender) is { IsImageMessage: true } message)
        {
            SaveMessageImage(message);
        }
    }

    private void SaveMessageImage(MessageViewModel message)
    {
        if (string.IsNullOrWhiteSpace(message.AttachmentPath) || !File.Exists(message.AttachmentPath))
        {
            NetworkStatusText.Text = "Could not save image.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Path.GetFileName(message.AttachmentPath),
            Filter = "Image|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.Copy(message.AttachmentPath, dialog.FileName, overwrite: true);
            NetworkStatusText.Text = "Image saved";
        }
    }

    private void MessageImage_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image { Tag: MessageViewModel { IsImageMessage: true } message })
        {
            return;
        }

        OpenImageViewer(message);
        e.Handled = true;
    }

    private void OpenImageViewer(MessageViewModel message)
    {
        if (string.IsNullOrWhiteSpace(message.AttachmentPath) ||
            !File.Exists(message.AttachmentPath) ||
            AvatarImageLoader.Load(message.AttachmentPath) is not BitmapSource image)
        {
            NetworkStatusText.Text = "Could not open image.";
            return;
        }

        _imageViewerMessage = message;
        _imageViewerSourceWidth = Math.Max(1, image.Width);
        _imageViewerSourceHeight = Math.Max(1, image.Height);
        ImageViewerImage.Source = image;
        ImageViewerOverlay.BeginAnimation(OpacityProperty, null);
        ImageViewerOverlay.Opacity = 1;
        ImageViewerOverlay.Visibility = Visibility.Visible;
        ForwardPanel.Visibility = Visibility.Collapsed;
        SetPickerPanelVisible(EmojiPanel, false);
        SetPickerPanelVisible(GifPanel, false);

        ImageViewerOverlay.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                var availableWidth = Math.Max(120, ImageViewerOverlay.ActualWidth - 80);
                var availableHeight = Math.Max(120, ImageViewerOverlay.ActualHeight - 130);
                _imageViewerFitZoom = Math.Min(
                    1,
                    Math.Min(availableWidth / _imageViewerSourceWidth, availableHeight / _imageViewerSourceHeight));
                ApplyImageViewerZoom(_imageViewerFitZoom);
                ImageViewerScrollViewer.ScrollToHorizontalOffset(0);
                ImageViewerScrollViewer.ScrollToVerticalOffset(0);
            }),
            DispatcherPriority.Loaded);
    }

    private void ImageViewerImage_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_imageViewerMessage is null || ImageViewerImage.ActualWidth <= 0 || ImageViewerImage.ActualHeight <= 0)
        {
            return;
        }

        var position = e.GetPosition(ImageViewerImage);
        var horizontalRatio = Math.Clamp(position.X / ImageViewerImage.ActualWidth, 0, 1);
        var verticalRatio = Math.Clamp(position.Y / ImageViewerImage.ActualHeight, 0, 1);
        var resetToFit = _imageViewerZoom >= 0.999;
        var nextZoom = resetToFit
            ? _imageViewerFitZoom
            : Math.Min(1, _imageViewerZoom + 0.25);
        ApplyImageViewerZoom(nextZoom);

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (resetToFit)
                {
                    ImageViewerScrollViewer.ScrollToHorizontalOffset(0);
                    ImageViewerScrollViewer.ScrollToVerticalOffset(0);
                    return;
                }

                ImageViewerScrollViewer.ScrollToHorizontalOffset(
                    Math.Max(0, horizontalRatio * ImageViewerImage.ActualWidth - ImageViewerScrollViewer.ViewportWidth / 2));
                ImageViewerScrollViewer.ScrollToVerticalOffset(
                    Math.Max(0, verticalRatio * ImageViewerImage.ActualHeight - ImageViewerScrollViewer.ViewportHeight / 2));
            }),
            DispatcherPriority.Background);
        e.Handled = true;
    }

    private void ImageViewerOverlay_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var current = e.OriginalSource as DependencyObject;
        while (current is not null && !ReferenceEquals(current, ImageViewerOverlay))
        {
            if (ReferenceEquals(current, ImageViewerImage) ||
                ReferenceEquals(current, ImageViewerToolbar) ||
                current is System.Windows.Controls.Primitives.ScrollBar or Thumb)
            {
                return;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        CloseImageViewer();
        e.Handled = true;
    }

    private void ImageViewerImage_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ImageViewerMagnifierCursor.Visibility = Visibility.Visible;
        UpdateImageViewerMagnifierPosition(e);
    }

    private void ImageViewerImage_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ImageViewerMagnifierCursor.Visibility = Visibility.Visible;
        UpdateImageViewerMagnifierPosition(e);
    }

    private void ImageViewerImage_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => ImageViewerMagnifierCursor.Visibility = Visibility.Collapsed;

    private void UpdateImageViewerMagnifierPosition(System.Windows.Input.MouseEventArgs e)
    {
        var position = e.GetPosition(ImageViewerCursorLayer);
        Canvas.SetLeft(ImageViewerMagnifierCursor, position.X - 10);
        Canvas.SetTop(ImageViewerMagnifierCursor, position.Y - 10);
    }

    private void ApplyImageViewerZoom(double zoom)
    {
        _imageViewerZoom = Math.Clamp(zoom, Math.Min(_imageViewerFitZoom, 1), 1);
        ImageViewerImage.Width = Math.Max(1, _imageViewerSourceWidth * _imageViewerZoom);
        ImageViewerImage.Height = Math.Max(1, _imageViewerSourceHeight * _imageViewerZoom);
    }

    private void ImageViewerCloseButton_OnClick(object sender, RoutedEventArgs e)
        => CloseImageViewer();

    private void CloseImageViewer()
    {
        ImageViewerOverlay.BeginAnimation(OpacityProperty, null);
        ImageViewerOverlay.Visibility = Visibility.Collapsed;
        ImageViewerMagnifierCursor.Visibility = Visibility.Collapsed;
        ImageViewerImage.Source = null;
        ImageViewerImage.Width = double.NaN;
        ImageViewerImage.Height = double.NaN;
        _imageViewerMessage = null;
        _imageViewerZoom = 1;
        _imageViewerFitZoom = 1;
    }

    private void ImageViewerForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_imageViewerMessage is not { } message)
        {
            return;
        }

        CloseImageViewer();
        BeginForwardMessage(message);
    }

    private void ImageViewerSaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_imageViewerMessage is { } message)
        {
            SaveMessageImage(message);
        }
    }

    private void ImageViewerCopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_imageViewerMessage is { } message)
        {
            CopyMessageImage(message);
        }
    }

    private async void ForwardContactsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_forwardTarget is null ||
            ForwardContactsList.SelectedItem is not ContactViewModel contact ||
            contact.IsGroup ||
            _profile is null)
        {
            return;
        }

        await SendForwardedMessageAsync(contact, _forwardTarget);
        _forwardTarget = null;
        ForwardPanel.Visibility = Visibility.Collapsed;
        NetworkStatusText.Text = $"Forwarded to {contact.DisplayName}";
    }

    private async Task SearchGifsAsync(string query)
    {
        _settings.TenorApiKey = SettingsTenorApiKeyInput.Text.Trim();
        await AppSettingsStore.SaveAsync(_settings);
        if (string.IsNullOrWhiteSpace(query))
        {
            GifStatusText.Text = "Type something to search GIFs.";
            return;
        }

        try
        {
            GifStatusText.Text = "Searching...";
            _gifResults.Clear();

            SearchBuiltInGifs(query);

            try
            {
                await SearchGiphyGifsAsync(query);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                AppLog.Write(ex, "Giphy GIF search failed, keeping built-in catalog");
            }

            if (!string.IsNullOrWhiteSpace(_settings.TenorApiKey))
            {
                try
                {
                    await SearchTenorGifsAsync(query, _settings.TenorApiKey.Trim());
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    AppLog.Write(ex, "Tenor GIF search failed, falling back to Wikimedia");
                }
            }

            GifStatusText.Text = _gifResults.Count == 0
                ? "No GIFs found."
                : $"{_gifResults.Count} GIFs found.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Write(ex, "GIF search failed");
            GifStatusText.Text = $"GIF search failed: {ex.Message}";
        }
    }

    private async Task SearchGiphyGifsAsync(string query)
    {
        const string publicBetaKey = "dc6zaTOxFJmzC";
        var url = $"https://api.giphy.com/v1/gifs/search?api_key={publicBetaKey}&q={Uri.EscapeDataString(query)}&limit=50&rating=g";
        using var response = await _httpClient.GetAsync(url, _stop.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(_stop.Token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: _stop.Token);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");
            var title = item.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString() ?? "GIF"
                : "GIF";

            if (!item.TryGetProperty("images", out var images))
            {
                continue;
            }

            if (!TryReadGiphyImageUrl(images, "original", out var gifUrl) &&
                !TryReadGiphyImageUrl(images, "fixed_height", out gifUrl))
            {
                continue;
            }

            if (!TryReadGiphyImageUrl(images, "fixed_width_small_still", out var previewUrl) &&
                !TryReadGiphyImageUrl(images, "fixed_width_small", out previewUrl))
            {
                previewUrl = gifUrl;
            }

            AddGifResult($"giphy:{id}", title, gifUrl, previewUrl);
        }
    }

    private static bool TryReadGiphyImageUrl(JsonElement images, string format, out string url)
    {
        url = "";
        if (!images.TryGetProperty(format, out var image) ||
            !image.TryGetProperty("url", out var urlElement))
        {
            return false;
        }

        url = urlElement.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(url);
    }

    private async Task SearchTenorGifsAsync(string query, string apiKey)
    {
        var url = $"https://tenor.googleapis.com/v2/search?q={Uri.EscapeDataString(query)}&key={Uri.EscapeDataString(apiKey)}&client_key=fluxchat&limit=50&media_filter=gif,tinygif";
        using var response = await _httpClient.GetAsync(url, _stop.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(_stop.Token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: _stop.Token);

        if (!doc.RootElement.TryGetProperty("results", out var results))
        {
            return;
        }

        foreach (var result in results.EnumerateArray())
        {
            var id = result.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
            var title = result.TryGetProperty("content_description", out var titleElement) ? titleElement.GetString() ?? "GIF" : "GIF";
            if (!TryReadTenorMediaUrl(result, "gif", out var gifUrl))
            {
                continue;
            }

            if (!TryReadTenorMediaUrl(result, "tinygif", out var previewUrl))
            {
                previewUrl = gifUrl;
            }

            AddGifResult(id, title, gifUrl, previewUrl);
        }
    }

    private async Task SearchWikimediaGifsAsync(string query)
    {
        var search = $"{query} gif";
        var url = "https://commons.wikimedia.org/w/api.php" +
                  "?action=query&generator=search&gsrnamespace=6&gsrlimit=36" +
                  $"&gsrsearch={Uri.EscapeDataString(search)}" +
                  "&prop=imageinfo&iiprop=url|mime&format=json&origin=*";
        using var response = await _httpClient.GetAsync(url, _stop.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(_stop.Token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: _stop.Token);

        if (!doc.RootElement.TryGetProperty("query", out var queryElement) ||
            !queryElement.TryGetProperty("pages", out var pages))
        {
            return;
        }

        foreach (var page in pages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("imageinfo", out var imageInfo) ||
                imageInfo.ValueKind != JsonValueKind.Array ||
                imageInfo.GetArrayLength() == 0)
            {
                continue;
            }

            var info = imageInfo[0];
            var mime = info.TryGetProperty("mime", out var mimeElement) ? mimeElement.GetString() ?? "" : "";
            var gifUrl = info.TryGetProperty("url", out var urlElement) ? urlElement.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(gifUrl) ||
                (!mime.Equals("image/gif", StringComparison.OrdinalIgnoreCase) &&
                 !gifUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var title = page.Value.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString() ?? "GIF"
                : "GIF";
            AddGifResult($"commons:{page.Name}", title.Replace("File:", "", StringComparison.OrdinalIgnoreCase), gifUrl, gifUrl);
            if (_gifResults.Count >= 24)
            {
                break;
            }
        }
    }

    private void SearchBuiltInGifs(string query)
    {
        var words = query.Split([' ', ',', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = BuiltInGifs
            .Where(gif => words.Length == 0 || words.Any(word => gif.SearchText.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(50)
            .ToArray();
        if (matches.Length == 0)
        {
            matches = BuiltInGifs.Take(50).ToArray();
        }

        foreach (var gif in matches)
        {
            AddGifResult($"builtin:{gif.Id}", gif.Title, gif.Url, gif.Url);
        }
    }

    private void AddGifResult(string id, string title, string gifUrl, string previewUrl)
    {
        if (_gifResults.Any(x => x.GifUrl.Equals(gifUrl, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _gifResults.Add(new TenorGifViewModel
        {
            Id = id,
            Title = title,
            GifUrl = gifUrl,
            PreviewUrl = previewUrl,
            IsFavorite = _favoriteGifs.Any(x => x.Id == id || x.GifUrl.Equals(gifUrl, StringComparison.OrdinalIgnoreCase))
        });
    }

    private sealed record BuiltInGifItem(string Id, string Title, string Url, string SearchText);

    private static readonly BuiltInGifItem[] BuiltInGifs =
    [
        new("cat-typing", "Typing cat", "https://media.giphy.com/media/JIX9t2j0ZTN9S/giphy.gif", "cat typing work keyboard funny a"),
        new("excited", "Excited", "https://media.giphy.com/media/5GoVLqeAOo6PK/giphy.gif", "excited happy yes wow reaction a"),
        new("thumbs-up", "Thumbs up", "https://media.giphy.com/media/111ebonMs90YLu/giphy.gif", "yes ok okay thumbs up approve good like"),
        new("wow", "Wow", "https://media.giphy.com/media/26ufdipQqU2lhNA4g/giphy.gif", "wow surprised shock omg reaction"),
        new("hello", "Hello", "https://media.giphy.com/media/ASd0Ukj0y3qMM/giphy.gif", "hello hi wave привет"),
        new("dance", "Dance", "https://media.giphy.com/media/l0MYt5jPR6QX5pnqM/giphy.gif", "dance party happy fun"),
        new("love", "Love", "https://media.giphy.com/media/MDJ9IbxxvDUQM/giphy.gif", "love heart hug cat мило"),
        new("fire", "Fire", "https://media.giphy.com/media/yr7n0u3qzO9nG/giphy.gif", "fire hot cool круто"),
        new("laugh", "Laugh", "https://media.giphy.com/media/10JhviFuU2gWD6/giphy.gif", "laugh lol funny haha ахаха"),
        new("facepalm", "Facepalm", "https://media.giphy.com/media/3o7btPCcdNniyf0ArS/giphy.gif", "facepalm bruh fail no"),
        new("clap", "Clap", "https://media.giphy.com/media/nbvFVPiEiJH6JOGIok/giphy.gif", "clap applause bravo good"),
        new("thinking", "Thinking", "https://media.giphy.com/media/3o7TKTDn976rzVgky4/giphy.gif", "thinking hmm think question"),
        new("cry", "Cry", "https://media.giphy.com/media/OPU6wzx8JrHna/giphy.gif", "cry sad tears"),
        new("no", "No", "https://media.giphy.com/media/3o7TKwmnDgQb5jemjK/giphy.gif", "no nope deny нет"),
        new("party", "Party", "https://media.giphy.com/media/blSTtZehjAZ8I/giphy.gif", "party celebrate праздник"),
        new("ok", "OK", "https://media.giphy.com/media/26gsvAm8UPaczzXz2/giphy.gif", "ok okay yes good")
    ];

    private static bool TryReadTenorMediaUrl(JsonElement result, string format, out string url)
    {
        url = "";
        if (result.TryGetProperty("media_formats", out var formats) &&
            formats.TryGetProperty(format, out var media) &&
            media.TryGetProperty("url", out var urlElement))
        {
            url = urlElement.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(url);
        }

        if (result.TryGetProperty("media", out var mediaArray) &&
            mediaArray.ValueKind == JsonValueKind.Array &&
            mediaArray.GetArrayLength() > 0 &&
            mediaArray[0].TryGetProperty(format, out var legacyMedia) &&
            legacyMedia.TryGetProperty("url", out var legacyUrlElement))
        {
            url = legacyUrlElement.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(url);
        }

        return false;
    }

    private void ToggleFavoriteGif(TenorGifViewModel gif)
    {
        var existing = _favoriteGifs.FirstOrDefault(x => x.Id == gif.Id);
        if (existing is null)
        {
            gif.IsFavorite = true;
            _favoriteGifs.Add(new TenorGifViewModel
            {
                Id = gif.Id,
                Title = gif.Title,
                PreviewUrl = gif.PreviewUrl,
                GifUrl = gif.GifUrl,
                IsFavorite = true
            });
        }
        else
        {
            _favoriteGifs.Remove(existing);
            gif.IsFavorite = false;
        }

        foreach (var result in _gifResults.Where(x => x.Id == gif.Id))
        {
            result.IsFavorite = _favoriteGifs.Any(x => x.Id == gif.Id);
        }

        SaveFavoriteGifs();
    }

    private void LoadFavoriteGifs()
    {
        _favoriteGifs.Clear();
        try
        {
            if (!File.Exists(AppPaths.GifFavoritesPath))
            {
                return;
            }

            var json = File.ReadAllText(AppPaths.GifFavoritesPath);
            var gifs = JsonSerializer.Deserialize<List<TenorGifViewModel>>(json) ?? [];
            foreach (var gif in gifs)
            {
                gif.IsFavorite = true;
                _favoriteGifs.Add(gif);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write(ex, "GIF favorites load failed");
        }
    }

    private void SaveFavoriteGifs()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.GifFavoritesPath, JsonSerializer.Serialize(_favoriteGifs, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException ex)
        {
            AppLog.Write(ex, "GIF favorites save failed");
        }
    }

    private static MessageViewModel? GetMessageFromActionSender(object sender)
        => sender switch
        {
            FrameworkElement { DataContext: MessageViewModel message } => message,
            _ => null
        };

    private void SetReplyTarget(MessageViewModel message)
    {
        _replyTarget = message;
        _editingMessage = null;
        ReplyEditTitleText.Text = "Replying";
        ReplyEditPreviewText.Text = message.PreviewText;
        ReplyEditBar.Visibility = Visibility.Visible;
        MessageInput.Focus();
    }

    private void ClearReplyTarget()
    {
        _replyTarget = null;
        if (_editingMessage is null)
        {
            ReplyEditBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearEditingMessage()
    {
        _editingMessage = null;
        if (_replyTarget is null)
        {
            ReplyEditBar.Visibility = Visibility.Collapsed;
        }
    }

    private async Task FinishEditingMessageAsync(string text)
    {
        if (_selectedContact is null || _editingMessage is null)
        {
            return;
        }

        var message = _editingMessage;
        MessageInput.Clear();
        message.Text = text;
        message.EditedAtUtc = DateTimeOffset.UtcNow;
        await _history.UpdateMessageAsync(message);
        var payload = new ChatEditPayload(message.MessageId, text, message.EditedAtUtc.Value);
        var packet = CreateProfilePacket(_selectedContact.UserId, JsonSerializer.Serialize(payload), ChatEditIntent);
        await SendOverRelayAsync(packet, _selectedContact);
        ClearEditingMessage();
    }

    private async Task AddReactionAsync(MessageViewModel message, string emoji)
    {
        if (_profile is null || _selectedContact is null)
        {
            return;
        }

        ApplyReaction(message, _profile.UserId, emoji);
        await _history.UpdateMessageAsync(message);
        var payload = new ChatReactionPayload(message.MessageId, _profile.UserId, emoji);
        var packet = CreateProfilePacket(_selectedContact.UserId, JsonSerializer.Serialize(payload), ChatReactionIntent);
        await SendOverRelayAsync(packet, _selectedContact);
    }

    private async Task RemoveReactionAsync(MessageViewModel message)
    {
        if (_profile is null || _selectedContact is null)
        {
            return;
        }

        ApplyReaction(message, _profile.UserId, "");
        await _history.UpdateMessageAsync(message);
        var payload = new ChatReactionPayload(message.MessageId, _profile.UserId, "");
        var packet = CreateProfilePacket(_selectedContact.UserId, JsonSerializer.Serialize(payload), ChatReactionIntent);
        await SendOverRelayAsync(packet, _selectedContact);
    }

    private async Task DeleteOwnMessageAsync(MessageViewModel message)
    {
        if (_selectedContact is null || !message.IsOutgoing)
        {
            return;
        }

        _messages.Remove(message);
        DeleteAttachmentFileIfOwned(message.AttachmentPath);
        await _history.DeleteMessageAsync(message.MessageId);
        var payload = new ChatDeletePayload(message.MessageId);
        var packet = CreateProfilePacket(_selectedContact.UserId, JsonSerializer.Serialize(payload), ChatDeleteIntent);
        await SendOverRelayAsync(packet, _selectedContact);
        NetworkStatusText.Text = "Message deleted";
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_profile is null || _selectedContact is null)
        {
            return;
        }

        if (_editingMessage is not null)
        {
            var editBody = MessageInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(editBody))
            {
                return;
            }

            await FinishEditingMessageAsync(editBody);
            return;
        }

        var body = MessageInput.Text.Trim();
        if (!string.IsNullOrWhiteSpace(_draftImagePath))
        {
            if (_selectedContact.IsGroup)
            {
                NetworkStatusText.Text = "Images are not supported in group chats yet.";
                return;
            }

            var imagePath = _draftImagePath;
            MessageInput.Clear();
            ClearImageDraft();
            await SendRichMessageAsync(MessageKinds.Image, body, imagePath, replyTarget: _replyTarget);
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (_selectedContact.IsGroup)
        {
            await SendGroupMessageAsync(_selectedContact, body);
            return;
        }

        AppLog.Write($"Send requested: to={_selectedContact.UserId}, ip={_selectedContact.IpAddress}, port={_selectedContact.MessagePort}, bodyLength={body.Length}");
        MessageInput.Clear();
        var payload = CreateRichPayload(MessageKinds.Text, body, "", "", null, _replyTarget, "");
        var packet = CreateProfilePacket(_selectedContact.UserId, JsonSerializer.Serialize(payload), ChatRichIntent);
        var message = new MessageViewModel
        {
            MessageId = packet.MessageId,
            PeerUserId = _selectedContact.UserId,
            Body = body,
            Text = body,
            IsOutgoing = true,
            SentAtUtc = packet.SentAtUtc,
            Kind = MessageKinds.Text,
            ReplyToMessageId = _replyTarget?.MessageId,
            ReplyPreview = _replyTarget?.PreviewText ?? ""
        };
        PrepareMessageForUi(message);
        ClearReplyTarget();

        _messages.Add(message);
        ScrollMessagesToEnd();
        await _history.SaveAsync(message);

        try
        {
            await SendOverRelayAsync(packet, _selectedContact);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Send failed: messageId={packet.MessageId}, to={_selectedContact.UserId}, ip={_selectedContact.IpAddress}:{_selectedContact.MessagePort}");
            var failedMessage = new MessageViewModel
            {
                MessageId = Guid.NewGuid(),
                PeerUserId = _selectedContact.UserId,
                Body = $"Failed to send: {ex.Message}",
                Text = $"Failed to send: {ex.Message}",
                IsOutgoing = false,
                SentAtUtc = DateTimeOffset.UtcNow
            };
            PrepareMessageForUi(failedMessage);
            _messages.Add(failedMessage);
            ScrollMessagesToEnd();
        }
    }

    private async Task SendGroupMessageAsync(ContactViewModel group, string body)
    {
        if (_profile is null || !group.IsGroup)
        {
            return;
        }

        MessageInput.Clear();
        var message = new MessageViewModel
        {
            MessageId = Guid.NewGuid(),
            PeerUserId = group.UserId,
            Body = body,
            Text = body,
            IsOutgoing = true,
            SentAtUtc = DateTimeOffset.UtcNow
        };
        PrepareMessageForUi(message);

        _messages.Add(message);
        ScrollMessagesToEnd();
        await _history.SaveAsync(message);

        var sent = 0;
        foreach (var member in LoadGroupMembers(group).Where(x => _profile is null || !string.Equals(x.UserId, _profile.UserId, StringComparison.Ordinal)))
        {
            try
            {
                var contact = _contacts.FirstOrDefault(x => x.UserId == member.UserId && !x.IsGroup)
                              ?? CreateContactFromGroupMember(member);
                var payload = new GroupMessagePayload(group.UserId, body);
                var packet = CreateProfilePacket(member.UserId, JsonSerializer.Serialize(payload), GroupMessageIntent, member.RelayServer);
                await SendOverRelayAsync(packet, contact, log: false);
                sent++;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Group message send failed: group={group.UserId}, to={member.UserId}");
            }
        }

        NetworkStatusText.Text = $"Group message sent to {sent}/{Math.Max(0, group.GroupMemberCount - 1)}";
    }

    private async Task SendRichMessageAsync(
        string kind,
        string text,
        string attachmentPath = "",
        string attachmentUrl = "",
        MessageViewModel? replyTarget = null,
        string forwardedFrom = "")
    {
        if (_profile is null || _selectedContact is null || _selectedContact.IsGroup)
        {
            return;
        }

        var attachmentBase64 = "";
        var attachmentFileName = "";
        var storedAttachmentPath = attachmentPath;
        if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath))
        {
            storedAttachmentPath = CopyAttachmentIntoStore(attachmentPath);
            attachmentBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(storedAttachmentPath));
            attachmentFileName = Path.GetFileName(storedAttachmentPath);
        }

        var payload = CreateRichPayload(kind, text, attachmentFileName, attachmentBase64, attachmentUrl, replyTarget, forwardedFrom);
        var packet = CreateProfilePacket(_selectedContact.UserId, JsonSerializer.Serialize(payload), ChatRichIntent);
        var message = new MessageViewModel
        {
            MessageId = packet.MessageId,
            PeerUserId = _selectedContact.UserId,
            Body = text,
            Text = text,
            IsOutgoing = true,
            SentAtUtc = packet.SentAtUtc,
            Kind = kind,
            AttachmentPath = storedAttachmentPath,
            AttachmentUrl = attachmentUrl,
            ReplyToMessageId = replyTarget?.MessageId,
            ReplyPreview = replyTarget?.PreviewText ?? "",
            ForwardedFrom = forwardedFrom
        };
        PrepareMessageForUi(message);

        _messages.Add(message);
        ScrollMessagesToEnd();
        await _history.SaveAsync(message);
        await SendOverRelayAsync(packet, _selectedContact);
        ClearReplyTarget();
    }

    private async Task SendForwardedMessageAsync(ContactViewModel contact, MessageViewModel source)
    {
        if (_profile is null)
        {
            return;
        }

        var attachmentBase64 = "";
        var attachmentFileName = "";
        if (!string.IsNullOrWhiteSpace(source.AttachmentPath) && File.Exists(source.AttachmentPath))
        {
            attachmentBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(source.AttachmentPath));
            attachmentFileName = Path.GetFileName(source.AttachmentPath);
        }

        var payload = CreateRichPayload(source.Kind, source.Text, attachmentFileName, attachmentBase64, source.AttachmentUrl, null, _profile.DisplayName);
        var packet = CreateProfilePacket(contact.UserId, JsonSerializer.Serialize(payload), ChatRichIntent);
        var message = new MessageViewModel
        {
            MessageId = packet.MessageId,
            PeerUserId = contact.UserId,
            Body = source.Text,
            Text = source.Text,
            IsOutgoing = true,
            SentAtUtc = packet.SentAtUtc,
            Kind = source.Kind,
            AttachmentPath = source.AttachmentPath,
            AttachmentUrl = source.AttachmentUrl,
            ForwardedFrom = _profile.DisplayName
        };
        PrepareMessageForUi(message);

        await _history.SaveAsync(message);
        if (_selectedContact?.UserId == contact.UserId)
        {
            _messages.Add(message);
            ScrollMessagesToEnd();
        }

        await SendOverRelayAsync(packet, contact);
    }

    private RichChatPayload CreateRichPayload(
        string kind,
        string text,
        string attachmentFileName,
        string attachmentBase64,
        string? attachmentUrl,
        MessageViewModel? replyTarget,
        string forwardedFrom)
        => new(
            kind,
            text,
            attachmentFileName,
            attachmentBase64,
            attachmentUrl ?? "",
            replyTarget?.MessageId,
            replyTarget?.PreviewText ?? "",
            forwardedFrom);

    private static string CopyAttachmentIntoStore(string sourcePath)
    {
        AppPaths.EnsureAttachmentsDirectoryCreated();
        var extension = Path.GetExtension(sourcePath);
        var destination = Path.Combine(AppPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private static string SaveIncomingAttachment(string fileName, string base64)
    {
        AppPaths.EnsureAttachmentsDirectoryCreated();
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var destination = Path.Combine(AppPaths.AttachmentsDirectory, $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(destination, Convert.FromBase64String(base64));
        return destination;
    }

    private async void HandleIncomingRichMessage(ChatPacket packet, string statusText)
    {
        RichChatPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RichChatPayload>(packet.Body);
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, $"Rich chat payload parse failed: messageId={packet.MessageId}");
            return;
        }

        if (payload is null)
        {
            return;
        }

        var attachmentPath = "";
        if (!string.IsNullOrWhiteSpace(payload.AttachmentBase64))
        {
            try
            {
                attachmentPath = SaveIncomingAttachment(payload.AttachmentFileName, payload.AttachmentBase64);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Incoming attachment save failed: messageId={packet.MessageId}");
            }
        }

        var message = new MessageViewModel
        {
            MessageId = packet.MessageId,
            PeerUserId = packet.FromUserId,
            Body = payload.Text,
            Text = payload.Text,
            IsOutgoing = false,
            SentAtUtc = packet.SentAtUtc,
            Kind = string.IsNullOrWhiteSpace(payload.Kind) ? MessageKinds.Text : payload.Kind,
            AttachmentPath = attachmentPath,
            AttachmentUrl = payload.AttachmentUrl,
            ReplyToMessageId = payload.ReplyToMessageId,
            ReplyPreview = payload.ReplyPreview,
            ForwardedFrom = payload.ForwardedFrom,
            SenderUserId = packet.FromUserId,
            SenderDisplayName = packet.FromDisplayName
        };
        PrepareMessageForUi(message);
        try
        {
            await _history.SaveAsync(message);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            AppLog.Write(ex, $"Incoming rich message save failed: messageId={packet.MessageId}");
        }

        var contact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId);
        if (contact is null)
        {
            UpsertFriendRequest(packet);
            _ = RequestProfileFromPacketAsync(packet);
            NetworkStatusText.Text = $"Message request from {packet.FromDisplayName}";
            ShowIncomingNotificationIfNeeded(packet.FromDisplayName, packet);
            return;
        }

        ApplyPacketProfileToContact(packet, contact);
        _ = _history.SaveContactAsync(contact);
        RequestProfileIfAvatarMissing(packet, contact);
        if (_selectedContact?.UserId == packet.FromUserId)
        {
            _messages.Add(message);
            ScrollMessagesToEnd();
        }

        NetworkStatusText.Text = $"{statusText}: {contact.DisplayName}";
        ShowIncomingNotificationIfNeeded(contact.DisplayName, packet);
    }

    private void HandleIncomingChatEdit(ChatPacket packet)
    {
        if (!TryDeserializePayload<ChatEditPayload>(packet.Body, out var payload) || payload is null)
        {
            return;
        }

        var message = FindMessage(payload.MessageId);
        if (message is null)
        {
            return;
        }

        message.Text = payload.Text;
        message.EditedAtUtc = payload.EditedAtUtc;
        _ = _history.UpdateMessageAsync(message);
    }

    private void HandleIncomingChatReaction(ChatPacket packet)
    {
        if (!TryDeserializePayload<ChatReactionPayload>(packet.Body, out var payload) || payload is null)
        {
            return;
        }

        var message = FindMessage(payload.MessageId);
        if (message is null)
        {
            return;
        }

        ApplyReaction(message, payload.UserId, payload.Emoji);
        _ = _history.UpdateMessageAsync(message);
    }

    private async void HandleIncomingChatDelete(ChatPacket packet)
    {
        try
        {
            if (!TryDeserializePayload<ChatDeletePayload>(packet.Body, out var payload) || payload is null)
            {
                return;
            }

            var message = FindMessage(payload.MessageId);
            var attachmentPath = "";
            if (message is not null)
            {
                if (message.IsOutgoing ||
                    !message.PeerUserId.Equals(packet.FromUserId, StringComparison.Ordinal))
                {
                    return;
                }

                attachmentPath = message.AttachmentPath;
                _messages.Remove(message);
            }

            if (string.IsNullOrWhiteSpace(attachmentPath))
            {
                attachmentPath = await _history.LoadMessageAttachmentPathAsync(payload.MessageId, packet.FromUserId, isOutgoing: false);
            }

            DeleteAttachmentFileIfOwned(attachmentPath);
            await _history.DeleteIncomingMessageAsync(payload.MessageId, packet.FromUserId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
        {
            AppLog.Write(ex, $"Incoming message delete failed: from={packet.FromUserId}, messageId={packet.MessageId}");
        }
    }

    private static bool TryDeserializePayload<T>(string body, out T? payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<T>(body);
            return payload is not null;
        }
        catch (JsonException)
        {
            payload = default;
            return false;
        }
    }

    private MessageViewModel? FindMessage(Guid messageId)
        => _messages.FirstOrDefault(x => x.MessageId == messageId);

    private void PrepareMessageForUi(MessageViewModel message)
    {
        message.CurrentUserId = _profile?.UserId ?? "";
        ApplyMessageSenderMetadata(message);
        if (message.IsGifMessage &&
            !string.IsNullOrWhiteSpace(message.AttachmentUrl) &&
            _messageGifDimensions.TryGetValue(message.AttachmentUrl, out var dimensions))
        {
            ApplyMessageGifDimensions(message, dimensions);
        }
    }

    private void ApplyMessageSenderMetadata(MessageViewModel message)
    {
        var senderId = message.SenderUserId;
        if (string.IsNullOrWhiteSpace(senderId))
        {
            senderId = message.IsOutgoing
                ? _profile?.UserId ?? ""
                : message.PeerUserId;
            message.SenderUserId = senderId;
        }

        if (_profile is not null && string.Equals(senderId, _profile.UserId, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(message.SenderDisplayName))
            {
                message.SenderDisplayName = _profile.DisplayName;
            }

            message.SenderAvatarKind = string.IsNullOrWhiteSpace(_profile.AvatarKind) ? "color" : _profile.AvatarKind;
            message.SenderAvatarPath = _profile.AvatarPath;
            return;
        }

        var contact = _contacts.FirstOrDefault(x => !x.IsGroup && string.Equals(x.UserId, senderId, StringComparison.Ordinal));
        if (contact is not null)
        {
            if (string.IsNullOrWhiteSpace(message.SenderDisplayName))
            {
                message.SenderDisplayName = contact.DisplayName;
            }

            message.SenderAvatarKind = contact.AvatarKind;
            message.SenderAvatarPath = contact.AvatarPath;
            return;
        }

        var group = _contacts.FirstOrDefault(x => x.IsGroup && string.Equals(x.UserId, message.PeerUserId, StringComparison.Ordinal))
                    ?? (_selectedContact?.IsGroup == true && string.Equals(_selectedContact.UserId, message.PeerUserId, StringComparison.Ordinal)
                        ? _selectedContact
                        : null);
        var member = group is null
            ? null
            : LoadGroupMembers(group).FirstOrDefault(x => string.Equals(x.UserId, senderId, StringComparison.Ordinal));
        if (member is not null)
        {
            if (string.IsNullOrWhiteSpace(message.SenderDisplayName))
            {
                message.SenderDisplayName = member.DisplayName;
            }

            message.SenderAvatarKind = member.AvatarKind;
            message.SenderAvatarPath = member.AvatarPath;
            return;
        }

        if (string.IsNullOrWhiteSpace(message.SenderDisplayName))
        {
            message.SenderDisplayName = senderId.Length <= 12 ? senderId : senderId[..12];
        }
    }

    private static void ApplyReaction(MessageViewModel message, string userId, string emoji)
    {
        var reactions = string.IsNullOrWhiteSpace(message.ReactionsJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(message.ReactionsJson) ?? new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(emoji))
        {
            reactions.Remove(userId);
        }
        else
        {
            reactions[userId] = emoji;
        }

        message.ReactionsJson = reactions.Count == 0 ? "" : JsonSerializer.Serialize(reactions);
    }

    private async Task SendOverRelayAsync(
        ChatPacket packet,
        ContactViewModel contact,
        bool log = true,
        CancellationToken cancellationToken = default)
    {
        var relayServer = NormalizeRelayServer(_settings.RelayServer);
        if (_relayClient is null || !_relayClient.IsConnected)
        {
            await ConnectRelayAsync(relayServer);
        }
        else if (!string.Equals(_relayClient.ConnectedServer, relayServer, StringComparison.OrdinalIgnoreCase))
        {
            await ConnectRelayAsync(relayServer);
        }

        if (_relayClient is null || !_relayClient.IsConnected)
        {
            throw new InvalidOperationException("VPS server is not connected.");
        }

        await _relayClient.SendAsync(packet, cancellationToken, log);
        if (log && string.IsNullOrWhiteSpace(packet.Intent))
        {
            NetworkStatusText.Text = string.IsNullOrWhiteSpace(packet.ToRelayServer)
                ? $"Last message sent over VPS {relayServer}"
                : $"Last message sent via {relayServer} to {packet.ToRelayServer}";
        }
        if (log)
        {
            AppLog.Write($"Send succeeded: messageId={packet.MessageId}, channel=VPS, server={relayServer}, targetServer={packet.ToRelayServer}, to={packet.ToUserId}");
        }
    }

    private async Task SendCallSignalAsync(ContactViewModel contact, string intent)
    {
        try
        {
            await SendCallControlAsync(contact, intent, "", log: true);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call signal failed: intent={intent}, to={contact.UserId}");
            NetworkStatusText.Text = $"Call failed: {ex.Message}";
        }
    }

    private async Task SendCallAudioStateAsync()
    {
        if (_activeCallContact is null)
        {
            return;
        }

        try
        {
            var body = JsonSerializer.Serialize(new CallAudioState(_isMicrophoneMuted, _isHeadphonesMuted));
            await SendCallControlAsync(_activeCallContact, CallAudioStateIntent, body, log: false);
            AppLog.Write($"Call audio state sent: to={_activeCallContact.UserId}, micMuted={_isMicrophoneMuted}, headphonesMuted={_isHeadphonesMuted}");
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call audio state failed: to={_activeCallContact?.UserId}");
        }
    }

    private async Task SendScreenShareSignalAsync(string intent, string body = "")
    {
        if (_activeCallContact is null)
        {
            return;
        }

        try
        {
            await SendCallControlAsync(_activeCallContact, intent, body, log: false);
            AppLog.Write($"Screen share signal sent: intent={intent}, to={_activeCallContact.UserId}, bodyLength={body.Length}");
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Screen share signal failed: intent={intent}, to={_activeCallContact?.UserId}");
        }
    }

    private async Task SendCallControlAsync(ContactViewModel contact, string intent, string body, bool log)
    {
        if (!contact.IsGroup)
        {
            var packet = CreateCallPacket(contact, body, intent);
            await SendOverRelayAsync(packet, contact, log);
            AppLog.Write($"Call signal sent: intent={intent}, to={contact.UserId}, bodyLength={packet.Body.Length}, targetRelay={packet.ToRelayServer}");
            return;
        }

        var groupBody = JsonSerializer.Serialize(new CallGroupSignalPayload(contact.UserId, body));
        var sent = 0;
        foreach (var member in GetGroupCallTargets(contact))
        {
            var target = _contacts.FirstOrDefault(x => !x.IsGroup && string.Equals(x.UserId, member.UserId, StringComparison.Ordinal))
                         ?? CreateContactFromGroupMember(member);
            var packet = CreateProfilePacket(member.UserId, groupBody, intent, member.RelayServer);
            await SendOverRelayAsync(packet, target, log);
            sent++;
        }

        AppLog.Write($"Group call signal sent: intent={intent}, group={contact.UserId}, targets={sent}, bodyLength={groupBody.Length}");
    }

    private IReadOnlyList<GroupMemberPayload> GetGroupCallTargets(ContactViewModel group)
        => _profile is null
            ? []
            : LoadGroupMembers(group)
                .Where(x => !string.Equals(x.UserId, _profile.UserId, StringComparison.Ordinal))
                .ToArray();

    private void RefreshScreenShareSources()
    {
        _screenShareSources.Clear();

        var previousStageVisibility = CallScreenShareStage.Visibility;
        var hideStageForPreviews = previousStageVisibility == Visibility.Visible;
        if (hideStageForPreviews)
        {
            CallScreenShareStage.Visibility = Visibility.Hidden;
            CallScreenShareStage.UpdateLayout();
        }

        try
        {
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                _screenShareSources.Add(CreateScreenShareSource(
                    $"Entire screen {(_screenShareSources.Count + 1)}",
                    $"{bounds.Width}x{bounds.Height}",
                    true,
                    IntPtr.Zero,
                    bounds));
            }

            foreach (var window in EnumerateShareableWindows())
            {
                _screenShareSources.Add(CreateScreenShareSource(
                    window.Title,
                    window.Description,
                    false,
                    window.WindowHandle,
                    window.Bounds));
            }
        }
        finally
        {
            if (hideStageForPreviews)
            {
                CallScreenShareStage.Visibility = previousStageVisibility;
                CallScreenShareStage.UpdateLayout();
            }
        }

        ApplyScreenShareSourceFilter();
    }

    private static ScreenShareSourceItem CreateDefaultWebRtcScreenShareSource()
    {
        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.FirstOrDefault();
        var bounds = screen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        return new ScreenShareSourceItem(
            "Screen picker",
            $"{bounds.Width}x{bounds.Height}",
            true,
            IntPtr.Zero,
            bounds,
            null);
    }

    private ScreenShareSourceItem CreateScreenShareSource(
        string title,
        string description,
        bool isScreen,
        IntPtr windowHandle,
        Rectangle bounds)
    {
        var source = new ScreenShareSourceItem(title, description, isScreen, windowHandle, bounds, null);
        return source with { Preview = CreateScreenSharePreviewImage(source) };
    }

    private BitmapImage? CreateScreenSharePreviewImage(ScreenShareSourceItem source)
    {
        try
        {
            var jpeg = CaptureScreenShareFrame(source, 120);
            return jpeg is null ? null : CreateBitmapImage(jpeg);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Screen share preview failed: source={source.Title}");
            return null;
        }
    }

    private void StartScreenShare(ScreenShareSourceItem source)
    {
        if (_activeCallContact is null || _profile is null)
        {
            return;
        }

        StopScreenShare(sendSignal: false);
        _activeScreenShareSource = source;
        _isScreenSharing = true;
        _isWatchingPeerScreen = _peerScreenSharing;
        ApplyScreenShareVoiceProtectionIfNeeded();
        _screenShareAdaptiveHeight = GetInitialScreenShareAdaptiveHeight();
        Interlocked.Exchange(ref _sentScreenShareFrames, 0);
        Interlocked.Exchange(ref _sentEncodedScreenShareChunks, 0);
        Interlocked.Exchange(ref _pendingScreenShareFrame, 0);
        Interlocked.Exchange(ref _pendingNativeWebRtcWebViewFrame, 0);
        Interlocked.Exchange(ref _screenShareJpegLoopStarted, 0);
        Interlocked.Exchange(ref _lastSelfScreenSharePreviewTicks, 0);
        Interlocked.Exchange(ref _lastEncodedScreenSharePreviewTicks, 0);
        _lastScreenShareFrameHash = 0;
        _lastScreenShareFrameHashSentTicks = 0;
        _screenShareUsingNativeWebRtc = false;
        _screenShareUsingEncodedWebRtc = false;
        _screenShareEncodedChannelOpen = false;
        _screenShareStartSignalSent = false;
        _screenShareStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);

        UpdateScreenShareControlVisuals();
        UpdateScreenShareStageVisibility();

        var useWebRtc = IsScreenShareWebRtcPreferred() && ScreenShareWebView.CoreWebView2 is not null;
        if (useWebRtc && ShouldUseCompatibleScreenShareForSimultaneousStart())
        {
            useWebRtc = false;
            ApplyCompatibleScreenShareFallbackQuality();
            UpdateScreenSharePickerState();
            NetworkStatusText.Text = "Using compatible screen share while both screens are active.";
            AppLog.Write($"Screen share WebRTC skipped for simultaneous share: peerWebRtc={_peerScreenShareUsingWebRtc}, source={source.Title}, resolution={_screenShareResolution}, fps={_screenShareFrameRate}");
        }

        if (!useWebRtc)
        {
            _screenShareFrameRate = ClampScreenShareFrameRate(_screenShareResolution, _screenShareFrameRate);
        }

        if (useWebRtc)
        {
            var ffmpegPath = FindFfmpegExecutable();
            if (ScreenSharePreferEncodedWebRtc && !string.IsNullOrWhiteSpace(ffmpegPath))
            {
                _screenShareUsingEncodedWebRtc = true;
                var encodedBounds = GetScreenShareSourceBounds(source);
                var encodedHeight = GetScreenShareTargetHeight(encodedBounds, _screenShareResolution);
                var encodedWidth = GetScreenShareTargetWidth(encodedBounds, encodedHeight);
                NetworkStatusText.Text = $"Starting hardware H.264 screen share: {source.Title}.";
                _ = Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        UpdateScreenShareStageVisibility();
                        PostScreenShareWebRtcMessage(new
                        {
                            type = "start-encoded-share",
                            iceServers = GetScreenShareWebRtcIceServers(),
                            polite = IsPoliteScreenSharePeer(),
                            title = source.Title,
                            mimeType = "video/mp4; codecs=\"avc1.640033\"",
                            width = encodedWidth,
                            height = encodedHeight,
                            frameRate = _screenShareFrameRate,
                            dataChannelBufferLimit = ScreenShareEncodedDataChannelBufferLimit
                        });
                    }),
                    DispatcherPriority.Loaded);
                AppLog.Write($"Screen share encoded WebRTC requested: source={source.Title}, resolution={_screenShareResolution}, effectiveHeight={encodedHeight}, fps={_screenShareFrameRate}, ffmpeg={ffmpegPath}");
                return;
            }

            if (ScreenSharePreferEncodedWebRtc)
            {
                AppLog.Write("Screen share encoded WebRTC unavailable: ffmpeg.exe was not found; using native WebRTC frame bridge.");
                NetworkStatusText.Text = "Hardware H.264 screen share unavailable: ffmpeg.exe not found. Using compatible mode.";
            }

            _screenShareUsingNativeWebRtc = true;
            var nativeBounds = GetScreenShareSourceBounds(source);
            var targetHeight = GetScreenShareTargetHeight(nativeBounds, _screenShareResolution);
            var targetWidth = GetScreenShareTargetWidth(nativeBounds, targetHeight);
            NetworkStatusText.Text = $"Starting native WebRTC screen share: {source.Title}.";
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    UpdateScreenShareStageVisibility();
                    PostScreenShareWebRtcMessage(new
                    {
                        type = "start-native-share",
                        iceServers = GetScreenShareWebRtcIceServers(),
                        polite = IsPoliteScreenSharePeer(),
                        title = source.Title,
                        width = targetWidth,
                        height = targetHeight,
                        frameRate = _screenShareFrameRate,
                        maxBitrate = GetScreenShareWebRtcMaxBitrate(),
                        contentHint = "detail"
                    });
                }),
                DispatcherPriority.Loaded);
            _ = Task.Run(() => RunNativeWebRtcScreenShareLoopAsync(source, _screenShareStop.Token));
            AppLog.Write($"Screen share native WebRTC requested: source={source.Title}, resolution={_screenShareResolution}, effectiveHeight={targetHeight}, fps={_screenShareFrameRate}, maxBitrate={GetScreenShareWebRtcMaxBitrate()}");
            return;
        }

        SendScreenShareStartSignal(source, useWebRtc: false);
        Interlocked.Exchange(ref _screenShareJpegLoopStarted, 1);
        NetworkStatusText.Text = $"Screen sharing {source.Title} at {_screenShareResolution}p {_screenShareFrameRate} fps.";
        AppLog.Write($"Screen share native capture started: source={source.Title}, resolution={_screenShareResolution}, fps={_screenShareFrameRate}, jpegQuality={ScreenShareJpegQuality}");
        _ = Task.Run(() => RunScreenShareLoopAsync(source, _screenShareStop.Token));
    }

    private void SendScreenShareStartSignal(ScreenShareSourceItem source, bool useWebRtc)
    {
        if (_screenShareStartSignalSent)
        {
            return;
        }

        _screenShareStartSignalSent = true;
        var start = JsonSerializer.Serialize(new ScreenShareStartPayload(
            useWebRtc ? $"{source.Title} (WebRTC)" : source.Title,
            _screenShareResolution,
            _screenShareFrameRate,
            _screenShareMuteAudio));
        _ = SendScreenShareSignalAsync(CallScreenStartIntent, start);
    }

    private void StopScreenShare(bool sendSignal)
    {
        var stop = Interlocked.Exchange(ref _screenShareStop, null);
        if (stop is not null)
        {
            stop.Cancel();
            stop.Dispose();
        }

        var wasSharing = _isScreenSharing;
        var startSignalSent = _screenShareStartSignalSent;
        var wasUsingNativeWebRtc = _screenShareUsingNativeWebRtc;
        var wasUsingEncodedWebRtc = _screenShareUsingEncodedWebRtc;
        _isScreenSharing = false;
        _activeScreenShareSource = null;
        _screenShareStartSignalSent = false;
        _screenShareUsingNativeWebRtc = false;
        _screenShareUsingEncodedWebRtc = false;
        _screenShareEncodedChannelOpen = false;
        StopScreenShareEncoderProcess();
        if (wasUsingNativeWebRtc || wasUsingEncodedWebRtc || _screenShareWebRtcActive || ScreenShareWebView.Visibility == Visibility.Visible)
        {
            PostScreenShareWebRtcMessage(new { type = "stop-local" });
            _screenShareWebRtcActive = _peerScreenShareUsingWebRtc;
            SetScreenShareWebRtcVisible(_screenShareWebRtcActive);
        }

        Interlocked.Exchange(ref _pendingScreenShareFrame, 0);
        Interlocked.Exchange(ref _pendingNativeWebRtcWebViewFrame, 0);
        Interlocked.Exchange(ref _screenShareJpegLoopStarted, 0);
        Interlocked.Exchange(ref _lastSelfScreenSharePreviewTicks, 0);
        Interlocked.Exchange(ref _lastEncodedScreenSharePreviewTicks, 0);
        CallSelfScreenSharePreview.Source = null;

        UpdateScreenShareControlVisuals();
        UpdateScreenShareStageVisibility();

        if (sendSignal && wasSharing && startSignalSent)
        {
            _ = SendScreenShareSignalAsync(CallScreenStopIntent);
        }
    }

    private async Task RunScreenShareLoopAsync(ScreenShareSourceItem source, CancellationToken cancellationToken)
    {
        try
        {
            var interval = TimeSpan.FromMilliseconds(1000d / Math.Clamp(_screenShareFrameRate, 15, 60));
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Interlocked.CompareExchange(ref _pendingScreenShareFrame, 1, 0) != 0)
                {
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendScreenShareFrameAsync(source, cancellationToken);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pendingScreenShareFrame, 0);
                    }
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Screen share loop failed");
            _ = Dispatcher.BeginInvoke(new Action(() => StopScreenShare(sendSignal: true)));
        }
    }

    private async Task SendScreenShareFrameAsync(ScreenShareSourceItem source, CancellationToken cancellationToken)
    {
        if (_profile is null ||
            _activeCallContact is null ||
            !_isScreenSharing ||
            _activeCallState != "connected")
        {
            return;
        }

        if (Volatile.Read(ref _pendingNativeWebRtcWebViewFrame) != 0)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var frameHeight = GetAdaptiveScreenShareHeight();
        var jpeg = CaptureScreenShareFrame(source, frameHeight);
        if (jpeg is null)
        {
            return;
        }

        if (ShouldSkipDuplicateScreenShareFrame(jpeg, ScreenShareDuplicateFrameInterval))
        {
            return;
        }

        var sent = Interlocked.Increment(ref _sentScreenShareFrames);
        var jpegBase64 = Convert.ToBase64String(jpeg);
        var payload = JsonSerializer.Serialize(new ScreenShareFramePayload(
            sent,
            jpegBase64,
            frameHeight,
            _screenShareFrameRate,
            _screenShareMuteAudio));
        if (payload.Length > ScreenShareMaxFrameBodyChars)
        {
            AppLog.Write($"Screen share frame skipped: bodyLength={payload.Length}, source={source.Title}");
            AdjustAdaptiveScreenShareHeight(TimeSpan.FromMilliseconds(1000d / Math.Max(1, _screenShareFrameRate) * 2), payload.Length);
            return;
        }

        using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendTimeout.CancelAfter(ScreenShareSendTimeout);
        if (_relayClient is null)
        {
            return;
        }

        try
        {
            await _relayClient.SendScreenFrameAsync(
                RelayScreenFramePacket.Create(_profile.UserId, _activeCallContact.UserId, payload),
                sendTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write($"Screen share media frame send timed out: source={source.Title}, channel=screen-tcp, bodyLength={payload.Length}, timeoutMs={ScreenShareSendTimeout.TotalMilliseconds:0}");
            AdjustAdaptiveScreenShareHeight(ScreenShareSendTimeout + ScreenShareSendTimeout, payload.Length);
            return;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Screen share media frame send failed: source={source.Title}, channel=screen-tcp, bodyLength={payload.Length}");
            AdjustAdaptiveScreenShareHeight(ScreenShareSendTimeout + ScreenShareSendTimeout, payload.Length);
            return;
        }

        stopwatch.Stop();
        AdjustAdaptiveScreenShareHeight(stopwatch.Elapsed, payload.Length);
        if (ShouldUpdateSelfScreenSharePreview())
        {
            var image = CreateBitmapImage(jpeg, 360);
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    CallSelfScreenSharePreview.Source = image;
                    if (_peerScreenShareUsingWebRtc)
                    {
                        PostScreenShareWebRtcMessage(new { type = "local-preview", jpegBase64 });
                    }
                }),
                DispatcherPriority.Background);
        }

        if (sent == 1 || sent % 100 == 0)
        {
            AppLog.Write($"Screen share frame sent: frames={sent}, channel=screen-tcp, bytes={jpeg.Length}, bodyLength={payload.Length}, source={source.Title}, resolution={_screenShareResolution}, effectiveHeight={frameHeight}, fps={_screenShareFrameRate}, elapsedMs={stopwatch.ElapsedMilliseconds}, jpegQuality={GetScreenShareJpegQuality(frameHeight)}");
        }
    }

    private void StartEncodedScreenShareEncoderIfReady()
    {
        var source = _activeScreenShareSource;
        var stop = _screenShareStop;
        if (!_isScreenSharing ||
            !_screenShareUsingEncodedWebRtc ||
            !_screenShareEncodedChannelOpen ||
            source is null ||
            stop is null ||
            _screenShareEncoderProcess is not null)
        {
            return;
        }

        var ffmpegPath = FindFfmpegExecutable();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            FallbackScreenShareFromWebRtc("ffmpeg.exe was not found");
            return;
        }

        _ = Task.Run(() => RunEncodedScreenShareAsync(source, ffmpegPath, stop.Token));
    }

    private async Task RunEncodedScreenShareAsync(ScreenShareSourceItem source, string ffmpegPath, CancellationToken cancellationToken)
    {
        try
        {
            var bounds = source.IsScreen ? source.Bounds : GetWindowBounds(source.WindowHandle);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("Screen source bounds are empty.");
            }

            Exception? lastError = null;
            foreach (var encoder in DetectFfmpegH264Encoders(ffmpegPath))
            {
                if (!_screenShareUsingEncodedWebRtc || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    var chunks = await RunEncodedScreenShareProcessAsync(source, ffmpegPath, bounds, encoder, cancellationToken);
                    if (!_screenShareUsingEncodedWebRtc || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    AppLog.Write($"Screen share H.264 encoder ended early: encoder={encoder}, chunks={chunks}; trying next encoder.");
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (!_stop.IsCancellationRequested)
                {
                    lastError = ex;
                    AppLog.Write(ex, $"Screen share H.264 encoder failed: encoder={encoder}");
                }
            }

            if (lastError is not null)
            {
                AppLog.Write(lastError, "Screen share H.264 encoders exhausted");
            }
            else
            {
                AppLog.Write("Screen share H.264 encoders exhausted");
            }

            if (!_screenShareUsingEncodedWebRtc || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(new Action(() => FallbackScreenShareFromWebRtc("H.264 encoder failed")));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Screen share H.264 encoder failed");
            _ = Dispatcher.BeginInvoke(new Action(() => FallbackScreenShareFromWebRtc("H.264 encoder failed")));
        }
    }

    private async Task<long> RunEncodedScreenShareProcessAsync(
        ScreenShareSourceItem source,
        string ffmpegPath,
        Rectangle bounds,
        string encoder,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            var arguments = CreateFfmpegScreenShareArguments(bounds, encoder);
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                throw new InvalidOperationException("ffmpeg process did not start.");
            }

            _screenShareEncoderProcess = process;
            _ = Task.Run(() => ReadScreenShareEncoderLogAsync(process, cancellationToken), cancellationToken);
            var effectiveHeight = GetScreenShareTargetHeight(bounds, _screenShareResolution);
            AppLog.Write($"Screen share H.264 encoder started: encoder={encoder}, source={source.Title}, bounds={bounds.Width}x{bounds.Height}+{bounds.Left}+{bounds.Top}, resolution={_screenShareResolution}, effectiveHeight={effectiveHeight}, fps={_screenShareFrameRate}");

            long chunks = 0;
            var buffer = new byte[ScreenShareEncodedChunkSize];
            while (_screenShareUsingEncodedWebRtc && !cancellationToken.IsCancellationRequested)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                chunks++;
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                PostEncodedScreenShareChunk(chunk);
                PostEncodedScreenShareLocalPreview(source);
            }

            if (!process.HasExited)
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            return chunks;
        }
        finally
        {
            if (ReferenceEquals(_screenShareEncoderProcess, process))
            {
                _screenShareEncoderProcess = null;
            }

            if (process is not null && !process.HasExited)
            {
                TryKillScreenShareEncoder(process);
            }

            process?.Dispose();
        }
    }

    private void PostEncodedScreenShareChunk(byte[] chunk)
    {
        if (!_screenShareUsingEncodedWebRtc || chunk.Length == 0)
        {
            return;
        }

        var sent = Interlocked.Increment(ref _sentEncodedScreenShareChunks);
        var json = JsonSerializer.Serialize(new
        {
            type = "encoded-local-chunk",
            sequence = sent,
            dataBase64 = Convert.ToBase64String(chunk)
        });

        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_screenShareUsingEncodedWebRtc && ScreenShareWebView.CoreWebView2 is not null)
                {
                    PostScreenShareWebRtcMessageJson(json);
                }
            }),
            DispatcherPriority.Background);

        if (sent == 1 || sent % 200 == 0)
        {
            AppLog.Write($"Screen share H.264 chunk queued: chunks={sent}, bytes={chunk.Length}");
        }
    }

    private void PostEncodedScreenShareLocalPreview(ScreenShareSourceItem source)
    {
        if (!_screenShareUsingEncodedWebRtc)
        {
            return;
        }

        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastEncodedScreenSharePreviewTicks);
        if (nowTicks - lastTicks < ScreenShareEncodedLocalPreviewInterval.Ticks ||
            Interlocked.CompareExchange(ref _lastEncodedScreenSharePreviewTicks, nowTicks, lastTicks) != lastTicks)
        {
            return;
        }

        var jpeg = CaptureScreenShareFrame(source, 360);
        if (jpeg is null)
        {
            return;
        }

        var base64 = Convert.ToBase64String(jpeg);
        var image = CreateBitmapImage(jpeg, 360);
        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!_screenShareUsingEncodedWebRtc)
                {
                    return;
                }

                CallSelfScreenSharePreview.Source = image;
                if (_peerScreenShareUsingWebRtc)
                {
                    PostScreenShareWebRtcMessage(new { type = "local-preview", jpegBase64 = base64 });
                }
            }),
            DispatcherPriority.Background);
    }

    private async Task ReadScreenShareEncoderLogAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(line) &&
                    (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("failed", StringComparison.OrdinalIgnoreCase)))
                {
                    AppLog.Write($"Screen share encoder: {line}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Screen share encoder log read failed");
        }
    }

    private IReadOnlyList<string> CreateFfmpegScreenShareArguments(Rectangle bounds, string encoder)
    {
        var fps = Math.Clamp(_screenShareFrameRate, 15, 60);
        var effectiveHeight = GetScreenShareTargetHeight(bounds, _screenShareResolution);
        var bitrateKbps = GetScreenShareH264BitrateKbps(effectiveHeight);
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "warning",
            "-f",
            "gdigrab",
            "-draw_mouse",
            "1",
            "-framerate",
            fps.ToString(CultureInfo.InvariantCulture),
            "-offset_x",
            bounds.Left.ToString(CultureInfo.InvariantCulture),
            "-offset_y",
            bounds.Top.ToString(CultureInfo.InvariantCulture),
            "-video_size",
            $"{bounds.Width}x{bounds.Height}",
            "-i",
            "desktop",
            "-vf",
            $"scale=-2:{effectiveHeight},format=yuv420p",
            "-an"
        };

        AddFfmpegEncoderArguments(arguments, encoder, bitrateKbps, fps);
        arguments.AddRange([
            "-movflags",
            "frag_keyframe+empty_moov+default_base_moof",
            "-frag_duration",
            "250000",
            "-flush_packets",
            "1",
            "-f",
            "mp4",
            "pipe:1"
        ]);
        return arguments;
    }

    private static void AddFfmpegEncoderArguments(List<string> arguments, string encoder, int bitrateKbps, int fps)
    {
        arguments.AddRange(["-c:v", encoder]);
        switch (encoder)
        {
            case "h264_nvenc":
                arguments.AddRange(["-preset", "p4", "-tune", "ll", "-rc", "vbr", "-bf", "0"]);
                break;
            case "h264_qsv":
                arguments.AddRange(["-preset", "veryfast", "-bf", "0"]);
                break;
            case "h264_amf":
                arguments.AddRange(["-quality", "speed", "-usage", "ultralowlatency", "-bf", "0"]);
                break;
            default:
                arguments.AddRange(["-preset", "veryfast", "-tune", "zerolatency", "-bf", "0"]);
                break;
        }

        arguments.AddRange([
            "-profile:v",
            "high",
            "-b:v",
            $"{bitrateKbps}k",
            "-maxrate",
            $"{bitrateKbps}k",
            "-bufsize",
            $"{bitrateKbps * 2}k",
            "-g",
            fps.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt",
            "yuv420p"
        ]);
    }

    private int GetScreenShareH264BitrateKbps(int effectiveHeight)
    {
        int bitrate;
        if (effectiveHeight >= 1440)
        {
            bitrate = _screenShareFrameRate >= 60 ? 28_000 : 16_000;
            return IsScreenShareVoiceProtectionActive()
                ? Math.Min(bitrate, ScreenShareVoiceProtectedH264MaxBitrateKbps)
                : bitrate;
        }

        if (effectiveHeight >= 1080)
        {
            bitrate = _screenShareFrameRate >= 60 ? 14_000 : 8_000;
            return IsScreenShareVoiceProtectionActive()
                ? Math.Min(bitrate, ScreenShareVoiceProtectedH264MaxBitrateKbps)
                : bitrate;
        }

        bitrate = _screenShareFrameRate >= 60 ? 7_000 : 4_500;
        return IsScreenShareVoiceProtectionActive()
            ? Math.Min(bitrate, ScreenShareVoiceProtectedH264MaxBitrateKbps)
            : bitrate;
    }

    private static IReadOnlyList<string> DetectFfmpegH264Encoders(string ffmpegPath)
    {
        var preferredEncoders = new[] { "h264_nvenc", "h264_qsv", "h264_amf", "libx264" };
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-encoders");
            if (process.Start())
            {
                var output = process.StandardOutput.ReadToEnd();
                output += process.StandardError.ReadToEnd();
                if (process.WaitForExit(1500))
                {
                    var detected = preferredEncoders
                        .Where(encoder => output.Contains(encoder, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    return detected.Length > 0 ? detected : new[] { "libx264" };
                }
                else
                {
                    TryKillScreenShareEncoder(process);
                }
            }
        }
        catch
        {
        }

        return new[] { "libx264" };
    }

    private static string? FindFfmpegExecutable()
    {
        foreach (var candidate in GetFfmpegCandidates())
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> GetFfmpegCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "ffmpeg.exe");
        yield return Path.Combine(baseDirectory, "tools", "ffmpeg.exe");
        yield return Path.Combine(AppPaths.DataDirectory, "ffmpeg.exe");
        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, "ffmpeg.exe");
            yield return Path.Combine(directory.FullName, "tools", "ffmpeg.exe");
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, "ffmpeg.exe");
        }
    }

    private void StopScreenShareEncoderProcess()
    {
        var process = Interlocked.Exchange(ref _screenShareEncoderProcess, null);
        if (process is null)
        {
            return;
        }

        TryKillScreenShareEncoder(process);
    }

    private static void TryKillScreenShareEncoder(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private async Task RunNativeWebRtcScreenShareLoopAsync(ScreenShareSourceItem source, CancellationToken cancellationToken)
    {
        try
        {
            var interval = TimeSpan.FromMilliseconds(1000d / Math.Clamp(_screenShareFrameRate, 15, 60));
            using var timer = new PeriodicTimer(interval);
            while (_screenShareUsingNativeWebRtc && await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Interlocked.CompareExchange(ref _pendingScreenShareFrame, 1, 0) != 0)
                {
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PostNativeWebRtcScreenShareFrameAsync(source, cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        AppLog.Write(ex, "Native WebRTC screen share frame failed");
                        _ = Dispatcher.BeginInvoke(new Action(() => FallbackScreenShareFromWebRtc("native WebRTC frame failed")));
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pendingScreenShareFrame, 0);
                    }
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Native WebRTC screen share loop failed");
            _ = Dispatcher.BeginInvoke(new Action(() => FallbackScreenShareFromWebRtc("native WebRTC capture loop failed")));
        }
    }

    private async Task PostNativeWebRtcScreenShareFrameAsync(ScreenShareSourceItem source, CancellationToken cancellationToken)
    {
        if (_profile is null ||
            _activeCallContact is null ||
            !_isScreenSharing ||
            !_screenShareUsingNativeWebRtc ||
            _activeCallState != "connected")
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var frameHeight = GetAdaptiveScreenShareHeight();
        var jpeg = CaptureScreenShareFrame(source, frameHeight);
        if (jpeg is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (ShouldSkipDuplicateScreenShareFrame(jpeg, ScreenShareWebRtcDuplicateFrameInterval))
        {
            return;
        }

        var sent = Interlocked.Increment(ref _sentScreenShareFrames);
        var base64 = Convert.ToBase64String(jpeg);
        var json = JsonSerializer.Serialize(new
        {
            type = "native-frame",
            sequence = sent,
            jpegBase64 = base64,
            resolution = frameHeight,
            frameRate = _screenShareFrameRate,
            timestampUs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L
        });
        if (json.Length > ScreenShareMaxFrameBodyChars)
        {
            AppLog.Write($"Native WebRTC screen frame skipped: bodyLength={json.Length}, source={source.Title}");
            AdjustAdaptiveScreenShareHeight(TimeSpan.FromMilliseconds(1000d / Math.Max(1, _screenShareFrameRate) * 2), json.Length);
            return;
        }

        if (Interlocked.Exchange(ref _pendingNativeWebRtcWebViewFrame, 1) != 0)
        {
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        if (_screenShareUsingNativeWebRtc && ScreenShareWebView.CoreWebView2 is not null)
                        {
                            PostScreenShareWebRtcMessageJson(json);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or COMException)
                    {
                        AppLog.Write(ex, "Native WebRTC screen frame post failed");
                        FallbackScreenShareFromWebRtc("native WebRTC frame post failed");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pendingNativeWebRtcWebViewFrame, 0);
                    }
                }),
                DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            Interlocked.Exchange(ref _pendingNativeWebRtcWebViewFrame, 0);
            if (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Write(ex, "Native WebRTC screen frame dispatch failed");
                _ = Dispatcher.BeginInvoke(new Action(() => FallbackScreenShareFromWebRtc("native WebRTC frame post failed")));
            }

            return;
        }

        stopwatch.Stop();
        AdjustAdaptiveScreenShareHeight(stopwatch.Elapsed, json.Length);
        if (ShouldUpdateSelfScreenSharePreview())
        {
            var image = CreateBitmapImage(jpeg, 360);
            _ = Dispatcher.BeginInvoke(
                new Action(() => CallSelfScreenSharePreview.Source = image),
                DispatcherPriority.Background);
        }

        if (sent == 1 || sent % 100 == 0)
        {
            AppLog.Write($"Native WebRTC screen frame queued: frames={sent}, bytes={jpeg.Length}, bodyLength={json.Length}, source={source.Title}, resolution={_screenShareResolution}, effectiveHeight={frameHeight}, fps={_screenShareFrameRate}, elapsedMs={stopwatch.ElapsedMilliseconds}, jpegQuality={GetScreenShareJpegQuality(frameHeight)}");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private bool ShouldSkipDuplicateScreenShareFrame(byte[] jpeg, TimeSpan duplicateInterval)
    {
        if (duplicateInterval <= TimeSpan.Zero)
        {
            return false;
        }

        var hash = ComputeFrameHash(jpeg);
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        if (hash == _lastScreenShareFrameHash &&
            nowTicks - _lastScreenShareFrameHashSentTicks < duplicateInterval.Ticks)
        {
            return true;
        }

        _lastScreenShareFrameHash = hash;
        _lastScreenShareFrameHashSentTicks = nowTicks;
        return false;
    }

    private static ulong ComputeFrameHash(ReadOnlySpan<byte> bytes)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offset;
        var step = Math.Max(1, bytes.Length / 4096);
        for (var index = 0; index < bytes.Length; index += step)
        {
            hash ^= bytes[index];
            hash *= prime;
        }

        hash ^= (ulong)bytes.Length;
        hash *= prime;
        return hash;
    }

    private int GetAdaptiveScreenShareHeight()
        => Math.Clamp(_screenShareAdaptiveHeight, GetMinimumAdaptiveScreenShareHeight(), _screenShareResolution);

    private int GetInitialScreenShareAdaptiveHeight()
        => _screenShareFrameRate >= 45
            ? Math.Min(_screenShareResolution, GetMinimumAdaptiveScreenShareHeight())
            : _screenShareResolution;

    private int GetMinimumAdaptiveScreenShareHeight()
        => _screenShareResolution >= 1440
            ? Math.Min(ScreenShareHighResolutionMinAdaptiveHeight, _screenShareResolution)
            : Math.Min(ScreenShareMinAdaptiveHeight, _screenShareResolution);

    private void AdjustAdaptiveScreenShareHeight(TimeSpan elapsed, int bodyLength)
    {
        var minimumHeight = GetMinimumAdaptiveScreenShareHeight();
        if (_screenShareResolution <= minimumHeight)
        {
            _screenShareAdaptiveHeight = _screenShareResolution;
            return;
        }

        var current = GetAdaptiveScreenShareHeight();
        var targetFrameMs = 1000d / Math.Max(1, _screenShareFrameRate);
        if (_screenShareFrameRate >= 45 &&
            elapsed.TotalMilliseconds > targetFrameMs * 1.65 &&
            current > minimumHeight)
        {
            _screenShareAdaptiveHeight = Math.Max(minimumHeight, current - ScreenShareAdaptiveStep);
            return;
        }

        if (bodyLength > ScreenShareMaxFrameBodyChars * 0.92 &&
            current > minimumHeight)
        {
            _screenShareAdaptiveHeight = Math.Max(minimumHeight, current - ScreenShareAdaptiveStep);
            return;
        }

        var isFastEnough = _screenShareFrameRate < 45 ||
            elapsed.TotalMilliseconds < targetFrameMs * 1.15;
        if (isFastEnough &&
            bodyLength < ScreenShareMaxFrameBodyChars * 0.55 &&
            current < _screenShareResolution)
        {
            _screenShareAdaptiveHeight = Math.Min(_screenShareResolution, current + ScreenShareAdaptiveStep);
        }
    }

    private bool ShouldUpdateSelfScreenSharePreview()
    {
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastSelfScreenSharePreviewTicks);
        if (nowTicks - lastTicks < ScreenShareSelfPreviewInterval.Ticks)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _lastSelfScreenSharePreviewTicks, nowTicks, lastTicks) == lastTicks;
    }

    private byte[]? CaptureScreenShareFrame(ScreenShareSourceItem source, int targetHeight)
    {
        var bounds = source.IsScreen ? source.Bounds : GetWindowBounds(source.WindowHandle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        using var capture = source.IsScreen
            ? CaptureScreenBounds(bounds)
            : CaptureWindowFrame(source.WindowHandle, bounds) ?? CaptureScreenBounds(bounds);

        var scale = Math.Min(1d, (double)Math.Max(1, targetHeight) / capture.Height);
        var width = Math.Max(2, (int)Math.Round(capture.Width * scale));
        var height = Math.Max(2, (int)Math.Round(capture.Height * scale));
        var highFrameRate = _screenShareFrameRate >= 45;
        using var scaled = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = highFrameRate
                ? CompositingQuality.HighSpeed
                : CompositingQuality.HighQuality;
            graphics.InterpolationMode = scale < 0.999d
                ? highFrameRate ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic
                : InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = highFrameRate
                ? PixelOffsetMode.HighSpeed
                : PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.DrawImage(capture, 0, 0, width, height);
        }

        using var stream = new MemoryStream();
        var jpegQuality = GetScreenShareJpegQuality(targetHeight);
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);
        var jpegCodec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => x.FormatID == ImageFormat.Jpeg.Guid);
        if (jpegCodec is null)
        {
            scaled.Save(stream, ImageFormat.Jpeg);
        }
        else
        {
            scaled.Save(stream, jpegCodec, encoderParameters);
        }

        return stream.ToArray();
    }

    private long GetScreenShareJpegQuality(int targetHeight)
    {
        if (_screenShareFrameRate < 45)
        {
            return ScreenShareJpegQuality;
        }

        return _screenShareResolution >= 1440 || targetHeight >= 1440
            ? ScreenShareHighLoadJpegQuality
            : ScreenShareHighFrameRateJpegQuality;
    }

    private static Bitmap CaptureScreenBounds(Rectangle bounds)
    {
        var capture = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(capture))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            DrawCursor(graphics, bounds);
        }

        return capture;
    }

    private static Bitmap? CaptureWindowFrame(IntPtr handle, Rectangle bounds)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var capture = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(capture);
        var hdc = graphics.GetHdc();
        var captured = false;
        try
        {
            captured = PrintWindow(handle, hdc, PrintWindowRenderFullContent);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        if (captured)
        {
            DrawCursor(graphics, bounds);
            return capture;
        }

        capture.Dispose();
        return null;
    }

    private static void DrawCursor(Graphics graphics, Rectangle captureBounds)
    {
        var cursorInfo = new CursorInfo
        {
            CbSize = Marshal.SizeOf<CursorInfo>()
        };

        if (!GetCursorInfo(ref cursorInfo) ||
            cursorInfo.Flags != CursorShowing ||
            cursorInfo.CursorHandle == IntPtr.Zero)
        {
            return;
        }

        var cursorX = cursorInfo.ScreenPosition.X - captureBounds.Left;
        var cursorY = cursorInfo.ScreenPosition.Y - captureBounds.Top;
        if (cursorX < -64 ||
            cursorY < -64 ||
            cursorX > captureBounds.Width + 64 ||
            cursorY > captureBounds.Height + 64)
        {
            return;
        }

        var hotX = 0;
        var hotY = 0;
        if (GetIconInfo(cursorInfo.CursorHandle, out var iconInfo))
        {
            hotX = iconInfo.XHotspot;
            hotY = iconInfo.YHotspot;
            if (iconInfo.ColorBitmap != IntPtr.Zero)
            {
                DeleteObject(iconInfo.ColorBitmap);
            }

            if (iconInfo.MaskBitmap != IntPtr.Zero)
            {
                DeleteObject(iconInfo.MaskBitmap);
            }
        }

        var hdc = graphics.GetHdc();
        try
        {
            DrawIconEx(
                hdc,
                cursorX - hotX,
                cursorY - hotY,
                cursorInfo.CursorHandle,
                0,
                0,
                0,
                IntPtr.Zero,
                DrawIconNormal);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    private void HandleScreenSharePacket(ChatPacket packet)
    {
        if (!IsActiveCallPeer(packet.FromUserId))
        {
            return;
        }

        switch (packet.Intent)
        {
            case CallScreenStartIntent:
                _peerScreenSharing = true;
                _isWatchingPeerScreen = true;
                var peerUsesWebRtc = IsWebRtcScreenShareStart(packet.Body);
                if (!peerUsesWebRtc && _peerScreenShareUsingWebRtc)
                {
                    PostScreenShareWebRtcMessage(new { type = "stop-remote" });
                }

                _peerScreenShareUsingWebRtc = peerUsesWebRtc;
                Interlocked.Exchange(ref _receivedScreenShareFrames, 0);
                Interlocked.Exchange(ref _droppedReceivedScreenShareFrames, 0);
                Interlocked.Exchange(ref _lastPeerScreenShareFrameAcceptedTicks, 0);
                if (!_isScreenShareFocusMode)
                {
                    EnterScreenShareFocusMode();
                }

                UpdateScreenShareStageVisibility();
                AppLog.Write($"Screen share started: from={packet.FromUserId}, bodyLength={packet.Body.Length}");
                break;
            case CallScreenFrameIntent:
                QueuePeerScreenShareFrame(packet.FromUserId, packet.Body);
                break;
            case CallScreenStopIntent:
                _peerScreenSharing = false;
                _isWatchingPeerScreen = false;
                _peerScreenShareUsingWebRtc = false;
                ClearQueuedPeerScreenShareFrames();
                CallPeerScreenSharePreview.Source = null;
                if (_screenShareWebRtcActive || ScreenShareWebView.Visibility == Visibility.Visible)
                {
                    PostScreenShareWebRtcMessage(new { type = "stop-remote" });
                    _screenShareWebRtcActive = _screenShareUsingNativeWebRtc || _screenShareUsingEncodedWebRtc;
                    SetScreenShareWebRtcVisible(_screenShareWebRtcActive);
                }

                UpdateScreenShareStageVisibility();
                AppLog.Write($"Screen share stopped: from={packet.FromUserId}");
                break;
        }
    }

    private static bool IsWebRtcScreenShareStart(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ScreenShareStartPayload>(body);
            return payload?.SourceTitle.Contains("(WebRTC)", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (JsonException)
        {
            return body.Contains("WebRTC", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void QueuePeerScreenShareFrame(ChatPacket packet)
        => QueuePeerScreenShareFrame(packet.FromUserId, packet.Body);

    private void HandleRelayScreenFramePacket(RelayScreenFramePacket packet)
    {
        if (!IsActiveCallPeer(packet.FromUserId) ||
            !_peerScreenSharing ||
            string.IsNullOrWhiteSpace(packet.Body))
        {
            return;
        }

        QueuePeerScreenShareFrame(packet.FromUserId, packet.Body);
    }

    private void QueuePeerScreenShareFrame(string fromUserId, string body)
    {
        if (!ShouldAcceptPeerScreenShareFrame())
        {
            Interlocked.Increment(ref _droppedReceivedScreenShareFrames);
            return;
        }

        lock (_peerScreenShareFrameGate)
        {
            if (_latestPeerScreenShareFrame is not null)
            {
                Interlocked.Increment(ref _droppedReceivedScreenShareFrames);
            }

            _latestPeerScreenShareFrame = new QueuedScreenShareFrame(fromUserId, body);
        }

        if (Interlocked.CompareExchange(ref _pendingPeerScreenShareFrame, 1, 0) == 0)
        {
            _ = Task.Run(ProcessQueuedPeerScreenShareFramesAsync);
        }
    }

    private bool ShouldAcceptPeerScreenShareFrame()
    {
        var maxFrameRate = _isScreenShareFullscreenMode
            ? ScreenShareFullscreenMaxPeerRenderFrameRate
            : ScreenShareMaxPeerRenderFrameRate;
        var intervalTicks = TimeSpan.FromMilliseconds(1000d / maxFrameRate).Ticks;
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastPeerScreenShareFrameAcceptedTicks);
        if (lastTicks > 0 && nowTicks - lastTicks < intervalTicks)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _lastPeerScreenShareFrameAcceptedTicks, nowTicks, lastTicks) == lastTicks;
    }

    private void ClearQueuedPeerScreenShareFrames()
    {
        lock (_peerScreenShareFrameGate)
        {
            _latestPeerScreenShareFrame = null;
        }

        Interlocked.Exchange(ref _lastPeerScreenShareFrameAcceptedTicks, 0);
    }

    private async Task ProcessQueuedPeerScreenShareFramesAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                QueuedScreenShareFrame? queued;
                lock (_peerScreenShareFrameGate)
                {
                    queued = _latestPeerScreenShareFrame;
                    _latestPeerScreenShareFrame = null;
                }

                if (queued is null)
                {
                    return;
                }

                if (!IsActiveCallPeer(queued.FromUserId))
                {
                    continue;
                }

                try
                {
                    var frame = JsonSerializer.Deserialize<ScreenShareFramePayload>(queued.Body);
                    if (frame is null || string.IsNullOrWhiteSpace(frame.JpegBase64))
                    {
                        continue;
                    }

                    var jpeg = Convert.FromBase64String(frame.JpegBase64);
                    var image = CreateBitmapImage(jpeg, GetPeerScreenShareDecodePixelHeight());
                    await Dispatcher.InvokeAsync(
                        () => ApplyPeerScreenShareFrame(queued.FromUserId, image, jpeg.Length),
                        DispatcherPriority.Render);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)
                {
                    AppLog.Write(ex, $"Screen share frame parse failed: from={queued.FromUserId}, bodyLength={queued.Body.Length}");
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pendingPeerScreenShareFrame, 0);
            lock (_peerScreenShareFrameGate)
            {
                if (_latestPeerScreenShareFrame is not null &&
                    Interlocked.CompareExchange(ref _pendingPeerScreenShareFrame, 1, 0) == 0)
                {
                    _ = Task.Run(ProcessQueuedPeerScreenShareFramesAsync);
                }
            }
        }
    }

    private void ApplyPeerScreenShareFrame(string fromUserId, ImageSource image, int byteLength)
    {
        if (!IsActiveCallPeer(fromUserId))
        {
            return;
        }

        _peerScreenSharing = true;
        _isWatchingPeerScreen = true;
        if (_peerScreenShareUsingWebRtc)
        {
            _peerScreenShareUsingWebRtc = false;
            PostScreenShareWebRtcMessage(new { type = "stop-remote" });
            _screenShareWebRtcActive = _screenShareUsingNativeWebRtc || _screenShareUsingEncodedWebRtc;
            SetScreenShareWebRtcVisible(_screenShareWebRtcActive);
        }

        CallPeerScreenSharePreview.Source = image;

        var received = Interlocked.Increment(ref _receivedScreenShareFrames);
        UpdateScreenShareStageVisibility();
        if (received == 1 || received % 100 == 0)
        {
            var dropped = Interlocked.Read(ref _droppedReceivedScreenShareFrames);
            AppLog.Write($"Screen share frame received: from={fromUserId}, frames={received}, dropped={dropped}, bytes={byteLength}");
        }
    }

    private void SetScreenSharePreview(byte[] jpeg, bool isPeerFrame)
    {
        var image = CreateBitmapImage(jpeg, isPeerFrame ? GetPeerScreenShareDecodePixelHeight() : 360);
        if (isPeerFrame)
        {
            CallPeerScreenSharePreview.Source = image;
            return;
        }

        CallSelfScreenSharePreview.Source = image;
    }

    private int GetPeerScreenShareDecodePixelHeight()
        => _isScreenShareFullscreenMode ? 1440 : 360;

    private Rectangle GetScreenShareSourceBounds(ScreenShareSourceItem source)
        => source.IsScreen ? source.Bounds : GetWindowBounds(source.WindowHandle);

    private static int GetScreenShareTargetHeight(Rectangle bounds, int requestedHeight)
    {
        var targetHeight = EnsureEvenScreenShareDimension(requestedHeight);
        if (bounds.Height <= 0)
        {
            return targetHeight;
        }

        return EnsureEvenScreenShareDimension(Math.Min(targetHeight, bounds.Height));
    }

    private static int GetScreenShareTargetWidth(Rectangle bounds, int targetHeight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return EnsureEvenScreenShareDimension(targetHeight * 16 / 9);
        }

        var scale = (double)Math.Max(1, targetHeight) / bounds.Height;
        return EnsureEvenScreenShareDimension((int)Math.Round(bounds.Width * scale));
    }

    private static int EnsureEvenScreenShareDimension(int value)
    {
        var dimension = Math.Max(2, value);
        return dimension % 2 == 0 ? dimension : dimension - 1;
    }

    private static BitmapImage CreateBitmapImage(byte[] bytes, int decodePixelHeight = 0)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelHeight > 0)
        {
            image.DecodePixelHeight = decodePixelHeight;
        }

        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void UpdateScreenShareStageVisibility()
    {
        var hasScreenShare = _isScreenSharing || _peerScreenSharing;
        if (!hasScreenShare && _isScreenShareFocusMode)
        {
            ExitScreenShareFocusMode();
        }

        if (!hasScreenShare)
        {
            SetScreenShareFocusTarget(ScreenShareFocusTarget.Auto);
        }
        else
        {
            NormalizeScreenShareFocusTarget();
        }

        var shouldShow = hasScreenShare && !_screenSharePickerSuppressesStage;
        var showSelectedOnly = _isScreenShareFullscreenMode && _screenShareFocusTarget != ScreenShareFocusTarget.Auto;
        var showSelfTile = shouldShow && _isScreenSharing && (!showSelectedOnly || _screenShareFocusTarget == ScreenShareFocusTarget.Local);
        var showPeerTile = shouldShow && _peerScreenSharing && (!showSelectedOnly || _screenShareFocusTarget == ScreenShareFocusTarget.Peer);
        var visibleTileCount = (showSelfTile ? 1 : 0) + (showPeerTile ? 1 : 0);

        CallScreenShareStage.Visibility = hasScreenShare
            ? (_screenSharePickerSuppressesStage ? Visibility.Hidden : Visibility.Visible)
            : Visibility.Collapsed;
        CallSelfScreenTile.Visibility = showSelfTile ? Visibility.Visible : Visibility.Collapsed;
        CallPeerScreenTile.Visibility = showPeerTile ? Visibility.Visible : Visibility.Collapsed;
        CallScreenShareJoinOverlay.Visibility = Visibility.Collapsed;
        CallScreenShareGrid.Columns = visibleTileCount > 1 ? 2 : 1;
        CallScreenShareFullscreenControlButton.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;

        if (_isScreenShareFocusMode && shouldShow)
        {
            CallPanel.MaxHeight = double.PositiveInfinity;
            CallPanel.Height = double.NaN;
            CallContentPanel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            var availableWidth = GetAvailableScreenShareStageWidth(minWidth: 420);
            var reservedHeight = _isScreenShareFullscreenMode ? 0 : 112;
            var availableHeight = Math.Max(280, CallPanel.ActualHeight - reservedHeight);
            var aspect = visibleTileCount > 1 ? 32d / 9d : 16d / 9d;
            var targetWidth = availableWidth;
            var targetHeight = targetWidth / aspect;
            if (targetHeight > availableHeight)
            {
                targetHeight = availableHeight;
                targetWidth = targetHeight * aspect;
            }

            CallScreenShareStage.Width = Math.Max(1, Math.Min(availableWidth, targetWidth));
            CallScreenShareStage.Height = Math.Max(1, Math.Min(availableHeight, targetHeight));
            CallScreenShareStage.HorizontalAlignment = _isScreenShareFullscreenMode
                ? System.Windows.HorizontalAlignment.Center
                : System.Windows.HorizontalAlignment.Left;
            CallScreenShareFocusExitButton.Visibility = Visibility.Visible;
            CallScreenShareFullscreenButton.Visibility = Visibility.Visible;
            return;
        }

        CallPanel.MaxHeight = 260;
        CallPanel.Height = hasScreenShare ? 260 : 150;
        CallContentPanel.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        var compactWidth = visibleTileCount > 1 ? 520 : 300;
        CallScreenShareStage.Width = Math.Min(compactWidth, GetAvailableScreenShareStageWidth(minWidth: 220));
        CallScreenShareStage.Height = ScreenShareMaxCompactHeight;
        CallScreenShareStage.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        CallScreenShareFocusExitButton.Visibility = Visibility.Collapsed;
        CallScreenShareFocusExitButton.Opacity = 0;
        CallScreenShareFullscreenButton.Visibility = Visibility.Collapsed;
        CallScreenShareFullscreenButton.Opacity = 0;
    }

    private double GetAvailableScreenShareStageWidth(double minWidth)
    {
        var panelWidth = CallPanel.ActualWidth;
        if (double.IsNaN(panelWidth) || panelWidth <= 0)
        {
            return minWidth;
        }

        if (_isScreenShareFullscreenMode)
        {
            return Math.Max(minWidth, panelWidth);
        }

        var controlsWidth = CallControlsPanel.Visibility == Visibility.Visible
            && !_isScreenShareFullscreenMode
            ? CallControlsPanel.ActualWidth
            : 0;
        var availableWidth = panelWidth - controlsWidth - 56;
        return Math.Max(1, availableWidth);
    }

    private void EnterScreenShareFocusMode()
    {
        if (_isScreenShareFocusMode || (!_isScreenSharing && !_peerScreenSharing))
        {
            return;
        }

        _isScreenShareFocusMode = true;
        ChatHeaderRow.Height = new GridLength(0);
        ChatHeaderLineRow.Height = new GridLength(0);
        ComposerRow.Height = new GridLength(0);
        CallPanelRow.Height = new GridLength(1, GridUnitType.Star);
        CallPanelSplitterRow.Height = new GridLength(0);
        MessagesRow.Height = new GridLength(0);

        ChatHeaderPanel.Visibility = Visibility.Collapsed;
        ChatHeaderLine.Visibility = Visibility.Collapsed;
        ComposerPanel.Visibility = Visibility.Collapsed;
        MessagesList.Visibility = Visibility.Collapsed;
        CallPanelSplitter.Visibility = Visibility.Collapsed;
        CallPanel.Margin = new Thickness(0);

        UpdateScreenShareStageVisibility();
        FadeFocusExitButton(show: true);
        _ = Dispatcher.BeginInvoke(new Action(UpdateScreenShareStageVisibility), DispatcherPriority.Loaded);
    }

    private void SetScreenShareFullscreenMode(bool enabled)
    {
        if (!enabled)
        {
            SetScreenShareFocusTarget(ScreenShareFocusTarget.Auto);
        }

        if (_isScreenShareFullscreenMode == enabled)
        {
            return;
        }

        _isScreenShareFullscreenMode = enabled;
        ApplyScreenShareWindowFullscreen(enabled);
        SidebarColumn.Width = enabled ? new GridLength(0) : new GridLength(300);
        ChatTitle.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ChatSubtitle.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        CallTitleText.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        CallStatusText.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        CallParticipantsPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        CallPanel.Padding = enabled ? new Thickness(0) : new Thickness(14);
        CallPanel.BorderThickness = enabled ? new Thickness(0) : new Thickness(1);
        CallPanel.CornerRadius = enabled ? new CornerRadius(0) : new CornerRadius(8);
        CallPanel.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(enabled ? "#0b0c0f" : "#25272d"));
        CallScreenShareStage.Margin = enabled ? new Thickness(0) : new Thickness(0, 12, 0, 0);
        CallScreenShareStage.BorderThickness = enabled ? new Thickness(0) : new Thickness(1);
        CallScreenShareStage.CornerRadius = enabled ? new CornerRadius(0) : new CornerRadius(6);
        CallScreenShareGrid.Margin = enabled ? new Thickness(0) : new Thickness(6);
        ScreenShareWebView.Margin = enabled ? new Thickness(0) : new Thickness(6);

        if (enabled)
        {
            if (CallContentPanel.Children.Contains(CallParticipantsPanel))
            {
                CallContentPanel.Children.Remove(CallParticipantsPanel);
                CallContentPanel.Children.Add(CallParticipantsPanel);
            }

            Grid.SetColumn(CallControlsPanel, 0);
            CallControlsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            CallControlsPanel.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
            CallControlsPanel.Margin = new Thickness(0, 0, 0, 2);
            SetScreenShareFullscreenIcon(expanded: true);
            return;
        }

        if (CallContentPanel.Children.Contains(CallParticipantsPanel))
        {
            CallContentPanel.Children.Remove(CallParticipantsPanel);
            CallContentPanel.Children.Insert(2, CallParticipantsPanel);
        }

        Grid.SetColumn(CallControlsPanel, 1);
        CallControlsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        CallControlsPanel.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
        CallControlsPanel.Margin = new Thickness(0);
        SetScreenShareFullscreenIcon(expanded: false);
    }

    private void SetScreenShareFullscreenIcon(bool expanded)
    {
        var data = expanded
            ? "M7,7H5V5H11V11H9V8.4L6.4,11L5,9.6L7.6,7H7M17,7.6L19.6,5L21,6.4L18.4,9H21V11H15V5H17V7.6M9,15H11V21H5V19H7.6L5,16.4L6.4,15L9,17.6V15M15,15H17V17.6L19.6,15L21,16.4L18.4,19H21V21H15V15Z"
            : "M5,5H11V7H7V11H5V5M13,5H19V11H17V7H13V5M17,13H19V19H13V17H17V13M7,13V17H11V19H5V13H7Z";
        CallScreenShareFullscreenIcon.Data = Geometry.Parse(data);
        CallScreenShareFullscreenControlIcon.Data = Geometry.Parse(data);
    }

    private void ApplyScreenShareWindowFullscreen(bool enabled)
    {
        if (enabled)
        {
            if (!_screenShareWindowFullscreenApplied)
            {
                _screenSharePreviousWindowStyle = WindowStyle;
                _screenSharePreviousWindowState = WindowState;
                _screenSharePreviousResizeMode = ResizeMode;
                _screenShareWindowFullscreenApplied = true;
            }

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            return;
        }

        if (!_screenShareWindowFullscreenApplied)
        {
            return;
        }

        WindowState = WindowState.Normal;
        WindowStyle = _screenSharePreviousWindowStyle;
        ResizeMode = _screenSharePreviousResizeMode;
        WindowState = _screenSharePreviousWindowState;
        _screenShareWindowFullscreenApplied = false;
    }

    private void ExitScreenShareFocusMode()
    {
        if (!_isScreenShareFocusMode)
        {
            return;
        }

        SetScreenShareFullscreenMode(false);
        _isScreenShareFocusMode = false;
        ChatHeaderRow.Height = new GridLength(58);
        ChatHeaderLineRow.Height = new GridLength(1);
        ComposerRow.Height = GridLength.Auto;
        CallPanelRow.Height = GridLength.Auto;
        CallPanelSplitterRow.Height = GridLength.Auto;
        MessagesRow.Height = new GridLength(1, GridUnitType.Star);

        ChatHeaderPanel.Visibility = Visibility.Visible;
        ChatHeaderLine.Visibility = Visibility.Visible;
        ComposerPanel.Visibility = _selectedContact is null ? Visibility.Collapsed : Visibility.Visible;
        MessagesList.Visibility = Visibility.Visible;
        CallPanelSplitter.Visibility = CallPanel.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        CallPanel.Margin = new Thickness(0, 0, 0, 10);

        FadeFocusExitButton(show: false);
        UpdateScreenShareStageVisibility();
    }

    private void FadeFocusExitButton(bool show)
    {
        if (show)
        {
            CallScreenShareFocusExitButton.Visibility = Visibility.Visible;
            CallScreenShareFullscreenButton.Visibility = Visibility.Visible;
        }

        var animation = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 180 : 120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (!show)
        {
            animation.Completed += (_, _) =>
            {
                if (!_isScreenShareFocusMode)
                {
                    CallScreenShareFocusExitButton.Visibility = Visibility.Collapsed;
                    CallScreenShareFullscreenButton.Visibility = Visibility.Collapsed;
                }
            };
        }

        CallScreenShareFocusExitButton.BeginAnimation(OpacityProperty, animation);
        CallScreenShareFullscreenButton.BeginAnimation(OpacityProperty, animation.Clone());
    }

    private void UpdateScreenShareControlVisuals()
    {
        ScreenShareButton.ToolTip = _isScreenSharing ? "Stop screen share" : "Share screen";
        ScreenShareButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isScreenSharing ? "#2f4938" : "#3a3c43"));
        ScreenShareIcon.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isScreenSharing ? "#b7f7c8" : "#f2f3f5"));
    }

    private static IEnumerable<ScreenShareSourceItem> EnumerateShareableWindows()
    {
        var windows = new List<ScreenShareSourceItem>();
        using var currentProcess = Process.GetCurrentProcess();
        var currentProcessId = (uint)currentProcess.Id;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == currentProcessId)
            {
                return true;
            }

            var length = GetWindowTextLength(handle);
            if (length <= 0)
            {
                return true;
            }

            var titleBuilder = new StringBuilder(length + 1);
            var written = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString(0, written).Trim();
            if (string.IsNullOrWhiteSpace(title) || !TryGetWindowBounds(handle, out var bounds))
            {
                return true;
            }

            if (bounds.Width < 80 || bounds.Height < 60)
            {
                return true;
            }

            windows.Add(new ScreenShareSourceItem(title, $"{bounds.Width}x{bounds.Height}", false, handle, bounds, null));
            return true;
        }, IntPtr.Zero);

        return windows
            .GroupBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase);
    }

    private static Rectangle GetWindowBounds(IntPtr handle)
    {
        if (TryGetWindowBounds(handle, out var bounds))
        {
            return bounds;
        }

        return Rectangle.Empty;
    }

    private static bool TryGetWindowBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (DwmGetWindowAttribute(
                handle,
                DwmWindowAttributeExtendedFrameBounds,
                out var frameRect,
                Marshal.SizeOf<NativeRect>()) == 0 &&
            TryCreateRectangle(frameRect, out bounds))
        {
            return true;
        }

        return GetWindowRect(handle, out var rect) && TryCreateRectangle(rect, out bounds);
    }

    private static bool TryCreateRectangle(NativeRect rect, out Rectangle bounds)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = new Rectangle(rect.Left, rect.Top, width, height);
        return true;
    }

    private async Task SendCallEndBurstAsync(ContactViewModel contact)
    {
        await SendCallSignalAsync(contact, CallEndIntent);

        for (var i = 0; i < 2 && !_stop.IsCancellationRequested; i++)
        {
            try
            {
                await Task.Delay(180, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await SendCallSignalAsync(contact, CallEndIntent);
        }
    }

    private void HandleCallPacket(ChatPacket packet)
    {
        var senderContact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId && !x.IsGroup) ?? CreateContactFromPacket(packet);
        AddOrUpdateContact(senderContact);
        _ = _history.SaveContactAsync(senderContact);

        var contact = ResolveCallContact(packet, senderContact, out packet);

        switch (packet.Intent)
        {
            case CallInviteIntent:
                if (_activeCallContact?.UserId == contact.UserId &&
                    _activeCallState == "outgoing" &&
                    CallPanel.Visibility == Visibility.Visible)
                {
                    StopCallRingtone();
                    _selfInCall = true;
                    _peerInCall = true;
                    _activeCallState = "connected";
                    ShowCallPanel(contact, "Connected", showIncomingActions: false);
                    _ = SendCallSignalAsync(contact, CallAcceptIntent);
                    StartAudioCall(contact);
                    break;
                }

                if (_activeCallContact is not null &&
                    _activeCallContact.UserId != contact.UserId &&
                    CallPanel.Visibility == Visibility.Visible)
                {
                    _ = SendCallSignalAsync(contact, CallDeclineIntent);
                    break;
                }

                _activeCallContact = contact;
                _activeCallState = "incoming";
                _selfInCall = false;
                _peerInCall = true;
                _ = OpenContactAsync(contact);
                RestoreWindowForIncomingCall();
                ShowCallPanel(contact, "Incoming call", showIncomingActions: true);
                StartCallRingtone();
                ShowIncomingCallNotification(contact);
                break;
            case CallAcceptIntent:
                StopCallRingtone();
                _activeCallContact = contact;
                _selfInCall = true;
                _peerInCall = true;
                _activeCallState = "connected";
                ShowCallPanel(contact, "Connected", showIncomingActions: false);
                StartAudioCall(contact);
                NetworkStatusText.Text = $"{contact.DisplayName} accepted the call";
                break;
            case CallDeclineIntent:
                StopCallRingtone();
                HideCallPanel();
                NetworkStatusText.Text = $"{contact.DisplayName} declined the call";
                break;
            case CallEndIntent:
                StopCallRingtone();
                HideCallPanel();
                NetworkStatusText.Text = $"{contact.DisplayName} ended the call";
                break;
            case CallLeaveIntent:
                StopCallRingtone();
                HideCallPanel();
                NetworkStatusText.Text = $"{contact.DisplayName} ended the call";
                break;
            case CallJoinIntent:
                StopCallRingtone();
                _activeCallContact = contact;
                _peerInCall = true;
                if (_selfInCall)
                {
                    _activeCallState = "connected";
                    ShowCallPanel(contact, "Connected", showIncomingActions: false);
                    StartAudioCall(contact);
                    NetworkStatusText.Text = $"{contact.DisplayName} joined the call";
                }
                else
                {
                    _activeCallState = "left";
                    ShowCallPanel(contact, $"{contact.DisplayName} is in the call", showIncomingActions: false);
                    NetworkStatusText.Text = $"{contact.DisplayName} joined the call";
                }

                break;
            case CallAudioStateIntent:
                ApplyPeerCallAudioState(packet);
                break;
            case CallPingIntent:
                _ = SendCallPongAsync(contact, packet);
                break;
            case CallPongIntent:
                ApplyCallPong(packet);
                break;
            case CallScreenStartIntent:
            case CallScreenFrameIntent:
            case CallScreenStopIntent:
                HandleScreenSharePacket(packet);
                break;
            case CallScreenWebRtcOfferIntent:
            case CallScreenWebRtcAnswerIntent:
            case CallScreenWebRtcIceIntent:
                HandleScreenShareWebRtcSignal(packet);
                break;
            case CallScreenWebRtcFallbackIntent:
                HandleScreenShareWebRtcFallbackRequest(packet);
                break;
        }
    }

    private ContactViewModel ResolveCallContact(ChatPacket packet, ContactViewModel senderContact, out ChatPacket effectivePacket)
    {
        effectivePacket = packet;
        if (!TryGetGroupCallSignal(packet.Body, out var groupId, out var innerBody))
        {
            return senderContact;
        }

        effectivePacket = packet with { Body = innerBody };
        var group = _contacts.FirstOrDefault(x => x.IsGroup && string.Equals(x.UserId, groupId, StringComparison.Ordinal));
        if (group is not null)
        {
            return group;
        }

        AppLog.Write($"Group call signal ignored as direct fallback: missing group={groupId}, from={packet.FromUserId}, intent={packet.Intent}");
        return senderContact;
    }

    private static bool TryGetGroupCallSignal(string body, out string groupId, out string innerBody)
    {
        groupId = "";
        innerBody = body;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CallGroupSignalPayload>(body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.GroupId))
            {
                return false;
            }

            groupId = payload.GroupId;
            innerBody = payload.Body ?? "";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool IsActiveCallPeer(string userId)
    {
        var contact = _activeCallContact;
        if (contact is null || string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (!contact.IsGroup)
        {
            return string.Equals(contact.UserId, userId, StringComparison.Ordinal);
        }

        return _activeCallPeerUserIds.Contains(userId);
    }

    private void RefreshActiveCallPeerCache(ContactViewModel contact)
    {
        _activeCallPeerUserIds.Clear();
        if (_profile is null)
        {
            _activeCallTargetUserIds = [];
            return;
        }

        if (!contact.IsGroup)
        {
            _activeCallPeerUserIds.Add(contact.UserId);
            _activeCallTargetUserIds = [contact.UserId];
            return;
        }

        var memberIds = LoadGroupMembers(contact)
            .Select(x => x.UserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var memberId in memberIds)
        {
            _activeCallPeerUserIds.Add(memberId);
        }

        _activeCallTargetUserIds = memberIds
            .Where(x => !string.Equals(x, _profile.UserId, StringComparison.Ordinal))
            .ToArray();
    }

    private void HandleScreenShareWebRtcFallbackRequest(ChatPacket packet)
    {
        if (!IsActiveCallPeer(packet.FromUserId) ||
            !_isScreenSharing ||
            (!_screenShareUsingNativeWebRtc && !_screenShareUsingEncodedWebRtc))
        {
            return;
        }

        var reason = string.IsNullOrWhiteSpace(packet.Body)
            ? "peer requested compatible screen share"
            : packet.Body;
        AppLog.Write($"Screen share WebRTC fallback requested by peer: from={packet.FromUserId}, reason={reason}");
        FallbackScreenShareFromWebRtc($"peer requested compatible screen share: {reason}");
    }

    private void ApplyPeerCallAudioState(ChatPacket packet)
    {
        try
        {
            var state = JsonSerializer.Deserialize<CallAudioState>(packet.Body);
            if (state is null)
            {
                return;
            }

            _peerMicrophoneMuted = state.MicrophoneMuted;
            _peerHeadphonesMuted = state.HeadphonesMuted;
            UpdateCallAudioControlVisuals(animate: true);
            AppLog.Write($"Call audio state received: from={packet.FromUserId}, micMuted={_peerMicrophoneMuted}, headphonesMuted={_peerHeadphonesMuted}");
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, $"Call audio state parse failed: from={packet.FromUserId}");
        }
    }

    private void CallNetworkMetricsTimer_OnTick(object? sender, EventArgs e)
    {
        var contact = _activeCallContact;
        if (contact is null ||
            _activeCallState != "connected" ||
            !_selfInCall ||
            CallPanel.Visibility != Visibility.Visible)
        {
            StopCallNetworkMetrics();
            return;
        }

        PruneExpiredCallPings();
        UpdateCallNetworkMetrics();
        _ = SendCallNetworkPingIfDueAsync(contact);
    }

    private void ResetCallNetworkMetrics()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(ResetCallNetworkMetrics));
            return;
        }

        _pendingCallPings.Clear();
        Interlocked.Exchange(ref _callPingSequence, 0);
        Interlocked.Exchange(ref _lastCallPingSentTicks, 0);
        Interlocked.Exchange(ref _callAudioSendSequence, 0);
        ResetCallAudioLossWindow();
        Interlocked.Exchange(ref _sequencedCallAudioPackets, 0);
        Interlocked.Exchange(ref _lostCallAudioPackets, 0);
        Interlocked.Exchange(ref _peerAudioSentFramesBaseline, -1);
        Interlocked.Exchange(ref _localAudioReceivedFramesBaseline, -1);
        Interlocked.Exchange(ref _peerAudioSentFramesLatest, 0);
        Interlocked.Exchange(ref _peerAudioSentFramesMaturedLatest, 0);
        _peerAudioSendReports.Clear();
        _currentCallPingMs = double.NaN;
        _averageCallPingMs = 0;
        _callPingSamples = 0;
        UpdateCallNetworkMetrics();
    }

    private void StartCallNetworkMetrics()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(StartCallNetworkMetrics));
            return;
        }

        if (!_callNetworkMetricsTimer.IsEnabled)
        {
            _callNetworkMetricsTimer.Start();
        }

        if (CallNetworkStatsCard.Visibility == Visibility.Visible)
        {
            AnimateCallNetworkStatsCard();
        }

        if (_activeCallContact is not null)
        {
            _ = SendCallNetworkPingIfDueAsync(_activeCallContact, force: true);
        }
    }

    private void StopCallNetworkMetrics()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(StopCallNetworkMetrics));
            return;
        }

        _callNetworkMetricsTimer.Stop();
        _pendingCallPings.Clear();
    }

    private void AnimateCallNetworkStatsCard()
    {
        CallNetworkStatsCard.BeginAnimation(OpacityProperty, null);
        CallNetworkStatsCard.Opacity = 0;
        CallNetworkStatsCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private async Task SendCallNetworkPingIfDueAsync(ContactViewModel contact, bool force = false)
    {
        if (_profile is null ||
            _relayClient is null ||
            !_relayClient.IsConnected ||
            _activeCallContact?.UserId != contact.UserId ||
            _activeCallState != "connected" ||
            !_selfInCall)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastCallPingSentTicks);
        if (!force &&
            lastTicks > 0 &&
            now - new DateTimeOffset(lastTicks, TimeSpan.Zero) < CallNetworkPingInterval)
        {
            return;
        }

        Interlocked.Exchange(ref _lastCallPingSentTicks, now.Ticks);
        var sequence = Interlocked.Increment(ref _callPingSequence);
        _pendingCallPings[sequence] = now;

        try
        {
            var body = JsonSerializer.Serialize(new CallNetworkPingPayload(sequence, now, Interlocked.Read(ref _sentAudioFrames)));
            await SendCallControlAsync(contact, CallPingIntent, body, log: false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            _pendingCallPings.Remove(sequence);
            AppLog.Write(ex, $"Call network ping failed: to={contact.UserId}");
        }
    }

    private async Task SendCallPongAsync(ContactViewModel contact, ChatPacket packet)
    {
        if (_profile is null ||
            _relayClient is null ||
            !_relayClient.IsConnected)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CallNetworkPingPayload>(packet.Body);
            if (payload is null || payload.Sequence <= 0)
            {
                return;
            }

            ApplyPeerAudioSendReport(packet.FromUserId, payload.AudioFramesSent);

            var responsePayload = payload with { AudioFramesSent = Interlocked.Read(ref _sentAudioFrames) };
            var body = JsonSerializer.Serialize(responsePayload);
            var responseTarget = contact.IsGroup
                ? _contacts.FirstOrDefault(x => !x.IsGroup && string.Equals(x.UserId, packet.FromUserId, StringComparison.Ordinal))
                  ?? CreateContactFromPacket(packet)
                : contact;
            var response = contact.IsGroup
                ? CreateProfilePacket(packet.FromUserId, body, CallPongIntent, packet.FromRelayServer)
                : CreateCallPacket(contact, body, CallPongIntent);
            await SendOverRelayAsync(response, responseTarget, log: false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call network pong failed: to={contact.UserId}");
        }
    }

    private void ApplyCallPong(ChatPacket packet)
    {
        if (!IsActiveCallPeer(packet.FromUserId) ||
            _activeCallState != "connected")
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CallNetworkPingPayload>(packet.Body);
            if (payload is null ||
                payload.Sequence <= 0 ||
                !_pendingCallPings.Remove(payload.Sequence, out var sentAtUtc))
            {
                return;
            }

            ApplyPeerAudioSendReport(packet.FromUserId, payload.AudioFramesSent);
            _currentCallPingMs = Math.Max(0, (DateTimeOffset.UtcNow - sentAtUtc).TotalMilliseconds);
            _callPingSamples++;
            _averageCallPingMs += (_currentCallPingMs - _averageCallPingMs) / _callPingSamples;
            UpdateCallNetworkMetrics();
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, $"Call network pong parse failed: from={packet.FromUserId}");
        }
    }

    private void PruneExpiredCallPings()
    {
        if (_pendingCallPings.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var item in _pendingCallPings.ToArray())
        {
            if (now - item.Value >= CallNetworkPingTimeout)
            {
                _pendingCallPings.Remove(item.Key);
            }
        }
    }

    private void ResetCallAudioLossWindow()
    {
        lock (_callAudioLossGate)
        {
            _receivedCallAudioSequences.Clear();
            _callAudioLossCursor = 0;
        }
    }

    private bool TrackReceivedCallAudioSequence(long sequence)
    {
        if (sequence <= 0)
        {
            return true;
        }

        lock (_callAudioLossGate)
        {
            if (_callAudioLossCursor == 0)
            {
                _callAudioLossCursor = sequence - 1;
            }

            if (sequence <= _callAudioLossCursor ||
                !_receivedCallAudioSequences.Add(sequence))
            {
                return false;
            }

            Interlocked.Increment(ref _sequencedCallAudioPackets);
            var highestSequence = _receivedCallAudioSequences.Max;
            var matureThrough = highestSequence - CallAudioLossSequenceDelay;
            while (_callAudioLossCursor < matureThrough)
            {
                _callAudioLossCursor++;
                if (!_receivedCallAudioSequences.Remove(_callAudioLossCursor))
                {
                    Interlocked.Increment(ref _lostCallAudioPackets);
                }
            }

            return true;
        }
    }

    private void ApplyPeerAudioSendReport(string fromUserId, long audioFramesSent)
    {
        if (!IsActiveCallPeer(fromUserId) ||
            _activeCallState != "connected" ||
            audioFramesSent <= 0)
        {
            return;
        }

        var localReceived = GetIncomingCallAudioFrameCount();
        var baseline = Interlocked.Read(ref _peerAudioSentFramesBaseline);
        if (baseline < 0 || audioFramesSent < baseline)
        {
            Interlocked.Exchange(ref _peerAudioSentFramesBaseline, audioFramesSent);
            Interlocked.Exchange(ref _localAudioReceivedFramesBaseline, localReceived);
            Interlocked.Exchange(ref _peerAudioSentFramesMaturedLatest, audioFramesSent);
            _peerAudioSendReports.Clear();
        }

        Interlocked.Exchange(ref _peerAudioSentFramesLatest, audioFramesSent);
        _peerAudioSendReports.Enqueue(new CallAudioSendReport(audioFramesSent, DateTimeOffset.UtcNow));
        while (_peerAudioSendReports.Count > 16)
        {
            _peerAudioSendReports.Dequeue();
        }
    }

    private long GetIncomingCallAudioFrameCount()
        => Interlocked.Read(ref _relayReceivedAudioFrames) +
           Interlocked.Read(ref _tcpReceivedAudioFrames) +
           Interlocked.Read(ref _legacyAudioFrames);

    private void UpdateMaturedPeerAudioSendReports()
    {
        var now = DateTimeOffset.UtcNow;
        while (_peerAudioSendReports.Count > 0 &&
               now - _peerAudioSendReports.Peek().ReceivedAtUtc >= CallAudioLossReportDelay)
        {
            var report = _peerAudioSendReports.Dequeue();
            Interlocked.Exchange(ref _peerAudioSentFramesMaturedLatest, report.AudioFramesSent);
        }
    }

    private void UpdateCallNetworkMetrics()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(UpdateCallNetworkMetrics));
            return;
        }

        CallCurrentPingText.Text = double.IsNaN(_currentCallPingMs)
            ? "--"
            : $"{_currentCallPingMs:0} ms";
        CallAveragePingText.Text = _callPingSamples == 0
            ? "--"
            : $"{_averageCallPingMs:0} ms";

        var sequenced = Interlocked.Read(ref _sequencedCallAudioPackets);
        var lost = Interlocked.Read(ref _lostCallAudioPackets);
        var total = sequenced + lost;
        if (total == 0)
        {
            UpdateMaturedPeerAudioSendReports();
            var peerSentBaseline = Interlocked.Read(ref _peerAudioSentFramesBaseline);
            var localReceivedBaseline = Interlocked.Read(ref _localAudioReceivedFramesBaseline);
            var peerSentMatured = Interlocked.Read(ref _peerAudioSentFramesMaturedLatest);
            if (peerSentBaseline >= 0 && localReceivedBaseline >= 0 && peerSentMatured > peerSentBaseline + 25)
            {
                var reportedSent = peerSentMatured - peerSentBaseline;
                var localReceived = Math.Max(0, GetIncomingCallAudioFrameCount() - localReceivedBaseline);
                total = reportedSent;
                lost = Math.Max(0, reportedSent - localReceived);
            }
        }

        var lossPercent = total == 0
            ? double.NaN
            : lost * 100d / total;

        CallPacketLossText.Text = double.IsNaN(lossPercent)
            ? "--"
            : $"{lossPercent:0.#}%";

        var scorePing = double.IsNaN(_currentCallPingMs) ? _averageCallPingMs : _currentCallPingMs;
        var hasPing = _callPingSamples > 0;
        var hasLoss = total > 0;
        var quality = "Waiting";
        var color = "#6c7080";

        if (hasPing || hasLoss)
        {
            if ((hasLoss && lossPercent >= 5) || (hasPing && scorePing >= 350))
            {
                quality = "Poor";
                color = "#ff6b6b";
            }
            else if ((hasLoss && lossPercent >= 1) || (hasPing && scorePing >= 150))
            {
                quality = "Unstable";
                color = "#f1c40f";
            }
            else
            {
                quality = "Stable";
                color = "#57f287";
            }
        }

        CallNetworkQualityText.Text = quality;
        CallNetworkQualityDot.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void ShowCallPanel(ContactViewModel contact, string status, bool showIncomingActions)
    {
        RefreshActiveCallPeerCache(contact);
        CallTitleText.Text = contact.DisplayName;
        CallStatusText.Text = status;
        ApplyCallAvatarVisuals(contact);
        AcceptCallButton.Visibility = showIncomingActions ? Visibility.Visible : Visibility.Collapsed;
        DeclineCallButton.Visibility = showIncomingActions ? Visibility.Visible : Visibility.Collapsed;
        JoinCallButton.Visibility = !showIncomingActions && !_selfInCall && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
        MicMuteButton.Visibility = !showIncomingActions && _selfInCall ? Visibility.Visible : Visibility.Collapsed;
        HeadphonesMuteButton.Visibility = !showIncomingActions && _selfInCall ? Visibility.Visible : Visibility.Collapsed;
        SoundboardButton.Visibility = !showIncomingActions && _selfInCall && status == "Connected" ? Visibility.Visible : Visibility.Collapsed;
        ScreenShareButton.Visibility = !showIncomingActions && _selfInCall && status == "Connected" && !contact.IsGroup ? Visibility.Visible : Visibility.Collapsed;
        EndCallButton.Visibility = showIncomingActions || (!_selfInCall && !_peerInCall) ? Visibility.Collapsed : Visibility.Visible;
        CallNetworkStatsCard.Visibility = !showIncomingActions && _selfInCall && status == "Connected" ? Visibility.Visible : Visibility.Collapsed;
        UpdateCallAudioControlVisuals(animate: false);
        UpdateCallNetworkMetrics();
        UpdateScreenShareControlVisuals();
        UpdateScreenShareStageVisibility();
        CallPanel.Visibility = Visibility.Visible;
        CallPanelSplitter.Visibility = Visibility.Visible;
    }

    private void UpdateCallAudioControlVisuals(bool animate)
    {
        MicMuteButton.ToolTip = _isMicrophoneMuted ? "Unmute microphone" : "Mute microphone";
        HeadphonesMuteButton.ToolTip = _isHeadphonesMuted ? "Undeafen" : "Deafen";

        MicMuteButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isMicrophoneMuted ? "#4a2d31" : "#3a3c43"));
        HeadphonesMuteButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isHeadphonesMuted ? "#4a2d31" : "#3a3c43"));
        MicMuteIcon.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isMicrophoneMuted ? "#ffb4b4" : "#f2f3f5"));
        HeadphonesMuteIcon.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_isHeadphonesMuted ? "#ffb4b4" : "#f2f3f5"));

        SetSlashState(MicMuteSlash, MicMuteIconScale, _isMicrophoneMuted, animate);
        SetSlashState(HeadphonesMuteSlash, HeadphonesMuteIconScale, _isHeadphonesMuted, animate);

        CallSelfMicBadge.Visibility = _isMicrophoneMuted && _selfInCall ? Visibility.Visible : Visibility.Collapsed;
        CallSelfHeadphonesBadge.Visibility = _isHeadphonesMuted && _selfInCall ? Visibility.Visible : Visibility.Collapsed;
        CallSelfAudioBadges.Visibility = (_isMicrophoneMuted || _isHeadphonesMuted) && _selfInCall ? Visibility.Visible : Visibility.Collapsed;

        var peerPreference = _activeCallContact is { IsGroup: false }
            ? _callAudioPreferences.Get(_activeCallContact.UserId)
            : null;
        var locallyMuted = peerPreference?.IsMuted == true;
        CallPeerMicBadge.Visibility = (_peerMicrophoneMuted || locallyMuted) && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
        CallPeerMicBadge.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(locallyMuted ? "#6d6f78" : "#e5484d"));
        CallPeerHeadphonesBadge.Visibility = _peerHeadphonesMuted && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
        CallPeerAudioBadges.Visibility = (_peerMicrophoneMuted || _peerHeadphonesMuted || locallyMuted) && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetSlashState(System.Windows.Shapes.Path slash, ScaleTransform scale, bool isCrossed, bool animate)
    {
        if (!animate)
        {
            slash.Opacity = isCrossed ? 1 : 0;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }

        scale.ScaleX = 0.82;
        scale.ScaleY = 0.82;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scaleAnimation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = easing
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        slash.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(isCrossed ? 1 : 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = easing
            });
    }

    private void ApplyCallAvatarVisuals(ContactViewModel contact)
    {
        CallSelfParticipant.Visibility = _selfInCall ? Visibility.Visible : Visibility.Collapsed;
        CallPeerParticipant.Visibility = _peerInCall ? Visibility.Visible : Visibility.Collapsed;

        CallSelfInitials.Text = _profile is null ? "ME" : GetInitials(_profile.DisplayName);
        CallPeerInitials.Text = contact.Initials;

        ApplyCallProfileAvatar();
        ApplyCallContactAvatar(contact);
    }

    private void ApplyCallProfileAvatar()
    {
        CallSelfAvatarImage.Visibility = Visibility.Collapsed;
        CallSelfAvatarVideo.Visibility = Visibility.Collapsed;
        CallSelfAvatarVideo.Stop();
        CallSelfInitials.Visibility = Visibility.Visible;
        CallSelfAvatarColor.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_profile?.AvatarColor ?? "#5865f2"));

        if (_profile is null || string.IsNullOrWhiteSpace(_profile.AvatarPath) || !File.Exists(_profile.AvatarPath))
        {
            return;
        }

        if (_profile.AvatarKind == "image")
        {
            CallSelfAvatarImage.Source = LoadBitmap(_profile.AvatarPath);
            CallSelfAvatarImage.Visibility = Visibility.Visible;
            CallSelfInitials.Visibility = Visibility.Collapsed;
            return;
        }

        if (_profile.AvatarKind == "video")
        {
            CallSelfAvatarVideo.Source = new Uri(_profile.AvatarPath, UriKind.Absolute);
            CallSelfAvatarVideo.Visibility = Visibility.Visible;
            CallSelfInitials.Visibility = Visibility.Collapsed;
            CallSelfAvatarVideo.Position = TimeSpan.FromSeconds(Math.Max(0, _profile.AvatarVideoStartSeconds));
            CallSelfAvatarVideo.Play();
        }
    }

    private void ApplyCallContactAvatar(ContactViewModel contact)
    {
        CallPeerAvatarImage.Visibility = Visibility.Collapsed;
        CallPeerAvatarVideo.Visibility = Visibility.Collapsed;
        CallPeerAvatarVideo.Stop();
        CallPeerInitials.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(contact.AvatarPath) || !File.Exists(contact.AvatarPath))
        {
            return;
        }

        if (contact.AvatarKind == "image")
        {
            CallPeerAvatarImage.Source = LoadBitmap(contact.AvatarPath);
            CallPeerAvatarImage.Visibility = Visibility.Visible;
            CallPeerInitials.Visibility = Visibility.Collapsed;
            return;
        }

        if (contact.AvatarKind == "video")
        {
            CallPeerAvatarVideo.Source = new Uri(contact.AvatarPath, UriKind.Absolute);
            CallPeerAvatarVideo.Visibility = Visibility.Visible;
            CallPeerInitials.Visibility = Visibility.Collapsed;
            CallPeerAvatarVideo.Position = TimeSpan.FromSeconds(Math.Max(0, contact.AvatarVideoStartSeconds));
            CallPeerAvatarVideo.Play();
        }
    }

    private void HideCallPanel()
    {
        StopCallRingtone();
        SoundboardPopup.IsOpen = false;
        StopActiveSoundboard();
        StopScreenShare(sendSignal: false);
        StopAudioCall();
        StopCallNetworkMetrics();
        _activeCallContact = null;
        _activeCallState = "";
        _selfInCall = false;
        _peerInCall = false;
        _isMicrophoneMuted = false;
        _isHeadphonesMuted = false;
        _peerMicrophoneMuted = false;
        _peerHeadphonesMuted = false;
        _peerScreenSharing = false;
        _isWatchingPeerScreen = false;
        _peerScreenShareUsingWebRtc = false;
        _screenShareWebRtcActive = false;
        _activeCallPeerUserIds.Clear();
        _activeCallTargetUserIds = [];
        ScreenSharePickerOverlay.Visibility = Visibility.Collapsed;
        SetScreenSharePickerStageSuppression(false);
        PostScreenShareWebRtcMessage(new { type = "stop" });
        SetScreenShareWebRtcVisible(false);
        ClearQueuedPeerScreenShareFrames();
        CallSelfScreenSharePreview.Source = null;
        CallPeerScreenSharePreview.Source = null;
        ExitScreenShareFocusMode();
        UpdateCallAudioControlVisuals(animate: false);
        UpdateScreenShareControlVisuals();
        UpdateScreenShareStageVisibility();
        CallPanel.Visibility = Visibility.Collapsed;
        CallPanelSplitter.Visibility = Visibility.Collapsed;
    }

    private void LeaveActiveCall(ContactViewModel contact)
    {
        _selfInCall = false;
        SoundboardPopup.IsOpen = false;
        StopActiveSoundboard();
        StopScreenShare(sendSignal: true);
        StopAudioCall();
        StopCallNetworkMetrics();
        _activeCallState = _peerInCall ? "left" : "";

        if (_peerInCall)
        {
            ShowCallPanel(contact, "You left the call", showIncomingActions: false);
            NetworkStatusText.Text = $"You left the call with {contact.DisplayName}";
            return;
        }

        HideCallPanel();
    }

    private void StartCallRingtone()
    {
        System.Media.SystemSounds.Exclamation.Play();
        _callRingtoneTimer.Start();
    }

    private void StopCallRingtone()
    {
        _callRingtoneTimer.Stop();
    }

    private void StartAudioCall(ContactViewModel contact)
    {
        int generation;
        lock (_audioStartGate)
        {
            if (_activeCallContact?.UserId != contact.UserId || _activeCallState != "connected")
            {
                return;
            }

            if (_audioCall is not null || string.Equals(_audioStartingPeerId, contact.UserId, StringComparison.Ordinal))
            {
                AppLog.Write($"Call audio start skipped: peer={contact.UserId}, alreadyStarting={_audioStartingPeerId is not null}, alreadyStarted={_audioCall is not null}");
                return;
            }

            generation = unchecked(++_audioStartGeneration);
            _audioStartingPeerId = contact.UserId;
        }

        StopAudioCall(invalidatePendingStart: false);
        ResetCallNetworkMetrics();
        StartCallNetworkMetrics();
        _ = Task.Run(() => StartAudioCallCore(contact, generation));
    }

    private void StartAudioCallCore(ContactViewModel contact, int generation)
    {
        AudioCallSession? session = null;
        try
        {
            if (!IsAudioStartCurrent(contact, generation))
            {
                return;
            }

            Interlocked.Exchange(ref _sentAudioFrames, 0);
            Interlocked.Exchange(ref _relayReceivedAudioFrames, 0);
            Interlocked.Exchange(ref _tcpReceivedAudioFrames, 0);
            Interlocked.Exchange(ref _receivedAudioFrames, 0);
            Interlocked.Exchange(ref _legacyAudioFrames, 0);
            Interlocked.Exchange(ref _failedPlaybackFrames, 0);
            Interlocked.Exchange(ref _droppedPlaybackQueueFrames, 0);
            Interlocked.Exchange(ref _quietPlaybackFrames, 0);
            Interlocked.Exchange(ref _quietCaptureFrames, 0);
            Interlocked.Exchange(ref _capturedAudioFrames, 0);
            Interlocked.Exchange(ref _droppedAudioFrames, 0);
            Interlocked.Exchange(ref _audioSendTimeouts, 0);
            Interlocked.Exchange(ref _opusEncodedAudioFrames, 0);
            Interlocked.Exchange(ref _opusDecodedAudioFrames, 0);
            Interlocked.Exchange(ref _opusFecRecoveredAudioFrames, 0);
            Interlocked.Exchange(ref _opusConcealedAudioFrames, 0);
            Interlocked.Exchange(ref _lastAudioPingTicks, 0);
            Interlocked.Exchange(ref _lastRelayAudioReceivedTicks, 0);
            Interlocked.Exchange(ref _udpAudioWarningShown, 0);
            Interlocked.Exchange(ref _callAudioSendSequence, 0);
            ResetCallAudioLossWindow();
            Interlocked.Exchange(ref _sequencedCallAudioPackets, 0);
            Interlocked.Exchange(ref _lostCallAudioPackets, 0);
            Interlocked.Exchange(ref _peerAudioSentFramesBaseline, -1);
            Interlocked.Exchange(ref _localAudioReceivedFramesBaseline, -1);
            Interlocked.Exchange(ref _peerAudioSentFramesLatest, 0);
            Interlocked.Exchange(ref _peerAudioSentFramesMaturedLatest, 0);
            _noiseFloorRms = 90;
            _noiseGateGain = 1;
            lock (_audioFrameGate)
            {
                _pendingAudioCaptureFrames.Clear();
            }
            ClearCallPlaybackQueue();
            ClearCallJitterBuffer();
            ResetCallOpusCodec();

            session = new AudioCallSession(_settings.AudioInputDeviceId, _settings.AudioOutputDeviceId);
            session.AudioCaptured += StoreCapturedCallAudioFrame;
            session.Start();

            CancellationTokenSource sendLoopStop;
            CancellationTokenSource playbackLoopStop;
            lock (_audioStartGate)
            {
                if (_audioStartGeneration != generation ||
                    !string.Equals(_audioStartingPeerId, contact.UserId, StringComparison.Ordinal) ||
                    _activeCallContact?.UserId != contact.UserId ||
                    _activeCallState != "connected" ||
                    _audioCall is not null)
                {
                    session.AudioCaptured -= StoreCapturedCallAudioFrame;
                    session.Dispose();
                    AppLog.Write($"Call audio start discarded: peer={contact.UserId}, generation={generation}");
                    return;
                }

                sendLoopStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                playbackLoopStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _audioSendLoopStop = sendLoopStop;
                _audioPlaybackLoopStop = playbackLoopStop;
                _audioCall = session;
                _audioStartingPeerId = null;
            }

            _ = Task.Run(() => SendCallAudioLoopAsync(contact, sendLoopStop.Token));
            _ = Task.Run(() => PlayCallAudioLoopAsync(playbackLoopStop.Token));
            _ = SendCallAudioPingAsync(contact, CancellationToken.None);
            _ = SendCallAudioStateAsync();
            AppLog.Write($"Call audio started: peer={contact.UserId}, input={_settings.AudioInputDeviceId}, output={_settings.AudioOutputDeviceId}");
        }
        catch (Exception ex)
        {
            if (session is not null)
            {
                session.AudioCaptured -= StoreCapturedCallAudioFrame;
                if (!ReferenceEquals(_audioCall, session))
                {
                    session.Dispose();
                }
            }

            StopAudioCall();
            AppLog.Write(ex, $"Call audio failed: peer={contact.UserId}");
            _ = Dispatcher.BeginInvoke(new Action(() => NetworkStatusText.Text = $"Call connected, but audio failed: {ex.Message}"));
        }
    }

    private bool IsAudioStartCurrent(ContactViewModel contact, int generation)
    {
        lock (_audioStartGate)
        {
            return _audioStartGeneration == generation &&
                   string.Equals(_audioStartingPeerId, contact.UserId, StringComparison.Ordinal) &&
                   _activeCallContact?.UserId == contact.UserId &&
                   _activeCallState == "connected";
        }
    }

    private void StopAudioCall(bool invalidatePendingStart = true)
    {
        StopCallNetworkMetrics();

        if (invalidatePendingStart)
        {
            lock (_audioStartGate)
            {
                _audioStartingPeerId = null;
                unchecked
                {
                    _audioStartGeneration++;
                }
            }
        }

        var session = Interlocked.Exchange(ref _audioCall, null);
        if (session is null)
        {
            return;
        }

        var sendLoopStop = Interlocked.Exchange(ref _audioSendLoopStop, null);
        if (sendLoopStop is not null)
        {
            sendLoopStop.Cancel();
            sendLoopStop.Dispose();
        }

        var playbackLoopStop = Interlocked.Exchange(ref _audioPlaybackLoopStop, null);
        if (playbackLoopStop is not null)
        {
            playbackLoopStop.Cancel();
            playbackLoopStop.Dispose();
        }
        ClearCallPlaybackQueue();
        ClearCallJitterBuffer();
        ResetCallOpusCodec();

        session.AudioCaptured -= StoreCapturedCallAudioFrame;
        Interlocked.Exchange(ref _pendingAudioFrame, 0);

        var captured = Interlocked.Read(ref _capturedAudioFrames);
        var dropped = Interlocked.Read(ref _droppedAudioFrames);
        var sent = Interlocked.Read(ref _sentAudioFrames);
        var relayReceived = Interlocked.Read(ref _relayReceivedAudioFrames);
        var tcpReceived = Interlocked.Read(ref _tcpReceivedAudioFrames);
        var received = Interlocked.Read(ref _receivedAudioFrames);
        var legacy = Interlocked.Read(ref _legacyAudioFrames);
        var failedPlayback = Interlocked.Read(ref _failedPlaybackFrames);
        var droppedPlayback = Interlocked.Read(ref _droppedPlaybackQueueFrames);
        var quietPlayback = Interlocked.Read(ref _quietPlaybackFrames);
        var quietCapture = Interlocked.Read(ref _quietCaptureFrames);
        var sendTimeouts = Interlocked.Read(ref _audioSendTimeouts);
        var opusEncoded = Interlocked.Read(ref _opusEncodedAudioFrames);
        var opusDecoded = Interlocked.Read(ref _opusDecodedAudioFrames);
        var opusFecRecovered = Interlocked.Read(ref _opusFecRecoveredAudioFrames);
        var opusConcealed = Interlocked.Read(ref _opusConcealedAudioFrames);
        AppLog.Write($"Call audio stopping: capturedFrames={captured}, droppedFrames={dropped}, sentFrames={sent}, relayReceivedFrames={relayReceived}, tcpReceivedFrames={tcpReceived}, receivedFrames={received}, legacyFrames={legacy}, failedPlaybackFrames={failedPlayback}, droppedPlaybackQueueFrames={droppedPlayback}, quietPlaybackFrames={quietPlayback}, quietCaptureFrames={quietCapture}, sendTimeouts={sendTimeouts}, opusEncodedFrames={opusEncoded}, opusDecodedFrames={opusDecoded}, opusFecRecoveredFrames={opusFecRecovered}, opusConcealedFrames={opusConcealed}");

        _ = Task.Run(() =>
        {
            try
            {
                session.Dispose();
                AppLog.Write($"Call audio stopped: capturedFrames={captured}, droppedFrames={dropped}, sentFrames={sent}, relayReceivedFrames={relayReceived}, tcpReceivedFrames={tcpReceived}, receivedFrames={received}, legacyFrames={legacy}, failedPlaybackFrames={failedPlayback}, droppedPlaybackQueueFrames={droppedPlayback}, quietPlaybackFrames={quietPlayback}, quietCaptureFrames={quietCapture}, sendTimeouts={sendTimeouts}, opusEncodedFrames={opusEncoded}, opusDecodedFrames={opusDecoded}, opusFecRecoveredFrames={opusFecRecovered}, opusConcealedFrames={opusConcealed}");
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, "Call audio stop failed");
            }
        });
    }

    private void StoreCapturedCallAudioFrame(byte[] pcm)
    {
        var captured = Interlocked.Increment(ref _capturedAudioFrames);
        lock (_audioFrameGate)
        {
            while (_pendingAudioCaptureFrames.Count >= CallAudioMaxCaptureQueueFrames)
            {
                Interlocked.Increment(ref _droppedAudioFrames);
                _pendingAudioCaptureFrames.Dequeue();
            }

            _pendingAudioCaptureFrames.Enqueue(pcm);
        }

        if (captured == 1 || captured % 250 == 0)
        {
            AppLog.Write($"Call audio captured: frames={captured}, bytes={pcm.Length}, peak={GetPcmPeak(pcm)}");
        }

        PlayActiveCallVoiceTestFrame(pcm);
    }

    private void PlayActiveCallVoiceTestFrame(byte[] pcm)
    {
        var session = _audioCall;
        if (!_isVoiceTestActive ||
            _voiceTestSession is not null ||
            _activeCallState != "connected" ||
            session is null)
        {
            return;
        }

        var peak = GetPcmPeak(pcm);
        var playbackPcm = AmplifyPcm(pcm, peak, out _);
        if (!session.Play(playbackPcm, out var error))
        {
            Dispatcher.BeginInvoke(new Action(() => VoiceTestStatusText.Text = $"Playback failed: {error}"));
            return;
        }

        UpdateVoiceTestLevel(peak);
    }

    private async Task SendCallAudioLoopAsync(ContactViewModel contact, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                byte[]? pcm;
                lock (_audioFrameGate)
                {
                    pcm = _pendingAudioCaptureFrames.Count > 0
                        ? _pendingAudioCaptureFrames.Dequeue()
                        : null;
                }

                var soundboardPcm = TakeNextSoundboardFrame();
                var voicePcm = _isMicrophoneMuted ? null : pcm;
                if (voicePcm is null && soundboardPcm is null)
                {
                    await SendCallAudioPingIfDueAsync(contact, cancellationToken);
                    continue;
                }

                await SendCallAudioFrameAsync(contact, voicePcm, soundboardPcm);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call audio send loop failed: to={contact.UserId}");
        }
    }

    private async Task SendCallAudioFrameAsync(ContactViewModel contact, byte[]? voicePcm, byte[]? soundboardPcm)
    {
        if (_profile is null ||
            _activeCallContact?.UserId != contact.UserId ||
            _activeCallState != "connected" ||
            _audioCall is null)
        {
            return;
        }

        if (!TryEnterAudioSend())
        {
            return;
        }

        try
        {
            if (_relayClient is null || !_relayClient.IsConnected)
            {
                return;
            }

            byte[]? sendVoicePcm = null;
            var capturePeak = 0;
            if (voicePcm is { Length: > 0 })
            {
                capturePeak = GetPcmPeak(voicePcm);
                sendVoicePcm = ProcessMicrophonePcm(voicePcm);
                if (GetPcmPeak(sendVoicePcm) <= CallAudioVoiceFloorPeak)
                {
                    Interlocked.Increment(ref _quietCaptureFrames);
                }
            }

            byte[]? sendSoundboardPcm = null;
            if (soundboardPcm is { Length: > 0 })
            {
                sendSoundboardPcm = ApplyGainAndLimiter(soundboardPcm, _soundboardVolume, CallAudioOutputLimitPeak);
            }

            var sequence = Interlocked.Increment(ref _callAudioSendSequence);
            var body = EncodeCallAudioBody(sendVoicePcm, sendSoundboardPcm);
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            sendTimeout.CancelAfter(CallAudioSendTimeout);
            var targets = GetCallAudioTargetUserIds(contact);
            foreach (var targetUserId in targets)
            {
                var packet = RelayAudioPacket.Create(_profile.UserId, targetUserId, body, sequence);
                await _relayClient.SendAudioAsync(packet, sendTimeout.Token);
            }

            var sent = Interlocked.Increment(ref _sentAudioFrames);
            if (sent == 1 || sent % 100 == 0)
            {
                AppLog.Write($"Call audio sent over relay: to={contact.UserId}, targets={targets.Count}, frames={sent}, voiceBytes={sendVoicePcm?.Length ?? 0}, soundboardBytes={sendSoundboardPcm?.Length ?? 0}, payloadBytes={Encoding.UTF8.GetByteCount(body)}, capturePeak={capturePeak}, sendPeak={GetPcmPeak(sendVoicePcm ?? [])}, noiseSuppression={_settings.NoiseSuppressionEnabled}");
            }

            if (sent >= 100 &&
                Interlocked.Read(ref _relayReceivedAudioFrames) == 0 &&
                Interlocked.Exchange(ref _udpAudioWarningShown, 1) == 0)
            {
                AppLog.Write($"Call audio relay not receiving: to={contact.UserId}, sentFrames={sent}. Check updated VPS server and audio relay connection.");
                Dispatcher.Invoke(() => NetworkStatusText.Text = "Call audio relay is not receiving from peer.");
            }
        }
        catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
        {
            var timeouts = Interlocked.Increment(ref _audioSendTimeouts);
            if (timeouts == 1 || timeouts % 10 == 0)
            {
                AppLog.Write($"Call audio send timed out: to={contact.UserId}, timeouts={timeouts}");
            }
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call audio send failed: to={contact.UserId}");
        }
        finally
        {
            Interlocked.Exchange(ref _pendingAudioFrame, 0);
            Interlocked.Exchange(ref _pendingAudioFrameStartedUtcTicks, 0);
        }
    }

    private string EncodeCallAudioBody(byte[]? voicePcm, byte[]? soundboardPcm)
    {
        if (soundboardPcm is { Length: > 0 })
        {
            var voiceTrack = voicePcm is { Length: > 0 }
                ? EncodeOpusTrack(voicePcm, soundboardTrack: false)
                : null;
            var soundboardTrack = EncodeOpusTrack(soundboardPcm, soundboardTrack: true);
            return JsonSerializer.Serialize(new CallAudioMixPayload(
                "call-audio-mix",
                CallAudioMixPayloadVersion,
                CallAudioSampleRate,
                CallAudioChannels,
                CallAudioOpusFrameSize,
                voiceTrack,
                soundboardTrack));
        }

        var pcm = voicePcm ?? [];
        if (pcm.Length < CallAudioMinDecodedBytes ||
            pcm.Length > CallAudioMaxDecodedBytes ||
            pcm.Length % 2 != 0)
        {
            return Convert.ToBase64String(pcm);
        }

        try
        {
            var sampleCount = pcm.Length / 2;
            var frameSize = sampleCount / CallAudioChannels;
            if (frameSize <= 0)
            {
                return Convert.ToBase64String(pcm);
            }

            var encodedData = EncodeOpusTrack(pcm, soundboardTrack: false);
            if (string.IsNullOrWhiteSpace(encodedData))
            {
                return Convert.ToBase64String(pcm);
            }

            var payload = new CallAudioOpusPayload(
                CallAudioCodecOpus,
                CallAudioOpusPayloadVersion,
                CallAudioSampleRate,
                CallAudioChannels,
                frameSize,
                encodedData);

            var encodedFrames = Interlocked.Increment(ref _opusEncodedAudioFrames);
            if (encodedFrames == 1 || encodedFrames % 250 == 0)
            {
                AppLog.Write($"Call audio Opus encoded: frames={encodedFrames}, pcmBytes={pcm.Length}, opusBytes={Convert.FromBase64String(encodedData).Length}, frameSize={frameSize}, bitrate={CallAudioOpusBitrate}");
            }

            return JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Call audio Opus encode failed, falling back to raw PCM");
            return Convert.ToBase64String(pcm);
        }
    }

    private string? EncodeOpusTrack(byte[] pcm, bool soundboardTrack)
    {
        if (pcm.Length < CallAudioMinDecodedBytes || pcm.Length > CallAudioMaxDecodedBytes || pcm.Length % 2 != 0)
        {
            return null;
        }

        var samples = new short[pcm.Length / 2];
        Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
        var frameSize = samples.Length / CallAudioChannels;
        var encoded = new byte[CallAudioOpusMaxPacketBytes];
        int bytesWritten;
        lock (_callOpusGate)
        {
            var encoder = soundboardTrack ? GetOrCreateSoundboardOpusEncoder() : GetOrCreateCallOpusEncoder();
            bytesWritten = encoder.Encode(samples, frameSize, encoded, encoded.Length);
        }

        return bytesWritten > 0 ? Convert.ToBase64String(encoded, 0, bytesWritten) : null;
    }

    private IOpusEncoder GetOrCreateCallOpusEncoder()
    {
        if (_callOpusEncoder is not null)
        {
            return _callOpusEncoder;
        }

        var encoder = OpusCodecFactory.CreateEncoder(
            CallAudioSampleRate,
            CallAudioChannels,
            OpusApplication.OPUS_APPLICATION_VOIP,
            null);
        encoder.Bitrate = CallAudioOpusBitrate;
        encoder.Complexity = 8;
        encoder.UseDTX = false;
        encoder.UseInbandFEC = true;
        encoder.PacketLossPercent = CallAudioOpusExpectedLossPercent;
        encoder.UseVBR = true;
        encoder.UseConstrainedVBR = true;
        _callOpusEncoder = encoder;
        return _callOpusEncoder;
    }

    private IOpusEncoder GetOrCreateSoundboardOpusEncoder()
    {
        if (_callSoundboardOpusEncoder is not null)
        {
            return _callSoundboardOpusEncoder;
        }

        var encoder = OpusCodecFactory.CreateEncoder(
            CallAudioSampleRate,
            CallAudioChannels,
            OpusApplication.OPUS_APPLICATION_AUDIO,
            null);
        encoder.Bitrate = 48000;
        encoder.Complexity = 8;
        encoder.UseDTX = false;
        encoder.UseInbandFEC = true;
        encoder.PacketLossPercent = CallAudioOpusExpectedLossPercent;
        encoder.UseVBR = true;
        encoder.UseConstrainedVBR = true;
        _callSoundboardOpusEncoder = encoder;
        return encoder;
    }

    private IOpusDecoder GetOrCreateCallOpusDecoder()
    {
        _callOpusDecoder ??= OpusCodecFactory.CreateDecoder(CallAudioSampleRate, CallAudioChannels, null);
        return _callOpusDecoder;
    }

    private IOpusDecoder GetOrCreateSoundboardOpusDecoder()
    {
        _callSoundboardOpusDecoder ??= OpusCodecFactory.CreateDecoder(CallAudioSampleRate, CallAudioChannels, null);
        return _callSoundboardOpusDecoder;
    }

    private void ResetCallOpusCodec()
    {
        lock (_callOpusGate)
        {
            _callOpusEncoder = null;
            _callOpusDecoder = null;
            _callSoundboardOpusEncoder = null;
            _callSoundboardOpusDecoder = null;
        }
    }

    private bool TryEnterAudioSend()
    {
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        if (Interlocked.CompareExchange(ref _pendingAudioFrame, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _pendingAudioFrameStartedUtcTicks, nowTicks);
            return true;
        }

        var startedTicks = Interlocked.Read(ref _pendingAudioFrameStartedUtcTicks);
        if (startedTicks > 0 && TimeSpan.FromTicks(nowTicks - startedTicks) > TimeSpan.FromSeconds(2))
        {
            Interlocked.Exchange(ref _pendingAudioFrame, 0);
            Interlocked.Exchange(ref _pendingAudioFrameStartedUtcTicks, 0);
            AppLog.Write("Call audio pending send recovered after timeout");
        }

        if (Interlocked.CompareExchange(ref _pendingAudioFrame, 1, 0) != 0)
        {
            return false;
        }

        Interlocked.Exchange(ref _pendingAudioFrameStartedUtcTicks, nowTicks);
        return true;
    }

    private async Task SendCallAudioPingIfDueAsync(ContactViewModel contact, CancellationToken cancellationToken)
    {
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastAudioPingTicks);
        if (lastTicks > 0 && TimeSpan.FromTicks(nowTicks - lastTicks) < TimeSpan.FromSeconds(1))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastAudioPingTicks, nowTicks, lastTicks) == lastTicks)
        {
            await SendCallAudioPingAsync(contact, cancellationToken);
        }
    }

    private async Task SendCallAudioPingAsync(ContactViewModel contact, CancellationToken cancellationToken)
    {
        if (_profile is null || _relayClient is null || !_relayClient.IsConnected)
        {
            return;
        }

        try
        {
            foreach (var targetUserId in GetCallAudioTargetUserIds(contact))
            {
                await _relayClient.SendAudioAsync(RelayAudioPacket.Create(_profile.UserId, targetUserId, ""), cancellationToken);
            }
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call audio UDP ping failed: to={contact.UserId}");
        }
    }

    private IReadOnlyList<string> GetCallAudioTargetUserIds(ContactViewModel contact)
    {
        if (_profile is null)
        {
            return [];
        }

        if (!contact.IsGroup)
        {
            return [contact.UserId];
        }

        return _activeCallContact?.UserId == contact.UserId && _activeCallTargetUserIds.Count > 0
            ? _activeCallTargetUserIds
            : LoadGroupMembers(contact)
                .Where(x => !string.Equals(x.UserId, _profile.UserId, StringComparison.Ordinal))
                .Select(x => x.UserId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }

    private void HandleCallAudioPacket(ChatPacket packet)
    {
        if (!IsActiveCallPeer(packet.FromUserId) ||
            _activeCallState != "connected" ||
            _audioCall is null ||
            string.IsNullOrWhiteSpace(packet.Body))
        {
            return;
        }

        if (!TryDecodeCallAudio(packet.Body, out var pcm))
        {
            AppLog.Write($"Invalid call audio packet: from={packet.FromUserId}, bodyLength={packet.Body.Length}");
            return;
        }

        var tcpReceived = Interlocked.Increment(ref _tcpReceivedAudioFrames);
        if (tcpReceived == 1 || tcpReceived % 100 == 0)
        {
            AppLog.Write($"Call audio received over TCP: from={packet.FromUserId}, frames={tcpReceived}, bytes={pcm.Length}");
        }

        QueueCallAudio(packet.FromUserId, pcm);
    }

    private void HandleCallAudioPacket(RelayAudioPacket packet)
    {
        if (!IsActiveCallPeer(packet.FromUserId) ||
            _activeCallState != "connected" ||
            _audioCall is null ||
            string.IsNullOrWhiteSpace(packet.Body))
        {
            return;
        }

        Interlocked.Exchange(ref _lastRelayAudioReceivedTicks, DateTimeOffset.UtcNow.Ticks);
        if (packet.Sequence > 0)
        {
            if (!TrackReceivedCallAudioSequence(packet.Sequence))
            {
                return;
            }

            var sequencedRelayReceived = Interlocked.Increment(ref _relayReceivedAudioFrames);
            if (sequencedRelayReceived == 1 || sequencedRelayReceived % 100 == 0)
            {
                AppLog.Write($"Call audio received over relay: from={packet.FromUserId}, frames={sequencedRelayReceived}, sequence={packet.Sequence}, payloadBytes={Encoding.UTF8.GetByteCount(packet.Body)}");
            }

            QueueJitteredCallAudio(packet.FromUserId, packet.Body, packet.Sequence);
            return;
        }

        if (!TryDecodeCallAudio(packet.Body, out var pcm))
        {
            AppLog.Write($"Invalid UDP call audio packet: from={packet.FromUserId}, bodyLength={packet.Body.Length}");
            return;
        }

        var relayReceived = Interlocked.Increment(ref _relayReceivedAudioFrames);
        if (relayReceived == 1 || relayReceived % 100 == 0)
        {
            AppLog.Write($"Call audio received over relay: from={packet.FromUserId}, frames={relayReceived}, bytes={pcm.Length}");
        }

        QueueCallAudio(packet.FromUserId, pcm, packet.Sequence);
    }

    private bool TryHandleLegacyCallAudioPacket(ChatPacket packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.Intent) ||
            !IsActiveCallPeer(packet.FromUserId) ||
            _activeCallState != "connected" ||
            _audioCall is null ||
            !TryDecodeCallAudio(packet.Body, out var pcm))
        {
            return false;
        }

        var legacy = Interlocked.Increment(ref _legacyAudioFrames);
        if (legacy == 1 || legacy % 100 == 0)
        {
            AppLog.Write($"Legacy call audio accepted: from={packet.FromUserId}, frames={legacy}, bytes={pcm.Length}");
        }

        QueueCallAudio(packet.FromUserId, pcm);
        return true;
    }

    private void QueueCallAudio(string fromUserId, byte[] pcm, long sequence = 0)
    {
        if (_audioCall is null || _isHeadphonesMuted)
        {
            return;
        }

        if (sequence > 0)
        {
            QueueJitteredCallAudio(fromUserId, pcm, sequence);
            return;
        }

        while (_callPlaybackQueue.Count >= CallAudioMaxPlaybackQueueFrames &&
               _callPlaybackQueue.TryDequeue(out _))
        {
            var dropped = Interlocked.Increment(ref _droppedPlaybackQueueFrames);
            if (dropped == 1 || dropped % 100 == 0)
            {
                AppLog.Write($"Call audio playback queue dropped old frame: dropped={dropped}, queued={_callPlaybackQueue.Count}");
            }
        }

        _callPlaybackQueue.Enqueue(new CallPlaybackFrame(fromUserId, pcm, sequence));
        _callPlaybackSignal.Release();
    }

    private void QueueJitteredCallAudio(string fromUserId, byte[] pcm, long sequence)
        => QueueJitteredCallAudioFrame(new CallPlaybackFrame(fromUserId, pcm, sequence));

    private void QueueJitteredCallAudio(string fromUserId, string encodedBody, long sequence)
        => QueueJitteredCallAudioFrame(new CallPlaybackFrame(fromUserId, null, sequence, EncodedBody: encodedBody));

    private void QueueJitteredCallAudioFrame(CallPlaybackFrame playbackFrame)
    {
        lock (_callJitterGate)
        {
            if (_callJitterExpectedSequence <= 0)
            {
                _callJitterExpectedSequence = playbackFrame.Sequence;
                _callJitterHighestSequence = playbackFrame.Sequence;
                _callJitterWarmedUp = false;
            }

            if (playbackFrame.Sequence < _callJitterExpectedSequence)
            {
                return;
            }

            _callJitterFrames[playbackFrame.Sequence] = playbackFrame;
            if (playbackFrame.Sequence > _callJitterHighestSequence)
            {
                _callJitterHighestSequence = playbackFrame.Sequence;
            }

            while (_callJitterFrames.Count > CallAudioJitterMaxFrames)
            {
                var first = _callJitterFrames.Keys.First();
                _callJitterFrames.Remove(first);
                if (first == _callJitterExpectedSequence)
                {
                    _callJitterExpectedSequence++;
                    Interlocked.Increment(ref _droppedPlaybackQueueFrames);
                }
            }
        }
    }

    private CallPlaybackFrame? CreateConcealmentFrame()
    {
        if (_lastCallPlaybackPcm is null ||
            _consecutiveCallConcealmentFrames >= CallAudioMaxConcealmentFrames)
        {
            return null;
        }

        _consecutiveCallConcealmentFrames++;
        if (TryDecodeMissingOpusFrame(out var opusPcm))
        {
            return new CallPlaybackFrame("opus-plc", opusPcm, 0, true);
        }

        var factor = Math.Max(0.15, 0.65 - (_consecutiveCallConcealmentFrames - 1) * 0.2);
        return new CallPlaybackFrame("concealment", ScalePcm(_lastCallPlaybackPcm, factor), 0, true);
    }

    private async Task PlayCallAudioLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var frame = TryDequeueCallPlaybackFrame();
                if (frame is null)
                {
                    continue;
                }

                PlayQueuedCallAudio(frame);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Call audio playback loop failed");
        }
    }

    private CallPlaybackFrame? TryDequeueCallPlaybackFrame()
    {
        if (_callPlaybackQueue.TryDequeue(out var directFrame))
        {
            return directFrame;
        }

        lock (_callJitterGate)
        {
            if (_callJitterExpectedSequence <= 0)
            {
                return null;
            }

            if (!_callJitterWarmedUp)
            {
                var bufferedAhead = _callJitterHighestSequence - _callJitterExpectedSequence + 1;
                if (_callJitterFrames.Count < CallAudioJitterWarmupFrames &&
                    bufferedAhead < CallAudioJitterWarmupFrames)
                {
                    return null;
                }

                _callJitterWarmedUp = true;
            }

            if (_callJitterFrames.Remove(_callJitterExpectedSequence, out var frame))
            {
                _callJitterExpectedSequence++;
                _consecutiveCallConcealmentFrames = 0;
                return frame;
            }

            if (_callJitterHighestSequence >= _callJitterExpectedSequence + CallAudioJitterLossDelay)
            {
                var concealed = TryCreateFecRecoveryFrame(_callJitterExpectedSequence) ?? CreateConcealmentFrame();
                _callJitterExpectedSequence++;
                if (concealed is not null)
                {
                    return concealed;
                }
            }
        }

        return null;
    }

    private CallPlaybackFrame? TryCreateFecRecoveryFrame(long missingSequence)
    {
        if (_callJitterFrames.TryGetValue(missingSequence + 1, out var nextFrame) &&
            !string.IsNullOrWhiteSpace(nextFrame.EncodedBody))
        {
            return new CallPlaybackFrame(
                nextFrame.FromUserId,
                null,
                missingSequence,
                EncodedBody: nextFrame.EncodedBody,
                DecodeWithFec: true);
        }

        return null;
    }

    private void PlayQueuedCallAudio(CallPlaybackFrame frame)
    {
        var session = _audioCall;
        if (session is null || _isHeadphonesMuted)
        {
            return;
        }

        if (!TryResolveCallPlaybackPcm(frame, out var voicePcm, out var soundboardPcm))
        {
            var failed = Interlocked.Increment(ref _failedPlaybackFrames);
            if (failed == 1 || failed % 25 == 0)
            {
                AppLog.Write($"Call audio decode failed: from={frame.FromUserId}, failures={failed}, sequence={frame.Sequence}, fec={frame.DecodeWithFec}, encoded={frame.EncodedBody is not null}");
            }

            return;
        }

        var preference = _callAudioPreferences.Get(frame.FromUserId);
        var playbackVoice = preference.IsMuted
            ? []
            : ApplyGainAndLimiter(voicePcm, preference.Volume, CallAudioOutputLimitPeak);
        var playbackSoundboard = preference.IsSoundboardMuted
            ? []
            : ApplyGainAndLimiter(soundboardPcm, preference.Volume, CallAudioOutputLimitPeak);
        var playbackPcm = MixPcm(playbackVoice, playbackSoundboard);
        if (playbackPcm.Length == 0)
        {
            return;
        }

        var peak = GetPcmPeak(playbackPcm);

        if (!session.Play(playbackPcm, out var error))
        {
            var failed = Interlocked.Increment(ref _failedPlaybackFrames);
            if (failed == 1 || failed % 25 == 0)
            {
                AppLog.Write($"Call audio playback failed: from={frame.FromUserId}, failures={failed}, bytes={playbackPcm.Length}, peak={peak}, volume={preference.Volume:0.##}, error={error}");
            }

            return;
        }

        if (!frame.IsConcealment)
        {
            _lastCallPlaybackPcm = voicePcm;
        }

        var received = Interlocked.Increment(ref _receivedAudioFrames);
        if (received == 1 || received % 100 == 0)
        {
            AppLog.Write($"Call audio played: from={frame.FromUserId}, frames={received}, voiceBytes={voicePcm.Length}, soundboardBytes={soundboardPcm.Length}, peak={peak}, volume={preference.Volume:0.##}, muted={preference.IsMuted}, soundboardMuted={preference.IsSoundboardMuted}, queued={_callPlaybackQueue.Count}");
        }
    }

    private bool TryResolveCallPlaybackPcm(CallPlaybackFrame frame, out byte[] voicePcm, out byte[] soundboardPcm)
    {
        if (frame.Pcm is { Length: > 0 })
        {
            voicePcm = frame.Pcm;
            soundboardPcm = [];
            return true;
        }

        if (!string.IsNullOrWhiteSpace(frame.EncodedBody))
        {
            if (TryDecodeCallAudioTracks(frame.EncodedBody, out voicePcm, out soundboardPcm, frame.DecodeWithFec))
            {
                return true;
            }

            if (frame.DecodeWithFec && TryDecodeMissingOpusFrame(out voicePcm))
            {
                soundboardPcm = [];
                return true;
            }
        }

        voicePcm = [];
        soundboardPcm = [];
        return false;
    }

    private void ClearCallPlaybackQueue()
    {
        while (_callPlaybackQueue.TryDequeue(out _))
        {
        }
    }

    private void ClearCallJitterBuffer()
    {
        lock (_callJitterGate)
        {
            _callJitterFrames.Clear();
            _callJitterExpectedSequence = 0;
            _callJitterHighestSequence = 0;
            _callJitterWarmedUp = false;
            _lastCallPlaybackPcm = null;
            _consecutiveCallConcealmentFrames = 0;
        }
    }

    private byte[]? TakeNextSoundboardFrame()
    {
        SoundboardClipViewModel? completedClip = null;
        byte[]? frame = null;
        lock (_soundboardAudioGate)
        {
            if (_activeSoundboardPcm is null || _activeSoundboardOffset >= _activeSoundboardPcm.Length)
            {
                return null;
            }

            var frameBytes = CallAudioOpusFrameSize * CallAudioChannels * 2;
            frame = new byte[frameBytes];
            var remaining = _activeSoundboardPcm.Length - _activeSoundboardOffset;
            var copyLength = Math.Min(frameBytes, remaining);
            Buffer.BlockCopy(_activeSoundboardPcm, _activeSoundboardOffset, frame, 0, copyLength);
            _activeSoundboardOffset += copyLength;
            if (_activeSoundboardOffset >= _activeSoundboardPcm.Length)
            {
                completedClip = _activeSoundboardClip;
                _activeSoundboardClip = null;
                _activeSoundboardPcm = null;
                _activeSoundboardOffset = 0;
            }
        }

        if (completedClip is not null)
        {
            Dispatcher.BeginInvoke(new Action(() => completedClip.IsPlaying = false));
        }

        return frame;
    }

    private byte[] ProcessMicrophonePcm(byte[] pcm)
    {
        if (pcm.Length == 0)
        {
            return pcm;
        }

        if (!_settings.NoiseSuppressionEnabled)
        {
            return ApplyGainAndLimiter(pcm, CallAudioMicrophoneGain, CallAudioOutputLimitPeak);
        }

        var rms = GetPcmRms(pcm);
        var quietObservationLimit = Math.Max(260, _noiseFloorRms * 1.55);
        if (rms <= quietObservationLimit)
        {
            _noiseFloorRms += (rms - _noiseFloorRms) * 0.025;
        }

        var closeThreshold = Math.Max(115, _noiseFloorRms * 2.05);
        var openThreshold = Math.Max(175, closeThreshold * 1.35);
        var targetGate = rms >= openThreshold
            ? 1d
            : rms <= closeThreshold
                ? 0.08
                : 0.08 + ((rms - closeThreshold) / Math.Max(1, openThreshold - closeThreshold)) * 0.92;
        var smoothing = targetGate > _noiseGateGain ? 0.48 : 0.09;
        _noiseGateGain += (targetGate - _noiseGateGain) * smoothing;
        if (_noiseGateGain < 0.1)
        {
            _noiseGateGain = 0.08;
        }

        return ApplyGainAndLimiter(pcm, _noiseGateGain * CallAudioMicrophoneGain, CallAudioOutputLimitPeak);
    }

    private static double GetPcmRms(byte[] pcm)
    {
        if (pcm.Length < 2)
        {
            return 0;
        }

        double sumSquares = 0;
        var sampleCount = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            sumSquares += (double)sample * sample;
            sampleCount++;
        }

        return sampleCount == 0 ? 0 : Math.Sqrt(sumSquares / sampleCount);
    }

    private static byte[] ApplyGainAndLimiter(byte[] pcm, double gain, int limitPeak)
    {
        if (pcm.Length == 0 || gain <= 0)
        {
            return [];
        }

        gain = Math.Clamp(gain, 0, 5);
        var output = new byte[pcm.Length];
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            var scaled = sample * gain;
            if (Math.Abs(scaled) > limitPeak)
            {
                var sign = Math.Sign(scaled);
                var excess = Math.Abs(scaled) - limitPeak;
                scaled = sign * (limitPeak + Math.Tanh(excess / 6000d) * 3500d);
            }

            var value = (int)Math.Round(Math.Clamp(scaled, short.MinValue, short.MaxValue));
            output[i] = (byte)(value & 0xff);
            output[i + 1] = (byte)((value >> 8) & 0xff);
        }

        return output;
    }

    private static byte[] MixPcm(byte[] first, byte[] second)
    {
        if (first.Length == 0)
        {
            return second;
        }

        if (second.Length == 0)
        {
            return first;
        }

        var length = Math.Max(first.Length, second.Length);
        if (length % 2 != 0)
        {
            length--;
        }

        var mixed = new byte[length];
        for (var i = 0; i + 1 < length; i += 2)
        {
            var firstSample = i + 1 < first.Length ? BitConverter.ToInt16(first, i) : (short)0;
            var secondSample = i + 1 < second.Length ? BitConverter.ToInt16(second, i) : (short)0;
            var value = Math.Clamp(firstSample + secondSample, short.MinValue, short.MaxValue);
            mixed[i] = (byte)(value & 0xff);
            mixed[i + 1] = (byte)((value >> 8) & 0xff);
        }

        return mixed;
    }

    private static byte[] AmplifyPcm(byte[] pcm, int peak, out double gain)
    {
        gain = 1;
        if (peak <= CallAudioSilencePeak)
        {
            return new byte[pcm.Length];
        }

        if (peak < CallAudioVoiceFloorPeak)
        {
            return pcm;
        }

        if (peak >= CallAudioTargetPeak)
        {
            return pcm;
        }

        gain = Math.Min(CallAudioMaxGain, (double)CallAudioTargetPeak / peak);
        var amplified = new byte[pcm.Length];
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            var scaled = (int)Math.Round(sample * gain);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            amplified[i] = (byte)(scaled & 0xff);
            amplified[i + 1] = (byte)((scaled >> 8) & 0xff);
        }

        return amplified;
    }

    private static byte[] ScalePcm(byte[] pcm, double factor)
    {
        var scaledPcm = new byte[pcm.Length];
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            var scaled = (int)Math.Round(sample * factor);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            scaledPcm[i] = (byte)(scaled & 0xff);
            scaledPcm[i + 1] = (byte)((scaled >> 8) & 0xff);
        }

        return scaledPcm;
    }

    private static int GetPcmPeak(byte[] pcm)
    {
        var peak = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            var amplitude = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);
            if (amplitude > peak)
            {
                peak = amplitude;
            }
        }

        return peak;
    }

    private bool TryDecodeCallAudioTracks(
        string body,
        out byte[] voicePcm,
        out byte[] soundboardPcm,
        bool decodeFec = false)
    {
        voicePcm = [];
        soundboardPcm = [];
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        if (body.TrimStart().StartsWith("{", StringComparison.Ordinal) &&
            TryDecodeCallAudioMix(body, out voicePcm, out soundboardPcm, decodeFec))
        {
            return voicePcm.Length > 0 || soundboardPcm.Length > 0;
        }

        if (!TryDecodeCallAudio(body, out voicePcm, decodeFec))
        {
            return false;
        }

        return true;
    }

    private bool TryDecodeCallAudioMix(
        string body,
        out byte[] voicePcm,
        out byte[] soundboardPcm,
        bool decodeFec)
    {
        voicePcm = [];
        soundboardPcm = [];
        try
        {
            var payload = JsonSerializer.Deserialize<CallAudioMixPayload>(body);
            if (payload is null ||
                !string.Equals(payload.Codec, "call-audio-mix", StringComparison.OrdinalIgnoreCase) ||
                payload.Version != CallAudioMixPayloadVersion ||
                payload.SampleRate != CallAudioSampleRate ||
                payload.Channels != CallAudioChannels ||
                payload.FrameSize != CallAudioOpusFrameSize)
            {
                return false;
            }

            var voiceDecoded = string.IsNullOrWhiteSpace(payload.VoiceData) ||
                               TryDecodeOpusTrack(payload.VoiceData, payload.FrameSize, soundboardTrack: false, decodeFec, out voicePcm);
            var soundboardDecoded = string.IsNullOrWhiteSpace(payload.SoundboardData) ||
                                    TryDecodeOpusTrack(payload.SoundboardData, payload.FrameSize, soundboardTrack: true, decodeFec, out soundboardPcm);
            if (!voiceDecoded || !soundboardDecoded)
            {
                voicePcm = [];
                soundboardPcm = [];
                return false;
            }

            Interlocked.Increment(ref _opusDecodedAudioFrames);
            if (decodeFec)
            {
                Interlocked.Increment(ref _opusFecRecoveredAudioFrames);
            }

            return voicePcm.Length > 0 || soundboardPcm.Length > 0;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            AppLog.Write(ex, $"Call audio mix decode failed: fec={decodeFec}, bodyLength={body.Length}");
            return false;
        }
    }

    private bool TryDecodeOpusTrack(
        string data,
        int frameSize,
        bool soundboardTrack,
        bool decodeFec,
        out byte[] pcm)
    {
        pcm = [];
        var opusBytes = Convert.FromBase64String(data);
        if (opusBytes.Length == 0 || opusBytes.Length > CallAudioOpusMaxPacketBytes)
        {
            return false;
        }

        var samples = new short[frameSize * CallAudioChannels];
        int decodedSamples;
        lock (_callOpusGate)
        {
            var decoder = soundboardTrack ? GetOrCreateSoundboardOpusDecoder() : GetOrCreateCallOpusDecoder();
            decodedSamples = decoder.Decode(opusBytes, samples, frameSize, decodeFec);
        }

        if (decodedSamples <= 0)
        {
            return false;
        }

        var byteLength = decodedSamples * CallAudioChannels * 2;
        if (byteLength < CallAudioMinDecodedBytes || byteLength > CallAudioMaxDecodedBytes)
        {
            return false;
        }

        pcm = new byte[byteLength];
        Buffer.BlockCopy(samples, 0, pcm, 0, byteLength);
        return true;
    }

    private bool TryDecodeCallAudio(string body, out byte[] pcm, bool decodeFec = false)
    {
        pcm = [];
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var trimmedBody = body.TrimStart();
        if (trimmedBody.StartsWith("{", StringComparison.Ordinal))
        {
            if (TryDecodeCallAudioMix(trimmedBody, out var voicePcm, out var soundboardPcm, decodeFec))
            {
                pcm = MixPcm(voicePcm, soundboardPcm);
                return pcm.Length > 0;
            }

            return TryDecodeOpusCallAudio(trimmedBody, out pcm, decodeFec);
        }

        if (decodeFec)
        {
            return false;
        }

        var maxEncodedLength = ((CallAudioMaxDecodedBytes + 2) / 3) * 4;
        if (body.Length > maxEncodedLength)
        {
            return false;
        }

        var buffer = new byte[CallAudioMaxDecodedBytes];
        if (!Convert.TryFromBase64String(body, buffer, out var bytesWritten) ||
            bytesWritten < CallAudioMinDecodedBytes ||
            bytesWritten > CallAudioMaxDecodedBytes ||
            bytesWritten % 2 != 0)
        {
            return false;
        }

        pcm = buffer[..bytesWritten];
        return true;
    }

    private bool TryDecodeOpusCallAudio(string body, out byte[] pcm, bool decodeFec)
    {
        pcm = [];
        try
        {
            var payload = JsonSerializer.Deserialize<CallAudioOpusPayload>(body);
            if (payload is null ||
                !string.Equals(payload.Codec, CallAudioCodecOpus, StringComparison.OrdinalIgnoreCase) ||
                payload.Version != CallAudioOpusPayloadVersion ||
                payload.SampleRate != CallAudioSampleRate ||
                payload.Channels != CallAudioChannels ||
                payload.FrameSize <= 0 ||
                payload.FrameSize * payload.Channels * 2 > CallAudioMaxDecodedBytes ||
                string.IsNullOrWhiteSpace(payload.Data))
            {
                return false;
            }

            var opusBytes = Convert.FromBase64String(payload.Data);
            if (opusBytes.Length == 0 || opusBytes.Length > CallAudioOpusMaxPacketBytes)
            {
                return false;
            }

            var samples = new short[payload.FrameSize * payload.Channels];
            int decodedSamples;
            lock (_callOpusGate)
            {
                var decoder = GetOrCreateCallOpusDecoder();
                decodedSamples = decoder.Decode(opusBytes, samples, payload.FrameSize, decodeFec);
            }

            if (decodedSamples <= 0)
            {
                return false;
            }

            var pcmLength = decodedSamples * payload.Channels * 2;
            if (pcmLength < CallAudioMinDecodedBytes || pcmLength > CallAudioMaxDecodedBytes)
            {
                return false;
            }

            pcm = new byte[pcmLength];
            Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);

            var decodedFrames = Interlocked.Increment(ref _opusDecodedAudioFrames);
            if (decodeFec)
            {
                var recovered = Interlocked.Increment(ref _opusFecRecoveredAudioFrames);
                if (recovered == 1 || recovered % 25 == 0)
                {
                    AppLog.Write($"Call audio Opus FEC recovered: recoveredFrames={recovered}, decodedFrames={decodedFrames}, opusBytes={opusBytes.Length}, pcmBytes={pcm.Length}");
                }
            }
            else if (decodedFrames == 1 || decodedFrames % 250 == 0)
            {
                AppLog.Write($"Call audio Opus decoded: frames={decodedFrames}, opusBytes={opusBytes.Length}, pcmBytes={pcm.Length}, frameSize={payload.FrameSize}");
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, $"Call audio Opus decode failed: fec={decodeFec}, bodyLength={body.Length}");
            return false;
        }
    }

    private bool TryDecodeMissingOpusFrame(out byte[] pcm)
    {
        pcm = [];
        try
        {
            var samples = new short[CallAudioOpusFrameSize * CallAudioChannels];
            int decodedSamples;
            lock (_callOpusGate)
            {
                if (_callOpusDecoder is null)
                {
                    return false;
                }

                decodedSamples = _callOpusDecoder.Decode(
                    ReadOnlySpan<byte>.Empty,
                    samples,
                    CallAudioOpusFrameSize,
                    false);
            }

            if (decodedSamples <= 0)
            {
                return false;
            }

            var pcmLength = decodedSamples * CallAudioChannels * 2;
            if (pcmLength < CallAudioMinDecodedBytes || pcmLength > CallAudioMaxDecodedBytes)
            {
                return false;
            }

            pcm = new byte[pcmLength];
            Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);

            var concealed = Interlocked.Increment(ref _opusConcealedAudioFrames);
            if (concealed == 1 || concealed % 25 == 0)
            {
                AppLog.Write($"Call audio Opus PLC concealed: frames={concealed}, pcmBytes={pcm.Length}");
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Call audio Opus PLC failed");
            return false;
        }
    }

    private void RestoreWindowForIncomingCall()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private ChatPacket CreateProfilePacket(string toUserId, string body, string? intent = null, string? relayServerOverride = null)
    {
        if (_profile is null)
        {
            throw new InvalidOperationException("Profile is not loaded.");
        }

        var currentRelayServer = NormalizeRelayServer(_settings.RelayServer);
        var contact = _contacts.FirstOrDefault(x => x.UserId == toUserId);
        var contactRelayServer = !string.IsNullOrWhiteSpace(relayServerOverride)
            ? NormalizeRelayServer(relayServerOverride)
            : contact is null ? currentRelayServer : GetRelayServer(contact);
        var toRelayServer = string.Equals(currentRelayServer, contactRelayServer, StringComparison.OrdinalIgnoreCase)
            ? null
            : contactRelayServer;
        var avatarPayload = CreateAvatarPayload();
        var packetBody = string.IsNullOrWhiteSpace(intent)
            ? body
            : CreateControlBody(intent, body, avatarPayload);

        return ChatPacket.Create(
            _profile.UserId,
            _profile.DisplayName,
            toUserId,
            packetBody,
            fromRelayServer: currentRelayServer,
            toRelayServer: toRelayServer,
            intent: intent,
            fromStatus: GetCurrentStatus().ToString(),
            fromAvatarKind: avatarPayload.kind,
            fromAvatarMediaBase64: avatarPayload.mediaBase64,
            fromAvatarExtension: avatarPayload.extension,
            fromAvatarScale: _profile.AvatarScale,
            fromAvatarOffsetX: _profile.AvatarOffsetX,
            fromAvatarOffsetY: _profile.AvatarOffsetY,
            fromAvatarVideoStartSeconds: _profile.AvatarVideoStartSeconds,
            fromAvatarVideoDurationSeconds: _profile.AvatarVideoDurationSeconds);
    }

    private ChatPacket CreateCallPacket(ContactViewModel contact, string body, string intent)
    {
        if (_profile is null)
        {
            throw new InvalidOperationException("Profile is not loaded.");
        }

        var currentRelayServer = NormalizeRelayServer(_settings.RelayServer);
        var contactRelayServer = GetRelayServer(contact);
        var toRelayServer = string.Equals(currentRelayServer, contactRelayServer, StringComparison.OrdinalIgnoreCase)
            ? null
            : contactRelayServer;
        var packetBody = intent == CallAudioIntent
            ? body
            : CreateControlBody(intent, body, ("", null, null));

        return ChatPacket.Create(
            _profile.UserId,
            _profile.DisplayName,
            contact.UserId,
            packetBody,
            fromRelayServer: currentRelayServer,
            toRelayServer: toRelayServer,
            intent: intent,
            fromStatus: GetCurrentStatus().ToString());
    }

    private ChatPacket CreatePresencePacket(
        ContactViewModel contact,
        UserPresenceStatus? statusOverride = null,
        bool includeAvatar = true)
    {
        if (_profile is null)
        {
            throw new InvalidOperationException("Profile is not loaded.");
        }

        var currentRelayServer = NormalizeRelayServer(_settings.RelayServer);
        var contactRelayServer = GetRelayServer(contact);
        var toRelayServer = string.Equals(currentRelayServer, contactRelayServer, StringComparison.OrdinalIgnoreCase)
            ? null
            : contactRelayServer;

        var avatarPayload = includeAvatar
            ? CreateAvatarPayload()
            : (_profile.AvatarKind, null, null);

        return ChatPacket.Create(
            _profile.UserId,
            _profile.DisplayName,
            contact.UserId,
            "",
            fromRelayServer: currentRelayServer,
            toRelayServer: toRelayServer,
            intent: "presence",
            fromStatus: (statusOverride ?? GetCurrentStatus()).ToString(),
            fromAvatarKind: avatarPayload.kind,
            fromAvatarMediaBase64: avatarPayload.mediaBase64,
            fromAvatarExtension: avatarPayload.extension,
            fromAvatarScale: _profile.AvatarScale,
            fromAvatarOffsetX: _profile.AvatarOffsetX,
            fromAvatarOffsetY: _profile.AvatarOffsetY,
            fromAvatarVideoStartSeconds: _profile.AvatarVideoStartSeconds,
            fromAvatarVideoDurationSeconds: _profile.AvatarVideoDurationSeconds);
    }

    private static string CreateControlBody(
        string intent,
        string body,
        (string kind, string? mediaBase64, string? extension) avatarPayload)
    {
        var control = new ControlBody(
            intent,
            body,
            avatarPayload.kind,
            avatarPayload.mediaBase64,
            avatarPayload.extension,
            null,
            null,
            null,
            null,
            null);

        return $"{ControlBodyPrefix}{JsonSerializer.Serialize(control)}";
    }

    private ChatPacket ApplyControlBodyFallback(ChatPacket packet)
    {
        if (string.IsNullOrWhiteSpace(packet.Body) ||
            !packet.Body.StartsWith(ControlBodyPrefix, StringComparison.Ordinal))
        {
            return packet;
        }

        try
        {
            var controlJson = packet.Body[ControlBodyPrefix.Length..];
            var control = JsonSerializer.Deserialize<ControlBody>(controlJson);
            if (control is null || string.IsNullOrWhiteSpace(control.Intent))
            {
                return packet;
            }

            return packet with
            {
                Body = control.Body ?? "",
                Intent = string.IsNullOrWhiteSpace(packet.Intent) ? control.Intent : packet.Intent,
                FromAvatarKind = string.IsNullOrWhiteSpace(packet.FromAvatarKind) ? control.AvatarKind : packet.FromAvatarKind,
                FromAvatarMediaBase64 = string.IsNullOrWhiteSpace(packet.FromAvatarMediaBase64) ? control.AvatarMediaBase64 : packet.FromAvatarMediaBase64,
                FromAvatarExtension = string.IsNullOrWhiteSpace(packet.FromAvatarExtension) ? control.AvatarExtension : packet.FromAvatarExtension,
                FromAvatarScale = control.AvatarScale ?? packet.FromAvatarScale,
                FromAvatarOffsetX = control.AvatarOffsetX ?? packet.FromAvatarOffsetX,
                FromAvatarOffsetY = control.AvatarOffsetY ?? packet.FromAvatarOffsetY,
                FromAvatarVideoStartSeconds = control.AvatarVideoStartSeconds ?? packet.FromAvatarVideoStartSeconds,
                FromAvatarVideoDurationSeconds = control.AvatarVideoDurationSeconds ?? packet.FromAvatarVideoDurationSeconds
            };
        }
        catch (JsonException ex)
        {
            AppLog.Write(ex, $"Control body parse failed: from={packet.FromUserId}, messageId={packet.MessageId}");
            return packet;
        }
    }

    private sealed record ControlBody(
        string Intent,
        string? Body,
        string? AvatarKind,
        string? AvatarMediaBase64,
        string? AvatarExtension,
        double? AvatarScale,
        double? AvatarOffsetX,
        double? AvatarOffsetY,
        double? AvatarVideoStartSeconds,
        double? AvatarVideoDurationSeconds);

    private sealed record RichChatPayload(
        string Kind,
        string Text,
        string AttachmentFileName,
        string AttachmentBase64,
        string AttachmentUrl,
        Guid? ReplyToMessageId,
        string ReplyPreview,
        string ForwardedFrom);

    private sealed record ChatEditPayload(Guid MessageId, string Text, DateTimeOffset EditedAtUtc);

    private sealed record ChatReactionPayload(Guid MessageId, string UserId, string Emoji);

    private sealed record ChatDeletePayload(Guid MessageId);

    private sealed record GroupMemberPayload(
        string UserId,
        string DisplayName,
        string RelayServer,
        string AvatarKind,
        string AvatarPath,
        string AvatarExtension,
        double AvatarScale,
        double AvatarOffsetX,
        double AvatarOffsetY,
        double AvatarVideoStartSeconds,
        double AvatarVideoDurationSeconds,
        DateTimeOffset JoinedAtUtc);

    private sealed record GroupSnapshotPayload(
        string GroupId,
        string DisplayName,
        string OwnerUserId,
        long GroupVersion,
        bool IsDeleted,
        string AvatarKind,
        string AvatarMediaBase64,
        string AvatarExtension,
        double AvatarScale,
        double AvatarOffsetX,
        double AvatarOffsetY,
        double AvatarVideoStartSeconds,
        double AvatarVideoDurationSeconds,
        IReadOnlyList<GroupMemberPayload> Members);

    private sealed record GroupActionPayload(
        string GroupId,
        long GroupVersion,
        string ActorUserId,
        string TargetUserId);

    private sealed record GroupMessagePayload(string GroupId, string Text);

    private sealed record CallGroupSignalPayload(string GroupId, string Body = "");

    private sealed record CallAudioState(bool MicrophoneMuted, bool HeadphonesMuted);

    private sealed record CallAudioOpusPayload(
        string Codec,
        int Version,
        int SampleRate,
        int Channels,
        int FrameSize,
        string Data);

    private sealed record CallAudioMixPayload(
        string Codec,
        int Version,
        int SampleRate,
        int Channels,
        int FrameSize,
        string? VoiceData,
        string? SoundboardData);

    private sealed record CallNetworkPingPayload(long Sequence, DateTimeOffset SentAtUtc, long AudioFramesSent = 0);

    private sealed record CallAudioSendReport(long AudioFramesSent, DateTimeOffset ReceivedAtUtc);

    private sealed record ScreenShareStartPayload(string SourceTitle, int Resolution, int FrameRate, bool AudioMuted);

    private sealed record ScreenShareFramePayload(
        long Sequence,
        string JpegBase64,
        int Resolution,
        int FrameRate,
        bool AudioMuted,
        string Codec = ScreenShareCodecJpeg,
        bool KeyFrame = true);

    private sealed record CallPlaybackFrame(
        string FromUserId,
        byte[]? Pcm,
        long Sequence = 0,
        bool IsConcealment = false,
        string? EncodedBody = null,
        bool DecodeWithFec = false);

    private sealed record QueuedScreenShareFrame(string FromUserId, string Body);

    private sealed record ScreenShareSourceItem(
        string Title,
        string Description,
        bool IsScreen,
        IntPtr WindowHandle,
        Rectangle Bounds,
        BitmapImage? Preview)
    {
        public string BadgeText => $"{(IsScreen ? "Screen" : "App")} · {Description}";
    }

    private const string ScreenShareWebRtcHtml = """
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>
    html, body { width: 100%; height: 100%; margin: 0; overflow: hidden; background: #0b0c0f; color: #f2f3f5; font-family: Segoe UI, sans-serif; }
    #stage { width: 100%; height: 100%; display: grid; grid-template-columns: 1fr; grid-auto-rows: 100%; gap: 8px; padding: 0; box-sizing: border-box; }
    #stage.both { grid-template-columns: 1fr 1fr; }
    .tile { position: relative; min-width: 0; min-height: 0; background: #111214; overflow: hidden; }
    video, .preview { width: 100%; height: 100%; object-fit: contain; background: #111214; }
    .label { position: absolute; top: 8px; left: 8px; padding: 4px 8px; border-radius: 4px; background: rgba(0,0,0,.62); font-size: 11px; font-weight: 600; }
    #localTile.hidden, #remoteTile.hidden { display: none; }
    .preview.hidden, video.hidden { display: none; }
    #status { position: absolute; left: 12px; bottom: 10px; padding: 5px 8px; border-radius: 4px; background: rgba(0,0,0,.62); font-size: 11px; color: #b5bac1; pointer-events: none; }
  </style>
</head>
<body>
  <div id="stage">
    <div id="localTile" class="tile hidden"><video id="localVideo" autoplay playsinline muted></video><img id="localPreviewImage" class="preview hidden" alt=""><div class="label">Your screen</div></div>
    <div id="remoteTile" class="tile hidden"><video id="remoteVideo" autoplay playsinline></video><div class="label">Friend WebRTC screen</div></div>
  </div>
  <div id="status">WebRTC ready</div>
  <script>
    const stage = document.getElementById('stage');
    const localTile = document.getElementById('localTile');
    const remoteTile = document.getElementById('remoteTile');
    const localVideo = document.getElementById('localVideo');
    const localPreviewImage = document.getElementById('localPreviewImage');
    const remoteVideo = document.getElementById('remoteVideo');
    const statusBox = document.getElementById('status');
    const defaultIceServers = [
      { urls: ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302'] }
    ];
    let pc = null;
    let politePeer = true;
    let makingOffer = false;
    let ignoringOffer = false;
    let localStream = null;
    let remoteStarted = false;
    let rtcConfig = { iceServers: defaultIceServers, bundlePolicy: 'max-bundle', iceTransportPolicy: 'all' };
    let connectTimeoutMs = 9000;
    let connectionWatchdog = null;
    let statsTimer = null;
    let previousStats = new Map();
    let remotePlaybackRetry = null;
    let remoteDecodeWatchdog = null;
    let remoteDecodeFailedNotified = false;
    let nativeCanvas = null;
    let nativeContext = null;
    let nativeFrameWriter = null;
    let nativeCanvasTrack = null;
    let nativePendingFrame = null;
    let nativeFrameBusy = false;
    let encodedSendChannel = null;
    let encodedReceiveChannel = null;
    let encodedMimeType = 'video/mp4; codecs="avc1.640033"';
    let encodedSendQueue = [];
    let encodedSendBusy = false;
    let encodedBufferLimit = 4194304;
    let encodedMediaSource = null;
    let encodedSourceBuffer = null;
    let encodedAppendQueue = [];
    let encodedReceivedChunks = 0;
    let encodedReceivedBytes = 0;
    let encodedObjectUrl = null;
    let screenShareFocusTarget = 'auto';
    let localPreviewActive = false;
    let remoteAudioMuted = false;
    let remoteAudioVolume = 1;

    function post(message) {
      if (window.chrome && chrome.webview) chrome.webview.postMessage(message);
    }
    function setStatus(value) {
      statusBox.textContent = value;
      post({ type: 'state', value });
    }
    stage.addEventListener('dblclick', event => {
      event.preventDefault();
      const tile = event.target && event.target.closest ? event.target.closest('.tile') : null;
      const target = tile === localTile ? 'local' : tile === remoteTile ? 'remote' : 'auto';
      post({ type: 'focus-request', target });
    });
    stage.addEventListener('contextmenu', event => {
      event.preventDefault();
      post({ type: 'stream-context-request' });
    });
    function applyRemoteAudioPreference(message) {
      if (message) {
        remoteAudioMuted = Boolean(message.muted);
        remoteAudioVolume = Math.max(0, Math.min(1, Number(message.volume ?? 1)));
      }
      remoteVideo.muted = remoteAudioMuted;
      remoteVideo.volume = remoteAudioVolume;
    }
    function normalizeIceServers(servers) {
      if (!Array.isArray(servers) || servers.length === 0) return defaultIceServers;
      return servers.map(server => {
        const urls = server.urls || server.Urls || [];
        const normalized = { urls: Array.isArray(urls) ? urls : [urls] };
        const username = server.username || server.Username;
        const credential = server.credential || server.Credential;
        const credentialType = server.credentialType || server.CredentialType;
        if (username) normalized.username = username;
        if (credential) normalized.credential = credential;
        if (credentialType) normalized.credentialType = credentialType;
        return normalized;
      }).filter(server => server.urls.length > 0);
    }
    function configure(message) {
      rtcConfig = {
        iceServers: normalizeIceServers(message.iceServers),
        bundlePolicy: 'max-bundle',
        iceTransportPolicy: 'all'
      };
      connectTimeoutMs = Number(message.connectTimeoutMs || connectTimeoutMs);
      if (typeof message.polite === 'boolean') politePeer = message.polite;
      setStatus('WebRTC ICE configured');
    }
    function updateLayout() {
      const hasLocalStream = localStream && localStream.getTracks().some(t => t.readyState === 'live');
      const hasLocal = hasLocalStream || localPreviewActive;
      const wantsLocal = screenShareFocusTarget === 'local';
      const wantsRemote = screenShareFocusTarget === 'remote';
      const showLocal = hasLocal && !(wantsRemote && remoteStarted);
      const showRemote = remoteStarted && !(wantsLocal && hasLocal);
      localVideo.classList.toggle('hidden', !hasLocalStream);
      localPreviewImage.classList.toggle('hidden', hasLocalStream || !localPreviewActive);
      localTile.classList.toggle('hidden', !showLocal);
      remoteTile.classList.toggle('hidden', !showRemote);
      stage.classList.toggle('both', showLocal && showRemote);
    }
    function clearLocalPreview() {
      localPreviewActive = false;
      localPreviewImage.removeAttribute('src');
      updateLayout();
    }
    function handleLocalPreview(message) {
      const base64 = message.jpegBase64 || message.JpegBase64 || '';
      if (!base64) return;
      localPreviewImage.src = 'data:image/jpeg;base64,' + base64;
      localPreviewActive = true;
      updateLayout();
    }
    function setFocusTarget(target) {
      screenShareFocusTarget = target === 'local' || target === 'remote' ? target : 'auto';
      updateLayout();
    }
    function clearRemotePlaybackRetry() {
      if (remotePlaybackRetry) clearTimeout(remotePlaybackRetry);
      remotePlaybackRetry = null;
    }
    function clearRemoteDecodeWatchdog() {
      if (remoteDecodeWatchdog) clearTimeout(remoteDecodeWatchdog);
      remoteDecodeWatchdog = null;
    }
    function hasRemoteVideoFrame() {
      return remoteVideo.readyState >= 2 && remoteVideo.videoWidth > 0 && remoteVideo.videoHeight > 0;
    }
    function notifyRemoteDecodeFailed(reason) {
      if (remoteDecodeFailedNotified) return;
      remoteDecodeFailedNotified = true;
      clearRemoteDecodeWatchdog();
      statusBox.textContent = 'Encoded WebRTC decode issue';
      post({ type: 'remote-decode-failed', reason });
    }
    function armRemoteDecodeWatchdog(reason) {
      clearRemoteDecodeWatchdog();
      remoteDecodeWatchdog = setTimeout(() => {
        if (remoteStarted && !hasRemoteVideoFrame()) {
          notifyRemoteDecodeFailed(reason + '; chunks=' + encodedReceivedChunks + '; bytes=' + encodedReceivedBytes);
        }
      }, 6500);
    }
    function notifyRemotePlaying() {
      clearRemoteDecodeWatchdog();
      post({
        type: 'remote-playing',
        width: Number(remoteVideo.videoWidth || 0),
        height: Number(remoteVideo.videoHeight || 0),
        readyState: Number(remoteVideo.readyState || 0)
      });
    }
    function playRemoteVideo() {
      const play = remoteVideo.play();
      if (play && typeof play.catch === 'function') {
        play.catch(error => {
          post({ type: 'state', value: 'WebRTC remote play retry: ' + String(error && error.message ? error.message : error) });
          setTimeout(() => remoteVideo.play().catch(() => {}), 250);
        });
      }
    }
    function armRemotePlaybackRetry(stream, attempt) {
      clearRemotePlaybackRetry();
      remotePlaybackRetry = setTimeout(() => {
        if (remoteVideo.srcObject !== stream) return;
        const hasFrame = remoteVideo.readyState >= 2 && remoteVideo.videoWidth > 0 && remoteVideo.videoHeight > 0;
        if (hasFrame) {
          notifyRemotePlaying();
          return;
        }

        post({ type: 'state', value: 'WebRTC remote video retry ' + attempt });
        remoteVideo.srcObject = null;
        requestAnimationFrame(() => {
          remoteVideo.srcObject = stream;
          playRemoteVideo();
          if (attempt < 4) armRemotePlaybackRetry(stream, attempt + 1);
        });
      }, attempt === 1 ? 1200 : 2200);
    }
    function attachRemoteStream(stream) {
      clearRemotePlaybackRetry();
      remoteVideo.autoplay = true;
      remoteVideo.playsInline = true;
      remoteVideo.srcObject = stream;
      applyRemoteAudioPreference();
      playRemoteVideo();
      remoteStarted = true;
      updateLayout();
      post({ type: 'remote-started' });
      setStatus('WebRTC remote stream');
      armRemotePlaybackRetry(stream, 1);
    }
    remoteVideo.onloadedmetadata = () => {
      if (hasRemoteVideoFrame()) clearRemoteDecodeWatchdog();
      post({
        type: 'remote-video-ready',
        width: Number(remoteVideo.videoWidth || 0),
        height: Number(remoteVideo.videoHeight || 0),
        readyState: Number(remoteVideo.readyState || 0)
      });
      playRemoteVideo();
    };
    remoteVideo.onplaying = () => {
      clearRemotePlaybackRetry();
      clearRemoteDecodeWatchdog();
      notifyRemotePlaying();
    };
    remoteVideo.onwaiting = () => post({ type: 'state', value: 'WebRTC remote video waiting' });
    remoteVideo.onstalled = () => post({ type: 'state', value: 'WebRTC remote video stalled' });
    remoteVideo.onerror = () => {
      const error = remoteVideo.error;
      notifyRemoteDecodeFailed('HTML video error code=' + String(error ? error.code : 0));
    };
    function clearConnectionWatchdog() {
      if (connectionWatchdog) clearTimeout(connectionWatchdog);
      connectionWatchdog = null;
    }
    function closePeerConnectionForRestart() {
      clearConnectionWatchdog();
      clearRemoteDecodeWatchdog();
      stopStatsTimer();
      if (pc) {
        try { pc.close(); } catch {}
      }
      pc = null;
      remoteStarted = false;
      remoteVideo.pause();
      remoteVideo.srcObject = null;
      remoteVideo.removeAttribute('src');
      try { remoteVideo.load(); } catch {}
      updateLayout();
    }
    function armConnectionWatchdog() {
      clearConnectionWatchdog();
      connectionWatchdog = setTimeout(() => {
        if (!pc) return;
        if (pc.connectionState !== 'connected' && pc.iceConnectionState !== 'connected' && pc.iceConnectionState !== 'completed') {
          post({ type: 'error', message: 'WebRTC connection timeout' });
          setStatus('WebRTC timeout');
        }
      }, connectTimeoutMs);
    }
    function startStatsTimer() {
      if (statsTimer) return;
      statsTimer = setInterval(collectStats, 2000);
    }
    function stopStatsTimer() {
      if (statsTimer) clearInterval(statsTimer);
      statsTimer = null;
      previousStats.clear();
    }
    async function ensurePeerConnection() {
      if (pc && (pc.connectionState === 'failed' || pc.connectionState === 'closed')) {
        closePeerConnectionForRestart();
      }
      if (pc) return pc;
      pc = new RTCPeerConnection(rtcConfig);
      pc.onicecandidate = event => {
        if (event.candidate) post({ type: 'ice', candidate: event.candidate.toJSON() });
      };
      pc.ontrack = event => {
        if (event.streams && event.streams[0]) {
          attachRemoteStream(event.streams[0]);
        }
      };
      pc.ondatachannel = event => {
        if (event.channel && event.channel.label === 'screen-h264') {
          setupEncodedChannel(event.channel, false, {});
        }
      };
      pc.onconnectionstatechange = () => {
        setStatus('WebRTC ' + pc.connectionState);
        if (pc.connectionState === 'connected') clearConnectionWatchdog();
        if (pc.connectionState === 'failed' || pc.connectionState === 'closed') {
          remoteStarted = false;
          updateLayout();
        }
      };
      pc.oniceconnectionstatechange = () => {
        if (pc.iceConnectionState === 'connected' || pc.iceConnectionState === 'completed') clearConnectionWatchdog();
      };
      startStatsTimer();
      return pc;
    }
    async function applySenderParameters(sender, options) {
      try {
        const parameters = sender.getParameters();
        if (!parameters.encodings || parameters.encodings.length === 0) parameters.encodings = [{}];
        parameters.encodings[0].maxBitrate = Number(options.maxBitrate || 12000000);
        parameters.encodings[0].maxFramerate = Number(options.frameRate || 60);
        parameters.encodings[0].priority = 'high';
        parameters.degradationPreference = Number(options.frameRate || 60) >= 45 ? 'maintain-framerate' : 'maintain-resolution';
        await sender.setParameters(parameters);
      } catch (error) {
        post({ type: 'state', value: 'WebRTC sender parameters skipped: ' + String(error && error.message ? error.message : error) });
      }
    }
    function removeLocalSenders() {
      if (!pc) return;
      for (const sender of pc.getSenders()) {
        if (sender.track) {
          try { pc.removeTrack(sender); } catch {}
        }
      }
    }
    function base64ToBytes(base64) {
      const binary = atob(base64);
      const bytes = new Uint8Array(binary.length);
      for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
      return bytes;
    }
    function resetNativeFrameSource() {
      nativePendingFrame = null;
      nativeFrameBusy = false;
      if (nativeFrameWriter) {
        nativeFrameWriter.close().catch(() => {});
        nativeFrameWriter = null;
      }
      nativeCanvasTrack = null;
      nativeCanvas = null;
      nativeContext = null;
    }
    function resetEncodedSender() {
      encodedSendQueue = [];
      encodedSendBusy = false;
      if (encodedSendChannel) {
        const channel = encodedSendChannel;
        encodedSendChannel = null;
        channel.onclose = null;
        channel.onerror = null;
        channel.onbufferedamountlow = null;
        try { channel.close(); } catch {}
      }
    }
    function resetEncodedReceiver() {
      clearRemoteDecodeWatchdog();
      remoteDecodeFailedNotified = false;
      encodedReceivedChunks = 0;
      encodedReceivedBytes = 0;
      encodedAppendQueue = [];
      encodedSourceBuffer = null;
      if (encodedReceiveChannel) {
        const channel = encodedReceiveChannel;
        encodedReceiveChannel = null;
        channel.onclose = null;
        channel.onerror = null;
        channel.onmessage = null;
        try { channel.close(); } catch {}
      }
      if (encodedMediaSource && encodedMediaSource.readyState === 'open') {
        try { encodedMediaSource.endOfStream(); } catch {}
      }
      encodedMediaSource = null;
      if (encodedObjectUrl) {
        URL.revokeObjectURL(encodedObjectUrl);
        encodedObjectUrl = null;
      }
    }
    function resetEncodedStream() {
      resetEncodedSender();
      resetEncodedReceiver();
    }
    function setupEncodedChannel(channel, isSender, options) {
      if (isSender) {
        setupEncodedSendChannel(channel, options);
      } else {
        setupEncodedReceiveChannel(channel, options);
      }
    }
    function setupEncodedSendChannel(channel, options) {
      resetEncodedSender();
      encodedSendChannel = channel;
      encodedMimeType = options.mimeType || encodedMimeType;
      encodedBufferLimit = Number(options.dataChannelBufferLimit || encodedBufferLimit);
      channel.binaryType = 'arraybuffer';
      channel.bufferedAmountLowThreshold = Math.floor(encodedBufferLimit / 2);
      channel.onopen = () => {
        try {
          channel.send(JSON.stringify({ type: 'encoded-config', mimeType: encodedMimeType }));
          post({ type: 'encoded-channel-open' });
        } catch (error) {
          post({ type: 'error', message: 'Encoded WebRTC config send failed: ' + String(error && error.message ? error.message : error) });
        }
        flushEncodedSendQueue();
      };
      channel.onclose = () => {
        if (encodedSendChannel === channel) encodedSendChannel = null;
        post({ type: 'encoded-channel-closed' });
      };
      channel.onerror = error => post({ type: 'error', message: 'Encoded WebRTC send channel error: ' + String(error && error.message ? error.message : error) });
      channel.onbufferedamountlow = flushEncodedSendQueue;
    }
    function setupEncodedReceiveChannel(channel, options) {
      resetEncodedReceiver();
      encodedReceiveChannel = channel;
      encodedMimeType = options.mimeType || encodedMimeType;
      channel.binaryType = 'arraybuffer';
      channel.onclose = () => {
        if (encodedReceiveChannel === channel) encodedReceiveChannel = null;
        post({ type: 'state', value: 'Encoded WebRTC receive channel ended' });
      };
      channel.onerror = error => post({ type: 'state', value: 'Encoded WebRTC receive channel error: ' + String(error && error.message ? error.message : error) });
      channel.onmessage = event => {
        if (typeof event.data === 'string') {
          try {
            const message = JSON.parse(event.data);
            if (message.type === 'encoded-config') startEncodedReceiver(message.mimeType || encodedMimeType);
          } catch {}
          return;
        }

        if (event.data instanceof Blob) {
          event.data.arrayBuffer().then(appendEncodedChunk).catch(error => post({ type: 'state', value: 'Encoded WebRTC blob failed: ' + String(error && error.message ? error.message : error) }));
          return;
        }

        appendEncodedChunk(event.data);
      };
    }
    function startEncodedReceiver(mimeType) {
      encodedMimeType = mimeType || encodedMimeType;
      if (encodedMediaSource) return;
      remoteDecodeFailedNotified = false;
      encodedMediaSource = new MediaSource();
      remoteVideo.muted = true;
      remoteVideo.autoplay = true;
      remoteVideo.playsInline = true;
      remoteVideo.srcObject = null;
      if (encodedObjectUrl) URL.revokeObjectURL(encodedObjectUrl);
      encodedObjectUrl = URL.createObjectURL(encodedMediaSource);
      remoteVideo.src = encodedObjectUrl;
      remoteStarted = true;
      updateLayout();
      post({ type: 'remote-started' });
      setStatus('Encoded WebRTC remote stream');
      armRemoteDecodeWatchdog('encoded WebRTC video did not render');
      encodedMediaSource.addEventListener('sourceopen', () => {
        try {
          if (!MediaSource.isTypeSupported(encodedMimeType)) {
            throw new Error('Unsupported encoded stream: ' + encodedMimeType);
          }
          encodedSourceBuffer = encodedMediaSource.addSourceBuffer(encodedMimeType);
          encodedSourceBuffer.mode = 'segments';
          encodedSourceBuffer.addEventListener('updateend', flushEncodedAppendQueue);
          encodedSourceBuffer.addEventListener('error', () => notifyRemoteDecodeFailed('encoded SourceBuffer error'));
          flushEncodedAppendQueue();
          playRemoteVideo();
        } catch (error) {
          notifyRemoteDecodeFailed(String(error && error.message ? error.message : error));
        }
      }, { once: true });
    }
    function appendEncodedChunk(data) {
      if (!data) return;
      startEncodedReceiver(encodedMimeType);
      const bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
      encodedReceivedChunks += 1;
      encodedReceivedBytes += bytes.byteLength || bytes.length || 0;
      encodedAppendQueue.push(bytes);
      if (!hasRemoteVideoFrame() && !remoteDecodeWatchdog) armRemoteDecodeWatchdog('encoded WebRTC video did not render');
      flushEncodedAppendQueue();
    }
    function flushEncodedAppendQueue() {
      if (!encodedSourceBuffer || encodedSourceBuffer.updating || encodedAppendQueue.length === 0) return;
      if (remoteVideo.error) {
        notifyRemoteDecodeFailed('HTML video error before append code=' + remoteVideo.error.code);
        encodedAppendQueue = [];
        return;
      }

      try {
        encodedSourceBuffer.appendBuffer(encodedAppendQueue.shift());
      } catch (error) {
        const message = String(error && error.message ? error.message : error);
        post({ type: 'state', value: 'Encoded WebRTC append skipped: ' + message });
        notifyRemoteDecodeFailed(message);
        encodedAppendQueue = [];
      }
    }
    function enqueueEncodedLocalChunk(message) {
      const base64 = message.dataBase64 || message.DataBase64 || '';
      if (!base64) return;
      encodedSendQueue.push(base64ToBytes(base64));
      if (encodedSendQueue.length > 240) encodedSendQueue.shift();
      flushEncodedSendQueue();
    }
    function flushEncodedSendQueue() {
      if (encodedSendBusy || !encodedSendChannel || encodedSendChannel.readyState !== 'open') return;
      encodedSendBusy = true;
      try {
        while (encodedSendQueue.length > 0 && encodedSendChannel.bufferedAmount < encodedBufferLimit) {
          encodedSendChannel.send(encodedSendQueue.shift());
        }
      } catch (error) {
        post({ type: 'error', message: 'Encoded WebRTC send failed: ' + String(error && error.message ? error.message : error) });
      } finally {
        encodedSendBusy = false;
      }
    }
    async function decodeNativeFrame(base64) {
      const bytes = base64ToBytes(base64);
      const blob = new Blob([bytes], { type: 'image/jpeg' });
      return await createImageBitmap(blob);
    }
    async function renderNativeFrame(message) {
      if (!nativeCanvas || !nativeContext) return;
      const base64 = message.jpegBase64 || message.JpegBase64 || '';
      if (!base64) return;
      const bitmap = await decodeNativeFrame(base64);
      try {
        if (!nativeCanvas || !nativeContext) return;
        if (bitmap.width > 0 && bitmap.height > 0 && (nativeCanvas.width !== bitmap.width || nativeCanvas.height !== bitmap.height)) {
          nativeCanvas.width = bitmap.width;
          nativeCanvas.height = bitmap.height;
        }
        nativeContext.drawImage(bitmap, 0, 0, nativeCanvas.width, nativeCanvas.height);
        if (nativeCanvasTrack && nativeCanvasTrack.requestFrame) {
          nativeCanvasTrack.requestFrame();
        }
        if (nativeFrameWriter && window.VideoFrame) {
          const timestamp = Number(message.timestampUs || Math.round(performance.now() * 1000));
          const frame = new VideoFrame(nativeCanvas, { timestamp });
          try {
            await nativeFrameWriter.write(frame);
          } finally {
            frame.close();
          }
        }
      } finally {
        if (bitmap.close) bitmap.close();
      }
    }
    async function processNativeFrameQueue() {
      if (nativeFrameBusy) return;
      nativeFrameBusy = true;
      try {
        while (nativePendingFrame) {
          const message = nativePendingFrame;
          nativePendingFrame = null;
          await renderNativeFrame(message);
        }
      } catch (error) {
        post({ type: 'state', value: 'Native WebRTC frame skipped: ' + String(error && error.message ? error.message : error) });
      } finally {
        nativeFrameBusy = false;
        if (nativePendingFrame) processNativeFrameQueue();
      }
    }
    function handleNativeFrame(message) {
      nativePendingFrame = message;
      processNativeFrameQueue();
    }
    async function createAndSendOffer(offerOptions, connectedStatus) {
      makingOffer = true;
      try {
        const offer = await pc.createOffer(offerOptions);
        await pc.setLocalDescription(offer);
      } finally {
        makingOffer = false;
      }

      post({ type: 'offer', sdp: pc.localDescription.sdp });
      armConnectionWatchdog();
      setStatus(connectedStatus);
    }
    async function startNativeShare(options) {
      try {
        if (options.iceServers) configure(options);
        await ensurePeerConnection();
        stopLocalTracks(false);
        removeLocalSenders();
        clearLocalPreview();
        const frameRate = Number(options.frameRate || 60);
        const width = Number(options.width || 1920);
        const height = Number(options.height || 1080);
        nativeCanvas = document.createElement('canvas');
        nativeCanvas.width = width;
        nativeCanvas.height = height;
        nativeContext = nativeCanvas.getContext('2d', { alpha: false, desynchronized: true });
        if (!nativeContext) throw new Error('Native WebRTC canvas context is unavailable');
        nativeContext.fillStyle = '#050608';
        nativeContext.fillRect(0, 0, nativeCanvas.width, nativeCanvas.height);

        let nativeTrack = null;
        if (window.MediaStreamTrackGenerator && window.VideoFrame) {
          try {
            nativeTrack = new MediaStreamTrackGenerator({ kind: 'video' });
            nativeFrameWriter = nativeTrack.writable.getWriter();
            localStream = new MediaStream([nativeTrack]);
          } catch (error) {
            nativeTrack = null;
            nativeFrameWriter = null;
            post({ type: 'state', value: 'Native WebRTC generator unavailable: ' + String(error && error.message ? error.message : error) });
          }
        }

        if (!localStream && nativeCanvas.captureStream) {
          localStream = nativeCanvas.captureStream(0);
          nativeTrack = localStream.getVideoTracks()[0];
          nativeCanvasTrack = nativeTrack;
        }

        if (!localStream || !nativeTrack) {
          throw new Error('Native WebRTC frame source is not supported by this WebView2 runtime');
        }

        nativeTrack.contentHint = options.contentHint || 'detail';
        localVideo.srcObject = localStream;
        localVideo.play().catch(() => {});
        const sender = pc.addTrack(nativeTrack, localStream);
        await applySenderParameters(sender, options);
        nativeTrack.onended = () => post({ type: 'local-ended' });
        updateLayout();
        post({ type: 'local-started' });
        await createAndSendOffer({ offerToReceiveVideo: true, offerToReceiveAudio: false }, 'Native WebRTC offer sent');
      } catch (error) {
        resetNativeFrameSource();
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
        setStatus('Native WebRTC error');
      }
    }
    async function startEncodedShare(options) {
      try {
        if (options.iceServers) configure(options);
        await ensurePeerConnection();
        stopLocalTracks(false);
        removeLocalSenders();
        resetEncodedSender();
        clearLocalPreview();
        setupEncodedChannel(pc.createDataChannel('screen-h264', { ordered: true }), true, options);
        updateLayout();
        post({ type: 'encoded-local-started' });
        await createAndSendOffer({ offerToReceiveVideo: false, offerToReceiveAudio: false }, 'Encoded WebRTC offer sent');
      } catch (error) {
        resetEncodedSender();
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
        setStatus('Encoded WebRTC error');
      }
    }
    async function startShare(options) {
      try {
        if (options.iceServers) configure(options);
        await ensurePeerConnection();
        stopLocalTracks(false);
        removeLocalSenders();
        clearLocalPreview();
        const frameRate = Number(options.frameRate || 60);
        const width = Number(options.width || 2560);
        const height = Number(options.height || 1440);
        localStream = await navigator.mediaDevices.getDisplayMedia({
          video: {
            width: { ideal: width },
            height: { ideal: height },
            frameRate: { ideal: frameRate, max: frameRate }
          },
          audio: options.audioMuted ? false : true
        });
        localVideo.srcObject = localStream;
        localVideo.play().catch(() => {});
        for (const track of localStream.getVideoTracks()) {
          track.contentHint = options.contentHint || 'detail';
          const sender = pc.addTrack(track, localStream);
          await applySenderParameters(sender, options);
          track.onended = () => post({ type: 'local-ended' });
        }
        for (const track of localStream.getAudioTracks()) {
          pc.addTrack(track, localStream);
        }
        updateLayout();
        post({ type: 'local-started' });
        await createAndSendOffer({ offerToReceiveVideo: true, offerToReceiveAudio: true }, 'WebRTC offer sent');
      } catch (error) {
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
        setStatus('WebRTC error');
      }
    }
    async function handleOffer(signal) {
      try {
        await ensurePeerConnection();
        const offerCollision = makingOffer || pc.signalingState !== 'stable';
        ignoringOffer = !politePeer && offerCollision;
        if (ignoringOffer) {
          post({ type: 'state', value: 'WebRTC offer collision ignored' });
          return;
        }

        if (offerCollision) {
          await pc.setLocalDescription({ type: 'rollback' });
        }

        ignoringOffer = false;
        await pc.setRemoteDescription({ type: 'offer', sdp: signal.sdp });
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        post({ type: 'answer', sdp: pc.localDescription.sdp });
        armConnectionWatchdog();
        setStatus('WebRTC answer sent');
      } catch (error) {
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
      }
    }
    async function handleAnswer(signal) {
      try {
        if (!pc) return;
        await pc.setRemoteDescription({ type: 'answer', sdp: signal.sdp });
        armConnectionWatchdog();
        setStatus('WebRTC answer accepted');
      } catch (error) {
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
      }
    }
    async function handleIce(signal) {
      try {
        if (pc && signal.candidate) await pc.addIceCandidate(signal.candidate);
      } catch (error) {
        if (!ignoringOffer) {
          post({ type: 'error', message: String(error && error.message ? error.message : error) });
        }
      }
    }
    function stopLocalTracks(notify) {
      if (localStream) {
        for (const track of localStream.getTracks()) {
          if (!notify) track.onended = null;
          try { track.stop(); } catch {}
        }
      }
      localStream = null;
      localVideo.srcObject = null;
      resetNativeFrameSource();
      resetEncodedSender();
      clearLocalPreview();
      updateLayout();
      if (notify) post({ type: 'local-ended' });
    }
    function stopLocalOnly() {
      stopLocalTracks(false);
      if (pc) {
        removeLocalSenders();
      }
      updateLayout();
      post({ type: 'local-stopped' });
    }
    function stopRemoteOnly() {
      clearRemotePlaybackRetry();
      resetEncodedReceiver();
      remoteVideo.pause();
      remoteVideo.srcObject = null;
      remoteVideo.removeAttribute('src');
      try { remoteVideo.load(); } catch {}
      remoteStarted = false;
      updateLayout();
      post({ type: 'remote-stopped' });
    }
    function stopAll() {
      clearConnectionWatchdog();
      clearRemotePlaybackRetry();
      stopStatsTimer();
      stopLocalTracks(false);
      resetEncodedStream();
      if (pc) {
        pc.close();
        pc = null;
      }
      remoteVideo.srcObject = null;
      remoteVideo.removeAttribute('src');
      try { remoteVideo.load(); } catch {}
      remoteStarted = false;
      updateLayout();
      setStatus('WebRTC stopped');
      post({ type: 'stopped' });
    }
    async function collectStats() {
      if (!pc) return;
      try {
        const stats = await pc.getStats();
        let rttMs = 0;
        let packetLoss = 0;
        stats.forEach(report => {
          if (report.type === 'remote-inbound-rtp' && report.kind === 'video') {
            rttMs = Number(report.roundTripTime || 0) * 1000;
            packetLoss = Number(report.fractionLost || 0);
          }
        });
        stats.forEach(report => {
          const isVideoRtp = (report.type === 'outbound-rtp' || report.type === 'inbound-rtp') &&
            (report.kind === 'video' || report.mediaType === 'video') &&
            !report.isRemote;
          if (!isVideoRtp) return;

          const previous = previousStats.get(report.id);
          const timestamp = Number(report.timestamp || performance.now());
          const bytes = Number(report.bytesSent || report.bytesReceived || 0);
          const frames = Number(report.framesEncoded || report.framesDecoded || 0);
          let bitrateKbps = 0;
          let fps = Number(report.framesPerSecond || 0);
          if (previous && timestamp > previous.timestamp) {
            const seconds = (timestamp - previous.timestamp) / 1000;
            bitrateKbps = Math.max(0, ((bytes - previous.bytes) * 8) / seconds / 1000);
            if (!fps && frames >= previous.frames) fps = (frames - previous.frames) / seconds;
          }
          previousStats.set(report.id, { timestamp, bytes, frames });
          post({
            type: 'stats',
            direction: report.type === 'outbound-rtp' ? 'outbound' : 'inbound',
            bitrateKbps,
            fps,
            framesDropped: Number(report.framesDropped || 0),
            packetsLost: Number(report.packetsLost || 0),
            rttMs,
            packetLoss,
            qualityLimitationReason: report.qualityLimitationReason || ''
          });
        });
      } catch (error) {
        post({ type: 'state', value: 'WebRTC stats failed: ' + String(error && error.message ? error.message : error) });
      }
    }
    if (window.chrome && chrome.webview) {
      chrome.webview.addEventListener('message', event => {
        const message = event.data || {};
        if (message.type === 'configure') configure(message);
        else if (message.type === 'start-encoded-share') startEncodedShare(message);
        else if (message.type === 'encoded-local-chunk') enqueueEncodedLocalChunk(message);
        else if (message.type === 'local-preview') handleLocalPreview(message);
        else if (message.type === 'start-native-share') startNativeShare(message);
        else if (message.type === 'native-frame') handleNativeFrame(message);
        else if (message.type === 'start-share') startShare(message);
        else if (message.type === 'stop-local') stopLocalOnly();
        else if (message.type === 'stop-remote') stopRemoteOnly();
        else if (message.type === 'stop') stopAll();
        else if (message.type === 'focus-target') setFocusTarget(message.target || 'auto');
        else if (message.type === 'set-remote-audio') applyRemoteAudioPreference(message);
        else if (message.type === 'remote-offer') handleOffer(message.signal || {});
        else if (message.type === 'remote-answer') handleAnswer(message.signal || {});
        else if (message.type === 'remote-ice') handleIce(message.signal || {});
      });
    }
    updateLayout();
    post({ type: 'ready' });
  </script>
</body>
</html>
""";

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    private const int DwmWindowAttributeExtendedFrameBounds = 9;
    private const int PrintWindowRenderFullContent = 0x00000002;
    private const int CursorShowing = 0x00000001;
    private const int DrawIconNormal = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int CbSize;
        public int Flags;
        public IntPtr CursorHandle;
        public NativePoint ScreenPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public int XHotspot;
        public int YHotspot;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr iconHandle, out IconInfo iconInfo);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(
        IntPtr deviceContext,
        int xLeft,
        int yTop,
        IntPtr iconHandle,
        int width,
        int height,
        int istepIfAniCur,
        IntPtr flickerFreeDraw,
        int flags);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr handle,
        int attribute,
        out NativeRect rect,
        int attributeSize);

    private (string kind, string? mediaBase64, string? extension) CreateAvatarPayload()
    {
        if (_profile is null ||
            string.IsNullOrWhiteSpace(_profile.AvatarPath) ||
            !File.Exists(_profile.AvatarPath))
        {
            AppLog.Write("Avatar sync skipped: avatar file missing");
            return (_profile?.AvatarKind ?? "color", null, null);
        }

        var file = new FileInfo(_profile.AvatarPath);
        if (file.Length > MaxAvatarSyncBytes)
        {
            AppLog.Write($"Avatar sync skipped: file too large, bytes={file.Length}, limit={MaxAvatarSyncBytes}");
            return (_profile.AvatarKind, null, null);
        }

        AppLog.Write($"Avatar sync attached: kind={_profile.AvatarKind}, bytes={file.Length}");
        return (
            _profile.AvatarKind,
            Convert.ToBase64String(File.ReadAllBytes(_profile.AvatarPath)),
            file.Extension);
    }

    private ContactViewModel CreateContactFromPacket(ChatPacket packet)
    {
        var contact = new ContactViewModel
        {
            UserId = packet.FromUserId,
            DisplayName = packet.FromDisplayName,
            IpAddress = $"{RelayContactPrefix}{NormalizeRelayServer(packet.FromRelayServer ?? _settings.RelayServer)}",
            MessagePort = FluxChatPorts.Relay,
            Status = ParsePresenceStatus(packet.FromStatus),
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        ApplyPacketProfileToContact(packet, contact);
        return contact;
    }

    private ContactViewModel CreateRelayContactFromPacket(ChatPacket packet)
        => new()
        {
            UserId = packet.FromUserId,
            DisplayName = packet.FromDisplayName,
            IpAddress = $"{RelayContactPrefix}{NormalizeRelayServer(packet.FromRelayServer ?? _settings.RelayServer)}",
            MessagePort = FluxChatPorts.Relay,
            Status = ParsePresenceStatus(packet.FromStatus),
            LastSeenUtc = DateTimeOffset.UtcNow
        };

    private static bool IsFriendRequestPacket(ChatPacket packet)
        => packet.Intent == FriendRequestIntent ||
           (string.IsNullOrWhiteSpace(packet.Intent) &&
            string.Equals(packet.Body.Trim(), LegacyFriendRequestBody, StringComparison.OrdinalIgnoreCase));

    private static bool IsFriendAcceptPacket(ChatPacket packet)
        => packet.Intent == FriendAcceptIntent ||
           (string.IsNullOrWhiteSpace(packet.Intent) &&
            string.Equals(packet.Body.Trim(), LegacyFriendAcceptBody, StringComparison.OrdinalIgnoreCase));

    private void RequestProfileIfAvatarMissing(ChatPacket packet, ContactViewModel contact)
    {
        if (contact.HasAvatar || !string.IsNullOrWhiteSpace(packet.FromAvatarMediaBase64))
        {
            return;
        }

        _ = RequestProfileFromPacketAsync(packet);
    }

    private async Task RequestProfileFromPacketAsync(ChatPacket packet)
    {
        if (_profile is null || packet.FromUserId == _profile.UserId)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_profileRequestAttempts.TryGetValue(packet.FromUserId, out var lastAttempt) &&
            now - lastAttempt < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _profileRequestAttempts[packet.FromUserId] = now;
        try
        {
            var contact = CreateRelayContactFromPacket(packet);
            var request = CreateProfilePacket(contact.UserId, "", ProfileRequestIntent, GetRelayServer(contact));
            await SendOverRelayAsync(request, contact);
            AppLog.Write($"Profile requested: to={contact.UserId}");
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Profile request failed: to={packet.FromUserId}");
        }
    }

    private void ApplyPacketProfileToContact(ChatPacket packet, ContactViewModel contact)
    {
        contact.DisplayName = packet.FromDisplayName;
        contact.Status = ParsePresenceStatus(packet.FromStatus);
        contact.LastSeenUtc = DateTimeOffset.UtcNow;
        contact.IpAddress = $"{RelayContactPrefix}{NormalizeRelayServer(packet.FromRelayServer ?? _settings.RelayServer)}";

        if (!string.IsNullOrWhiteSpace(packet.FromAvatarKind))
        {
            contact.AvatarKind = packet.FromAvatarKind;
            contact.AvatarScale = packet.FromAvatarScale;
            contact.AvatarOffsetX = packet.FromAvatarOffsetX;
            contact.AvatarOffsetY = packet.FromAvatarOffsetY;
            contact.AvatarVideoStartSeconds = packet.FromAvatarVideoStartSeconds;
            contact.AvatarVideoDurationSeconds = packet.FromAvatarVideoDurationSeconds;
        }

        if (string.IsNullOrWhiteSpace(packet.FromAvatarMediaBase64))
        {
            AppLog.Write($"Incoming avatar skipped: no media payload from={packet.FromUserId}, kind={packet.FromAvatarKind}");
            return;
        }

        var avatarPath = SaveContactAvatar(packet.FromUserId, packet.FromAvatarExtension, packet.FromAvatarMediaBase64);
        if (!string.IsNullOrWhiteSpace(contact.AvatarPath) &&
            !string.Equals(contact.AvatarPath, avatarPath, StringComparison.OrdinalIgnoreCase))
        {
            DeleteAvatarFileIfOwned(contact.AvatarPath);
        }

        contact.AvatarPath = avatarPath;
        AppLog.Write($"Incoming avatar saved: from={packet.FromUserId}, kind={contact.AvatarKind}, path={avatarPath}");
    }

    private void ApplyRelayPresenceAvatarToContact(RelayPresencePacket presence, ContactViewModel contact)
    {
        AppLog.Write($"Relay presence: from={presence.UserId}, avatarKind={presence.AvatarKind}, avatarBytes={presence.AvatarMediaBase64?.Length ?? 0}");

        if (!string.IsNullOrWhiteSpace(presence.AvatarKind))
        {
            contact.AvatarKind = presence.AvatarKind;
            contact.AvatarScale = presence.AvatarScale;
            contact.AvatarOffsetX = presence.AvatarOffsetX;
            contact.AvatarOffsetY = presence.AvatarOffsetY;
            contact.AvatarVideoStartSeconds = presence.AvatarVideoStartSeconds;
            contact.AvatarVideoDurationSeconds = presence.AvatarVideoDurationSeconds;
        }

        if (string.IsNullOrWhiteSpace(presence.AvatarMediaBase64))
        {
            return;
        }

        var avatarPath = SaveContactAvatar(presence.UserId, presence.AvatarExtension, presence.AvatarMediaBase64);
        if (!string.IsNullOrWhiteSpace(contact.AvatarPath) &&
            !string.Equals(contact.AvatarPath, avatarPath, StringComparison.OrdinalIgnoreCase))
        {
            DeleteAvatarFileIfOwned(contact.AvatarPath);
        }

        contact.AvatarPath = avatarPath;
        AppLog.Write($"Relay presence avatar saved: from={presence.UserId}, kind={contact.AvatarKind}, path={avatarPath}");
    }

    private static string SaveContactAvatar(string userId, string? extension, string mediaBase64)
    {
        AppPaths.EnsureAvatarDirectoryCreated();
        var bytes = Convert.FromBase64String(mediaBase64);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? ".avatar" : extension.Trim();
        if (!safeExtension.StartsWith(".", StringComparison.Ordinal))
        {
            safeExtension = $".{safeExtension}";
        }

        safeExtension = string.Concat(safeExtension.Where(c => char.IsAsciiLetterOrDigit(c) || c == '.'));
        if (safeExtension is "." or "")
        {
            safeExtension = ".avatar";
        }

        var path = Path.Combine(AppPaths.AvatarDirectory, $"contact-{userId}-{hash}{safeExtension.ToLowerInvariant()}");
        if (File.Exists(path))
        {
            return path;
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static UserPresenceStatus ParsePresenceStatus(string? status)
        => Enum.TryParse<UserPresenceStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : UserPresenceStatus.Online;

    private void UpsertFriendRequest(ChatPacket packet)
    {
        var relayServer = NormalizeRelayServer(packet.FromRelayServer ?? _settings.RelayServer);
        var existing = _friendRequests.FirstOrDefault(x => x.UserId == packet.FromUserId);
        if (existing is not null)
        {
            _friendRequests.Remove(existing);
            DeleteAvatarFileIfOwned(existing.AvatarPath);
        }

        var request = new FriendRequestViewModel
        {
            UserId = packet.FromUserId,
            DisplayName = packet.FromDisplayName,
            RelayServer = relayServer
        };

        ApplyPacketProfileToFriendRequest(packet, request);
        _friendRequests.Add(request);
        RemoveUnsavedContact(packet.FromUserId);
    }

    private void ApplyPacketProfileToFriendRequest(ChatPacket packet, FriendRequestViewModel request)
    {
        if (!string.IsNullOrWhiteSpace(packet.FromAvatarKind))
        {
            request.AvatarKind = packet.FromAvatarKind;
            request.AvatarScale = packet.FromAvatarScale;
            request.AvatarOffsetX = packet.FromAvatarOffsetX;
            request.AvatarOffsetY = packet.FromAvatarOffsetY;
            request.AvatarVideoStartSeconds = packet.FromAvatarVideoStartSeconds;
            request.AvatarVideoDurationSeconds = packet.FromAvatarVideoDurationSeconds;
        }

        if (string.IsNullOrWhiteSpace(packet.FromAvatarMediaBase64))
        {
            AppLog.Write($"Friend request avatar skipped: no media payload from={packet.FromUserId}, kind={packet.FromAvatarKind}");
            return;
        }

        request.AvatarPath = SaveContactAvatar(packet.FromUserId, packet.FromAvatarExtension, packet.FromAvatarMediaBase64);
        AppLog.Write($"Friend request avatar saved: from={packet.FromUserId}, kind={request.AvatarKind}, path={request.AvatarPath}");
    }

    private void ScrollMessagesToEnd()
    {
        if (_messages.Count > 0)
        {
            MessagesList.ScrollIntoView(_messages[^1]);
        }
    }

    private void AddOrUpdateContact(ContactViewModel contact)
    {
        contact.CurrentUserId = _profile?.UserId ?? "";
        var existing = _contacts.FirstOrDefault(x => x.UserId == contact.UserId);
        if (existing is null)
        {
            if (contact.GroupIsDeleted)
            {
                return;
            }

            _contacts.Add(contact);
            EmptyContactsHint.Visibility = Visibility.Collapsed;
            return;
        }

        existing.CurrentUserId = _profile?.UserId ?? "";
        existing.DisplayName = contact.DisplayName;
        existing.IpAddress = contact.IpAddress;
        existing.MessagePort = contact.MessagePort;
        existing.Status = contact.Status;
        existing.LastSeenUtc = contact.LastSeenUtc == default ? DateTimeOffset.UtcNow : contact.LastSeenUtc;
        existing.IsGroup = contact.IsGroup;
        existing.GroupMemberIds = contact.GroupMemberIds;
        existing.GroupOwnerUserId = contact.GroupOwnerUserId;
        existing.GroupVersion = contact.GroupVersion;
        existing.GroupIsDeleted = contact.GroupIsDeleted;
        existing.GroupMembersJson = contact.GroupMembersJson;
        if (!string.IsNullOrWhiteSpace(contact.AvatarPath) || !existing.HasAvatar)
        {
            existing.AvatarKind = contact.AvatarKind;
            existing.AvatarPath = contact.AvatarPath;
            existing.AvatarScale = contact.AvatarScale;
            existing.AvatarOffsetX = contact.AvatarOffsetX;
            existing.AvatarOffsetY = contact.AvatarOffsetY;
            existing.AvatarVideoStartSeconds = contact.AvatarVideoStartSeconds;
            existing.AvatarVideoDurationSeconds = contact.AvatarVideoDurationSeconds;
        }

        if (existing.GroupIsDeleted)
        {
            RemoveContactFromUi(existing);
        }

        if (_selectedContact?.UserId == existing.UserId)
        {
            ChatSubtitle.Text = existing.IsGroup
                ? $"{existing.GroupMemberCount} participants"
                : $"{existing.IpAddress}:{existing.MessagePort}";
            RefreshGroupMembersPanel();
        }

        if (_selectedContact is { IsGroup: true } selectedGroup &&
            GroupMembersPanel.Visibility == Visibility.Visible &&
            LoadGroupMembers(selectedGroup).Any(x => string.Equals(x.UserId, existing.UserId, StringComparison.Ordinal)))
        {
            RefreshGroupMembersPanel();
        }
    }

    private void RemoveUnsavedContact(string userId)
    {
        var contact = _contacts.FirstOrDefault(x => x.UserId == userId);
        if (contact is null)
        {
            return;
        }

        _contacts.Remove(contact);
        _ = _history.DeleteContactAsync(userId);
        EmptyContactsHint.Visibility = _contacts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetRelayServer(ContactViewModel contact)
    {
        if (contact.IpAddress.StartsWith(RelayContactPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRelayServer(contact.IpAddress[RelayContactPrefix.Length..]);
        }

        if (string.Equals(contact.IpAddress, "VPS", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRelayServer(_settings.RelayServer);
        }

        return NormalizeRelayServer(contact.IpAddress);
    }

    private static bool TryParseRemoteAddress(string value, out string userId, out string relayServer)
    {
        userId = "";
        relayServer = "";
        var atIndex = value.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == value.Length - 1)
        {
            return false;
        }

        userId = value[..atIndex].Trim();
        relayServer = NormalizeRelayServer(value[(atIndex + 1)..].Trim());
        return !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(relayServer);
    }

    private static string NormalizeRelayServer(string relayServer)
    {
        var value = relayServer.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Enter VPS server address.");
        }

        return value.Contains(':', StringComparison.Ordinal)
            ? value
            : $"{value}:{FluxChatPorts.Relay}";
    }

    private void InitializeNotifications()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "FluxChat",
            Visible = true
        };

        _notifyIcon.BalloonTipClicked += async (_, _) => await RestoreFromNotificationAsync();
        _notifyIcon.DoubleClick += async (_, _) => await RestoreFromNotificationAsync();
    }

    private void ShowIncomingNotificationIfNeeded(string displayName, ChatPacket packet)
    {
        if (_notifyIcon is null ||
            !string.IsNullOrWhiteSpace(packet.Intent) ||
            string.IsNullOrWhiteSpace(packet.Body) ||
            !ShouldShowDesktopNotification(packet.FromUserId))
        {
            return;
        }

        var preview = packet.Body.Length <= 120 ? packet.Body : $"{packet.Body[..117]}...";
        try
        {
            _notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(displayName) ? "FluxChat" : displayName;
            _notifyIcon.BalloonTipText = preview;
            _notifyIcon.ShowBalloonTip(5000);
        }
        catch (ArgumentException ex)
        {
            AppLog.Write(ex, "Desktop notification skipped");
        }
    }

    private bool ShouldShowDesktopNotification(string fromUserId)
    {
        if (WindowState == WindowState.Minimized || !_isWindowActive)
        {
            return true;
        }

        return _selectedContact?.UserId != fromUserId;
    }

    private void ShowIncomingCallNotification(ContactViewModel contact)
    {
        _notificationContactUserId = contact.UserId;
        if (_notifyIcon is null)
        {
            return;
        }

        try
        {
            _notifyIcon.BalloonTipTitle = $"Call from {contact.DisplayName}";
            _notifyIcon.BalloonTipText = "Click to open the call";
            _notifyIcon.ShowBalloonTip(8000);
        }
        catch (ArgumentException ex)
        {
            AppLog.Write(ex, "Call notification skipped");
        }
    }

    private async Task RestoreFromNotificationAsync()
    {
        Dispatcher.Invoke(() =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        });

        if (!string.IsNullOrWhiteSpace(_notificationContactUserId))
        {
            var contact = _contacts.FirstOrDefault(x => x.UserId == _notificationContactUserId);
            _notificationContactUserId = null;
            if (contact is not null)
            {
                await OpenContactAsync(contact);
            }
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateService.CheckLatestAsync(_stop.Token);
            if (update is null)
            {
                return;
            }

            var updateTask = await Dispatcher.InvokeAsync(async () =>
            {
                NetworkStatusText.Text = update.IsRepair
                    ? $"Update files available: {update.Version}"
                    : $"Update available: {update.Version}";
                var prompt = update.IsRepair
                    ? $"Additional FluxChat {update.Version} files are available.\n\nInstall them now?"
                    : $"FluxChat {update.Version} is available.\n\nInstall it now?";
                var result = MessageBox.Show(
                    prompt,
                    "FluxChat update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    NetworkStatusText.Text = "Downloading update...";
                    await UpdateService.DownloadAndInstallAsync(update, _stop.Token);
                    NetworkStatusText.Text = "Update downloaded. Restarting...";
                    Close();
                }
                catch (Exception ex) when (!_stop.IsCancellationRequested)
                {
                    AppLog.Write(ex, "Update install failed");
                    NetworkStatusText.Text = $"Update failed: {ex.Message}";
                    MessageBox.Show(
                        $"Could not install the update automatically.\n\n{ex.Message}\n\nRelease page:\n{update.ReleasePage}",
                        "FluxChat update",
                        MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                }
            });
            await updateTask;
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, "Update check failed");
        }
    }

    private UserPresenceStatus GetCurrentStatus()
    {
        if (_selectedStatus is UserPresenceStatus.Offline or UserPresenceStatus.Idle)
        {
            return _selectedStatus;
        }

        return IdleDetector.GetIdleTime() >= IdleAfter
            ? UserPresenceStatus.Idle
            : UserPresenceStatus.Online;
    }

    private string GetCurrentStatusText() => GetCurrentStatus() switch
    {
        UserPresenceStatus.Online => "online",
        UserPresenceStatus.Idle => "idle",
        _ => "offline"
    };

    private void UpdateProfileStatusVisuals()
    {
        var color = _selectedStatus switch
        {
            UserPresenceStatus.Online => System.Windows.Media.Color.FromRgb(35, 165, 90),
            UserPresenceStatus.Idle => System.Windows.Media.Color.FromRgb(240, 178, 50),
            _ => System.Windows.Media.Color.FromRgb(128, 132, 142)
        };

        var brush = new SolidColorBrush(color);
        ProfileStatusRing.Stroke = brush;
        ProfileStatusDot.Fill = brush;
        ProfileStatusText.Text = GetCurrentStatusText();
    }

    private void RefreshProfileUi()
    {
        if (_profile is null)
        {
            return;
        }

        var initials = GetInitials(_profile.DisplayName);
        DisplayNameText.Text = _profile.DisplayName;
        UserIdText.Text = _profile.UserId;
        AddressText.Text = $"{_profile.UserId}@vps";
        ProfileFlyoutName.Text = _profile.DisplayName;
        ProfileFlyoutUserId.Text = _profile.UserId;
        ProfileFlyoutAddress.Text = $"{_profile.UserId}@vps";
        ProfileInitialsText.Text = initials;
        SettingsDisplayNameInput.Text = _profile.DisplayName;
        SettingsAvatarInitials.Text = initials;
        LoadAvatarSelectionFromProfile();
        ApplyAvatarVisuals();
        UpdateProfileStatusVisuals();
    }

    private void ApplyAvatarColor(string color)
    {
        _selectedAvatarColor = color;
        var brush = CreateBrush(color, "#5865f2");
        ProfileAvatarColor.Fill = brush;
        SettingsAvatarPreview.Fill = brush.Clone();
    }

    private void ShowAvatarEditor(string kind, string path)
    {
        _pendingAvatarKind = kind;
        _pendingAvatarPath = path;
        _pendingAvatarScale = 1;
        _pendingAvatarOffsetX = 0;
        _pendingAvatarOffsetY = 0;
        _pendingAvatarVideoStartSeconds = 0;
        _pendingAvatarVideoDurationSeconds = 10;

        AvatarEditorTitle.Text = kind == "video" ? "Edit Animated Avatar" : "Edit Image";
        AvatarEditorImage.Visibility = Visibility.Collapsed;
        AvatarEditorVideo.Visibility = Visibility.Collapsed;
        AvatarEditorVideoControls.Visibility = kind == "video" ? Visibility.Visible : Visibility.Collapsed;
        AvatarEditorZoomSlider.Value = 1;
        AvatarEditorVideoStartSlider.Value = 0;
        AvatarEditorVideoDurationSlider.Value = 10;

        if (kind == "image")
        {
            AvatarEditorImage.Source = LoadBitmap(path);
            AvatarEditorImage.Visibility = Visibility.Visible;
            AvatarEditorVideo.Stop();
        }
        else
        {
            AvatarEditorVideo.Source = new Uri(path, UriKind.Absolute);
            AvatarEditorVideo.Visibility = Visibility.Visible;
            _avatarVideoLoopTimer.Start();
            RestartEditorVideo();
        }

        ApplyAvatarEditorTransform();
        AvatarEditorOverlay.Visibility = Visibility.Visible;
    }

    private void ApplyAvatarEditorTransform()
    {
        ClampPendingAvatarOffset();
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(_pendingAvatarScale, _pendingAvatarScale));
        transform.Children.Add(new TranslateTransform(_pendingAvatarOffsetX, _pendingAvatarOffsetY));
        AvatarEditorImage.RenderTransform = transform;
        AvatarEditorVideo.RenderTransform = transform.Clone();
    }

    private void RestartEditorVideo()
    {
        if (_pendingAvatarKind != "video" || AvatarEditorOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        AvatarEditorVideo.Position = TimeSpan.FromSeconds(_pendingAvatarVideoStartSeconds);
        AvatarEditorVideo.Play();
        _avatarVideoLoopTimer.Start();
    }

    private void LoadAvatarSelectionFromProfile()
    {
        if (_profile is null)
        {
            return;
        }

        _selectedAvatarColor = string.IsNullOrWhiteSpace(_profile.AvatarColor) ? "#5865f2" : _profile.AvatarColor;
        _selectedAvatarKind = string.IsNullOrWhiteSpace(_profile.AvatarKind) ? "color" : _profile.AvatarKind;
        _selectedAvatarPath = _profile.AvatarPath ?? "";
        _selectedAvatarScale = Math.Clamp(_profile.AvatarScale <= 0 ? 1 : _profile.AvatarScale, 1, 3);
        _selectedAvatarOffsetX = Math.Clamp(_profile.AvatarOffsetX, -180, 180);
        _selectedAvatarOffsetY = Math.Clamp(_profile.AvatarOffsetY, -180, 180);
        _selectedAvatarVideoStartSeconds = Math.Max(0, _profile.AvatarVideoStartSeconds);
        _selectedAvatarVideoDurationSeconds = Math.Clamp(_profile.AvatarVideoDurationSeconds <= 0 ? 10 : _profile.AvatarVideoDurationSeconds, 1, 10);

        SettingsAvatarZoomSlider.Value = _selectedAvatarScale;
        SettingsAvatarOffsetXSlider.Value = _selectedAvatarOffsetX;
        SettingsAvatarOffsetYSlider.Value = _selectedAvatarOffsetY;
        SettingsAvatarVideoStartSlider.Value = _selectedAvatarVideoStartSeconds;
        SettingsAvatarVideoDurationSlider.Value = _selectedAvatarVideoDurationSeconds;
    }

    private void ApplyAvatarVisuals()
    {
        ApplyAvatarColor(_selectedAvatarColor);
        ApplyAvatarTransforms();
        ProfileAvatarImage.Visibility = Visibility.Collapsed;
        ProfileAvatarVideo.Visibility = Visibility.Collapsed;
        SettingsAvatarImage.Visibility = Visibility.Collapsed;
        SettingsAvatarVideo.Visibility = Visibility.Collapsed;
        ProfileAvatarVideo.Stop();
        SettingsAvatarVideo.Stop();
        _avatarVideoLoopTimer.Stop();

        var mediaExists = !string.IsNullOrWhiteSpace(_selectedAvatarPath) && File.Exists(_selectedAvatarPath);
        if (_selectedAvatarKind == "image" && mediaExists)
        {
            var image = LoadBitmap(_selectedAvatarPath);
            ProfileAvatarImage.Source = image;
            SettingsAvatarImage.Source = image;
            ProfileAvatarImage.Visibility = Visibility.Visible;
            SettingsAvatarImage.Visibility = Visibility.Visible;
            ProfileInitialsText.Visibility = Visibility.Collapsed;
            SettingsAvatarInitials.Visibility = Visibility.Collapsed;
            return;
        }

        if (_selectedAvatarKind == "video" && mediaExists)
        {
            var source = new Uri(_selectedAvatarPath, UriKind.Absolute);
            ProfileAvatarVideo.Source = source;
            SettingsAvatarVideo.Source = source;
            ProfileAvatarVideo.Visibility = Visibility.Visible;
            SettingsAvatarVideo.Visibility = Visibility.Visible;
            ProfileInitialsText.Visibility = Visibility.Collapsed;
            SettingsAvatarInitials.Visibility = Visibility.Collapsed;
            RestartAvatarVideos();
            _avatarVideoLoopTimer.Start();
            return;
        }

        ProfileInitialsText.Visibility = Visibility.Visible;
        SettingsAvatarInitials.Visibility = Visibility.Visible;
    }

    private void ApplyAvatarTransforms()
    {
        ClampSelectedAvatarOffset();
        var profileTransform = CreateAvatarTransform(ProfileAvatarSize);
        var settingsTransform = CreateAvatarTransform(SettingsAvatarSize);
        ProfileAvatarImage.RenderTransform = profileTransform;
        ProfileAvatarVideo.RenderTransform = profileTransform.Clone();
        SettingsAvatarImage.RenderTransform = settingsTransform;
        SettingsAvatarVideo.RenderTransform = settingsTransform.Clone();
    }

    private TransformGroup CreateAvatarTransform(double targetSize)
    {
        var factor = targetSize / AvatarEditorPreviewSize;
        var offsetX = ClampAvatarOffset(_selectedAvatarOffsetX, _selectedAvatarScale);
        var offsetY = ClampAvatarOffset(_selectedAvatarOffsetY, _selectedAvatarScale);
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(_selectedAvatarScale, _selectedAvatarScale));
        transform.Children.Add(new TranslateTransform(offsetX * factor, offsetY * factor));
        return transform;
    }

    private void ClampPendingAvatarOffset()
    {
        _pendingAvatarOffsetX = ClampAvatarOffset(_pendingAvatarOffsetX, _pendingAvatarScale);
        _pendingAvatarOffsetY = ClampAvatarOffset(_pendingAvatarOffsetY, _pendingAvatarScale);
    }

    private void ClampSelectedAvatarOffset()
    {
        _selectedAvatarOffsetX = ClampAvatarOffset(_selectedAvatarOffsetX, _selectedAvatarScale);
        _selectedAvatarOffsetY = ClampAvatarOffset(_selectedAvatarOffsetY, _selectedAvatarScale);
    }

    private static double ClampAvatarOffset(double value, double scale)
    {
        var maxOffset = Math.Max(0, ((AvatarEditorPreviewSize * scale) - AvatarEditorCircleSize) / 2);
        return Math.Clamp(value, -maxOffset, maxOffset);
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string CopyAvatarFile(string sourcePath)
    {
        AppPaths.EnsureAvatarDirectoryCreated();
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".avatar";
        }

        var destination = Path.Combine(AppPaths.AvatarDirectory, $"avatar-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }

    private static void DeleteAvatarFileIfOwned(string? path, string? exceptPath = null)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(path, exceptPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var avatarDirectory = Path.GetFullPath(AppPaths.AvatarDirectory);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(avatarDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                return;
            }

            File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Write(ex, $"Avatar cleanup failed: path={path}");
        }
    }

    private static void DeleteAttachmentFileIfOwned(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var attachmentsDirectory = Path.GetFullPath(AppPaths.AttachmentsDirectory);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(attachmentsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                return;
            }

            File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Write(ex, $"Attachment cleanup failed: path={path}");
        }
    }

    private void ResetAvatarCrop()
    {
        _selectedAvatarScale = 1;
        _selectedAvatarOffsetX = 0;
        _selectedAvatarOffsetY = 0;
        SettingsAvatarZoomSlider.Value = 1;
        SettingsAvatarOffsetXSlider.Value = 0;
        SettingsAvatarOffsetYSlider.Value = 0;
    }

    private void RestartAvatarVideos()
    {
        if (_selectedAvatarKind != "video")
        {
            return;
        }

        var start = TimeSpan.FromSeconds(_selectedAvatarVideoStartSeconds);
        ProfileAvatarVideo.Position = start;
        SettingsAvatarVideo.Position = start;
        ProfileAvatarVideo.Play();
        SettingsAvatarVideo.Play();
    }

    private void AvatarVideoLoopTimer_OnTick(object? sender, EventArgs e)
    {
        var hasSelectedVideo = _selectedAvatarKind == "video";
        var hasEditorVideo = AvatarEditorOverlay.Visibility == Visibility.Visible && _pendingAvatarKind == "video";
        if (!hasSelectedVideo && !hasEditorVideo)
        {
            _avatarVideoLoopTimer.Stop();
            return;
        }

        if (hasSelectedVideo)
        {
            var start = TimeSpan.FromSeconds(_selectedAvatarVideoStartSeconds);
            var end = start + TimeSpan.FromSeconds(Math.Min(10, Math.Max(1, _selectedAvatarVideoDurationSeconds)));
            LoopAvatarVideo(ProfileAvatarVideo, start, end);
            LoopAvatarVideo(SettingsAvatarVideo, start, end);
        }

        if (hasEditorVideo)
        {
            var start = TimeSpan.FromSeconds(_pendingAvatarVideoStartSeconds);
            var end = start + TimeSpan.FromSeconds(Math.Min(10, Math.Max(1, _pendingAvatarVideoDurationSeconds)));
            LoopAvatarVideo(AvatarEditorVideo, start, end);
        }
    }

    private static void LoopAvatarVideo(MediaElement element, TimeSpan start, TimeSpan end)
    {
        if (element.Visibility != Visibility.Visible)
        {
            return;
        }

        if (element.Position >= end)
        {
            element.Position = start;
            element.Play();
        }
    }

    private void AvatarVideo_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement element)
        {
            return;
        }

        TryRestartAvatarVideo(element, TimeSpan.Zero);
    }

    private void AvatarVideo_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement element)
        {
            return;
        }

        TryRestartAvatarVideo(element, TimeSpan.Zero);
    }

    private static void TryRestartAvatarVideo(MediaElement element, TimeSpan position)
    {
        try
        {
            element.Position = position;
            element.Play();
        }
        catch (NotSupportedException)
        {
        }
    }

    private void AvatarVideo_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement element || !element.NaturalDuration.HasTimeSpan)
        {
            RestartAvatarVideos();
            return;
        }

        var totalSeconds = Math.Max(1, element.NaturalDuration.TimeSpan.TotalSeconds);
        SettingsAvatarVideoStartSlider.Maximum = Math.Max(0, totalSeconds - 1);
        SettingsAvatarVideoDurationSlider.Maximum = Math.Min(10, totalSeconds);
        _selectedAvatarVideoStartSeconds = Math.Min(_selectedAvatarVideoStartSeconds, SettingsAvatarVideoStartSlider.Maximum);
        _selectedAvatarVideoDurationSeconds = Math.Min(_selectedAvatarVideoDurationSeconds, SettingsAvatarVideoDurationSlider.Maximum);
        SettingsAvatarVideoStartSlider.Value = _selectedAvatarVideoStartSeconds;
        SettingsAvatarVideoDurationSlider.Value = _selectedAvatarVideoDurationSeconds;
        RestartAvatarVideos();
    }

    private void AvatarEditorVideo_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (!AvatarEditorVideo.NaturalDuration.HasTimeSpan)
        {
            RestartEditorVideo();
            return;
        }

        var totalSeconds = Math.Max(1, AvatarEditorVideo.NaturalDuration.TimeSpan.TotalSeconds);
        AvatarEditorVideoStartSlider.Maximum = Math.Max(0, totalSeconds - 1);
        AvatarEditorVideoDurationSlider.Maximum = Math.Min(10, totalSeconds);
        _pendingAvatarVideoStartSeconds = Math.Min(_pendingAvatarVideoStartSeconds, AvatarEditorVideoStartSlider.Maximum);
        _pendingAvatarVideoDurationSeconds = Math.Min(_pendingAvatarVideoDurationSeconds, AvatarEditorVideoDurationSlider.Maximum);
        AvatarEditorVideoStartSlider.Value = _pendingAvatarVideoStartSeconds;
        AvatarEditorVideoDurationSlider.Value = _pendingAvatarVideoDurationSeconds;
        RestartEditorVideo();
    }

    private static SolidColorBrush CreateBrush(string color, string fallback)
    {
        try
        {
            if (new BrushConverter().ConvertFromString(color) is SolidColorBrush brush)
            {
                return brush;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return (SolidColorBrush)new BrushConverter().ConvertFromString(fallback)!;
    }

    private static string GetInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "ME";
        }

        var initials = string.Concat(displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant();
        return initials.Length <= 2 ? initials : initials[..2];
    }

}
