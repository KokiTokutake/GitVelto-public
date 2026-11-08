using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GitVelto.Core;

namespace GitVelto.UI;

/// <summary>Lets a view close or retitle the window hosting it.</summary>
public sealed class WindowHandle
{
    public Window? Window { get; internal set; }
    public void Close() => Window?.Close();
    public void SetTitle(string t) { if (Window != null) Window.Title = t; }
}

/// <summary>
/// Base class of every dialog body. WindowManager.Show hosts it in a window and sets <see cref="Handle"/>.
/// Override <see cref="OnShown"/> to start loading data (called once, after the window is visible).
/// </summary>
public class GVView : UserControl
{
    public WindowHandle Handle { get; internal set; } = new();
    public Window? OwnerWindow => Handle.Window;
    protected internal virtual void OnShown() { }
    /// <summary>Return false to keep the window open (e.g. while a command runs).</summary>
    protected internal virtual bool CanClose() => true;

    /// <summary>Runs <paramref name="action"/> whenever GitVelto changes the repository (from any window) while this view is loaded.</summary>
    protected void OnRepoChanged(string root, Action action)
    {
        void Handler(string changed)
        {
            if (PathUtil.SamePath(changed, root)) Dispatcher.BeginInvoke(action);
        }
        Loaded += (_, _) => { WindowManager.RepoChanged -= Handler; WindowManager.RepoChanged += Handler; };
        Unloaded += (_, _) => WindowManager.RepoChanged -= Handler;
    }
}

public sealed class WindowManager
{
    public static WindowManager Shared { get; } = new();

    /// <summary>Raised (on any thread) with the repository root after GitVelto changed a repository.</summary>
    public static event Action<string>? RepoChanged;

    private readonly Dictionary<string, Window> keyed = [];

    /// <summary>Notifies open windows (and the overlay cache) that a repository changed.</summary>
    public static void NotifyRepoChanged(string path)
    {
        var root = PathUtil.FindRepoRoot(path) ?? path;
        RepoChanged?.Invoke(root);
        CacheClient.Invalidate(root);
    }

    // ---- windows ----

