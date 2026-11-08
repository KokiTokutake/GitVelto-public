using System.Runtime.InteropServices;
using System.Text;
using GitVelto.Core;

namespace GitVelto.Cache;

/// <summary>Badge server: answers the shell extension's folder queries (port of the macOS StatusService).</summary>
public sealed class StatusService
{
    private readonly object gate = new();
    private readonly bool logEnabled;
    private readonly string logFile = Path.Combine(Path.GetTempPath(), "GitVelto", "cache.log");

    private readonly Dictionary<string, RepoSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> computing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Timer> debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> lastSent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime At, DirInfo Info)> dirInfo = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim workers = new(4);
    private readonly Timer housekeeping;
    private DateTime lastQuery = DateTime.UtcNow;

    private static readonly string WindowsDir = PathUtil.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    public StatusService(bool log)
    {
        logEnabled = log;
        housekeeping = new Timer(_ => Housekeeping(), null, 60_000, 60_000);
        Log("started");
    }

    public void Log(string s)
    {
        if (!logEnabled) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            File.AppendAllText(logFile, $"{DateTime.Now:HH:mm:ss.fff} {s}\n");
        }
        catch { }
    }

    public string Handle(string line)
    {
        var parts = line.Split('\t');
        switch (parts[0])
        {
            case "ping": return "pong\n";
            case "settings":
                AppSettings.Shared.Reload();
                lock (gate) lastSent.Clear();
                RefreshAllObserved();
                return "ok\n";
            case "invalidate" when parts.Length > 1:
                var root = PathUtil.FindRepoRoot(parts[1]);
                if (root != null) Compute(root);
                return "ok\n";
            case "dir" when parts.Length > 1:
                return QueryDir(parts[1]);
            default:
                return "error\n";
        }
    }

    // ---- folder query ----

    private sealed record Child(string Name, bool IsDir, bool IsRepo);
    private sealed record DirInfo(string? Container, List<Child> Children);

    private bool Excluded(string dir)
    {
        if (!AppSettings.Shared.BadgesEnabled) return true;
        if (dir.StartsWith(@"\\")) return true; // network shares: too slow for overlays
        if (dir.Contains(@"\.git\", StringComparison.OrdinalIgnoreCase) || dir.EndsWith(@"\.git", StringComparison.OrdinalIgnoreCase)) return true;
        if (PathUtil.IsUnder(dir, WindowsDir)) return true;
        return AppSettings.Shared.IsBadgeExcluded(dir);
    }

    private DirInfo Dependencies(string dir)
    {
        lock (gate)
        {
            if (dirInfo.TryGetValue(dir, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(3)) return c.Info;
        }
        var container = PathUtil.FindRepoRoot(dir);
        var children = new List<Child>();
        try
        {
            foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                var name = e.Name;
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                bool isDir = (e.Attributes & FileAttributes.Directory) != 0;
                bool isRepo = isDir && (Directory.Exists(Path.Combine(e.FullName, ".git")) || File.Exists(Path.Combine(e.FullName, ".git")));
                children.Add(new Child(name, isDir, isRepo));
            }
        }
        catch { }
        var info = new DirInfo(container, children);
        lock (gate) dirInfo[dir] = (DateTime.UtcNow, info);
        return info;
    }

    /// <summary>Badges of the direct children of <paramref name="dir"/>; null entries = snapshot missing.</summary>
    private (Dictionary<string, string> Items, string? Repo, List<string> Missing) Badges(string dir, DirInfo info)
    {
        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        RepoSnapshot? containerSnap = null;
        lock (gate)
        {
            if (info.Container != null)
            {
                if (!snapshots.TryGetValue(info.Container, out containerSnap)) missing.Add(info.Container);
            }
            foreach (var ch in info.Children)
            {
                var full = Path.Combine(dir, ch.Name);
                if (ch.IsRepo && !PathUtil.SamePath(full, info.Container ?? ""))
                {
                    if (snapshots.TryGetValue(full, out var s)) items[ch.Name] = s.Valid ? s.Overall.Id() : "";
                    else missing.Add(full);
                    continue;
                }
                if (info.Container != null && containerSnap != null)
                {
                    var rel = full[(info.Container.TrimEnd('\\').Length + 1)..].Replace('\\', '/');
                    items[ch.Name] = containerSnap.Badge(rel, ch.IsDir)?.Id() ?? "";
                }
            }
        }
        return (items, info.Container, missing);
    }

    private string QueryDir(string rawDir)
    {
        lastQuery = DateTime.UtcNow;
        var dir = PathUtil.Normalize(rawDir);
        if (Excluded(dir) || !Directory.Exists(dir)) return "repo\t\tready\n";
        lock (gate) observed[dir] = DateTime.UtcNow;

        var info = Dependencies(dir);
        var (items, repo, missing) = Badges(dir, info);
        if (missing.Count > 0)
        {
            var tasks = missing.Select(Compute).ToArray();
            // Answer quickly: small repositories finish within the wait, large ones are refreshed later.
            Task.WaitAll(tasks, 300);
            (items, repo, missing) = Badges(dir, info);
        }
        bool pending = missing.Count > 0;
        lock (gate)
        {
            if (!pending) lastSent[dir] = new Dictionary<string, string>(items, StringComparer.OrdinalIgnoreCase);
            else lastSent.Remove(dir);
        }
        var sb = new StringBuilder();
        sb.Append("repo\t").Append(repo ?? "").Append('\t').Append(pending ? "pending" : "ready").Append('\n');
        foreach (var (name, badge) in items)
            if (badge.Length > 0) sb.Append(name).Append('\t').Append(badge).Append('\n');
        return sb.ToString();
    }

    // ---- computing ----

    private Task Compute(string root)
    {
        lock (gate)
        {
            if (computing.TryGetValue(root, out var running))
            {
                dirty.Add(root);
                return running;
            }
            var t = Task.Run(async () =>
            {
                await workers.WaitAsync();
                RepoSnapshot snap;
                try { snap = RepoSnapshot.Compute(root); }
                finally { workers.Release(); }
                if (!snap.Valid) Log("status failed " + root);
                bool again;
                lock (gate)
                {
                    snapshots[root] = snap;
                    computing.Remove(root);
                    again = dirty.Remove(root);
                }
                EnsureWatcher(root);
                NotifyDependents(root);
                if (again) _ = Compute(root);
            });
            computing[root] = t;
            return t;
        }
    }

    private List<string> Dependents(string root)
    {
        var parent = Path.GetDirectoryName(root) ?? root;
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(30);
        lock (gate)
        {
            return observed.Where(kv => kv.Value > cutoff)
                .Select(kv => kv.Key)
                .Where(d => PathUtil.SamePath(d, parent) || PathUtil.IsUnder(d, root))
                .ToList();
        }
    }

    /// <summary>Tells Explorer to redraw the overlays of items whose badge changed.</summary>
    private void NotifyDependents(string root)
    {
        foreach (var dir in Dependents(root))
        {
            if (Excluded(dir)) continue;
            var info = Dependencies(dir);
            var (items, _, missing) = Badges(dir, info);
            if (missing.Count > 0) continue;
            Dictionary<string, string>? prev;
            lock (gate)
            {
                lastSent.TryGetValue(dir, out prev);
                lastSent[dir] = new Dictionary<string, string>(items, StringComparer.OrdinalIgnoreCase);
            }
            var changed = items.Where(kv => prev == null ? kv.Value.Length > 0 : !prev.TryGetValue(kv.Key, out var old) || old != kv.Value)
                .Select(kv => kv.Key).ToList();
            if (prev != null) changed.AddRange(prev.Keys.Where(k => !items.ContainsKey(k)));
            if (changed.Count == 0) continue;
            Log($"notify {dir} ({changed.Count})");
            foreach (var name in changed.Take(3000)) Shell.UpdateItem(Path.Combine(dir, name));
        }
    }

    private void RefreshAllObserved()
    {
        List<string> dirs;
        lock (gate) dirs = observed.Keys.ToList();
        foreach (var d in dirs) Shell.UpdateDir(d);
    }

    // ---- watching ----

    private static bool Relevant(string path)
    {
        var p = path.Replace('/', '\\');
        if (p.Contains(@"\.git\objects", StringComparison.OrdinalIgnoreCase)) return false;
        if (p.Contains(@"\.git\logs", StringComparison.OrdinalIgnoreCase)) return false;
        if (p.Contains(@"\.git\fsmonitor", StringComparison.OrdinalIgnoreCase)) return false;
        if (p.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void EnsureWatcher(string root)
    {
        lock (gate)
        {
            if (watchers.ContainsKey(root)) return;
            try
            {
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                FileSystemEventHandler h = (_, e) => { if (Relevant(e.FullPath)) ScheduleRefresh(root, 600); };
                w.Changed += h; w.Created += h; w.Deleted += h;
                w.Renamed += (_, e) => { if (Relevant(e.FullPath) || Relevant(e.OldFullPath)) ScheduleRefresh(root, 600); };
                w.Error += (_, _) => ScheduleRefresh(root, 1000);
                w.EnableRaisingEvents = true;
                watchers[root] = w;
            }
            catch (Exception e) { Log($"watch failed {root}: {e.Message}"); }
        }
    }

    private void ScheduleRefresh(string root, int delayMs)
    {
        lock (gate)
        {
            if (debounce.TryGetValue(root, out var t)) t.Dispose();
            debounce[root] = new Timer(_ =>
            {
                lock (gate)
                {
                    if (debounce.TryGetValue(root, out var self)) { self.Dispose(); debounce.Remove(root); }
                    dirInfo.Clear();
                }
                if (Dependents(root).Count == 0)
                {
                    lock (gate) snapshots.Remove(root);
                }
                else _ = Compute(root);
            }, null, delayMs, Timeout.Infinite);
        }
    }

    private void Housekeeping()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(30);
        lock (gate)
        {
            foreach (var d in observed.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
            {
                observed.Remove(d);
                lastSent.Remove(d);
            }
            foreach (var k in dirInfo.Where(kv => DateTime.UtcNow - kv.Value.At > TimeSpan.FromMinutes(1)).Select(kv => kv.Key).ToList())
                dirInfo.Remove(k);
        }
        foreach (var root in watchers.Keys.ToList())
        {
            if (Dependents(root).Count > 0) continue;
            lock (gate)
            {
                if (watchers.Remove(root, out var w)) w.Dispose();
                snapshots.Remove(root);
            }
        }
        // Nobody looked at a folder for a long time: quit (the shell extension starts us again on demand).
        if (DateTime.UtcNow - lastQuery > TimeSpan.FromHours(2)) Environment.Exit(0);
    }
}

internal static class Shell
{
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_FLUSHNOWAIT = 0x3000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);

    public static void UpdateItem(string path) => SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path, IntPtr.Zero);
    public static void UpdateDir(string path) => SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, path, IntPtr.Zero);
}
