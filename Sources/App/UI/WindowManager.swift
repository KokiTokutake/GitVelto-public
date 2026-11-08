import AppKit
import SwiftUI
import os

private let wlog = Logger(subsystem: "com.ktoku.GitVelto", category: "Window")

/// Lets SwiftUI content close its hosting window.
extension Notification.Name {
    /// Posted (object = repository root path) after GitVelto changed a repository, so open windows can refresh.
    static let gitVeltoRepoChanged = Notification.Name("GitVeltoRepoChanged")
}

/// Notifies open windows that a repository changed.
func notifyRepoChanged(_ path: String) {
    let root = PathUtil.findRepoRoot(path) ?? path
    DispatchQueue.main.async { NotificationCenter.default.post(name: .gitVeltoRepoChanged, object: root) }
}

extension View {
    /// Runs `action` whenever GitVelto changes the given repository (from any window).
    func onRepoChanged(_ root: String, perform action: @escaping () -> Void) -> some View {
        onReceive(NotificationCenter.default.publisher(for: .gitVeltoRepoChanged)) { n in
            if (n.object as? String) == root { action() }
        }
    }
}

final class WindowHandle: ObservableObject {
    weak var window: NSWindow?
    func close() { window?.close() }
    func setTitle(_ t: String) { window?.title = t }
}

final class WindowManager: NSObject, NSWindowDelegate {
    static let shared = WindowManager()
    private var windows: Set<NSWindow> = []
    private var keyed: [String: NSWindow] = [:]

    // MARK: - Window plumbing

    @discardableResult
    func show<V: View>(_ title: String, size: NSSize, key: String? = nil, minSize: NSSize? = nil,
                       @ViewBuilder content: (WindowHandle) -> V) -> NSWindow {
        if let key, let w = keyed[key] {
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
            return w
        }
        let handle = WindowHandle()
        let root = content(handle)
            .environmentObject(handle)
            .environmentObject(AppSettings.shared)
        let hosting = NSHostingController(rootView: root)
        // Do not let SwiftUI push size constraints into the window while it is laying out:
        // content whose size changes after async loads would re-enter AppKit layout and crash AttributeGraph.
        hosting.sizingOptions = []
        let w = NSWindow(contentRect: NSRect(origin: .zero, size: size),
                         styleMask: [.titled, .closable, .miniaturizable, .resizable],
                         backing: .buffered, defer: false)
        w.contentViewController = hosting
        w.setContentSize(size)
        w.contentMinSize = minSize ?? NSSize(width: min(size.width, 420), height: min(size.height, 220))
        w.title = title
        w.isReleasedWhenClosed = false
        w.delegate = self
        w.tabbingMode = .disallowed
        handle.window = w
        if let front = windows.first(where: { $0.isVisible }) {
            w.setFrameTopLeftPoint(NSPoint(x: front.frame.minX + 28, y: front.frame.maxY - 28))
        } else {
            w.center()
        }
        windows.insert(w)
        if let key { keyed[key] = w }
        wlog.notice("window opened: \(title, privacy: .public)")
        updatePolicy()
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
        return w
    }

    func windowWillClose(_ notification: Notification) {
        guard let w = notification.object as? NSWindow else { return }
        windows.remove(w)
        for (k, v) in keyed where v === w { keyed.removeValue(forKey: k) }
        DispatchQueue.main.async { self.updatePolicy() }
    }

    private func updatePolicy() {
        let want: NSApplication.ActivationPolicy = windows.isEmpty ? .accessory : .regular
        if NSApp.activationPolicy() != want {
            NSApp.setActivationPolicy(want)
            if want == .regular { NSApp.activate(ignoringOtherApps: true) }
        }
    }

    // MARK: - Alerts

    static func alert(_ message: String, info: String = "", style: NSAlert.Style = .warning) {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.alertStyle = style
        a.messageText = message
        a.informativeText = info
        a.runModal()
    }

    static func confirm(_ message: String, info: String = "", ok: String = "OK", destructive: Bool = false) -> Bool {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.alertStyle = destructive ? .critical : .warning
        a.messageText = message
        a.informativeText = info
        let b = a.addButton(withTitle: ok)
        if destructive { b.hasDestructiveAction = true }
        a.addButton(withTitle: "キャンセル")
        return a.runModal() == .alertFirstButtonReturn
    }

