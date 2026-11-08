using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>
/// Commit log (port of LogView.swift): commit graph, ref badges, filters, paging, working-tree row,
/// multi-selection, details + changed files, and the commit context menu operations.
/// </summary>
public sealed class LogView : GVView
{
    public const string WorktreeID = "__WORKTREE__";
    private const double RowHeight = 24;
    internal const double LaneWidth = 12;
    private const double GraphPadding = 4;

    // Split fractions are remembered for the session (Swift: @AppStorage logSplitV / logSplitH).
    private static double splitV = 0.55, splitH = 0.45;

    private readonly Repo repo;
    private readonly IReadOnlyList<string> paths;
    private readonly string? startRef;

    // ---- model ----
    private List<Commit> commits = [];
    private List<Commit> visible = [];
    private Dictionary<string, GraphRow> graph = [];
    private List<StatusEntry> worktree = [];
    private int maxLanes = 1;
    private int limit;
    private bool allBranches, wholeProject, useDates, loading, reloadPending, canLoadMore, bisecting, firstLoad = true;
    private string serverSearch = "";
    private string? selected;
    private HashSet<string> multi = [];
    private int filesGeneration;
    private bool suppressSelection, selectionUpdateQueued;

    // ---- UI ----
    private readonly DataGrid grid;
    private readonly DataGridTemplateColumn graphColumn;
    private readonly TextBox filterBox = new() { Width = 260, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox authorBox = new() { Width = 180, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly DatePicker sincePicker = new() { Width = 140, IsEnabled = false, SelectedDate = DateTime.Today.AddMonths(-1) };
    private readonly DatePicker untilPicker = new() { Width = 140, IsEnabled = false, SelectedDate = DateTime.Today };
    private readonly ProgressBar loadingBar = new() { IsIndeterminate = true, Width = 48, Height = 4, Margin = new Thickness(6, 0, 6, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button searchAllButton = new() { Content = "全履歴のメッセージから検索", Visibility = Visibility.Collapsed };
    private readonly Button clearSearchButton = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock countLabel = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button loadMoreButton = new() { Content = "さらに読み込む…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 6, 8, 6), Visibility = Visibility.Collapsed };
    private readonly StackPanel detailsPanel = new() { Margin = new Thickness(10) };
    private readonly ChangedFileGrid fileGrid = new();
    private TextBlock? targetLabel;

    public LogView(Repo repo, IReadOnlyList<string> paths) : this(repo, paths, null) { }

    public LogView(Repo repo, IReadOnlyList<string> paths, string? startRef)
    {
        this.repo = repo;
        this.paths = paths;
        this.startRef = startRef;
        limit = AppSettings.Shared.LogLimit;

        graphColumn = new DataGridTemplateColumn
        {
            Header = "グラフ", CanUserResize = false, Width = GraphWidth(),
            CellTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(GraphCell)) },
            CellStyle = CellStyle(new Thickness(0)),
        };
        grid = BuildCommitGrid();

        var root = new DockPanel();
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        var sep = new Separator { Margin = new Thickness(0) };
        DockPanel.SetDock(sep, Dock.Top);
        root.Children.Add(sep);
        root.Children.Add(BuildPanes());
        Content = root;

        fileGrid.Open += OpenDiff;
        fileGrid.MenuBuilder = FileMenu;

        OnRepoChanged(repo.Root, Reload);
        PreviewKeyDown += (_, e) =>
        {
            if ((e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None))
            {
                Reload();
                e.Handled = true;
            }
        };
        RenderDetails();
        UpdateCount();
    }

    protected internal override void OnShown() => Reload();

    // ================= layout =================

    private FrameworkElement BuildToolbar()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };

        // Row 1: repository, target, branches toggle, filter, reload.
        var row1 = new DockPanel { LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var allBox = new CheckBox { Content = "すべてのブランチ", VerticalAlignment = VerticalAlignment.Center };
        allBox.Click += (_, _) => { allBranches = allBox.IsChecked == true; Reload(); };
        right.Children.Add(allBox);
        right.Children.Add(new TermHelp("log") { Margin = new Thickness(2, 0, 8, 0) });
        right.Children.Add(WithHint(filterBox, "フィルタ (メッセージ/作成者/ハッシュ/参照)"));
        right.Children.Add(loadingBar);
        right.Children.Add(Ui.IconButton("", "再読み込み (Ctrl+R)", Reload));
        DockPanel.SetDock(right, Dock.Right);
        row1.Children.Add(right);

        var left = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true };
        left.Children.Add(new RepoHeader(repo) { Margin = new Thickness(0, 0, 12, 0), Width = 420, VerticalAlignment = VerticalAlignment.Center });
        if (startRef != null)
            left.Children.Add(new TextBlock { Text = $"リビジョン: {startRef}", Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        if (paths.Count > 0)
        {
            var text = "対象: " + string.Join(", ", paths.Select(repo.Relative));
            targetLabel = new TextBlock
            {
                Text = text, ToolTip = text, Foreground = Ui.Secondary(), MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            };
            left.Children.Add(targetLabel);
            var whole = new CheckBox { Content = "プロジェクト全体", VerticalAlignment = VerticalAlignment.Center };
            whole.Click += (_, _) =>
            {
                wholeProject = whole.IsChecked == true;
                targetLabel.TextDecorations = wholeProject ? TextDecorations.Strikethrough : null;
                Reload();
            };
            left.Children.Add(whole);
        }
        row1.Children.Add(left);
        panel.Children.Add(row1);

        // Row 2: author, period, server-side search, count, Reflog / refs.
        var row2 = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var right2 = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right2.Children.Add(countLabel);
        var reflog = new Button { Content = "Reflog", Margin = new Thickness(10, 0, 0, 0) };
        reflog.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Reflog, [repo.Root]);
        var refs = new Button { Content = "参照の一覧", Margin = new Thickness(8, 0, 0, 0) };
        refs.Click += (_, _) => WindowManager.Shared.Perform(GitAction.RefBrowser, [repo.Root]);
        right2.Children.Add(reflog);
        right2.Children.Add(refs);
        DockPanel.SetDock(right2, Dock.Right);
        row2.Children.Add(right2);

        var left2 = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true };
        var author = WithHint(authorBox, "作成者で絞り込み");
        author.Margin = new Thickness(0, 0, 10, 0);
        left2.Children.Add(author);
        authorBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Reload(); e.Handled = true; } };
        var dates = new CheckBox { Content = "期間:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        dates.Click += (_, _) =>
        {
            useDates = dates.IsChecked == true;
            sincePicker.IsEnabled = untilPicker.IsEnabled = useDates;
            Reload();
        };
        left2.Children.Add(dates);
        left2.Children.Add(sincePicker);
        left2.Children.Add(new TextBlock { Text = "〜", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        left2.Children.Add(untilPicker);
        sincePicker.SelectedDateChanged += (_, _) => { if (useDates) Reload(); };
        untilPicker.SelectedDateChanged += (_, _) => { if (useDates) Reload(); };
        searchAllButton.Margin = new Thickness(10, 0, 0, 0);
        searchAllButton.Click += (_, _) => { serverSearch = filterBox.Text; UpdateSearchButtons(); Reload(); };
        clearSearchButton.Margin = new Thickness(10, 0, 0, 0);
        clearSearchButton.Click += (_, _) =>
        {
            serverSearch = "";
            filterBox.Text = "";
            UpdateSearchButtons();
            Reload();
        };
        left2.Children.Add(searchAllButton);
        left2.Children.Add(clearSearchButton);
        row2.Children.Add(left2);
        panel.Children.Add(row2);

        filterBox.TextChanged += (_, _) =>
        {
            RebuildRows();
            UpdateSearchButtons();
        };
        return panel;
    }

    private FrameworkElement BuildPanes()
    {
        var splitterBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));

        // Top: commit list (+ "load more").
        var listPane = new DockPanel();
        DockPanel.SetDock(loadMoreButton, Dock.Bottom);
        loadMoreButton.Click += (_, _) => { limit += AppSettings.Shared.LogLimit; Reload(); };
        listPane.Children.Add(loadMoreButton);
        listPane.Children.Add(grid);

        // Bottom: details | files.
        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(splitH, GridUnitType.Star), MinWidth = 240 });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - splitH, GridUnitType.Star), MinWidth = 240 });
        var details = new ScrollViewer { Content = detailsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        bottom.Children.Add(details);
        var hSplit = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, Background = splitterBrush };
        Grid.SetColumn(hSplit, 1);
        bottom.Children.Add(hSplit);
        Grid.SetColumn(fileGrid, 2);
        bottom.Children.Add(fileGrid);
        hSplit.DragCompleted += (_, _) =>
        {
            double a = bottom.ColumnDefinitions[0].ActualWidth, b = bottom.ColumnDefinitions[2].ActualWidth;
            if (a + b > 0) splitH = a / (a + b);
        };

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(splitV, GridUnitType.Star), MinHeight = 160 });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - splitV, GridUnitType.Star), MinHeight = 140 });
        main.Children.Add(listPane);
        var vSplit = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows, Background = splitterBrush };
        Grid.SetRow(vSplit, 1);
        main.Children.Add(vSplit);
        Grid.SetRow(bottom, 2);
        main.Children.Add(bottom);
        vSplit.DragCompleted += (_, _) =>
        {
            double a = main.RowDefinitions[0].ActualHeight, b = main.RowDefinitions[2].ActualHeight;
            if (a + b > 0) splitV = a / (a + b);
        };
        return main;
    }

    private DataGrid BuildCommitGrid()
    {
        var g = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserSortColumns = false, CanUserReorderColumns = false, CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None,
            RowHeight = RowHeight, EnableRowVirtualization = true, BorderThickness = new Thickness(0),
        };
        VirtualizingPanel.SetIsVirtualizing(g, true);
        VirtualizingPanel.SetVirtualizationMode(g, VirtualizationMode.Recycling);

        g.Columns.Add(graphColumn);
        g.Columns.Add(new DataGridTemplateColumn
        {
            Header = "メッセージ", Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 120,
            CellTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(MessageCell)) },
        });
        g.Columns.Add(TextColumn("作成者", nameof(LogRow.Author), 150, mono: false));
        g.Columns.Add(TextColumn("日時", nameof(LogRow.DateText), 130, mono: false));
        g.Columns.Add(TextColumn("ハッシュ", nameof(LogRow.Short), 80, mono: true));

        // Working-tree row: light orange tint (Swift: Color.orange.opacity(0.08)).
        var wtBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x95, 0x00));
        wtBrush.Freeze();
        g.LoadingRow += (_, e) =>
        {
            if (e.Row.Item is LogRow { IsWorktree: true }) e.Row.Background = wtBrush;
            else e.Row.ClearValue(BackgroundProperty);
        };
        g.SelectionChanged += (_, _) => QueueSelectionUpdate();
        g.CurrentCellChanged += (_, _) => QueueSelectionUpdate();
        g.ContextMenuOpening += (_, e) =>
        {
            var src = e.OriginalSource as DependencyObject;
            if (src != null && FindParent<DataGridColumnHeader>(src) != null) { e.Handled = true; return; }
            var row = src != null && FindParent<DataGridRow>(src) is { Item: LogRow r } ? r : PrimaryRow();
            g.ContextMenu = row == null ? null : row.IsWorktree ? WorktreeMenu() : CommitMenu(row.Commit!);
            if (g.ContextMenu == null) e.Handled = true;
        };
        return g;
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width, bool mono)
    {
        var style = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        if (mono)
        {
            style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, Ui.Mono));
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));
        }
        return new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width, ElementStyle = style };
    }

    private static Style CellStyle(Thickness padding)
    {
        var baseStyle = Application.Current?.TryFindResource(typeof(DataGridCell)) as Style;
        var s = baseStyle != null ? new Style(typeof(DataGridCell), baseStyle) : new Style(typeof(DataGridCell));
        s.Setters.Add(new Setter(PaddingProperty, padding));
        s.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        return s;
    }

    /// <summary>TextBox with a placeholder hint shown while empty.</summary>
    private static Grid WithHint(TextBox box, string hint)
    {
        var t = new TextBlock
        {
            Text = hint, Foreground = Ui.Secondary(), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        box.TextChanged += (_, _) => t.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(box);
        g.Children.Add(t);
        System.Windows.Automation.AutomationProperties.SetName(box, hint);
        return g;
    }

    private DataGridLength GraphWidth() => new(maxLanes * LaneWidth + 6 + GraphPadding * 2);

    // ================= loading =================

    private bool ShowsWorktree => startRef == null && worktree.Count > 0;
    private bool FilterEmpty => filterBox.Text.Trim().Length == 0;
    private Commit? SelectedCommit => selected == null ? null : commits.FirstOrDefault(c => c.Hash == selected);
    private List<Commit> SelectedCommits => commits.Where(c => multi.Contains(c.Hash)).ToList();

    private async void Reload()
    {
        if (loading) { reloadPending = true; return; }
        loading = true;
        loadingBar.Visibility = Visibility.Visible;
        IReadOnlyList<string> p = wholeProject ? Array.Empty<string>() : paths;
        bool all = allBranches;
        int lim = limit;
        var rev = startRef;
        DateTimeOffset? since = useDates && sincePicker.SelectedDate is { } s ? new DateTimeOffset(s.Date) : null;
        // The date pickers have no time: include the whole "until" day.
        DateTimeOffset? until = useDates && untilPicker.SelectedDate is { } u ? new DateTimeOffset(u.Date.AddDays(1).AddSeconds(-1)) : null;
        var author = authorBox.Text.Trim();
        var search = serverSearch;
        try
        {
            var r = await Task.Run(() =>
            {
                var list = repo.Log(lim, 0, all, p, search, rev, since, until, author);
                var rows = CommitGraph.Compute(list);
                var dict = new Dictionary<string, GraphRow>(rows.Count);
                int maxL = 1;
                foreach (var row in rows)
                {
                    dict[row.Hash] = row;
                    maxL = Math.Max(maxL, Math.Max(row.Top.Count, Math.Max(row.Bottom.Count, row.Node + 1)));
                }
                var wt = rev == null ? repo.Status(p, "all") : [];
                return (list, dict, maxL, wt, bis: repo.IsBisecting);
            });
            bisecting = r.bis;
            commits = r.list;
            graph = r.dict;
            maxLanes = Math.Min(r.maxL, 24);
            canLoadMore = r.list.Count >= lim;
            worktree = r.wt;
            graphColumn.Width = GraphWidth();

            multi.IntersectWith(commits.Select(c => c.Hash));
            RebuildRows();
            if (selected == null || (selected != WorktreeID && !commits.Any(c => c.Hash == selected)) || (selected == WorktreeID && !ShowsWorktree))
            {
                SelectId(commits.FirstOrDefault()?.Hash);
            }
            else
            {
                RenderDetails();
                if (selected == WorktreeID) LoadFiles();
            }
            if (firstLoad)
            {
                firstLoad = false;
                if (PrimaryRow() is { } pr) grid.ScrollIntoView(pr);
                grid.Focus();
            }
        }
        catch (Exception e)
        {
            WindowManager.Alert("ログを読み込めません", e.Message);
        }
        finally
        {
            loading = false;
            loadingBar.Visibility = Visibility.Collapsed;
            if (reloadPending)
            {
                reloadPending = false;
                Reload();
            }
        }
    }

    /// <summary>Applies the client-side filter and refills the grid, keeping the selection.</summary>
    private void RebuildRows()
    {
        var f = filterBox.Text.Trim();
        visible = f.Length == 0 ? commits : commits.Where(c =>
            c.Subject.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            c.Body.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            c.Author.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            c.Hash.StartsWith(f.ToLowerInvariant(), StringComparison.Ordinal) ||
            string.Concat(c.Refs).Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        bool showGraph = f.Length == 0;

        var rows = new List<LogRow>(visible.Count + 1);
        if (ShowsWorktree && showGraph) rows.Add(new LogRow(null, null, worktree.Count));
        foreach (var c in visible) rows.Add(new LogRow(c, showGraph ? graph.GetValueOrDefault(c.Hash) : null, 0));

        suppressSelection = true;
        try
        {
            grid.ItemsSource = rows;
            LogRow? primary = null;
            foreach (var r in rows)
            {
                if (r.Id == selected) primary = r;
                if (r.Id == selected || multi.Contains(r.Id)) grid.SelectedItems.Add(r);
            }
            if (primary != null) grid.CurrentCell = new DataGridCellInfo(primary, grid.Columns[1]);
        }
        finally { suppressSelection = false; }

        graphColumn.Visibility = showGraph ? Visibility.Visible : Visibility.Collapsed;
        loadMoreButton.Visibility = canLoadMore && showGraph ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount();
    }

    private void UpdateCount() =>
        countLabel.Text = $"{visible.Count} コミット" + (multi.Count > 1 ? $" / {multi.Count} 個選択" : "");

    private void UpdateSearchButtons()
    {
        searchAllButton.Visibility = filterBox.Text.Length > 0 && serverSearch != filterBox.Text ? Visibility.Visible : Visibility.Collapsed;
        clearSearchButton.Visibility = serverSearch.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        clearSearchButton.Content = $"検索を解除: {serverSearch}";
    }

    // ================= selection =================

    private LogRow? PrimaryRow() => (grid.ItemsSource as List<LogRow>)?.FirstOrDefault(r => r.Id == selected);

    /// <summary>Programmatic single selection (Swift: model.select(hash)).</summary>
    private void SelectId(string? id)
    {
        selected = id;
        multi = id == null || id == WorktreeID ? [] : [id];
        suppressSelection = true;
        try
        {
            grid.SelectedItems.Clear();
            if (PrimaryRow() is { } row)
            {
                grid.SelectedItem = row;
                grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[1]);
                grid.ScrollIntoView(row);
            }
        }
        finally { suppressSelection = false; }
        RenderDetails();
        LoadFiles();
        UpdateCount();
    }

    private void QueueSelectionUpdate()
    {
        if (suppressSelection || selectionUpdateQueued) return;
        selectionUpdateQueued = true;
        // Deferred: the current cell is updated after SelectionChanged for mouse clicks.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            selectionUpdateQueued = false;
            UpdateSelectionFromGrid();
        });
    }

    /// <summary>Maps the grid selection (Ctrl/Shift multi-select) to selected + multi.</summary>
    private void UpdateSelectionFromGrid()
    {
        if (suppressSelection) return;
        var rows = grid.SelectedItems.OfType<LogRow>().ToList();
        if (rows.Count == 0) return; // e.g. the selected commit is hidden by the filter: keep the details
        var primary = grid.CurrentItem as LogRow;
        if (primary == null || !rows.Contains(primary)) primary = rows[^1];
        multi = rows.Where(r => !r.IsWorktree).Select(r => r.Id).ToHashSet();
        bool changed = primary.Id != selected;
        selected = primary.Id;
        if (selected == WorktreeID && rows.Count == 1) multi.Clear();
        RenderDetails();
        if (changed) LoadFiles();
        UpdateCount();
    }

    // ================= files =================

    private async void LoadFiles()
    {
        int gen = ++filesGeneration;
        if (selected == WorktreeID)
        {
            fileGrid.SetFiles(worktree.Select(e => new ChangedFile(
                e.State == FileState.Untracked ? "?" : (e.X == ' ' ? e.Y : e.X).ToString(), e.Path, e.OrigPath)));
            return;
        }
        if (SelectedCommit is not { } c)
        {
            fileGrid.SetFiles([]);
            return;
        }
        fileGrid.SetFiles([]);
        try
        {
            var f = await Task.Run(() => repo.ChangedFiles(c));
            if (gen == filesGeneration && selected == c.Hash) fileGrid.SetFiles(f);
        }
        catch { /* repository vanished etc. */ }
    }

    private void OpenDiff(ChangedFile f)
    {
        if (selected == WorktreeID)
        {
            if (worktree.FirstOrDefault(e => e.Path == f.Path) is { } e)
                WindowManager.Shared.ShowDiff(repo, new DiffSource.WorkingTree(e.Path, e.OrigPath, e.State));
            return;
        }
        if (SelectedCommit is not { } c) return;
        WindowManager.Shared.ShowDiff(repo, new DiffSource.CommitDiff(c.Hash, c.Parents.FirstOrDefault(), f.Path, f.OldPath));
    }

    private ContextMenu? FileMenu(IReadOnlyList<ChangedFile> sel)
    {
        if (sel.Count == 0) return null;
        var f = sel[0];
        var abs = repo.Absolute(f.Path);
        var m = new ContextMenu();
        m.Items.Add(FileList.Item("差分を表示", () => OpenDiff(f), ""));
        m.Items.Add(FileList.Item("このファイルのログ", () => WindowManager.Shared.Perform(GitAction.Log, [abs]), ""));
        m.Items.Add(FileList.Item("Blame", () => WindowManager.Shared.Perform(GitAction.Blame, [abs]), ""));
        m.Items.Add(FileList.Item("エクスプローラーで表示", () => Shell.Reveal([abs]), ""));
        m.Items.Add(FileList.Item("パスをコピー", () => Shell.Copy(string.Join("\n", sel.Select(x => x.Path))), ""));
        return m;
    }

    // ================= details =================

    private void RenderDetails()
    {
        detailsPanel.Children.Clear();
        if (selected == WorktreeID)
        {
            detailsPanel.Children.Add(Ui.Heading("コミットされていない変更"));
            detailsPanel.Children.Add(new TextBlock
            {
                Text = $"{worktree.Count} 個のファイルが変更されています。右側のファイルをダブルクリックすると差分を表示します。",
                Foreground = Ui.Secondary(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8),
            });
            var commit = new Button { Content = "コミット…" };
            commit.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Commit, [repo.Root]);
            var status = new Button { Content = "変更をチェック…" };
            status.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Status, [repo.Root]);
            detailsPanel.Children.Add(Ui.Row(commit, status));
            return;
        }
        if (SelectedCommit is not { } c)
        {
            detailsPanel.Children.Add(new TextBlock { Text = "コミットを選択してください", Foreground = Ui.Secondary(), Margin = new Thickness(6) });
            return;
        }

        detailsPanel.Children.Add(ActionBar(c));
        detailsPanel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });

        var copy = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 12 }, ToolTip = "ハッシュをコピー",
            Padding = new Thickness(4, 2, 4, 2), MinWidth = 0, MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Click += (_, _) => Shell.Copy(c.Hash);
        detailsPanel.Children.Add(Field("コミット:", SelText(c.Hash, mono: true), copy));
        if (c.Parents.Count > 0)
            detailsPanel.Children.Add(Field("親:", SelText(string.Join(", ", c.Parents.Select(x => x[..Math.Min(10, x.Length)])), mono: true)));
        detailsPanel.Children.Add(Field("作成者:", SelText($"{c.Author} <{c.Email}>")));
        detailsPanel.Children.Add(Field("日時:", SelText(c.DateText)));
        if (c.Refs.Count > 0)
        {
            var badges = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var r in c.Refs) badges.Children.Add(RefBadge(r));
            detailsPanel.Children.Add(badges);
        }
        detailsPanel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
        var subject = SelText(c.Subject, size: 15);
        subject.FontWeight = FontWeights.SemiBold;
        detailsPanel.Children.Add(subject);
        if (c.Body.Length > 0)
        {
            var body = SelText(c.Body);
            body.Margin = new Thickness(0, 6, 0, 0);
            detailsPanel.Children.Add(body);
        }
    }

    private static StackPanel Field(string label, params UIElement[] values)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        p.Children.Add(new TextBlock { Text = label, Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        foreach (var v in values) p.Children.Add(v);
        return p;
    }

    /// <summary>Read-only, selectable text (Swift: .textSelection(.enabled)).</summary>
    private static TextBox SelText(string text, bool mono = false, double size = 12) => new()
    {
        Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        Padding = new Thickness(0), MinHeight = 0, MinWidth = 0, TextWrapping = TextWrapping.Wrap, FontSize = size,
        FontFamily = mono ? Ui.Mono : SystemFonts.MessageFontFamily, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Buttons for the selected commit(s), shown above the details (same actions as the context menu).</summary>
    private FrameworkElement ActionBar(Commit c)
    {
        var sel = multi.Contains(c.Hash) ? SelectedCommits : [c];
        var bar = new WrapPanel();
        void Add(string title, string help, Action a)
        {
            var b = new Button { Content = title, ToolTip = help, Padding = new Thickness(8, 3, 8, 3), MinWidth = 0, Margin = new Thickness(0, 0, 6, 4), FontSize = 12 };
            b.Click += (_, _) => a();
            bar.Children.Add(b);
        }
        string Sum(string key) => Glossary.Entry(key)?.Summary ?? "";
        Add("ブランチ…", "このコミットからブランチを作成", () => CreateRef(CreateRefKind.Branch, c));
        Add("タグ…", "このコミットにタグを作成", () => CreateRef(CreateRefKind.Tag, c));
        Add("切り替え", "このコミットに切り替え (HEAD 切り離し)", () => Detach(c));
        Add("リセット…", "現在のブランチをこのコミットまでリセット — " + Sum("reset"), () => Reset(c));
        Add("リバート", "このコミットを打ち消すコミットを作成 — " + Sum("revert"), () => Revert(c));
        Add(sel.Count > 1 ? $"チェリーピック ({sel.Count})" : "チェリーピック", "現在のブランチにチェリーピック — " + Sum("cherry-pick"), () => CherryPick(sel));
        bar.Children.Add(new TermHelp("cherry-pick") { Margin = new Thickness(0, 0, 0, 4) });
        return bar;
    }

    // ================= context menus =================

    private ContextMenu WorktreeMenu()
    {
        var m = new ContextMenu();
        m.Items.Add(FileList.Item("コミット…", () => WindowManager.Shared.Perform(GitAction.Commit, [repo.Root]), GitAction.Commit.Glyph()));
        m.Items.Add(FileList.Item("変更をチェック…", () => WindowManager.Shared.Perform(GitAction.Status, [repo.Root]), GitAction.Status.Glyph()));
        m.Items.Add(FileList.Item("変更を退避 (stash)…", () => WindowManager.Shared.Perform(GitAction.StashSave, [repo.Root]), GitAction.StashSave.Glyph()));
        return m;
    }

    private ContextMenu CommitMenu(Commit c)
    {
        var m = new ContextMenu();
        var sel = multi.Contains(c.Hash) ? SelectedCommits : [c];
        void Add(string header, Action a, string? glyph = null) => m.Items.Add(FileList.Item(header, a, glyph));
        void Sep() => m.Items.Add(new Separator());

        if (sel.Count == 2)
        {
            Add("選択した 2 つのコミットを比較", async () =>
            {
                var ordered = Enumerable.Reverse(sel).ToList(); // log order is newest first
                var files = await Task.Run(() => repo.ChangedFiles(ordered[0].Hash, ordered[1].Hash));
                WindowManager.Shared.Show($"GitVelto - {ordered[0].Short} ↔ {ordered[1].Short}",
                    new ChangedFilesView(repo, files, ordered[0].Hash, ordered[1].Hash), 640, 480);
            }, "");
        }
        if (sel.Count > 1)
        {
            Add($"選択した {sel.Count} コミットのパッチを作成…", () => ShowCreatePatch(sel.Select(x => x.Hash).ToList()), GitAction.CreatePatch.Glyph());
            Add($"選択した {sel.Count} コミットをチェリーピック", () => CherryPick(sel));
            Sep();
        }
        Add("ハッシュをコピー", () => Shell.Copy(string.Join("\n", sel.Select(x => x.Hash))), "");
        Add("メッセージをコピー", () => Shell.Copy(c.Subject + (c.Body.Length == 0 ? "" : "\n\n" + c.Body)));
        Sep();
        Add("このコミットからブランチを作成…", () => CreateRef(CreateRefKind.Branch, c), GitAction.Branch.Glyph());
        Add("このコミットにタグを作成…", () => CreateRef(CreateRefKind.Tag, c), GitAction.Tag.Glyph());
        Add("このコミットに切り替え (HEAD 切り離し)", () => Detach(c), GitAction.SwitchBranch.Glyph());
        Add("このコミットまでリセット…", () => Reset(c));
        Sep();
        Add("このコミットを打ち消す (revert)", () => Revert(c), GitAction.Revert.Glyph());
        Add("チェリーピック", () => CherryPick([c]));
        Add("現在のブランチをこのコミットへリベース…", () =>
            WindowManager.Shared.Show("GitVelto - リベース", new RebaseView(repo, c.Hash), 820, 580), GitAction.Rebase.Glyph());
        Sep();
        Add("HEAD と比較 (変更ファイル一覧)", async () =>
        {
            var files = await Task.Run(() => repo.ChangedFiles(c.Hash, "HEAD"));
            WindowManager.Shared.Show($"GitVelto - {c.Short} ↔ HEAD", new ChangedFilesView(repo, files, c.Hash, "HEAD"), 640, 480);
        }, "");
        Add("このリビジョンをリポジトリブラウザで開く", () =>
            WindowManager.Shared.Show($"GitVelto - リポジトリブラウザ - {c.Short}",
                new RepoBrowserView(repo, "", c.Hash), 800, 580),
            GitAction.RepoBrowser.Glyph());
        Add("このリビジョンをエクスポート…", () =>
            WindowManager.Shared.Show("GitVelto - エクスポート",
                new ExportView(repo, c.Hash), 560, 320),
            GitAction.Export.Glyph());
        Add("パッチを作成…", () => ShowCreatePatch([c.Hash]), GitAction.CreatePatch.Glyph());
        Sep();
        if (bisecting)
        {
            Add("二分探索: このコミットは良い", () => Bisect("good", c), GitAction.BisectGood.Glyph());
            Add("二分探索: このコミットは悪い", () => Bisect("bad", c), GitAction.BisectBad.Glyph());
        }
        else
        {
            Add("二分探索を開始 (このコミットを良いとする)", () =>
                WindowManager.Shared.Show("GitVelto - 二分探索",
                    new BisectStartView(repo, c.Hash, "HEAD"), 560, 320));
            Add("二分探索を開始 (このコミットを悪いとする)", () =>
                WindowManager.Shared.Show("GitVelto - 二分探索",
                    new BisectStartView(repo, "", c.Hash), 560, 320));
        }
        return m;
    }

    // ================= commit actions (context menu + action bar) =================

    private void ShowCreatePatch(List<string> hashes) =>
        WindowManager.Shared.Show("GitVelto - パッチを作成",
            new CreatePatchView(repo, hashes), 560, 400);

    private void CreateRef(CreateRefKind kind, Commit c) =>
        WindowManager.Shared.Show(kind == CreateRefKind.Branch ? "GitVelto - ブランチを作成" : "GitVelto - タグを作成",
            new CreateRefView(repo, kind, c.Hash), 560, kind == CreateRefKind.Branch ? 370 : 430);

    private void Detach(Commit c)
    {
        if (WindowManager.Confirm($"{c.Short} をチェックアウトしますか?", "HEAD が切り離された状態になります。"))
            WindowManager.Shared.RunProgress("チェックアウト", repo.Root, [ProgressStep.Git(GitOps.Detach(c.Hash))]);
    }

    private void Reset(Commit c) =>
        WindowManager.Shared.Show("GitVelto - リセット", new ResetView(repo, c), 520, 330);

    private void Revert(Commit c)
    {
        if (WindowManager.Confirm($"{c.Short} を打ち消すコミットを作成しますか?", c.Subject))
            WindowManager.Shared.RunProgress("リバート", repo.Root, [ProgressStep.Git(GitOps.RevertCommit(c))]);
    }

    private void CherryPick(List<Commit> sel)
    {
        if (sel.Count == 0) return;
        var msg = sel.Count == 1 ? $"{sel[0].Short} を現在のブランチにチェリーピックしますか?" : $"{sel.Count} 個のコミットを現在のブランチにチェリーピックしますか?";
        if (WindowManager.Confirm(msg, sel.Count == 1 ? sel[0].Subject : ""))
            WindowManager.Shared.RunProgress("チェリーピック", repo.Root, [ProgressStep.Git(GitOps.CherryPick(sel))]);
    }

    private void Bisect(string verb, Commit c) =>
        WindowManager.Shared.RunProgress("二分探索", repo.Root, [ProgressStep.Git("bisect", verb, c.Hash)]);

    // The repository reloads through OnRepoChanged (RunProgress notifies on finish), like model.reload() in Swift.

    // ================= helpers =================

    internal static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private static readonly Dictionary<string, (Brush Fill, Brush Stroke)> BadgeBrushes = [];

    private static (Brush Fill, Brush Stroke) BadgeColors(Color c)
    {
        var key = c.ToString();
        if (BadgeBrushes.TryGetValue(key, out var b)) return b;
        var fill = new SolidColorBrush(Color.FromArgb(0x38, c.R, c.G, c.B));
        var stroke = new SolidColorBrush(Color.FromArgb(0xB3, c.R, c.G, c.B));
        fill.Freeze();
        stroke.Freeze();
        return BadgeBrushes[key] = (fill, stroke);
    }

    private static readonly Color BadgeGreen = Color.FromRgb(0x34, 0xC7, 0x59);
    private static readonly Color BadgeRed = Color.FromRgb(0xFF, 0x3B, 0x30);
    private static readonly Color BadgeYellow = Color.FromRgb(0xFF, 0xCC, 0x00);
    private static readonly Color BadgeOrange = Color.FromRgb(0xFF, 0x95, 0x00);

    /// <summary>Colored label for a ref name from %D (HEAD -> x, tag: x, remote/x, branch).</summary>
    internal static Border RefBadge(string r)
    {
        var (text, color, bold) =
            r.StartsWith("HEAD -> ") ? (r[8..], BadgeGreen, true) :
            r == "HEAD" ? ("HEAD", BadgeRed, true) :
            r.StartsWith("tag: ") ? (r[5..], BadgeYellow, false) :
            r.Contains('/') ? (r, BadgeOrange, false) :
            (r, BadgeGreen, false);
        var (fill, stroke) = BadgeColors(color);
        return new Border
        {
            Background = fill, BorderBrush = stroke, BorderThickness = new Thickness(0.8), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 10, FontWeight = bold ? FontWeights.Bold : FontWeights.Medium },
        };
    }

    // ================= row / cell types =================

    /// <summary>One row of the commit list (a commit, or the working-tree changes row).</summary>
    public sealed class LogRow
    {
        public LogRow(Commit? commit, GraphRow? graph, int worktreeCount)
        {
            Commit = commit;
            Graph = graph;
            WorktreeCount = worktreeCount;
        }

        public Commit? Commit { get; }
        public GraphRow? Graph { get; }
        public int WorktreeCount { get; }
        public bool IsWorktree => Commit == null;
        public string Id => Commit?.Hash ?? WorktreeID;
        public string Author => Commit?.Author ?? "";
        public string DateText => Commit?.DateText ?? "";
        public string Short => Commit?.Short ?? "";
    }

    /// <summary>Message column: ref badges followed by the subject.</summary>
    public sealed class MessageCell : DockPanel
    {
        public MessageCell()
        {
            LastChildFill = true;
            ClipToBounds = true;
            VerticalAlignment = VerticalAlignment.Center;
            DataContextChanged += (_, _) => Rebuild();
        }

        private void Rebuild()
        {
            Children.Clear();
            if (DataContext is not LogRow r) return;
            if (r.Commit is not { } c)
            {
                Children.Add(new TextBlock { Text = $"作業ツリーの変更 ({r.WorktreeCount} ファイル)", FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center });
                return;
            }
            foreach (var name in c.Refs)
            {
                var b = RefBadge(name);
                SetDock(b, Dock.Left);
                Children.Add(b);
            }
            Children.Add(new TextBlock { Text = c.Subject, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        }
    }

    /// <summary>Graph column: lanes, merge lines and the commit node, drawn from the row's <see cref="GraphRow"/>.</summary>
    public sealed class GraphCell : FrameworkElement
    {
        private static readonly Color[] Palette =
        [
            Color.FromRgb(0x00, 0x7A, 0xFF), // blue
            Color.FromRgb(0x34, 0xC7, 0x59), // green
            Color.FromRgb(0xFF, 0x95, 0x00), // orange
            Color.FromRgb(0xAF, 0x52, 0xDE), // purple
            Color.FromRgb(0xFF, 0x2D, 0x55), // pink
            Color.FromRgb(0x30, 0xB0, 0xC7), // teal
            Color.FromRgb(0xFF, 0x3B, 0x30), // red
            Color.FromRgb(0xA2, 0x84, 0x5E), // brown
            Color.FromRgb(0x58, 0x56, 0xD6), // indigo
        ];

        private static readonly Brush[] Brushes_ = Palette.Select(c => Freeze(new SolidColorBrush(c))).ToArray();
        private static readonly Pen[] LinePens = Brushes_.Select(b => Freeze(new Pen(b, 1.6))).ToArray();
        private static readonly Pen[] RingPens = Brushes_.Select(b => Freeze(new Pen(b, 2))).ToArray();
        private static readonly Brush WorktreeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x95, 0x00)));

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        public static readonly DependencyProperty HoleBrushProperty = DependencyProperty.Register(
            nameof(HoleBrush), typeof(Brush), typeof(GraphCell), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Fill of merge-commit rings (the theme's background).</summary>
        public Brush? HoleBrush
        {
            get => (Brush?)GetValue(HoleBrushProperty);
            set => SetValue(HoleBrushProperty, value);
        }

        public GraphCell()
        {
            SetResourceReference(HoleBrushProperty, "SolidBackgroundFillColorBaseBrush");
            DataContextChanged += (_, _) => InvalidateVisual();
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
        }

        protected override Size MeasureOverride(Size availableSize) => new(0, 0);

        protected override void OnRender(DrawingContext dc)
        {
            if (DataContext is not LogRow r) return;

            // Extend to the full row height so lanes connect across rows even if the cell has padding.
            double top = 0, bottom = ActualHeight;
            if (FindParent<DataGridRow>(this) is { } row)
            {
                try
                {
                    var p = TransformToAncestor(row).Transform(new Point(0, 0));
                    top = -p.Y;
                    bottom = row.ActualHeight - p.Y;
                }
                catch (InvalidOperationException) { }
            }
            double midY = (top + bottom) / 2;
            static double X(int i) => GraphPadding + i * LaneWidth + LaneWidth / 2;

            if (r.IsWorktree)
            {
                var ft = new FormattedText("", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Ui.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 13, WorktreeBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(GraphPadding + 2, midY - ft.Height / 2));
                return;
            }
            if (r.Graph is not { } g) return;

            dc.PushClip(new RectangleGeometry(new Rect(0, top, Math.Max(0, ActualWidth), Math.Max(0, bottom - top))));

            void Line(Point a, Point b, int color)
            {
                var pen = LinePens[color % LinePens.Length];
                if (a.X == b.X)
                {
                    dc.DrawLine(pen, a, b);
                    return;
                }
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    double my = (a.Y + b.Y) / 2;
                    ctx.BeginFigure(a, false, false);
                    ctx.BezierTo(new Point(a.X, my), new Point(b.X, my), b, true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }

            var mid = new Point(X(g.Node), midY);
            for (int j = 0; j < g.Top.Count; j++)
            {
                if (g.Top[j] is not { } hash) continue;
                if (hash == g.Hash)
                {
                    Line(new Point(X(j), top), mid, j);
                }
                else
                {
                    int k = IndexOf(g.Bottom, hash);
                    if (k < 0) k = j;
                    if (j < g.Bottom.Count && g.Bottom[j] == hash) k = j;
                    Line(new Point(X(j), top), new Point(X(k), bottom), j);
                }
            }
            foreach (var p in g.Parents)
            {
                int k = IndexOf(g.Bottom, p);
                if (g.Node < g.Bottom.Count && g.Bottom[g.Node] == p) k = g.Node;
                if (k >= 0) Line(mid, new Point(X(k), bottom), k);
            }

            const double radius = 4;
            int ci = g.Node % Brushes_.Length;
            if (g.Parents.Count > 1)
                dc.DrawEllipse(HoleBrush ?? SystemColors.WindowBrush, RingPens[ci], mid, radius, radius);
            else
                dc.DrawEllipse(Brushes_[ci], null, mid, radius, radius);
            dc.Pop();
        }

        private static int IndexOf(IReadOnlyList<string?> list, string value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
            return -1;
        }
    }

    /// <summary>Row of <see cref="ChangedFileGrid"/>.</summary>
    public sealed class ChangedFileRow
    {
        public ChangedFileRow(ChangedFile file) { File = file; }
        public ChangedFile File { get; }
        public string StateLabel => File.State.Label();
        public Brush Color => Ui.StateBrush(File.State);
        public string DisplayPath => File.DisplayPath;
        public ImageSource? Icon => Shell.Icon(System.IO.Path.GetFileName(File.Path.TrimEnd('/')));
    }

    /// <summary>Read-only list of changed files (state + path), double click / Enter opens.</summary>
    internal sealed class ChangedFileGrid : DataGrid
    {
        public event Action<ChangedFile>? Open;
        public Func<IReadOnlyList<ChangedFile>, ContextMenu?>? MenuBuilder { get; set; }

        public ChangedFileGrid()
        {
            // Subclasses do not receive the implicit (Fluent) style of their base type: apply it explicitly.
            SetResourceReference(StyleProperty, typeof(DataGrid));
            AutoGenerateColumns = false;
            IsReadOnly = true;
            CanUserAddRows = false;
            CanUserDeleteRows = false;
            CanUserReorderColumns = false;
            SelectionMode = DataGridSelectionMode.Extended;
            SelectionUnit = DataGridSelectionUnit.FullRow;
            HeadersVisibility = DataGridHeadersVisibility.Column;
            GridLinesVisibility = DataGridGridLinesVisibility.None;
            RowHeight = 26;
            EnableRowVirtualization = true;
            VirtualizingPanel.SetIsVirtualizing(this, true);
            VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);

            var st = new FrameworkElementFactory(typeof(TextBlock));
            st.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChangedFileRow.StateLabel)));
            st.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ChangedFileRow.Color)));
            st.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            Columns.Add(new DataGridTemplateColumn
            {
                Header = "状態", Width = 70, MinWidth = 50, MaxWidth = 90, SortMemberPath = nameof(ChangedFileRow.StateLabel),
                CellTemplate = new DataTemplate { VisualTree = st },
            });

            var cell = new FrameworkElementFactory(typeof(StackPanel));
            cell.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding(nameof(ChangedFileRow.Icon)));
            img.SetValue(WidthProperty, 16.0);
            img.SetValue(HeightProperty, 16.0);
            img.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
            cell.AppendChild(img);
            var txt = new FrameworkElementFactory(typeof(TextBlock));
            txt.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChangedFileRow.DisplayPath)));
            txt.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            txt.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            cell.AppendChild(txt);
            Columns.Add(new DataGridTemplateColumn
            {
                Header = "パス", Width = new DataGridLength(1, DataGridLengthUnitType.Star), SortMemberPath = nameof(ChangedFileRow.DisplayPath),
                CellTemplate = new DataTemplate { VisualTree = cell },
            });

            MouseDoubleClick += (_, e) =>
            {
                if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: ChangedFileRow r }) Open?.Invoke(r.File);
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && SelectedItem is ChangedFileRow r) { Open?.Invoke(r.File); e.Handled = true; }
            };
            ContextMenuOpening += (_, e) =>
            {
                var sel = SelectedFiles;
                ContextMenu = sel.Count > 0 ? MenuBuilder?.Invoke(sel) : null;
                if (ContextMenu == null) e.Handled = true;
            };
        }

        public IReadOnlyList<ChangedFile> SelectedFiles => SelectedItems.OfType<ChangedFileRow>().Select(r => r.File).ToList();

        public void SetFiles(IEnumerable<ChangedFile> files) => ItemsSource = files.Select(f => new ChangedFileRow(f)).ToList();
    }
}

