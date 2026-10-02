using System.Windows;
using CodexBridge.App.ViewModels;

namespace CodexBridge.App;

public partial class SnapshotView : System.Windows.Controls.UserControl
{
    public SnapshotView() => InitializeComponent();

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.SaveCurrentSnapshotAsync(vm.SnapshotName);
    }

    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.RestoreSelectedSnapshotAsync();
    }

    private void RenameClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedSnapshot?.Snapshot is null) return;
        var dialog = new SnapshotRenameView
        {
            DataContext = new SnapshotRenameDialogModel(vm.SelectedSnapshot.Snapshot.Name),
        };
        var window = new Window
        {
            Title = (string)(Application.Current.FindResource("UiRenameSnapshotTitle") ?? "重命名工作现场"),
            Width = 400,
            Height = 190,
            Owner = Window.GetWindow(this),
            Content = dialog,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        if (window.ShowDialog() == true && dialog.DataContext is SnapshotRenameDialogModel model)
        {
            vm.RenameSelectedSnapshot(model.Name);
        }
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedSnapshot is null) return;
        vm.DeleteSelectedSnapshot(entry =>
        {
            var messageKey = entry.Snapshot is null ? "UiDeleteCorruptSnapshotConfirm" : "UiDeleteSnapshotConfirm";
            var dialog = new SnapshotDeleteView
            {
                DataContext = Application.Current.FindResource(messageKey),
            };
            var window = new Window
            {
                Title = (string)Application.Current.FindResource("UiDeleteSnapshotTitle"),
                Width = 400,
                Height = 180,
                Owner = Window.GetWindow(this),
                Content = dialog,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
            };
            return window.ShowDialog() == true;
        });
    }
}

public sealed class SnapshotRenameDialogModel
{
    public SnapshotRenameDialogModel(string name) => Name = name;
    public string Name { get; set; }
}
