using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Small layout helpers shared by the browser / tool views (BrowserViews.cs, ToolViews.cs).</summary>
internal static class BrowserToolKit
{
    /// <summary>Read-only, virtualized data grid with full-row selection.</summary>
    public static DataGrid NewGrid(DataGridSelectionMode mode = DataGridSelectionMode.Extended)
    {
        var g = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserReorderColumns = false, SelectionMode = mode, SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None,
            RowHeight = 26, EnableRowVirtualization = true,
        };
        VirtualizingPanel.SetIsVirtualizing(g, true);
        VirtualizingPanel.SetVirtualizationMode(g, VirtualizationMode.Recycling);
        return g;
    }

    private static DataGridLength Len(double? width) =>
        width.HasValue ? new DataGridLength(width.Value) : new DataGridLength(1, DataGridLengthUnitType.Star);

    /// <summary>Text column bound to <paramref name="path"/>. Sortable only when <paramref name="sortPath"/> is given.</summary>
    public static DataGridTemplateColumn TextColumn(string header, string path, double? width = null, bool mono = false,
        bool secondary = false, string? sortPath = null, string? brushPath = null)
    {
        var t = new FrameworkElementFactory(typeof(TextBlock));
        t.SetBinding(TextBlock.TextProperty, new Binding(path));
        t.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        t.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        if (mono)
        {
            t.SetValue(TextBlock.FontFamilyProperty, Ui.Mono);
            t.SetValue(TextBlock.FontSizeProperty, 11.5);
        }
        if (brushPath != null) t.SetBinding(TextBlock.ForegroundProperty, new Binding(brushPath));
        else if (secondary) t.SetValue(TextBlock.ForegroundProperty, Ui.Secondary());
        return new DataGridTemplateColumn
        {
            Header = header, Width = Len(width), CellTemplate = new DataTemplate { VisualTree = t },
            SortMemberPath = sortPath, CanUserSort = sortPath != null,
        };
    }

    /// <summary>Column with an icon (font glyph or image) followed by text (and an optional secondary note).</summary>
    public static DataGridTemplateColumn IconTextColumn(string header, string textPath, string? glyphPath = null, string? glyphBrushPath = null,
        string? imagePath = null, string? weightPath = null, string? notePath = null, double? width = null, string? sortPath = null)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        if (glyphPath != null)
        {
            var g = new FrameworkElementFactory(typeof(TextBlock));
            g.SetBinding(TextBlock.TextProperty, new Binding(glyphPath));
            if (glyphBrushPath != null) g.SetBinding(TextBlock.ForegroundProperty, new Binding(glyphBrushPath));
            g.SetValue(TextBlock.FontFamilyProperty, Ui.IconFont);
            g.SetValue(TextBlock.FontSizeProperty, 13.0);
            g.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            g.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(g);
        }
        if (imagePath != null)
        {
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding(imagePath));
            img.SetValue(FrameworkElement.WidthProperty, 16.0);
            img.SetValue(FrameworkElement.HeightProperty, 16.0);
            img.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            img.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(img);
        }
        var t = new FrameworkElementFactory(typeof(TextBlock));
        t.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        if (weightPath != null) t.SetBinding(TextBlock.FontWeightProperty, new Binding(weightPath));
        t.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        t.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(t);
        if (notePath != null)
        {
            var n = new FrameworkElementFactory(typeof(TextBlock));
            n.SetBinding(TextBlock.TextProperty, new Binding(notePath));
            n.SetValue(TextBlock.ForegroundProperty, Ui.Secondary());
            n.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            n.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(n);
        }
        return new DataGridTemplateColumn
        {
            Header = header, Width = Len(width), CellTemplate = new DataTemplate { VisualTree = panel },
            SortMemberPath = sortPath, CanUserSort = sortPath != null,
        };
    }

    /// <summary>Double click on a row → <paramref name="action"/>(row item). Enter does the same.</summary>
    public static void OnActivate<T>(DataGrid grid, Action<T> action) where T : class
    {
        grid.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<DataGridRow>(d) is { Item: T item }) action(item);
        };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && grid.SelectedItem is T item) { e.Handled = true; action(item); }
        };
    }

    /// <summary>Builds the context menu on demand from the current selection (null = no menu).</summary>
    public static void OnContextMenu(FrameworkElement element, Func<ContextMenu?> build)
    {
        element.ContextMenuOpening += (_, e) =>
        {
            element.ContextMenu = build();
            if (element.ContextMenu == null) e.Handled = true;
        };
    }

    public static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    /// <summary>Wraps a text box with a grey placeholder shown while it is empty.</summary>
    public static Grid Placeholder(TextBox box, string text, double? width = null)
    {
        var hint = new TextBlock
        {
            Text = text, Foreground = Ui.Secondary(), IsHitTestVisible = false, Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        void Update() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Update();
        Update();
        var g = new Grid();
        if (width.HasValue) g.Width = width.Value;
        g.Children.Add(box);
        g.Children.Add(hint);
        return g;
    }

    /// <summary>Segmented control (toggle-styled radio buttons). Calls <paramref name="changed"/> with the index.</summary>
    public static StackPanel Segmented(IReadOnlyList<object> labels, int selected, Action<int> changed)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var style = Application.Current.TryFindResource(typeof(ToggleButton)) as Style;
        var group = "seg-" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            var rb = new RadioButton { Content = labels[i], GroupName = group, IsChecked = i == selected, Margin = new Thickness(0, 0, 2, 0), MinWidth = 64 };
            if (style != null) rb.Style = style;
            rb.Checked += (_, _) => changed(index);
            p.Children.Add(rb);
        }
        return p;
    }

    /// <summary>Vertical radio group. Calls <paramref name="changed"/> with the index.</summary>
    public static StackPanel RadioGroup(IReadOnlyList<string> labels, int selected, Action<int> changed)
    {
        var p = new StackPanel();
        var group = "radio-" + Guid.NewGuid().ToString("N");
        for (int i = 0; i < labels.Count; i++)
        {
            int index = i;
            var rb = new RadioButton { Content = labels[i], GroupName = group, IsChecked = i == selected, Margin = new Thickness(0, 2, 0, 2) };
            rb.Checked += (_, _) => changed(index);
            p.Children.Add(rb);
        }
        return p;
    }

    /// <summary>Flat (borderless) button, like SwiftUI's .borderless style.</summary>
    public static Button Flat(object content, Action onClick)
    {
        var b = new Button
        {
            Content = content, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 4, 2), MinWidth = 0, MinHeight = 0,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Bottom bar: left items, right items.</summary>
    public static DockPanel Bar(IEnumerable<UIElement> left, IEnumerable<UIElement> right)
    {
        var d = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var r = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var e in right) { if (e is FrameworkElement f && f.Margin == default) f.Margin = new Thickness(8, 0, 0, 0); r.Children.Add(e); }
        DockPanel.SetDock(r, Dock.Right);
        d.Children.Add(r);
        var l = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var e in left)
        {
            if (e is FrameworkElement f) { if (f.Margin == default) f.Margin = new Thickness(0, 0, 6, 0); f.VerticalAlignment = VerticalAlignment.Center; }
            l.Children.Add(e);
        }
        DockPanel.SetDock(l, Dock.Left);
        d.Children.Add(l);
        return d;
    }

    public static Button CloseButton(GVView view, string text = "閉じる")
    {
        var b = new Button { Content = text, MinWidth = 90, IsCancel = true };
        b.Click += (_, _) => view.Handle.Close();
        return b;
    }

    public static Button Button(string text, Action onClick)
    {
        var b = new Button { Content = text };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var m = FileList.Item(header, action);
        m.IsEnabled = enabled;
        return m;
    }

    /// <summary>Byte count in the style of macOS ByteCountFormatter (.file, decimal units).</summary>
    public static string FormatSize(long n)
    {
        if (n < 0) return "";
        if (n < 1000) return $"{n} バイト";
        if (n < 1_000_000) return $"{Math.Round(n / 1000.0):0} KB";
        if (n < 1_000_000_000) return $"{n / 1_000_000.0:0.#} MB";
        return $"{n / 1_000_000_000.0:0.##} GB";
    }

    /// <summary>Root layout: top elements docked, bottom docked, the rest fills.</summary>
    public static DockPanel Layout(double margin, IEnumerable<UIElement> top, UIElement? fill, IEnumerable<UIElement> bottom)
    {
        var root = new DockPanel { Margin = new Thickness(margin) };
        foreach (var t in top) { DockPanel.SetDock(t, Dock.Top); root.Children.Add(t); }
        foreach (var b in bottom.Reverse()) { DockPanel.SetDock(b, Dock.Bottom); root.Children.Add(b); }
        root.Children.Add(fill ?? new Border());
        return root;
    }
}

