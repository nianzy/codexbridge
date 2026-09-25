using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexBridge.App.Controls;

public partial class MarkdownText : UserControl
{
    public MarkdownText() => InitializeComponent();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkdownText), new PropertyMetadata(string.Empty, OnTextChanged));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MarkdownText)d).Render(e.NewValue as string ?? string.Empty);

    private void Render(string text)
    {
        ContentPanel.Children.Clear();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("```") )
            {
                var language = line[3..].Trim();
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].StartsWith("```")) code.Add(lines[i]);
                ContentPanel.Children.Add(new CodeBlock { Language = language, Code = string.Join("\n", code) });
                continue;
            }
            var trimmed = line.TrimStart();
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), LineHeight = 21 };
            if (trimmed.StartsWith("#"))
            {
                var heading = trimmed.TrimStart('#').Trim();
                block.Text = heading; block.FontFamily = new FontFamily("Segoe UI Semibold"); block.FontSize = 16;
            }
            else if (trimmed.StartsWith(">"))
            {
                block.Text = trimmed[1..].Trim(); block.Foreground = (Brush)FindResource("TextSecondary"); block.FontStyle = System.Windows.FontStyles.Italic;
            }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || (trimmed.Length > 2 && char.IsDigit(trimmed[0]) && trimmed[1] == '.'))
            {
                block.Text = "• " + trimmed[(trimmed.IndexOf(' ') + 1)..];
            }
            else block.Text = line;
            ContentPanel.Children.Add(block);
        }
    }
}
