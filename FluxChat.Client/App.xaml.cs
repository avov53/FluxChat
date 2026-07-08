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
        CrashLog.Write(e.Exception, "WPF dispatcher exception");
        MessageBox.Show(
            $"FluxChat crashed during startup or UI work.\n\n{e.Exception.Message}\n\nLog: {CrashLog.LogPath}",
            "FluxChat",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown();
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
