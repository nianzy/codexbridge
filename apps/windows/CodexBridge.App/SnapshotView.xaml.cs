using System.Windows;
using Microsoft.Win32;
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

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.CanExportSnapshot) return;
        var dialog = new SaveFileDialog
        {
            Filter = (string)Application.Current.FindResource("UiSnapshotPackageFilter"),
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = vm.GetSelectedSnapshotExportFileName(),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            vm.ExportSelectedSnapshot(dialog.FileName);
        }
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.BeginSnapshotImport();
        var dialog = new OpenFileDialog
        {
            Filter = (string)Application.Current.FindResource("UiSnapshotPackageFilter"),
            DefaultExt = ".zip",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var inspection = vm.InspectSnapshotImport(dialog.FileName);
        if (inspection is null)
        {
            ShowImportError(vm.SnapshotImportErrorMessage);
            return;
        }

        var view = new SnapshotImportView { DataContext = inspection };
        var window = new Window
        {
            Title = (string)Application.Current.FindResource("UiSnapshotImportWindowTitle"),
            Width = 560,
            Height = 520,
            Owner = Window.GetWindow(this),
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
        };
        if (window.ShowDialog() == true)
        {
            if (!vm.ImportSnapshotPackage(inspection)) ShowImportError(vm.SnapshotImportErrorMessage);
        }
    }

    private void ShowImportError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var view = new SnapshotImportErrorView { DataContext = message };
        var window = new Window
        {
            Title = (string)Application.Current.FindResource("UiSnapshotImportWindowTitle"),
            Width = 460,
            Height = 210,
            Owner = Window.GetWindow(this),
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        window.ShowDialog();
    }

    private async void PreviewClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var model = await vm.BuildSelectedSnapshotPreviewAsync();
        if (model is null) return;
        var view = new SnapshotPreviewView { DataContext = model };
        var window = new Window
        {
            Title = (string)Application.Current.FindResource("UiSnapshotPreviewWindowTitle"),
            Width = 940,
            Height = 740,
            MinWidth = 760,
            MinHeight = 560,
            Owner = Window.GetWindow(this),
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        view.CloseRequested += (_, _) => window.Close();
        view.RestoreRequested += async (_, _) =>
        {
            if (await vm.RestoreSelectedSnapshotAsync()) window.Close();
        };
        window.ShowDialog();
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