/// <summary>Generic list of changed files between two revisions (double click shows the diff).</summary>
public sealed class ChangedFilesView : GVView
{
    public ChangedFilesView(Repo repo, IReadOnlyList<ChangedFile> files, string from, string to)
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        var head = Ui.Heading($"{files.Count} 個のファイル");
        head.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var list = new LogView.ChangedFileGrid();
        list.SetFiles(files);
        list.Open += f => WindowManager.Shared.ShowDiff(repo, new DiffSource.Range(from, to, f.Path, f.OldPath));
        root.Children.Add(list);
        Content = root;
    }
}

/// <summary>Resets the current branch to a commit (soft / mixed / hard).</summary>
public sealed class ResetView : GVView
{
    private readonly Repo repo;
    private readonly Commit commit;
    private readonly Action? onDone;
    private readonly CommandPreview preview = new();
    private string mode = "mixed";

    public ResetView(Repo repo, Commit commit, Action? onDone = null)
    {
        this.repo = repo;
        this.commit = commit;
        this.onDone = onDone;

        var root = new DockPanel { Margin = new Thickness(16) };

        var bottom = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var buttons = Ui.Buttons(this, "リセット", DoReset);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        var help = TermHelp.Label("リセットとは", "reset");
        help.VerticalAlignment = VerticalAlignment.Bottom;
        help.Margin = new Thickness(0, 12, 0, 4);
        if (help.Children[0] is TextBlock ht) { ht.FontSize = 12; ht.Foreground = Ui.Secondary(); }
        bottom.Children.Add(help);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(preview);

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = $"現在のブランチを {commit.Short} にリセットします", Style = (Style)Application.Current.FindResource("Heading"), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = commit.Subject, Foreground = Ui.Secondary(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        top.Children.Add(new TextBlock { Text = "リセットの種類", Margin = new Thickness(0, 12, 0, 4) });
        var group = "reset-" + Guid.NewGuid().ToString("N");
        foreach (var (id, label) in new[]
        {
            ("soft", "ソフト: 作業ツリーとインデックスを維持"),
            ("mixed", "混合: 作業ツリーを維持し、インデックスをリセット"),
            ("hard", "ハード: 作業ツリーとインデックスをリセット (変更は失われます)"),
        })
        {
            var rb = new RadioButton { Content = label, GroupName = group, IsChecked = id == mode, Margin = new Thickness(8, 2, 0, 2) };
            rb.Checked += (_, _) => { mode = id; UpdatePreview(); };
            top.Children.Add(rb);
        }
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Ui.Secondary(), Margin = new Thickness(0, 10, 0, 0) };
        note.Inlines.Add(new Run("取り消したいコミットがプッシュ済みなら、リセットではなくリバートが安全です。"));
        note.Inlines.Add(new InlineUIContainer(new TermHelp("revert")) { BaselineAlignment = BaselineAlignment.Center });
        top.Children.Add(note);
        root.Children.Add(top);

        Content = root;
        UpdatePreview();
    }

    private void UpdatePreview() => preview.Set(GitOps.Reset(mode, commit.Short));

    private void DoReset()
    {
        if (mode == "hard" && !WindowManager.Confirm("ハードリセットしますか?", "コミットされていない変更はすべて失われます。", "リセット", destructive: true)) return;
        WindowManager.Shared.RunProgress("リセット", repo.Root, [ProgressStep.Git(GitOps.Reset(mode, commit.Hash))], null, _ => onDone?.Invoke());
        Handle.Close();
    }
}