// MARK: - Browse references

/// <summary>Lists branches, remote branches and tags; switch / rename / delete / merge / rebase from here.</summary>
public sealed class RefBrowserView : GVView
{
    public sealed class Row(RefDetail r)
    {
        public RefDetail Ref { get; } = r;
        public string Id => Ref.Id;
        public string Name => Ref.Name;
        public string KindLabel => Ref.KindLabel;
        public string Subject => Ref.Subject;
        public DateTimeOffset Date => Ref.Date;
        public string DateText => Ui.FormatDate(Ref.Date);
        public string Author => Ref.Author;
        public string UpstreamText => string.Join(" ", new[] { Ref.Upstream, Ref.Track }.Where(s => s.Length > 0));
        public string Glyph => Ref.Kind switch { RefKind.Tag => "", RefKind.Remote => "", _ => "" };
        public Brush GlyphBrush => Ref.Kind switch { RefKind.Tag => Brushes.Goldenrod, RefKind.Remote => Brushes.DarkOrange, _ => Brushes.SeaGreen };
        public FontWeight Weight => Ref.IsHead ? FontWeights.Bold : FontWeights.Normal;
    }

    private readonly Repo repo;
    private readonly ObservableCollection<Row> rows = [];
    private readonly ICollectionView view;
    private readonly DataGrid grid = BrowserToolKit.NewGrid();
    private readonly TextBox filter = new();
    private readonly TextBlock count = new() { FontSize = 12, Foreground = Ui.Secondary() };
    private readonly Button logButton, switchButton, renameButton, deleteButton;
    private int kind; // 0 all, 1 local, 2 remote, 3 tag
    private string? firstRemote;

