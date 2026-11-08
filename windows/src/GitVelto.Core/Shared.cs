using System.Text;

namespace GitVelto.Core;

/// <summary>Constants shared by the app, the status cache and the shell extension.</summary>
public static class KG
{
    public const string AppName = "GitVelto";
    /// <summary>Named pipe served by GitVeltoCache.exe (status for the overlays).</summary>
    public const string CachePipe = "GitVeltoCache";
    /// <summary>Named pipe served by a running GitVelto.exe (single instance: forwarded commands).</summary>
    public const string AppPipe = "GitVeltoApp";
    /// <summary>Registry key (HKCU) holding the settings that the shell extension reads.</summary>
    public const string RegistryKey = @"Software\GitVelto";
}

/// <summary>Overlay badge kinds shown in Explorer (TortoiseGit-like icon overlays).</summary>
public enum BadgeKind { Normal, Modified, Added, Conflict, Untracked, Ignored }

public static class BadgeKindExt
{
    public static string Id(this BadgeKind k) => k switch
    {
        BadgeKind.Normal => "normal",
        BadgeKind.Modified => "modified",
        BadgeKind.Added => "added",
        BadgeKind.Conflict => "conflict",
        BadgeKind.Untracked => "untracked",
        _ => "ignored",
    };

    public static BadgeKind? Parse(string? s) => s switch
    {
        "normal" => BadgeKind.Normal,
        "modified" => BadgeKind.Modified,
        "added" => BadgeKind.Added,
        "conflict" => BadgeKind.Conflict,
        "untracked" => BadgeKind.Untracked,
        "ignored" => BadgeKind.Ignored,
        _ => null,
    };

    public static string Label(this BadgeKind k) => k switch
    {
        BadgeKind.Normal => "変更なし",
        BadgeKind.Modified => "変更あり",
        BadgeKind.Added => "追加",
        BadgeKind.Conflict => "競合",
        BadgeKind.Untracked => "未追跡",
        _ => "無視",
    };

    /// <summary>Higher wins when aggregating folder state.</summary>
    public static int Severity(this BadgeKind k) => k switch
    {
        BadgeKind.Ignored => 0,
        BadgeKind.Untracked => 1,
        BadgeKind.Normal => 2,
        BadgeKind.Added => 3,
        BadgeKind.Modified => 4,
        _ => 5,
    };
}

/// <summary>Commands the shell extension (or the command line) can ask the app to perform.</summary>
public enum GitAction
{
    Start,
    Sync, Commit, Log, Diff, DiffPrevious, Status, RepoBrowser, Reflog, RefBrowser,
    Pull, Fetch, Push,
    SwitchBranch, Branch, Tag, Merge, Rebase,
    StashSave, StashPop, StashList,
    BisectStart, BisectGood, BisectBad, BisectSkip, BisectReset,
    Add, Revert, Resolve, EditConflict, Delete, DeleteKeep, Rename, Ignore, Blame, Clean,
    SubmoduleAdd, SubmoduleUpdate, SubmoduleSync,
    CreatePatch, ApplyPatch, Export,
    Remotes, RepoSettings,
    Clone, InitRepo,
    Terminal, Settings, Glossary, CommandHistory, Help, About,
}

