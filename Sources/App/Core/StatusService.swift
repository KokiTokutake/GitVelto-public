import Foundation
import CoreServices
import os

private let slog = Logger(subsystem: "com.ktoku.GitGlide", category: "Status")

/// Snapshot of one repository's working-tree state, used to compute Finder badges.
struct RepoSnapshot {
    let root: String
    var files: [String: BadgeKind] = [:]       // NFC rel path -> kind (tracked changes)
    var dirAggregate: [String: BadgeKind] = [:] // NFC rel dir -> worst kind beneath
    var untrackedFiles: Set<String> = []
    var ignoredFiles: Set<String> = []
    var untrackedDirs: [String] = []           // NFC rel, no trailing slash
    var ignoredDirs: [String] = []
    var overall: BadgeKind = .normal
    var valid = true

    init(root: String, entries: [StatusEntry], valid: Bool) {
        self.root = root
        self.valid = valid
        var worst = BadgeKind.normal
        for e in entries {
            let rel = e.path.nfc
            switch e.state {
            case .untracked:
                if rel.hasSuffix("/") { untrackedDirs.append(rel.trimmedPath) } else { untrackedFiles.insert(rel) }
            case .ignored:
                if rel.hasSuffix("/") { ignoredDirs.append(rel.trimmedPath) } else { ignoredFiles.insert(rel) }
            default:
                let kind: BadgeKind
                switch e.state {
                case .conflict: kind = .conflict
                case .added: kind = .added
                default: kind = .modified
                }
                files[rel] = kind
                if kind.severity > worst.severity { worst = kind == .added ? .modified : kind }
                // Aggregate onto ancestor folders
                var comps = rel.split(separator: "/").map(String.init)
                comps.removeLast()
                var acc = ""
                let folderKind: BadgeKind = kind == .conflict ? .conflict : .modified
                for c in comps {
                    acc = acc.isEmpty ? c : acc + "/" + c
                    if (dirAggregate[acc]?.severity ?? 0) < folderKind.severity { dirAggregate[acc] = folderKind }
                }
            }
        }
        overall = worst
    }

    private static func under(_ rel: String, _ dirs: [String]) -> Bool {
        dirs.contains { rel == $0 || rel.hasPrefix($0 + "/") }
    }

    func badge(rel: String, isDir: Bool) -> BadgeKind? {
        guard valid else { return nil }
        let r = rel.nfc
        if ignoredFiles.contains(r) || RepoSnapshot.under(r, ignoredDirs) { return .ignored }
        if untrackedFiles.contains(r) || RepoSnapshot.under(r, untrackedDirs) { return .untracked }
        if isDir { return dirAggregate[r] ?? .normal }
        return files[r] ?? .normal
    }
}

/// Watches a directory tree with FSEvents.
final class FSWatcher {
    private var stream: FSEventStreamRef?
    private let handler: ([String]) -> Void

    init?(path: String, queue: DispatchQueue, handler: @escaping ([String]) -> Void) {
        self.handler = handler
        var ctx = FSEventStreamContext(version: 0, info: nil, retain: nil, release: nil, copyDescription: nil)
        ctx.info = Unmanaged.passUnretained(self).toOpaque()
        let cb: FSEventStreamCallback = { _, info, count, paths, _, _ in
            guard let info else { return }
            let w = Unmanaged<FSWatcher>.fromOpaque(info).takeUnretainedValue()
            let arr = unsafeBitCast(paths, to: NSArray.self) as? [String] ?? []
            w.handler(Array(arr.prefix(count)))
        }
        let flags = UInt32(kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagNoDefer | kFSEventStreamCreateFlagWatchRoot)
        guard let s = FSEventStreamCreate(nil, cb, &ctx, [path] as CFArray,
                                          FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 0.4, flags) else { return nil }
        stream = s
        FSEventStreamSetDispatchQueue(s, queue)
        FSEventStreamStart(s)
    }

    func stop() {
        guard let s = stream else { return }
        FSEventStreamStop(s)
        FSEventStreamInvalidate(s)
        FSEventStreamRelease(s)
        stream = nil
    }

    deinit { stop() }
}

/// Badge server: answers the Finder extension's directory observations (like TGitCache).
final class StatusService {
    static let shared = StatusService()

    private let q = DispatchQueue(label: "gitglide.status")
    /// Directory scans: unbounded so one folder waiting on a privacy prompt never blocks the others.
    private let scanQ = DispatchQueue(label: "gitglide.status.scan", qos: .utility, attributes: .concurrent)
    private let workQ: OperationQueue = {
        let q = OperationQueue()
        q.maxConcurrentOperationCount = 4
        q.qualityOfService = .utility
        return q
    }()