    public Window Show(string title, GVView view, double width, double height, string? key = null, double? minWidth = null, double? minHeight = null)
    {
        if (key != null && keyed.TryGetValue(key, out var existing))
        {
            Activate(existing);
            return existing;
        }
        // Learning mode adds a command preview to dialogs: give small dialogs more room.
        if (AppSettings.Shared.LearningMode && height <= 520) height += 150;
        if (AppSettings.Shared.LearningMode && title.Contains("コミット") && height < 820) height = 820;

        var w = new Window
        {
            Title = title,
            Content = view,
            Width = width,
            Height = height,
            MinWidth = minWidth ?? Math.Min(width, 420),
            MinHeight = minHeight ?? Math.Min(height, 220),
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = true,
        };
        try { w.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico")); } catch { }
        view.Handle.Window = w;
        Place(w);
        if (key != null) keyed[key] = w;
        w.Closing += (_, e) => { if (!view.CanClose()) e.Cancel = true; };
        w.Closed += (_, _) =>
        {
            foreach (var k in keyed.Where(kv => kv.Value == w).Select(kv => kv.Key).ToList()) keyed.Remove(k);
            App.Instance.ExitIfIdle();
        };
        w.ContentRendered += (_, _) => view.OnShown();
        w.Show();
        Activate(w);
        return w;
    }

    private static void Place(Window w)
    {
        var front = Application.Current.Windows.OfType<Window>().Where(x => x != w && x.IsVisible).LastOrDefault();
        var area = SystemParameters.WorkArea;
        if (front != null)
        {
            w.Left = Math.Min(front.Left + 28, area.Right - w.Width);
            w.Top = Math.Min(front.Top + 28, area.Bottom - w.Height);
        }
        else
        {
            w.Left = area.Left + (area.Width - w.Width) / 2;
            w.Top = area.Top + (area.Height - w.Height) / 2;
        }
        if (w.Left < area.Left) w.Left = area.Left;
        if (w.Top < area.Top) w.Top = area.Top;
    }

    public static void Activate(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        // Bring to front even when launched from Explorer (foreground lock).
        w.Topmost = true;
        w.Topmost = false;
        w.Focus();
    }

    private static Window? ActiveWindow =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
        ?? Application.Current?.Windows.OfType<Window>().LastOrDefault(x => x.IsVisible);

    // ---- alerts ----

    public static void Alert(string message, string info = "", bool error = true) =>
        MessageDialog.Show(ActiveWindow, message, info, error ? MessageKind.Warning : MessageKind.Info, "OK", null);

    public static bool Confirm(string message, string info = "", string ok = "OK", bool destructive = false) =>
        MessageDialog.Show(ActiveWindow, message, info, destructive ? MessageKind.Danger : MessageKind.Question, ok, "キャンセル");

    /// <summary>Simple text input dialog. Returns null when cancelled.</summary>
    public static string? Prompt(string message, string info = "", string defaultValue = "") =>
        MessageDialog.Prompt(ActiveWindow, message, info, defaultValue);

    // ---- progress ----

    /// <summary>Runs commands in a TortoiseGit-style progress dialog.</summary>
    public void RunProgress(string title, string dir, IReadOnlyList<ProgressStep> steps,
        IReadOnlyList<ProgressAction>? successActions = null, Action<bool>? onFinish = null)
    {
        var runner = new CommandRunner(dir, steps);
        runner.Finished += ok =>
        {
            NotifyRepoChanged(dir);
            onFinish?.Invoke(ok);
        };
        Show($"GitVelto - {title}", new ProgressView(title, runner, successActions ?? []), 680, 420, minWidth: 420, minHeight: 240);
        runner.Start();
    }

    // ---- simple windows ----

    public void ShowGlossary(string? select = null)
    {
        if (keyed.TryGetValue("glossary", out var w)) w.Close();
        Show("Git 用語集", new GlossaryView(select), 860, 580, "glossary");
    }

    public void ShowCommandHistory() => Show("実行したコマンドの履歴", new CommandHistoryView(), 780, 540, "history");

    public void ShowSettings() => Show("GitVelto 設定", new SettingsView(), 720, 620, "settings", 560, 420);

    // ---- dispatch ----

    private static string BaseDir(string? path)
    {
        if (path == null) return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(path)) return path;
        return Path.GetDirectoryName(path) ?? path;
    }

    private static bool IsDir(string p) => Directory.Exists(p);

