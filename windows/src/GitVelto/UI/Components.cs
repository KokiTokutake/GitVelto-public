using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Header showing repository + branch (+ merge/rebase state), used by most dialogs.</summary>
public sealed class RepoHeader : DockPanel
{
    private readonly Repo repo;
    private readonly TextBlock branch = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border opBadge = new()
    {
        CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 10, 0),
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0x8A, 0x00)), Visibility = Visibility.Collapsed,
    };

    public RepoHeader(Repo repo)
    {
        this.repo = repo;
        Margin = new Thickness(0, 0, 0, 8);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(opBadge);
        right.Children.Add(Ui.Glyph("", 13));
        branch.Margin = new Thickness(6, 0, 0, 0);
        right.Children.Add(branch);
        SetDock(right, Dock.Right);
        Children.Add(right);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Ui.Glyph("", 13));
        left.Children.Add(new TextBlock { Text = repo.Root, Foreground = Ui.Secondary(), Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(left);

        void Handler(string root) { if (PathUtil.SamePath(root, repo.Root)) Dispatcher.BeginInvoke(Refresh); }
        Loaded += (_, _) => { WindowManager.RepoChanged -= Handler; WindowManager.RepoChanged += Handler; Refresh(); };
        Unloaded += (_, _) => WindowManager.RepoChanged -= Handler;
    }

    public async void Refresh()
    {
        var (b, s) = await Task.Run(() => (repo.HeadDescription(), repo.OperationState()));
        branch.Text = b;
        opBadge.Child = new TextBlock { Text = s.Label(), FontSize = 12 };
        opBadge.Visibility = s == RepoOperationState.None ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>Editable combo box listing branches / remote branches / tags (grouped by headers).</summary>
public sealed class RefPicker : ComboBox
{
    public RefPicker(IEnumerable<RefInfo> refs, bool local = true, bool remote = true, bool tags = true)
    {
        IsEditable = true;
        MinWidth = 220;
        IsTextSearchEnabled = true;
        SetRefs(refs, local, remote, tags);
    }

    public void SetRefs(IEnumerable<RefInfo> refs, bool local = true, bool remote = true, bool tags = true)
    {
        var text = Text;
        Items.Clear();
        void Group(string title, IEnumerable<RefInfo> list)
        {
            var l = list.ToList();
            if (l.Count == 0) return;
            Items.Add(new ComboBoxItem { Content = title, IsEnabled = false, FontWeight = FontWeights.SemiBold, FontSize = 11 });
            foreach (var r in l) Items.Add(new ComboBoxItem { Content = r.Name, Tag = r.Name, Padding = new Thickness(18, 4, 8, 4) });
        }
        var all = refs.ToList();
        if (local) Group("ローカルブランチ", all.Where(r => r.Kind == RefKind.Local));
        if (remote) Group("リモートブランチ", all.Where(r => r.Kind == RefKind.Remote));
        if (tags) Group("タグ", all.Where(r => r.Kind == RefKind.Tag));
        Text = text;
    }

    /// <summary>Selected or typed reference.</summary>
    public string Value
    {
        get => (SelectedItem as ComboBoxItem)?.Tag as string ?? Text.Trim();
        set
        {
            var item = Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == value);
            if (item != null) SelectedItem = item; else Text = value;
        }
    }
}

/// <summary>Status entries of a work tree (shared by the commit and "check for modifications" windows).</summary>
public sealed class StatusModel
{
    public Repo Repo { get; }
    public IReadOnlyList<string> Paths { get; }
    public bool HasPathFilter => Paths.Count > 0;
    public bool ShowUntracked { get; set; } = true;
    public bool ShowIgnored { get; set; }
    public bool WholeProject { get; set; }
    public RepoOperationState OpState { get; private set; }
    private bool firstLoad = true;

    public StatusModel(Repo repo, IEnumerable<string> paths)
    {
        Repo = repo;
        Paths = paths.Where(p => !PathUtil.SamePath(p, repo.Root)).ToList();
    }

    /// <summary>Loads entries into <paramref name="list"/>, keeping check marks (TortoiseGit defaults on first load).</summary>
    public async Task Reload(FileList list)
    {
        var paths = WholeProject ? [] : Paths;
        bool untracked = ShowUntracked, ignored = ShowIgnored;
        var (entries, st) = await Task.Run(() =>
        {
            var l = Repo.Status(paths, untracked ? "all" : "no", ignored).OrderBy(e => e.Path, NaturalComparer.Instance).ToList();
            return (l, Repo.OperationState());
        });
        OpState = st;
        var explicitRel = new HashSet<string>(paths.Select(Repo.Relative), StringComparer.OrdinalIgnoreCase);
        if (firstLoad)
        {
            // TortoiseGit default: tracked changes checked; untracked only if explicitly selected.
            list.SetEntries(entries, e => e.State.IsTracked() || explicitRel.Contains(e.Path));
            firstLoad = false;
        }
        else
        {
            var known = list.Entries.ToDictionary(e => e.Id);
            var wasChecked = new HashSet<string>(list.CheckedEntries.Select(e => e.Id));
            list.SetEntries(entries, e => known.ContainsKey(e.Id) ? wasChecked.Contains(e.Id) : e.State.IsTracked());
        }
    }

    public void OpenDiff(StatusEntry e) =>
        WindowManager.Shared.ShowDiff(Repo, new DiffSource.WorkingTree(e.Path, e.OrigPath, e.State));

    /// <summary>Shared context menu for working-tree file lists.</summary>
    public ContextMenu WorkingTreeMenu(IReadOnlyList<StatusEntry> sel, Action reload)
    {
        var m = new ContextMenu();
        var abs = sel.Select(e => Repo.Absolute(e.Path)).ToList();
        if (sel.Count == 1) m.Items.Add(FileList.Item("差分を表示", () => OpenDiff(sel[0]), ""));
        if (sel.Any(e => e.State == FileState.Untracked))
        {
            m.Items.Add(FileList.Item("追加 (git add)", () =>
            {
                var rels = sel.Where(e => e.State == FileState.Untracked).Select(e => e.Path).ToList();
                CommandHistory.Shared.Record(["add", "--", .. rels], Repo.Root, true);
                Repo.Git(["--literal-pathspecs", "add", "--", .. rels]);
                WindowManager.NotifyRepoChanged(Repo.Root);
                reload();
            }, ""));
        }
        if (sel.Any(e => e.State.IsTracked())) m.Items.Add(FileList.Item("元に戻す…", () => WindowManager.Shared.Perform(GitAction.Revert, abs), ""));
        if (sel.Any(e => e.State == FileState.Conflict))
        {
            if (sel.Count == 1) m.Items.Add(FileList.Item("競合を編集…", () => WindowManager.Shared.Perform(GitAction.EditConflict, abs), ""));
            m.Items.Add(FileList.Item("競合を解決…", () => WindowManager.Shared.Perform(GitAction.Resolve, abs)));
        }
        if (sel.Any(e => e.Staged))
        {
            m.Items.Add(FileList.Item("ステージを解除", () =>
            {
                var rels = sel.Where(e => e.Staged).Select(e => e.Path).ToList();
                CommandHistory.Shared.Record(["restore", "--staged", "--", .. rels], Repo.Root, true);
                Repo.Git(["--literal-pathspecs", "restore", "--staged", "--", .. rels]);
                WindowManager.NotifyRepoChanged(Repo.Root);
                reload();
            }));
        }
        m.Items.Add(FileList.Item("無視リストに追加…", () => WindowManager.Shared.Perform(GitAction.Ignore, abs), ""));
        m.Items.Add(new Separator());
        m.Items.Add(FileList.Item("開く", () => abs.ForEach(Shell.OpenFile), ""));
        m.Items.Add(FileList.Item("エクスプローラーで表示", () => Shell.Reveal(abs), ""));
        m.Items.Add(FileList.Item("パスをコピー", () => Shell.Copy(string.Join("\n", sel.Select(e => e.Path))), ""));
        if (sel.Count == 1)
        {
            m.Items.Add(FileList.Item("ログを表示", () => WindowManager.Shared.Perform(GitAction.Log, abs), ""));
            if (sel[0].State != FileState.Untracked) m.Items.Add(FileList.Item("Blame", () => WindowManager.Shared.Perform(GitAction.Blame, abs), ""));
        }
        return m;
    }
}

/// <summary>What a diff window compares.</summary>
public abstract record DiffSource
{
    /// <summary>Repository-relative path ("/" separators) or file path for <see cref="Files"/>.</summary>
    public abstract string Path { get; }
    public abstract string Subtitle { get; }

    public sealed record WorkingTree(string RelPath, string? OrigPath, FileState State) : DiffSource
    {
        public override string Path => RelPath;
        public override string Subtitle => "HEAD ↔ 作業ツリー";
    }

    public sealed record CommitDiff(string Hash, string? Parent, string RelPath, string? OldPath) : DiffSource
    {
        public override string Path => RelPath;
        public override string Subtitle => $"{(Parent == null ? "(なし)" : Parent[..Math.Min(8, Parent.Length)])} ↔ {Hash[..Math.Min(8, Hash.Length)]}";
    }

    public sealed record Range(string From, string To, string RelPath, string? OldPath) : DiffSource
    {
        public override string Path => RelPath;
        public override string Subtitle => $"{From[..Math.Min(12, From.Length)]} ↔ {To[..Math.Min(12, To.Length)]}";
    }

    public sealed record RevToWorking(string Rev, string RelPath) : DiffSource
    {
        public override string Path => RelPath;
        public override string Subtitle => $"{Rev[..Math.Min(12, Rev.Length)]} ↔ 作業ツリー";
    }

    public sealed record Files(string Left, string Right) : DiffSource
    {
        public override string Path => Right;
        public override string Subtitle => $"{System.IO.Path.GetFileName(Left)} ↔";
    }

    /// <summary>git diff arguments producing the unified diff.</summary>
    public List<string> Args(Repo repo, bool ignoreWhitespace)
    {
        var ws = ignoreWhitespace ? new List<string> { "-w" } : [];
        static string[] Two(string? o, string p) => o != null ? [o, p] : [p];
        return this switch
        {
            WorkingTree w when w.State == FileState.Untracked || (!repo.HasHead() && w.State != FileState.Deleted)
                => ["diff", "--no-index", .. ws, "--", "/dev/null", w.RelPath],
            WorkingTree w => ["--literal-pathspecs", "diff", "-M", "HEAD", .. ws, "--", .. Two(w.OrigPath, w.RelPath)],
            CommitDiff c when c.Parent == null => ["--literal-pathspecs", "show", "--format=", c.Hash, .. ws, "--", c.RelPath],
            CommitDiff c => ["--literal-pathspecs", "diff", "-M", c.Parent!, c.Hash, .. ws, "--", .. Two(c.OldPath, c.RelPath)],
            Range r => ["--literal-pathspecs", "diff", "-M", r.From, r.To, .. ws, "--", .. Two(r.OldPath, r.RelPath)],
            RevToWorking v => ["--literal-pathspecs", "diff", v.Rev, .. ws, "--", v.RelPath],
            Files f => ["diff", "--no-index", .. ws, "--", f.Left, f.Right],
            _ => [],
        };
    }

    /// <summary>Two files on disk for external diff tools.</summary>
    public (string Left, string Right) FilesForExternal(Repo repo)
    {
        string Blob(string rev, string p) => repo.ExportBlob(rev, p) ?? KGTemp.EmptyFile();
        return this switch
        {
            WorkingTree w => (w.State is FileState.Untracked or FileState.Added ? KGTemp.EmptyFile() : Blob("HEAD", w.OrigPath ?? w.RelPath), repo.Absolute(w.RelPath)),
            CommitDiff c => (c.Parent != null ? Blob(c.Parent, c.OldPath ?? c.RelPath) : KGTemp.EmptyFile(), Blob(c.Hash, c.RelPath)),
            Range r => (Blob(r.From, r.OldPath ?? r.RelPath), Blob(r.To, r.RelPath)),
            RevToWorking v => (Blob(v.Rev, v.RelPath), repo.Absolute(v.RelPath)),
            Files f => (f.Left, f.Right),
            _ => (KGTemp.EmptyFile(), KGTemp.EmptyFile()),
        };
    }
}

/// <summary>External diff / merge tools configured in the settings ($LOCAL $REMOTE $BASE $MERGED).</summary>
public static class ExternalTools
{
    public static void RunTemplate(string template, Dictionary<string, string> vars, string dir)
    {
        var cmd = template;
        foreach (var (k, v) in vars)
            cmd = cmd.Replace($"\"${k}\"", $"\"{v}\"").Replace($"${k}", $"\"{v}\"");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe") { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(cmd);
            foreach (var kv in Git.BuildEnvironment()) psi.Environment[kv.Key] = kv.Value;
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception e) { WindowManager.Alert("外部ツールを起動できません", e.Message); }
    }

    public static void Diff(string left, string right, string dir) =>
        RunTemplate(AppSettings.Shared.ExternalDiff, new() { ["LOCAL"] = left, ["REMOTE"] = right }, dir);

    /// <summary>Writes stage 1/2/3 versions and launches the external merge tool for a conflicted file.</summary>
    public static void Merge(Repo repo, string path)
    {
        var rel = repo.Relative(path);
        var b = repo.ExportBlob(":1", rel) ?? KGTemp.EmptyFile();
        var l = repo.ExportBlob(":2", rel) ?? KGTemp.EmptyFile();
        var r = repo.ExportBlob(":3", rel) ?? KGTemp.EmptyFile();
        RunTemplate(AppSettings.Shared.ExternalMerge, new() { ["BASE"] = b, ["LOCAL"] = l, ["REMOTE"] = r, ["MERGED"] = path }, repo.Root);
    }

    public static readonly (string Name, string Diff, string Merge)[] Presets =
    [
        ("Visual Studio Code", "code --wait --diff \"$LOCAL\" \"$REMOTE\"", "code --wait --merge \"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\""),
        ("Cursor", "cursor --wait --diff \"$LOCAL\" \"$REMOTE\"", "cursor --wait --merge \"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\""),
        ("WinMerge", "\"%ProgramFiles%\\WinMerge\\WinMergeU.exe\" -e -u \"$LOCAL\" \"$REMOTE\"", "\"%ProgramFiles%\\WinMerge\\WinMergeU.exe\" -e -u -o \"$MERGED\" \"$LOCAL\" \"$BASE\" \"$REMOTE\""),
        ("Beyond Compare", "\"%ProgramFiles%\\Beyond Compare 5\\BComp.exe\" \"$LOCAL\" \"$REMOTE\"", "\"%ProgramFiles%\\Beyond Compare 5\\BComp.exe\" \"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\""),
        ("KDiff3", "\"%ProgramFiles%\\KDiff3\\kdiff3.exe\" \"$LOCAL\" \"$REMOTE\"", "\"%ProgramFiles%\\KDiff3\\kdiff3.exe\" \"$BASE\" \"$LOCAL\" \"$REMOTE\" -o \"$MERGED\""),
        ("TortoiseGitMerge", "\"%ProgramFiles%\\TortoiseGit\\bin\\TortoiseGitMerge.exe\" /base:\"$LOCAL\" /mine:\"$REMOTE\"", "\"%ProgramFiles%\\TortoiseGit\\bin\\TortoiseGitMerge.exe\" /base:\"$BASE\" /mine:\"$LOCAL\" /theirs:\"$REMOTE\" /merged:\"$MERGED\""),
    ];
}

/// <summary>Talks to GitVeltoCache.exe (overlay status cache). No-op until the cache is installed.</summary>
public static class CacheClient
{
    /// <summary>Asks the cache to recompute a repository (after GitVelto changed it).</summary>
    public static void Invalidate(string root)
    {
        Task.Run(() =>
        {
            try
            {
                using var c = new System.IO.Pipes.NamedPipeClientStream(".", $"{KG.CachePipe}-{Environment.UserName}", System.IO.Pipes.PipeDirection.InOut);
                c.Connect(200);
                var msg = System.Text.Encoding.UTF8.GetBytes("invalidate\t" + root + "\n");
                c.Write(msg);
                c.Flush();
            }
            catch { /* cache not running */ }
        });
    }
}
