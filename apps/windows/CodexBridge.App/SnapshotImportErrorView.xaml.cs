using System.Windows;

namespace CodexBridge.App;

public partial class SnapshotImportErrorView : System.Windows.Controls.UserControl
{
    public SnapshotImportErrorView() => InitializeComponent();

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.DialogResult = true;
    }
}
