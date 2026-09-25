using System.Windows.Controls;

namespace CodexBridge.App.Controls;

public partial class DraftPanel : UserControl
{
    public DraftPanel() => InitializeComponent();
    public bool IsExpanded { get; set; } = true;
    private void ToggleClick(object sender, System.Windows.RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
        DraftBody.Visibility = IsExpanded ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ToggleButton.Content = IsExpanded ? "Collapse" : "Expand";
    }
}
