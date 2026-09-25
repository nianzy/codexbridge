using System.Windows;
using System.Windows.Controls;
using CodexBridge.App.Infrastructure;
using CodexBridge.App.ViewModels;

namespace CodexBridge.App.Controls;
public partial class GitStatusView : UserControl
{
    public GitStatusView() => InitializeComponent();
    private void RefreshClick(object sender, RoutedEventArgs e) => _ = (DataContext as MainWindowViewModel)?.RefreshGitStatusAsync();
    private void FileSelected(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is GitChangedFile file) _ = (DataContext as MainWindowViewModel)?.SelectGitFileAsync(file);
    }
    private void AddContextClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: DockPanel panel } } && panel.DataContext is GitChangedFile file) _ = (DataContext as MainWindowViewModel)?.AddGitDiffToContextAsync(file);
    }
}
