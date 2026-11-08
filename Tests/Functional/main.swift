// Functional tests: run GitGlide's git command builders against real sample repositories.
//   scripts/test.sh
import Foundation
setvbuf(stdout, nil, _IONBF, 0)

var failures = 0
var passed = 0
func check(_ cond: Bool, _ name: String, _ detail: @autoclosure () -> String = "") {
    if cond { passed += 1; print("  ✓ \(name)") } else { failures += 1; print("  ✗ \(name) \(detail())") }
}
func sh(_ cmd: String, _ dir: String) -> String {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/bin/bash")
    p.arguments = ["-c", cmd]
    p.currentDirectoryURL = URL(fileURLWithPath: dir)
    let pipe = Pipe(); p.standardOutput = pipe; p.standardError = pipe
    try? p.run(); p.waitUntilExit()
    return String(decoding: pipe.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
}

let base = CommandLine.arguments[1]
let work = Repo(root: base + "/work")
let conflict = Repo(root: base + "/conflict")

print("■ status / parsers")
let st = work.status()
check(st.contains { $0.path == "README.md" && $0.state == .modified }, "README modified")
check(st.contains { $0.path == "docs/guide.md" && $0.state == .deleted }, "guide deleted")
check(st.contains { $0.path == "src/new_feature.swift" && $0.state == .added && $0.staged }, "new_feature added+staged")
check(st.contains { $0.path == "日本語メモ.txt" && $0.state == .untracked }, "Japanese file untracked (NFC path)")
let cst = conflict.status()
check(cst.filter { $0.state == .conflict }.count == 2 && cst.allSatisfy { !$0.staged }, "conflicts detected, not staged")
check(conflict.operationState() == .merging, "merge state")
let snap = RepoSnapshot(root: work.root, entries: work.status(untracked: "normal", ignored: true), valid: true)
check(snap.badge(rel: "build", isDir: true) == .ignored, "badge build/ ignored")
check(snap.badge(rel: "debug.log", isDir: false) == .ignored, "badge *.log ignored")
check(snap.badge(rel: "src", isDir: true) == .modified, "badge src/ modified (contains added)")
check(snap.badge(rel: "src/main.swift", isDir: false) == .normal, "badge clean file normal")
check(snap.badge(rel: "日本語メモ.txt".decomposedStringWithCanonicalMapping, isDir: false) == .untracked, "badge NFD lookup")
check(snap.overall == .modified, "overall modified")
let refs = work.refsDetailed()
check(refs.first { $0.name == "v1.1" }.map { $0.date.timeIntervalSince1970 > 1_000_000 && !$0.author.isEmpty } == true, "annotated tag date/author")
check(work.reflog().count >= 5, "reflog entries")
check(work.lsTree(rev: "HEAD", path: "src").map(\.name) == ["login.swift", "main.swift", "math.swift"], "ls-tree src")
check(work.remotesDetailed().first?.name == "origin", "remotes")
check(work.stashes().count == 1, "stash list")
let commits = work.log(limit: 50, all: true)
let graph = CommitGraph.compute(commits)
check(graph.count == commits.count && graph.allSatisfy { $0.node >= 0 }, "graph computed for \(commits.count) commits")
check(work.commits(range: "main..origin/main").count == 1, "incoming = 1")
check(work.commits(range: "origin/main..main").count == 2, "outgoing = 2")

print("■ conflict parser")
let text = (try? String(contentsOfFile: conflict.root + "/src/main.swift", encoding: .utf8)) ?? ""
let segs = ConflictParser.parse(text)
check(segs.filter(\.isConflict).count == 1, "1 conflict block")
check(segs.map(\.output).joined() == text, "round-trip unchanged")
var resolved = segs
for i in resolved.indices where resolved[i].isConflict { resolved[i].resolution = .theirs; resolved[i].resolved = resolved[i].theirs }
let out = resolved.map(\.output).joined()
check(!out.contains("<<<<<<<") && out.contains("topic"), "resolve theirs")

print("■ translation")
check(GitMessages.translate(line: "Already up to date.") == "すでに最新です。", "up to date")
check(GitMessages.translate(line: "fatal: not a git repository (or any of the parent directories): .git").hasPrefix("致命的エラー: Git リポジトリではありません"), "fatal not repo")
check(GitMessages.translate(line: "CONFLICT (content): Merge conflict in a.txt") == "競合 (内容): a.txt でマージ競合が発生しました", "conflict")
check(GitMessages.translate(line: "Receiving objects: 100% (3/3), done.") == "オブジェクトを受信中: 100% (3/3), done.", "progress")
check(GitMessages.translate(line: "\tmodified:   README.md") == "\t変更:     README.md", "status modified")
check(GitMessages.translate(line: "some unknown line") == "some unknown line", "unknown passthrough")
check(GitMessages.translate(line: "hint: use 'git pull' before pushing again.") == "ヒント: 再度プッシュする前に 'git pull' を実行してください。", "hint pull before push")
check(GitMessages.translate(line: "Version 71d3852 of docs/guide.md left in tree.").contains("作業ツリーに残されています"), "modify/delete left in tree")
check(GitMessages.translate(line: "  (use \"git pull\" to update your local branch)").contains("ローカルブランチを更新"), "pull hint in status")

print("■ commit (only checked files)")
let commitEntries = work.status().filter { $0.path == "README.md" || $0.path == "docs/guide.md" }
let r1 = GitOps.runSync(GitOps.commitSteps(repo: work, entries: commitEntries, options: .init(message: "README とガイドを更新")), in: work.root)
check(r1.ok, "commit ok", r1.output)
let after = work.status()
check(!after.contains { $0.path == "README.md" } && !after.contains { $0.path == "docs/guide.md" }, "committed files clean")
check(after.contains { $0.path == "src/new_feature.swift" && $0.staged }, "unchecked staged file stays staged")
check(sh("git log -1 --format=%s", work.root) == "README とガイドを更新", "commit message")
check(sh("git show --stat --format= HEAD", work.root).contains("new_feature") == false, "new_feature not in commit")

print("■ commit untracked + amend + author + new branch")
let jp = work.status().filter { $0.state == .untracked }
var o2 = GitOps.CommitOptions(message: "メモを追加")
o2.author = "Hanako <hanako@example.com>"
o2.newBranch = "topic/memo"
let r2 = GitOps.runSync(GitOps.commitSteps(repo: work, entries: jp, options: o2), in: work.root)
check(r2.ok, "commit untracked on new branch", r2.output)
check(work.currentBranch() == "topic/memo", "switched to new branch")
check(sh("git log -1 --format=%an", work.root) == "Hanako", "author override")
let r3 = GitOps.runSync(GitOps.commitSteps(repo: work, entries: [], options: .init(message: "メモを追加 (修正)", amend: true)), in: work.root)
check(r3.ok && sh("git log -1 --format=%s", work.root) == "メモを追加 (修正)", "amend message only", r3.output)
let r4 = GitOps.runSync(GitOps.commitSteps(repo: work, entries: [], options: .init(message: "空コミット")), in: work.root)
check(r4.ok && sh("git log -1 --format=%s", work.root) == "空コミット", "allow-empty commit", r4.output)

print("■ revert")
sh("printf 'x' >> src/math.swift && printf 'y' > added.txt && git add added.txt && git mv src/login.swift src/auth.swift", work.root)
let rv = work.status().filter { ["src/math.swift", "added.txt", "src/auth.swift"].contains($0.path) }
check(rv.count == 3, "prepared 3 changes", "\(rv.map(\.path))")
let r5 = GitOps.runSync(GitOps.revertSteps(repo: work, entries: rv), in: work.root)
check(r5.ok, "revert ok", r5.output)
let st5 = work.status()
check(!st5.contains { $0.path == "src/math.swift" }, "modified file restored")
check(st5.contains { $0.path == "added.txt" && $0.state == .untracked }, "added file unstaged, kept")
check(FileManager.default.fileExists(atPath: work.root + "/src/login.swift"), "renamed file restored")
sh("rm -f added.txt src/auth.swift", work.root)

print("■ interactive rebase (reorder + squash + reword + drop)")
sh("git switch -q main && git stash -u -q || true", work.root)
let rb = work.commits(range: "v1.1..HEAD", reverse: true, noMerges: true)
check(rb.count == 3, "3 commits to rebase", "\(rb.map(\.subject))")
var items = rb.map { RebaseItem(commit: $0, message: $0.subject) }
// [挨拶を日本語化, 引き算を追加, README とガイドを更新]
items[1].action = .squash
items[2].action = .reword
items[2].message = "ドキュメント更新 (改名)"
items.swapAt(0, 2) // README first
items[0].action = .reword
items[2].action = .pick
items[1].action = .squash
let step = try! GitOps.rebaseStep(repo: work, items: items, originalOrder: rb.map(\.hash), upstream: "v1.1", autostash: true)
let r6 = GitOps.runSync([step], in: work.root)
check(r6.ok, "rebase ok", r6.output)
let subjects = work.commits(range: "v1.1..HEAD").map(\.subject)
check(subjects.count == 2 && subjects.last == "ドキュメント更新 (改名)", "rebased history", "\(subjects)")
check(work.operationState() == .none, "rebase finished")
let dropItems = work.commits(range: "v1.1..HEAD", reverse: true).map { RebaseItem(commit: $0, action: .drop, message: $0.subject) }
var di = dropItems; di[0].action = .pick
let r7 = GitOps.runSync([try! GitOps.rebaseStep(repo: work, items: di, originalOrder: dropItems.map(\.id), upstream: "v1.1", autostash: true)], in: work.root)
check(r7.ok && work.commits(range: "v1.1..HEAD").count == 1, "drop commit", r7.output)
check((try? GitOps.rebaseStep(repo: work, items: [RebaseItem(commit: rb[0], action: .squash, message: "")], originalOrder: [], upstream: "v1.1", autostash: false)) == nil, "first squash rejected")

print("■ merge conflict resolution flow")
var csegs = ConflictParser.parse((try? String(contentsOfFile: conflict.root + "/src/main.swift", encoding: .utf8)) ?? "")
for i in csegs.indices where csegs[i].isConflict { csegs[i].resolution = .oursThenTheirs; csegs[i].resolved = csegs[i].ours + csegs[i].theirs }
try? csegs.map(\.output).joined().write(toFile: conflict.root + "/src/main.swift", atomically: false, encoding: .utf8)
conflict.git(["add", "src/main.swift"])
conflict.git(["checkout", "--theirs", "README.md"]); conflict.git(["add", "README.md"])
check(conflict.status().allSatisfy { $0.state != .conflict }, "all conflicts resolved")
let r8 = GitOps.runSync(GitOps.commitSteps(repo: conflict, entries: conflict.status(), options: .init(message: "Merge topic")), in: conflict.root)
check(r8.ok && conflict.operationState() == .none, "merge commit", r8.output)
check(sh("git show HEAD:README.md", conflict.root) == "README from topic", "theirs taken")

print("■ push / pull with local remote")
let r9 = GitOps.runSync([.git(["push", "--progress", "origin", "main:main"])], in: conflict.root)
check(r9.ok, "push ok", r9.output)
let r10 = GitOps.runSync([.git(["pull", "--progress", "--no-rebase", "origin", "main"])], in: base + "/other")
check(r10.ok && sh("git log -1 --format=%s", base + "/other") == "Merge topic", "pull ok", r10.output)

print("■ archive / patch")
let r11 = GitOps.runSync([.git(["archive", "--format=zip", "-o", base + "/out.zip", "HEAD"])], in: work.root)
check(r11.ok && FileManager.default.fileExists(atPath: base + "/out.zip"), "archive zip")
let r12 = GitOps.runSync([.git(["format-patch", "-1", "-o", base + "/patches", "HEAD"])], in: work.root)
check(r12.ok && ((try? FileManager.default.contentsOfDirectory(atPath: base + "/patches"))?.count ?? 0) == 1, "format-patch")

print("■ Finder context menu planner")
let cfg = MenuConfig()
func titles(_ e: [MenuEntry], top: Bool) -> [String] { e.filter { $0.topLevel == top }.map(\.title) }
let outside = MenuPlanner.plan(MenuContext(inRepo: false, targetCount: 1, anyDir: true, states: [nil], isRoot: false), config: cfg)
check(titles(outside, top: true) == ["Git クローン…", "Git ここにリポジトリを作成…"], "outside repo: clone/init top", "\(titles(outside, top: true))")
let two = MenuPlanner.plan(MenuContext(inRepo: false, targetCount: 2, anyDir: false, states: [nil, nil], isRoot: false), config: cfg)
check(two.first?.title == "選択した 2 つのファイルを比較", "two files compare")
let rootCtx = MenuContext(inRepo: true, targetCount: 1, anyDir: true, states: [nil], isRoot: true, branch: "main")
let rootMenu = MenuPlanner.plan(rootCtx, config: cfg)
check(titles(rootMenu, top: true) == ["Git 同期…", "Git コミット -> \"main\"…"], "repo root top-level = Sync + Commit -> branch", "\(titles(rootMenu, top: true))")
check(!rootMenu.contains { $0.action == .delete || $0.action == .rename || $0.action == .blame }, "root: no delete/rename/blame")
check(rootMenu.contains { $0.action == .bisectStart } && !rootMenu.contains { $0.action == .bisectGood }, "bisect start only")
let clean = MenuPlanner.plan(MenuContext(inRepo: true, targetCount: 1, anyDir: false, states: ["normal"], isRoot: false), config: cfg)
check(clean.contains { $0.action == .blame } && clean.contains { $0.action == .diffPrevious }, "clean file: blame & diff previous")
check(!clean.contains { $0.action == .revert || $0.action == .add || $0.action == .diff || $0.action == .resolve }, "clean file: no revert/add/diff/resolve")
let mod = MenuPlanner.plan(MenuContext(inRepo: true, targetCount: 1, anyDir: false, states: ["modified"], isRoot: false), config: cfg)
check(mod.contains { $0.action == .diff } && mod.contains { $0.action == .revert }, "modified file: diff & revert")
let unt = MenuPlanner.plan(MenuContext(inRepo: true, targetCount: 1, anyDir: false, states: ["untracked"], isRoot: false), config: cfg)
check(unt.contains { $0.action == .add } && !unt.contains { $0.action == .blame }, "untracked: add, no blame")
let con = MenuPlanner.plan(MenuContext(inRepo: true, targetCount: 1, anyDir: false, states: ["conflict"], isRoot: false), config: cfg)
check(con.contains { $0.action == .editConflict } && con.contains { $0.action == .resolve }, "conflict: edit conflict & resolve")
let bis = MenuPlanner.plan(MenuContext(inRepo: true, targetCount: 1, anyDir: true, states: [nil], isRoot: true, bisecting: true, hasSubmodules: true), config: cfg)
check(bis.contains { $0.action == .bisectGood } && bis.contains { $0.action == .submoduleUpdate }, "bisecting + submodules")
var custom = MenuConfig(); custom.topLevel = ["log", "commit"]; custom.hidden = ["sync", "clean"]; custom.showBranch = false
let cm = MenuPlanner.plan(rootCtx, config: custom)
check(titles(cm, top: true) == ["Git コミット…", "ログを表示"], "custom top-level", "\(titles(cm, top: true))")
check(!cm.contains { $0.action == .sync || $0.action == .clean }, "hidden items removed")
check(!(cm.first { !$0.topLevel }?.separatorBefore ?? true), "no leading separator in submenu")
check(rootMenu.filter { !$0.topLevel }.count > 30, "submenu has full TortoiseGit set (\(rootMenu.filter { !$0.topLevel }.count) items)")

print("")
print("結果: \(passed) 件成功, \(failures) 件失敗")
exit(failures == 0 ? 0 : 1)
