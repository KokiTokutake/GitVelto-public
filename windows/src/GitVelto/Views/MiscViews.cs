using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

// MARK: - Clone

public sealed class CloneView : GVView
{
    private readonly string parentDir;
    private readonly TextBox url = new();
    private readonly TextBox dir = new();
    private readonly TextBox branch = new() { ToolTip = "省略可" };
    private readonly TextBox depth = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "省略可" };
    private readonly TextBox origin = new() { Text = "origin", Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox recursive = new() { Content = "サブモジュールも取得 (--recursive)", IsChecked = true };
    private readonly CheckBox noCheckout = new() { Content = "チェックアウトしない (--no-checkout)" };
    private readonly CheckBox bare = new() { Content = "ベアリポジトリとしてクローン (--bare)" };
    private readonly CommandPreview preview = new();
    private readonly Button okButton;
    private bool dirEdited, settingDir;

    public CloneView(string parentDir)
    {
        this.parentDir = parentDir;

        var choose = new Button { Content = "選択…", Margin = new Thickness(6, 0, 0, 0) };
        choose.Click += (_, _) =>
        {
            if (Shell.ChooseFolder("クローン先のフォルダを選択", this.parentDir, OwnerWindow) is { } chosen)
            {
                SetDir(Join(chosen, RepoName(url.Text)));
                dirEdited = true;
            }
        };
        var form = Ui.Form(
            ("URL", WithRight(url, new TermHelp("clone"))),
            ("クローン先", WithRight(dir, choose)),
            ("ブランチ (省略可)", branch),
            ("深さ --depth (省略可)", TermHelp.With(depth, "depth")),
            ("リモート名 (-o)", origin),
            ("", TermHelp.With(recursive, "submodule")),
            ("", noCheckout),
            ("", TermHelp.With(bare, "bare")));

        url.TextChanged += (_, _) =>
        {
            if (!dirEdited) SetDir(Join(this.parentDir, RepoName(url.Text)));
            Update();
        };
        dir.TextChanged += (_, _) =>
        {
            if (!settingDir) dirEdited = true;
            Update();
        };
        foreach (var t in new[] { branch, depth, origin }) t.TextChanged += (_, _) => Update();
        foreach (var c in new[] { recursive, noCheckout, bare }) c.Click += (_, _) => Update();

        var buttons = Ui.Buttons(this, "クローン", Run);
        okButton = (Button)buttons.Children[buttons.Children.Count - 1];

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(form, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(form);
        root.Children.Add(buttons);
        root.Children.Add(preview);
        root.Children.Add(new Border());
        Content = root;

        // Pre-fill the URL from the clipboard when it looks like a repository URL.
        if (Shell.ClipboardText()?.Trim() is { } s && !s.Contains('\n')
            && (s.StartsWith("https://") || s.StartsWith("git@") || s.StartsWith("ssh://")))
            url.Text = s;
        SetDir(Join(parentDir, RepoName(url.Text)));
        Update();
        Loaded += (_, _) => { url.Focus(); url.SelectAll(); };
    }

    private static DockPanel WithRight(UIElement main, UIElement right)
    {
        var p = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        p.Children.Add(right);
        p.Children.Add(main);
        return p;
    }

    private static string Join(string parent, string name) => parent.TrimEnd('\\', '/') + "\\" + name;

    private void SetDir(string value)
    {
        settingDir = true;
        dir.Text = value;
        settingDir = false;
    }

    /// <summary>"https://host/user/repo.git" → "repo".</summary>
    public static string RepoName(string url)
    {
        var s = url.Trim().TrimEnd('/');
        var name = s.Split('/', ':').LastOrDefault() ?? "";
        if (name.EndsWith(".git")) name = name[..^4];
        return name;
    }

    private List<string> CloneArgs()
    {
        var args = new List<string> { "clone", "--progress" };
        if (recursive.IsChecked == true) args.Add("--recursive");
        if (branch.Text.Trim().Length > 0) args.AddRange(["-b", branch.Text.Trim()]);
        if (int.TryParse(depth.Text.Trim(), out var d) && d > 0) args.AddRange(["--depth", d.ToString()]);
        var o = origin.Text.Trim();
        if (o.Length > 0 && o != "origin") args.AddRange(["-o", o]);
        if (noCheckout.IsChecked == true) args.Add("--no-checkout");
        if (bare.IsChecked == true) args.Add("--bare");
        args.Add(url.Text.Trim());
        args.Add(dir.Text.Trim());
        return args;
    }

    private void Update()
    {
        okButton.IsEnabled = url.Text.Trim().Length > 0 && dir.Text.Trim().Length > 0;
        preview.Set(url.Text.Trim().Length == 0 ? [] : [CloneArgs()]);
    }

    private void Run()
    {
        if (url.Text.Trim().Length == 0 || dir.Text.Trim().Length == 0) return;
        var args = CloneArgs();
        var target = dir.Text.Trim();
        string cwd;
        try
        {
            cwd = Path.GetDirectoryName(Path.GetFullPath(target)) ?? parentDir;
            Directory.CreateDirectory(cwd);
        }
        catch (Exception e) { WindowManager.Alert("フォルダを作成できません", e.Message); return; }
        WindowManager.Shared.RunProgress("クローン", cwd, [ProgressStep.Git(args)],
        [
            new ProgressAction("フォルダを開く", () => Shell.OpenFile(target)),
            new ProgressAction("ログを表示", () => WindowManager.Shared.Perform(GitAction.Log, [target])),
        ]);
        Handle.Close();
    }
}

// MARK: - Init

public sealed class InitRepoView : GVView
{
    private readonly string dir;
    private readonly TextBox branch = new() { Text = "main", Width = 220 };
    private readonly CheckBox bare = new() { Content = "ベアリポジトリにする (--bare)" };
    private readonly CommandPreview preview = new();
    private bool branchEdited, settingBranch;

    public InitRepoView(string dir)
    {
        this.dir = dir;

        var heading = Ui.Heading("次の場所に Git リポジトリを作成します");
        var path = new TextBlock
        {
            Text = dir, Foreground = Ui.Secondary(), TextWrapping = TextWrapping.Wrap, MaxHeight = 40,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = dir, Margin = new Thickness(0, 4, 0, 8),
        };
        var form = Ui.Form(
            ("初期ブランチ名", TermHelp.With(branch, "main")),
            ("", TermHelp.With(bare, "bare")));
        branch.TextChanged += (_, _) => { if (!settingBranch) branchEdited = true; Update(); };
        bare.Click += (_, _) => Update();

        var top = new StackPanel();
        top.Children.Add(heading);
        top.Children.Add(path);
        top.Children.Add(form);

        var help = TermHelp.Label("リポジトリとは", "repository");
        if (help.Children[0] is TextBlock t) { t.FontSize = 12; t.Foreground = Ui.Secondary(); }
        help.Margin = new Thickness(0, 8, 0, 0);
        var buttons = Ui.Buttons(this, "作成", Run);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(help, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(help);
        root.Children.Add(preview);
        root.Children.Add(new Border());
        Content = root;
        Update();
    }

    protected internal override async void OnShown()
    {
        var def = await Task.Run(() => Git.GlobalConfig("init.defaultBranch"));
        if (def.Length > 0 && !branchEdited)
        {
            settingBranch = true;
            branch.Text = def;
            settingBranch = false;
            Update();
        }
    }

    private List<string> InitArgs()
    {
        var args = new List<string> { "init" };
        if (branch.Text.Trim().Length > 0) args.AddRange(["-b", branch.Text.Trim()]);
        if (bare.IsChecked == true) args.Add("--bare");
        return args;
    }

    private void Update() => preview.Set(InitArgs());

    private async void Run()
    {
        var args = InitArgs();
        var d = dir;
        var r = await Task.Run(() =>
        {
            var id = CommandHistory.Shared.Record(args, d);
            var res = Git.Run(args, d);
            CommandHistory.Shared.Finish(id, res.Ok);
            return res;
        });
        if (r.Ok)
        {
            WindowManager.NotifyRepoChanged(d);
            WindowManager.Alert("リポジトリを作成しました", r.Message, error: false);
            Handle.Close();
        }
        else WindowManager.Alert("作成できません", r.Message);
    }
}

// MARK: - Blame

public sealed class BlameView : GVView
{
    private sealed class BlameCommit
    {
        public string Author = "";
        public DateTimeOffset Time;
        public string Summary = "";
        /// <summary>Path of the file in that commit (differs from the current path after renames).</summary>
        public string Filename = "";
    }

    /// <summary>One line of the blame output (row of the virtualized list).</summary>
    private sealed class BlameRow : INotifyPropertyChanged
    {
        public required int Number { get; init; }
        public required string Hash { get; init; }
        public required string Text { get; init; }
        public string HashText { get; set; } = "";
        public string Author { get; set; } = "";
        public string Date { get; set; } = "";
        public required Brush Tint { get; init; }
        private bool selected;
        public bool Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; OnChanged(nameof(Background)); }
        }
        public static Brush? SelectedBrush;
        public Brush Background => selected && SelectedBrush != null ? SelectedBrush : Tint;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private readonly Repo repo;
    private readonly string path;
    private readonly string? revision;
    private List<BlameRow> rows = [];
    private Dictionary<string, BlameCommit> commits = [];
    private string? selectedHash;
    private readonly DockPanel selectionInfo = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(16, 0, 0, 0) };
    private readonly TextBlock infoText = new() { Foreground = Ui.Secondary(), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Grid body = new();
    private readonly ItemsControl list;

    public BlameView(Repo repo, string path, string? revision)
    {
        this.repo = repo;
        this.path = path;
        this.revision = revision;
        var accent = Ui.Accent() is SolidColorBrush a ? a.Color : Color.FromRgb(0x2F, 0x7B, 0xD9);
        BlameRow.SelectedBrush = new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B));

        // --- header ---
        var showCommit = new Button { Content = "このコミットの変更", Margin = new Thickness(0, 0, 6, 0) };
        showCommit.Click += (_, _) => ShowCommitChanges();
        var older = new Button { Content = "この行より前の Blame", Margin = new Thickness(0, 0, 6, 0) };
        older.Click += (_, _) => BlameBefore();
        var fileLog = new Button { Content = "ファイルのログ" };
        fileLog.Click += (_, _) => WindowManager.Shared.Perform(GitAction.Log, [path]);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(showCommit);
        actions.Children.Add(older);
        actions.Children.Add(fileLog);

        var header = new DockPanel { Margin = new Thickness(10) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = repo.Relative(path), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(new TermHelp("blame"));
        if (revision != null)
            left.Children.Add(new TextBlock { Text = $"@ {revision}", Foreground = Ui.Secondary(), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(left, Dock.Left);
        header.Children.Add(left);
        DockPanel.SetDock(actions, Dock.Right);
        selectionInfo.Children.Add(actions);
        selectionInfo.Children.Add(infoText);
        infoText.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(selectionInfo);

        // --- list (virtualized) ---
        list = BuildList();
        body.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var sep = new Separator { Margin = new Thickness(0) };
        DockPanel.SetDock(sep, Dock.Top);
        root.Children.Add(sep);
        root.Children.Add(body);
        Content = root;
    }

    private ItemsControl BuildList()
    {
        static FrameworkElementFactory Cell(string binding, double width, bool trailing = false, bool secondary = false)
        {
            var t = new FrameworkElementFactory(typeof(TextBlock));
            t.SetBinding(TextBlock.TextProperty, new Binding(binding));
            t.SetValue(WidthProperty, width);
            t.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            t.SetValue(DockPanel.DockProperty, Dock.Left);
            if (trailing)
            {
                t.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
                t.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
            }
            if (secondary) t.SetValue(OpacityProperty, 0.6);
            return t;
        }

        var row = new FrameworkElementFactory(typeof(Border));
        row.SetBinding(Border.BackgroundProperty, new Binding(nameof(BlameRow.Background)));
        row.SetValue(Border.PaddingProperty, new Thickness(6, 0, 6, 0));
        var dock = new FrameworkElementFactory(typeof(DockPanel));
        dock.SetValue(DockPanel.LastChildFillProperty, true);
        dock.AppendChild(Cell(nameof(BlameRow.HashText), 72));
        dock.AppendChild(Cell(nameof(BlameRow.Author), 120));
        dock.AppendChild(Cell(nameof(BlameRow.Date), 130));
        dock.AppendChild(Cell(nameof(BlameRow.Number), 44, trailing: true, secondary: true));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(BlameRow.Text)));
        dock.AppendChild(text);
        row.AppendChild(dock);

        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));

        var ic = new ItemsControl
        {
            ItemTemplate = new DataTemplate { VisualTree = row },
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))),
            Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroll },
            FontFamily = Ui.Mono, FontSize = 13, Focusable = true,
        };
        VirtualizingPanel.SetIsVirtualizing(ic, true);
        VirtualizingPanel.SetVirtualizationMode(ic, VirtualizationMode.Recycling);
        ic.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { DataContext: BlameRow r }) Select(r.Hash);
        };
        ic.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { DataContext: BlameRow }) ShowCommitChanges();
        };
        var menu = new ContextMenu();
        menu.Items.Add(FileList.Item("このコミットの変更", ShowCommitChanges, ""));
        menu.Items.Add(FileList.Item("この行より前の Blame", BlameBefore, ""));
        menu.Items.Add(FileList.Item("ハッシュをコピー", () => { if (selectedHash != null) Shell.Copy(selectedHash); }, ""));
        ic.ContextMenu = menu;
        ic.ContextMenuOpening += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { DataContext: BlameRow r }) Select(r.Hash);
            if (selectedHash == null || IsUncommitted(selectedHash)) e.Handled = true;
        };
        return ic;
    }

    private static bool IsUncommitted(string hash) => hash.All(c => c == '0');

    protected internal override async void OnShown()
    {
        var args = new List<string> { "blame", "--porcelain" };
        if (revision != null) args.Add(revision);
        args.Add("--");
        args.Add(repo.Relative(path));
        var r = await repo.GitAsync(args);
        body.Children.Clear();
        if (!r.Ok)
        {
            body.Children.Add(new TextBlock
            {
                Text = r.Message, Foreground = Brushes.Crimson, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }
        var (parsedRows, info) = await Task.Run(() => Parse(r.Out));
        rows = parsedRows;
        commits = info;
        list.ItemsSource = rows;
        body.Children.Add(list);
    }

    private static (List<BlameRow>, Dictionary<string, BlameCommit>) Parse(string output)
    {
        var lines = new List<(string Hash, string Text)>();
        var info = new Dictionary<string, BlameCommit>();
        var current = "";
        foreach (var raw in output.Split('\n'))
        {
            if (raw.StartsWith('\t'))
            {
                lines.Add((current, raw[1..].TrimEnd('\r')));
                continue;
            }
            var first = raw.Split(' ')[0];
            if (first.Length == 40 && first.All(Uri.IsHexDigit))
            {
                current = first;
                if (!info.ContainsKey(current)) info[current] = new BlameCommit();
            }
            else if (!info.TryGetValue(current, out var c)) continue;
            else if (raw.StartsWith("author ")) c.Author = raw[7..];
            else if (raw.StartsWith("author-time ")) c.Time = long.TryParse(raw[12..], out var t) ? DateTimeOffset.FromUnixTimeSeconds(t) : default;
            else if (raw.StartsWith("summary ")) c.Summary = raw[8..];
            else if (raw.StartsWith("filename ")) c.Filename = raw[9..];
        }

        var tints = new Dictionary<string, Brush>();
        Brush Tint(string hash)
        {
            if (tints.TryGetValue(hash, out var b)) return b;
            // Stable hue per commit (pale, low alpha so it works in light and dark themes).
            var hue = (int)(Convert.ToUInt32(hash.Length >= 6 ? hash[..6] : "0", 16) % 360);
            var brush = new SolidColorBrush(HsvColor(hue, 0.25, 1.0, 0x2E));
            brush.Freeze();
            tints[hash] = brush;
            return brush;
        }

        var rows = new List<BlameRow>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            var (hash, text) = lines[i];
            var firstOfBlock = i == 0 || lines[i - 1].Hash != hash;
            info.TryGetValue(hash, out var c);
            rows.Add(new BlameRow
            {
                Number = i + 1, Hash = hash, Text = text.Length == 0 ? " " : text, Tint = Tint(hash),
                HashText = firstOfBlock ? hash[..Math.Min(8, hash.Length)] : "",
                Author = firstOfBlock ? c?.Author ?? "" : "",
                Date = firstOfBlock && c != null ? Ui.FormatDate(c.Time) : "",
            });
        }
        return (rows, info);
    }

    private static Color HsvColor(int hue, double s, double v, byte alpha)
    {
        double c = v * s, x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1)), m = v - c;
        var (r, g, b) = (hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromArgb(alpha, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private void Select(string hash)
    {
        if (selectedHash == hash) return;
        foreach (var r in rows)
        {
            if (r.Hash == selectedHash) r.Selected = false;
            else if (r.Hash == hash) r.Selected = true;
        }
        selectedHash = hash;
        if (commits.TryGetValue(hash, out var c))
        {
            infoText.Text = $"{hash[..Math.Min(8, hash.Length)]}  {c.Author}  {Ui.FormatDate(c.Time)}  {c.Summary}";
            infoText.ToolTip = infoText.Text;
            selectionInfo.Visibility = IsUncommitted(hash) ? Visibility.Collapsed : Visibility.Visible;
        }
        else selectionInfo.Visibility = Visibility.Collapsed;
    }

    private async void ShowCommitChanges()
    {
        if (selectedHash is not { } h || IsUncommitted(h) || !commits.TryGetValue(h, out var c)) return;
        var (parent, files) = await Task.Run(() =>
        {
            var p = repo.Git(["rev-parse", h + "^"]).Out.Trim();
            IReadOnlyList<ChangedFile> f = p.Length == 0 ? [] : repo.ChangedFiles(p, h);
            return (p, f);
        });
        WindowManager.Shared.Show($"GitVelto - {h[..8]} {c.Summary}", new ChangedFilesView(repo, files, parent, h), 640, 480);
    }

    private void BlameBefore()
    {
        if (selectedHash is not { } h || IsUncommitted(h)) return;
        var original = commits.TryGetValue(h, out var c) ? c.Filename : "";
        var p = original.Length == 0 ? path : repo.Absolute(original);
        WindowManager.Shared.Show($"GitVelto - Blame - {h[..8]}^", new BlameView(repo, p, h + "^"), 1000, 720);
    }
}
