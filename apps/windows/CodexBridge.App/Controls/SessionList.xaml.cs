using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CodexBridge.App.Controls;

public partial class SessionList : UserControl
{
    public static readonly DependencyProperty SourceIndexProperty = DependencyProperty.Register(
        nameof(SourceIndex),
        typeof(int),
        typeof(SessionList),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public SessionList()
    {
        InitializeComponent();
    }

    public int SourceIndex
    {
        get => (int)GetValue(SourceIndexProperty);
        set => SetValue(SourceIndexProperty, value);
    }

    public TextBox SearchBoxControl => SearchBox;

    private void SearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
    }

    private void CopyItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || menu.Parent is not ContextMenu context || context.PlacementTarget is not FrameworkElement target) return;
        var value = target.DataContext;
        var text = menu.Tag?.ToString() == "id"
            ? value switch
            {
                CodexBridge.App.ViewModels.ConversationListItemViewModel chat => chat.Conversation.Id.ToString(),
                CodexBridge.App.ViewModels.CodexThreadListItemViewModel codex => codex.Thread.Id,
                _ => string.Empty,
            }
            : value switch
            {
                CodexBridge.App.ViewModels.ConversationListItemViewModel chat => chat.Title,
                CodexBridge.App.ViewModels.CodexThreadListItemViewModel codex => codex.Title,
                _ => string.Empty,
            };
        if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
    }

    private void OnChatGptSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            SourceIndex = 0;
        }
    }

    private void OnCodexSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            SourceIndex = 1;
        }
    }
}