    public void Perform(GitAction action, IReadOnlyList<string> rawPaths)
    {
        var paths = rawPaths.Select(PathUtil.Normalize).ToList();
        switch (action)
        {
            case GitAction.Start: return;
            case GitAction.Settings: ShowSettings(); return;
            case GitAction.About: Show("GitVelto について", new AboutView(), 440, 340, "about"); return;
            case GitAction.Help: Show("GitVelto ヘルプ", new HelpView(), 680, 640, "help"); return;
            case GitAction.Glossary: ShowGlossary(); return;
            case GitAction.CommandHistory: ShowCommandHistory(); return;
            case GitAction.Clone:
                Show("GitVelto - クローン", new CloneView(BaseDir(paths.FirstOrDefault())), 600, 380);
                return;
            case GitAction.InitRepo:
                Show("GitVelto - リポジトリを作成", new InitRepoView(BaseDir(paths.FirstOrDefault())), 520, 280);
                return;
            case GitAction.Terminal: Shell.OpenTerminal(BaseDir(paths.FirstOrDefault())); return;
            case GitAction.Diff when paths.Count == 2 && !IsDir(paths[0]) && !IsDir(paths[1]):
                var r0 = Repo.At(paths[0]) ?? new Repo(Path.GetDirectoryName(paths[0])!);
                ShowDiff(r0, new DiffSource.Files(paths[0], paths[1]));
                return;
        }

        if (paths.FirstOrDefault() is not { } first || Repo.At(first) is not { } repo)
        {
            Alert("Git リポジトリではありません", paths.FirstOrDefault() ?? "");
            return;
        }
        var name = repo.Name;
        var single = paths.Count == 1 ? paths[0] : null;

        switch (action)
        {
            case GitAction.Sync:
                Show($"GitVelto - 同期 - {name}", new SyncView(repo), 860, 620, minWidth: 640, minHeight: 440); break;
            case GitAction.Commit:
                Show($"GitVelto - コミット - {name}", new CommitView(repo, paths), 820, 760, minWidth: 600, minHeight: 520); break;
            case GitAction.Log:
                Show($"GitVelto - ログ - {name}", new LogView(repo, paths.Where(p => !PathUtil.SamePath(p, repo.Root)).ToList()), 1120, 780, minWidth: 700, minHeight: 500); break;
            case GitAction.Diff:
                if (single != null && !IsDir(single))
                {
                    var e = repo.Status([single]).FirstOrDefault();
                    if (e == null) { Alert("変更はありません", repo.Relative(single), false); return; }
                    ShowDiff(repo, new DiffSource.WorkingTree(e.Path, e.OrigPath, e.State));
                }
                else Show($"GitVelto - 変更をチェック - {name}", new StatusView(repo, paths), 860, 580);
                break;
            case GitAction.Status:
                Show($"GitVelto - 変更をチェック - {name}", new StatusView(repo, paths), 860, 580); break;
            case GitAction.DiffPrevious:
                {
                    if (single == null) return;
                    var rel = repo.Relative(single);
                    var last = repo.Git(["log", "-1", "--format=%H", "--", rel]).Out.Trim();
                    if (last.Length == 0) { Alert("履歴がありません", rel); return; }
                    bool hasParent = repo.Git(["rev-parse", "--verify", "-q", last + "^"]).Ok;
                    ShowDiff(repo, hasParent ? new DiffSource.RevToWorking(last + "^", rel) : new DiffSource.CommitDiff(last, null, rel, null));
                    break;
                }
            case GitAction.RepoBrowser:
                {
                    var rel = single == null ? "" : IsDir(single) ? repo.Relative(single) : ParentRel(repo.Relative(single));
                    Show($"GitVelto - リポジトリブラウザ - {name}", new RepoBrowserView(repo, rel), 800, 580);
                    break;
                }
            case GitAction.Reflog: Show($"GitVelto - Reflog - {name}", new ReflogView(repo), 820, 540); break;
            case GitAction.RefBrowser: Show($"GitVelto - 参照の一覧 - {name}", new RefBrowserView(repo), 1000, 580); break;
            case GitAction.Rebase: Show($"GitVelto - リベース - {name}", new RebaseView(repo, null), 820, 580); break;
            case GitAction.BisectStart: Show($"GitVelto - 二分探索 - {name}", new BisectStartView(repo), 560, 320); break;
            case GitAction.BisectGood or GitAction.BisectBad or GitAction.BisectSkip or GitAction.BisectReset:
                {
                    var verb = action switch { GitAction.BisectGood => "good", GitAction.BisectBad => "bad", GitAction.BisectSkip => "skip", _ => "reset" };
                    RunProgress(action.Title(), repo.Root, [ProgressStep.Git("bisect", verb)]);
                    break;
                }
            case GitAction.EditConflict:
                if (single == null) return;
                if (AppSettings.Shared.ExternalMerge.Length > 0) ExternalTools.Merge(repo, single);
                else Show($"GitVelto - 競合の編集 - {Path.GetFileName(single)}", new ConflictEditorView(repo, single), 1040, 780);
                break;
            case GitAction.DeleteKeep:
                Show($"GitVelto - 削除 (ローカルに残す) - {name}", new DeleteView(repo, paths, true), 600, 380); break;
            case GitAction.SubmoduleAdd:
                Show($"GitVelto - サブモジュールを追加 - {name}", new SubmoduleAddView(repo, BaseDir(paths.FirstOrDefault())), 580, 300); break;
            case GitAction.SubmoduleUpdate:
                Show($"GitVelto - サブモジュールを更新 - {name}", new SubmoduleUpdateView(repo), 680, 460); break;
            case GitAction.SubmoduleSync:
                RunProgress("サブモジュールを同期", repo.Root, [ProgressStep.Git("submodule", "sync", "--recursive")]); break;
            case GitAction.CreatePatch:
                Show($"GitVelto - パッチを作成 - {name}", new CreatePatchView(repo), 560, 400); break;
            case GitAction.ApplyPatch:
                {
                    var patches = paths.Where(p => Path.GetExtension(p).ToLowerInvariant() is ".patch" or ".diff").ToList();
                    Show($"GitVelto - パッチを適用 - {name}", new ApplyPatchView(repo, patches), 680, 480);
                    break;
                }
            case GitAction.Export: Show($"GitVelto - エクスポート - {name}", new ExportView(repo), 560, 320); break;
            case GitAction.Remotes: Show($"GitVelto - リモートの管理 - {name}", new RemotesView(repo), 680, 400); break;
            case GitAction.RepoSettings: Show($"GitVelto - リポジトリの設定 - {name}", new RepoSettingsView(repo), 720, 520); break;
            case GitAction.Pull: Show($"GitVelto - プル - {name}", new RemoteOpView(repo, RemoteOpMode.Pull), 560, 360); break;
            case GitAction.Fetch: Show($"GitVelto - フェッチ - {name}", new RemoteOpView(repo, RemoteOpMode.Fetch), 560, 330); break;
            case GitAction.Push: Show($"GitVelto - プッシュ - {name}", new RemoteOpView(repo, RemoteOpMode.Push), 580, 410); break;
            case GitAction.SwitchBranch: Show($"GitVelto - 切り替え/チェックアウト - {name}", new SwitchView(repo), 560, 390); break;
            case GitAction.Branch: Show($"GitVelto - ブランチを作成 - {name}", new CreateRefView(repo, CreateRefKind.Branch, null), 560, 370); break;
            case GitAction.Tag: Show($"GitVelto - タグを作成 - {name}", new CreateRefView(repo, CreateRefKind.Tag, null), 560, 430); break;
            case GitAction.Merge: Show($"GitVelto - マージ - {name}", new MergeView(repo), 560, 450); break;
            case GitAction.StashSave: Show($"GitVelto - 変更を退避 - {name}", new StashSaveView(repo), 520, 260); break;
            case GitAction.StashPop: RunProgress("退避した変更を復元", repo.Root, [ProgressStep.Git("stash", "pop")]); break;
            case GitAction.StashList: Show($"GitVelto - 退避リスト - {name}", new StashListView(repo), 800, 540); break;
            case GitAction.Add: Show($"GitVelto - 追加 - {name}", new FileActionView(repo, paths, FileActionMode.Add), 740, 500); break;
            case GitAction.Revert: Show($"GitVelto - 元に戻す - {name}", new FileActionView(repo, paths, FileActionMode.Revert), 740, 500); break;
            case GitAction.Resolve: Show($"GitVelto - 競合を解決 - {name}", new FileActionView(repo, paths, FileActionMode.Resolve), 740, 500); break;
            case GitAction.Delete: Show($"GitVelto - 削除 - {name}", new DeleteView(repo, paths, false), 600, 380); break;
            case GitAction.Rename:
                if (single == null) return;
                Show($"GitVelto - 名前の変更 - {name}", new RenameView(repo, single), 560, 200); break;
            case GitAction.Ignore: Show($"GitVelto - 無視リストに追加 - {name}", new IgnoreView(repo, paths), 600, 380); break;
            case GitAction.Blame:
                if (single == null) return;
                Show($"GitVelto - Blame - {Path.GetFileName(single)}", new BlameView(repo, single, null), 1040, 740); break;
            case GitAction.Clean: Show($"GitVelto - クリーンアップ - {name}", new CleanView(repo, paths), 740, 540); break;
        }
    }

    private static string ParentRel(string rel)
    {
        int i = rel.LastIndexOf('/');
        return i < 0 ? "" : rel[..i];
    }

    public void ShowDiff(Repo repo, DiffSource source) =>
        Show($"GitVelto - 差分 - {Path.GetFileName(source.Path.Replace('/', '\\'))}", new DiffView(repo, source), 1040, 740, minWidth: 500, minHeight: 300);
}
