using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

public enum FileActionMode { Add, Revert, Resolve }

// MARK: - Add / Revert / Resolve

/// <summary>Checkable file list for "add", "revert" and "resolve" (TortoiseGit style).</summary>
public sealed class FileActionView : GVView
{
    private readonly FileActionMode mode;
    private readonly StatusModel model;
    private readonly FileList files = new(checkable: true) { MinHeight = 140 };
    private readonly CommandPreview preview = new();
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Width = 60, Height = 4, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly CheckBox showIgnored = new() { Content = "無視されたファイルも表示", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel opButtons = new() { Orientation = Orientation.Horizontal };
    private readonly Button okButton;
    private RepoOperationState opState;

    public FileActionView(Repo repo, IReadOnlyList<string> paths, FileActionMode mode)
    {
        this.mode = mode;
        model = new StatusModel(repo, paths);
        if (mode == FileActionMode.Add) model.ShowUntracked = true;

        // --- header ---
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        if (mode == FileActionMode.Add)
        {
            right.Children.Add(showIgnored);
            showIgnored.Click += (_, _) => { model.ShowIgnored = showIgnored.IsChecked == true; Reload(); };
        }
        var all = new Button { Content = "全選択", Margin = new Thickness(0, 0, 6, 0) };
        all.Click += (_, _) => files.SetAllChecked(true);
        var none = new Button { Content = "全解除" };
        none.Click += (_, _) => files.SetAllChecked(false);
        right.Children.Add(all);
        right.Children.Add(none);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        var helpKey = mode switch { FileActionMode.Add => "untracked", FileActionMode.Revert => "revert-file", _ => "conflict" };
        var title = Ui.Heading(Description);
        title.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(Ui.Row(title, new TermHelp(helpKey), loading));

        // --- list ---
        files.Open += e =>
        {
            if (mode == FileActionMode.Resolve) WindowManager.Shared.Perform(GitAction.EditConflict, [model.Repo.Absolute(e.Path)]);
            else model.OpenDiff(e);
        };
        files.MenuBuilder = sel => mode == FileActionMode.Resolve ? ResolveMenu(sel) : model.WorkingTreeMenu(sel, Reload);
        files.CheckedChanged += Update;

        // --- bottom ---
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var cancel = new Button { Content = "キャンセル", MinWidth = 96, IsCancel = true, Margin = new Thickness(8, 0, 8, 0) };
        cancel.Click += (_, _) => Handle.Close();
        okButton = Ui.Accent(new Button { Content = OkTitle, MinWidth = 120, IsDefault = true, IsEnabled = false });
        okButton.Click += (_, _) => Run();
        var bRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bRight.Children.Add(cancel);
        bRight.Children.Add(okButton);
        DockPanel.SetDock(bRight, Dock.Right);
        bottom.Children.Add(bRight);
        if (mode == FileActionMode.Resolve)
        {
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(opButtons);
            left.Children.Add(new TermHelp("ours-theirs"));
            bottom.Children.Add(left);
        }

        var root = new DockPanel { Margin = new Thickness(14) };
        var top = new StackPanel();
        top.Children.Add(new RepoHeader(repo));
        top.Children.Add(header);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(preview);
        root.Children.Add(files);
        Content = root;
        OnRepoChanged(repo.Root, Reload);
    }

    private string Description => mode switch
    {
        FileActionMode.Add => "追加するファイルを選択してください",
        FileActionMode.Revert => "元に戻すファイルを選択してください (変更は失われます)",
        _ => "解決済みにするファイルを選択してください",
    };

    private string OkTitle => mode switch
    {
        FileActionMode.Add => "追加",
        FileActionMode.Revert => "元に戻す",
        _ => "解決済みにする",
    };

    private bool Applies(StatusEntry e) => mode switch
    {
        FileActionMode.Add => e.State is FileState.Untracked or FileState.Ignored,
        FileActionMode.Revert => e.State.IsTracked(),
        _ => e.State == FileState.Conflict,
    };

    protected internal override void OnShown() => Reload();

    private bool reloading, reloadPending;

    private async void Reload()
    {
        if (reloading) { reloadPending = true; return; }
        reloading = true;
        reloadPending = false;
        loading.Visibility = Visibility.Visible;
        try
        {
            var repo = model.Repo;
            var paths = model.WholeProject ? [] : model.Paths;
            var untracked = model.ShowUntracked ? "all" : "no";
            var ignored = model.ShowIgnored;
            var (entries, st) = await Task.Run(() =>
            {
                var l = repo.Status(paths, untracked, ignored).Where(Applies).OrderBy(e => e.Path, NaturalComparer.Instance).ToList();
                return (l, repo.OperationState());
            });
            opState = st;
            // Check everything that applies by default.
            files.SetEntries(entries, _ => true);
            UpdateOperationButtons();
            Update();
        }
        catch (Exception e) { WindowManager.Alert("状態を取得できません", e.Message); }
        finally
        {
            loading.Visibility = Visibility.Collapsed;
            reloading = false;
        }
        if (reloadPending) Reload();
    }

    private void Update()
    {
        okButton.IsEnabled = files.CheckedEntries.Count > 0;
        preview.Set(PreviewCommands());
    }

    private List<IReadOnlyList<string>> PreviewCommands()
    {
        var sel = files.CheckedEntries;
        if (sel.Count == 0) return [];
        switch (mode)
        {
            case FileActionMode.Add: return [new List<string> { "add", "-f", "--" }.Concat(sel.Select(e => e.Path)).ToList()];
            case FileActionMode.Resolve: return [new List<string> { "add", "--" }.Concat(sel.Select(e => e.Path)).ToList()];
            default:
                var output = new List<IReadOnlyList<string>>();
                static bool IsNew(StatusEntry e) => e.State is FileState.Added or FileState.Renamed or FileState.Copied;
                var unstage = sel.Where(IsNew).Select(e => e.Path).ToList();
                var restore = sel.Where(e => !IsNew(e)).Select(e => e.Path).Concat(sel.Where(e => e.OrigPath != null).Select(e => e.OrigPath!)).ToList();
                if (unstage.Count > 0) output.Add(["rm", "--cached", "-r", "-q", "--", .. unstage]);
                if (restore.Count > 0) output.Add(["restore", "--source=HEAD", "--staged", "--worktree", "--", .. restore]);
                return output;
        }
    }

    /// <summary>Merge/rebase state label with continue / abort buttons (resolve mode only).</summary>
    private void UpdateOperationButtons()
    {
        opButtons.Children.Clear();
        var st = opState;
        if (mode != FileActionMode.Resolve || st.Command() is not { } cmd) return;
        opButtons.Children.Add(new TextBlock
        {
            Text = st.Label(), Foreground = (Brush)Application.Current.FindResource("StateConflict"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        var root = model.Repo.Root;
        if (st != RepoOperationState.Merging)
        {
            var cont = new Button { Content = "続行", Margin = new Thickness(0, 0, 6, 0) };
            cont.Click += (_, _) =>
            {
                WindowManager.Shared.RunProgress($"{st.Label()}の続行", root, [ProgressStep.Git(cmd, "--continue")]);
                Handle.Close();
            };
            opButtons.Children.Add(cont);
        }
        var abort = new Button { Content = "中止 (abort)" };
        abort.Click += (_, _) =>
        {
            if (!WindowManager.Confirm($"{st.Label()}を中止しますか?", "操作前の状態に戻ります。", "中止", true)) return;
            WindowManager.Shared.RunProgress($"{st.Label()}の中止", root, [ProgressStep.Git(cmd, "--abort")]);
            Handle.Close();
        };
        opButtons.Children.Add(abort);
    }

    private ContextMenu ResolveMenu(IReadOnlyList<StatusEntry> sel)
    {
        var m = new ContextMenu();
        var repo = model.Repo;
        if (sel.Count == 1)
            m.Items.Add(FileList.Item("競合を編集…", () => WindowManager.Shared.Perform(GitAction.EditConflict, [repo.Absolute(sel[0].Path)]), ""));
        m.Items.Add(FileList.Item("自分の変更を使用 (ours)", () => Checkout(sel, "--ours")));
        m.Items.Add(FileList.Item("相手の変更を使用 (theirs)", () => Checkout(sel, "--theirs")));
        m.Items.Add(FileList.Item("エディタで開く", () => { foreach (var e in sel) Shell.OpenFile(repo.Absolute(e.Path)); }, ""));
        m.Items.Add(FileList.Item("差分を表示", () => { if (sel.Count > 0) model.OpenDiff(sel[0]); }, ""));
        return m;
    }

    private async void Checkout(IReadOnlyList<StatusEntry> sel, string side)
    {
        var repo = model.Repo;
        var rels = sel.Select(e => e.Path).ToList();
        var r = await Task.Run(() =>
        {
            CommandHistory.Shared.Record(["checkout", side, "--", .. rels], repo.Root);
            var res = repo.Git(["--literal-pathspecs", "checkout", side, "--", .. rels]);
            if (res.Ok)
            {
                CommandHistory.Shared.Record(["add", "--", .. rels], repo.Root, true);
                repo.Git(["--literal-pathspecs", "add", "--", .. rels]);
            }
            return res;
        });
        if (!r.Ok) WindowManager.Alert("失敗しました", r.Message);
        WindowManager.NotifyRepoChanged(repo.Root);
        Reload();
    }

    private void Run()
    {
        var repo = model.Repo;
        var sel = files.CheckedEntries;
        if (sel.Count == 0) return;
        var steps = new List<ProgressStep>();
        switch (mode)
        {
            case FileActionMode.Add:
                if (repo.PathspecFile(sel.Select(e => e.Path)) is { } f1)
                    steps.Add(ProgressStep.Git("--literal-pathspecs", "add", "-f", $"--pathspec-from-file={f1}", "--pathspec-file-nul"));
                break;
            case FileActionMode.Revert:
                if (!WindowManager.Confirm($"{sel.Count} 個のファイルの変更を元に戻しますか?", "この操作は取り消せません。", "元に戻す", true)) return;
                steps = GitOps.RevertSteps(repo, sel);
                break;
            case FileActionMode.Resolve:
                if (repo.PathspecFile(sel.Select(e => e.Path)) is { } f2)
                    steps.Add(ProgressStep.Git("--literal-pathspecs", "add", $"--pathspec-from-file={f2}", "--pathspec-file-nul"));
                break;
        }
        var root = repo.Root;
        var actions = mode == FileActionMode.Resolve
            ? new List<ProgressAction> { new("コミット…", () => WindowManager.Shared.Perform(GitAction.Commit, [root])) }
            : [];
        WindowManager.Shared.RunProgress(OkTitle, root, steps, actions);
        Handle.Close();
    }
}

// MARK: - Delete

/// <summary>git rm (optionally keeping the local files).</summary>
public sealed class DeleteView : GVView
{
    private readonly Repo repo;
    private readonly IReadOnlyList<string> paths;
    private readonly CheckBox keepLocal = new() { Content = "ローカルのファイルは残す (--cached)", Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox force = new() { Content = "変更があっても削除する (-f)", Margin = new Thickness(0, 4, 0, 0) };
    private readonly CommandPreview preview = new();

    public DeleteView(Repo repo, IReadOnlyList<string> paths, bool keepLocal)
    {
        this.repo = repo;
        this.paths = paths;
        this.keepLocal.IsChecked = keepLocal;

        var list = new ListBox { MinHeight = 80 };
        foreach (var p in paths) list.Items.Add(new TextBlock { Text = repo.Relative(p), TextTrimming = TextTrimming.CharacterEllipsis });

        this.keepLocal.Click += (_, _) => UpdatePreview();
        force.Click += (_, _) => UpdatePreview();

        var bottom = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
        var buttons = Ui.Buttons(this, "削除", Run);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        var help = TermHelp.Label("Git から削除とは", "tracked");
        help.VerticalAlignment = VerticalAlignment.Bottom;
        help.Margin = new Thickness(0, 12, 0, 0);
        if (help.Children[0] is TextBlock t) { t.FontSize = 12; t.Foreground = Ui.Secondary(); }
        bottom.Children.Add(help);

        var top = new StackPanel();
        var heading = Ui.Heading("次の項目を Git から削除します");
        heading.Margin = new Thickness(0, 0, 0, 8);
        top.Children.Add(heading);
        var opts = new StackPanel();
        opts.Children.Add(this.keepLocal);
        opts.Children.Add(force);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        DockPanel.SetDock(opts, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(preview);
        root.Children.Add(opts);
        root.Children.Add(list);
        Content = root;
        UpdatePreview();
    }

    private List<string> Args(bool literal)
    {
        var args = literal ? new List<string> { "--literal-pathspecs", "rm", "-r" } : ["rm", "-r"];
        if (keepLocal.IsChecked == true) args.Add("--cached");
        if (force.IsChecked == true) args.Add("-f");
        args.Add("--");
        args.AddRange(paths.Select(repo.Relative));
        return args;
    }

    private void UpdatePreview() => preview.Set(Args(false));

    private void Run()
    {
        WindowManager.Shared.RunProgress("削除", repo.Root, [ProgressStep.Git(Args(true))]);
        Handle.Close();
    }
}

// MARK: - Rename

/// <summary>git mv (a name containing "/" moves the file).</summary>
public sealed class RenameView : GVView
{
    private readonly Repo repo;
    private readonly string path;
    private readonly TextBox newName = new();
    private readonly CommandPreview preview = new();
    private readonly Button okButton;

    public RenameView(Repo repo, string path)
    {
        this.repo = repo;
        this.path = path;
        newName.Text = Path.GetFileName(path.TrimEnd('\\', '/'));
        newName.ToolTip = "新しい名前 (フォルダを含めて指定すると移動)";

        var heading = Ui.Heading($"{repo.Relative(path)} の新しい名前");
        heading.TextWrapping = TextWrapping.Wrap;
        heading.Margin = new Thickness(0, 0, 0, 8);
        var hint = Ui.Caption("フォルダを含めて指定すると移動します (例: docs/新しい名前.txt)");
        hint.Margin = new Thickness(0, 4, 0, 0);

        var buttons = Ui.Buttons(this, "名前を変更", Run);
        okButton = (Button)buttons.Children[buttons.Children.Count - 1];
        newName.TextChanged += (_, _) => Update();

        var top = new StackPanel();
        top.Children.Add(heading);
        top.Children.Add(newName);
        top.Children.Add(hint);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(preview);
        root.Children.Add(new Border());
        Content = root;
        Update();
        Loaded += (_, _) =>
        {
            newName.Focus();
            // Select the base name (without the extension), like Explorer.
            var ext = Path.GetExtension(newName.Text);
            newName.Select(0, ext.Length > 0 && ext.Length < newName.Text.Length ? newName.Text.Length - ext.Length : newName.Text.Length);
        };
    }

    private string RenameTarget
    {
        get
        {
            var name = newName.Text.Trim().Replace('\\', '/');
            var rel = repo.Relative(path);
            var i = rel.LastIndexOf('/');
            var dir = i < 0 ? "" : rel[..i];
            return name.Contains('/') ? name : (dir.Length == 0 ? name : dir + "/" + name);
        }
    }

    private void Update()
    {
        okButton.IsEnabled = newName.Text.Trim().Length > 0;
        preview.Set(["mv", "--", repo.Relative(path), RenameTarget]);
    }

    private async void Run()
    {
        if (newName.Text.Trim().Length == 0) return;
        var src = repo.Relative(path);
        var target = RenameTarget;
        okButton.IsEnabled = false;
        var r = await Task.Run(() =>
        {
            var id = CommandHistory.Shared.Record(["mv", "--", src, target], repo.Root);
            var res = repo.Git(["mv", "--", src, target]);
            CommandHistory.Shared.Finish(id, res.Ok);
            return res;
        });
        if (r.Ok)
        {
            WindowManager.NotifyRepoChanged(repo.Root);
            Handle.Close();
        }
        else
        {
            okButton.IsEnabled = true;
            WindowManager.Alert("名前を変更できません", r.Message);
        }
    }
}

// MARK: - Ignore

/// <summary>Adds patterns to .gitignore / .git/info/exclude (optionally untracking the files).</summary>
public sealed class IgnoreView : GVView
{
    private enum How { Path, Name, Ext }

    private readonly Repo repo;
    private readonly IReadOnlyList<string> paths;
    private readonly RadioButton byPath = new() { Content = "パスで指定", GroupName = "how", IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
    private readonly RadioButton byName = new() { Content = "名前で指定", GroupName = "how", Margin = new Thickness(0, 0, 14, 0) };
    private readonly RadioButton byExt = new() { Content = "拡張子で指定", GroupName = "how" };
    private readonly ComboBox target = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox untrack = new() { Content = "追跡中の場合はインデックスから削除 (git rm --cached)", IsChecked = true };
    private readonly TextBox patternsBox = new()
    {
        IsReadOnly = true, FontSize = 12, TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(6),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
    };
    private readonly TextBlock targetText = new() { FontSize = 12, Opacity = 0.72, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    public IgnoreView(Repo repo, IReadOnlyList<string> paths)
    {
        this.repo = repo;
        this.paths = paths;
        patternsBox.FontFamily = Ui.Mono;
        target.Items.Add("リポジトリ直下の .gitignore");
        target.Items.Add("同じフォルダの .gitignore");
        target.Items.Add(".git/info/exclude (共有しない)");
        target.SelectedIndex = 0;

        foreach (var rb in new[] { byPath, byName, byExt }) rb.Checked += (_, _) => Update();
        target.SelectionChanged += (_, _) => Update();

        var howRow = new StackPanel { Orientation = Orientation.Horizontal };
        howRow.Children.Add(byPath);
        howRow.Children.Add(byName);
        howRow.Children.Add(byExt);
        var form = Ui.Form(("指定方法", howRow), ("追加先", target), ("", untrack));

        var patternsHeader = Ui.Row(Ui.Heading("追加されるパターン:"), new TermHelp("ignore"));
        patternsHeader.Margin = new Thickness(0, 10, 0, 4);

        var top = new StackPanel();
        top.Children.Add(form);
        top.Children.Add(patternsHeader);

        var buttons = Ui.Buttons(this, "追加", Run);
        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(targetText, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(targetText);
        root.Children.Add(patternsBox);
        Content = root;
        Update();
    }

    private How Mode => byName.IsChecked == true ? How.Name : byExt.IsChecked == true ? How.Ext : How.Path;

    private List<string> Patterns()
    {
        var output = new List<string>();
        var how = Mode;
        var sameFolder = target.SelectedIndex == 1;
        foreach (var p in paths)
        {
            var rel = repo.Relative(p);
            var isDir = Directory.Exists(p);
            var name = rel.Split('/').Last();
            var slash = isDir ? "/" : "";
            switch (how)
            {
                case How.Path:
                    output.Add(sameFolder ? "/" + name + slash : "/" + rel + slash);
                    break;
                case How.Name:
                    output.Add(name + slash);
                    break;
                default:
                    var ext = Path.GetExtension(name).TrimStart('.');
                    output.Add(ext.Length == 0 ? name : "*." + ext);
                    break;
            }
        }
        return output.Distinct().ToList();
    }

    private string TargetFile => target.SelectedIndex switch
    {
        1 => Path.Combine(paths.FirstOrDefault() is { } first ? Path.GetDirectoryName(first.TrimEnd('\\')) ?? repo.Root : repo.Root, ".gitignore"),
        2 => Path.Combine(repo.GitDir, "info", "exclude"),
        _ => Path.Combine(repo.Root, ".gitignore"),
    };

    private void Update()
    {
        patternsBox.Text = string.Join("\n", Patterns());
        targetText.Text = TargetFile;
    }

    private void Run()
    {
        var file = TargetFile;
        var add = Patterns();
        try
        {
            string content = File.Exists(file) ? File.ReadAllText(file) : "";
            var existing = new HashSet<string>(content.Split('\n').Select(l => l.Trim()));
            add = add.Where(p => !existing.Contains(p)).ToList();
            if (add.Count > 0)
            {
                // Keep the file's line endings.
                var eol = content.Contains("\r\n") ? "\r\n" : "\n";
                if (content.Length > 0 && !content.EndsWith('\n')) content += eol;
                content += string.Join(eol, add) + eol;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var hadBom = File.Exists(file) && HasUtf8Bom(file);
                File.WriteAllText(file, content, new UTF8Encoding(hadBom));
            }
        }
        catch (Exception e)
        {
            WindowManager.Alert("書き込めません", e.Message);
            return;
        }
        if (untrack.IsChecked == true)
        {
            var rels = paths.Select(repo.Relative).ToList();
            CommandHistory.Shared.Record(["rm", "-r", "--cached", "-q", "--ignore-unmatch", "--", .. rels], repo.Root, true);
            repo.Git(["--literal-pathspecs", "rm", "-r", "--cached", "-q", "--ignore-unmatch", "--", .. rels]);
        }
        WindowManager.NotifyRepoChanged(repo.Root);
        Handle.Close();
    }

    private static bool HasUtf8Bom(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            Span<byte> b = stackalloc byte[3];
            return fs.Read(b) == 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
        }
        catch { return false; }
    }
}

// MARK: - Clean up

/// <summary>git clean with a preview list (optionally moving the items to the Recycle Bin).</summary>
public sealed class CleanView : GVView
{
    private enum Scope { Untracked, Ignored, Both }

    private sealed class CleanItem(string path) : INotifyPropertyChanged
    {
        public string Path { get; } = path;
        public ImageSource? Icon => Shell.Icon(Path.TrimEnd('/'), Path.EndsWith('/'));
        private bool isChecked = true;
        public bool IsChecked
        {
            get => isChecked;
            set { if (isChecked == value) return; isChecked = value; OnChanged(); CheckedChanged?.Invoke(); }
        }
        public Action? CheckedChanged;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private readonly Repo repo;
    private readonly IReadOnlyList<string> paths;
    private readonly ObservableCollection<CleanItem> items = [];
    private readonly RadioButton untrackedOnly = new() { Content = "未追跡のみ", GroupName = "scope", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
    private readonly RadioButton ignoredOnly = new() { Content = "無視のみ", GroupName = "scope", Margin = new Thickness(0, 0, 12, 0) };
    private readonly RadioButton both = new() { Content = "未追跡+無視", GroupName = "scope", Margin = new Thickness(0, 0, 16, 0) };
    private readonly CheckBox dirs = new() { Content = "フォルダも含める", IsChecked = true };
    private readonly CheckBox toTrash = new() { Content = "ごみ箱に移動する", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Width = 60, Height = 4, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock countText = new() { Foreground = Ui.Secondary(), VerticalAlignment = VerticalAlignment.Center };
    private readonly CommandPreview preview = new();
    private readonly ListBox list = new() { MinHeight = 120 };
    private readonly Button okButton;
    private int generation;

    public CleanView(Repo repo, IReadOnlyList<string> paths)
    {
        this.repo = repo;
        this.paths = paths;

        // --- list (virtualized, check box + shell icon + path) ---
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding(nameof(CleanItem.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        check.SetValue(MinWidthProperty, 0.0);
        check.SetValue(FocusableProperty, false);
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var img = new FrameworkElementFactory(typeof(Image));
        img.SetBinding(Image.SourceProperty, new Binding(nameof(CleanItem.Icon)));
        img.SetValue(WidthProperty, 16.0);
        img.SetValue(HeightProperty, 16.0);
        img.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        var txt = new FrameworkElementFactory(typeof(TextBlock));
        txt.SetBinding(TextBlock.TextProperty, new Binding(nameof(CleanItem.Path)));
        txt.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(img);
        panel.AppendChild(txt);
        check.AppendChild(panel);
        list.ItemTemplate = new DataTemplate { VisualTree = check };
        list.ItemsSource = items;
        list.SelectionMode = SelectionMode.Extended;
        VirtualizingPanel.SetIsVirtualizing(list, true);
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Space || list.SelectedItems.Count == 0) return;
            var sel = list.SelectedItems.OfType<CleanItem>().ToList();
            var value = !sel.All(i => i.IsChecked);
            foreach (var i in sel) i.IsChecked = value;
            e.Handled = true;
        };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is CleanItem i) Shell.Reveal([AbsolutePath(i.Path)]); };
        var menu = new ContextMenu();
        menu.Items.Add(FileList.Item("エクスプローラーで表示", () => Shell.Reveal(list.SelectedItems.OfType<CleanItem>().Select(i => AbsolutePath(i.Path))), ""));
        menu.Items.Add(FileList.Item("パスをコピー", () => Shell.Copy(string.Join("\n", list.SelectedItems.OfType<CleanItem>().Select(i => i.Path))), ""));
        list.ContextMenu = menu;

        // --- options ---
        foreach (var rb in new[] { untrackedOnly, ignoredOnly, both }) rb.Checked += (_, _) => Preview();
        dirs.Click += (_, _) => Preview();
        toTrash.IsChecked = AppSettings.Shared.CleanToTrash;
        toTrash.Click += (_, _) => { AppSettings.Shared.CleanToTrash = toTrash.IsChecked == true; UpdateState(); };

        var options = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(loading, Dock.Right);
        options.Children.Add(loading);
        var optRow = new StackPanel { Orientation = Orientation.Horizontal };
        optRow.Children.Add(new TextBlock { Text = "対象", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        optRow.Children.Add(untrackedOnly);
        optRow.Children.Add(ignoredOnly);
        optRow.Children.Add(both);
        optRow.Children.Add(TermHelp.With(dirs, "clean"));
        options.Children.Add(optRow);

        // --- bottom ---
        var buttons = Ui.Buttons(this, "削除", Run);
        okButton = (Button)buttons.Children[buttons.Children.Count - 1];
        okButton.IsEnabled = false;
        var bottom = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 12, 0, 4) };
        left.Children.Add(countText);
        left.Children.Add(toTrash);
        bottom.Children.Add(left);

        var top = new StackPanel();
        top.Children.Add(new RepoHeader(repo));
        top.Children.Add(options);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(preview);
        root.Children.Add(list);
        Content = root;
        UpdateState();
    }

    protected internal override void OnShown() => Preview();

    private Scope CurrentScope => ignoredOnly.IsChecked == true ? Scope.Ignored : both.IsChecked == true ? Scope.Both : Scope.Untracked;

    private List<string> CleanArgs(bool dryRun)
    {
        var args = new List<string> { "clean", dryRun ? "-n" : "-f" };
        if (dirs.IsChecked == true) args.Add("-d");
        switch (CurrentScope)
        {
            case Scope.Ignored: args.Add("-X"); break;
            case Scope.Both: args.Add("-x"); break;
        }
        return args;
    }

    private string AbsolutePath(string rel) => repo.Absolute(rel.TrimEnd('/'));

    private async void Preview()
    {
        var gen = ++generation;
        loading.Visibility = Visibility.Visible;
        var args = CleanArgs(true);
        var rels = paths.Select(repo.Relative).Where(r => r.Length > 0).ToList();
        if (rels.Count > 0) { args.Insert(0, "--literal-pathspecs"); args.Add("--"); args.AddRange(rels); }
        var r = await Task.Run(() => repo.Git(args));
        if (gen != generation) return;
        const string prefix = "Would remove ";
        items.Clear();
        foreach (var raw in r.Out.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (!l.StartsWith(prefix)) continue;
            var item = new CleanItem(l[prefix.Length..]) { CheckedChanged = UpdateState };
            items.Add(item);
        }
        loading.Visibility = Visibility.Collapsed;
        if (!r.Ok) WindowManager.Alert("一覧を取得できません", r.Message);
        UpdateState();
    }

    private void UpdateState()
    {
        var n = items.Count(i => i.IsChecked);
        countText.Text = $"{n} / {items.Count} 件";
        okButton.IsEnabled = n > 0;
        var trash = AppSettings.Shared.CleanToTrash;
        preview.Set(trash ? [CleanArgs(true)] : [CleanArgs(true), CleanArgs(false)],
            trash ? "「ごみ箱に移動する」がオンの場合、git clean -n で一覧を出した後、エクスプローラーと同じようにごみ箱へ移動します（git clean -f は使いません）。" : null);
    }

    private void Run()
    {
        var sel = items.Where(i => i.IsChecked).Select(i => i.Path).ToList();
        if (sel.Count == 0) return;
        var trash = AppSettings.Shared.CleanToTrash;
        if (!WindowManager.Confirm($"{sel.Count} 件を削除しますか?", trash ? "ごみ箱に移動します。" : "完全に削除されます。", "削除", !trash)) return;
        var root = repo.Root;
        if (trash)
        {
            WindowManager.Shared.RunProgress("クリーンアップ", root, [new ProgressStep.Custom("ごみ箱に移動しています…", () =>
            {
                var output = new List<string>();
                foreach (var rel in sel)
                {
                    Shell.MoveToRecycleBin(Path.Combine(root, rel.TrimEnd('/').Replace('/', '\\')));
                    output.Add($"ごみ箱へ: {rel}");
                }
                return string.Join("\n", output);
            })]);
        }
        else
        {
            var args = CleanArgs(false);
            args.Insert(0, "--literal-pathspecs");
            args.Add("--");
            args.AddRange(sel);
            WindowManager.Shared.RunProgress("クリーンアップ", root, [ProgressStep.Git(args)]);
        }
        Handle.Close();
    }
}
