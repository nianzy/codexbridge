using System.Windows;
using System.Windows.Controls;

namespace CodexBridge.App;

public partial class SnapshotPreviewView : UserControl
{
    public SnapshotPreviewView() => InitializeComponent();

    public event EventHandler? CloseRequested;
    public event EventHandler? RestoreRequested;

    private void CloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void RestoreClick(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
}
