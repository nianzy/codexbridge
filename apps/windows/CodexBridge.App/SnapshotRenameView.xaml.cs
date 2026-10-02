using System.Windows;
using CodexBridge.App.Infrastructure;

namespace CodexBridge.App;

public partial class SnapshotRenameView : System.Windows.Controls.UserControl
{
    public SnapshotRenameView() => InitializeComponent();

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show((string)(Application.Current.FindResource("UiSnapshotNameRequired") ?? "现场名称不能为空。"), (string)(Application.Current.FindResource("UiRenameSnapshotTitle") ?? "重命名工作现场"), MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }

        if (Window.GetWindow(this) is { } window)
        {
            window.DialogResult = true;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.DialogResult = false;
        }
    }

    private void NameBoxLoaded(object sender, RoutedEventArgs e) => NameBox.SelectAll();
}
