import Foundation

/// Constants shared between the main app and the Finder Sync extension.
enum KG {
    static let appBundleID = "com.ktoku.GitVelto"
    static let extensionBundleID = "com.ktoku.GitVelto.FinderSync"
    static let urlScheme = "gitvelto"

    /// Distributed notification names used for app <-> extension IPC.
    /// The extension is sandboxed, so it only sends the `object` string (no userInfo).
    enum Note {
        /// ext -> app: object = directory path that Finder started showing
        static let observe = "com.ktoku.GitVelto.observe"
        /// ext -> app: object = directory path that Finder stopped showing
        static let unobserve = "com.ktoku.GitVelto.unobserve"
        /// app -> ext: object = JSON encoded `BadgePayload`
        static let badges = "com.ktoku.GitVelto.badges"
        /// app -> ext: app (re)started, extension should re-send observed directories
        static let appReady = "com.ktoku.GitVelto.appReady"
        /// app -> ext: object = JSON encoded `MenuConfig`
        static let menuConfig = "com.ktoku.GitVelto.menuConfig"
        /// ext -> app: please send the menu configuration
        static let requestConfig = "com.ktoku.GitVelto.requestConfig"
    }
}

/// Overlay badge kinds shown in Finder (TortoiseGit-like icon overlays).
enum BadgeKind: String, CaseIterable, Codable {
    case normal, modified, added, conflict, untracked, ignored

    var label: String {
        switch self {
        case .normal: return "変更なし"
        case .modified: return "変更あり"
        case .added: return "追加"
        case .conflict: return "競合"
        case .untracked: return "未追跡"
        case .ignored: return "無視"
        }
    }

    /// Higher wins when aggregating folder state.
    var severity: Int {
        switch self {
        case .ignored: return 0
        case .untracked: return 1
        case .normal: return 2
        case .added: return 3
        case .modified: return 4
        case .conflict: return 5
        }
    }
}

/// Badge information for the direct children of one directory.
struct BadgePayload: Codable {
    var dir: String
    /// Root of the repository that contains `dir` (nil when outside any repository).
    var repo: String?
    /// child name -> BadgeKind.rawValue ("" clears the badge)
    var items: [String: String]
    /// child names that are repository roots themselves
    var repoChildren: [String]
}

/// Commands that the Finder extension can ask the app to perform.
enum GitAction: String, CaseIterable, Codable {
    case start
    case sync, commit, log, diff, diffPrevious, status, repoBrowser, reflog, refBrowser
    case pull, fetch, push
    case switchBranch = "switch", branch, tag, merge, rebase
    case stashSave, stashPop, stashList
    case bisectStart, bisectGood, bisectBad, bisectSkip, bisectReset
    case add, revert, resolve, editConflict, delete, deleteKeep, rename, ignore, blame, clean
    case submoduleAdd, submoduleUpdate, submoduleSync
    case createPatch, applyPatch, export
    case remotes, repoSettings
    case clone, initRepo = "init"
    case terminal, settings, glossary, commandHistory, help, about

