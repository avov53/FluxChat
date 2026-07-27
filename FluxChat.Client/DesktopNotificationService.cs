using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security;
using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;

namespace FluxChat.Client;

internal sealed class DesktopNotificationService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Func<Task> _fallbackActivated;
    private readonly Action<string> _toastActivated;

    public DesktopNotificationService(Func<Task> fallbackActivated, Action<string> toastActivated)
    {
        _fallbackActivated = fallbackActivated;
        _toastActivated = toastActivated;
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "FluxChat",
            Visible = true
        };

        _notifyIcon.BalloonTipClicked += async (_, _) => await _fallbackActivated();
        _notifyIcon.DoubleClick += async (_, _) => await _fallbackActivated();
    }

    public void ShowMessage(string title, string preview, string? avatarPath, string activationArgument)
    {
        if (TryShowToast(title, preview, avatarPath, activationArgument, []))
        {
            return;
        }

        ShowFallback(title, preview, 5000);
    }

    public void ShowCall(string title, string preview, string? avatarPath, string contactId)
    {
        var actions = new[]
        {
            ("Accept", $"accept-call:{contactId}"),
            ("Decline", $"decline-call:{contactId}")
        };

        if (TryShowToast(title, preview, avatarPath, $"open-chat:{contactId}", actions))
        {
            return;
        }

        ShowFallback(title, preview, 8000);
    }

    private void ShowFallback(string title, string text, int timeoutMs)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(timeoutMs);
        }
        catch (ArgumentException ex)
        {
            AppLog.Write(ex, "Desktop notification skipped");
        }
    }

    private bool TryShowToast(string title, string text, string? avatarPath, string launchArgument, IReadOnlyList<(string Label, string Argument)> actions)
    {
        try
        {
            var xmlDocumentType = Type.GetType("Windows.Data.Xml.Dom.XmlDocument, Windows, ContentType=WindowsRuntime");
            var toastNotificationType = Type.GetType("Windows.UI.Notifications.ToastNotification, Windows, ContentType=WindowsRuntime");
            var toastNotificationManagerType = Type.GetType("Windows.UI.Notifications.ToastNotificationManager, Windows, ContentType=WindowsRuntime");
            if (xmlDocumentType is null || toastNotificationType is null || toastNotificationManagerType is null)
            {
                return false;
            }

            var xml = BuildToastXml(title, text, avatarPath, launchArgument, actions);
            var document = Activator.CreateInstance(xmlDocumentType);
            xmlDocumentType.GetMethod("LoadXml", [typeof(string)])?.Invoke(document, [xml]);
            var toast = Activator.CreateInstance(toastNotificationType, document);
            var notifier = toastNotificationManagerType.GetMethod("CreateToastNotifier", [typeof(string)])?.Invoke(null, ["FluxChat"]);
            notifier?.GetType().GetMethod("Show")?.Invoke(notifier, [toast]);
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException or TypeLoadException or MethodAccessException or MemberAccessException or SecurityException or InvalidOperationException)
        {
            AppLog.Write(ex, "Windows toast notification unavailable");
            return false;
        }
    }

    private static string BuildToastXml(string title, string text, string? avatarPath, string launchArgument, IReadOnlyList<(string Label, string Argument)> actions)
    {
        var builder = new StringBuilder();
        builder.Append("<toast activationType=\"foreground\" launch=\"")
            .Append(EscapeXml(launchArgument))
            .Append("\"><visual><binding template=\"ToastGeneric\">");
        builder.Append("<text>").Append(EscapeXml(title)).Append("</text>");
        builder.Append("<text>").Append(EscapeXml(text)).Append("</text>");
        if (!string.IsNullOrWhiteSpace(avatarPath) && File.Exists(avatarPath))
        {
            builder.Append("<image placement=\"appLogoOverride\" hint-crop=\"circle\" src=\"")
                .Append(EscapeXml(new Uri(avatarPath).AbsoluteUri))
                .Append("\"/>");
        }

        builder.Append("</binding></visual>");
        if (actions.Count > 0)
        {
            builder.Append("<actions>");
            foreach (var action in actions)
            {
                builder.Append("<action activationType=\"foreground\" content=\"")
                    .Append(EscapeXml(action.Label))
                    .Append("\" arguments=\"")
                    .Append(EscapeXml(action.Argument))
                    .Append("\"/>");
            }

            builder.Append("</actions>");
        }

        builder.Append("</toast>");
        return builder.ToString();
    }

    private static string EscapeXml(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
