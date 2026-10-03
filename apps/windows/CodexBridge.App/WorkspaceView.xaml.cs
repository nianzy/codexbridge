using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CodexBridge.App.ViewModels;
using CodexBridge.App.Infrastructure;
using Microsoft.Win32;

namespace CodexBridge.App;

public partial class WorkspaceView : UserControl
{
    private bool suppressSelection;

    public WorkspaceView() => InitializeComponent();

    private async void WorkspaceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelection || e.AddedItems.Count == 0 || e.AddedItems[^1] is not WorkspaceItem candidate || DataContext is not MainWindowViewModel viewModel || viewModel.IsWorkspaceSwitchInProgress) return;
        WorkspaceListBox.IsEnabled = false;
        try
        {
            var committed = await viewModel.TrySwitchWorkspaceAsync(candidate);
            if (!committed && !string.IsNullOrWhiteSpace(viewModel.SnapshotOperationErrorMessage))
                (Window.GetWindow(this) as MainWindow)?.ShowSnapshotError(viewModel.SnapshotOperationErrorMessage);
        }
        finally
        {
            WorkspaceListBox.IsEnabled = true;
            suppressSelection = true;
            try { WorkspaceListBox.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget(); }
            finally { suppressSelection = false; }
        }
    }

    private async void AddWorkspaceClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "选择工作区文件夹",
            Multiselect = false,
        };

        if (picker.ShowDialog() != true) return;
        if (DataContext is MainWindowViewModel viewModel)
            await viewModel.AddWorkspaceFromPathAsync(picker.FolderName);
    }
}
