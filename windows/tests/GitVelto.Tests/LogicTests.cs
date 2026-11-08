using GitVelto.Core;

namespace GitVelto.Tests;

/// <summary>Pure logic: translation, learning mode, menu planner, parsers.</summary>
public class LogicTests
{
    [Theory]
    [InlineData("Already up to date.", "すでに最新です。")]
    [InlineData("CONFLICT (content): Merge conflict in a.txt", "競合 (内容): a.txt でマージ競合が発生しました")]
    [InlineData("Receiving objects: 100% (3/3), done.", "オブジェクトを受信中: 100% (3/3), done.")]
    [InlineData("\tmodified:   README.md", "\t変更:     README.md")]
    [InlineData("some unknown line", "some unknown line")]
    [InlineData("hint: use 'git pull' before pushing again.", "ヒント: 再度プッシュする前に 'git pull' を実行してください。")]
    [InlineData("remote: Counting objects: 5", "remote: オブジェクトを数えています: 5")]
    public void TranslateExact(string en, string ja) => Assert.Equal(ja, GitMessages.TranslateLine(en));

    [Theory]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git", "致命的エラー: Git リポジトリではありません")]
    [InlineData("hint: If you are sure you want to delete it, run 'git branch -D ctx'", "強制削除")]
    [InlineData("hint: To restore the original branch and stop patching, run \"git am --abort\".", "中止")]
    [InlineData("Version 71d3852 of docs/guide.md left in tree.", "作業ツリーに残されています")]
    [InlineData("  (use \"git pull\" to update your local branch)", "ローカルブランチを更新")]
    [InlineData("error: CONFLICT (content): Merge conflict in x", "エラー: 競合 (内容)")]
    public void TranslateContains(string en, string part) => Assert.Contains(part, GitMessages.TranslateLine(en));

    [Fact]
    public void TranslateMultiLineWithCrLf() =>
        Assert.Equal("すでに最新です。\nすべて最新です", GitMessages.Translate("Already up to date.\r\nEverything up-to-date"));

    [Fact]
    public void Glossary()
    {
        string[] usedKeys = ["amend", "autostash", "bare", "bisect", "blame", "branch", "cherry-pick", "clean", "clone", "commit", "conflict",
            "depth", "detached", "fast-forward", "fixup", "force-push", "hash", "ignore", "log", "main", "merge", "no-ff", "origin",
            "ours-theirs", "patch", "prune", "push", "rebase", "reflog", "remote", "repository", "reset", "revert", "revert-file",
            "signoff", "squash", "stage", "stash", "submodule", "switch", "tag", "tracked", "untracked", "upstream", "modified"];
        Assert.DoesNotContain(usedKeys, k => Core.Glossary.Entry(k) == null);
        Assert.Empty(Core.Glossary.Entries.SelectMany(e => e.Related.Where(r => Core.Glossary.Entry(r) == null).Select(r => $"{e.Id}->{r}")));
        Assert.Equal(Core.Glossary.Entries.Length, Core.Glossary.Entries.Select(e => e.Id).Distinct().Count());
        Assert.True(Core.Glossary.Entries.Length >= 45);
        Assert.Contains(Core.Glossary.Search("リベース"), e => e.Id == "rebase");
        Assert.Contains(Core.Glossary.Search("push"), e => e.Id == "push");
        Assert.DoesNotContain(Core.Glossary.Entries, e => e.Detail.Contains("Finder"));
    }