    private var observed: Set<String> = []                // dir paths
    private var snapshots: [String: RepoSnapshot] = [:]   // root -> snapshot
    private var inFlight: Set<String> = []
    private var dirty: Set<String> = []                   // roots changed while computing
    private var watchers: [String: FSWatcher] = [:]
    private var debounce: [String: DispatchWorkItem] = [:]
    private var lastSent: [String: [String: String]] = [:]

    func start() {
        let dnc = DistributedNotificationCenter.default()
        dnc.addObserver(self, selector: #selector(onObserve(_:)), name: Notification.Name(KG.Note.observe), object: nil, suspensionBehavior: .deliverImmediately)
        dnc.addObserver(self, selector: #selector(onUnobserve(_:)), name: Notification.Name(KG.Note.unobserve), object: nil, suspensionBehavior: .deliverImmediately)
        dnc.addObserver(self, selector: #selector(onRequestConfig(_:)), name: Notification.Name(KG.Note.requestConfig), object: nil, suspensionBehavior: .deliverImmediately)
        broadcastConfig()
        dnc.postNotificationName(Notification.Name(KG.Note.appReady), object: nil, userInfo: nil, deliverImmediately: true)
    }

    @objc private func onRequestConfig(_ n: Notification) { broadcastConfig() }

    func broadcastConfig() {
        guard let data = try? JSONEncoder().encode(AppSettings.shared.menuConfig),
              let json = String(data: data, encoding: .utf8) else { return }
        DistributedNotificationCenter.default().postNotificationName(
            Notification.Name(KG.Note.menuConfig), object: json, userInfo: nil, deliverImmediately: true)
    }

    private func excluded(_ path: String) -> Bool {
        AppSettings.shared.badgeExcludes.contains { ex in
            let e = (ex as NSString).expandingTildeInPath.trimmedPath
            return !e.isEmpty && (path == e || path.hasPrefix(e + "/"))
        }
    }

    func settingsChanged() {
        q.async {
            self.lastSent.removeAll()
            for d in self.observed { self.push(dir: d) }
        }
    }

    /// Call after the app modified a repository.
    func invalidate(path: String) {
        guard let root = PathUtil.findRepoRoot(path) else { return }
        q.async { self.scheduleRefresh(root: root, delay: 0.1) }
    }

    @objc private func onObserve(_ n: Notification) {
        guard let dir = n.object as? String else { return }
        slog.notice("observe \(dir, privacy: .public)")
        q.async {
            self.observed.insert(dir)
            self.lastSent.removeValue(forKey: dir)
            self.push(dir: dir)
        }
    }

    @objc private func onUnobserve(_ n: Notification) {
        guard let dir = n.object as? String else { return }
        q.async {
            self.observed.remove(dir)
            self.lastSent.removeValue(forKey: dir)
            self.cleanupWatchers()
        }
    }

    // MARK: - On q

    private struct Child { let name: String; let isDir: Bool; let isRepo: Bool }

    private func listChildren(_ dir: String) -> [Child] {
        let url = URL(fileURLWithPath: dir, isDirectory: true)
        guard let items = try? FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: [.isDirectoryKey, .isPackageKey], options: []) else { return [] }
        return items.compactMap { u in
            let name = u.lastPathComponent
            if name == ".git" || name == ".DS_Store" { return nil }
            let isDir = (try? u.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false
            let isRepo = isDir && FileManager.default.fileExists(atPath: u.path + "/.git")
            return Child(name: name, isDir: isDir, isRepo: isRepo)
        }
    }

    /// Roots a directory's badges depend on.
    private func dependencies(of dir: String) -> (container: String?, children: [Child]) {
        let container = PathUtil.findRepoRoot(dir)
        return (container, listChildren(dir))
    }

    /// Directory scanning may block (e.g. waiting for a privacy prompt), so it runs on the work queue
    /// and only the bookkeeping happens on `q`.
    private func push(dir: String) {
        if dir.contains("/.git/") || dir.hasSuffix("/.git") { return }
        scanQ.async {
            let (container, children) = self.dependencies(of: dir)
            self.q.async { self.push(dir: dir, container: container, children: children) }
        }
    }

    private func push(dir: String, container: String?, children: [Child]) {
        guard observed.contains(dir) else { return }
        guard AppSettings.shared.badgesEnabled, !excluded(dir) else {
            send(dir: dir, repo: container, items: Dictionary(uniqueKeysWithValues: children.map { ($0.name, "") }), repoChildren: [])
            return
        }
        var items: [String: String] = [:]
        var repoChildren: [String] = []
        var missing: [String] = []

        var containerSnap: RepoSnapshot?
        if let c = container {
            if let s = snapshots[c] { containerSnap = s } else { missing.append(c) }
        }
        for ch in children {
            let full = dir + "/" + ch.name
            if ch.isRepo && full != container {
                repoChildren.append(ch.name)
                if let s = snapshots[full] { items[ch.name] = s.valid ? s.overall.rawValue : "" }
                else { missing.append(full) }
                continue
            }
            if let c = container, let s = containerSnap {
                let rel = String(full.dropFirst(c.count + 1))
                items[ch.name] = s.badge(rel: rel, isDir: ch.isDir)?.rawValue ?? ""
            }
        }
        if !items.isEmpty || !repoChildren.isEmpty || container != nil {
            send(dir: dir, repo: container, items: items, repoChildren: repoChildren)
        }
        for root in missing { compute(root: root) }
    }

    private func send(dir: String, repo: String?, items: [String: String], repoChildren: [String]) {
        let first = lastSent[dir] == nil
        var changed = items
        if let prev = lastSent[dir] { changed = items.filter { prev[$0.key] != $0.value } }
        lastSent[dir] = (lastSent[dir] ?? [:]).merging(items) { _, n in n }
        if changed.isEmpty && !first { return }
        // Chunk large folders to keep notification payloads small.
        let pairs = Array(changed)
        let chunkSize = 400
        var start = 0
        repeat {
            let slice = pairs.isEmpty ? [] : Array(pairs[start..<min(start + chunkSize, pairs.count)])
            let payload = BadgePayload(dir: dir, repo: repo, items: Dictionary(uniqueKeysWithValues: slice), repoChildren: repoChildren)
            slog.notice("send badges \(dir, privacy: .public) items=\(payload.items.count) repo=\(repo ?? "-", privacy: .public)")
            if let data = try? JSONEncoder().encode(payload), let json = String(data: data, encoding: .utf8) {
                DistributedNotificationCenter.default().postNotificationName(
                    Notification.Name(KG.Note.badges), object: json, userInfo: nil, deliverImmediately: true)
            }
            start += chunkSize
        } while start < pairs.count
    }

    private func compute(root: String) {
        if inFlight.contains(root) { dirty.insert(root); return }
        inFlight.insert(root)
        workQ.addOperation {
            let r = Git.run(["--no-optional-locks", "status", "--porcelain=v1", "-z",
                             "--untracked-files=normal", "--ignored=matching"], in: root)
            let snap = RepoSnapshot(root: root, entries: r.ok ? Repo.parseStatus(r.stdout) : [], valid: r.ok)
            if !r.ok { slog.error("status failed \(root, privacy: .public): \(r.err, privacy: .public)") }
            self.q.async {
                self.inFlight.remove(root)
                self.snapshots[root] = snap
                self.ensureWatcher(root: root)
                if self.dirty.remove(root) != nil {
                    self.compute(root: root)
                }
                for d in self.dependents(of: root) { self.push(dir: d) }
            }
        }
    }

    private func dependents(of root: String) -> [String] {
        let parent = (root as NSString).deletingLastPathComponent
        return observed.filter { d in
            d == parent || d == root || d.hasPrefix(root + "/")
        }
    }

    private func ensureWatcher(root: String) {
        guard watchers[root] == nil else { return }
        watchers[root] = FSWatcher(path: root, queue: q) { [weak self] paths in
            guard let self else { return }
            let relevant = paths.contains { p in
                !(p.contains("/.git/objects") || p.contains("/.git/logs") || p.hasSuffix(".lock") || p.contains("/.git/fsmonitor"))
            }
            if relevant { self.scheduleRefresh(root: root, delay: 0.6) }
        }
    }

    private func scheduleRefresh(root: String, delay: TimeInterval) {
        debounce[root]?.cancel()
        let item = DispatchWorkItem { [weak self] in
            guard let self else { return }
            self.debounce[root] = nil
            if self.dependents(of: root).isEmpty {
                self.snapshots.removeValue(forKey: root)
            } else {
                self.compute(root: root)
            }
        }
        debounce[root] = item
        q.asyncAfter(deadline: .now() + delay, execute: item)
    }

    private func cleanupWatchers() {
        for root in Array(watchers.keys) where dependents(of: root).isEmpty {
            watchers[root]?.stop()
            watchers.removeValue(forKey: root)
            snapshots.removeValue(forKey: root)
        }
    }
}
