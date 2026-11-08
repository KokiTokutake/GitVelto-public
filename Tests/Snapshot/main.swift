// Off-screen snapshot harness: renders every GitGlide dialog into PNG files without showing windows.
//   build: see scripts/snapshot.sh
//   run:   snapshot <test-repos-dir> <out-dir> [filter]
import AppKit
import SwiftUI

let args = CommandLine.arguments
guard args.count >= 3 else { print("usage: snapshot <repos> <out> [filter]"); exit(2) }
let reposDir = args[1]
let outDir = args[2]
let filter = args.count > 3 ? args[3] : ""
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

let app = NSApplication.shared
app.setActivationPolicy(.prohibited)

let work = Repo(root: reposDir + "/work")
let conflict = Repo(root: reposDir + "/conflict")

func spin(_ seconds: Double) { RunLoop.main.run(until: Date().addingTimeInterval(seconds)) }

func snap<V: View>(_ name: String, _ size: NSSize, wait: Double = 1.2, _ view: V) {
    if !filter.isEmpty && !name.contains(filter) { return }
    let handle = WindowHandle()
    let host = NSHostingView(rootView: view.environmentObject(handle).environmentObject(AppSettings.shared))
    let win = NSWindow(contentRect: NSRect(origin: .zero, size: size), styleMask: [.titled, .resizable], backing: .buffered, defer: false)
    win.contentView = host
    handle.window = win
    host.frame = NSRect(origin: .zero, size: size)
    spin(wait)
    host.layoutSubtreeIfNeeded()
    spin(0.2)
    guard let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { print("FAIL \(name)"); return }
    host.cacheDisplay(in: host.bounds, to: rep)
    let png = rep.representation(using: .png, properties: [:])
    try? png?.write(to: URL(fileURLWithPath: "\(outDir)/\(name).png"))
    print("ok \(name)")
    win.contentView = nil
}

let s = NSSize(width: 820, height: 600)
snap("01_commit", NSSize(width: 780, height: 740), CommitView(repo: work, paths: [work.root]))
snap("02_log", NSSize(width: 1100, height: 760), wait: 2, LogView(repo: work, paths: []))
snap("03_status", s, StatusView(repo: work, paths: [work.root]))
snap("04_diff_unified", NSSize(width: 900, height: 500), DiffView(repo: work, source: .workingTree(path: "README.md", origPath: nil, state: .modified)))
AppSettings.shared.diffSideBySide = true
snap("05_diff_side", NSSize(width: 900, height: 500), DiffView(repo: work, source: .commit(hash: "HEAD", parent: "HEAD~1", path: "src/math.swift", oldPath: nil)))
AppSettings.shared.diffSideBySide = false
snap("06_sync", NSSize(width: 820, height: 600), wait: 2, SyncView(repo: work))
snap("07_rebase", NSSize(width: 780, height: 560), RebaseView(repo: work, initialUpstream: "v1.1"))
snap("08_refs", NSSize(width: 960, height: 520), RefBrowserView(repo: work))
snap("09_reflog", NSSize(width: 780, height: 480), ReflogView(repo: work))
snap("10_repobrowser", NSSize(width: 760, height: 480), RepoBrowserView(repo: work, path: "src"))
snap("11_blame", NSSize(width: 1000, height: 400), BlameView(repo: work, path: work.root + "/src/login.swift"))
snap("12_conflict_editor", NSSize(width: 1000, height: 700), ConflictEditorView(repo: conflict, path: conflict.root + "/src/main.swift"))
snap("13_resolve", NSSize(width: 700, height: 420), FileActionView(repo: conflict, paths: [conflict.root], mode: .resolve))
snap("14_push", NSSize(width: 540, height: 400), RemoteOpView(repo: work, mode: .push))
snap("15_pull", NSSize(width: 520, height: 360), RemoteOpView(repo: work, mode: .pull))
snap("16_switch", NSSize(width: 520, height: 360), SwitchView(repo: work))
snap("17_merge", NSSize(width: 520, height: 420), MergeView(repo: work))
snap("18_branch", NSSize(width: 520, height: 360), CreateRefView(repo: work, kind: .branch, base: nil))
snap("19_tag", NSSize(width: 520, height: 420), CreateRefView(repo: work, kind: .tag, base: nil))
snap("20_stashlist", NSSize(width: 760, height: 420), StashListView(repo: work))
snap("21_revert", NSSize(width: 700, height: 420), FileActionView(repo: work, paths: [work.root], mode: .revert))
snap("22_add", NSSize(width: 700, height: 420), FileActionView(repo: work, paths: [work.root], mode: .add))
snap("23_clean", NSSize(width: 700, height: 420), CleanView(repo: work, paths: [work.root]))
snap("24_ignore", NSSize(width: 560, height: 380), IgnoreView(repo: work, paths: [work.root + "/debug.log"]))
snap("25_export", NSSize(width: 520, height: 300), ExportView(repo: work))
snap("26_patch", NSSize(width: 520, height: 360), CreatePatchView(repo: work))
snap("27_apply", NSSize(width: 640, height: 420), ApplyPatchView(repo: work, files: []))
snap("28_remotes", NSSize(width: 640, height: 320), RemotesView(repo: work))
snap("29_reposettings", NSSize(width: 680, height: 480), RepoSettingsView(repo: work))
snap("30_settings", NSSize(width: 680, height: 620), SettingsView())
snap("31_help", NSSize(width: 640, height: 620), HelpView())
snap("32_clone", NSSize(width: 560, height: 380), CloneView(parentDir: reposDir))
snap("33_init", NSSize(width: 480, height: 240), InitRepoView(dir: reposDir))
snap("34_bisect", NSSize(width: 520, height: 300), BisectStartView(repo: work))
snap("35_submodule", NSSize(width: 540, height: 260), SubmoduleAddView(repo: work, dir: work.root))
snap("36_about", NSSize(width: 420, height: 300), AboutView())
snap("37_delete", NSSize(width: 560, height: 360), DeleteView(repo: work, paths: [work.root + "/src/math.swift"]))
snap("38_stashsave", NSSize(width: 480, height: 230), StashSaveView(repo: work))

// Progress dialog with a real command (fetch from local bare remote) + translation
if filter.isEmpty || "39_progress".contains(filter) {
    let runner = CommandRunner(dir: work.root, steps: [.git(["fetch", "--progress", "origin"]), .git(["status"]), .git(["merge", "--ff-only", "no-such-branch"])])
    runner.start()
    spin(3)
    snap("39_progress", NSSize(width: 640, height: 400), ProgressOutputView(title: "フェッチ", runner: runner, successActions: []))
}
print("done")
exit(0)
