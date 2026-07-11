using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluxChat.Shared;
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
    private const string ProfileUpdateIntent = "profile-update";
    private const string ProfileRequestIntent = "profile-request";
    private const string CallInviteIntent = "call-invite";
    private const string CallAcceptIntent = "call-accept";
    private const string CallDeclineIntent = "call-decline";
    private const string CallEndIntent = "call-end";
    private const string CallLeaveIntent = "call-leave";
    private const string CallJoinIntent = "call-join";
    private const string CallAudioIntent = "call-audio";
    private const string CallAudioStateIntent = "call-audio-state";
    private const string CallScreenStartIntent = "call-screen-start";
    private const string CallScreenFrameIntent = "call-screen-frame";
    private const string CallScreenStopIntent = "call-screen-stop";
    private const string CallScreenWebRtcOfferIntent = "call-screen-webrtc-offer";
    private const string CallScreenWebRtcAnswerIntent = "call-screen-webrtc-answer";
    private const string CallScreenWebRtcIceIntent = "call-screen-webrtc-ice";
    private const string LegacyFriendRequestBody = "Friend request";
    private const string LegacyFriendAcceptBody = "Friend request accepted";
    private const string ControlBodyPrefix = "fluxchat-control:";
    private const int MaxAvatarSyncBytes = 5_000_000;
    private const int ScreenShareMaxFrameBodyChars = 1_800_000;
    private const long ScreenShareJpegQuality = 74L;
    private const long ScreenShareHighFrameRateJpegQuality = 50L;
    private const long ScreenShareHighLoadJpegQuality = 46L;
    private const int ScreenShareMaxCompactHeight = 150;
    private static readonly TimeSpan ScreenShareSelfPreviewInterval = TimeSpan.FromMilliseconds(180);
    private const int ScreenShareMinAdaptiveHeight = 720;
    private const int ScreenShareHighResolutionMinAdaptiveHeight = 1080;
    private const int ScreenShareAdaptiveStep = 120;
    private const int ScreenShareHighResolutionMaxFrameRate = 30;
    private const int ScreenShareFallbackMaxHeight = 720;
    private const int ScreenShareFallbackMaxFrameRate = 15;
    private const int ScreenShareMaxPeerRenderFrameRate = 60;
    private const int ScreenShareFullscreenMaxPeerRenderFrameRate = 60;
    private const int ScreenShareEncodedChunkSize = 32 * 1024;
    private const int ScreenShareEncodedDataChannelBufferLimit = 1024 * 1024;
    private static readonly TimeSpan ScreenShareSendTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly bool ScreenSharePreferWebRtc = true;
    private static readonly bool ScreenSharePreferEncodedWebRtc = true;
    private static readonly TimeSpan ScreenShareDuplicateFrameInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ScreenShareWebRtcDuplicateFrameInterval = TimeSpan.Zero;
    private const string ScreenShareCodecJpeg = "jpeg";
    private const string ScreenShareCodecH264Fmp4 = "h264-fmp4";
    private const int CallAudioMinDecodedBytes = 64;
    private const int CallAudioMaxDecodedBytes = 2560;
    private const int CallAudioTargetPeak = 9000;
    private const int CallAudioOutputLimitPeak = 24000;
    private const int CallAudioSilencePeak = 45;
    private const int CallAudioVoiceFloorPeak = 180;
    private const int CallAudioMaxCaptureQueueFrames = 8;
    private const int CallAudioMaxPlaybackQueueFrames = 8;
    private const double CallAudioMaxGain = 3.0;
    private const double CallAudioGainAttack = 0.35;
    private const double CallAudioGainRelease = 0.10;
    private static readonly TimeSpan CallAudioSendTimeout = TimeSpan.FromMilliseconds(750);
    private const double AvatarEditorPreviewSize = 350;
    private const double AvatarEditorCircleSize = 344;
    private const double ProfileAvatarSize = 44;
    private const double SettingsAvatarSize = 64;

    private readonly ObservableCollection<ContactViewModel> _contacts = [];
    private readonly ObservableCollection<MessageViewModel> _messages = [];
    private readonly ObservableCollection<FriendRequestViewModel> _friendRequests = [];
    private readonly ObservableCollection<GroupCandidateViewModel> _groupCandidates = [];
    private readonly ObservableCollection<ScreenShareSourceItem> _screenShareSources = [];
    private readonly ObservableCollection<ScreenShareSourceItem> _visibleScreenShareSources = [];
    private readonly HashSet<string> _selectedGroupMemberIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _profileRequestAttempts = [];
    private readonly HistoryStore _history = new();
    private readonly CancellationTokenSource _stop = new();
    private AppSettings _settings = new();
    private UserProfile? _profile;
    private RelayClient? _relayClient;
    private Forms.NotifyIcon? _notifyIcon;
    private ContactViewModel? _selectedContact;
    private ContactViewModel? _draftGroupContact;
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
    private readonly DispatcherTimer _callRingtoneTimer;
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
    private long _sentScreenShareFrames;
    private long _sentEncodedScreenShareChunks;
    private long _receivedScreenShareFrames;
    private long _droppedReceivedScreenShareFrames;
    private long _lastSelfScreenSharePreviewTicks;
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
    private double _sendAudioGain = 1;
    private double _playbackAudioGain = 1;
    private readonly ConcurrentQueue<CallPlaybackFrame> _callPlaybackQueue = new();
    private readonly SemaphoreSlim _callPlaybackSignal = new(0, int.MaxValue);
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
        _callRingtoneTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _callRingtoneTimer.Tick += (_, _) => System.Media.SystemSounds.Exclamation.Play();
        ProfileAvatarVideo.MediaOpened += AvatarVideo_OnMediaOpened;
        ProfileAvatarVideo.MediaEnded += AvatarVideo_OnMediaEnded;
        SettingsAvatarVideo.MediaOpened += AvatarVideo_OnMediaOpened;
        SettingsAvatarVideo.MediaEnded += AvatarVideo_OnMediaEnded;
        AvatarEditorVideo.MediaOpened += AvatarEditorVideo_OnMediaOpened;
        AvatarEditorVideo.MediaEnded += AvatarVideo_OnMediaEnded;
        ContactsList.ItemsSource = _contacts;
        MessagesList.ItemsSource = _messages;
        FriendRequestsList.ItemsSource = _friendRequests;
        GroupFriendsList.ItemsSource = _groupCandidates;
        Loaded += OnLoaded;
        Closed += OnClosed;
        KeyDown += MainWindow_OnKeyDown;
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

    private async Task InitializeAsync()
    {
        AppLog.Write("Main window initialization started");
        var isFirstRun = !AppSettingsStore.Exists();
        _settings = await AppSettingsStore.LoadAsync();
        _profile = await UserProfileStore.LoadOrCreateAsync();
        AppLog.Write($"Profile loaded: userId={_profile.UserId}, displayName={_profile.DisplayName}");
        await _history.InitializeAsync();
        AppLog.Write("History store initialized");

        LoadAvatarSelectionFromProfile();
        RefreshProfileUi();
        InitializeNotifications();
        ServerAddressInput.Text = _settings.RelayServer;
        ServerAccessKeyInput.Text = _settings.RelayAccessKey;
        SettingsServerAddressInput.Text = _settings.RelayServer;
        SettingsServerAccessKeyInput.Text = _settings.RelayAccessKey;
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
            AddOrUpdateContact(contact);
        }

        _relayClient = new RelayClient(_profile);
        _relayClient.MessageReceived += OnRelayMessageReceived;
        _relayClient.AudioReceived += OnRelayAudioReceived;
        _relayClient.ScreenFrameReceived += OnRelayScreenFrameReceived;
        _relayClient.PresenceReceived += OnRelayPresenceReceived;
        _relayClient.StatusChanged += OnNetworkStatusChanged;
        await ConnectRelayAsync();
        _presenceTimer.Start();

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
                        PostScreenShareWebRtcMessage(new { type = "stop" });
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
                        PostScreenShareWebRtcMessage(new { type = "stop" });
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
                    _screenShareWebRtcActive = true;
                    SetScreenShareWebRtcVisible(true);
                    if (!_isScreenShareFocusMode)
                    {
                        EnterScreenShareFocusMode();
                    }

                    UpdateScreenShareStageVisibility();
                    AppLog.Write("Screen share WebRTC remote stream started");
                    break;
                case "remote-playing":
                    _peerScreenSharing = true;
                    _isWatchingPeerScreen = true;
                    _screenShareWebRtcActive = true;
                    SetScreenShareWebRtcVisible(true);
                    UpdateScreenShareStageVisibility();
                    AppLog.Write($"Screen share WebRTC remote video playing: width={GetJsonDouble(root, "width"):0}, height={GetJsonDouble(root, "height"):0}, readyState={GetJsonDouble(root, "readyState"):0}");
                    break;
                case "remote-video-ready":
                    AppLog.Write($"Screen share WebRTC remote video ready: width={GetJsonDouble(root, "width"):0}, height={GetJsonDouble(root, "height"):0}, readyState={GetJsonDouble(root, "readyState"):0}");
                    break;
                case "focus-request":
                    ToggleScreenShareFocusFromPreview();
                    break;
                case "local-ended":
                    if (_isScreenSharing)
                    {
                        StopScreenShare(sendSignal: true);
                    }
                    break;
                case "stopped":
                    _screenShareWebRtcActive = false;
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

        if (_activeCallContact?.UserId != packet.FromUserId)
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
            connectTimeoutMs = 9000
        });

        var turnCount = iceServers.Count(server =>
            server.Urls.Any(url => url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase)));
        AppLog.Write($"Screen share WebRTC configured: iceServers={iceServers.Count}, turnServers={turnCount}");
    }

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
        if (_screenShareResolution >= 1440)
        {
            return _screenShareFrameRate >= 60 ? 30_000_000 : 18_000_000;
        }

        if (_screenShareResolution >= 1080)
        {
            return _screenShareFrameRate >= 60 ? 14_000_000 : 8_000_000;
        }

        return _screenShareFrameRate >= 60 ? 6_000_000 : 3_500_000;
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
        ScreenShareWebView.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CallScreenShareGrid.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool IsScreenShareWebRtcPreferred()
        => ScreenSharePreferWebRtc && _screenShareWebRtcReady;

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
        PostScreenShareWebRtcMessage(new { type = "stop" });
        _screenShareWebRtcActive = false;
        SetScreenShareWebRtcVisible(false);
        _screenShareResolution = Math.Min(_screenShareResolution, ScreenShareFallbackMaxHeight);
        _screenShareFrameRate = Math.Min(
            ScreenShareFallbackMaxFrameRate,
            ClampScreenShareFrameRate(_screenShareResolution, _screenShareFrameRate));
        _screenShareAdaptiveHeight = Math.Min(_screenShareResolution, ScreenShareFallbackMaxHeight);
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

    private async void OnClosed(object? sender, EventArgs e)
    {
        _presenceTimer.Stop();
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
            _ = _history.SaveContactAsync(contact);

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

            if (packet.Intent is CallInviteIntent or CallAcceptIntent or CallDeclineIntent or CallEndIntent or CallLeaveIntent or CallJoinIntent or CallAudioStateIntent or CallScreenStartIntent or CallScreenFrameIntent or CallScreenStopIntent or CallScreenWebRtcOfferIntent or CallScreenWebRtcAnswerIntent or CallScreenWebRtcIceIntent)
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
                IsOutgoing = false,
                SentAtUtc = packet.SentAtUtc
            };
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

    private async void ContactsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContactsList.SelectedItem is not ContactViewModel contact)
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
        ComposerPanel.Visibility = Visibility.Visible;
        EmptyChatHint.Visibility = Visibility.Collapsed;
        StartCallButton.Visibility = contact.IsGroup ? Visibility.Collapsed : Visibility.Visible;

        _messages.Clear();
        foreach (var message in await _history.LoadConversationAsync(contact.UserId))
        {
            _messages.Add(message);
        }

        ScrollMessagesToEnd();
        MessageInput.Focus();
    }

    private async void StartCallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedContact is null || _selectedContact.IsGroup)
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

    private void ScreenSharePickerCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        _screenShareWebRtcSettingsMode = false;
        ScreenSharePickerOverlay.Visibility = Visibility.Collapsed;
        ScreenShareSourceList.SelectedItem = null;
        UpdateScreenSharePickerState();
    }

    private void ShowScreenSharePicker(bool webRtcSettingsOnly)
    {
        _screenShareWebRtcSettingsMode = webRtcSettingsOnly;
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
        StartScreenShare(source);
        UpdateScreenSharePickerState();
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
            ? "Native capture uses WebRTC transport and hardware video when available; 1440p60 still depends on GPU, network, and NAT."
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
        ToggleScreenShareFocusFromPreview();
    }

    private void ToggleScreenShareFocusFromPreview()
    {
        if (!_isScreenSharing && !_peerScreenSharing)
        {
            return;
        }

        if (!_isScreenShareFocusMode)
        {
            EnterScreenShareFocusMode();
            return;
        }

        SetScreenShareFullscreenMode(!_isScreenShareFullscreenMode);
        UpdateScreenShareStageVisibility();
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
        group.GroupMemberIds = string.Join('|', _selectedGroupMemberIds);
        group.DisplayName = BuildGroupDisplayName(group.GroupMemberIdsList);
        group.Status = UserPresenceStatus.Online;
        group.LastSeenUtc = DateTimeOffset.UtcNow;

        AddOrUpdateContact(group);
        await _history.SaveContactAsync(group);
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
            AvatarKind = "color"
        };

    private string BuildGroupDisplayName(IReadOnlyList<string> memberIds)
    {
        var names = memberIds
            .Select(id => _contacts.FirstOrDefault(x => x.UserId == id)?.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToArray();

        return names.Length == 0
            ? "Group"
            : string.Join(", ", names);
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

    private void ShowSettingsTab(string tab)
    {
        var isVoice = string.Equals(tab, "voice", StringComparison.OrdinalIgnoreCase);
        SettingsAccountHeader.Visibility = isVoice ? Visibility.Collapsed : Visibility.Visible;
        SettingsAccountContent.Visibility = isVoice ? Visibility.Collapsed : Visibility.Visible;
        SettingsVoiceHeader.Visibility = isVoice ? Visibility.Visible : Visibility.Collapsed;
        SettingsVoiceContent.Visibility = isVoice ? Visibility.Visible : Visibility.Collapsed;
        SettingsAccountTabButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isVoice ? "#00000000" : "#404249"));
        SettingsVoiceTabButton.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isVoice ? "#404249" : "#00000000"));
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
        var playbackPcm = AmplifyPcm(pcm, peak, out _);
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
    }

    private async void RenameContactMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ContactViewModel contact })
        {
            return;
        }

        var dialog = new RenameContactWindow(contact.DisplayName)
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
            $"Delete {contact.DisplayName} from contacts?",
            "FluxChat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _contacts.Remove(contact);
        await _history.DeleteContactAsync(contact.UserId);

        if (_contacts.Count == 0)
        {
            EmptyContactsHint.Visibility = Visibility.Visible;
        }

        if (_selectedContact?.UserId == contact.UserId)
        {
            _selectedContact = null;
            _messages.Clear();
            ChatTitle.Text = "Choose a contact";
            ChatSubtitle.Text = "Double-click a contact to open conversation";
            ComposerPanel.Visibility = Visibility.Collapsed;
            StartCallButton.Visibility = Visibility.Collapsed;
            EmptyChatHint.Visibility = Visibility.Visible;
        }

        NetworkStatusText.Text = $"Deleted {contact.DisplayName}";
    }

    private async void MessageInput_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await SendCurrentMessageAsync();
        }
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_profile is null || _selectedContact is null)
        {
            return;
        }

        var body = MessageInput.Text.Trim();
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
        var currentRelayServer = NormalizeRelayServer(_settings.RelayServer);
        var contactRelayServer = GetRelayServer(_selectedContact);
        var toRelayServer = string.Equals(currentRelayServer, contactRelayServer, StringComparison.OrdinalIgnoreCase)
            ? null
            : contactRelayServer;
        var packet = CreateProfilePacket(_selectedContact.UserId, body);
        var message = new MessageViewModel
        {
            MessageId = packet.MessageId,
            PeerUserId = _selectedContact.UserId,
            Body = body,
            IsOutgoing = true,
            SentAtUtc = packet.SentAtUtc
        };

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
            _messages.Add(new MessageViewModel
            {
                MessageId = Guid.NewGuid(),
                PeerUserId = _selectedContact.UserId,
                Body = $"Failed to send: {ex.Message}",
                IsOutgoing = false,
                SentAtUtc = DateTimeOffset.UtcNow
            });
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
            IsOutgoing = true,
            SentAtUtc = DateTimeOffset.UtcNow
        };

        _messages.Add(message);
        ScrollMessagesToEnd();
        await _history.SaveAsync(message);

        var sent = 0;
        foreach (var memberId in group.GroupMemberIdsList)
        {
            var member = _contacts.FirstOrDefault(x => x.UserId == memberId && !x.IsGroup);
            if (member is null)
            {
                continue;
            }

            try
            {
                var packet = CreateProfilePacket(member.UserId, body);
                await SendOverRelayAsync(packet, member, log: false);
                sent++;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex, $"Group message send failed: group={group.UserId}, to={memberId}");
            }
        }

        NetworkStatusText.Text = $"Group message sent to {sent}/{group.GroupMemberCount}";
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
            var packet = CreateCallPacket(contact, "", intent);
            await SendOverRelayAsync(packet, contact);
            AppLog.Write($"Call signal sent: intent={intent}, to={contact.UserId}, bodyLength={packet.Body.Length}, targetRelay={packet.ToRelayServer}");
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
            var packet = CreateCallPacket(_activeCallContact, body, CallAudioStateIntent);
            await SendOverRelayAsync(packet, _activeCallContact, log: false);
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
            var packet = CreateCallPacket(_activeCallContact, body, intent);
            await SendOverRelayAsync(packet, _activeCallContact, log: false);
            AppLog.Write($"Screen share signal sent: intent={intent}, to={_activeCallContact.UserId}, bodyLength={body.Length}");
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Screen share signal failed: intent={intent}, to={_activeCallContact?.UserId}");
        }
    }

    private void RefreshScreenShareSources()
    {
        _screenShareSources.Clear();

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
        _screenShareAdaptiveHeight = GetInitialScreenShareAdaptiveHeight();
        Interlocked.Exchange(ref _sentScreenShareFrames, 0);
        Interlocked.Exchange(ref _sentEncodedScreenShareChunks, 0);
        Interlocked.Exchange(ref _pendingScreenShareFrame, 0);
        Interlocked.Exchange(ref _pendingNativeWebRtcWebViewFrame, 0);
        Interlocked.Exchange(ref _screenShareJpegLoopStarted, 0);
        Interlocked.Exchange(ref _lastSelfScreenSharePreviewTicks, 0);
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
                            title = source.Title,
                            mimeType = "video/mp4; codecs=\"avc1.42E01E\"",
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
            PostScreenShareWebRtcMessage(new { type = "stop" });
            _screenShareWebRtcActive = false;
            SetScreenShareWebRtcVisible(false);
        }

        Interlocked.Exchange(ref _pendingScreenShareFrame, 0);
        Interlocked.Exchange(ref _pendingNativeWebRtcWebViewFrame, 0);
        Interlocked.Exchange(ref _screenShareJpegLoopStarted, 0);
        Interlocked.Exchange(ref _lastSelfScreenSharePreviewTicks, 0);
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
        var payload = JsonSerializer.Serialize(new ScreenShareFramePayload(
            sent,
            Convert.ToBase64String(jpeg),
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
                new Action(() => CallSelfScreenSharePreview.Source = image),
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
        if (effectiveHeight >= 1440)
        {
            return _screenShareFrameRate >= 60 ? 22_000 : 14_000;
        }

        if (effectiveHeight >= 1080)
        {
            return _screenShareFrameRate >= 60 ? 10_000 : 6_000;
        }

        return _screenShareFrameRate >= 60 ? 5_500 : 3_500;
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
        if (_activeCallContact?.UserId != packet.FromUserId)
        {
            return;
        }

        switch (packet.Intent)
        {
            case CallScreenStartIntent:
                _peerScreenSharing = true;
                _isWatchingPeerScreen = true;
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
                ClearQueuedPeerScreenShareFrames();
                CallPeerScreenSharePreview.Source = null;
                if (_screenShareWebRtcActive || ScreenShareWebView.Visibility == Visibility.Visible)
                {
                    PostScreenShareWebRtcMessage(new { type = "stop" });
                    _screenShareWebRtcActive = false;
                    SetScreenShareWebRtcVisible(false);
                }

                UpdateScreenShareStageVisibility();
                AppLog.Write($"Screen share stopped: from={packet.FromUserId}");
                break;
        }
    }

    private void QueuePeerScreenShareFrame(ChatPacket packet)
        => QueuePeerScreenShareFrame(packet.FromUserId, packet.Body);

    private void HandleRelayScreenFramePacket(RelayScreenFramePacket packet)
    {
        if (_activeCallContact?.UserId != packet.FromUserId ||
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

                if (_activeCallContact?.UserId != queued.FromUserId)
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
        if (_activeCallContact?.UserId != fromUserId)
        {
            return;
        }

        _peerScreenSharing = true;
        _isWatchingPeerScreen = true;
        if (_screenShareWebRtcActive || ScreenShareWebView.Visibility == Visibility.Visible)
        {
            PostScreenShareWebRtcMessage(new { type = "stop" });
            _screenShareWebRtcActive = false;
            SetScreenShareWebRtcVisible(false);
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
        var shouldShow = _isScreenSharing || _peerScreenSharing;
        if (!shouldShow && _isScreenShareFocusMode)
        {
            ExitScreenShareFocusMode();
        }

        CallScreenShareStage.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        CallSelfScreenTile.Visibility = _isScreenSharing ? Visibility.Visible : Visibility.Collapsed;
        CallPeerScreenTile.Visibility = _peerScreenSharing ? Visibility.Visible : Visibility.Collapsed;
        CallScreenShareJoinOverlay.Visibility = Visibility.Collapsed;
        CallScreenShareGrid.Columns = _isScreenSharing && _peerScreenSharing ? 2 : 1;
        CallScreenShareFullscreenControlButton.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;

        if (_isScreenShareFocusMode && shouldShow)
        {
            CallPanel.MaxHeight = double.PositiveInfinity;
            CallPanel.Height = double.NaN;
            CallContentPanel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            var availableWidth = GetAvailableScreenShareStageWidth(minWidth: 420);
            var reservedHeight = _isScreenShareFullscreenMode ? 0 : 112;
            var availableHeight = Math.Max(280, CallPanel.ActualHeight - reservedHeight);
            var aspect = _isScreenSharing && _peerScreenSharing ? 32d / 9d : 16d / 9d;
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
        CallPanel.Height = shouldShow ? 260 : 150;
        CallContentPanel.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        var compactWidth = _isScreenSharing && _peerScreenSharing ? 520 : 300;
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
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
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
        var contact = _contacts.FirstOrDefault(x => x.UserId == packet.FromUserId) ?? CreateContactFromPacket(packet);
        AddOrUpdateContact(contact);
        _ = _history.SaveContactAsync(contact);

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
        }
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

    private void ShowCallPanel(ContactViewModel contact, string status, bool showIncomingActions)
    {
        CallTitleText.Text = contact.DisplayName;
        CallStatusText.Text = status;
        ApplyCallAvatarVisuals(contact);
        AcceptCallButton.Visibility = showIncomingActions ? Visibility.Visible : Visibility.Collapsed;
        DeclineCallButton.Visibility = showIncomingActions ? Visibility.Visible : Visibility.Collapsed;
        JoinCallButton.Visibility = !showIncomingActions && !_selfInCall && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
        MicMuteButton.Visibility = !showIncomingActions && _selfInCall ? Visibility.Visible : Visibility.Collapsed;
        HeadphonesMuteButton.Visibility = !showIncomingActions && _selfInCall ? Visibility.Visible : Visibility.Collapsed;
        ScreenShareButton.Visibility = !showIncomingActions && _selfInCall && status == "Connected" ? Visibility.Visible : Visibility.Collapsed;
        EndCallButton.Visibility = showIncomingActions || (!_selfInCall && !_peerInCall) ? Visibility.Collapsed : Visibility.Visible;
        UpdateCallAudioControlVisuals(animate: false);
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

        CallPeerMicBadge.Visibility = _peerMicrophoneMuted && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
        CallPeerHeadphonesBadge.Visibility = _peerHeadphonesMuted && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
        CallPeerAudioBadges.Visibility = (_peerMicrophoneMuted || _peerHeadphonesMuted) && _peerInCall ? Visibility.Visible : Visibility.Collapsed;
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
        StopScreenShare(sendSignal: false);
        StopAudioCall();
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
        StopScreenShare(sendSignal: true);
        StopAudioCall();
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
            Interlocked.Exchange(ref _lastAudioPingTicks, 0);
            Interlocked.Exchange(ref _lastRelayAudioReceivedTicks, 0);
            Interlocked.Exchange(ref _udpAudioWarningShown, 0);
            _sendAudioGain = 1;
            _playbackAudioGain = 1;
            lock (_audioFrameGate)
            {
                _pendingAudioCaptureFrames.Clear();
            }
            ClearCallPlaybackQueue();

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
        AppLog.Write($"Call audio stopping: capturedFrames={captured}, droppedFrames={dropped}, sentFrames={sent}, relayReceivedFrames={relayReceived}, tcpReceivedFrames={tcpReceived}, receivedFrames={received}, legacyFrames={legacy}, failedPlaybackFrames={failedPlayback}, droppedPlaybackQueueFrames={droppedPlayback}, quietPlaybackFrames={quietPlayback}, quietCaptureFrames={quietCapture}, sendTimeouts={sendTimeouts}");

        _ = Task.Run(() =>
        {
            try
            {
                session.Dispose();
                AppLog.Write($"Call audio stopped: capturedFrames={captured}, droppedFrames={dropped}, sentFrames={sent}, relayReceivedFrames={relayReceived}, tcpReceivedFrames={tcpReceived}, receivedFrames={received}, legacyFrames={legacy}, failedPlaybackFrames={failedPlayback}, droppedPlaybackQueueFrames={droppedPlayback}, quietPlaybackFrames={quietPlayback}, quietCaptureFrames={quietCapture}, sendTimeouts={sendTimeouts}");
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
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(40));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                byte[]? pcm;
                lock (_audioFrameGate)
                {
                    pcm = _pendingAudioCaptureFrames.Count > 0
                        ? _pendingAudioCaptureFrames.Dequeue()
                        : null;
                }

                if (pcm is null)
                {
                    await SendCallAudioPingIfDueAsync(contact, cancellationToken);
                    continue;
                }

                await SendCallAudioFrameAsync(contact, pcm);
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

    private async Task SendCallAudioFrameAsync(ContactViewModel contact, byte[] pcm)
    {
        if (_profile is null ||
            _activeCallContact?.UserId != contact.UserId ||
            _activeCallState != "connected" ||
            _audioCall is null)
        {
            return;
        }

        if (_isMicrophoneMuted)
        {
            await SendCallAudioPingIfDueAsync(contact, CancellationToken.None);
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

            var capturePeak = GetPcmPeak(pcm);
            var sendPcm = SmoothAmplifyPcm(pcm, capturePeak, _sendAudioGain, out _sendAudioGain, out var gain);
            if (gain > 1)
            {
                var quiet = Interlocked.Increment(ref _quietCaptureFrames);
                if (quiet == 1 || quiet % 100 == 0)
                {
                    AppLog.Write($"Call audio capture boosted: to={contact.UserId}, quietFrames={quiet}, peak={capturePeak}, gain={gain:0.##}");
                }
            }

            var packet = RelayAudioPacket.Create(_profile.UserId, contact.UserId, Convert.ToBase64String(sendPcm));
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            sendTimeout.CancelAfter(CallAudioSendTimeout);
            await _relayClient.SendAudioAsync(packet, sendTimeout.Token);
            var sent = Interlocked.Increment(ref _sentAudioFrames);
            if (sent == 1 || sent % 100 == 0)
            {
                AppLog.Write($"Call audio sent over relay: to={contact.UserId}, frames={sent}, bytes={sendPcm.Length}, capturePeak={capturePeak}, sendPeak={GetPcmPeak(sendPcm)}, gain={gain:0.##}");
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
            await _relayClient.SendAudioAsync(RelayAudioPacket.Create(_profile.UserId, contact.UserId, ""), cancellationToken);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call audio UDP ping failed: to={contact.UserId}");
        }
    }

    private void HandleCallAudioPacket(ChatPacket packet)
    {
        if (_activeCallContact?.UserId != packet.FromUserId ||
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

        PlayCallAudio(packet.FromUserId, pcm);
    }

    private void HandleCallAudioPacket(RelayAudioPacket packet)
    {
        if (_activeCallContact?.UserId != packet.FromUserId ||
            _activeCallState != "connected" ||
            _audioCall is null ||
            string.IsNullOrWhiteSpace(packet.Body))
        {
            return;
        }

        if (!TryDecodeCallAudio(packet.Body, out var pcm))
        {
            AppLog.Write($"Invalid UDP call audio packet: from={packet.FromUserId}, bodyLength={packet.Body.Length}");
            return;
        }

        Interlocked.Exchange(ref _lastRelayAudioReceivedTicks, DateTimeOffset.UtcNow.Ticks);
        var relayReceived = Interlocked.Increment(ref _relayReceivedAudioFrames);
        if (relayReceived == 1 || relayReceived % 100 == 0)
        {
            AppLog.Write($"Call audio received over relay: from={packet.FromUserId}, frames={relayReceived}, bytes={pcm.Length}");
        }

        PlayCallAudio(packet.FromUserId, pcm);
    }

    private bool TryHandleLegacyCallAudioPacket(ChatPacket packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.Intent) ||
            _activeCallContact?.UserId != packet.FromUserId ||
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

        PlayCallAudio(packet.FromUserId, pcm);
        return true;
    }

    private void PlayCallAudio(string fromUserId, byte[] pcm)
    {
        if (_audioCall is null || _isHeadphonesMuted)
        {
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

        _callPlaybackQueue.Enqueue(new CallPlaybackFrame(fromUserId, pcm));
        _callPlaybackSignal.Release();
    }

    private async Task PlayCallAudioLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _callPlaybackSignal.WaitAsync(cancellationToken);
                if (!_callPlaybackQueue.TryDequeue(out var frame))
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

    private void PlayQueuedCallAudio(CallPlaybackFrame frame)
    {
        var session = _audioCall;
        if (session is null || _isHeadphonesMuted)
        {
            return;
        }

        var pcm = frame.Pcm;
        var peak = GetPcmPeak(pcm);
        var playbackPcm = SmoothAmplifyPcm(pcm, peak, _playbackAudioGain, out _playbackAudioGain, out var gain);
        if (gain > 1)
        {
            var quiet = Interlocked.Increment(ref _quietPlaybackFrames);
            if (quiet == 1 || quiet % 100 == 0)
            {
                AppLog.Write($"Call audio boosted: from={frame.FromUserId}, quietFrames={quiet}, peak={peak}, gain={gain:0.##}");
            }
        }

        if (!session.Play(playbackPcm, out var error))
        {
            var failed = Interlocked.Increment(ref _failedPlaybackFrames);
            if (failed == 1 || failed % 25 == 0)
            {
                AppLog.Write($"Call audio playback failed: from={frame.FromUserId}, failures={failed}, bytes={pcm.Length}, peak={peak}, gain={gain:0.##}, error={error}");
            }

            return;
        }

        var received = Interlocked.Increment(ref _receivedAudioFrames);
        if (received == 1 || received % 100 == 0)
        {
            AppLog.Write($"Call audio played: from={frame.FromUserId}, frames={received}, bytes={pcm.Length}, peak={peak}, gain={gain:0.##}, queued={_callPlaybackQueue.Count}");
        }
    }

    private void ClearCallPlaybackQueue()
    {
        while (_callPlaybackQueue.TryDequeue(out _))
        {
        }
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

    private static byte[] SmoothAmplifyPcm(
        byte[] pcm,
        int peak,
        double currentGain,
        out double nextGain,
        out double appliedGain)
    {
        if (peak <= CallAudioSilencePeak)
        {
            nextGain = currentGain + (1 - currentGain) * CallAudioGainRelease;
            appliedGain = 0;
            return new byte[pcm.Length];
        }

        var desiredGain = 1d;
        if (peak >= CallAudioVoiceFloorPeak && peak < CallAudioTargetPeak)
        {
            desiredGain = Math.Min(CallAudioMaxGain, (double)CallAudioTargetPeak / peak);
        }

        if (peak > 0 && peak * desiredGain > CallAudioOutputLimitPeak)
        {
            desiredGain = Math.Min(desiredGain, (double)CallAudioOutputLimitPeak / peak);
        }

        var smoothing = desiredGain > currentGain ? CallAudioGainAttack : CallAudioGainRelease;
        nextGain = currentGain + (desiredGain - currentGain) * smoothing;
        if (Math.Abs(nextGain - 1) < 0.03)
        {
            nextGain = 1;
        }

        appliedGain = nextGain;
        if (Math.Abs(appliedGain - 1) < 0.03)
        {
            return pcm;
        }

        var amplified = new byte[pcm.Length];
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            var scaled = (int)Math.Round(sample * appliedGain);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            amplified[i] = (byte)(scaled & 0xff);
            amplified[i + 1] = (byte)((scaled >> 8) & 0xff);
        }

        return amplified;
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

    private static bool TryDecodeCallAudio(string body, out byte[] pcm)
    {
        pcm = [];
        if (string.IsNullOrWhiteSpace(body))
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

    private sealed record CallAudioState(bool MicrophoneMuted, bool HeadphonesMuted);

    private sealed record ScreenShareStartPayload(string SourceTitle, int Resolution, int FrameRate, bool AudioMuted);

    private sealed record ScreenShareFramePayload(
        long Sequence,
        string JpegBase64,
        int Resolution,
        int FrameRate,
        bool AudioMuted,
        string Codec = ScreenShareCodecJpeg,
        bool KeyFrame = true);

    private sealed record CallPlaybackFrame(string FromUserId, byte[] Pcm);

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
    video { width: 100%; height: 100%; object-fit: contain; background: #111214; }
    .label { position: absolute; top: 8px; left: 8px; padding: 4px 8px; border-radius: 4px; background: rgba(0,0,0,.62); font-size: 11px; font-weight: 600; }
    #localTile.hidden, #remoteTile.hidden { display: none; }
    #status { position: absolute; left: 12px; bottom: 10px; padding: 5px 8px; border-radius: 4px; background: rgba(0,0,0,.62); font-size: 11px; color: #b5bac1; pointer-events: none; }
  </style>
</head>
<body>
  <div id="stage">
    <div id="localTile" class="tile hidden"><video id="localVideo" autoplay playsinline muted></video><div class="label">Your native WebRTC screen</div></div>
    <div id="remoteTile" class="tile hidden"><video id="remoteVideo" autoplay playsinline muted></video><div class="label">Friend WebRTC screen</div></div>
  </div>
  <div id="status">WebRTC ready</div>
  <script>
    const stage = document.getElementById('stage');
    const localTile = document.getElementById('localTile');
    const remoteTile = document.getElementById('remoteTile');
    const localVideo = document.getElementById('localVideo');
    const remoteVideo = document.getElementById('remoteVideo');
    const statusBox = document.getElementById('status');
    const defaultIceServers = [
      { urls: ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302'] }
    ];
    let pc = null;
    let localStream = null;
    let remoteStarted = false;
    let rtcConfig = { iceServers: defaultIceServers, bundlePolicy: 'max-bundle', iceTransportPolicy: 'all' };
    let connectTimeoutMs = 9000;
    let connectionWatchdog = null;
    let statsTimer = null;
    let previousStats = new Map();
    let remotePlaybackRetry = null;
    let nativeCanvas = null;
    let nativeContext = null;
    let nativeFrameWriter = null;
    let nativeCanvasTrack = null;
    let nativePendingFrame = null;
    let nativeFrameBusy = false;
    let encodedChannel = null;
    let encodedIsSender = false;
    let encodedMimeType = 'video/mp4; codecs="avc1.42E01E"';
    let encodedSendQueue = [];
    let encodedSendBusy = false;
    let encodedBufferLimit = 4194304;
    let encodedMediaSource = null;
    let encodedSourceBuffer = null;
    let encodedAppendQueue = [];

    function post(message) {
      if (window.chrome && chrome.webview) chrome.webview.postMessage(message);
    }
    function setStatus(value) {
      statusBox.textContent = value;
      post({ type: 'state', value });
    }
    stage.addEventListener('dblclick', event => {
      event.preventDefault();
      post({ type: 'focus-request' });
    });
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
      setStatus('WebRTC ICE configured');
    }
    function updateLayout() {
      const hasLocal = localStream && localStream.getTracks().some(t => t.readyState === 'live');
      localTile.classList.toggle('hidden', !hasLocal);
      remoteTile.classList.toggle('hidden', !remoteStarted);
      stage.classList.toggle('both', hasLocal && remoteStarted);
    }
    function clearRemotePlaybackRetry() {
      if (remotePlaybackRetry) clearTimeout(remotePlaybackRetry);
      remotePlaybackRetry = null;
    }
    function notifyRemotePlaying() {
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
      remoteVideo.muted = true;
      remoteVideo.autoplay = true;
      remoteVideo.playsInline = true;
      remoteVideo.srcObject = stream;
      playRemoteVideo();
      remoteStarted = true;
      updateLayout();
      post({ type: 'remote-started' });
      setStatus('WebRTC remote stream');
      armRemotePlaybackRetry(stream, 1);
    }
    remoteVideo.onloadedmetadata = () => {
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
      notifyRemotePlaying();
    };
    remoteVideo.onwaiting = () => post({ type: 'state', value: 'WebRTC remote video waiting' });
    remoteVideo.onstalled = () => post({ type: 'state', value: 'WebRTC remote video stalled' });
    function clearConnectionWatchdog() {
      if (connectionWatchdog) clearTimeout(connectionWatchdog);
      connectionWatchdog = null;
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
    function resetEncodedStream() {
      encodedSendQueue = [];
      encodedSendBusy = false;
      encodedAppendQueue = [];
      encodedSourceBuffer = null;
      if (encodedChannel) {
        try { encodedChannel.close(); } catch {}
      }
      encodedChannel = null;
      encodedIsSender = false;
      if (encodedMediaSource && encodedMediaSource.readyState === 'open') {
        try { encodedMediaSource.endOfStream(); } catch {}
      }
      encodedMediaSource = null;
    }
    function setupEncodedChannel(channel, isSender, options) {
      encodedChannel = channel;
      encodedIsSender = isSender;
      encodedMimeType = options.mimeType || encodedMimeType;
      encodedBufferLimit = Number(options.dataChannelBufferLimit || encodedBufferLimit);
      encodedChannel.binaryType = 'arraybuffer';
      encodedChannel.bufferedAmountLowThreshold = Math.floor(encodedBufferLimit / 2);
      encodedChannel.onopen = () => {
        if (encodedIsSender) {
          encodedChannel.send(JSON.stringify({ type: 'encoded-config', mimeType: encodedMimeType }));
          post({ type: 'encoded-channel-open' });
        }
        flushEncodedSendQueue();
      };
      encodedChannel.onclose = () => post({ type: 'encoded-channel-closed' });
      encodedChannel.onerror = error => post({ type: 'error', message: 'Encoded WebRTC data channel error: ' + String(error && error.message ? error.message : error) });
      encodedChannel.onbufferedamountlow = flushEncodedSendQueue;
      encodedChannel.onmessage = event => {
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
      encodedMediaSource = new MediaSource();
      remoteVideo.muted = true;
      remoteVideo.autoplay = true;
      remoteVideo.playsInline = true;
      remoteVideo.srcObject = null;
      remoteVideo.src = URL.createObjectURL(encodedMediaSource);
      remoteStarted = true;
      updateLayout();
      post({ type: 'remote-started' });
      setStatus('Encoded WebRTC remote stream');
      encodedMediaSource.addEventListener('sourceopen', () => {
        try {
          if (!MediaSource.isTypeSupported(encodedMimeType)) {
            throw new Error('Unsupported encoded stream: ' + encodedMimeType);
          }
          encodedSourceBuffer = encodedMediaSource.addSourceBuffer(encodedMimeType);
          encodedSourceBuffer.mode = 'segments';
          encodedSourceBuffer.addEventListener('updateend', flushEncodedAppendQueue);
          flushEncodedAppendQueue();
          playRemoteVideo();
        } catch (error) {
          post({ type: 'error', message: String(error && error.message ? error.message : error) });
        }
      }, { once: true });
    }
    function appendEncodedChunk(data) {
      if (!data) return;
      startEncodedReceiver(encodedMimeType);
      const bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
      encodedAppendQueue.push(bytes);
      flushEncodedAppendQueue();
    }
    function flushEncodedAppendQueue() {
      if (!encodedSourceBuffer || encodedSourceBuffer.updating || encodedAppendQueue.length === 0) return;
      try {
        encodedSourceBuffer.appendBuffer(encodedAppendQueue.shift());
      } catch (error) {
        post({ type: 'state', value: 'Encoded WebRTC append skipped: ' + String(error && error.message ? error.message : error) });
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
      if (encodedSendBusy || !encodedChannel || encodedChannel.readyState !== 'open') return;
      encodedSendBusy = true;
      try {
        while (encodedSendQueue.length > 0 && encodedChannel.bufferedAmount < encodedBufferLimit) {
          encodedChannel.send(encodedSendQueue.shift());
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
    async function startNativeShare(options) {
      try {
        if (options.iceServers) configure(options);
        await ensurePeerConnection();
        stopLocalTracks(false);
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
        const offer = await pc.createOffer({ offerToReceiveVideo: true, offerToReceiveAudio: false });
        await pc.setLocalDescription(offer);
        post({ type: 'offer', sdp: pc.localDescription.sdp });
        armConnectionWatchdog();
        setStatus('Native WebRTC offer sent');
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
        resetEncodedStream();
        setupEncodedChannel(pc.createDataChannel('screen-h264', { ordered: true }), true, options);
        updateLayout();
        post({ type: 'encoded-local-started' });
        const offer = await pc.createOffer({ offerToReceiveVideo: false, offerToReceiveAudio: false });
        await pc.setLocalDescription(offer);
        post({ type: 'offer', sdp: pc.localDescription.sdp });
        armConnectionWatchdog();
        setStatus('Encoded WebRTC offer sent');
      } catch (error) {
        resetEncodedStream();
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
        setStatus('Encoded WebRTC error');
      }
    }
    async function startShare(options) {
      try {
        if (options.iceServers) configure(options);
        await ensurePeerConnection();
        stopLocalTracks(false);
        const frameRate = Number(options.frameRate || 60);
        const width = Number(options.width || 2560);
        const height = Number(options.height || 1440);
        localStream = await navigator.mediaDevices.getDisplayMedia({
          video: {
            width: { ideal: width },
            height: { ideal: height },
            frameRate: { ideal: frameRate, max: frameRate }
          },
          audio: false
        });
        localVideo.srcObject = localStream;
        localVideo.play().catch(() => {});
        for (const track of localStream.getVideoTracks()) {
          track.contentHint = options.contentHint || 'detail';
          const sender = pc.addTrack(track, localStream);
          await applySenderParameters(sender, options);
          track.onended = () => post({ type: 'local-ended' });
        }
        updateLayout();
        post({ type: 'local-started' });
        const offer = await pc.createOffer({ offerToReceiveVideo: true, offerToReceiveAudio: false });
        await pc.setLocalDescription(offer);
        post({ type: 'offer', sdp: pc.localDescription.sdp });
        armConnectionWatchdog();
        setStatus('WebRTC offer sent');
      } catch (error) {
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
        setStatus('WebRTC error');
      }
    }
    async function handleOffer(signal) {
      try {
        await ensurePeerConnection();
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
        post({ type: 'error', message: String(error && error.message ? error.message : error) });
      }
    }
    function stopLocalTracks(notify) {
      if (localStream) {
        for (const track of localStream.getTracks()) track.stop();
      }
      localStream = null;
      localVideo.srcObject = null;
      resetNativeFrameSource();
      resetEncodedStream();
      updateLayout();
      if (notify) post({ type: 'local-ended' });
    }
    function stopAll() {
      clearConnectionWatchdog();
      clearRemotePlaybackRetry();
      stopStatsTimer();
      stopLocalTracks(false);
      if (pc) {
        pc.close();
        pc = null;
      }
      remoteVideo.srcObject = null;
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
        else if (message.type === 'start-native-share') startNativeShare(message);
        else if (message.type === 'native-frame') handleNativeFrame(message);
        else if (message.type === 'start-share') startShare(message);
        else if (message.type === 'stop') stopAll();
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
        var existing = _contacts.FirstOrDefault(x => x.UserId == contact.UserId);
        if (existing is null)
        {
            _contacts.Add(contact);
            EmptyContactsHint.Visibility = Visibility.Collapsed;
            return;
        }

        existing.DisplayName = contact.DisplayName;
        existing.IpAddress = contact.IpAddress;
        existing.MessagePort = contact.MessagePort;
        existing.Status = contact.Status;
        existing.LastSeenUtc = contact.LastSeenUtc == default ? DateTimeOffset.UtcNow : contact.LastSeenUtc;
        existing.IsGroup = contact.IsGroup;
        existing.GroupMemberIds = contact.GroupMemberIds;
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
