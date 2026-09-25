using System.Windows.Controls;
using System.Windows;
using CodexBridge.App.ViewModels;
namespace CodexBridge.App;
public partial class NotesView : UserControl
{
    public NotesView() => InitializeComponent();
    private void AddContextClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: TextBlock text } } && text.DataContext is NoteItem note) (DataContext as MainWindowViewModel)?.AddNoteToContext(note);
    }
}
