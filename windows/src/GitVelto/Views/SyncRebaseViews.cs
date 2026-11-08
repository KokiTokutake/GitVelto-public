using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Helpers shared by the views in this file.</summary>
file static class SR
{
    public static Button IconLabelButton(string glyph, string text, Action onClick)
    {
        var b = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Margin = new Thickness(0, 0, 8, 0),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    /// <summary>Selects <paramref name="value"/> in a combo box of strings, adding it when missing.</summary>
    public static void Select(ComboBox c, string value)
    {
        if (value.Length == 0) { c.SelectedIndex = -1; return; }
        if (!c.Items.Cast<object>().Any(i => i as string == value)) c.Items.Add(value);
        c.SelectedItem = value;
    }

    public static void Fill(ComboBox c, IEnumerable<string> items)
    {
        c.Items.Clear();
        foreach (var i in items) c.Items.Add(i);
    }
}

// MARK: - Git Sync

public sealed class SyncView : GVView
{
    // MARK: Output log (embeddable)

    /// <summary>Streams the lines of a <see cref="CommandRunner"/> (translated or raw, with learning-mode notes).</summary>
    private sealed class OutputLogView : Border
    {
        private readonly CommandRunner runner;
        private readonly Paragraph doc = new() { Margin = new Thickness(0) };
        private readonly RichTextBox box;
        private readonly Run partialRun = new();
        private int rendered;

        public OutputLogView(CommandRunner runner)
        {
            this.runner = runner;
            box = new RichTextBox
            {
                IsReadOnly = true, IsDocumentEnabled = true, FontFamily = Ui.Mono, FontSize = 12, BorderThickness = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Document = new FlowDocument(doc) { PageWidth = 4000 }, Padding = new Thickness(6),
            };
            Child = box;
            runner.Lines.CollectionChanged += OnLines;
            runner.Changed += UpdatePartial;
            Unloaded += (_, _) => { runner.Lines.CollectionChanged -= OnLines; runner.Changed -= UpdatePartial; };
            Loaded += (_, _) =>
            {
                runner.Lines.CollectionChanged -= OnLines;
                runner.Changed -= UpdatePartial;
                runner.Lines.CollectionChanged += OnLines;
                runner.Changed += UpdatePartial;
                RenderAll();
            };
            RenderAll();
        }

        private static bool ShowRaw => AppSettings.Shared.ProgressShowRaw;

        private void OnLines(object? s, NotifyCollectionChangedEventArgs e) => RenderNew();

        private static Brush? Color(OutputKind k) => k switch
        {
            OutputKind.Command => Ui.Accent(),
            OutputKind.Error => Brushes.Crimson,
            OutputKind.Warning => Brushes.DarkOrange,
            OutputKind.Success => Brushes.SeaGreen,
            _ => null,
        };

        private void RenderAll()
        {
            doc.Inlines.Clear();
            rendered = 0;
            RenderNew();
        }

        private void RenderNew()
        {
            doc.Inlines.Remove(partialRun);
            var learning = AppSettings.Shared.LearningMode;
            for (; rendered < runner.Lines.Count; rendered++)
            {
                var line = runner.Lines[rendered];
                if (doc.Inlines.Count > 0) doc.Inlines.Add(new LineBreak());
                var text = ShowRaw ? line.Raw : line.Translated;
                var run = new Run(text.Length == 0 ? " " : text) { FontWeight = line.Kind == OutputKind.Command ? FontWeights.SemiBold : FontWeights.Normal };
                if (Color(line.Kind) is { } c) run.Foreground = c;
                doc.Inlines.Add(run);
                if (learning && line.Args != null)
                {
                    var ex = CommandExplainer.Explain(line.Args);
                    var opts = string.Join(" / ", ex.Options.Select(o => $"{o.Option}: {o.Meaning}"));
                    var note = (ex.Summary.Length > 0 ? "\n  ↳ " + ex.Summary : "") + (opts.Length > 0 ? "\n    " + opts : "");
                    if (note.Length > 0)
                        doc.Inlines.Add(new Run(note) { Foreground = Ui.Accent(), FontFamily = SystemFonts.MessageFontFamily, FontSize = 11.5 });
                }
            }
            UpdatePartial();
        }

        private void UpdatePartial()
        {
            var p = runner.Partial;
            doc.Inlines.Remove(partialRun);
            if (p.Length > 0)
            {
                partialRun.Text = (doc.Inlines.Count > 0 ? "\n" : "") + (ShowRaw ? p : GitMessages.TranslateLine(p));
                doc.Inlines.Add(partialRun);
            }
            box.ScrollToEnd();
        }
    }

    // MARK: Commit list (used by sync)

    private sealed class RefBadgeItem(string text, Brush background)
    {
        public string Text { get; } = text;
        public Brush Background { get; } = background;
    }

    private sealed class CommitRow(Commit c)
    {
        private static readonly Brush HeadBg = Frozen(0x50, 0x2E, 0xA0, 0x43);
        private static readonly Brush TagBg = Frozen(0x50, 0xE0, 0x8A, 0x00);
        private static readonly Brush RefBg = Frozen(0x40, 0x3B, 0x82, 0xF6);

        private static Brush Frozen(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        public Commit Commit => c;
        public string Short => c.Short;
        public string Subject => c.Subject;
        public string Author => c.Author;
        public string DateText => c.DateText;
        public IReadOnlyList<RefBadgeItem> Badges { get; } = c.Refs.Select(r =>
            r.StartsWith("tag: ", StringComparison.Ordinal) ? new RefBadgeItem(r[5..], TagBg)
            : r.StartsWith("HEAD -> ", StringComparison.Ordinal) ? new RefBadgeItem(r[8..], HeadBg)
            : r == "HEAD" ? new RefBadgeItem(r, HeadBg)
            : new RefBadgeItem(r, RefBg)).ToList();
    }

    /// <summary>Simple commit list (hash / message with ref badges / author / date).</summary>
    private static DataGrid CommitListTable()
    {
        var g = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None,
            RowHeight = 24, EnableRowVirtualization = true, BorderThickness = new Thickness(0),
        };
        VirtualizingPanel.SetIsVirtualizing(g, true);
        VirtualizingPanel.SetVirtualizationMode(g, VirtualizationMode.Recycling);

        var hash = new FrameworkElementFactory(typeof(TextBlock));
        hash.SetBinding(TextBlock.TextProperty, new Binding(nameof(CommitRow.Short)));
        hash.SetValue(TextBlock.FontFamilyProperty, Ui.Mono);
        hash.SetValue(TextBlock.FontSizeProperty, 11.0);
        hash.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        g.Columns.Add(new DataGridTemplateColumn { Header = "ハッシュ", Width = 80, CellTemplate = new DataTemplate { VisualTree = hash } });

        // Message: ref badges + subject.
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        badge.SetValue(Border.PaddingProperty, new Thickness(5, 0, 5, 1));
        badge.SetValue(MarginProperty, new Thickness(0, 0, 4, 0));
        badge.SetBinding(Border.BackgroundProperty, new Binding(nameof(RefBadgeItem.Background)));
        var badgeText = new FrameworkElementFactory(typeof(TextBlock));
        badgeText.SetBinding(TextBlock.TextProperty, new Binding(nameof(RefBadgeItem.Text)));
        badgeText.SetValue(TextBlock.FontSizeProperty, 11.0);
        badge.AppendChild(badgeText);
        var badgePanel = new FrameworkElementFactory(typeof(StackPanel));
        badgePanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var badges = new FrameworkElementFactory(typeof(ItemsControl));
        badges.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(CommitRow.Badges)));
        badges.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = badge });
        badges.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(badgePanel));
        badges.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        badges.SetValue(FocusableProperty, false);
        var subject = new FrameworkElementFactory(typeof(TextBlock));
        subject.SetBinding(TextBlock.TextProperty, new Binding(nameof(CommitRow.Subject)));
        subject.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        subject.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var msg = new FrameworkElementFactory(typeof(StackPanel));
        msg.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        msg.AppendChild(badges);
        msg.AppendChild(subject);
        g.Columns.Add(new DataGridTemplateColumn { Header = "メッセージ", Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellTemplate = new DataTemplate { VisualTree = msg } });

        g.Columns.Add(new DataGridTextColumn { Header = "作成者", Binding = new Binding(nameof(CommitRow.Author)), Width = 120, MinWidth = 80, MaxWidth = 180 });
        g.Columns.Add(new DataGridTextColumn { Header = "日時", Binding = new Binding(nameof(CommitRow.DateText)), Width = 120 });
        return g;
    }

    // MARK: Sync view

    private readonly Repo repo;
    private List<RefInfo> refs = [];
    private List<string> remotes = [];
    private CommandRunner? runner;
    private bool running, suppress, ignoreOwnNotify, hasUpstream;
    private RepoOperationState opState = RepoOperationState.None;
    private int commitsVersion, upstreamVersion;

    private readonly ComboBox localBranch = new() { MinWidth = 180 };
    private readonly ComboBox remote = new() { Width = 140 };
    private readonly TextBox remoteBranch = new();
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal };
    private readonly ProgressBar busy = new() { IsIndeterminate = true, Width = 60, Height = 4, Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabControl tabs = new() { MinHeight = 200 };
    private readonly TabItem outputTab = new() { Header = "出力" };
    private readonly TabItem incomingTab = new();
    private readonly TabItem outgoingTab = new();
    private readonly DataGrid incomingList = CommitListTable();
    private readonly DataGrid outgoingList = CommitListTable();
    private readonly TextBlock placeholder = new() { Foreground = Ui.Secondary(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
    private readonly StackPanel opWarning = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private readonly StackPanel actionBarRight;
    private readonly DockPanel learningNoteRow;
    private readonly TextBlock opLabel = new() { Foreground = Brushes.DarkOrange, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };

    public SyncView(Repo repo)
    {
        this.repo = repo;

        // --- branch / remote grid ---
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Cell(UIElement e, int row, int col, int span = 1)
        {
            if (e is FrameworkElement f) { f.Margin = new Thickness(0, 4, 10, 4); f.VerticalAlignment = VerticalAlignment.Center; }
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            Grid.SetColumnSpan(e, span);
            grid.Children.Add(e);
        }
        Cell(new TextBlock { Text = "ローカルブランチ" }, 0, 0);
        Cell(localBranch, 0, 1);
        Cell(new TextBlock { Text = "リモート" }, 0, 2);
        Cell(remote, 0, 3);
        Cell(new TextBlock { Text = "リモートブランチ" }, 1, 0);
        Cell(remoteBranch, 1, 1);
        var manage = new Button { Content = "リモートの管理…", HorizontalAlignment = HorizontalAlignment.Left };
        manage.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Remotes, [repo.Root]);
        Cell(manage, 1, 2, 2);

        localBranch.SelectionChanged += async (_, _) =>
        {
            if (suppress) return;
            await DefaultsForBranch();
            RefreshCommits();
        };
        remote.SelectionChanged += (_, _) => { if (!suppress) RefreshCommits(); };
        remoteBranch.KeyDown += (_, e) => { if (e.Key == Key.Enter) { RefreshCommits(); e.Handled = true; } };

        // --- action buttons ---
        actions.Children.Add(SR.IconLabelButton("", "プル", () => RunCommands("プル", [PullArgs()])));
        actions.Children.Add(SR.IconLabelButton("", "フェッチ", () => RunCommands("フェッチ", [["fetch", "--progress", RemoteName]])));
        actions.Children.Add(SR.IconLabelButton("", "フェッチ＆リベース", () =>
            RunCommands("フェッチ＆リベース", [["fetch", "--progress", RemoteName], ["rebase", "--autostash", $"{RemoteName}/{RemoteBranchName}"]])));
        actions.Children.Add(SR.IconLabelButton("", "プッシュ", () => RunCommands("プッシュ", [PushArgs()])));
        actions.Children.Add(SR.IconLabelButton("", "プル＆プッシュ", () => RunCommands("プル＆プッシュ", [PullArgs(), PushArgs()])));
        var reload = Ui.IconButton("", "状態を再読み込み", Load);
        reload.Margin = new Thickness(0, 0, 8, 0);
        var fetchAll = new Button { Content = "すべてフェッチ" };
        fetchAll.Click += (_, _) => RunCommands("フェッチ", [["fetch", "--progress", "--all", "--prune"]]);
        var actionsRight = new StackPanel { Orientation = Orientation.Horizontal, Children = { busy, reload, fetchAll } };
        var actionBar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(actionsRight, Dock.Right);
        actionBar.Children.Add(actionsRight);
        DockPanel.SetDock(actions, Dock.Left);
        actionBar.Children.Add(actions);
        actionsRight.IsEnabled = false;
        actions.IsEnabled = false;
        actionBarRight = actionsRight;

        // --- learning note ---
        var noteText = Ui.Caption("プル = git pull（取得＋取り込み）/ フェッチ = git fetch（取得のみ）/ フェッチ＆リベース = git fetch + git rebase / プッシュ = git push。受信予定 = リモートにあって手元にないコミット、送信予定 = 手元にあってリモートにないコミット。");
        noteText.TextWrapping = TextWrapping.Wrap;
        noteText.Margin = new Thickness(4, 0, 0, 0);
        noteText.VerticalAlignment = VerticalAlignment.Center;
        var notePanel = new DockPanel();
        var noteHelp = new TermHelp("upstream");
        DockPanel.SetDock(noteHelp, Dock.Right);
        notePanel.Children.Add(noteHelp);
        notePanel.Children.Add(noteText);
        var noteRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var cap = Ui.Glyph("", 13, Ui.Accent());
        DockPanel.SetDock(cap, Dock.Left);
        noteRow.Children.Add(cap);
        noteRow.Children.Add(notePanel);
        learningNoteRow = noteRow;

        // --- tabs ---
        outputTab.Content = placeholder;
        incomingTab.Content = incomingList;
        outgoingTab.Content = outgoingList;
        tabs.Items.Add(outputTab);
        tabs.Items.Add(incomingTab);
        tabs.Items.Add(outgoingTab);
        tabs.SelectedIndex = 0;
        tabs.Margin = new Thickness(0, 8, 0, 0);
        UpdateCounts([], []);

        // --- bottom ---
        var showLog = new Button { Content = "ログを表示", Margin = new Thickness(0, 0, 8, 0) };
        showLog.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Log, [repo.Root]);
        var commit = new Button { Content = "コミット…", Margin = new Thickness(0, 0, 8, 0) };
        commit.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Commit, [repo.Root]);
        var resolve = Ui.Accent(new Button { Content = "競合を解決…" });
        resolve.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Resolve, [repo.Root]);
        opWarning.Children.Add(Ui.Glyph("", 14, Brushes.DarkOrange));
        opWarning.Children.Add(opLabel);
        opWarning.Children.Add(resolve);
        var close = new Button { Content = "閉じる", MinWidth = 96, IsCancel = true };
        close.Click += (_, _) => Handle.Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        var bottomLeft = new StackPanel { Orientation = Orientation.Horizontal, Children = { showLog, commit, opWarning } };
        DockPanel.SetDock(bottomLeft, Dock.Left);
        bottom.Children.Add(bottomLeft);

        var root = new DockPanel { Margin = new Thickness(14) };
        var header = new RepoHeader(repo);
        foreach (var e in new UIElement[] { header, grid, actionBar, noteRow })
        {
            DockPanel.SetDock(e, Dock.Top);
            root.Children.Add(e);
        }
        actionBar.Margin = new Thickness(0, 0, 0, 4);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(tabs);
        Content = root;

        UpdateLearning();
        void OnSettings(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AppSettings.LearningMode) or null) Dispatcher.BeginInvoke(UpdateLearning);
        }
        Loaded += (_, _) => { AppSettings.Shared.PropertyChanged -= OnSettings; AppSettings.Shared.PropertyChanged += OnSettings; };
        Unloaded += (_, _) => AppSettings.Shared.PropertyChanged -= OnSettings;

        OnRepoChanged(repo.Root, () =>
        {
            if (ignoreOwnNotify) { ignoreOwnNotify = false; return; }
            if (!running) Load();
        });
    }

    protected internal override void OnShown() => Load();

    protected internal override bool CanClose()
    {
        if (!running || runner == null) return true;
        if (!WindowManager.Confirm("実行中の処理を中止しますか？", "", "中止する", true)) return false;
        runner.Cancel();
        return true;
    }

    private string LocalName => localBranch.SelectedItem as string ?? "";
    private string RemoteName => remote.SelectedItem as string ?? "";
    private string RemoteBranchName => remoteBranch.Text.Trim();

    private List<string> PullArgs() => ["pull", "--progress", "--no-rebase", RemoteName, RemoteBranchName];

    private List<string> PushArgs()
    {
        var a = new List<string> { "push", "--progress" };
        if (!hasUpstream) a.Add("-u");
        var rb = RemoteBranchName;
        a.AddRange([RemoteName, $"{LocalName}:{(rb.Length == 0 ? LocalName : rb)}"]);
        return a;
    }

    private void UpdateLearning() =>
        learningNoteRow.Visibility = AppSettings.Shared.LearningMode ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateCounts(List<Commit> incoming, List<Commit> outgoing)
    {
        incomingTab.Header = $"受信予定のコミット ({incoming.Count})";
        outgoingTab.Header = $"送信予定のコミット ({outgoing.Count})";
        incomingList.ItemsSource = incoming.Select(c => new CommitRow(c)).ToList();
        outgoingList.ItemsSource = outgoing.Select(c => new CommitRow(c)).ToList();
    }

    private void UpdateUi()
    {
        bool enabled = remotes.Count > 0 && !running;
        actions.IsEnabled = enabled;
        actionBarRight.IsEnabled = enabled;
        busy.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        placeholder.Text = remotes.Count == 0
            ? "リモートが登録されていません。「リモートの管理…」から追加してください。"
            : "操作ボタンを押すと、ここに git の出力が表示されます。";
        if (opState != RepoOperationState.None && opState != RepoOperationState.Bisecting)
        {
            opLabel.Text = opState.Label();
            opWarning.Visibility = Visibility.Visible;
        }
        else opWarning.Visibility = Visibility.Collapsed;
    }

    private async void Load()
    {
        var (rf, rs, st, cur) = await Task.Run(() => (repo.Refs(), repo.Remotes(), repo.OperationState(), repo.CurrentBranch()));
        refs = rf;
        remotes = rs;
        opState = st;
        suppress = true;
        SR.Fill(localBranch, refs.Where(r => r.Kind == RefKind.Local).Select(r => r.Name));
        SR.Fill(remote, remotes);
        SR.Select(localBranch, cur ?? refs.FirstOrDefault(r => r.Kind == RefKind.Local)?.Name ?? "");
        suppress = false;
        UpdateUi();
        await DefaultsForBranch();
        RefreshCommits();
    }

    private async Task DefaultsForBranch()
    {
        var lb = LocalName;
        int v = ++upstreamVersion;
        var up = await Task.Run(() => lb.Length > 0 ? repo.Upstream(lb) : null);
        if (v != upstreamVersion) return;
        hasUpstream = up != null;
        suppress = true;
        if (up is { } u)
        {
            SR.Select(remote, u.Remote);
            remoteBranch.Text = u.Branch;
        }
        else
        {
            SR.Select(remote, remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault() ?? "");
            remoteBranch.Text = lb;
        }
        suppress = false;
    }

    private async void RefreshCommits()
    {
        var rm = RemoteName;
        var lb = LocalName;
        if (rm.Length == 0 || lb.Length == 0) return;
        var r = $"{rm}/{RemoteBranchName}";
        int v = ++commitsVersion;
        var (inc, outg) = await Task.Run(() =>
        {
            bool exists = repo.Git(["rev-parse", "--verify", "-q", r]).Ok;
            var inc = exists ? repo.Commits($"{lb}..{r}") : [];
            var outg = exists ? repo.Commits($"{r}..{lb}") : repo.Commits(lb, limit: 100);
            return (inc, outg);
        });
        if (v != commitsVersion) return;
        UpdateCounts(inc, outg);
    }

    private void RunCommands(string title, List<List<string>> commands)
    {
        if (running) return;
        var root = repo.Root;
        var r = new CommandRunner(root, commands.Select(c => ProgressStep.Git(c.Where(a => a.Length > 0))));
        r.Finished += async _ =>
        {
            running = false;
            // Let other windows (and the overlay cache) know; this view refreshes itself below.
            ignoreOwnNotify = true;
            WindowManager.NotifyRepoChanged(root);
            var (rf, st) = await Task.Run(() => (repo.Refs(), repo.OperationState()));
            refs = rf;
            opState = st;
            var selected = LocalName;
            suppress = true;
            SR.Fill(localBranch, refs.Where(x => x.Kind == RefKind.Local).Select(x => x.Name));
            SR.Select(localBranch, selected);
            suppress = false;
            UpdateUi();
            RefreshCommits();
        };
        runner = r;
        running = true;
        outputTab.Content = new OutputLogView(r);
        tabs.SelectedIndex = 0;
        UpdateUi();
        r.Start();
    }
}

