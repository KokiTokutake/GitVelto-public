namespace GitVelto.Core;

/// <summary>One step of a progress dialog.</summary>
public abstract record ProgressStep
{
    public sealed record GitCmd(IReadOnlyList<string> Args, Dictionary<string, string>? Env = null) : ProgressStep;
    /// <summary>Arbitrary work run on a background thread; returns text to show. Throw to fail.</summary>
    public sealed record Custom(string Title, Func<string> Work) : ProgressStep;

    public static ProgressStep Git(params string[] args) => new GitCmd(args);
    public static ProgressStep Git(IEnumerable<string> args, Dictionary<string, string>? env = null) => new GitCmd(args.ToList(), env);
}

public enum RebaseAction { Pick, Reword, Edit, Squash, Fixup, Drop }

public static class RebaseActionExt
{
    public static string Id(this RebaseAction a) => a.ToString().ToLowerInvariant();

    public static string Label(this RebaseAction a) => a switch
    {
        RebaseAction.Pick => "採用 (pick)",
        RebaseAction.Reword => "メッセージ変更",
        RebaseAction.Edit => "編集で停止",
        RebaseAction.Squash => "前と統合 (squash)",
        RebaseAction.Fixup => "前と統合・メッセージ破棄",
        _ => "削除 (drop)",
    };
}

public sealed class RebaseItem(Commit commit, string message, RebaseAction action = RebaseAction.Pick)
{
    public string Id => Commit.Hash;
    public Commit Commit { get; } = commit;
    public RebaseAction Action { get; set; } = action;
    public string Message { get; set; } = message;
}

/// <summary>Builds the git command sequences for the main operations (kept out of the views so they can be tested).</summary>
public static class GitOps
{
    public sealed record CommitOptions(string Message)
    {
        public bool Amend { get; init; }
        public string? Author { get; init; }
        public DateTimeOffset? Date { get; init; }
        public string? NewBranch { get; init; }
        public bool Push { get; init; }
        public bool SignOff { get; init; }
    }

    private static ProgressStep TempError() => new ProgressStep.Custom("一時ファイルを作成しています",
        () => throw new GitException($"一時ファイルを作成できません: {KGTemp.Dir}"));

    private static string[] FromFile(string file) => [$"--pathspec-from-file={file}", "--pathspec-file-nul"];

