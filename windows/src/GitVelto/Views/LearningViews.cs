using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Searchable Git glossary.</summary>
public sealed class GlossaryView : GVView
{
    private readonly ListBox list = new();
    private readonly TextBox search = new();
    private readonly ScrollViewer detail = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(22) };
    private string? selection;

    public GlossaryView(string? select)
    {
        selection = select ?? Glossary.Entries.First().Id;
        search.Margin = new Thickness(8);
        SetPlaceholder(search, "用語を検索 (例: リベース, push, HEAD)");
        search.TextChanged += (_, _) => Fill();
        list.BorderThickness = new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: string id }) { selection = id; ShowDetail(); }
        };

        var left = new DockPanel();
        DockPanel.SetDock(search, Dock.Top);
        left.Children.Add(search);
        left.Children.Add(list);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290), MinWidth = 220 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 320 });
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(detail, 2);
        grid.Children.Add(left);
        grid.Children.Add(splitter);
        grid.Children.Add(detail);
        Content = grid;
        Fill();
        ShowDetail();
    }

    internal static void SetPlaceholder(TextBox box, string text)
    {
        // Fluent theme shows ToolTip-free placeholder via this attached property when available.
        box.ToolTip = text;
        var hint = new TextBlock { Text = text, Foreground = Ui.Secondary(), IsHitTestVisible = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        box.Loaded += (_, _) =>
        {
            if (box.Parent is not Panel) return;
            var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(box);
            if (layer == null) return;
            var adorner = new PlaceholderAdorner(box, hint);
            layer.Add(adorner);
            void Update() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (_, _) => Update();
            Update();
        };
    }

    private sealed class PlaceholderAdorner : System.Windows.Documents.Adorner
    {
        private readonly TextBlock child;
        public PlaceholderAdorner(UIElement e, TextBlock child) : base(e) { this.child = child; AddVisualChild(child); IsHitTestVisible = false; }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => child;
        protected override Size ArrangeOverride(Size finalSize) { child.Arrange(new Rect(finalSize)); return finalSize; }
        protected override Size MeasureOverride(Size constraint) { child.Measure(constraint); return AdornedElement.RenderSize; }
    }

    private void Fill()
    {
        list.Items.Clear();
        foreach (var e in Glossary.Search(search.Text))
        {
            var panel = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
            panel.Children.Add(new TextBlock { Text = e.Term, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = e.Summary, FontSize = 11.5, Foreground = Ui.Secondary(), TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListBoxItem { Content = panel, Tag = e.Id };
            list.Items.Add(item);
            if (e.Id == selection) { list.SelectedItem = item; list.ScrollIntoView(item); }
        }
    }

    private void ShowDetail()
    {
        if (selection != null && Glossary.Entry(selection) is { } e)
        {
            detail.Content = new TermCard(e, onSelectRelated: key =>
            {
                selection = key;
                search.Text = "";
                Fill();
                ShowDetail();
            });
            return;
        }
        var intro = new StackPanel();
        intro.Children.Add(new TextBlock { Text = "Git 用語集", FontSize = 22, FontWeight = FontWeights.Bold });
        intro.Children.Add(new TextBlock { Text = "左の一覧から用語を選ぶと、意味・使いどころ・対応するコマンドを表示します。各画面の「?」ボタンからもここを開けます。", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Secondary(), Margin = new Thickness(0, 10, 0, 0) });
        intro.Children.Add(new TextBlock { Text = "はじめての方へのおすすめ順: リポジトリ → 作業ツリー → コミット → ブランチ → プッシュ → プル → マージ", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        detail.Content = intro;
    }
}

/// <summary>Commands GitVelto ran on the user's behalf.</summary>
public sealed class CommandHistoryView : GVView
{
    private readonly ListBox list = new();
    private readonly TextBlock empty = new() { Text = "まだコマンドは実行されていません。", Foreground = Ui.Secondary(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private Guid? selected;

    public CommandHistoryView()
    {
        var copyAll = new Button { Content = "すべてコピー", Margin = new Thickness(0, 0, 8, 0) };
        copyAll.Click += (_, _) => Shell.Copy(string.Join("\n", CommandHistory.Shared.Entries.AsEnumerable().Reverse()
            .Select(e => "cd " + CommandExplainer.Quote(e.Dir) + " && " + e.Display)));
        var clear = new Button { Content = "クリア" };
        clear.Click += (_, _) => CommandHistory.Shared.Clear();

        var header = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(copyAll);
        right.Children.Add(clear);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(Ui.Heading("GitVelto が実行した git コマンド"));

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(header);
        top.Children.Add(Ui.Caption("ボタン操作の裏で実際に何が実行されたかを確認できます。コピーしてターミナルで同じ操作を試すこともできます。"));

        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        list.SelectionChanged += (_, _) =>
        {
            selected = (list.SelectedItem as ListBoxItem)?.Tag as Guid?;
            Fill();
        };

        var body = new Grid();
        body.Children.Add(list);
        body.Children.Add(empty);
        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(body);
        Content = root;

        void Changed() => Dispatcher.BeginInvoke(Fill);
        Loaded += (_, _) => { CommandHistory.Shared.Changed -= Changed; CommandHistory.Shared.Changed += Changed; };
        Unloaded += (_, _) => CommandHistory.Shared.Changed -= Changed;
        Fill();
    }

    private bool filling;

    private void Fill()
    {
        if (filling) return;
        filling = true;
        try
        {
            var entries = CommandHistory.Shared.Entries;
            empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            list.Items.Clear();
            foreach (var e in entries)
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                var (glyph, color) = e.Ok switch
                {
                    null => ("", Ui.Secondary()),
                    true => ("", (Brush)Brushes.SeaGreen),
                    false => ("", Brushes.Crimson),
                };
                head.Children.Add(Ui.Glyph(glyph, 13, color));
                head.Children.Add(new TextBlock { Text = "  " + Ui.FormatDate(e.Date) + "   " + Path.GetFileName(e.Dir), FontSize = 11.5, Foreground = Ui.Secondary() });
                var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                panel.Children.Add(head);
                panel.Children.Add(new CommandExplanationRow(e.Args, selected == e.Id));
                var item = new ListBoxItem { Content = panel, Tag = e.Id };
                list.Items.Add(item);
                if (selected == e.Id) list.SelectedItem = item;
            }
        }
        finally { filling = false; }
    }
}
