using GitVelto.Core;

namespace GitVelto.Tests;

/// <summary>
/// Runs GitVelto's git command builders against real sample repositories
/// (port of Tests/Functional/main.swift of the macOS version; same sample repositories).
/// </summary>
public class FunctionalTests
{
    private static string Sh(string cmd, string dir) => TestEnv.Sh(cmd, dir);

    [Fact]
    public void Scenario()
    {
        var c = new Checks();
        var b = TestEnv.MakeTestRepos();
        var work = new Repo(Path.Combine(b, "work"));
        var conflict = new Repo(Path.Combine(b, "conflict"));

        // ■ status / parsers
        var st = work.Status();
        c.Check(st.Any(e => e.Path == "README.md" && e.State == FileState.Modified), "README modified");
        c.Check(st.Any(e => e.Path == "docs/guide.md" && e.State == FileState.Deleted), "guide deleted");
        c.Check(st.Any(e => e.Path == "src/new_feature.swift" && e.State == FileState.Added && e.Staged), "new_feature added+staged");
        c.Check(st.Any(e => e.Path == "日本語メモ.txt" && e.State == FileState.Untracked), "Japanese file untracked");
        var cst = conflict.Status();
        c.Check(cst.Count(e => e.State == FileState.Conflict) == 2 && cst.All(e => !e.Staged), "conflicts detected, not staged");
        c.Check(conflict.OperationState() == RepoOperationState.Merging, "merge state");
        var snap = new RepoSnapshot(work.Root, work.Status(untracked: "normal", ignored: true), true);
        c.Check(snap.Badge("build", true) == BadgeKind.Ignored, "badge build/ ignored");
        c.Check(snap.Badge("debug.log", false) == BadgeKind.Ignored, "badge *.log ignored");
        c.Check(snap.Badge("src", true) == BadgeKind.Modified, "badge src/ modified (contains added)");
        c.Check(snap.Badge("src/main.swift", false) == BadgeKind.Normal, "badge clean file normal");
        c.Check(snap.Badge("SRC/Main.swift", false) == BadgeKind.Normal && snap.Badge("Src", true) == BadgeKind.Modified, "badge case-insensitive");
        c.Check(snap.Badge("日本語メモ.txt".Normalize(System.Text.NormalizationForm.FormD), false) == BadgeKind.Untracked, "badge NFD lookup");
        c.Check(snap.Overall == BadgeKind.Modified, "overall modified");
        c.Check(RepoSnapshot.Compute(work.Root).Overall == BadgeKind.Modified, "snapshot compute");
        var refs = work.RefsDetailed();
        c.Check(refs.FirstOrDefault(r => r.Name == "v1.1") is { } t && t.Date.ToUnixTimeSeconds() > 1_000_000 && t.Author.Length > 0, "annotated tag date/author");
        c.Check(work.Reflog().Count >= 5, "reflog entries");
        c.Check(work.LsTree("HEAD", "src").Select(e => e.Name).SequenceEqual(["login.swift", "main.swift", "math.swift"]), "ls-tree src");
        c.Check(work.RemotesDetailed().FirstOrDefault()?.Name == "origin", "remotes");
        c.Check(work.Stashes().Count == 1, "stash list");
        var commits = work.Log(50, all: true);
        var graph = CommitGraph.Compute(commits);
        c.Check(graph.Count == commits.Count && graph.All(g => g.Node >= 0), $"graph computed for {commits.Count} commits");
        c.Check(work.Commits("main..origin/main").Count == 1, "incoming = 1");
        c.Check(work.Commits("origin/main..main").Count == 2, "outgoing = 2");
        c.Check(work.CurrentBranch() == "main" && PathUtil.BranchName(work.Root) == "main", "current branch");
        c.Check(Repo.At(Path.Combine(work.Root, "src", "main.swift"))?.Root == work.Root, "Repo.At from file");
        var blob = work.ExportBlob("v1.0", "src/main.swift");
        c.Check(blob != null && File.ReadAllText(blob).Contains("print(\"hello\")"), "export blob at v1.0");

        // ■ commit (only checked files)
        var commitEntries = work.Status().Where(e => e.Path is "README.md" or "docs/guide.md").ToList();
        var r1 = GitOps.RunSync(GitOps.CommitSteps(work, commitEntries, new("README とガイドを更新")), work.Root);
        c.Check(r1.Ok, "commit ok", r1.Output);
        var after = work.Status();
        c.Check(!after.Any(e => e.Path == "README.md") && !after.Any(e => e.Path == "docs/guide.md"), "committed files clean");
        c.Check(after.Any(e => e.Path == "src/new_feature.swift" && e.Staged), "unchecked staged file stays staged");
        c.Check(Sh("git log -1 --format=%s", work.Root) == "README とガイドを更新", "commit message (Japanese)", Sh("git log -1 --format=%s", work.Root));
        c.Check(!Sh("git show --stat --format= HEAD", work.Root).Contains("new_feature"), "new_feature not in commit");

        // ■ commit untracked + amend + author + new branch
        var jp = work.Status().Where(e => e.State == FileState.Untracked).ToList();
        var r2 = GitOps.RunSync(GitOps.CommitSteps(work, jp, new("メモを追加") { Author = "Hanako <hanako@example.com>", NewBranch = "topic/memo" }), work.Root);
        c.Check(r2.Ok, "commit untracked on new branch", r2.Output);
        c.Check(work.CurrentBranch() == "topic/memo", "switched to new branch");
        c.Check(Sh("git log -1 --format=%an", work.Root) == "Hanako", "author override");
        var r3 = GitOps.RunSync(GitOps.CommitSteps(work, [], new("メモを追加 (修正)") { Amend = true }), work.Root);
        c.Check(r3.Ok && Sh("git log -1 --format=%s", work.Root) == "メモを追加 (修正)", "amend message only", r3.Output);
        var r4 = GitOps.RunSync(GitOps.CommitSteps(work, [], new("空コミット") { SignOff = true }), work.Root);
        c.Check(r4.Ok && Sh("git log -1 --format=%s", work.Root) == "空コミット", "allow-empty commit", r4.Output);
        c.Check(Sh("git log -1 --format=%b", work.Root).Contains("Signed-off-by: Test User"), "sign-off");

        // ■ commit staged rename + staged deletion
        Sh("git mv src/main.swift src/hello.swift && git rm -q src/login.swift && printf 'z' >> README.md", work.Root);
        var staged = work.Status().Where(e => e.Path is "src/hello.swift" or "src/login.swift").ToList();
        c.Check(staged.Count == 2, "prepared staged rename + deletion");
        var r4b = GitOps.RunSync(GitOps.CommitSteps(work, staged, new("名前変更と削除")), work.Root);
        c.Check(r4b.Ok, "commit staged rename/deletion", r4b.Output);
        c.Check(Sh("git show --name-status --format= -M HEAD", work.Root).Contains("R100\tsrc/main.swift\tsrc/hello.swift"), "rename committed");
        c.Check(Sh("git show --name-status --format= HEAD", work.Root).Contains("D\tsrc/login.swift"), "deletion committed");
        c.Check(work.Status().Any(e => e.Path == "README.md"), "unchecked README change left uncommitted");
        Sh("git checkout -q -- README.md && git mv src/hello.swift src/main.swift && git checkout -q HEAD~1 -- src/login.swift && git commit -qm restore", work.Root);

        // ■ commit files of a folder that was deleted entirely (no "could not open directory" warning)
        Sh("mkdir -p 画像 && printf 'a' > 画像/a.jpg && printf 'b' > 画像/b.jpg && git add 画像 && git commit -qm 'add images' && rm -rf 画像", work.Root);
        var gone = work.Status().Where(e => e.Path.StartsWith("画像/")).ToList();
        c.Check(gone.Count == 2 && gone.All(e => e.State == FileState.Deleted), "prepared deleted folder", string.Join(",", gone.Select(e => e.Path)));
        var rg = GitOps.RunSync(GitOps.CommitSteps(work, gone, new("画像フォルダを削除")), work.Root);
        c.Check(rg.Ok && !rg.Output.Contains("could not open directory"), "commit deleted folder without warning", rg.Output);
        c.Check(!work.Status().Any(e => e.Path.StartsWith("画像/")), "deleted folder committed");

        // ■ revert
        Sh("printf 'x' >> src/math.swift && printf 'y' > added.txt && git add added.txt && git mv src/login.swift src/auth.swift", work.Root);
        var rv = work.Status().Where(e => e.Path is "src/math.swift" or "added.txt" or "src/auth.swift").ToList();
        c.Check(rv.Count == 3, "prepared 3 changes", string.Join(",", rv.Select(e => e.Path)));
        var r5 = GitOps.RunSync(GitOps.RevertSteps(work, rv), work.Root);
        c.Check(r5.Ok, "revert ok", r5.Output);
        var st5 = work.Status();
        c.Check(!st5.Any(e => e.Path == "src/math.swift"), "modified file restored");
        c.Check(st5.Any(e => e.Path == "added.txt" && e.State == FileState.Untracked), "added file unstaged, kept");
        c.Check(File.Exists(Path.Combine(work.Root, "src", "login.swift")), "renamed file restored");
        Sh("rm -f added.txt src/auth.swift", work.Root);

        // ■ interactive rebase (reorder + squash + reword + drop)
        Sh("git switch -q main && git stash -u -q || true", work.Root);
        var rb = work.Commits("v1.1..HEAD", reverse: true, noMerges: true);
        c.Check(rb.Count == 3, "3 commits to rebase", string.Join(",", rb.Select(x => x.Subject)));
        if (rb.Count == 3)
        {
            var items = rb.Select(x => new RebaseItem(x, x.Subject)).ToList();
            (items[0], items[2]) = (items[2], items[0]); // README first
            items[0].Action = RebaseAction.Reword;
            items[0].Message = "ドキュメント更新 (改名)";
            items[1].Action = RebaseAction.Squash;
            items[2].Action = RebaseAction.Pick;
            // order: README(reword), 引き算(squash into README), 挨拶(pick)
            var step = GitOps.RebaseStep(work, items, rb.Select(x => x.Hash).ToList(), "v1.1", true);
            var r6 = GitOps.RunSync([step], work.Root);
            c.Check(r6.Ok, "rebase ok", r6.Output);
            var subjects = work.Commits("v1.1..HEAD").Select(x => x.Subject).ToList();
            c.Check(subjects.Count == 2 && subjects[^1] == "ドキュメント更新 (改名)", "rebased history", string.Join(",", subjects));
            c.Check(work.OperationState() == RepoOperationState.None, "rebase finished");
            var dropItems = work.Commits("v1.1..HEAD", reverse: true).Select(x => new RebaseItem(x, x.Subject, RebaseAction.Drop)).ToList();
            dropItems[0].Action = RebaseAction.Pick;
            var r7 = GitOps.RunSync([GitOps.RebaseStep(work, dropItems, dropItems.Select(x => x.Id).ToList(), "v1.1", true)], work.Root);
            c.Check(r7.Ok && work.Commits("v1.1..HEAD").Count == 1, "drop commit", r7.Output);
            c.Check(Throws(() => GitOps.RebaseStep(work, [new RebaseItem(rb[0], "", RebaseAction.Squash)], [], "v1.1", false)), "first squash rejected");
        }

        // ■ merge conflict resolution flow
        var mainPath = Path.Combine(conflict.Root, "src", "main.swift");
        var csegs = ConflictParser.Parse(File.ReadAllText(mainPath));
        c.Check(csegs.Count(s => s.IsConflict) == 1, "1 conflict block");
        foreach (var s in csegs.Where(s => s.IsConflict)) s.Apply(ConflictResolution.OursThenTheirs);
        File.WriteAllText(mainPath, string.Concat(csegs.Select(s => s.Output)));
        conflict.Git(["add", "src/main.swift"]);
        conflict.Git(["checkout", "--theirs", "README.md"]);
        conflict.Git(["add", "README.md"]);
        c.Check(conflict.Status().All(e => e.State != FileState.Conflict), "all conflicts resolved");
        var r8 = GitOps.RunSync(GitOps.CommitSteps(conflict, conflict.Status(), new("Merge topic")), conflict.Root);
        c.Check(r8.Ok && conflict.OperationState() == RepoOperationState.None, "merge commit", r8.Output);
        c.Check(Sh("git show HEAD:README.md", conflict.Root) == "README from topic", "theirs taken");

        // ■ push / pull with local remote
        var r9 = GitOps.RunSync([ProgressStep.Git("push", "--progress", "origin", "main:main")], conflict.Root);
        c.Check(r9.Ok, "push ok", r9.Output);
        var other = Path.Combine(b, "other");
        var r10 = GitOps.RunSync([ProgressStep.Git("pull", "--progress", "--no-rebase", "origin", "main")], other);
        c.Check(r10.Ok && Sh("git log -1 --format=%s", other) == "Merge topic", "pull ok", r10.Output);

        // ■ log / reference browser context menu operations
        Sh("git switch -q -c ctx-test main && printf 'ctx\\n' > ctx.txt && git add ctx.txt && git commit -qm 'ctx 追加' && printf 'ctx2\\n' >> ctx.txt && git commit -qam 'ctx 更新' && git switch -q main && git commit -q --allow-empty -m 'main を進める'", work.Root);
        var ctxCommits = work.Commits("main..ctx-test");
        c.Check(ctxCommits.Count == 2, "prepared 2 commits on ctx-test");
        var rcp = GitOps.RunSync([ProgressStep.Git(GitOps.CherryPick(ctxCommits))], work.Root);
        c.Check(rcp.Ok && Sh("cat ctx.txt", work.Root) == "ctx\nctx2", "cherry-pick 2 commits in order", rcp.Output);
        var head = work.Log(1)[0];
        var rrv = GitOps.RunSync([ProgressStep.Git(GitOps.RevertCommit(head))], work.Root);
        c.Check(rrv.Ok && Sh("cat ctx.txt", work.Root) == "ctx", "revert last commit", rrv.Output);
        var mergeCommit = work.Log(50).FirstOrDefault(x => x.Parents.Count > 1);
        c.Check(mergeCommit != null, "merge commit in history");
        if (mergeCommit != null)
        {
            var rrm = GitOps.RunSync([ProgressStep.Git(GitOps.RevertCommit(mergeCommit))], work.Root);
            c.Check(rrm.Ok, "revert merge commit (-m 1)", rrm.Output);
        }
        var before = Sh("git rev-parse HEAD~2", work.Root);
        var rrs = GitOps.RunSync([ProgressStep.Git(GitOps.Reset("hard", before))], work.Root);
        c.Check(rrs.Ok && Sh("git rev-parse HEAD", work.Root) == before, "reset --hard", rrs.Output);
        var rdt = GitOps.RunSync([ProgressStep.Git(GitOps.Detach(before))], work.Root);
        c.Check(rdt.Ok && work.CurrentBranch() == null, "switch --detach", rdt.Output);
        Sh("git switch -q main", work.Root);
        c.Check(!GitOps.RunSync([ProgressStep.Git(GitOps.DeleteBranches(["ctx-test"], false))], work.Root).Ok, "unmerged branch delete refused (-d)");
        c.Check(GitOps.RunSync([ProgressStep.Git(GitOps.RenameBranch("ctx-test", "ctx-renamed"))], work.Root).Ok
            && Sh("git branch --list ctx-renamed", work.Root).Contains("ctx-renamed"), "rename branch");
        c.Check(GitOps.RunSync([ProgressStep.Git(GitOps.SetUpstream("origin/main", "ctx-renamed"))], work.Root).Ok
            && work.Upstream("ctx-renamed")?.Branch == "main", "set upstream");
        c.Check(GitOps.RunSync([ProgressStep.Git(GitOps.DeleteBranches(["ctx-renamed"], true))], work.Root).Ok, "force delete branch (-D)");
        Sh("git push -q origin main:refs/heads/tmp-remote 2>/dev/null; git tag tmp-tag && git push -q origin tmp-tag 2>/dev/null; git fetch -q origin", work.Root);
        c.Check(GitOps.RunSync(GitOps.DeleteRemoteBranches(["origin/tmp-remote"]), work.Root).Ok
            && !Sh("git ls-remote --heads origin", work.Root).Contains("tmp-remote"), "delete remote branch");
        c.Check(GitOps.RunSync([ProgressStep.Git(GitOps.DeleteRemoteTags(["tmp-tag"], "origin")), ProgressStep.Git(GitOps.DeleteTags(["tmp-tag"]))], work.Root).Ok
            && !Sh("git ls-remote --tags origin", work.Root).Contains("tmp-tag") && Sh("git tag --list tmp-tag", work.Root).Length == 0, "delete tag locally and on remote");

        // ■ archive / patch
        var zip = Path.Combine(b, "out.zip");
        var r11 = GitOps.RunSync([ProgressStep.Git("archive", "--format=zip", "-o", zip, "HEAD")], work.Root);
        c.Check(r11.Ok && File.Exists(zip), "archive zip", r11.Output);
        var patches = Path.Combine(b, "patches");
        var r12 = GitOps.RunSync([ProgressStep.Git("format-patch", "-1", "-o", patches, "HEAD")], work.Root);
        c.Check(r12.Ok && Directory.Exists(patches) && Directory.GetFiles(patches).Length == 1, "format-patch", r12.Output);

        c.AssertAll();
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; } catch (GitException) { return true; }
    }
}
