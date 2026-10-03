using System.Windows;
using System.Windows.Controls;
using CodexBridge.App.Infrastructure;

namespace CodexBridge.App;

public partial class WorkspaceSwitchConfirmView : UserControl
{
    public WorkspaceSwitchConfirmView() => InitializeComponent();

    public static bool ShowConfirmation(bool autoSaveEnabled)
    {
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current.MainWindow;
        var window = new Window
        {
            Title = (string)Application.Current.FindResource("UiWorkspaceSwitchConfirmTitle"),
            Width = 480,
            SizeToContent = SizeToContent.Height,
            Owner = owner,
            Content = new WorkspaceSwitchConfirmView { DataContext = UiStrings.WorkspaceSwitchConfirmationMessage(autoSaveEnabled) },
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        return window.ShowDialog() == true;
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.DialogResult = false;
    }
}
