using System.Windows;
using System.Windows.Threading;
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

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        window.Show();
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
