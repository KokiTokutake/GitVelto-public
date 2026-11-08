using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Built-in diff viewer (unified or side by side), with next/previous change navigation.</summary>
public sealed class DiffView : GVView
{
    private readonly Repo repo;
    private readonly DiffSource source;
    private readonly ListBox list = new();
    private readonly TextBlock adds = new() { Foreground = Brushes.SeaGreen, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock dels = new() { Foreground = Brushes.Crimson, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock position = new() { Foreground = Ui.Secondary(), Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 40 };
    private readonly CheckBox ignoreWs = new() { Content = "空白を無視", Margin = new Thickness(0, 0, 10, 0) };
    private readonly ComboBox mode = new() { Width = 96, Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBlock message = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) };
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Width = 120, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private List<DiffLine> lines = [];
    private List<int> changeStarts = [];
    private int changeIndex = -1;

    public DiffView(Repo repo, DiffSource source)
    {
        this.repo = repo;
        this.source = source;

        var name = source is DiffSource.Files f ? f.Right : repo.Relative(source.Path);
        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 8) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new Image { Source = Shell.Icon(source.Path), Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
        left.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 });
        left.Children.Add(new TextBlock { Text = "  " + source.Subtitle, Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center });

        mode.Items.Add("統合");
        mode.Items.Add("左右");
        mode.SelectedIndex = AppSettings.Shared.DiffSideBySide ? 1 : 0;
        mode.SelectionChanged += (_, _) => { AppSettings.Shared.DiffSideBySide = mode.SelectedIndex == 1; Render(); };
        ignoreWs.Click += (_, _) => Load();

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(adds);
        right.Children.Add(dels);
        right.Children.Add(Ui.IconButton("", "前の変更 (Shift+F7)", () => Go(-1)));
        right.Children.Add(Ui.IconButton("", "次の変更 (F7)", () => Go(1)));
        right.Children.Add(position);
        right.Children.Add(ignoreWs);
        right.Children.Add(mode);
        right.Children.Add(Ui.IconButton("", "外部ツールで開く", OpenExternal));
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(left);