    /// Simple text input dialog.
    static func prompt(_ message: String, info: String = "", defaultValue: String = "") -> String? {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = message
        a.informativeText = info
        a.addButton(withTitle: "OK")
        a.addButton(withTitle: "キャンセル")
        let f = NSTextField(frame: NSRect(x: 0, y: 0, width: 300, height: 24))
        f.stringValue = defaultValue
        a.accessoryView = f
        a.window.initialFirstResponder = f
        return a.runModal() == .alertFirstButtonReturn ? f.stringValue.trimmingCharacters(in: .whitespaces) : nil
    }

    // MARK: - Progress

    /// Runs commands in a TortoiseGit-style progress dialog.
    func runProgress(_ title: String, dir: String, steps: [ProgressStep],
                     successActions: [ProgressAction] = [], onFinish: ((Bool) -> Void)? = nil) {
        let runner = CommandRunner(dir: dir, steps: steps)
        runner.onFinish = { ok in
            StatusService.shared.invalidate(path: dir)  // also notifies open windows
            onFinish?(ok)
        }
        show("GitVelto - \(title)", size: NSSize(width: 640, height: 400), minSize: NSSize(width: 420, height: 240)) { _ in
            ProgressOutputView(title: title, runner: runner, successActions: successActions)
        }
        runner.start()
    }

    // MARK: - Dispatch

    func showSettings() {
        show("GitVelto 設定", size: NSSize(width: 680, height: 560), key: "settings") { _ in SettingsView() }
    }

    private func baseDir(_ path: String?) -> String {
        guard let path else { return NSHomeDirectory() }
        var isDir: ObjCBool = false
        if FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue { return path }
        return (path as NSString).deletingLastPathComponent
    }

    private func isDirectory(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && isDir.boolValue
    }