    /// <summary>TortoiseGit-style commit: only the checked entries are committed (staged state of others is kept).</summary>
    public static List<ProgressStep> CommitSteps(Repo repo, IEnumerable<StatusEntry> entries, CommitOptions o)
    {
        // Paths for the commit pathspec: new and old paths (so renames/deletions are committed).
        var rels = new List<string>();
        // Paths to `git add`: only those that exist in the work tree or still exist in the index.
        // Staged deletions and the old side of staged renames are no longer known to the index,
        // and `git add -- <path>` would fail with "pathspec did not match".
        var addRels = new List<string>();
        var removeRels = new List<string>();
        foreach (var e in entries)
        {
            rels.Add(e.Path);
            if (e.OrigPath != null) rels.Add(e.OrigPath);
            var abs = repo.Absolute(e.Path);
            bool inWorktree = File.Exists(abs) || Directory.Exists(abs);
            bool inIndex = e.Y != ' ' && e.Y != '?';   // has unstaged changes => present in index
            if (inWorktree) addRels.Add(e.Path);
            // Deleted from the work tree but still in the index: stage the deletion with "rm --cached".
            // ("add -A" would also work, but warns "could not open directory" when the whole folder is gone.)
            else if (inIndex) removeRels.Add(e.Path);
            if (e.OrigPath != null)
            {
                var o2 = repo.Absolute(e.OrigPath);
                if (File.Exists(o2) || Directory.Exists(o2)) addRels.Add(e.OrigPath);
            }
        }
        bool merging = repo.OperationState() == RepoOperationState.Merging;
        var steps = new List<ProgressStep>();
        if (!string.IsNullOrEmpty(o.NewBranch)) steps.Add(ProgressStep.Git("switch", "-c", o.NewBranch));
        if (addRels.Count > 0)
        {
            if (repo.PathspecFile(addRels) is not { } f) return [TempError()];
            steps.Add(ProgressStep.Git(["--literal-pathspecs", "add", "-A", .. FromFile(f)]));
        }
        if (removeRels.Count > 0)
        {
            if (repo.PathspecFile(removeRels) is not { } f) return [TempError()];
            steps.Add(ProgressStep.Git(["--literal-pathspecs", "rm", "--cached", "-q", "--ignore-unmatch", .. FromFile(f)]));
        }
        if (repo.MessageFile(o.Message) is not { } msgFile) return [TempError()];
        var args = new List<string> { "--literal-pathspecs", "commit", "-F", msgFile, "--cleanup=strip" };
        if (o.Amend) args.Add("--amend");
        if (o.SignOff) args.Add("-s");
        if (!string.IsNullOrEmpty(o.Author)) args.Add($"--author={o.Author}");
        if (o.Date is { } d) args.Add("--date=" + d.ToString("yyyy-MM-dd'T'HH:mm:sszzz"));
        if (merging)
        {
            // Partial commits are not allowed during a merge: commit the whole index.
        }
        else if (rels.Count == 0)
        {
            args.Add(o.Amend ? "--only" : "--allow-empty");
        }
        else
        {
            if (repo.PathspecFile(rels) is not { } f) return [TempError()];
            args.AddRange(FromFile(f));
        }
        steps.Add(ProgressStep.Git(args));

        if (o.Push)
        {
            var branch = !string.IsNullOrEmpty(o.NewBranch) ? o.NewBranch : repo.CurrentBranch();
            if (branch != null && repo.Upstream(branch) != null)
            {
                steps.Add(ProgressStep.Git(o.Amend ? ["push", "--progress", "--force-with-lease"] : ["push", "--progress"]));
            }
            else if (branch != null && repo.Remotes().FirstOrDefault() is { } remote)
            {
                steps.Add(ProgressStep.Git("push", "--progress", "-u", remote, branch));
            }
        }
        return steps;
    }

    /// <summary>Revert (discard) changes of the given entries. Added files become untracked again (file is kept).</summary>
    public static List<ProgressStep> RevertSteps(Repo repo, IEnumerable<StatusEntry> entries)
    {
        var list = entries.ToList();
        var steps = new List<ProgressStep>();
        var added = list.Where(e => e.State == FileState.Added).Select(e => e.Path);
        var renamedNew = list.Where(e => e.State is FileState.Renamed or FileState.Copied).Select(e => e.Path);
        var restore = list.Where(e => e.State is not (FileState.Added or FileState.Renamed or FileState.Copied) && e.State.IsTracked())
            .Select(e => e.Path).ToList();
        restore.AddRange(list.Where(e => e.OrigPath != null).Select(e => e.OrigPath!));
        var unstage = added.Concat(renamedNew).ToList();
        if (unstage.Count > 0 && repo.PathspecFile(unstage) is { } f1)
            steps.Add(ProgressStep.Git(["--literal-pathspecs", "rm", "--cached", "-r", "-q", .. FromFile(f1)]));
        if (restore.Count > 0 && repo.PathspecFile(restore) is { } f2)
        {
            steps.Add(repo.HasHead()
                ? ProgressStep.Git(["--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", .. FromFile(f2)])
                : ProgressStep.Git(["--literal-pathspecs", "restore", "--worktree", .. FromFile(f2)]));
        }
        return steps;
    }

    /// <summary>Path in a form that git's bundled sh accepts inside single quotes.</summary>
    public static string ShPath(string windowsPath) => windowsPath.Replace('\\', '/').Replace("'", "'\\''");