    var title: String {
        switch self {
        case .start: return "起動"
        case .sync: return "Git 同期…"
        case .commit: return "Git コミット…"
        case .log: return "ログを表示"
        case .diff: return "差分"
        case .diffPrevious: return "前のバージョンと差分"
        case .status: return "変更をチェック…"
        case .repoBrowser: return "リポジトリブラウザ"
        case .reflog: return "Reflog を表示"
        case .refBrowser: return "参照の一覧…"
        case .pull: return "プル…"
        case .fetch: return "フェッチ…"
        case .push: return "プッシュ…"
        case .switchBranch: return "切り替え/チェックアウト…"
        case .branch: return "ブランチを作成…"
        case .tag: return "タグを作成…"
        case .merge: return "マージ…"
        case .rebase: return "リベース…"
        case .stashSave: return "変更を退避 (stash)…"
        case .stashPop: return "退避した変更を復元 (stash pop)"
        case .stashList: return "退避リスト…"
        case .bisectStart: return "二分探索を開始 (bisect)…"
        case .bisectGood: return "二分探索: 良い (good)"
        case .bisectBad: return "二分探索: 悪い (bad)"
        case .bisectSkip: return "二分探索: スキップ"
        case .bisectReset: return "二分探索を終了 (reset)"
        case .add: return "追加…"
        case .revert: return "元に戻す…"
        case .resolve: return "競合を解決…"
        case .editConflict: return "競合を編集…"
        case .delete: return "削除 (git rm)…"
        case .deleteKeep: return "削除 (ローカルに残す)…"
        case .rename: return "名前の変更 (git mv)…"
        case .ignore: return "無視リストに追加…"
        case .blame: return "Blame"
        case .clean: return "クリーンアップ…"
        case .submoduleAdd: return "サブモジュールを追加…"
        case .submoduleUpdate: return "サブモジュールを更新…"
        case .submoduleSync: return "サブモジュールを同期"
        case .createPatch: return "パッチを作成…"
        case .applyPatch: return "パッチを適用…"
        case .export: return "エクスポート…"
        case .remotes: return "リモートの管理…"
        case .repoSettings: return "リポジトリの設定…"
        case .clone: return "Git クローン…"
        case .initRepo: return "Git ここにリポジトリを作成…"
        case .terminal: return "ターミナルで開く"
        case .settings: return "設定…"
        case .glossary: return "Git 用語集"
        case .commandHistory: return "実行したコマンドの履歴"
        case .help: return "ヘルプ"
        case .about: return "GitVelto について"
        }
    }

    /// SF Symbol name used for menu icons.
    var symbol: String {
        switch self {
        case .start: return "power"
        case .sync: return "arrow.triangle.2.circlepath"
        case .commit: return "checkmark.circle"
        case .log: return "clock.arrow.circlepath"
        case .diff: return "plusminus"
        case .diffPrevious: return "plusminus.circle"
        case .status: return "magnifyingglass"
        case .repoBrowser: return "folder"
        case .reflog: return "list.bullet.rectangle"
        case .refBrowser: return "list.bullet.indent"
        case .pull: return "arrow.down.circle"
        case .fetch: return "arrow.down.to.line"
        case .push: return "arrow.up.circle"
        case .switchBranch: return "arrow.triangle.branch"
        case .branch: return "plus.square.on.square"
        case .tag: return "tag"
        case .merge: return "arrow.triangle.merge"
        case .rebase: return "arrow.up.and.down.and.arrow.left.and.right"
        case .stashSave: return "tray.and.arrow.down"
        case .stashPop: return "tray.and.arrow.up"
        case .stashList: return "tray.full"
        case .bisectStart: return "scope"
        case .bisectGood: return "hand.thumbsup"
        case .bisectBad: return "hand.thumbsdown"
        case .bisectSkip: return "forward"
        case .bisectReset: return "xmark.circle"
        case .add: return "plus.circle"
        case .revert: return "arrow.uturn.backward"
        case .resolve: return "checkmark.seal"
        case .editConflict: return "exclamationmark.triangle"
        case .delete: return "trash"
        case .deleteKeep: return "trash.slash"
        case .rename: return "pencil"
        case .ignore: return "eye.slash"
        case .blame: return "person.text.rectangle"
        case .clean: return "sparkles"
        case .submoduleAdd: return "shippingbox"
        case .submoduleUpdate: return "shippingbox.circle"
        case .submoduleSync: return "arrow.clockwise.circle"
        case .createPatch: return "doc.badge.plus"
        case .applyPatch: return "doc.badge.gearshape"
        case .export: return "archivebox"
        case .remotes: return "network"
        case .repoSettings: return "slider.horizontal.3"
        case .clone: return "square.and.arrow.down.on.square"
        case .initRepo: return "folder.badge.plus"
        case .terminal: return "terminal"
        case .settings: return "gearshape"
        case .glossary: return "book"
        case .commandHistory: return "list.bullet.clipboard"
        case .help: return "questionmark.circle"
        case .about: return "info.circle"
        }
    }

    func url(paths: [String]) -> URL? {
        var comps = URLComponents()
        comps.scheme = KG.urlScheme
        comps.host = rawValue
        comps.queryItems = paths.map { URLQueryItem(name: "path", value: $0) }
        return comps.url
    }