public static class GitActionExt
{
    private static readonly Dictionary<GitAction, (string id, string title, string glyph)> Info = new()
    {
        // glyph = Segoe Fluent Icons / Segoe MDL2 Assets code point
        [GitAction.Start] = ("start", "起動", "\uE7E8"),
        [GitAction.Sync] = ("sync", "Git 同期…", "\uE895"),
        [GitAction.Commit] = ("commit", "Git コミット…", "\uE73E"),
        [GitAction.Log] = ("log", "ログを表示", "\uE81C"),
        [GitAction.Diff] = ("diff", "差分", "\uE8D4"),
        [GitAction.DiffPrevious] = ("diffPrevious", "前のバージョンと差分", "\uE8D4"),
        [GitAction.Status] = ("status", "変更をチェック…", "\uE721"),
        [GitAction.RepoBrowser] = ("repoBrowser", "リポジトリブラウザ", "\uE8B7"),
        [GitAction.Reflog] = ("reflog", "Reflog を表示", "\uE81C"),
        [GitAction.RefBrowser] = ("refBrowser", "参照の一覧…", "\uE8FD"),
        [GitAction.Pull] = ("pull", "プル…", "\uE896"),
        [GitAction.Fetch] = ("fetch", "フェッチ…", "\uE896"),
        [GitAction.Push] = ("push", "プッシュ…", "\uE898"),
        [GitAction.SwitchBranch] = ("switch", "切り替え/チェックアウト…", "\uE8AB"),
        [GitAction.Branch] = ("branch", "ブランチを作成…", "\uE710"),
        [GitAction.Tag] = ("tag", "タグを作成…", "\uE8EC"),
        [GitAction.Merge] = ("merge", "マージ…", "\uEA3C"),
        [GitAction.Rebase] = ("rebase", "リベース…", "\uE8CB"),
        [GitAction.StashSave] = ("stashSave", "変更を退避 (stash)…", "\uE7B8"),
        [GitAction.StashPop] = ("stashPop", "退避した変更を復元 (stash pop)", "\uE7B8"),
        [GitAction.StashList] = ("stashList", "退避リスト…", "\uE7B8"),
        [GitAction.BisectStart] = ("bisectStart", "二分探索を開始 (bisect)…", "\uE721"),
        [GitAction.BisectGood] = ("bisectGood", "二分探索: 良い (good)", "\uE8E1"),
        [GitAction.BisectBad] = ("bisectBad", "二分探索: 悪い (bad)", "\uE8E0"),
        [GitAction.BisectSkip] = ("bisectSkip", "二分探索: スキップ", "\uE893"),
        [GitAction.BisectReset] = ("bisectReset", "二分探索を終了 (reset)", "\uE711"),
        [GitAction.Add] = ("add", "追加…", "\uE710"),
        [GitAction.Revert] = ("revert", "元に戻す…", "\uE7A7"),
        [GitAction.Resolve] = ("resolve", "競合を解決…", "\uE73E"),
        [GitAction.EditConflict] = ("editConflict", "競合を編集…", "\uE7BA"),
        [GitAction.Delete] = ("delete", "削除 (git rm)…", "\uE74D"),
        [GitAction.DeleteKeep] = ("deleteKeep", "削除 (ローカルに残す)…", "\uE74D"),
        [GitAction.Rename] = ("rename", "名前の変更 (git mv)…", "\uE8AC"),
        [GitAction.Ignore] = ("ignore", "無視リストに追加…", "\uED1A"),
        [GitAction.Blame] = ("blame", "Blame", "\uE77B"),
        [GitAction.Clean] = ("clean", "クリーンアップ…", "\uE894"),
        [GitAction.SubmoduleAdd] = ("submoduleAdd", "サブモジュールを追加…", "\uE7B8"),
        [GitAction.SubmoduleUpdate] = ("submoduleUpdate", "サブモジュールを更新…", "\uE7B8"),
        [GitAction.SubmoduleSync] = ("submoduleSync", "サブモジュールを同期", "\uE72C"),
        [GitAction.CreatePatch] = ("createPatch", "パッチを作成…", "\uE8A5"),
        [GitAction.ApplyPatch] = ("applyPatch", "パッチを適用…", "\uE8A5"),
        [GitAction.Export] = ("export", "エクスポート…", "\uE7B8"),
        [GitAction.Remotes] = ("remotes", "リモートの管理…", "\uE774"),
        [GitAction.RepoSettings] = ("repoSettings", "リポジトリの設定…", "\uE9E9"),
        [GitAction.Clone] = ("clone", "Git クローン…", "\uE8C8"),
        [GitAction.InitRepo] = ("init", "Git ここにリポジトリを作成…", "\uE8F4"),
        [GitAction.Terminal] = ("terminal", "ターミナルで開く", "\uE756"),
        [GitAction.Settings] = ("settings", "設定…", "\uE713"),
        [GitAction.Glossary] = ("glossary", "Git 用語集", "\uE82D"),
        [GitAction.CommandHistory] = ("commandHistory", "実行したコマンドの履歴", "\uE81C"),
        [GitAction.Help] = ("help", "ヘルプ", "\uE897"),
        [GitAction.About] = ("about", "GitVelto について", "\uE946"),
    };

