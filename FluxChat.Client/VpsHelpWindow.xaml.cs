using System.Windows;
using Clipboard = System.Windows.Clipboard;

namespace FluxChat.Client;

public partial class VpsHelpWindow : Window
{
    public VpsHelpWindow()
    {
        InitializeComponent();
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command } && !string.IsNullOrWhiteSpace(command))
        {
            Clipboard.SetText(command);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
