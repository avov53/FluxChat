using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FluxChat.Shared;

namespace FluxChat.Client;

public partial class AccountWindow : Window
{
    private UserProfile _profile;
    private readonly AppSettings _settings;
    private FrameworkElement? _currentPage;
    private readonly Stack<FrameworkElement> _pageHistory = new();
    private RecoveryMode _recoveryMode;
    private string _pendingRegistrationLogin = "";
    private string _pendingRegistrationPassword = "";
    private string _registrationBootstrapToken = "";
    private string _registrationAccountApiUrl = "";
    private LocalAccountVault _accountVault = LocalAccountVault.Load();
    private bool _isApplyingSavedAccount;
    private bool _isSyncingPasswordText;
    private bool _isLoginPasswordVisible;
    private bool _isNewPasswordVisible;
    private bool _isConfirmPasswordVisible;

    internal UserProfile SelectedProfile => _profile;

    internal AccountWindow(UserProfile profile, AppSettings settings)
    {
        _profile = profile;
        _settings = settings;
        InitializeComponent();

        SignInApiUrlInput.Text = settings.RelayServer;
        RegisterApiUrlInput.Text = settings.RelayServer;
        RegisterInviteInput.Text = settings.RelayAccessKey;
        LoginInput.Text = settings.AccountLogin;
        NewLoginInput.Text = settings.AccountLogin;
        TryFillSavedPassword(LoginInput.Text);
        ApplyLocalization();
        _currentPage = WelcomePage;
        AnimateShell();
    }

    private static string L(string key) => AppLanguage.Text(key);

    private void ApplyLocalization()
    {
        AccountPrivateText.Text = L("account.private");
        WelcomeTitleText.Text = L("account.welcome.title");
        WelcomeSubtitleText.Text = L("account.welcome.subtitle");
        WelcomeSignInButton.Content = L("account.welcome.signIn");
        WelcomeCreateAccountButton.Content = L("account.welcome.create");
        WelcomePrivacyText.Text = L("account.welcome.privacy");
        SignInBackButton.ToolTip = L("account.back");
        RegisterBackButton.ToolTip = L("account.back");
        SignInTitleText.Text = L("account.signin.title");
        SignInSubtitleText.Text = L("account.signin.subtitle");
        SignInLoginLabelText.Text = L("account.login");
        SignInPasswordLabelText.Text = L("account.password");
        SignInServerLabelText.Text = L("account.vpsServer");
        SignInButton.Content = L("account.welcome.signIn");
        RegisterTitleText.Text = L("account.create.title");
        RegisterSubtitleText.Text = L("account.create.subtitle");
        RegisterLoginLabelText.Text = L("account.login");
        RegisterPasswordLabelText.Text = L("account.passwordLong");
        RegisterRepeatPasswordLabelText.Text = L("account.repeatPassword");
        RegisterServerLabelText.Text = L("account.vpsServer");
        RegisterInviteLabelText.Text = L("account.inviteCode");
        RegisterButton.Content = L("account.welcome.create");
        BusyText.Text = L("account.wait");
        PasswordRevealButton.ToolTip = L(_isLoginPasswordVisible ? "account.hidePassword" : "account.showPassword");
        NewPasswordRevealButton.ToolTip = L(_isNewPasswordVisible ? "account.hidePassword" : "account.showPassword");
        ConfirmPasswordRevealButton.ToolTip = L(_isConfirmPasswordVisible ? "account.hidePassword" : "account.showPassword");
    }