    public RefBrowserView(Repo repo)
    {
        this.repo = repo;
        view = CollectionViewSource.GetDefaultView(rows);
        view.SortDescriptions.Add(new SortDescription(nameof(Row.Name), ListSortDirection.Ascending));
        view.Filter = o => o is Row r && Matches(r.Ref);

        var kinds = BrowserToolKit.Segmented(["すべて", "ローカル", "リモート", "タグ"], 0, i => { kind = i; Refilter(); });
        filter.TextChanged += (_, _) => Refilter();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var reload = Ui.IconButton("", "再読み込み", Load);
        DockPanel.SetDock(kinds, Dock.Left);
        DockPanel.SetDock(reload, Dock.Right);
        top.Children.Add(kinds);
        top.Children.Add(reload);
        var filterBox = BrowserToolKit.Placeholder(filter, "フィルタ");
        filterBox.Margin = new Thickness(8, 0, 0, 0);
        top.Children.Add(filterBox);

        var nameCol = BrowserToolKit.IconTextColumn("名前", nameof(Row.Name), nameof(Row.Glyph), nameof(Row.GlyphBrush),
            weightPath: nameof(Row.Weight), sortPath: nameof(Row.Name));
        nameCol.Width = new DataGridLength(1.3, DataGridLengthUnitType.Star);
        nameCol.SortDirection = ListSortDirection.Ascending;
        grid.Columns.Add(nameCol);
        grid.Columns.Add(BrowserToolKit.TextColumn("種類", nameof(Row.KindLabel), 64));
        grid.Columns.Add(BrowserToolKit.TextColumn("最新のコミット", nameof(Row.Subject)));
        grid.Columns.Add(BrowserToolKit.TextColumn("日時", nameof(Row.DateText), 130, sortPath: nameof(Row.Date)));
        grid.Columns.Add(BrowserToolKit.TextColumn("作成者", nameof(Row.Author), 110));
        grid.Columns.Add(BrowserToolKit.TextColumn("上流 / 状態", nameof(Row.UpstreamText), secondary: true));
        grid.ItemsSource = view;
        grid.SelectionChanged += (_, _) => UpdateButtons();
        BrowserToolKit.OnActivate<Row>(grid, r => ShowLog(r.Ref));
        BrowserToolKit.OnContextMenu(grid, () => Selected().Count > 0 ? Menu(Selected()) : null);

        logButton = BrowserToolKit.Button("ログ", () => { if (Selected() is [var r]) ShowLog(r); });
        switchButton = BrowserToolKit.Button("切り替え", () => { if (Selected() is [var r]) SwitchTo(r); });
        renameButton = BrowserToolKit.Button("名前を変更…", () => { if (Selected() is [var r]) Rename(r); });
        deleteButton = BrowserToolKit.Button("削除…", () => Delete(Selected()));
        var create = BrowserToolKit.Button("ブランチを作成…", () => WindowManager.Shared.Perform(GitAction.Branch, [repo.Root]));
        var bar = BrowserToolKit.Bar([logButton, switchButton, renameButton, deleteButton, count], [create, BrowserToolKit.CloseButton(this)]);

        Content = BrowserToolKit.Layout(14, [new RepoHeader(repo), top], grid, [bar]);
        OnRepoChanged(repo.Root, Load);
        UpdateButtons();
    }

