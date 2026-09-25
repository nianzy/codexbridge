using System.Windows.Controls;
using System.Windows;
using CodexBridge.App.ViewModels;

namespace CodexBridge.App.Controls;

public partial class MessageBubble : UserControl
{
    public MessageBubble()
    {
        InitializeComponent();
    }

    private void CopyMessageClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is TurnRowViewModel row) Clipboard.SetText($"{row.Turn.User.Text}\n\n{row.Turn.Assistant?.Text ?? ""}");
    }

    private void CopyMarkdownClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is TurnRowViewModel row) Clipboard.SetText($"## User\n\n{row.Turn.User.Text}\n\n## Assistant\n\n{row.Turn.Assistant?.Text ?? ""}");
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow?.DataContext is MainWindowViewModel vm && vm.ExportDraftCommand.CanExecute(null)) vm.ExportDraftCommand.Execute(null);
    }

    private void AddContextClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is TurnRowViewModel row && Application.Current.MainWindow?.DataContext is MainWindowViewModel vm) vm.AddTurnToContext(row);
    }
}
