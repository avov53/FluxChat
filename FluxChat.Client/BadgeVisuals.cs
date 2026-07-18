using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluxChat.Shared;

namespace FluxChat.Client;

internal static class BadgeVisuals
{
    private static readonly ImageSource Owner = Load("owner.png");
    private static readonly ImageSource Tester = Load("tester.png");

    public static ImageSource? GetImage(string? badgeId)
        => badgeId == BadgeIds.Owner ? Owner : badgeId == BadgeIds.Tester ? Tester : null;

    private static ImageSource Load(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/Badges/{file}", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