    protected internal override void OnShown() => Load();

    private bool Matches(RefDetail r)
    {
        bool k = kind == 0 || (kind == 1 && r.Kind == RefKind.Local) || (kind == 2 && r.Kind == RefKind.Remote) || (kind == 3 && r.Kind == RefKind.Tag);
        var f = filter.Text;
        return k && (f.Length == 0 || r.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase) || r.Subject.Contains(f, StringComparison.CurrentCultureIgnoreCase));
    }

    private void Refilter()
    {
        view.Refresh();
        UpdateCount();
    }

    private void UpdateCount() =>
        count.Text = $"{rows.Count(r => Matches(r.Ref))} 件  (ダブルクリックでログ / 右クリックでその他の操作)";

    private List<RefDetail> Selected() => grid.SelectedItems.OfType<Row>().Select(r => r.Ref).ToList();

    private void UpdateButtons()
    {
        var sel = Selected();
        logButton.IsEnabled = sel.Count == 1;
        switchButton.IsEnabled = sel.Count == 1 && !sel[0].IsHead;
        renameButton.IsEnabled = sel.Count == 1 && sel[0].Kind == RefKind.Local;
        deleteButton.IsEnabled = sel.Count > 0 && !sel.Any(r => r.IsHead);
    }

    private async void Load()
    {
        var (refs, remote) = await Task.Run(() => (repo.RefsDetailed(), repo.Remotes().FirstOrDefault()));
        var selected = new HashSet<string>(Selected().Select(r => r.Id));
        // (DeferRefresh cannot be used here: an ObservableCollection may not change while a refresh is deferred.)
        rows.Clear();
        foreach (var r in refs) rows.Add(new Row(r));
        firstRemote = remote;
        foreach (var r in grid.Items.OfType<Row>())
            if (selected.Contains(r.Id)) grid.SelectedItems.Add(r);
        UpdateCount();
        UpdateButtons();
    }

    private void ShowLog(RefDetail r) =>
        WindowManager.Shared.Show($"GitVelto - ログ - {r.Name}", new LogView(repo, [], r.Name), 1120, 780, minWidth: 700, minHeight: 500);

    private void Run(string title, IReadOnlyList<ProgressStep> steps) =>
        WindowManager.Shared.RunProgress(title, repo.Root, steps, onFinish: _ => Load());

    private void SwitchTo(RefDetail r)
    {
        if (r.Kind == RefKind.Local) Run("切り替え", [ProgressStep.Git("switch", r.Name)]);
        else if (r.Kind == RefKind.Remote)
        {
            var shortName = string.Join("/", r.Name.Split('/').Skip(1));
            Run("切り替え", [ProgressStep.Git("switch", "-c", shortName, "--track", r.Name)]);
        }
        else Run("切り替え", [ProgressStep.Git(GitOps.Detach(r.Name))]);
    }

    private void Rename(RefDetail r)
    {
        if (WindowManager.Prompt("ブランチ名を変更", r.Name, r.Name) is { Length: > 0 } n && n != r.Name)
            Run("名前を変更", [ProgressStep.Git(GitOps.RenameBranch(r.Name, n))]);
    }

    /// <summary>Deletes local branches with -d, offering -D when some are not merged.</summary>
    private async Task DeleteLocalBranches(List<string> names)
    {
        var r = await repo.GitAsync(GitOps.DeleteBranches(names, false));
        if (!r.Ok && WindowManager.Confirm("マージされていないブランチがあります", r.Message + "\n\n強制的に削除しますか?", "強制削除", true))
            await repo.GitAsync(GitOps.DeleteBranches(names, true));
    }

    /// <summary>Deletes local branches, remote branches and tags (tags also from the first remote when chosen).</summary>
    private async void Delete(List<RefDetail> sel)
    {
        var locals = sel.Where(r => r.Kind == RefKind.Local && !r.IsHead).ToList();
        var remotes = sel.Where(r => r.Kind == RefKind.Remote).ToList();
        var tags = sel.Where(r => r.Kind == RefKind.Tag).ToList();
        if (locals.Count == 0 && remotes.Count == 0 && tags.Count == 0) return;
        var info = new List<string>();
        if (locals.Count > 0) info.Add("ローカルブランチ: " + string.Join(", ", locals.Select(r => r.Name)));
        if (remotes.Count > 0) info.Add("リモートブランチ (サーバー上から削除): " + string.Join(", ", remotes.Select(r => r.Name)));
        if (tags.Count > 0) info.Add("タグ: " + string.Join(", ", tags.Select(r => r.Name)));
        if (!WindowManager.Confirm($"{sel.Count} 件の参照を削除しますか?", string.Join("\n", info), "削除", true)) return;
        if (locals.Count > 0) await DeleteLocalBranches(locals.Select(r => r.Name).ToList());
        var steps = GitOps.DeleteRemoteBranches(remotes.Select(r => r.Name));
        if (tags.Count > 0)
        {
            steps.Add(ProgressStep.Git(GitOps.DeleteTags(tags.Select(r => r.Name))));
            if (firstRemote is { } remote && WindowManager.Confirm($"タグをリモート ({remote}) からも削除しますか?", "", "リモートからも削除"))
                steps.Add(ProgressStep.Git(GitOps.DeleteRemoteTags(tags.Select(r => r.Name), remote)));
        }
        if (steps.Count == 0) WindowManager.NotifyRepoChanged(repo.Root); // reloads this view too
        else Run("参照を削除", steps);
    }

    private ContextMenu Menu(List<RefDetail> sel)
    {
        var m = new ContextMenu();
        if (sel is [var r])
        {
            m.Items.Add(BrowserToolKit.Item("ログを表示", () => ShowLog(r)));
            m.Items.Add(BrowserToolKit.Item("切り替え/チェックアウト", () => SwitchTo(r), !r.IsHead));
            m.Items.Add(BrowserToolKit.Item("現在のブランチにマージ", () =>
            {
                if (WindowManager.Confirm($"{r.Name} を現在のブランチにマージしますか?", "", "マージ"))
                    Run("マージ", [ProgressStep.Git("merge", "--no-edit", r.Name)]);
            }, !r.IsHead));
            m.Items.Add(BrowserToolKit.Item("現在のブランチをこの参照にリベース", () =>
                WindowManager.Shared.Show($"GitVelto - リベース - {repo.Name}", new RebaseView(repo, r.Name), 820, 580), !r.IsHead));
            m.Items.Add(BrowserToolKit.Item("ここからブランチを作成…", () =>
                WindowManager.Shared.Show($"GitVelto - ブランチを作成 - {repo.Name}", new CreateRefView(repo, CreateRefKind.Branch, r.Name), 560, 370)));
            m.Items.Add(BrowserToolKit.Item("HEAD と比較", async () =>
            {
                var files = await Task.Run(() => repo.ChangedFiles(r.Name, "HEAD"));
                WindowManager.Shared.Show($"GitVelto - {r.Name} ↔ HEAD", new ChangedFilesView(repo, files, r.Name, "HEAD"), 640, 480);
            }));
            m.Items.Add(new Separator());
            if (r.Kind == RefKind.Local)
            {
                m.Items.Add(BrowserToolKit.Item("名前を変更…", () => Rename(r)));
                m.Items.Add(BrowserToolKit.Item("上流ブランチを設定…", () =>
                {
                    var def = r.Upstream.Length == 0 ? $"origin/{r.Name}" : r.Upstream;
                    if (WindowManager.Prompt($"上流ブランチ (例: origin/{r.Name})", "", def) is { Length: > 0 } n)
                        Run("上流ブランチを設定", [ProgressStep.Git(GitOps.SetUpstream(n, r.Name))]);
                }));
            }
            m.Items.Add(BrowserToolKit.Item("参照名をコピー", () => Shell.Copy(r.Name)));
            m.Items.Add(BrowserToolKit.Item("ハッシュをコピー", () => Shell.Copy(r.Hash)));
        }
        var locals = sel.Where(x => x.Kind == RefKind.Local && !x.IsHead).ToList();
        var remotes = sel.Where(x => x.Kind == RefKind.Remote).ToList();
        var tags = sel.Where(x => x.Kind == RefKind.Tag).ToList();
        if ((locals.Count > 0 || remotes.Count > 0 || tags.Count > 0) && m.Items.Count > 0) m.Items.Add(new Separator());
        if (locals.Count > 0)
        {
            m.Items.Add(BrowserToolKit.Item($"ブランチを削除 ({locals.Count})", async () =>
            {
                if (!WindowManager.Confirm($"{string.Join(", ", locals.Select(x => x.Name))} を削除しますか?", "", "削除", true)) return;
                await DeleteLocalBranches(locals.Select(x => x.Name).ToList());
                WindowManager.NotifyRepoChanged(repo.Root);
            }));
        }
        if (remotes.Count > 0)
        {
            m.Items.Add(BrowserToolKit.Item($"リモートブランチを削除 ({remotes.Count})", () =>
            {
                if (!WindowManager.Confirm($"リモートから {string.Join(", ", remotes.Select(x => x.Name))} を削除しますか?", "サーバー上のブランチが削除されます。", "削除", true)) return;
                Run("リモートブランチを削除", GitOps.DeleteRemoteBranches(remotes.Select(x => x.Name)));
            }));
        }
        if (tags.Count > 0)
        {
            var names = tags.Select(x => x.Name).ToList();
            m.Items.Add(BrowserToolKit.Item($"タグを削除 ({tags.Count})", () =>
            {
                if (!WindowManager.Confirm($"タグ {string.Join(", ", names)} を削除しますか?", "", "削除", true)) return;
                Run("タグを削除", [ProgressStep.Git(GitOps.DeleteTags(names))]);
            }));
            if (firstRemote is { } remote)
            {
                m.Items.Add(BrowserToolKit.Item($"タグをリモートからも削除 ({remote})", () =>
                {
                    if (!WindowManager.Confirm($"{remote} からタグ {string.Join(", ", names)} を削除しますか?", "", "削除", true)) return;
                    Run("リモートのタグを削除", [ProgressStep.Git(GitOps.DeleteRemoteTags(names, remote))]);
                }));
                m.Items.Add(BrowserToolKit.Item($"タグをプッシュ ({remote})", () =>
                    Run("タグをプッシュ", [ProgressStep.Git(new List<string> { "push", "--progress", remote }.Concat(names.Select(n => "refs/tags/" + n)))])));
            }
        }
        return m;
    }
}