        list.FontFamily = Ui.Mono;
        list.FontSize = 12;
        list.SelectionMode = SelectionMode.Extended;
        list.BorderThickness = new Thickness(0);
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Item);
        list.ItemContainerStyle = CompactItemStyle();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { CopySelection(); e.Handled = true; }
        };
        var copyMenu = new ContextMenu();
        copyMenu.Items.Add(FileList.Item("選択した行をコピー", CopySelection, ""));
        list.ContextMenu = copyMenu;

        var body = new Grid();
        body.Children.Add(list);
        body.Children.Add(message);
        body.Children.Add(loading);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var sep = new Separator { Margin = new Thickness(0) };
        DockPanel.SetDock(sep, Dock.Top);
        root.Children.Add(sep);
        root.Children.Add(body);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F7) { Go(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; }
        };
    }

    private static Style CompactItemStyle()
    {
        var s = new Style(typeof(ListBoxItem), Application.Current.TryFindResource(typeof(ListBoxItem)) as Style);
        s.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        s.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
        s.Setters.Add(new Setter(MinHeightProperty, 0.0));
        s.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return s;
    }

    protected internal override void OnShown() => Load();

    private async void Load()
    {
        loading.Visibility = Visibility.Visible;
        message.Visibility = Visibility.Collapsed;
        var args = source.Args(repo, ignoreWs.IsChecked == true);
        var r = await repo.GitAsync(args);
        loading.Visibility = Visibility.Collapsed;
        // `diff --no-index` exits with 1 when files differ.
        if (r.Status > 1 || (r.Status != 0 && r.Stdout.Length == 0))
        {
            lines = [];
            ShowMessage(r.Message, true);
            Render();
            return;
        }
        lines = DiffParser.Parse(r.Out);
        changeStarts = DiffParser.ChangeStarts(lines);
        changeIndex = -1;
        adds.Text = "+" + lines.Count(l => l.Kind == DiffLineKind.Add);
        dels.Text = "-" + lines.Count(l => l.Kind == DiffLineKind.Del);
        if (lines.Count == 0) ShowMessage("差分はありません", false);
        Render();
    }

    private void ShowMessage(string text, bool error)
    {
        message.Text = text;
        message.Foreground = error ? Brushes.Crimson : Ui.Secondary();
        message.Visibility = Visibility.Visible;
    }

    private void UpdatePosition() =>
        position.Text = changeStarts.Count == 0 ? "" : $"{Math.Max(changeIndex + 1, 0)}/{changeStarts.Count}";

    // ---- rendering ----

    private static readonly Brush AddBg = Frozen(Color.FromArgb(0x2E, 0x28, 0xA7, 0x45));
    private static readonly Brush DelBg = Frozen(Color.FromArgb(0x2E, 0xD7, 0x26, 0x3D));
    private static readonly Brush HunkBg = Frozen(Color.FromArgb(0x1E, 0x2F, 0x7B, 0xD9));
    private static readonly Brush MetaBg = Frozen(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
    private static readonly Brush EmptyBg = Frozen(Color.FromArgb(0x10, 0x80, 0x80, 0x80));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static Brush KindBackground(DiffLineKind k) => k switch
    {
        DiffLineKind.Add => AddBg,
        DiffLineKind.Del => DelBg,
        DiffLineKind.Hunk => HunkBg,
        DiffLineKind.Meta => MetaBg,
        _ => Brushes.Transparent,
    };

    private static TextBlock LineNo(int? n) => new()
    {
        Text = n?.ToString() ?? "", Width = 48, TextAlignment = TextAlignment.Right, Padding = new Thickness(0, 0, 6, 0),
        Foreground = Ui.Secondary(),
    };

    private bool SideBySide => mode.SelectedIndex == 1;

    private void Render()
    {
        UpdatePosition();
        list.Items.Clear();
        if (lines.Count == 0) return;
        if (SideBySide)
        {
            var rows = DiffParser.SideBySide(lines.Where(l => !(l.Kind == DiffLineKind.Meta &&
                (l.Text.StartsWith("diff --git") || l.Text.StartsWith("index ") || l.Text.StartsWith("--- ") || l.Text.StartsWith("+++ ")))));
            foreach (var row in rows) list.Items.Add(new DiffRowItem(row));
        }
        else
        {
            foreach (var l in lines) list.Items.Add(new DiffLineItem(l));
        }
        list.ItemTemplateSelector = null;
        list.ItemTemplate = null;
        list.DisplayMemberPath = null;
        // Items render themselves through implicit templates below.
        list.Resources[new DataTemplateKey(typeof(DiffLineItem))] = UnifiedTemplate();
        list.Resources[new DataTemplateKey(typeof(DiffRowItem))] = SideTemplate();
    }

    private sealed class DiffLineItem(DiffLine line)
    {
        public DiffLine Line { get; } = line;
        public override string ToString() => Line.Text;
    }

    private sealed class DiffRowItem(SideRow row)
    {
        public SideRow Row { get; } = row;
        public override string ToString() =>
            Row.Header?.Text ?? $"{Row.Left?.Text ?? ""}\t{Row.Right?.Text ?? ""}";
    }

    private static DataTemplate UnifiedTemplate()
    {
        var t = new DataTemplate(typeof(DiffLineItem)) { VisualTree = new FrameworkElementFactory(typeof(UnifiedRow)) };
        t.VisualTree.SetBinding(UnifiedRow.LineProperty, new System.Windows.Data.Binding(nameof(DiffLineItem.Line)));
        return t;
    }

    private static DataTemplate SideTemplate()
    {
        var t = new DataTemplate(typeof(DiffRowItem)) { VisualTree = new FrameworkElementFactory(typeof(SideBySideRow)) };
        t.VisualTree.SetBinding(SideBySideRow.SideRowProperty, new System.Windows.Data.Binding(nameof(DiffRowItem.Row)));
        return t;
    }

    /// <summary>One line of the unified view.</summary>
    private sealed class UnifiedRow : Grid
    {
        public static readonly DependencyProperty LineProperty = DependencyProperty.Register(nameof(Line), typeof(DiffLine), typeof(UnifiedRow),
            new PropertyMetadata(null, (d, _) => ((UnifiedRow)d).Build()));
        public DiffLine? Line { get => (DiffLine?)GetValue(LineProperty); set => SetValue(LineProperty, value); }

        public UnifiedRow()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            ColumnDefinitions.Add(new ColumnDefinition());
        }

        private void Build()
        {
            Children.Clear();
            if (Line is not { } l) return;
            Background = DiffView.KindBackground(l.Kind);
            var a = LineNo(l.OldNo); var b = LineNo(l.NewNo);
            SetColumn(b, 1);
            var sign = new TextBlock
            {
                Text = l.Kind == DiffLineKind.Add ? "+" : l.Kind == DiffLineKind.Del ? "-" : " ",
                Foreground = l.Kind == DiffLineKind.Add ? Brushes.SeaGreen : Brushes.Crimson, TextAlignment = TextAlignment.Center,
            };
            SetColumn(sign, 2);
            var text = new TextBlock { Text = l.Text.Length == 0 ? " " : l.Text, TextWrapping = TextWrapping.Wrap };
            if (l.Kind == DiffLineKind.Hunk) text.Foreground = Ui.Accent();
            else if (l.Kind == DiffLineKind.Meta) text.Foreground = Ui.Secondary();
            SetColumn(text, 3);
            Children.Add(a); Children.Add(b); Children.Add(sign); Children.Add(text);
        }
    }

    /// <summary>One row of the side-by-side view.</summary>
    private sealed class SideBySideRow : Grid
    {
        public static readonly DependencyProperty SideRowProperty = DependencyProperty.Register(nameof(Row), typeof(SideRow), typeof(SideBySideRow),
            new PropertyMetadata(null, (d, _) => ((SideBySideRow)d).Build()));
        public SideRow? Row { get => (SideRow?)GetValue(SideRowProperty); set => SetValue(SideRowProperty, value); }

        public SideBySideRow()
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            ColumnDefinitions.Add(new ColumnDefinition());
        }

        private void Build()
        {
            Children.Clear();
            Background = Brushes.Transparent;
            if (Row is not { } r) return;
            if (r.Header is { } h)
            {
                var t = new TextBlock
                {
                    Text = h.Text.Length == 0 ? " " : h.Text, Padding = new Thickness(6, 0, 0, 0),
                    Foreground = h.Kind == DiffLineKind.Hunk ? Ui.Accent() : Ui.Secondary(), Background = DiffView.KindBackground(h.Kind),
                };
                SetColumnSpan(t, 3);
                Children.Add(t);
                return;
            }
            Children.Add(Side(r.Left, true));
            var div = new Border { Background = MetaBg };
            SetColumn(div, 1);
            Children.Add(div);
            var right = Side(r.Right, false);
            SetColumn(right, 2);
            Children.Add(right);
        }

        private static Grid Side(DiffLine? l, bool isLeft)
        {
            var g = new Grid { Background = l == null ? EmptyBg : l.Kind == DiffLineKind.Context ? Brushes.Transparent : DiffView.KindBackground(l.Kind) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(LineNo(isLeft ? l?.OldNo : l?.NewNo));
            var t = new TextBlock { Text = l == null || l.Text.Length == 0 ? " " : l.Text, TextWrapping = TextWrapping.Wrap };
            SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }
    }

    // ---- navigation / actions ----

    private void Go(int delta)
    {
        if (changeStarts.Count == 0) return;
        changeIndex = Math.Clamp(changeIndex + delta, 0, changeStarts.Count - 1);
        UpdatePosition();
        var id = changeStarts[changeIndex];
        object? target = list.Items.OfType<object>().FirstOrDefault(i => i switch
        {
            DiffLineItem li => li.Line.Id == id,
            DiffRowItem ri => ri.Row.Left?.Id == id || ri.Row.Right?.Id == id,
            _ => false,
        });
        if (target == null) return;
        // Scroll a little past the target so it lands near the middle.
        int idx = list.Items.IndexOf(target);
        list.ScrollIntoView(list.Items[Math.Min(list.Items.Count - 1, idx + 10)]);
        list.ScrollIntoView(target);
        list.SelectedItem = target;
    }

    private void CopySelection()
    {
        var items = list.SelectedItems.OfType<object>().OrderBy(i => list.Items.IndexOf(i)).Select(i => i.ToString());
        Shell.Copy(string.Join("\n", items));
    }

    private void OpenExternal()
    {
        if (AppSettings.Shared.ExternalDiff.Length == 0)
        {
            WindowManager.Alert("外部差分ツールが設定されていません", "設定 > 外部ツールで指定してください。", false);
            return;
        }
        var (l, r) = source.FilesForExternal(repo);
        ExternalTools.Diff(l, r, repo.Root);
    }
}