    [Fact]
    public void ExplainCommands()
    {
        var ex = CommandExplainer.Explain(["pull", "--progress", "--rebase", "--autostash", "origin", "main"]);
        Assert.Equal("git pull --progress --rebase --autostash origin main", ex.Command);
        Assert.Contains("取り込み", ex.Summary);
        Assert.Equal(["--progress", "--rebase", "--autostash"], ex.Options.Select(o => o.Option));

        var ex2 = CommandExplainer.Explain(["--literal-pathspecs", "commit", "-m", "日本語 メッセージ", "--amend"]);
        Assert.Equal("git commit -m \"日本語 メッセージ\" --amend", ex2.Command);
        Assert.Equal(["-m", "--amend"], ex2.Options.Select(o => o.Option));

        var ex3 = CommandExplainer.Explain(["-c", "protocol.file.allow=always", "submodule", "add", "--progress", "--", "/a", "b"]);
        Assert.Contains("サブモジュール", ex3.Summary);
        Assert.Contains(ex3.Options, o => o.Option == "add");

        Assert.Contains("捨て", CommandExplainer.Explain(["reset", "--hard", "abc"]).Options[0].Meaning);
        // Windows paths are shown as typed (no doubled backslashes); GitVelto's temp files are shortened.
        Assert.Equal(@"git apply C:\work\a.patch", CommandExplainer.Display(["apply", @"C:\work\a.patch"]));
        Assert.Equal(@"git commit -F %TEMP%\GitVelto\msg-1.txt", CommandExplainer.Display(["commit", "-F", KGTemp.PathFor("msg-1.txt")]));
        Assert.Equal("git add \"C:\\My Files\\x.txt\"", CommandExplainer.Display(["add", @"C:\My Files\x.txt"]));
        Assert.Contains(CommandExplainer.Explain(["commit", "--cleanup=strip"]).Options, o => o.Option == "--cleanup=strip");
    }

    [Fact]
    public void CommandHistoryRecords()
    {
        var file = Path.Combine(TestEnv.Temp, "hist-" + Guid.NewGuid().ToString("N") + ".json");
        var h = new CommandHistory(file);
        var id = h.Record(["status"], @"C:\tmp");
        h.Finish(id, true);
        Assert.True(h.Entries[0].Ok);
        h.Save();
        Assert.Equal("git status", new CommandHistory(file).Entries[0].Display);
    }

    [Fact]
    public void ParseStatusAndNameStatus()
    {
        var data = System.Text.Encoding.UTF8.GetBytes(" M a.txt\0R  new.txt\0old.txt\0?? 日本語.txt\0UU c.txt\0");
        var st = Repo.ParseStatus(data);
        Assert.Equal(4, st.Count);
        Assert.Equal(FileState.Modified, st[0].State);
        Assert.False(st[0].Staged);
        Assert.Equal(FileState.Renamed, st[1].State);
        Assert.Equal("old.txt", st[1].OrigPath);
        Assert.True(st[1].Staged);
        Assert.Equal(FileState.Untracked, st[2].State);
        Assert.Equal(FileState.Conflict, st[3].State);

        var ns = Repo.ParseNameStatus(System.Text.Encoding.UTF8.GetBytes("M\0a\0R100\0b\0c\0D\0d\0"));
        Assert.Equal(["a", "c", "d"], ns.Select(n => n.Path));
        Assert.Equal("b", ns[1].OldPath);
    }