    /// Actions that can be shown on the top level of the Finder context menu (configurable).
    static let menuConfigurable: [GitAction] = [
        .sync, .commit, .log, .diff, .diffPrevious, .status, .repoBrowser, .reflog, .refBrowser,
        .pull, .fetch, .push, .switchBranch, .branch, .tag, .merge, .rebase,
        .stashSave, .stashPop, .stashList, .bisectStart,
        .add, .revert, .resolve, .editConflict, .delete, .deleteKeep, .rename, .ignore, .blame, .clean,
        .submoduleAdd, .submoduleUpdate, .submoduleSync, .createPatch, .applyPatch, .export,
        .remotes, .repoSettings, .terminal, .glossary, .commandHistory,
    ]
}

/// Context menu layout, sent from the app to the extension.
struct MenuConfig: Codable, Equatable {
    /// Actions shown directly in the Finder context menu (TortoiseGit default: Sync + Commit).
    var topLevel: [String] = [GitAction.sync.rawValue, GitAction.commit.rawValue]
    /// Actions hidden entirely.
    var hidden: [String] = []
    /// Show the current branch in the commit item title ("Git コミット -> main…").
    var showBranch = true

    func isTop(_ a: GitAction) -> Bool { topLevel.contains(a.rawValue) }
    func isHidden(_ a: GitAction) -> Bool { hidden.contains(a.rawValue) }
}

/// Facts about the Finder selection used to decide which menu items appear.
struct MenuContext {
    var inRepo: Bool
    var targetCount: Int
    var anyDir: Bool
    /// badge identifiers of the targets (nil = unknown)
    var states: [String?]
    var isRoot: Bool
    var bisecting = false
    var hasSubmodules = false
    var branch: String? = nil
}

struct MenuEntry: Equatable {
    let action: GitAction
    let title: String
    let topLevel: Bool
    let separatorBefore: Bool
}

/// Decides the Finder context menu (TortoiseGit layout). Pure logic, shared with tests.
enum MenuPlanner {
    static func plan(_ c: MenuContext, config cfg: MenuConfig) -> [MenuEntry] {
        let unknown = c.states.contains { $0 == nil }
        func any(_ kinds: BadgeKind...) -> Bool {
            c.anyDir || unknown || c.states.contains { s in kinds.contains { $0.rawValue == s } }
        }
        let single = c.targetCount == 1
        let twoFiles = c.targetCount == 2 && !c.anyDir
        let singleFile = single && !c.anyDir
        let tracked = singleFile && c.states.first.flatMap { $0 }.map { $0 != BadgeKind.untracked.rawValue && $0 != BadgeKind.ignored.rawValue } ?? false
        let isConflict = singleFile && c.states.first.flatMap { $0 } == BadgeKind.conflict.rawValue

        guard c.inRepo else {
            var out: [MenuEntry] = []
            if twoFiles { out.append(MenuEntry(action: .diff, title: "選択した 2 つのファイルを比較", topLevel: true, separatorBefore: false)) }
            out.append(MenuEntry(action: .clone, title: GitAction.clone.title, topLevel: true, separatorBefore: false))
            out.append(MenuEntry(action: .initRepo, title: GitAction.initRepo.title, topLevel: true, separatorBefore: false))
            for a in [GitAction.terminal, .settings, .glossary, .help, .about] {
                out.append(MenuEntry(action: a, title: a.title, topLevel: false, separatorBefore: false))
            }
            return out
        }

        var plan: [GitAction?] = [.sync, .commit, nil,
            .log, .diff, .diffPrevious, .status, .repoBrowser, .refBrowser, .reflog, nil,
            .pull, .fetch, .push, nil,
            .switchBranch, .merge, .rebase, .branch, .tag, nil,
            .stashSave, .stashPop, .stashList, nil]
        plan += c.bisecting ? [.bisectGood, .bisectBad, .bisectSkip, .bisectReset, nil] : [.bisectStart, nil]
        plan += [.editConflict, .resolve, .add, .revert, .delete, .deleteKeep, .rename, .ignore, .blame, .clean, nil]
        plan += [.submoduleAdd] + (c.hasSubmodules ? [.submoduleUpdate, .submoduleSync] : []) + [nil]
        plan += [.createPatch, .applyPatch, .export, nil, .remotes, .repoSettings, .terminal, nil, .settings, .glossary, .commandHistory, .help, .about]

        func visible(_ a: GitAction) -> Bool {
            if cfg.isHidden(a) { return false }
            switch a {
            case .diff: return twoFiles || any(.modified, .added, .conflict)
            case .diffPrevious, .blame: return tracked
            case .editConflict: return isConflict
            case .resolve: return any(.conflict)
            case .add: return any(.untracked, .ignored)
            case .revert: return any(.modified, .added, .conflict)
            case .delete, .deleteKeep, .ignore: return !c.isRoot
            case .rename: return single && !c.isRoot
            default: return true
            }
        }

        var out: [MenuEntry] = []
        var pendingSep = false
        var subCount = 0
        for entry in plan {
            guard let a = entry else { pendingSep = true; continue }
            guard visible(a) else { continue }
            var title = a.title
            if a == .commit, cfg.showBranch, let b = c.branch { title = "Git コミット -> \"\(b)\"…" }
            if a == .diff, twoFiles { title = "選択した 2 つのファイルを比較" }
            if cfg.isTop(a) {
                out.append(MenuEntry(action: a, title: title, topLevel: true, separatorBefore: false))
            } else {
                out.append(MenuEntry(action: a, title: title, topLevel: false, separatorBefore: pendingSep && subCount > 0))
                pendingSep = false
                subCount += 1
            }
        }
        return out
    }
}

