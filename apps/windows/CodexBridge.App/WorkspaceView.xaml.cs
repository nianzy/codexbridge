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
            Title = "选择工作区文件夹",
            Multiselect = false,
        };

        if (picker.ShowDialog() != true) return;
        (DataContext as MainWindowViewModel)?.AddWorkspaceFromPath(picker.FolderName);
    }
}
