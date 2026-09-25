using System.Windows;
using System.Windows.Controls;
using CodexBridge.App.Infrastructure;
using CodexBridge.App.ViewModels;
namespace CodexBridge.App.Controls;
public partial class ContextPanel : UserControl
{
    public ContextPanel() => InitializeComponent();
    private void RemoveClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is ContextPackItem item) (DataContext as MainWindowViewModel)?.RemoveContextItem(item); }
}
