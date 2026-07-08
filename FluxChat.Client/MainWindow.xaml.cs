using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluxChat.Shared;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace FluxChat.Client;

public partial class MainWindow : Window
{
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);
    private const string RelayContactPrefix = "VPS ";
    private const string FriendRequestIntent = "friend-request";
    private const string FriendAcceptIntent = "friend-accept";
    private const string ProfileUpdateIntent = "profile-update";
    private const string ProfileRequestIntent = "profile-request";
    private const string CallInviteIntent = "call-invite";
    private const string CallAcceptIntent = "call-accept";
    private const string CallDeclineIntent = "call-decline";
    private const string CallEndIntent = "call-end";
    private const string CallAudioIntent = "call-audio";
    private const string LegacyFriendRequestBody = "Friend request";
    private const string LegacyFriendAcceptBody = "Friend request accepted";
    private const string ControlBodyPrefix = "fluxchat-control:";
    private const int MaxAvatarSyncBytes = 5_000_000;
    private const double AvatarEditorPreviewSize = 350;
    private const double AvatarEditorCircleSize = 344;
    private const double ProfileAvatarSize = 44;
    private const double SettingsAvatarSize = 64;

    private readonly ObservableCollection<ContactViewModel> _contacts = [];
    private readonly ObservableCollection<MessageViewModel> _messages = [];
    private readonly ObservableCollection<FriendRequestViewModel> _friendRequests = [];
    private readonly Dictionary<string, DateTimeOffset> _profileRequestAttempts = [];
    private readonly HistoryStore _history = new();
    private readonly CancellationTokenSource _stop = new();
    private AppSettings _settings = new();
    private UserProfile? _profile;
    private RelayClient? _relayClient;
    private Forms.NotifyIcon? _notifyIcon;
    private ContactViewModel? _selectedContact;
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
    private AudioCallSession? _audioCall;
    private int _pendingAudioFrame;
    private string? _notificationContactUserId;

    public MainWindow()
    {
        InitializeComponent();
        _avatarVideoLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _avatarVideoLoopTimer.Tick += AvatarVideoLoopTimer_OnTick;
        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _presenceTimer.Tick += async (_, _) => await PublishPresenceAsync();
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
        Loaded += OnLoaded;
        Closed += OnClosed;
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
        _relayClient.PresenceReceived += OnRelayPresenceReceived;
        _relayClient.StatusChanged += OnNetworkStatusChanged;
        await ConnectRelayAsync();
        _presenceTimer.Start();

        NetworkStatusText.Text = "VPS mode ready. Add a contact by UserId.";
        _ = CheckForUpdatesAsync();
        if (isFirstRun)
        {
            FirstRunVpsOverlay.Visibility = Visibility.Visible;
            FirstRunServerAddressInput.Focus();
            NetworkStatusText.Text = "Connect your VPS server to start.";
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _presenceTimer.Stop();
        _callRingtoneTimer.Stop();
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

            if (packet.Intent is CallInviteIntent or CallAcceptIntent or CallDeclineIntent or CallEndIntent)
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
        _selectedContact = contact;
        ContactsList.SelectedItem = contact;
        AddFriendPanel.Visibility = Visibility.Collapsed;
        ChatTitle.Text = contact.DisplayName;
        ChatSubtitle.Text = $"{contact.IpAddress} | {contact.ShortId}";
        ComposerPanel.Visibility = Visibility.Visible;
        EmptyChatHint.Visibility = Visibility.Collapsed;
        StartCallButton.Visibility = Visibility.Visible;

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
        StopAudioCall();
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
        StopAudioCall();
        await SendCallSignalAsync(contact, CallEndIntent);
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
        ProfileFlyout.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsServerAddressInput.Focus();
    }

    private void SettingsCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
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

        foreach (var contact in _contacts.ToArray())
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
        if (_profile is null)
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
        if (!force && status == _lastPublishedStatus)
        {
            return;
        }

        _lastPublishedStatus = status;
        try
        {
            var avatarPayload = CreateAvatarPayload();
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

        foreach (var contact in _contacts.ToArray())
        {
            try
            {
                var packet = CreatePresencePacket(contact);
                await SendOverRelayAsync(packet, contact);
            }
            catch (Exception ex) when (!_stop.IsCancellationRequested)
            {
                AppLog.Write(ex, $"Contact presence failed: to={contact.UserId}");
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

    private async Task SendOverRelayAsync(ChatPacket packet, ContactViewModel contact, bool log = true)
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

        await _relayClient.SendAsync(packet, CancellationToken.None, log);
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
                _ = OpenContactAsync(contact);
                RestoreWindowForIncomingCall();
                ShowCallPanel(contact, "Incoming call", showIncomingActions: true);
                StartCallRingtone();
                ShowIncomingCallNotification(contact);
                break;
            case CallAcceptIntent:
                StopCallRingtone();
                _activeCallContact = contact;
                _activeCallState = "connected";
                ShowCallPanel(contact, "Connected", showIncomingActions: false);
                StartAudioCall(contact);
                NetworkStatusText.Text = $"{contact.DisplayName} accepted the call";
                break;
            case CallDeclineIntent:
                StopCallRingtone();
                StopAudioCall();
                HideCallPanel();
                NetworkStatusText.Text = $"{contact.DisplayName} declined the call";
                break;
            case CallEndIntent:
                StopCallRingtone();
                StopAudioCall();
                HideCallPanel();
                NetworkStatusText.Text = $"{contact.DisplayName} ended the call";
                break;
        }
    }

    private void ShowCallPanel(ContactViewModel contact, string status, bool showIncomingActions)
    {
        CallTitleText.Text = contact.DisplayName;
        CallStatusText.Text = status;
        CallPeerInitials.Text = contact.Initials;
        CallSelfInitials.Text = _profile is null ? "ME" : GetInitials(_profile.DisplayName);
        AcceptCallButton.Visibility = showIncomingActions ? Visibility.Visible : Visibility.Collapsed;
        DeclineCallButton.Visibility = showIncomingActions ? Visibility.Visible : Visibility.Collapsed;
        EndCallButton.Visibility = showIncomingActions ? Visibility.Collapsed : Visibility.Visible;
        CallPanel.Visibility = Visibility.Visible;
        CallPanelSplitter.Visibility = Visibility.Visible;
    }

    private void HideCallPanel()
    {
        StopCallRingtone();
        StopAudioCall();
        _activeCallContact = null;
        _activeCallState = "";
        CallPanel.Visibility = Visibility.Collapsed;
        CallPanelSplitter.Visibility = Visibility.Collapsed;
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
        StopAudioCall();
        try
        {
            var session = new AudioCallSession();
            session.AudioCaptured += bytes => _ = SendCallAudioFrameAsync(contact, bytes);
            session.Start();
            _audioCall = session;
            AppLog.Write($"Call audio started: peer={contact.UserId}");
        }
        catch (Exception ex)
        {
            StopAudioCall();
            AppLog.Write(ex, $"Call audio failed: peer={contact.UserId}");
            NetworkStatusText.Text = $"Call connected, but audio failed: {ex.Message}";
        }
    }

    private void StopAudioCall()
    {
        if (_audioCall is null)
        {
            return;
        }

        try
        {
            _audioCall.Dispose();
            AppLog.Write("Call audio stopped");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Call audio stop failed");
        }
        finally
        {
            _audioCall = null;
            Interlocked.Exchange(ref _pendingAudioFrame, 0);
        }
    }

    private async Task SendCallAudioFrameAsync(ContactViewModel contact, byte[] pcm)
    {
        if (_profile is null ||
            _activeCallContact?.UserId != contact.UserId ||
            _activeCallState != "connected" ||
            Interlocked.Exchange(ref _pendingAudioFrame, 1) == 1)
        {
            return;
        }

        try
        {
            if (_relayClient is null || !_relayClient.IsConnected)
            {
                return;
            }

            var packet = CreateCallPacket(contact, Convert.ToBase64String(pcm), CallAudioIntent);
            await _relayClient.SendAsync(packet, CancellationToken.None, log: false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            AppLog.Write(ex, $"Call audio send failed: to={contact.UserId}");
        }
        finally
        {
            Interlocked.Exchange(ref _pendingAudioFrame, 0);
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

        try
        {
            _audioCall.Play(Convert.FromBase64String(packet.Body));
        }
        catch (FormatException ex)
        {
            AppLog.Write(ex, $"Invalid call audio packet: from={packet.FromUserId}");
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

    private ChatPacket CreatePresencePacket(ContactViewModel contact)
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

        var avatarPayload = CreateAvatarPayload();

        return ChatPacket.Create(
            _profile.UserId,
            _profile.DisplayName,
            contact.UserId,
            "",
            fromRelayServer: currentRelayServer,
            toRelayServer: toRelayServer,
            intent: "presence",
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

        var path = Path.Combine(AppPaths.AvatarDirectory, $"contact-{userId}-{Guid.NewGuid():N}{safeExtension.ToLowerInvariant()}");
        File.WriteAllBytes(path, Convert.FromBase64String(mediaBase64));
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
                NetworkStatusText.Text = $"Update available: {update.Version}";
                var result = MessageBox.Show(
                    $"FluxChat {update.Version} is available.\n\nInstall it now?",
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

    private void AvatarVideo_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement element)
        {
            return;
        }

        element.Position = TimeSpan.Zero;
        element.Play();
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
