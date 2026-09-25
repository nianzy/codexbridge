using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CodexBridge.App.ViewModels;

namespace CodexBridge.App.Controls;

public sealed class ProjectTreeItem
{
    public ProjectTreeItem(string name, string fullPath, bool isDirectory) { Name = name; FullPath = fullPath; IsDirectory = isDirectory; }
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<ProjectTreeItem> Children { get; } = new();
}

public partial class ProjectExplorer : UserControl
{
    public ProjectExplorer() => InitializeComponent();

    private void RefreshClick(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.RefreshProjectExplorer();
    private void ContextClick(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.GenerateProjectContext();
    private void NotesClick(object sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.OpenNotesWindow();
    private void TreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ProjectTreeItem item && !item.IsDirectory) (DataContext as MainWindowViewModel)?.SelectProjectFile(item.FullPath);
    }
    private void AddContextClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: TextBlock text } } && text.DataContext is ProjectTreeItem item && !item.IsDirectory) (DataContext as MainWindowViewModel)?.AddProjectFileToContext(item.FullPath);
    }
}
