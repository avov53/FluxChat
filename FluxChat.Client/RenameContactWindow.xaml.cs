using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluxChat.Client;

public partial class RenameContactWindow : Window
{
    private readonly ContactViewModel _contact;

    public RenameContactWindow(ContactViewModel contact)
    {
        _contact = contact;
        InitializeComponent();
        NameInput.Text = contact.DisplayName;
        CurrentNameText.Text = contact.DisplayName;
        InitialsText.Text = contact.Initials;
        LoadAvatar();
        NameInput.SelectAll();
    }

    public string ContactName => NameInput.Text.Trim();

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        NameInput.Focus();

        var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, opacity);

        if (DialogCard.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var scaleAnimation = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new BackEase
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = 0.25
            }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Save();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Window_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        => Save();

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(ContactName))
        {
            ValidationText.Visibility = Visibility.Visible;
            NameInput.Focus();
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        AvatarVideo.Stop();
        base.OnClosed(e);
    }

    private void LoadAvatar()
    {
        if (!_contact.HasAvatar)
        {
            return;
        }

        if (_contact.IsAvatarImage)
        {
            AvatarImage.Source = AvatarImageLoader.Load(_contact.AvatarPath);
            if (AvatarImage.Source is not null)
            {
                AvatarImage.Visibility = Visibility.Visible;
                InitialsText.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_contact.IsAvatarVideo)
        {
            AvatarVideo.Source = new Uri(_contact.AvatarPath);
            AvatarVideo.Visibility = Visibility.Visible;
            InitialsText.Visibility = Visibility.Collapsed;
            AvatarVideo.Position = TimeSpan.FromSeconds(_contact.AvatarVideoStartSeconds);
            AvatarVideo.Play();
        }
    }

    private void AvatarVideo_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        AvatarVideo.Position = TimeSpan.FromSeconds(_contact.AvatarVideoStartSeconds);
        AvatarVideo.Play();
    }
}
