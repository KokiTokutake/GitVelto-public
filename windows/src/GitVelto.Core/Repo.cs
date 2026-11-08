using System.Text;

namespace GitVelto.Core;

public sealed class Repo
{
    /// <summary>Work tree root (Windows path, no trailing separator).</summary>
    public string Root { get; }

    public Repo(string root) { Root = PathUtil.Normalize(root); }

    public string Name => Path.GetFileName(Root);

    public GitResult Git(IEnumerable<string> args, byte[]? stdin = null) => Core.Git.Run(args, Root, stdin);
    public Task<GitResult> GitAsync(IEnumerable<string> args, byte[]? stdin = null) => Core.Git.RunAsync(args, Root, stdin);

    public static Repo? At(string path) => PathUtil.FindRepoRoot(path) is { } r ? new Repo(r) : null;

    /// <summary>Path relative to the repository root, with "/" separators ("" for the root itself).</summary>
    public string Relative(string path)
    {
        var p = PathUtil.Normalize(path);
        if (string.Equals(p, Root, StringComparison.OrdinalIgnoreCase)) return "";
        var prefix = Root.EndsWith('\\') ? Root : Root + "\\";
        if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return p[prefix.Length..].Replace('\\', '/');
        var pn = p.Nfc(); var rn = prefix.Nfc();
        if (pn.StartsWith(rn, StringComparison.OrdinalIgnoreCase)) return pn[rn.Length..].Replace('\\', '/');
        return path.Replace('\\', '/');
    }

    /// <summary>Absolute Windows path of a repository-relative path.</summary>
    public string Absolute(string rel) =>
        rel.Length == 0 ? Root : Path.Combine(Root, rel.TrimEnd('/').Replace('/', '\\'));

    private string? gitDir;
    public string GitDir
    {
        get
        {
            if (gitDir != null) return gitDir;
            var s = Git(["rev-parse", "--absolute-git-dir"]).Out.Trim();
            gitDir = s.Length == 0 ? Path.Combine(Root, ".git") : PathUtil.Normalize(s);
            return gitDir;
        }
    }

    public string? CurrentBranch()
    {
        var s = Git(["symbolic-ref", "--short", "-q", "HEAD"]).Out.Trim();
        return s.Length == 0 ? null : s;
    }

    public string HeadDescription()
    {
        if (CurrentBranch() is { } b) return b;
        var s = Git(["rev-parse", "--short", "HEAD"]).Out.Trim();
        return s.Length == 0 ? "(コミットなし)" : $"HEAD 切り離し @ {s}";
    }

    public bool HasHead() => Git(["rev-parse", "--verify", "-q", "HEAD"]).Ok;

    private bool GitDirHas(string name) =>
        File.Exists(Path.Combine(GitDir, name)) || Directory.Exists(Path.Combine(GitDir, name));

    public RepoOperationState OperationState()
    {
        if (GitDirHas(@"rebase-apply\applying")) return RepoOperationState.ApplyingPatches;
        if (GitDirHas("rebase-merge") || GitDirHas("rebase-apply")) return RepoOperationState.Rebasing;
        if (GitDirHas("MERGE_HEAD")) return RepoOperationState.Merging;
        if (GitDirHas("CHERRY_PICK_HEAD")) return RepoOperationState.CherryPicking;
        if (GitDirHas("REVERT_HEAD")) return RepoOperationState.Reverting;
        if (GitDirHas("BISECT_LOG")) return RepoOperationState.Bisecting;
        return RepoOperationState.None;
    }

    public string? MergeMessage()
    {
        try { return File.ReadAllText(Path.Combine(GitDir, "MERGE_MSG")); } catch { return null; }
    }

    public bool IsBisecting => GitDirHas("BISECT_LOG");
    public bool HasSubmodules => File.Exists(Path.Combine(Root, ".gitmodules"));

    // ---- status ----

