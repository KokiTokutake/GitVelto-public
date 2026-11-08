using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Small "?" button next to a Git term. Shows the glossary entry in a popup.</summary>
public sealed class TermHelp : Button
{
    public TermHelp(string key)
    {
        var e = Glossary.Entry(key);
        Content = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 13, Foreground = Ui.Secondary() };
        Padding = new Thickness(3);
        MinWidth = 0; MinHeight = 0;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(4, 0, 0, 0);
        ToolTip = e?.Summary;
        Focusable = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "用語の説明: " + (e?.Term ?? key));
        if (e == null) return;
        var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var border = new Border
        {
            Width = 400, Padding = new Thickness(14), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            Child = new TermCard(e, compact: true, close: () => popup.IsOpen = false),
        };
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        if (border.Background == null) border.Background = SystemColors.WindowBrush;
        popup.Child = border;
        Click += (_, _) => popup.IsOpen = !popup.IsOpen;
    }

    /// <summary>Text label followed by a "?" help button.</summary>
    public static StackPanel Label(string text, string key) =>
        new() { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, new TermHelp(key) } };

    /// <summary>Any control followed by a "?" help button.</summary>
    public static StackPanel With(UIElement control, string key) =>
        new() { Orientation = Orientation.Horizontal, Children = { control, new TermHelp(key) } };
}

public sealed class TermCard : StackPanel
{
    public TermCard(GlossaryEntry entry, bool compact = false, Action<string>? onSelectRelated = null, Action? close = null)
    {
        var head = new TextBlock { TextWrapping = TextWrapping.Wrap };
        head.Inlines.Add(new Run(entry.Term) { FontSize = compact ? 15 : 22, FontWeight = FontWeights.Bold });
        head.Inlines.Add(new Run("  " + entry.English) { Foreground = Ui.Secondary() });
        Children.Add(head);
        Children.Add(new TextBlock { Text = entry.Summary, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        Children.Add(new TextBox
        {
            Text = entry.Detail, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(0),
        });
        if (entry.Command.Length > 0)
        {
            Children.Add(new TextBlock { Text = "コマンドでは", FontSize = 11, Foreground = Ui.Secondary(), Margin = new Thickness(0, 10, 0, 2) });
            Children.Add(new TextBox
            {
                Text = entry.Command, IsReadOnly = true, FontFamily = Ui.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            });
        }
        if (entry.Related.Length > 0)
        {
            var rel = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            rel.Children.Add(new TextBlock { Text = "関連:", FontSize = 11, Foreground = Ui.Secondary(), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            foreach (var key in entry.Related)
            {
                if (Glossary.Entry(key) is not { } r) continue;
                var link = new Hyperlink(new Run(r.Term));
                link.Click += (_, _) =>
                {
                    if (onSelectRelated != null) onSelectRelated(key);
                    else { close?.Invoke(); WindowManager.Shared.ShowGlossary(key); }
                };
                rel.Children.Add(new TextBlock(link) { FontSize = 12, Margin = new Thickness(0, 0, 10, 0) });
            }
            Children.Add(rel);
        }
        if (compact)
        {
            var b = new Button { Content = "用語集で開く", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            b.Click += (_, _) => { close?.Invoke(); WindowManager.Shared.ShowGlossary(entry.Id); };
            Children.Add(b);
        }
    }
}

/// <summary>Shows the git commands a dialog will run, with explanations (learning mode only).</summary>
public sealed class CommandPreview : Border
{
    private readonly StackPanel list = new();
    private readonly TextBlock noteText = new() { FontSize = 11, Foreground = Ui.Secondary(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock chevron = new() { Text = "", FontFamily = Ui.IconFont, FontSize = 11 };
    private IReadOnlyList<IReadOnlyList<string>> commands = [];
    private string? note;
    private static bool expanded = true;

    public CommandPreview()
    {
        Padding = new Thickness(10, 8, 10, 8);
        CornerRadius = new CornerRadius(6);
        Background = (Brush)Application.Current.FindResource("LearningBg");
        Margin = new Thickness(0, 8, 0, 0);

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Ui.Glyph("", 13, Ui.Accent()));
        title.Children.Add(new TextBlock { Text = " 学習モード: 実行される git コマンド", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var copy = SmallButton("", "コマンドをコピー", () => Shell.Copy(string.Join("\n", commands.Select(c => CommandExplainer.Explain(c).Command))));
        var toggle = new Button { Content = chevron, Padding = new Thickness(6, 3, 6, 3), MinWidth = 0, MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        toggle.Click += (_, _) => { expanded = !expanded; Render(); };
        var header = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(copy);
        right.Children.Add(toggle);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(title);

        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(list);
        root.Children.Add(noteText);
        Child = root;
        AppSettings.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.LearningMode) or null) Dispatcher.BeginInvoke(Render);
        };
        Render();
    }

    private static Button SmallButton(string glyph, string tip, Action a)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 12 }, ToolTip = tip,
            Padding = new Thickness(6, 3, 6, 3), MinWidth = 0, MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        b.Click += (_, _) => a();
        return b;
    }

    /// <summary>Sets the commands (git arguments without "git") and an optional note.</summary>
    public void Set(IEnumerable<IReadOnlyList<string>> cmds, string? note = null)
    {
        commands = cmds.ToList();
        this.note = note;
        Render();
    }

    public void Set(IReadOnlyList<string> single, string? note = null) => Set([single], note);

    private void Render()
    {
        Visibility = AppSettings.Shared.LearningMode && commands.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        chevron.Text = expanded ? "" : "";
        list.Children.Clear();
        foreach (var c in commands) list.Children.Add(new CommandExplanationRow(c, expanded));
        noteText.Text = note ?? "";
        noteText.Visibility = expanded && !string.IsNullOrEmpty(note) ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class CommandExplanationRow : StackPanel
{
    public CommandExplanationRow(IReadOnlyList<string> args, bool expanded = true)
    {
        var ex = CommandExplainer.Explain(args);
        Margin = new Thickness(0, 4, 0, 0);
        Children.Add(new TextBox
        {
            Text = "$ " + ex.Command, FontFamily = Ui.Mono, FontSize = 12, IsReadOnly = true, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(0), TextWrapping = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap,
        });
        if (!expanded) return;
        if (ex.Summary.Length > 0) Children.Add(new TextBlock { Text = "→ " + ex.Summary, FontSize = 11.5, Foreground = Ui.Secondary(), TextWrapping = TextWrapping.Wrap });
        foreach (var (opt, meaning) in ex.Options)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 1, 0, 0), FontSize = 11.5 };
            t.Inlines.Add(new Run(opt) { FontFamily = Ui.Mono, Foreground = Ui.Accent() });
            t.Inlines.Add(new Run("  " + meaning) { Foreground = Ui.Secondary() });
            Children.Add(t);
        }
    }
}
