using System.Windows;
using CodexBridge.App.Infrastructure;

namespace CodexBridge.App;

public partial class SnapshotImportView : System.Windows.Controls.UserControl
{
    public SnapshotImportView() => InitializeComponent();

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.DialogResult = false;
    }
}
