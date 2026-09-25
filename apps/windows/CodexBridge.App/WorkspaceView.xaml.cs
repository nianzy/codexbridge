using System.Windows;
using System.Windows.Controls;
using CodexBridge.App.ViewModels;
using Microsoft.Win32;

namespace CodexBridge.App;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView() => InitializeComponent();

    private void AddWorkspaceClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Select a workspace folder",
            Multiselect = false,
        };

        if (picker.ShowDialog() != true) return;
        (DataContext as MainWindowViewModel)?.AddWorkspaceFromPath(picker.FolderName);
    }
}