    private void WelcomeSignInButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateTo(SignInPage);
        LoginInput.Focus();
    }

    private void WelcomeCreateAccountButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateTo(RegisterPage);
        NewLoginInput.Focus();
    }

    private void BackToWelcomeButton_OnClick(object sender, RoutedEventArgs e)
        => NavigateBack(WelcomePage);

    private void BackToSignInButton_OnClick(object sender, RoutedEventArgs e)
        => NavigateBack(SignInPage);

    private void CodeSignInButton_OnClick(object sender, RoutedEventArgs e)
    {
        ConfigureRecovery(RecoveryMode.SignIn);
        NavigateTo(RecoveryPage);
        RecoveryLoginInput.Focus();
    }

    private void ForgotPasswordButton_OnClick(object sender, RoutedEventArgs e)
    {
        ConfigureRecovery(RecoveryMode.ResetPassword);
        NavigateTo(RecoveryPage);
        RecoveryLoginInput.Focus();
    }

    private AccountClient CreateClient(string relayAddress, string? discoveredAccountApiUrl = null)
    {
        var normalizedRelayAddress = NormalizeRelayAddress(relayAddress);
        _settings.RelayServer = normalizedRelayAddress;
        _settings.AccountApiUrl = ResolveAccountApiUrl(normalizedRelayAddress, discoveredAccountApiUrl);
        SignInApiUrlInput.Text = normalizedRelayAddress;
        RegisterApiUrlInput.Text = normalizedRelayAddress;
        return new AccountClient(_settings.AccountApiUrl);
    }

    private string ResolveAccountApiUrl(string normalizedRelayAddress, string? discoveredAccountApiUrl)
    {
        if (!string.IsNullOrWhiteSpace(discoveredAccountApiUrl))
        {
            return discoveredAccountApiUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.AccountApiUrl))
        {
            return _settings.AccountApiUrl.Trim();
        }

        return AccountEndpointResolver.FromRelayAddress(normalizedRelayAddress);
    }

    private async Task<string?> TryDiscoverAccountApiUrlFromRelayAsync(string normalizedRelayAddress, CancellationToken cancellationToken)
    {
        await using var relay = new RelayClient(_profile);
        var credential = !string.IsNullOrWhiteSpace(_settings.RelayClientToken)
            ? _settings.RelayClientToken
            : !string.IsNullOrWhiteSpace(_settings.RelayAccessKey)
                ? _settings.RelayAccessKey
                : "account-discovery";
        try
        {
            await relay.ConnectAsync(normalizedRelayAddress, credential, cancellationToken);
            return relay.AccountApiUrl;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            AppLog.Write(ex, "Account API discovery through relay failed");
            return relay.AccountApiUrl;
        }
    }

    private async void SignInButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var relayAddress = NormalizeRelayAddress(SignInApiUrlInput.Text);
            await TrySwitchToSavedProfileAsync(LoginInput.Text, relayAddress);
            var discoveredAccountApiUrl = await TryDiscoverAccountApiUrlFromRelayAsync(relayAddress, CancellationToken.None);
            var result = await CreateClient(relayAddress, discoveredAccountApiUrl)
                .LoginAsync(LoginInput.Text, PasswordInput.Password, CancellationToken.None);
            await ApplyLoginAsync(result, LoginInput.Text, PasswordInput.Password, relayAddress);
        });
    }

    private async void RegisterButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (NewPasswordInput.Password != ConfirmPasswordInput.Password)
        {
            ShowStatus("Passwords do not match.", true);
            return;
        }

        await RunAsync(async () =>
        {
            var invite = RegisterInviteInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(invite))
            {
                ShowStatus("Enter the VPS invite code.", true);
                return;
            }

            var relayAddress = NormalizeRelayAddress(RegisterApiUrlInput.Text);
            if (_accountVault.Find(NewLoginInput.Text, relayAddress) is not null)
            {
                ShowStatus("This account is already saved on this device. Use Sign in instead.", true);
                return;
            }

            if (!_accountVault.CanAddLogin(NewLoginInput.Text, relayAddress))
            {
                ShowStatus($"This device already has {LocalAccountVault.MaxAccountsPerDevice} saved FluxChat accounts. Remove one before creating another account.", true);
                return;
            }

            await UseFreshRegistrationProfileAsync(NewLoginInput.Text);

            if (string.IsNullOrWhiteSpace(_registrationBootstrapToken))
            {
                await using var relay = new RelayClient(_profile);
                var bootstrapToken = await relay.ConnectAsync(relayAddress, invite, CancellationToken.None);
                _registrationBootstrapToken = string.IsNullOrWhiteSpace(bootstrapToken) ? invite : bootstrapToken;
                _registrationAccountApiUrl = relay.AccountApiUrl ?? "";
            }

            var client = CreateClient(relayAddress, _registrationAccountApiUrl);
            var result = await client.RegisterAsync(
                _profile,
                NewLoginInput.Text,
                NewPasswordInput.Password,
                _registrationBootstrapToken,
                CancellationToken.None);
            if (!result.Accepted)
            {
                if (result.Message.Contains("profile is already linked", StringComparison.OrdinalIgnoreCase))
                {
                    await UseFreshRegistrationProfileAsync(NewLoginInput.Text);
                    _registrationBootstrapToken = "";
                    _registrationAccountApiUrl = "";
                    await using var retryRelay = new RelayClient(_profile);
                    var retryBootstrapToken = await retryRelay.ConnectAsync(relayAddress, invite, CancellationToken.None);
                    _registrationBootstrapToken = string.IsNullOrWhiteSpace(retryBootstrapToken) ? invite : retryBootstrapToken;
                    _registrationAccountApiUrl = retryRelay.AccountApiUrl ?? "";
                    client = CreateClient(relayAddress, _registrationAccountApiUrl);
                    result = await client.RegisterAsync(
                        _profile,
                        NewLoginInput.Text,
                        NewPasswordInput.Password,
                        _registrationBootstrapToken,
                        CancellationToken.None);
                    if (result.Accepted)
                    {
                        goto Registered;
                    }
                }

                ShowStatus(result.Message, true);
                return;
            }

Registered:
            _pendingRegistrationLogin = NewLoginInput.Text.Trim();
            _pendingRegistrationPassword = NewPasswordInput.Password;
            _settings.AccountLogin = _pendingRegistrationLogin;
            var login = await client.LoginAsync(
                _pendingRegistrationLogin,
                _pendingRegistrationPassword,
                CancellationToken.None);
            await ApplyLoginAsync(login, _pendingRegistrationLogin, _pendingRegistrationPassword, relayAddress);
        });
    }

    private async void VerifyEmailButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var client = CreateClient(RegisterApiUrlInput.Text);
            var result = await client.VerifyEmailAsync(
                _pendingRegistrationLogin,
                VerificationCodeInput.Text,
                CancellationToken.None);
            if (!result.Accepted)
            {
                ShowStatus(result.Message, true);
                return;
            }

            var login = await client.LoginAsync(
                _pendingRegistrationLogin,
                _pendingRegistrationPassword,
                CancellationToken.None);
            await ApplyLoginAsync(login, _pendingRegistrationLogin, _pendingRegistrationPassword, RegisterApiUrlInput.Text);
        });
    }

    private async void ResendVerificationButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var result = await CreateClient(RegisterApiUrlInput.Text)
                .RequestCodeAsync(_pendingRegistrationLogin, "verify-email", CancellationToken.None);
            ShowStatus(result.Message);
        });
    }

    private async void SendRecoveryCodeButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var purpose = _recoveryMode == RecoveryMode.SignIn ? "login" : "reset";
            var result = await CreateClient(SignInApiUrlInput.Text)
                .RequestCodeAsync(RecoveryLoginInput.Text, purpose, CancellationToken.None);
            ShowStatus(result.Message);
            RecoveryCodeInput.Focus();
        });
    }

    private async void RecoveryActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_recoveryMode == RecoveryMode.ResetPassword &&
            ResetPasswordInput.Password != ResetPasswordConfirmInput.Password)
        {
            ShowStatus("Passwords do not match.", true);
            return;
        }

        await RunAsync(async () =>
        {
            var client = CreateClient(SignInApiUrlInput.Text);
            if (_recoveryMode == RecoveryMode.SignIn)
            {
                var login = await client.LoginByCodeAsync(
                    RecoveryLoginInput.Text,
                    RecoveryCodeInput.Text,
                    CancellationToken.None);
                await ApplyLoginAsync(login, RecoveryLoginInput.Text, "", SignInApiUrlInput.Text);
                return;
            }

            var result = await client.ResetPasswordAsync(
                RecoveryLoginInput.Text,
                RecoveryCodeInput.Text,
                ResetPasswordInput.Password,
                CancellationToken.None);
            if (!result.Accepted)
            {
                ShowStatus(result.Message, true);
                return;
            }

            LoginInput.Text = RecoveryLoginInput.Text;
            PasswordInput.Password = "";
            NavigateTo(SignInPage, rememberCurrent: false);
            ShowStatus("Password changed. Sign in with your new password.");
            PasswordInput.Focus();
        });
    }

    private void ConfigureRecovery(RecoveryMode mode)
    {
        _recoveryMode = mode;
        RecoveryLoginInput.Text = LoginInput.Text;
        RecoveryCodeInput.Text = "";
        ResetPasswordInput.Password = "";
        ResetPasswordConfirmInput.Password = "";
        var isReset = mode == RecoveryMode.ResetPassword;
        RecoveryTitle.Text = isReset ? "Reset password" : "Sign in with a code";
        RecoverySubtitle.Text = isReset
            ? "We will send a password-reset code to your email."
            : "We will send an eight-digit sign-in code to your email.";
        ResetPasswordFields.Visibility = isReset ? Visibility.Visible : Visibility.Collapsed;
        RecoveryActionButton.Content = isReset ? "Change password" : "Sign in";
    }

    private async Task ApplyLoginAsync(AccountSessionResponse result, string fallbackLogin, string password, string relayAddress)
    {
        if (!result.Accepted || string.IsNullOrWhiteSpace(result.RelayToken))
        {
            ShowStatus(result.Message, true);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.UserId) ||
            !string.Equals(result.UserId, _profile.UserId, StringComparison.Ordinal))
        {
            var savedProfile = string.IsNullOrWhiteSpace(result.UserId)
                ? null
                : await UserProfileStore.TryLoadProfileAsync(result.UserId);
            if (savedProfile is null)
            {
                ShowStatus(
                    "This account belongs to another encrypted identity. Sign in on the device where it was created.",
                    true);
                return;
            }

            _profile = savedProfile;
        }

        _settings.AccountLogin = result.Login ?? fallbackLogin.Trim();
        _settings.AccountSessionToken = result.RelayToken;
        _settings.RelayClientToken = result.RelayToken;
        if (!string.IsNullOrWhiteSpace(password))
        {
            await _accountVault.RememberAsync(_settings.AccountLogin, password, NormalizeRelayAddress(relayAddress), _profile);
        }

        await UserProfileStore.ActivateAsync(_profile);
        await AppSettingsStore.SaveAsync(_settings);
        DialogResult = true;
        Close();
    }

    private async Task TrySwitchToSavedProfileAsync(string login, string relayAddress)
    {
        var entry = _accountVault.Find(login, relayAddress);
        if (entry is null || string.IsNullOrWhiteSpace(entry.UserId))
        {
            return;
        }

        var profile = await UserProfileStore.TryLoadProfileAsync(entry.UserId);
        if (profile is not null)
        {
            _profile = profile;
        }
    }

    private async Task UseFreshRegistrationProfileAsync(string login)
    {
        _profile = await UserProfileStore.CreateNewAsync(string.IsNullOrWhiteSpace(login) ? Environment.UserName : login.Trim());
        _registrationBootstrapToken = "";
        _registrationAccountApiUrl = "";
    }

    private void LoginInput_OnGotKeyboardFocus(object sender, RoutedEventArgs e)
        => ShowSavedAccounts();

    private void LoginInput_OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShowSavedAccounts();

    private void LoginInput_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingSavedAccount)
        {
            return;
        }

        ShowSavedAccounts();
        TryFillSavedPassword(LoginInput.Text);
    }

    private async void LoginInput_OnLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        await Task.Delay(120);
        if (!SavedAccountsPopup.IsKeyboardFocusWithin && !LoginInput.IsKeyboardFocusWithin)
        {
            SavedAccountsPopup.IsOpen = false;
        }
    }

    private void SavedAccountsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplySelectedSavedAccount();

    private void SavedAccountsList_OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ApplySelectedSavedAccount();

    private void PasswordInput_OnGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        => TryFillSavedPassword(LoginInput.Text);

    private void PasswordField_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingPasswordText)
        {
            return;
        }

        _isSyncingPasswordText = true;
        try
        {
            if (sender == PasswordInput)
            {
                PasswordRevealInput.Text = PasswordInput.Password;
            }
            else if (sender == NewPasswordInput)
            {
                NewPasswordRevealInput.Text = NewPasswordInput.Password;
            }
            else if (sender == ConfirmPasswordInput)
            {
                ConfirmPasswordRevealInput.Text = ConfirmPasswordInput.Password;
            }
        }
        finally
        {
            _isSyncingPasswordText = false;
        }
    }

    private void PasswordRevealInput_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingPasswordText)
        {
            return;
        }

        _isSyncingPasswordText = true;
        try
        {
            if (sender == PasswordRevealInput)
            {
                PasswordInput.Password = PasswordRevealInput.Text;
            }
            else if (sender == NewPasswordRevealInput)
            {
                NewPasswordInput.Password = NewPasswordRevealInput.Text;
            }
            else if (sender == ConfirmPasswordRevealInput)
            {
                ConfirmPasswordInput.Password = ConfirmPasswordRevealInput.Text;
            }
        }
        finally
        {
            _isSyncingPasswordText = false;
        }
    }

    private void PasswordRevealButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender == PasswordRevealButton)
        {
            _isLoginPasswordVisible = !_isLoginPasswordVisible;
            SetPasswordRevealState(PasswordRevealInput, PasswordRevealButton, PasswordRevealSlash, _isLoginPasswordVisible);
        }
        else if (sender == NewPasswordRevealButton)
        {
            _isNewPasswordVisible = !_isNewPasswordVisible;
            SetPasswordRevealState(NewPasswordRevealInput, NewPasswordRevealButton, NewPasswordRevealSlash, _isNewPasswordVisible);
        }
        else if (sender == ConfirmPasswordRevealButton)
        {
            _isConfirmPasswordVisible = !_isConfirmPasswordVisible;
            SetPasswordRevealState(ConfirmPasswordRevealInput, ConfirmPasswordRevealButton, ConfirmPasswordRevealSlash, _isConfirmPasswordVisible);
        }
    }

    private void SetPasswordRevealState(System.Windows.Controls.TextBox revealInput, System.Windows.Controls.Button button, FrameworkElement slash, bool isVisible)
    {
        button.ToolTip = L(isVisible ? "account.hidePassword" : "account.showPassword");
        revealInput.Visibility = Visibility.Visible;
        revealInput.IsHitTestVisible = isVisible;
        revealInput.Focusable = isVisible;
        var revealAnimation = new DoubleAnimation(isVisible ? 1 : 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        if (!isVisible)
        {
            revealAnimation.Completed += (_, _) => revealInput.Visibility = Visibility.Collapsed;
        }

        revealInput.BeginAnimation(OpacityProperty, revealAnimation);
        slash.BeginAnimation(OpacityProperty, new DoubleAnimation(isVisible ? 0 : 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });

        if (button.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            });
        }

        if (isVisible)
        {
            revealInput.CaretIndex = revealInput.Text.Length;
            revealInput.Focus();
        }
    }

    private void ShowSavedAccounts()
    {
        var typed = LoginInput.Text.Trim();
        var entries = _accountVault.GetEntriesForRelay(SignInApiUrlInput.Text)
            .Where(entry => string.IsNullOrWhiteSpace(typed) ||
                            entry.Login.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(LocalAccountVault.MaxAccountsPerDevice)
            .ToList();

        SavedAccountsList.ItemsSource = entries;
        SavedAccountsPopup.IsOpen = entries.Count > 0 && LoginInput.IsKeyboardFocusWithin;
    }

    private void ApplySelectedSavedAccount()
    {
        if (SavedAccountsList.SelectedItem is not LocalAccountVaultEntry entry)
        {
            return;
        }

        _isApplyingSavedAccount = true;
        try
        {
            LoginInput.Text = entry.Login;
            SignInApiUrlInput.Text = entry.RelayServer;
            TryFillSavedPassword(entry.Login);
            SavedAccountsPopup.IsOpen = false;
            PasswordInput.Focus();
        }
        finally
        {
            _isApplyingSavedAccount = false;
        }
    }

    private void TryFillSavedPassword(string login)
    {
        var password = _accountVault.TryGetPassword(login, SignInApiUrlInput.Text);
        if (!string.IsNullOrWhiteSpace(password))
        {
            PasswordInput.Password = password;
        }
    }

    private static string NormalizeRelayAddress(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Enter only the VPS IP address and relay port, for example 91.186.217.186:42800.");
        }

        var separator = trimmed.LastIndexOf(':');
        if (separator <= 0 || separator == trimmed.Length - 1 ||
            !int.TryParse(trimmed[(separator + 1)..], out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Enter the VPS address with port 42800, for example 91.186.217.186:42800.");
        }

        var host = trimmed[..separator].Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("VPS address is required.");
        }

        return $"{host}:{port}";
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            BusyOverlay.Visibility = Visibility.Visible;
            IsHitTestVisible = false;
            StatusPanel.Visibility = Visibility.Collapsed;
            await action();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, true);
        }
        finally
        {
            IsHitTestVisible = true;
            BusyOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void NavigateTo(FrameworkElement next, bool rememberCurrent = true)
    {
        if (rememberCurrent && _currentPage is not null && !ReferenceEquals(_currentPage, next))
        {
            _pageHistory.Push(_currentPage);
        }

        ShowPage(next);
    }

    private void NavigateBack(FrameworkElement fallback)
    {
        var next = _pageHistory.Count > 0 ? _pageHistory.Pop() : fallback;
        ShowPage(next, isBackNavigation: true);
    }

    private void ShowPage(FrameworkElement next, bool isBackNavigation = false)
    {
        if (_currentPage is not null && !ReferenceEquals(_currentPage, next))
        {
            _currentPage.Visibility = Visibility.Collapsed;
            _currentPage.Opacity = 0;
        }

        _currentPage = next;
        next.Visibility = Visibility.Visible;
        if (_settings.ReducedMotionEnabled)
        {
            next.RenderTransform = null;
            next.Opacity = 1;
            StatusPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var translate = new System.Windows.Media.TranslateTransform(isBackNavigation ? -16 : 16, 0);
        next.RenderTransform = translate;
        next.Opacity = 0;
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        translate.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(isBackNavigation ? -16 : 16, 0, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        StatusPanel.Visibility = Visibility.Collapsed;
    }

    private void AnimateShell()
    {
        if (_settings.ReducedMotionEnabled)
        {
            AccountCard.Opacity = 1;
            AccountCardScale.ScaleX = 1;
            AccountCardScale.ScaleY = 1;
            AccountCardShift.Y = 0;
            return;
        }

        AccountCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        AccountCardScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { Amplitude = 0.16, EasingMode = EasingMode.EaseOut }
            });
        AccountCardScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { Amplitude = 0.16, EasingMode = EasingMode.EaseOut }
            });
        AccountCardShift.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void ShowStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            isError ? System.Windows.Media.Color.FromRgb(255, 151, 151) : System.Windows.Media.Color.FromRgb(201, 205, 215));
        StatusPanel.Visibility = Visibility.Visible;
    }

    private enum RecoveryMode
    {
        SignIn,
        ResetPassword
    }
}