// MARK: - Interactive rebase

public sealed class RebaseView : GVView
{
    /// <summary>Bindable wrapper of a <see cref="RebaseItem"/>.</summary>
    private sealed class Row(RebaseItem item) : INotifyPropertyChanged
    {
        public RebaseItem Item => item;
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? ActionChanged;

        public RebaseAction Action
        {
            get => item.Action;
            set
            {
                if (item.Action == value) return;
                item.Action = value;
                Raise();
                Raise(nameof(IsReword));
                Raise(nameof(IsNotReword));
                Raise(nameof(Decorations));
                Raise(nameof(SubjectOpacity));
                ActionChanged?.Invoke();
            }
        }

        public string Message
        {
            get => item.Message;
            set { if (item.Message == value) return; item.Message = value; Raise(); }
        }

        public string Short => item.Commit.Short;
        public string Subject => item.Commit.Subject;
        public string Author => item.Commit.Author;
        public bool IsReword => item.Action == RebaseAction.Reword;
        public bool IsNotReword => !IsReword;
        public TextDecorationCollection? Decorations => item.Action == RebaseAction.Drop ? TextDecorations.Strikethrough : null;
        public double SubjectOpacity => item.Action == RebaseAction.Drop ? 0.5 : 1.0;

        private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private static readonly List<KeyValuePair<RebaseAction, string>> ActionOptions =
        Enum.GetValues<RebaseAction>().Select(a => new KeyValuePair<RebaseAction, string>(a, a.Label())).ToList();