// MARK: - Reflog

/// <summary>HEAD movement history; restore lost commits from here.</summary>
public sealed class ReflogView : GVView
{
    public sealed class Row(ReflogEntry e)
    {
        public ReflogEntry Entry { get; } = e;
        public string Selector => Entry.Selector;
        public string Short => Entry.Short;
        public string Subject => Entry.Subject;
        public string DateText => Ui.FormatDate(Entry.Date);
    }

    private readonly Repo repo;
    private readonly DataGrid grid = BrowserToolKit.NewGrid(DataGridSelectionMode.Single);
    private readonly TextBox filter = new();
    private List<ReflogEntry> entries = [];

    public ReflogView(Repo repo)
    {
        this.repo = repo;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(BrowserToolKit.Placeholder(filter, "フィルタ", 220));
        right.Children.Add(Ui.IconButton("", "再読み込み", Load));
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(Ui.Row(Ui.Heading("HEAD の移動履歴 (Reflog)"), new TermHelp("reflog")));
        filter.TextChanged += (_, _) => Fill();

        grid.Columns.Add(BrowserToolKit.TextColumn("参照", nameof(Row.Selector), 100, mono: true));
        grid.Columns.Add(BrowserToolKit.TextColumn("ハッシュ", nameof(Row.Short), 90, mono: true));
        grid.Columns.Add(BrowserToolKit.TextColumn("操作", nameof(Row.Subject)));
        grid.Columns.Add(BrowserToolKit.TextColumn("日時", nameof(Row.DateText), 130));
        BrowserToolKit.OnActivate<Row>(grid, r => ShowFiles(r.Entry));
        BrowserToolKit.OnContextMenu(grid, () => grid.SelectedItem is Row r ? Menu(r.Entry) : null);

        var bar = BrowserToolKit.Bar([Ui.Caption("誤ってリセット/削除したコミットもここから復元できます。")], [BrowserToolKit.CloseButton(this)]);
        Content = BrowserToolKit.Layout(14, [new RepoHeader(repo), header], grid, [bar]);
        OnRepoChanged(repo.Root, Load);
    }