extension String {
    /// Unicode NFC form. Finder may hand out NFD paths while git reports NFC.
    var nfc: String { precomposedStringWithCanonicalMapping }

    /// Path without trailing slash (except root).
    var trimmedPath: String {
        var s = self
        while s.count > 1 && s.hasSuffix("/") { s.removeLast() }
        return s
    }
}

enum PathUtil {
    /// Walk up from `path` looking for a `.git` entry (directory, or file for worktrees/submodules).
    static func findRepoRoot(_ path: String) -> String? {
        let fm = FileManager.default
        var current = path.trimmedPath
        while true {
            if (current as NSString).lastPathComponent == ".git" { return nil }
            if fm.fileExists(atPath: current + "/.git") { return current }
            if current == "/" || current.isEmpty { return nil }
            let parent = (current as NSString).deletingLastPathComponent
            if parent == current { return nil }
            current = parent
        }
    }

    /// Current branch name read directly from .git/HEAD (nil if detached / unknown).
    static func branchName(repoRoot: String) -> String? {
        var gitDir = repoRoot + "/.git"
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: gitDir, isDirectory: &isDir) else { return nil }
        if !isDir.boolValue {
            // worktree / submodule: "gitdir: <path>"
            guard let s = try? String(contentsOfFile: gitDir, encoding: .utf8),
                  let line = s.split(separator: "\n").first, line.hasPrefix("gitdir: ") else { return nil }
            let p = String(line.dropFirst(8)).trimmingCharacters(in: .whitespaces)
            gitDir = p.hasPrefix("/") ? p : repoRoot + "/" + p
        }
        guard let head = try? String(contentsOfFile: gitDir + "/HEAD", encoding: .utf8) else { return nil }
        let t = head.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.hasPrefix("ref: refs/heads/") { return String(t.dropFirst(16)) }
        return nil
    }

    static func gitDir(repoRoot: String) -> String? {
        let gitDir = repoRoot + "/.git"
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: gitDir, isDirectory: &isDir) else { return nil }
        if isDir.boolValue { return gitDir }
        guard let s = try? String(contentsOfFile: gitDir, encoding: .utf8),
              let line = s.split(separator: "\n").first, line.hasPrefix("gitdir: ") else { return nil }
        let p = String(line.dropFirst(8)).trimmingCharacters(in: .whitespaces)
        return p.hasPrefix("/") ? p : repoRoot + "/" + p
    }

    static func isRepoRoot(_ path: String) -> Bool {
        FileManager.default.fileExists(atPath: path.trimmedPath + "/.git")
    }
}