    private readonly Repo repo;
    private readonly string? initialUpstream;
    private List<RefInfo> refs = [];
    private readonly ObservableCollection<Row> items = [];
    private List<string> originalOrder = [];
    private bool suppress;
    private int loadVersion;

    private readonly RefPicker upstream = new([]) { Width = 260 };
    private readonly TextBox custom = new() { Width = 220 };
    private readonly CheckBox useCustom = new() { Content = "コミットを指定", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock countText = Ui.Caption("");
    private readonly ListBox list = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowDrop = true };
    private readonly CommandPreview preview = new();
    private readonly CheckBox autostash = new() { Content = "ローカルの変更を自動退避 (--autostash)", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button up = new() { Content = new TextBlock { Text = "", FontFamily = Ui.IconFont }, ToolTip = "上へ (Alt+↑)", Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button down = new() { Content = new TextBlock { Text = "", FontFamily = Ui.IconFont }, ToolTip = "下へ (Alt+↓)", Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button start = Ui.Accent(new Button { Content = "リベース開始", MinWidth = 110, IsDefault = true });
    private Point dragStart;
    private Row? dragRow;

    public RebaseView(Repo repo, string? initialUpstream)
    {
        this.repo = repo;
        this.initialUpstream = initialUpstream;

        // --- upstream row ---
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var reload = Ui.IconButton("", "再読み込み", Load);
        DockPanel.SetDock(reload, Dock.Right);
        top.Children.Add(reload);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock { Text = "リベース先 (upstream):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        custom.Visibility = Visibility.Collapsed;
        custom.Margin = new Thickness(0, 0, 10, 0);
        upstream.Margin = new Thickness(0, 0, 10, 0);
        left.Children.Add(upstream);
        left.Children.Add(custom);
        left.Children.Add(useCustom);
        top.Children.Add(left);

        upstream.SelectionChanged += (_, _) => { if (!suppress && upstream.SelectedItem != null) Load(); };
        upstream.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (!suppress) UpdateUi(); }));
        upstream.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Load(); e.Handled = true; } };
        custom.TextChanged += (_, _) => { if (!suppress) UpdateUi(); };
        custom.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Load(); e.Handled = true; } };
        useCustom.Click += (_, _) =>
        {
            ApplyCustomVisibility();
            Load();
        };

        // --- hint ---
        var hint = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var helps = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TermHelp("rebase"), new TermHelp("squash"), new TermHelp("fixup") } };
        DockPanel.SetDock(helps, Dock.Right);
        hint.Children.Add(helps);
        countText.TextWrapping = TextWrapping.Wrap;
        countText.VerticalAlignment = VerticalAlignment.Center;
        hint.Children.Add(countText);

        // --- list ---
        list.ItemTemplate = RowTemplate();
        list.ItemsSource = items;
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) => UpdateUi();
        list.PreviewMouseLeftButtonDown += OnDragMouseDown;
        list.PreviewMouseMove += OnDragMouseMove;
        list.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(typeof(Row)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        list.Drop += OnDrop;
        list.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey is Key.Up or Key.Down)
            {
                Move(e.SystemKey == Key.Up ? -1 : 1);
                e.Handled = true;
            }
        };

        // --- bottom ---
        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(1);
        var bulk = new Button { Content = "一括設定 ▾", Margin = new Thickness(0, 0, 10, 0) };
        var bulkMenu = new ContextMenu();
        foreach (var a in Enum.GetValues<RebaseAction>())
        {
            var mi = new MenuItem { Header = a.Label() };
            mi.Click += (_, _) => BulkSet(a);
            bulkMenu.Items.Add(mi);
        }
        bulk.ContextMenu = bulkMenu;
        bulk.Click += (_, _) =>
        {
            bulkMenu.PlacementTarget = bulk;
            bulkMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            bulkMenu.IsOpen = true;
        };
        autostash.Click += (_, _) => UpdateUi();
        var cancel = new Button { Content = "キャンセル", MinWidth = 96, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Handle.Close();
        start.Click += (_, _) => Start();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, start } };
        DockPanel.SetDock(right, Dock.Right);
        bottom.Children.Add(right);
        var bl = new StackPanel { Orientation = Orientation.Horizontal, Children = { up, down, bulk, autostash } };
        DockPanel.SetDock(bl, Dock.Left);
        bottom.Children.Add(bl);

        var root = new DockPanel { Margin = new Thickness(14) };
        var header = new RepoHeader(repo);
        foreach (var e in new UIElement[] { header, top, hint })
        {
            DockPanel.SetDock(e, Dock.Top);
            root.Children.Add(e);
        }
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(preview);
        root.Children.Add(list);
        Content = root;
        UpdateUi();
    }

    protected internal override async void OnShown()
    {
        var u0 = initialUpstream;
        var (rf, def) = await Task.Run(() =>
        {
            var rf = repo.Refs();
            string? def = null;
            if (u0 == null && repo.CurrentBranch() is { } b && repo.Upstream(b) is { } upst) def = $"{upst.Remote}/{upst.Branch}";
            return (rf, def);
        });
        refs = rf;
        suppress = true;
        upstream.SetRefs(refs.Where(r => !r.IsHead));
        if (u0 != null)
        {
            if (refs.Any(r => r.Name == u0)) upstream.Value = u0;
            else { useCustom.IsChecked = true; custom.Text = u0; }
        }
        else if (def != null)
        {
            upstream.Value = def;
        }
        else
        {
            upstream.Value = refs.FirstOrDefault(r => r.Kind == RefKind.Local && !r.IsHead && (r.Name == "main" || r.Name == "master"))?.Name
                ?? refs.FirstOrDefault(r => !r.IsHead)?.Name ?? "";
        }
        suppress = false;
        ApplyCustomVisibility();
        Load();
    }

    private void ApplyCustomVisibility()
    {
        bool c = useCustom.IsChecked == true;
        custom.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        upstream.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
    }

    private string Target => useCustom.IsChecked == true ? custom.Text.Trim() : upstream.Value;

    private bool Unchanged => items.Select(i => i.Item.Id).SequenceEqual(originalOrder) && items.All(i => i.Action == RebaseAction.Pick);

    private DataTemplate RowTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(DockPanel.LastChildFillProperty, true);
        row.SetValue(MarginProperty, new Thickness(0, 1, 0, 1));

        var combo = new FrameworkElementFactory(typeof(ComboBox));
        combo.SetValue(DockPanel.DockProperty, Dock.Left);
        combo.SetValue(WidthProperty, 190.0);
        combo.SetValue(ItemsControl.ItemsSourceProperty, ActionOptions);
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, "Value");
        combo.SetValue(System.Windows.Controls.Primitives.Selector.SelectedValuePathProperty, "Key");
        combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, new Binding(nameof(Row.Action)) { Mode = BindingMode.TwoWay });
        combo.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(combo);

        var hash = new FrameworkElementFactory(typeof(TextBlock));
        hash.SetValue(DockPanel.DockProperty, Dock.Left);
        hash.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.Short)));
        hash.SetValue(TextBlock.FontFamilyProperty, Ui.Mono);
        hash.SetValue(TextBlock.FontSizeProperty, 11.0);
        hash.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        hash.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        hash.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
        row.AppendChild(hash);

        var author = new FrameworkElementFactory(typeof(TextBlock));
        author.SetValue(DockPanel.DockProperty, Dock.Right);
        author.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.Author)));
        author.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        author.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        author.SetValue(MaxWidthProperty, 160.0);
        author.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        author.SetValue(MarginProperty, new Thickness(10, 0, 4, 0));
        row.AppendChild(author);

        var vis = new BooleanToVisibilityConverter();
        var middle = new FrameworkElementFactory(typeof(Grid));
        var subject = new FrameworkElementFactory(typeof(TextBlock));
        subject.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.Subject)));
        subject.SetBinding(TextBlock.TextDecorationsProperty, new Binding(nameof(Row.Decorations)));
        subject.SetBinding(OpacityProperty, new Binding(nameof(Row.SubjectOpacity)));
        subject.SetBinding(VisibilityProperty, new Binding(nameof(Row.IsNotReword)) { Converter = vis });
        subject.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        subject.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        middle.AppendChild(subject);
        var edit = new FrameworkElementFactory(typeof(TextBox));
        edit.SetBinding(TextBox.TextProperty, new Binding(nameof(Row.Message)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        edit.SetBinding(VisibilityProperty, new Binding(nameof(Row.IsReword)) { Converter = vis });
        edit.SetValue(TextBoxBase_AcceptsReturn, true);
        edit.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
        edit.SetValue(TextBox.MaxLinesProperty, 4);
        edit.SetValue(TextBox.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        edit.SetValue(ToolTipProperty, "新しいメッセージ");
        middle.AppendChild(edit);
        row.AppendChild(middle);

        return new DataTemplate { VisualTree = row };
    }

    private static readonly DependencyProperty TextBoxBase_AcceptsReturn = System.Windows.Controls.Primitives.TextBoxBase.AcceptsReturnProperty;

    private void UpdateUi()
    {
        countText.Text = $"{items.Count} 個のコミットを並べ替え・統合・削除できます (ドラッグまたは ↑↓ で並べ替え)。上が古いコミットです。";
        bool sel = list.SelectedItem != null;
        up.IsEnabled = sel;
        down.IsEnabled = sel;
        var target = Target;
        start.IsEnabled = target.Length > 0;
        if (target.Length == 0) { preview.Set(new List<IReadOnlyList<string>>()); return; }
        bool unchanged = Unchanged;
        var args = new List<string> { "rebase" };
        if (autostash.IsChecked == true) args.Add("--autostash");
        if (!unchanged) args.Add("-i");
        args.Add(target);
        var todo = string.Join("\n", items.Select(i => $"{i.Action.Id()} {i.Item.Commit.Short} {i.Item.Commit.Subject}"));
        preview.Set(args, unchanged ? null : "対話的リベースでは、次の作業リスト (todo) を git に渡します:\n" + todo);
    }

    private async void Load()
    {
        var target = Target;
        int v = ++loadVersion;
        if (target.Length == 0)
        {
            SetItems([]);
            return;
        }
        var commits = await Task.Run(() => repo.Commits($"{target}..HEAD", limit: 500, reverse: true, noMerges: true));
        if (v != loadVersion) return;
        SetItems(commits);
    }

    private void SetItems(List<Commit> commits)
    {
        items.Clear();
        foreach (var c in commits)
        {
            var r = new Row(new RebaseItem(c, c.Subject + (c.Body.Length == 0 ? "" : "\n\n" + c.Body)));
            r.ActionChanged += UpdateUi;
            items.Add(r);
        }
        originalOrder = commits.Select(c => c.Hash).ToList();
        UpdateUi();
    }

    private void Move(int delta)
    {
        if (list.SelectedItem is not Row r) return;
        int i = items.IndexOf(r), j = i + delta;
        if (i < 0 || j < 0 || j >= items.Count) return;
        items.Move(i, j);
        list.SelectedItem = r;
        list.ScrollIntoView(r);
        UpdateUi();
    }

    private void BulkSet(RebaseAction a)
    {
        for (int i = 1; i < items.Count; i++) items[i].Action = a;
        if (items.Count > 0 && a != RebaseAction.Squash && a != RebaseAction.Fixup) items[0].Action = a;
        UpdateUi();
    }

    // ---- drag & drop reordering ----

    private static bool IsInteractive(DependencyObject? d) =>
        SR.FindParent<ComboBox>(d) != null || SR.FindParent<TextBox>(d) != null || SR.FindParent<System.Windows.Controls.Primitives.ScrollBar>(d) != null;

    private void OnDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        dragRow = null;
        if (e.OriginalSource is not DependencyObject d || IsInteractive(d)) return;
        if (SR.FindParent<ListBoxItem>(d) is { DataContext: Row r })
        {
            dragRow = r;
            dragStart = e.GetPosition(list);
        }
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (dragRow == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(list);
        if (Math.Abs(p.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var r = dragRow;
        dragRow = null;
        DragDrop.DoDragDrop(list, new DataObject(typeof(Row), r), DragDropEffects.Move);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Row)) is not Row r) return;
        int from = items.IndexOf(r);
        if (from < 0) return;
        int to;
        if (SR.FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: Row target } item)
        {
            to = items.IndexOf(target);
            // Dropping on the lower half of a row places the item after it.
            if (e.GetPosition(item).Y > item.ActualHeight / 2 && to < from) to++;
            else if (e.GetPosition(item).Y <= item.ActualHeight / 2 && to > from) to--;
        }
        else to = items.Count - 1;
        to = Math.Clamp(to, 0, items.Count - 1);
        if (to != from) items.Move(from, to);
        list.SelectedItem = r;
        e.Handled = true;
        UpdateUi();
    }

    private void Start()
    {
        var target = Target;
        if (target.Length == 0) return;
        ProgressStep step;
        try
        {
            step = GitOps.RebaseStep(repo, items.Select(i => i.Item).ToList(), originalOrder, target, autostash.IsChecked == true);
        }
        catch (Exception e)
        {
            WindowManager.Alert(e.Message);
            return;
        }
        var root = repo.Root;
        WindowManager.Shared.RunProgress("リベース", root, [step],
            [new ProgressAction("ログを表示", () => WindowManager.Shared.Perform(GitAction.Log, [root]))],
            ok =>
            {
                if (new Repo(root).OperationState() == RepoOperationState.Rebasing &&
                    WindowManager.Confirm(ok ? "リベースが途中で停止しました" : "リベース中に競合が発生しました", "競合の解決/続行画面を開きますか?", "開く"))
                    WindowManager.Shared.Perform(GitAction.Resolve, [root]);
            });
        Handle.Close();
    }
}
