using System.Windows;
using System.Windows.Controls;

namespace CodexBridge.App;

public partial class SnapshotDeleteView : UserControl
{
    public SnapshotDeleteView() => InitializeComponent();

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.DialogResult = false;
    }
}