    protected internal override void OnShown() => Load();

    private async void Load()
    {
        entries = await Task.Run(() => repo.Reflog());
        Fill();
    }

    private void Fill()
    {
        var f = filter.Text;
        var sel = (grid.SelectedItem as Row)?.Entry.Id;
        var shown = f.Length == 0 ? entries
            : entries.Where(e => e.Subject.Contains(f, StringComparison.CurrentCultureIgnoreCase) || e.Hash.StartsWith(f, StringComparison.OrdinalIgnoreCase)).ToList();
        var rows = shown.Select(e => new Row(e)).ToList();
        grid.ItemsSource = rows;
        if (sel != null) grid.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == sel);
    }

    private async void ShowFiles(ReflogEntry e)
    {
        var (parent, files) = await Task.Run(() =>
        {
            // A root commit has no parent: the list stays empty (same as the macOS version).
            var r = repo.Git(["rev-parse", "--verify", "-q", e.Hash + "^"]);
            var p = r.Ok ? r.Out.Trim() : "";
            return (p, p.Length == 0 ? [] : repo.ChangedFiles(p, e.Hash));
        });
        WindowManager.Shared.Show($"GitVelto - {e.Short}", new ChangedFilesView(repo, files, parent, e.Hash), 640, 480);
    }

    private ContextMenu Menu(ReflogEntry e)
    {
        var m = new ContextMenu();
        m.Items.Add(BrowserToolKit.Item("変更ファイルを表示", () => ShowFiles(e)));
        m.Items.Add(BrowserToolKit.Item("ハッシュをコピー", () => Shell.Copy(e.Hash)));
        m.Items.Add(new Separator());
        m.Items.Add(BrowserToolKit.Item("ここからブランチを作成…", () =>
            WindowManager.Shared.Show($"GitVelto - ブランチを作成 - {repo.Name}", new CreateRefView(repo, CreateRefKind.Branch, e.Hash), 560, 370)));
        m.Items.Add(BrowserToolKit.Item("このコミットに切り替え (HEAD 切り離し)", () =>
            WindowManager.Shared.RunProgress("チェックアウト", repo.Root, [ProgressStep.Git("switch", "--detach", e.Hash)])));
        m.Items.Add(BrowserToolKit.Item("このコミットまでリセット…", () =>
            WindowManager.Shared.Show("GitVelto - リセット", new ResetView(repo, new Commit(e.Hash, e.Short, [], "", "", e.Date, [], e.Subject, "")), 560, 380)));
        return m;
    }
}

