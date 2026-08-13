using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluxChat.Client;

public partial class IncomingCallMiniWindow : Window
{
    private string _videoPath = "";
    private double _videoStartSeconds;

    public IncomingCallMiniWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? AcceptAudioRequested;
    public event EventHandler? AcceptVideoRequested;
    public event EventHandler? DeclineRequested;

    public void UpdateCall(
        ContactViewModel contact,
        ContactViewModel? caller,
        bool isVideoCall,
        bool canUseCamera,
        string cameraUnavailableReason)
    {
        var displayContact = contact.IsGroup ? contact : caller ?? contact;
        TitleText.Text = isVideoCall ? "Incoming video call..." : contact.IsGroup ? "Incoming group call..." : "Incoming call...";
        CallerNameText.Text = contact.DisplayName;
        SubtitleText.Text = contact.IsGroup
            ? isVideoCall ? "Group video call" : "Group call"
            : isVideoCall ? "Camera requested" : "";
        SubtitleText.Visibility = contact.IsGroup || isVideoCall ? Visibility.Visible : Visibility.Collapsed;
        AcceptVideoButton.Visibility = isVideoCall ? Visibility.Visible : Visibility.Collapsed;
        AcceptVideoButton.IsEnabled = canUseCamera;
        AcceptVideoButton.ToolTip = canUseCamera
            ? "Accept with video"
            : string.IsNullOrWhiteSpace(cameraUnavailableReason) ? "Camera unavailable" : cameraUnavailableReason;

        AvatarInitials.Text = displayContact.Initials;
        AvatarInitials.Visibility = Visibility.Visible;
        AvatarImage.Visibility = Visibility.Collapsed;
        AvatarVideo.Visibility = Visibility.Collapsed;
        AvatarVideo.Stop();
        AvatarVideo.Source = null;
        _videoPath = "";
        _videoStartSeconds = 0;

        if (string.IsNullOrWhiteSpace(displayContact.AvatarPath) || !File.Exists(displayContact.AvatarPath))
        {
            return;
        }

        if (displayContact.IsAvatarImage)
        {
            var image = LoadBitmap(displayContact.AvatarPath);
            if (image is null)
            {
                return;
            }

            AvatarImage.Source = image;
            AvatarImage.Visibility = Visibility.Visible;
            AvatarInitials.Visibility = Visibility.Collapsed;
            return;
        }

        if (displayContact.IsAvatarVideo)
        {
            _videoPath = displayContact.AvatarPath;
            _videoStartSeconds = Math.Max(0, displayContact.AvatarVideoStartSeconds);
            AvatarVideo.Source = new Uri(_videoPath, UriKind.Absolute);
            AvatarVideo.Position = TimeSpan.FromSeconds(_videoStartSeconds);
            AvatarVideo.Visibility = Visibility.Visible;
            AvatarInitials.Visibility = Visibility.Collapsed;
            AvatarVideo.Play();
        }
    }

    public void PlaceNearWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    protected override void OnClosed(EventArgs e)
    {
        AvatarVideo.Stop();
        AvatarVideo.Source = null;
        base.OnClosed(e);
    }

    private void DragSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void DeclineButton_OnClick(object sender, RoutedEventArgs e)
        => DeclineRequested?.Invoke(this, EventArgs.Empty);

    private void AcceptAudioButton_OnClick(object sender, RoutedEventArgs e)
        => AcceptAudioRequested?.Invoke(this, EventArgs.Empty);

    private void AcceptVideoButton_OnClick(object sender, RoutedEventArgs e)
        => AcceptVideoRequested?.Invoke(this, EventArgs.Empty);

    private void AvatarVideo_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_videoPath))
        {
            return;
        }

        AvatarVideo.Position = TimeSpan.FromSeconds(_videoStartSeconds);
        AvatarVideo.Play();
    }

    private static ImageSource? LoadBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "Incoming call avatar load failed");
            return null;
        }
    }
}