    private static readonly Dictionary<string, GitAction> ById =
        Info.ToDictionary(kv => kv.Value.id, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static string Id(this GitAction a) => Info[a].id;
    public static string Title(this GitAction a) => Info[a].title;
    public static string Glyph(this GitAction a) => Info[a].glyph;

    public static GitAction? Parse(string id) => ById.TryGetValue(id, out var a) ? a : null;

    /// <summary>Actions that can be shown on the top level of the Explorer context menu (configurable).</summary>
    public static readonly GitAction[] MenuConfigurable =
    [
        GitAction.Sync, GitAction.Commit, GitAction.Log, GitAction.Diff, GitAction.DiffPrevious, GitAction.Status,
        GitAction.RepoBrowser, GitAction.Reflog, GitAction.RefBrowser,
        GitAction.Pull, GitAction.Fetch, GitAction.Push, GitAction.SwitchBranch, GitAction.Branch, GitAction.Tag,
        GitAction.Merge, GitAction.Rebase,
        GitAction.StashSave, GitAction.StashPop, GitAction.StashList, GitAction.BisectStart,
        GitAction.Add, GitAction.Revert, GitAction.Resolve, GitAction.EditConflict, GitAction.Delete, GitAction.DeleteKeep,
        GitAction.Rename, GitAction.Ignore, GitAction.Blame, GitAction.Clean,
        GitAction.SubmoduleAdd, GitAction.SubmoduleUpdate, GitAction.SubmoduleSync, GitAction.CreatePatch,
        GitAction.ApplyPatch, GitAction.Export,
        GitAction.Remotes, GitAction.RepoSettings, GitAction.Terminal, GitAction.Glossary, GitAction.CommandHistory,
    ];
}

/// <summary>Context menu layout (stored in the registry so the shell extension can read it).</summary>
public sealed class MenuConfig
{
    /// <summary>Actions shown directly in the context menu (TortoiseGit default: Sync + Commit).</summary>
    public List<string> TopLevel { get; set; } = ["sync", "commit"];
    /// <summary>Actions hidden entirely.</summary>
    public List<string> Hidden { get; set; } = [];
    /// <summary>Show the current branch in the commit item title ("Git コミット -> main…").</summary>
    public bool ShowBranch { get; set; } = true;

    public bool IsTop(GitAction a) => TopLevel.Contains(a.Id());
    public bool IsHidden(GitAction a) => Hidden.Contains(a.Id());
}

/// <summary>Facts about the Explorer selection used to decide which menu items appear.</summary>
public sealed record MenuContext(
    bool InRepo,
    int TargetCount,
    bool AnyDir,
    IReadOnlyList<string?> States,   // badge ids of the targets (null = unknown)
    bool IsRoot,
    bool Bisecting = false,
    bool HasSubmodules = false,
    string? Branch = null);

public sealed record MenuEntry(GitAction Action, string Title, bool TopLevel, bool SeparatorBefore);

/// <summary>Decides the Explorer context menu (TortoiseGit layout). Pure logic; the C++ shell extension mirrors it.</summary>
public static class MenuPlanner
{
    public const string CompareTwoTitle = "選択した 2 つのファイルを比較";