// MARK: - Repository browser

/// <summary>Browses the tree of any revision (open / save / blame / compare files).</summary>
public sealed class RepoBrowserView : GVView
{
    public sealed class Row(TreeEntry e)
    {
        public TreeEntry Entry { get; } = e;
        public string Name => Entry.Name;
        public string Note => Entry.IsSubmodule ? "(サブモジュール)" : "";
        public ImageSource? Icon => Shell.Icon(Entry.Name, Entry.IsDir || Entry.IsSubmodule);
        public string SizeText => BrowserToolKit.FormatSize(Entry.Size);
        public string ShortHash => Entry.Hash.Length > 10 ? Entry.Hash[..10] : Entry.Hash;
    }

    private readonly Repo repo;
    private string revision;
    private string path;
    private readonly TextBox revInput = new();
    private readonly StackPanel crumbs = new() { Orientation = Orientation.Horizontal };
    private readonly Button upButton;
    private readonly TextBlock error = new() { Foreground = Brushes.Crimson, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
    private readonly DataGrid grid = BrowserToolKit.NewGrid(DataGridSelectionMode.Single);
    private readonly TextBlock footer = Ui.Caption("");
    private int loadSeq;

    public RepoBrowserView(Repo repo, string relPath, string? revision = null)
    {
        this.repo = repo;
        this.revision = string.IsNullOrWhiteSpace(revision) ? "HEAD" : revision;
        path = relPath.Trim('/');
        revInput.Text = this.revision;

        var show = BrowserToolKit.Button("表示", ApplyRevision);
        revInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; ApplyRevision(); } };
        var revRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var name = new TextBlock { Text = repo.Name, Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(name, Dock.Right);
        revRow.Children.Add(name);
        revRow.Children.Add(Ui.Row(new TextBlock { Text = "リビジョン:" }, BrowserToolKit.Placeholder(revInput, "HEAD / ブランチ / タグ / ハッシュ", 260), show, new TermHelp("hash")));

        upButton = Ui.IconButton("", "上の階層へ", GoUp);
        var crumbRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(upButton, Dock.Right);
        crumbRow.Children.Add(upButton);
        crumbRow.Children.Add(new ScrollViewer { Content = crumbs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });

        grid.Columns.Add(BrowserToolKit.IconTextColumn("名前", nameof(Row.Name), imagePath: nameof(Row.Icon), notePath: nameof(Row.Note)));
        grid.Columns.Add(BrowserToolKit.TextColumn("サイズ", nameof(Row.SizeText), 100));
        grid.Columns.Add(BrowserToolKit.TextColumn("ハッシュ", nameof(Row.ShortHash), 100, mono: true, secondary: true));
        BrowserToolKit.OnActivate<Row>(grid, r => Open(r.Entry));
        BrowserToolKit.OnContextMenu(grid, () => grid.SelectedItem is Row r ? Menu(r.Entry) : null);
        grid.PreviewKeyDown += (_, e) => { if (e.Key == Key.Back && path.Length > 0) { e.Handled = true; GoUp(); } };

        var bar = BrowserToolKit.Bar([footer], [BrowserToolKit.CloseButton(this)]);
        Content = BrowserToolKit.Layout(14, [revRow, crumbRow, error], grid, [bar]);
        RenderCrumbs();
    }