    [Fact]
    public void DiffParserSideBySide()
    {
        var text = "diff --git a/x b/x\nindex 1..2\n--- a/x\n+++ b/x\n@@ -1,3 +1,3 @@\n a\n-b\n+B\n c\n\\ No newline at end of file\n";
        var lines = DiffParser.Parse(text);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Del && l.OldNo == 2 && l.Text == "b");
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Add && l.NewNo == 2 && l.Text == "B");
        var rows = DiffParser.SideBySide(lines.Where(l => l.Kind != DiffLineKind.Meta));
        Assert.Contains(rows, r => r.Left?.Text == "b" && r.Right?.Text == "B");
        Assert.Single(DiffParser.ChangeStarts(lines));
    }

    [Fact]
    public void ConflictParserRoundTrip()
    {
        var text = "a\n<<<<<<< HEAD\nmine\n=======\ntheirs\n>>>>>>> topic\nb\n";
        var segs = ConflictParser.Parse(text);
        Assert.Single(segs, s => s.IsConflict);
        Assert.Equal(text, string.Concat(segs.Select(s => s.Output)));
        foreach (var s in segs.Where(s => s.IsConflict)) s.Apply(ConflictResolution.Theirs);
        Assert.Equal("a\ntheirs\nb\n", string.Concat(segs.Select(s => s.Output)));
    }

    [Fact]
    public void CommitGraphLanes()
    {
        Commit C(string h, params string[] p) => new(h, h, p, "", "", DateTimeOffset.Now, [], h, "");
        var commits = new[] { C("m", "a", "b"), C("b", "a"), C("a") };
        var rows = CommitGraph.Compute(commits);
        Assert.Equal([0, 1, 0], rows.Select(r => r.Node));
    }

    private static List<string> Titles(IEnumerable<MenuEntry> e, bool top) => e.Where(x => x.TopLevel == top).Select(x => x.Title).ToList();

    [Fact]
    public void MenuPlannerLayout()
    {
        var cfg = new MenuConfig();
        var outside = MenuPlanner.Plan(new MenuContext(false, 1, true, [null], false), cfg);
        Assert.Equal(["Git クローン…", "Git ここにリポジトリを作成…"], Titles(outside, true));
        var two = MenuPlanner.Plan(new MenuContext(false, 2, false, [null, null], false), cfg);
        Assert.Equal("選択した 2 つのファイルを比較", two[0].Title);

        var rootCtx = new MenuContext(true, 1, true, [null], true, Branch: "main");
        var rootMenu = MenuPlanner.Plan(rootCtx, cfg);
        Assert.Equal(["Git 同期…", "Git コミット -> \"main\"…"], Titles(rootMenu, true));
        Assert.DoesNotContain(rootMenu, e => e.Action is GitAction.Delete or GitAction.Rename or GitAction.Blame);
        Assert.Contains(rootMenu, e => e.Action == GitAction.BisectStart);
        Assert.DoesNotContain(rootMenu, e => e.Action == GitAction.BisectGood);

        var clean = MenuPlanner.Plan(new MenuContext(true, 1, false, ["normal"], false), cfg);
        Assert.Contains(clean, e => e.Action == GitAction.Blame);
        Assert.Contains(clean, e => e.Action == GitAction.DiffPrevious);
        Assert.DoesNotContain(clean, e => e.Action is GitAction.Revert or GitAction.Add or GitAction.Diff or GitAction.Resolve);

        var mod = MenuPlanner.Plan(new MenuContext(true, 1, false, ["modified"], false), cfg);
        Assert.Contains(mod, e => e.Action == GitAction.Diff);
        Assert.Contains(mod, e => e.Action == GitAction.Revert);
        var unt = MenuPlanner.Plan(new MenuContext(true, 1, false, ["untracked"], false), cfg);
        Assert.Contains(unt, e => e.Action == GitAction.Add);
        Assert.DoesNotContain(unt, e => e.Action == GitAction.Blame);
        var con = MenuPlanner.Plan(new MenuContext(true, 1, false, ["conflict"], false), cfg);
        Assert.Contains(con, e => e.Action == GitAction.EditConflict);
        Assert.Contains(con, e => e.Action == GitAction.Resolve);
        var bis = MenuPlanner.Plan(new MenuContext(true, 1, true, [null], true, Bisecting: true, HasSubmodules: true), cfg);
        Assert.Contains(bis, e => e.Action == GitAction.BisectGood);
        Assert.Contains(bis, e => e.Action == GitAction.SubmoduleUpdate);

        var custom = new MenuConfig { TopLevel = ["log", "commit"], Hidden = ["sync", "clean"], ShowBranch = false };
        var cm = MenuPlanner.Plan(rootCtx, custom);
        Assert.Equal(["Git コミット…", "ログを表示"], Titles(cm, true));
        Assert.DoesNotContain(cm, e => e.Action is GitAction.Sync or GitAction.Clean);
        Assert.False(cm.First(e => !e.TopLevel).SeparatorBefore);
        Assert.True(rootMenu.Count(e => !e.TopLevel) > 30);
    }

    [Fact]
    public void ActionIdsRoundTrip()
    {
        foreach (var a in Enum.GetValues<GitAction>()) Assert.Equal(a, GitActionExt.Parse(a.Id()));
        Assert.Equal(GitAction.SwitchBranch, GitActionExt.Parse("switch"));
        Assert.Equal(GitAction.InitRepo, GitActionExt.Parse("init"));
    }

    [Fact]
    public void PathHelpers()
    {
        Assert.True(PathUtil.IsUnder(@"C:\a\b\c.txt", @"c:\A\b\"));
        Assert.False(PathUtil.IsUnder(@"C:\a\bc", @"C:\a\b"));
        Assert.Equal(@"C:\", PathUtil.Normalize(@"C:\"));
        var r = new Repo(@"C:\work\repo\");
        Assert.Equal("src/a.txt", r.Relative(@"c:\work\repo\src\a.txt"));
        Assert.Equal("", r.Relative(@"C:\work\repo"));
        Assert.Equal(@"C:\work\repo\src\a.txt", r.Absolute("src/a.txt"));
    }
}
