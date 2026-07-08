using System.Windows;

namespace FluxChat.Client;

public partial class RenameContactWindow : Window
{
    public RenameContactWindow(string currentName)
    {
        InitializeComponent();
        NameInput.Text = currentName;
        NameInput.SelectAll();
        Loaded += (_, _) => NameInput.Focus();
    }

    public string ContactName => NameInput.Text.Trim();

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ContactName))
        {
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