    func perform(_ action: GitAction, paths: [String]) {
        switch action {
        case .start:
            return
        case .settings:
            showSettings(); return
        case .about:
            show("GitVelto について", size: NSSize(width: 420, height: 300), key: "about") { _ in AboutView() }
            return
        case .help:
            show("GitVelto ヘルプ", size: NSSize(width: 640, height: 620), key: "help") { _ in HelpView() }
            return
        case .clone:
            let dir = baseDir(paths.first)
            show("GitVelto - クローン", size: NSSize(width: 560, height: 330)) { _ in CloneView(parentDir: dir) }
            return
        case .initRepo:
            let dir = baseDir(paths.first)
            show("GitVelto - リポジトリを作成", size: NSSize(width: 480, height: 240)) { _ in InitRepoView(dir: dir) }
            return
        case .terminal:
            openTerminal(baseDir(paths.first)); return
        case .diff where paths.count == 2 && !isDirectory(paths[0]) && !isDirectory(paths[1]):
            let repo = Repo.at(paths[0]) ?? Repo(root: (paths[0] as NSString).deletingLastPathComponent)
            showDiff(repo: repo, source: .files(left: paths[0], right: paths[1]))
            return
        default:
            break
        }

        guard let first = paths.first, let repo = Repo.at(first) else {
            WindowManager.alert("Git リポジトリではありません", info: paths.first ?? "")
            return
        }
        let name = repo.name
        let single = paths.count == 1 ? paths[0] : nil

        switch action {
        case .sync:
            show("GitVelto - 同期 - \(name)", size: NSSize(width: 820, height: 600), minSize: NSSize(width: 640, height: 440)) { _ in SyncView(repo: repo) }
        case .commit:
            show("GitVelto - コミット - \(name)", size: NSSize(width: 780, height: 740), minSize: NSSize(width: 600, height: 520)) { _ in
                CommitView(repo: repo, paths: paths)
            }
        case .log:
            let filter = paths.filter { $0.trimmedPath != repo.root }
            show("GitVelto - ログ - \(name)", size: NSSize(width: 1080, height: 760), minSize: NSSize(width: 700, height: 500)) { _ in
                LogView(repo: repo, paths: filter)
            }
        case .diff:
            if let p = single, !isDirectory(p) {
                let entries = repo.status(paths: [p])
                guard let e = entries.first else {
                    WindowManager.alert("変更はありません", info: repo.relative(p), style: .informational)
                    return
                }
                showDiff(repo: repo, source: .workingTree(path: e.path, origPath: e.origPath, state: e.state))
            } else {
                show("GitVelto - 変更をチェック - \(name)", size: NSSize(width: 820, height: 560)) { _ in
                    StatusView(repo: repo, paths: paths)
                }
            }
        case .status:
            show("GitVelto - 変更をチェック - \(name)", size: NSSize(width: 820, height: 560)) { _ in
                StatusView(repo: repo, paths: paths)
            }
        case .diffPrevious:
            guard let p = single else { return }
            let rel = repo.relative(p)
            let last = repo.git(["log", "-1", "--format=%H", "--", rel]).out.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !last.isEmpty else { WindowManager.alert("履歴がありません", info: rel); return }
            let hasParent = repo.git(["rev-parse", "--verify", "-q", "\(last)^"]).ok
            showDiff(repo: repo, source: hasParent ? .revToWorking(rev: "\(last)^", path: rel) : .commit(hash: last, parent: nil, path: rel, oldPath: nil))
        case .repoBrowser:
            let rel = single.map { isDirectory($0) ? repo.relative($0) : (repo.relative($0) as NSString).deletingLastPathComponent } ?? ""
            show("GitVelto - リポジトリブラウザ - \(name)", size: NSSize(width: 760, height: 560)) { _ in RepoBrowserView(repo: repo, path: rel) }
        case .reflog:
            show("GitVelto - Reflog - \(name)", size: NSSize(width: 780, height: 520)) { _ in ReflogView(repo: repo) }
        case .refBrowser:
            show("GitVelto - 参照の一覧 - \(name)", size: NSSize(width: 960, height: 560)) { _ in RefBrowserView(repo: repo) }
        case .rebase:
            show("GitVelto - リベース - \(name)", size: NSSize(width: 780, height: 560)) { _ in RebaseView(repo: repo, initialUpstream: nil) }
        case .bisectStart:
            show("GitVelto - 二分探索 - \(name)", size: NSSize(width: 520, height: 300)) { _ in BisectStartView(repo: repo) }
        case .bisectGood, .bisectBad, .bisectSkip, .bisectReset:
            let verb = [GitAction.bisectGood: "good", .bisectBad: "bad", .bisectSkip: "skip", .bisectReset: "reset"][action]!
            runProgress(action.title, dir: repo.root, steps: [.git(["bisect", verb])])
        case .editConflict:
            guard let p = single else { return }
            if !AppSettings.shared.externalMerge.isEmpty {
                ExternalTools.merge(repo: repo, path: p)
            } else {
                show("GitVelto - 競合の編集 - \((p as NSString).lastPathComponent)", size: NSSize(width: 1000, height: 760)) { _ in ConflictEditorView(repo: repo, path: p) }
            }
        case .deleteKeep:
            show("GitVelto - 削除 (ローカルに残す) - \(name)", size: NSSize(width: 560, height: 360)) { _ in DeleteView(repo: repo, paths: paths, keepLocalDefault: true) }
        case .submoduleAdd:
            let dir = baseDir(paths.first)
            show("GitVelto - サブモジュールを追加 - \(name)", size: NSSize(width: 540, height: 260)) { _ in SubmoduleAddView(repo: repo, dir: dir) }
        case .submoduleUpdate:
            show("GitVelto - サブモジュールを更新 - \(name)", size: NSSize(width: 640, height: 440)) { _ in SubmoduleUpdateView(repo: repo) }
        case .submoduleSync:
            runProgress("サブモジュールを同期", dir: repo.root, steps: [.git(["submodule", "sync", "--recursive"])])
        case .createPatch:
            show("GitVelto - パッチを作成 - \(name)", size: NSSize(width: 520, height: 360)) { _ in CreatePatchView(repo: repo) }
        case .applyPatch:
            let patches = paths.filter { ["patch", "diff"].contains(($0 as NSString).pathExtension.lowercased()) }
            show("GitVelto - パッチを適用 - \(name)", size: NSSize(width: 640, height: 460)) { _ in ApplyPatchView(repo: repo, files: patches) }
        case .export:
            show("GitVelto - エクスポート - \(name)", size: NSSize(width: 520, height: 300)) { _ in ExportView(repo: repo) }
        case .remotes:
            show("GitVelto - リモートの管理 - \(name)", size: NSSize(width: 640, height: 360)) { _ in RemotesView(repo: repo).padding(10) }
        case .repoSettings:
            show("GitVelto - リポジトリの設定 - \(name)", size: NSSize(width: 680, height: 480)) { _ in RepoSettingsView(repo: repo) }
        case .pull:
            show("GitVelto - プル - \(name)", size: NSSize(width: 520, height: 330)) { _ in RemoteOpView(repo: repo, mode: .pull) }
        case .fetch:
            show("GitVelto - フェッチ - \(name)", size: NSSize(width: 520, height: 300)) { _ in RemoteOpView(repo: repo, mode: .fetch) }
        case .push:
            show("GitVelto - プッシュ - \(name)", size: NSSize(width: 540, height: 380)) { _ in RemoteOpView(repo: repo, mode: .push) }
        case .switchBranch:
            show("GitVelto - 切り替え/チェックアウト - \(name)", size: NSSize(width: 520, height: 360)) { _ in SwitchView(repo: repo) }
        case .branch:
            show("GitVelto - ブランチを作成 - \(name)", size: NSSize(width: 520, height: 340)) { _ in CreateRefView(repo: repo, kind: .branch, base: nil) }
        case .tag:
            show("GitVelto - タグを作成 - \(name)", size: NSSize(width: 520, height: 400)) { _ in CreateRefView(repo: repo, kind: .tag, base: nil) }
        case .merge:
            show("GitVelto - マージ - \(name)", size: NSSize(width: 520, height: 420)) { _ in MergeView(repo: repo) }
        case .stashSave:
            show("GitVelto - 変更を退避 - \(name)", size: NSSize(width: 480, height: 230)) { _ in StashSaveView(repo: repo) }
        case .stashPop:
            runProgress("退避した変更を復元", dir: repo.root, steps: [.git(["stash", "pop"])])
        case .stashList:
            show("GitVelto - 退避リスト - \(name)", size: NSSize(width: 760, height: 520)) { _ in StashListView(repo: repo) }
        case .add:
            show("GitVelto - 追加 - \(name)", size: NSSize(width: 700, height: 480)) { _ in FileActionView(repo: repo, paths: paths, mode: .add) }
        case .revert:
            show("GitVelto - 元に戻す - \(name)", size: NSSize(width: 700, height: 480)) { _ in FileActionView(repo: repo, paths: paths, mode: .revert) }
        case .resolve:
            show("GitVelto - 競合を解決 - \(name)", size: NSSize(width: 700, height: 480)) { _ in FileActionView(repo: repo, paths: paths, mode: .resolve) }
        case .delete:
            show("GitVelto - 削除 - \(name)", size: NSSize(width: 560, height: 360)) { _ in DeleteView(repo: repo, paths: paths) }
        case .rename:
            guard let p = single else { return }
            show("GitVelto - 名前の変更 - \(name)", size: NSSize(width: 520, height: 180)) { _ in RenameView(repo: repo, path: p) }
        case .ignore:
            show("GitVelto - 無視リストに追加 - \(name)", size: NSSize(width: 560, height: 360)) { _ in IgnoreView(repo: repo, paths: paths) }
        case .blame:
            guard let p = single else { return }
            show("GitVelto - Blame - \((p as NSString).lastPathComponent)", size: NSSize(width: 1000, height: 720)) { _ in BlameView(repo: repo, path: p) }
        case .clean:
            show("GitVelto - クリーンアップ - \(name)", size: NSSize(width: 700, height: 520)) { _ in CleanView(repo: repo, paths: paths) }
        default:
            break
        }
    }

    func showDiff(repo: Repo, source: DiffSource) {
        show("GitVelto - 差分 - \((source.path as NSString).lastPathComponent)", size: NSSize(width: 1000, height: 720),
             minSize: NSSize(width: 500, height: 300)) { _ in
            DiffView(repo: repo, source: source)
        }
    }

    func openTerminal(_ dir: String) {
        let url = URL(fileURLWithPath: dir, isDirectory: true)
        let cfg = NSWorkspace.OpenConfiguration()
        let candidates = ["com.apple.Terminal"]
        let preferred = AppSettings.shared.terminalBundleID
        for id in ([preferred].filter { !$0.isEmpty } + candidates) {
            if let app = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) {
                NSWorkspace.shared.open([url], withApplicationAt: app, configuration: cfg, completionHandler: nil)
                return
            }
        }
    }
}