    /// <summary>Interactive rebase driven by a prepared todo list (GIT_SEQUENCE_EDITOR copies it into place).</summary>
    public static ProgressStep RebaseStep(Repo repo, IReadOnlyList<RebaseItem> items, IReadOnlyList<string> originalOrder, string upstream, bool autostash)
    {
        var first = items.FirstOrDefault(i => i.Action != RebaseAction.Drop);
        if (first != null && first.Action is RebaseAction.Squash or RebaseAction.Fixup)
            throw new GitException("最初のコミットは統合 (squash/fixup) にできません");
        bool unchanged = items.Select(i => i.Id).SequenceEqual(originalOrder) && items.All(i => i.Action == RebaseAction.Pick);
        var args = new List<string> { "rebase" };
        if (autostash) args.Add("--autostash");
        var env = new Dictionary<string, string>();
        if (!unchanged)
        {
            var todo = new List<string>();
            foreach (var it in items)
            {
                if (it.Action == RebaseAction.Reword)
                {
                    todo.Add($"pick {it.Commit.Hash} {it.Commit.Subject}");
                    if (repo.MessageFile(it.Message) is { } f)
                        todo.Add($"exec git commit --amend --no-verify -F '{ShPath(f)}'");
                }
                else
                {
                    todo.Add($"{it.Action.Id()} {it.Commit.Hash} {it.Commit.Subject}");
                }
            }
            var file = KGTemp.PathFor("rebase-todo-" + KGTemp.NewId());
            File.WriteAllText(file, string.Join("\n", todo) + "\n", new System.Text.UTF8Encoding(false));
            args.Add("-i");
            env["GIT_SEQUENCE_EDITOR"] = $"cp '{ShPath(file)}'";
        }
        args.Add(upstream);
        return ProgressStep.Git(args, env);
    }

    // ---- log / reference browser context menu operations ----

    public static List<string> RevertCommit(Commit c)
    {
        var args = new List<string> { "revert", "--no-edit" };
        if (c.Parents.Count > 1) args.AddRange(["-m", "1"]);
        args.Add(c.Hash);
        return args;
    }

    /// <summary>Cherry-picks oldest first. <paramref name="commits"/> are in log order (newest first).</summary>
    public static List<string> CherryPick(IEnumerable<Commit> commits) => ["cherry-pick", .. commits.Reverse().Select(c => c.Hash)];

    public static List<string> Reset(string mode, string hash) => ["reset", $"--{mode}", hash];
    public static List<string> Detach(string hash) => ["switch", "--detach", hash];
    public static List<string> DeleteBranches(IEnumerable<string> names, bool force) => ["branch", force ? "-D" : "-d", .. names];
    public static List<string> RenameBranch(string old, string @new) => ["branch", "-m", old, @new];
    public static List<string> SetUpstream(string upstream, string branch) => ["branch", "-u", upstream, branch];

    /// <summary>"origin/feature/x" → git push origin --delete feature/x</summary>
    public static List<ProgressStep> DeleteRemoteBranches(IEnumerable<string> refs) =>
        refs.Select(n => n.Split('/', 2)).Where(p => p.Length == 2)
            .Select(p => ProgressStep.Git("push", "--progress", p[0], "--delete", p[1])).ToList();

    public static List<string> DeleteTags(IEnumerable<string> names) => ["tag", "-d", .. names];

    public static List<string> DeleteRemoteTags(IEnumerable<string> names, string remote) =>
        ["push", "--progress", remote, "--delete", .. names.Select(n => "refs/tags/" + n)];

    /// <summary>Runs steps synchronously (tests and simple flows). Returns combined output and success.</summary>
    public static (bool Ok, string Output) RunSync(IEnumerable<ProgressStep> steps, string dir)
    {
        var o = new System.Text.StringBuilder();
        foreach (var s in steps)
        {
            switch (s)
            {
                case ProgressStep.GitCmd g:
                    var r = Git.Run(g.Args, dir, null, g.Env);
                    o.Append(r.Out).Append(r.Err);
                    if (!r.Ok) return (false, o.ToString());
                    break;
                case ProgressStep.Custom c:
                    try { o.Append(c.Work()); }
                    catch (Exception e) { return (false, o + e.Message); }
                    break;
            }
        }
        return (true, o.ToString());
    }
}