    public List<StatusEntry> Status(IEnumerable<string>? paths = null, string untracked = "all", bool ignored = false)
    {
        var args = new List<string> { "--no-optional-locks", "status", "--porcelain=v1", "-z", $"--untracked-files={untracked}" };
        if (ignored) args.Add("--ignored=matching");
        var rels = (paths ?? []).Select(Relative).Where(r => r.Length > 0).ToList();
        if (rels.Count > 0) { args.Add("--"); args.AddRange(rels); }
        return ParseStatus(Git(args).Stdout);
    }

    public static List<string> SplitNul(byte[] data)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0) continue;
            if (i > start) list.Add(Encoding.UTF8.GetString(data, start, i - start));
            start = i + 1;
        }
        if (start < data.Length) list.Add(Encoding.UTF8.GetString(data, start, data.Length - start));
        return list;
    }

    public static List<StatusEntry> ParseStatus(byte[] data)
    {
        var tokens = SplitNul(data);
        var result = new List<StatusEntry>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Length < 4) continue;
            char x = t[0], y = t[1];
            var e = new StatusEntry(t[3..], null, x, y);
            if ((x is 'R' or 'C' || y is 'R' or 'C') && i + 1 < tokens.Count)
            {
                e = e with { OrigPath = tokens[i + 1] };
                i++;
            }
            result.Add(e);
        }
        return result;
    }

    // ---- log ----

    public const string LogFormat = "%H%x1f%h%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s%x1f%b%x1e";

    public List<Commit> Log(int limit, int skip = 0, bool all = false, IEnumerable<string>? paths = null, string search = "",
        string? rev = null, DateTimeOffset? since = null, DateTimeOffset? until = null, string author = "")
    {
        var args = new List<string> { "log", "--date-order", "--format=" + LogFormat, "-n", limit.ToString(), $"--skip={skip}" };
        if (all) args.Add("--all"); else if (!string.IsNullOrEmpty(rev)) args.Add(rev);
        if (since is { } s) args.Add($"--since={s.ToUnixTimeSeconds()}");
        if (until is { } u) args.Add($"--until={u.ToUnixTimeSeconds()}");
        if (author.Length > 0) args.AddRange(["-i", $"--author={author}"]);
        var rels = (paths ?? []).Select(Relative).Where(r => r.Length > 0).ToList();
        if (rels.Count > 0) args.Add("--parents");
        if (search.Length > 0) args.AddRange(["-i", $"--grep={search}"]);
        if (rels.Count > 0) { args.Add("--"); args.AddRange(rels); }
        return ParseLog(Git(args).Out);
    }

    /// <summary>Commits in a revision range such as "origin/main..HEAD".</summary>
    public List<Commit> Commits(string range, int limit = 300, bool reverse = false, bool noMerges = false)
    {
        var args = new List<string> { "log", "--format=" + LogFormat, "-n", limit.ToString() };
        if (reverse) args.Add("--reverse");
        if (noMerges) args.Add("--no-merges");
        args.Add(range);
        var r = Git(args);
        return r.Ok ? ParseLog(r.Out) : [];
    }

    public static List<Commit> ParseLog(string output)
    {
        var list = new List<Commit>();
        foreach (var rec in output.Split('\u001e'))
        {
            var s = rec.StartsWith('\n') ? rec[1..] : rec;
            var f = s.Split('\u001f');
            if (f.Length < 9) continue;
            list.Add(new Commit(
                f[0], f[1],
                f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                f[3], f[4],
                DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[5], out var t) ? t : 0),
                f[6].Length == 0 ? [] : f[6].Split(", "),
                f[7], f[8].Trim()));
        }
        return list;
    }

    public List<ChangedFile> ChangedFiles(Commit commit)
    {
        var r = commit.Parents.Count > 0
            ? Git(["diff", "--name-status", "-z", "-M", commit.Parents[0], commit.Hash])
            : Git(["diff-tree", "--root", "--no-commit-id", "-r", "--name-status", "-z", "-M", commit.Hash]);
        return ParseNameStatus(r.Stdout);
    }

    public List<ChangedFile> ChangedFiles(string from, string to) =>
        ParseNameStatus(Git(["diff", "--name-status", "-z", "-M", from, to]).Stdout);

    public static List<ChangedFile> ParseNameStatus(byte[] data)
    {
        var tokens = SplitNul(data);
        var result = new List<ChangedFile>();
        int i = 0;
        while (i < tokens.Count)
        {
            var st = tokens[i];
            if (st.StartsWith('R') || st.StartsWith('C'))
            {
                if (i + 2 >= tokens.Count) break;
                result.Add(new ChangedFile(st, tokens[i + 2], tokens[i + 1]));
                i += 3;
            }
            else
            {
                if (i + 1 >= tokens.Count) break;
                result.Add(new ChangedFile(st, tokens[i + 1], null));
                i += 2;
            }
        }
        return result;
    }

    // ---- refs ----

    private static IEnumerable<string> Lines(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'));

    private static RefKind KindOf(string full) =>
        full.StartsWith("refs/heads/") ? RefKind.Local : full.StartsWith("refs/remotes/") ? RefKind.Remote : RefKind.Tag;

    public List<RefInfo> Refs()
    {
        const string fmt = "%(refname)%1f%(refname:short)%1f%(objectname:short)%1f%(upstream:short)%1f%(HEAD)";
        var r = Git(["for-each-ref", "--format=" + fmt, "refs/heads", "refs/remotes", "refs/tags"]);
        var list = new List<RefInfo>();
        foreach (var line in Lines(r.Out))
        {
            var f = line.Split('\u001f');
            if (f.Length < 5) continue;
            var kind = KindOf(f[0]);
            if (kind == RefKind.Remote && f[0].EndsWith("/HEAD")) continue;
            list.Add(new RefInfo(f[0], f[1], kind, f[2], f[3], f[4] == "*"));
        }
        return list;
    }

    public List<string> Remotes() => Lines(Git(["remote"]).Out).ToList();

    public (string Remote, string Branch)? Upstream(string branch)
    {
        var remote = Git(["config", "--get", $"branch.{branch}.remote"]).Out.Trim();
        var merge = Git(["config", "--get", $"branch.{branch}.merge"]).Out.Trim();
        if (remote.Length == 0 || merge.Length == 0) return null;
        return (remote, merge.Replace("refs/heads/", ""));
    }

    public List<StashEntry> Stashes()
    {
        var r = Git(["stash", "list", "--format=%gd%x1f%s%x1f%ct"]);
        var list = new List<StashEntry>();
        foreach (var line in Lines(r.Out))
        {
            var f = line.Split('\u001f');
            if (f.Length < 3) continue;
            list.Add(new StashEntry(f[0], f[1], DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[2], out var t) ? t : 0)));
        }
        return list;
    }

    public List<RefDetail> RefsDetailed()
    {
        const string fmt = "%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)%1f%(creatordate:unix)%1f%(subject)%1f%(upstream:track)%1f%(authorname)%(taggername)";
        var r = Git(["for-each-ref", "--format=" + fmt, "refs/heads", "refs/remotes", "refs/tags"]);
        var list = new List<RefDetail>();
        foreach (var line in Lines(r.Out))
        {
            var f = line.Split('\u001f');
            if (f.Length < 9) continue;
            var kind = KindOf(f[0]);
            if (kind == RefKind.Remote && f[0].EndsWith("/HEAD")) continue;
            list.Add(new RefDetail(f[0], f[1], kind, f[2], f[3], f[4] == "*",
                DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[5], out var t) ? t : 0), f[6], f[7], f[8]));
        }
        return list;
    }

    public List<ReflogEntry> Reflog(int limit = 1000)
    {
        var r = Git(["reflog", "-n", limit.ToString(), "--format=%H%x1f%h%x1f%gd%x1f%gs%x1f%ct"]);
        var list = new List<ReflogEntry>();
        foreach (var line in Lines(r.Out))
        {
            var f = line.Split('\u001f');
            if (f.Length < 5) continue;
            list.Add(new ReflogEntry(f[0], f[1], f[2], f[3], DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[4], out var t) ? t : 0)));
        }
        return list;
    }

    public List<TreeEntry> LsTree(string rev, string path)
    {
        var r = Git(["ls-tree", "-z", "-l", rev + ":" + path]);
        if (!r.Ok) return [];
        var list = new List<TreeEntry>();
        foreach (var s in SplitNul(r.Stdout))
        {
            int tab = s.IndexOf('\t');
            if (tab < 0) continue;
            var meta = s[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (meta.Length < 4) continue;
            var name = s[(tab + 1)..];
            list.Add(new TreeEntry(meta[0], meta[1], meta[2], long.TryParse(meta[3], out var sz) ? sz : -1, name,
                path.Length == 0 ? name : path + "/" + name));
        }
        return list.OrderBy(e => e.IsDir ? 0 : 1).ThenBy(e => e.Name, NaturalComparer.Instance).ToList();
    }

    public List<RemoteInfo> RemotesDetailed()
    {
        var dict = new Dictionary<string, RemoteInfo>();
        var order = new List<string>();
        foreach (var line in Lines(Git(["remote", "-v"]).Out))
        {
            var parts = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (!dict.ContainsKey(parts[0])) { dict[parts[0]] = new RemoteInfo(parts[0], "", ""); order.Add(parts[0]); }
            dict[parts[0]] = parts[2] == "(fetch)" ? dict[parts[0]] with { FetchUrl = parts[1] } : dict[parts[0]] with { PushUrl = parts[1] };
        }
        foreach (var name in Remotes())
            if (!dict.ContainsKey(name)) { dict[name] = new RemoteInfo(name, "", ""); order.Add(name); }
        return order.Select(n => dict[n]).ToList();
    }

    public List<SubmoduleInfo> Submodules()
    {
        var list = new List<SubmoduleInfo>();
        foreach (var line in Lines(Git(["submodule", "status", "--recursive"]).Out))
        {
            if (line.Length == 0) continue;
            var parts = line[1..].Split(' ', 3);
            if (parts.Length < 2) continue;
            list.Add(new SubmoduleInfo(line[0], parts[0], parts[1], parts.Length > 2 ? parts[2] : ""));
        }
        return list;
    }

    /// <summary>Writes the blob at rev:path into a temp file and returns its path.</summary>
    public string? ExportBlob(string rev, string path)
    {
        var r = Git(["show", $"{rev}:{path}"]);
        if (!r.Ok) return null;
        var dir = KGTemp.PathFor("blob-" + KGTemp.NewId(8));
        Directory.CreateDirectory(dir);
        var isHash = rev.Length > 10 && rev.All(Uri.IsHexDigit);
        var shortRev = isHash ? rev[..8] : string.Concat(rev.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == '/' ? '_' : ch));
        var name = path.Split('/').Last();
        var ext = Path.GetExtension(name);
        var bas = Path.GetFileNameWithoutExtension(name);
        var file = Path.Combine(dir, ext.Length == 0 ? $"{bas}-{shortRev}" : $"{bas}-{shortRev}{ext}");
        try { File.WriteAllBytes(file, r.Stdout); } catch { return null; }
        return file;
    }

    public string Config(string key) => Git(["config", "--get", key]).Out.Trim();

    /// <summary>Writes pathspecs to a temp file for --pathspec-from-file (avoids command line limits).</summary>
    public string? PathspecFile(IEnumerable<string> rels)
    {
        var file = KGTemp.PathFor("pathspec-" + KGTemp.NewId());
        try
        {
            using var fs = File.Create(file);
            foreach (var r in rels)
            {
                fs.Write(Encoding.UTF8.GetBytes(r));
                fs.WriteByte(0);
            }
        }
        catch { return null; }
        return file;
    }

    public string? MessageFile(string message)
    {
        var file = KGTemp.PathFor("msg-" + KGTemp.NewId() + ".txt");
        try { File.WriteAllBytes(file, Encoding.UTF8.GetBytes(message)); } catch { return null; }
        return file;
    }
}

/// <summary>"file2" before "file10" (like Explorer / Finder sorting).</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    public int Compare(string? x, string? y)
    {
        try { return StrCmpLogicalW(x ?? "", y ?? ""); }
        catch { return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase); }
    }
}