    public static List<MenuEntry> Plan(MenuContext c, MenuConfig cfg)
    {
        bool unknown = c.States.Any(s => s == null);
        bool Any(params BadgeKind[] kinds) => c.AnyDir || unknown || c.States.Any(s => kinds.Any(k => k.Id() == s));
        bool single = c.TargetCount == 1;
        bool twoFiles = c.TargetCount == 2 && !c.AnyDir;
        bool singleFile = single && !c.AnyDir;
        string? firstState = c.States.Count > 0 ? c.States[0] : null;
        bool tracked = singleFile && firstState != null && firstState != "untracked" && firstState != "ignored";
        bool isConflict = singleFile && firstState == "conflict";

        if (!c.InRepo)
        {
            var o = new List<MenuEntry>();
            if (twoFiles) o.Add(new(GitAction.Diff, CompareTwoTitle, true, false));
            o.Add(new(GitAction.Clone, GitAction.Clone.Title(), true, false));
            o.Add(new(GitAction.InitRepo, GitAction.InitRepo.Title(), true, false));
            foreach (var a in new[] { GitAction.Terminal, GitAction.Settings, GitAction.Glossary, GitAction.Help, GitAction.About })
                o.Add(new(a, a.Title(), false, false));
            return o;
        }

        var plan = new List<GitAction?>
        {
            GitAction.Sync, GitAction.Commit, null,
            GitAction.Log, GitAction.Diff, GitAction.DiffPrevious, GitAction.Status, GitAction.RepoBrowser, GitAction.RefBrowser, GitAction.Reflog, null,
            GitAction.Pull, GitAction.Fetch, GitAction.Push, null,
            GitAction.SwitchBranch, GitAction.Merge, GitAction.Rebase, GitAction.Branch, GitAction.Tag, null,
            GitAction.StashSave, GitAction.StashPop, GitAction.StashList, null,
        };
        if (c.Bisecting) plan.AddRange([GitAction.BisectGood, GitAction.BisectBad, GitAction.BisectSkip, GitAction.BisectReset, null]);
        else plan.AddRange([GitAction.BisectStart, null]);
        plan.AddRange([GitAction.EditConflict, GitAction.Resolve, GitAction.Add, GitAction.Revert, GitAction.Delete, GitAction.DeleteKeep,
            GitAction.Rename, GitAction.Ignore, GitAction.Blame, GitAction.Clean, null]);
        plan.Add(GitAction.SubmoduleAdd);
        if (c.HasSubmodules) plan.AddRange([GitAction.SubmoduleUpdate, GitAction.SubmoduleSync]);
        plan.Add(null);
        plan.AddRange([GitAction.CreatePatch, GitAction.ApplyPatch, GitAction.Export, null, GitAction.Remotes, GitAction.RepoSettings,
            GitAction.Terminal, null, GitAction.Settings, GitAction.Glossary, GitAction.CommandHistory, GitAction.Help, GitAction.About]);

        bool Visible(GitAction a)
        {
            if (cfg.IsHidden(a)) return false;
            return a switch
            {
                GitAction.Diff => twoFiles || Any(BadgeKind.Modified, BadgeKind.Added, BadgeKind.Conflict),
                GitAction.DiffPrevious or GitAction.Blame => tracked,
                GitAction.EditConflict => isConflict,
                GitAction.Resolve => Any(BadgeKind.Conflict),
                GitAction.Add => Any(BadgeKind.Untracked, BadgeKind.Ignored),
                GitAction.Revert => Any(BadgeKind.Modified, BadgeKind.Added, BadgeKind.Conflict),
                GitAction.Delete or GitAction.DeleteKeep or GitAction.Ignore => !c.IsRoot,
                GitAction.Rename => single && !c.IsRoot,
                _ => true,
            };
        }

        var outList = new List<MenuEntry>();
        bool pendingSep = false;
        int subCount = 0;
        foreach (var entry in plan)
        {
            if (entry is not GitAction a) { pendingSep = true; continue; }
            if (!Visible(a)) continue;
            string title = a.Title();
            if (a == GitAction.Commit && cfg.ShowBranch && c.Branch != null) title = $"Git コミット -> \"{c.Branch}\"…";
            if (a == GitAction.Diff && twoFiles) title = CompareTwoTitle;
            if (cfg.IsTop(a))
            {
                outList.Add(new(a, title, true, false));
            }
            else
            {
                outList.Add(new(a, title, false, pendingSep && subCount > 0));
                pendingSep = false;
                subCount++;
            }
        }
        return outList;
    }
}

public static class PathUtil
{
    /// <summary>Full path without trailing separator (except drive roots such as "C:\").</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string p;
        try { p = Path.GetFullPath(path.Replace('/', '\\')); } catch { p = path; }
        while (p.Length > 3 && (p.EndsWith('\\') || p.EndsWith('/'))) p = p[..^1];
        return p;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> equals <paramref name="dir"/> or lies inside it.</summary>
    public static bool IsUnder(string path, string dir)
    {
        var p = Normalize(path); var d = Normalize(dir);
        if (string.Equals(p, d, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = d.EndsWith('\\') ? d : d + "\\";
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Walk up from <paramref name="path"/> looking for a ".git" entry (directory, or file for worktrees/submodules).</summary>
    public static string? FindRepoRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var current = Normalize(path);
        while (true)
        {
            if (string.Equals(Path.GetFileName(current), ".git", StringComparison.OrdinalIgnoreCase)) return null;
            var dotGit = Path.Combine(current, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit)) return current;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) return null;
            current = parent;
        }
    }

    public static bool IsRepoRoot(string path)
    {
        var dotGit = Path.Combine(Normalize(path), ".git");
        return Directory.Exists(dotGit) || File.Exists(dotGit);
    }

    /// <summary>The .git directory of a work tree (follows "gitdir:" files of worktrees / submodules).</summary>
    public static string? GitDir(string repoRoot)
    {
        var dotGit = Path.Combine(repoRoot, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (!File.Exists(dotGit)) return null;
        try
        {
            var line = File.ReadLines(dotGit).FirstOrDefault() ?? "";
            if (!line.StartsWith("gitdir: ")) return null;
            var p = line[8..].Trim();
            return Path.IsPathRooted(p) ? Normalize(p) : Normalize(Path.Combine(repoRoot, p));
        }
        catch { return null; }
    }

    /// <summary>Current branch name read directly from HEAD (null if detached / unknown).</summary>
    public static string? BranchName(string repoRoot)
    {
        var gitDir = GitDir(repoRoot);
        if (gitDir == null) return null;
        try
        {
            var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            return head.StartsWith("ref: refs/heads/") ? head[16..] : null;
        }
        catch { return null; }
    }

    /// <summary>Unicode NFC form (git reports NFC; some tools hand out NFD).</summary>
    public static string Nfc(this string s) => s.IsNormalized(NormalizationForm.FormC) ? s : s.Normalize(NormalizationForm.FormC);
}