    protected internal override void OnShown() => Load();

    private void ApplyRevision()
    {
        var r = revInput.Text.Trim();
        revision = r.Length == 0 ? "HEAD" : r;
        path = "";
        Load();
    }

    private void Navigate(string p)
    {
        path = p;
        Load();
    }

    private void GoUp()
    {
        int i = path.LastIndexOf('/');
        Navigate(i < 0 ? "" : path[..i]);
    }

    private void RenderCrumbs()
    {
        crumbs.Children.Clear();
        var home = new StackPanel { Orientation = Orientation.Horizontal };
        home.Children.Add(Ui.Glyph("", 13, Ui.Accent()));
        home.Children.Add(new TextBlock { Text = repo.Name, Margin = new Thickness(5, 0, 0, 0), Foreground = Ui.Accent() });
        crumbs.Children.Add(BrowserToolKit.Flat(home, () => Navigate("")));
        var comps = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < comps.Length; i++)
        {
            var target = string.Join("/", comps.Take(i + 1));
            crumbs.Children.Add(Ui.Glyph("", 10));
            crumbs.Children.Add(BrowserToolKit.Flat(new TextBlock { Text = comps[i], Foreground = Ui.Accent() }, () => Navigate(target)));
        }
        upButton.IsEnabled = path.Length > 0;
    }

    private async void Load()
    {
        RenderCrumbs();
        var seq = ++loadSeq;
        string rev = revision, p = path;
        var (ok, list) = await Task.Run(() =>
        {
            var r = repo.Git(["rev-parse", "--verify", "-q", rev + "^{commit}"]);
            return r.Ok ? (true, repo.LsTree(rev, p)) : (false, new List<TreeEntry>());
        });
        if (seq != loadSeq) return;
        error.Text = ok ? "" : $"リビジョン '{rev}' が見つかりません";
        error.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        grid.ItemsSource = list.Select(e => new Row(e)).ToList();
        footer.Text = $"{list.Count} 項目 @ {rev}";
    }

    private async void Open(TreeEntry e)
    {
        if (e.IsDir) { Navigate(e.Path); return; }
        var rev = revision;
        if (await Task.Run(() => repo.ExportBlob(rev, e.Path)) is { } f) Shell.OpenFile(f);
    }

    private ContextMenu Menu(TreeEntry e)
    {
        var m = new ContextMenu();
        var rev = revision;
        m.Items.Add(BrowserToolKit.Item(e.IsDir ? "開く" : "開く (一時ファイル)", () => Open(e)));
        if (!e.IsDir)
        {
            m.Items.Add(BrowserToolKit.Item("名前を付けて保存…", async () =>
            {
                if (Shell.SaveFile("名前を付けて保存", e.Name, owner: OwnerWindow) is not { } file) return;
                var r = await repo.GitAsync(["show", $"{rev}:{e.Path}"]);
                if (!r.Ok) { WindowManager.Alert("保存できません", r.Message); return; }
                try { File.WriteAllBytes(file, r.Stdout); }
                catch (Exception ex) { WindowManager.Alert("保存できません", ex.Message); }
            }));
            m.Items.Add(BrowserToolKit.Item("Blame", () =>
                WindowManager.Shared.Show($"GitVelto - Blame - {e.Name}", new BlameView(repo, repo.Absolute(e.Path), rev), 1040, 740)));
            m.Items.Add(BrowserToolKit.Item("作業ツリーと比較", () =>
                WindowManager.Shared.ShowDiff(repo, new DiffSource.RevToWorking(rev, e.Path))));
        }
        m.Items.Add(BrowserToolKit.Item("ログを表示", () =>
            WindowManager.Shared.Show($"GitVelto - ログ - {e.Name}", new LogView(repo, [repo.Absolute(e.Path)], rev), 1120, 780, minWidth: 700, minHeight: 500)));
        m.Items.Add(BrowserToolKit.Item("パスをコピー", () => Shell.Copy(e.Path)));
        m.Items.Add(BrowserToolKit.Item("ハッシュをコピー", () => Shell.Copy(e.Hash)));
        return m;
    }
}
