using System.Windows;
using System.Windows.Threading;
using System.Net.Http;
using MessageBox = System.Windows.MessageBox;

namespace FluxChat.Client;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private async void App_OnStartup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var settings = await AppSettingsStore.LoadAsync();
            var profile = await UserProfileStore.LoadOrCreateAsync();
            AppPaths.UseAccountData(profile.UserId);
            var accountVault = LocalAccountVault.Load();
            if (!string.IsNullOrWhiteSpace(settings.AccountLogin))
            {
                var savedAccount = accountVault.Find(settings.AccountLogin, settings.RelayServer);
                if (savedAccount is not null && !string.IsNullOrWhiteSpace(savedAccount.UserId))
                {
                    var savedProfile = await UserProfileStore.TryLoadProfileAsync(savedAccount.UserId);
                    if (savedProfile is not null)
                    {
                        profile = savedProfile;
                        await UserProfileStore.ActivateAsync(profile);
                        AppPaths.UseAccountData(profile.UserId);
                    }
                }
            }

            var authenticated = false;

            if (!string.IsNullOrWhiteSpace(settings.AccountApiUrl) &&
                !string.IsNullOrWhiteSpace(settings.AccountSessionToken))
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    var session = await new AccountClient(settings.AccountApiUrl)
                        .ValidateSessionAsync(settings.AccountSessionToken, timeout.Token);
                    authenticated = session.Accepted &&
                                    string.Equals(session.UserId, profile.UserId, StringComparison.Ordinal);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    AppLog.Write(ex, "Saved account session could not be validated");
                }
            }

            if (!authenticated)
            {
                var accountWindow = new AccountWindow(profile, settings);
                MainWindow = accountWindow;
                if (accountWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                profile = accountWindow.SelectedProfile;
                AppPaths.UseAccountData(profile.UserId);
            }

            var window = new MainWindow(profile, settings);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Account startup failed");
            MessageBox.Show(
                $"FluxChat could not open the account screen.\n\n{ex.Message}\n\nLog: {CrashLog.LogPath}",
                "FluxChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is OperationCanceledException)
        {
            CrashLog.Write(e.Exception, "Recovered canceled UI operation");
            e.Handled = true;
            return;
        }

        if (IsRecoverableWebViewCompositionResizeException(e.Exception))
        {
            CrashLog.Write(e.Exception, "Recovered WebView2 composition resize exception");
            e.Handled = true;
            if (Current?.MainWindow is MainWindow window)
            {
                window.RecoverFromWebViewCompositionResizeFault();
            }

            return;
        }

        CrashLog.Write(e.Exception, "WPF dispatcher exception");
        MessageBox.Show(
            $"FluxChat crashed during startup or UI work.\n\n{e.Exception.Message}\n\nLog: {CrashLog.LogPath}",
            "FluxChat",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current?.Shutdown();
    }

    private static bool IsRecoverableWebViewCompositionResizeException(Exception exception)
    {
        if (exception is not ArgumentException)
        {
            return false;
        }

        var details = exception.ToString();
        return details.Contains("WebView2CompositionControl_SizeChanged", StringComparison.Ordinal) ||
               details.Contains("Direct3D11CaptureFramePool", StringComparison.Ordinal);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CrashLog.Write(exception, "Unhandled app domain exception");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
