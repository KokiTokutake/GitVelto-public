import Cocoa
import FinderSync
import os

private let log = Logger(subsystem: "com.ktoku.GitVelto", category: "FinderSync")

/// Finder Sync extension: provides the right-click "Git" menus and status overlay badges.
/// Heavy work (running git) happens in the main app; this process only renders what the app sends.
@objc(FinderSync)
final class FinderSync: FIFinderSync {
    private let lock = NSLock()
    /// NFC full path -> badge identifier
    private var badges: [String: String] = [:]
    /// NFC observed dir -> payload info
    private var dirInfo: [String: (repo: String?, repoChildren: Set<String>)] = [:]
    /// observed dir (original path string) keyed by NFC
    private var observed: [String: String] = [:]
    /// targets captured when the menu was built
    private var menuTargets: [URL] = []
    private var config = FinderSync.loadConfig()

    override init() {
        super.init()
        BadgeImages.registerAll()
        updateDirectoryURLs()

        let dnc = DistributedNotificationCenter.default()
        dnc.addObserver(self, selector: #selector(onBadges(_:)),
                        name: Notification.Name(KG.Note.badges), object: nil,
                        suspensionBehavior: .deliverImmediately)
        dnc.addObserver(self, selector: #selector(onAppReady(_:)),
                        name: Notification.Name(KG.Note.appReady), object: nil,
                        suspensionBehavior: .deliverImmediately)
        dnc.addObserver(self, selector: #selector(onMenuConfig(_:)),
                        name: Notification.Name(KG.Note.menuConfig), object: nil,
                        suspensionBehavior: .deliverImmediately)
        log.notice("GitVelto Finder extension started")
        post(KG.Note.requestConfig, "")

        let wnc = NSWorkspace.shared.notificationCenter
        wnc.addObserver(self, selector: #selector(volumesChanged(_:)), name: NSWorkspace.didMountNotification, object: nil)
        wnc.addObserver(self, selector: #selector(volumesChanged(_:)), name: NSWorkspace.didUnmountNotification, object: nil)

        launchAppIfNeeded()
    }

    // MARK: - Monitored locations

    private func updateDirectoryURLs() {
        var urls: Set<URL> = [URL(fileURLWithPath: "/")]
        let vols = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: [.skipHiddenVolumes]) ?? []
        for v in vols { urls.insert(v) }
        FIFinderSyncController.default().directoryURLs = urls
    }

    @objc private func volumesChanged(_ note: Notification) { updateDirectoryURLs() }

    private func launchAppIfNeeded() {
        if !NSRunningApplication.runningApplications(withBundleIdentifier: KG.appBundleID).isEmpty { return }
        guard let url = GitAction.start.url(paths: []) else { return }
        let cfg = NSWorkspace.OpenConfiguration()
        cfg.activates = false
        cfg.addsToRecentItems = false
        NSWorkspace.shared.open(url, configuration: cfg, completionHandler: nil)
    }

    // MARK: - Badges

    private func post(_ name: String, _ object: String) {
        DistributedNotificationCenter.default().postNotificationName(
            Notification.Name(name), object: object, userInfo: nil, deliverImmediately: true)
    }

    override func beginObservingDirectory(at url: URL) {
        let path = url.path.trimmedPath
        lock.lock(); observed[path.nfc] = path; lock.unlock()
        post(KG.Note.observe, path)
    }

    override func endObservingDirectory(at url: URL) {
        let path = url.path.trimmedPath
        let key = path.nfc
        lock.lock()
        observed.removeValue(forKey: key)
        dirInfo.removeValue(forKey: key)
        let prefix = key + "/"
        badges = badges.filter { k, _ in
            !(k.hasPrefix(prefix) && !k.dropFirst(prefix.count).contains("/"))
        }
        lock.unlock()
        post(KG.Note.unobserve, path)
    }

    override func requestBadgeIdentifier(for url: URL) {
        let key = url.path.trimmedPath.nfc
        lock.lock(); let b = badges[key]; lock.unlock()
        if let b { FIFinderSyncController.default().setBadgeIdentifier(b, for: url) }
    }

    @objc private func onBadges(_ note: Notification) {
        guard let json = note.object as? String,
              let data = json.data(using: .utf8),
              let payload = try? JSONDecoder().decode(BadgePayload.self, from: data) else { return }
        let dirKey = payload.dir.trimmedPath.nfc
        lock.lock()
        guard observed[dirKey] != nil else { lock.unlock(); return }
        var current = dirInfo[dirKey] ?? (repo: nil, repoChildren: [])
        current.repo = payload.repo
        current.repoChildren.formUnion(payload.repoChildren.map { $0.nfc })
        dirInfo[dirKey] = current
        var updates: [(URL, String)] = []
        let dirURL = URL(fileURLWithPath: payload.dir, isDirectory: true)
        for (name, badge) in payload.items {
            let url = dirURL.appendingPathComponent(name)
            let key = url.path.trimmedPath.nfc
            if badges[key] != badge {
                badges[key] = badge
                updates.append((url, badge))
            }
        }
        lock.unlock()
        guard !updates.isEmpty else { return }
        log.debug("badges for \(payload.dir, privacy: .public): \(updates.count)")
        let apply = {
            let ctl = FIFinderSyncController.default()
            for (url, badge) in updates { ctl.setBadgeIdentifier(badge, for: url) }
        }
        if Thread.isMainThread { apply() } else { DispatchQueue.main.async(execute: apply) }
    }

    private static func loadConfig() -> MenuConfig {
        guard let data = UserDefaults.standard.data(forKey: "menuConfig"),
              let c = try? JSONDecoder().decode(MenuConfig.self, from: data) else { return MenuConfig() }
        return c
    }

    @objc private func onMenuConfig(_ note: Notification) {
        guard let json = note.object as? String, let data = json.data(using: .utf8),
              let c = try? JSONDecoder().decode(MenuConfig.self, from: data) else { return }
        lock.lock(); config = c; lock.unlock()
        UserDefaults.standard.set(data, forKey: "menuConfig")
        log.notice("menu config updated: top=\(c.topLevel.joined(separator: ","), privacy: .public)")
    }

    @objc private func onAppReady(_ note: Notification) {
        log.notice("app ready; re-sending observed directories")
        post(KG.Note.requestConfig, "")
        lock.lock(); let dirs = Array(observed.values); lock.unlock()
        for d in dirs { post(KG.Note.observe, d) }
    }

    // MARK: - Toolbar

    override var toolbarItemName: String { "GitVelto" }
    override var toolbarItemToolTip: String { "GitVelto: Git 操作" }
    override var toolbarItemImage: NSImage {
        NSImage(systemSymbolName: "arrow.triangle.branch", accessibilityDescription: "GitVelto") ?? NSImage()
    }

    // MARK: - Context menu

    override func menu(for menuKind: FIMenuKind) -> NSMenu? {
        let ctl = FIFinderSyncController.default()
        var targets: [URL] = []
        switch menuKind {
        case .contextualMenuForItems:
            targets = ctl.selectedItemURLs() ?? []
        case .contextualMenuForContainer, .toolbarItemMenu, .contextualMenuForSidebar:
            if let t = ctl.targetedURL() { targets = [t] }
        @unknown default:
            if let t = ctl.targetedURL() { targets = [t] }
        }
        if targets.isEmpty, let t = ctl.targetedURL() { targets = [t] }
        guard !targets.isEmpty else { return nil }
        menuTargets = targets
        return buildMenu(targets: targets, kind: menuKind)
    }

    private func repoRoot(for url: URL) -> String? {
        let path = url.path.trimmedPath
        if let r = PathUtil.findRepoRoot(path) { return r }
        // Fallback to what the app told us (in case filesystem access is restricted).
        let dir = (path as NSString).deletingLastPathComponent
        lock.lock(); defer { lock.unlock() }
        if let info = dirInfo[dir.nfc] {
            if info.repoChildren.contains((path as NSString).lastPathComponent.nfc) { return path }
            return info.repo
        }
        if let info = dirInfo[path.nfc] { return info.repo }
        return nil
    }

    private func buildMenu(targets: [URL], kind: FIMenuKind) -> NSMenu {
        lock.lock()
        let cfg = config
        let states: [String?] = targets.map { badges[$0.path.trimmedPath.nfc] }
        lock.unlock()

        let roots = targets.map { repoRoot(for: $0) }
        let inRepo = roots.allSatisfy { $0 != nil }
        let isDirs: [Bool] = targets.map { (try? $0.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false }
        var ctx = MenuContext(inRepo: inRepo, targetCount: targets.count, anyDir: isDirs.contains(true), states: states,
                              isRoot: targets.count == 1 && roots[0] == targets[0].path.trimmedPath)
        if inRepo, let root = roots[0] {
            let gitDir = PathUtil.gitDir(repoRoot: root) ?? root + "/.git"
            ctx.bisecting = FileManager.default.fileExists(atPath: gitDir + "/BISECT_LOG")
            ctx.hasSubmodules = FileManager.default.fileExists(atPath: root + "/.gitmodules")
            ctx.branch = PathUtil.branchName(repoRoot: root)
        }
        let entries = MenuPlanner.plan(ctx, config: cfg)

        let menu = NSMenu(title: "")
        let sub = NSMenu(title: "GitVelto")
        for e in entries {
            let mi = item(e.action, title: e.title)
            if e.topLevel {
                menu.addItem(mi)
            } else {
                if e.separatorBefore { sub.addItem(.separator()) }
                sub.addItem(mi)
            }
        }
        if !sub.items.isEmpty {
            let top = NSMenuItem(title: "GitVelto", action: nil, keyEquivalent: "")
            top.image = BadgeImages.menuIcon()
            top.submenu = sub
            menu.addItem(top)
        }
        log.notice("menu built: top=\(menu.items.count) sub=\(sub.items.count) inRepo=\(inRepo)")
        return menu
    }

    private func item(_ action: GitAction, title: String? = nil) -> NSMenuItem {
        let mi = NSMenuItem(title: title ?? action.title, action: #selector(menuAction(_:)), keyEquivalent: "")
        // Finder copies the menu; only `tag` survives the round-trip reliably.
        mi.tag = GitAction.allCases.firstIndex(of: action) ?? 0
        mi.image = NSImage(systemSymbolName: action.symbol, accessibilityDescription: nil)
        return mi
    }

    @objc func menuAction(_ sender: AnyObject?) {
        guard let mi = sender as? NSMenuItem, GitAction.allCases.indices.contains(mi.tag) else { return }
        let action = GitAction.allCases[mi.tag]
        var targets = menuTargets
        if targets.isEmpty {
            let ctl = FIFinderSyncController.default()
            targets = ctl.selectedItemURLs() ?? []
            if targets.isEmpty, let t = ctl.targetedURL() { targets = [t] }
        }
        log.notice("menu action \(action.rawValue, privacy: .public) targets=\(targets.count)")
        guard let url = action.url(paths: targets.map { $0.path.trimmedPath }) else { return }
        let cfg = NSWorkspace.OpenConfiguration()
        cfg.activates = true
        cfg.addsToRecentItems = false
        NSWorkspace.shared.open(url, configuration: cfg, completionHandler: nil)
    }
}

/// Renders overlay badge bitmaps (no asset catalog needed).
enum BadgeImages {
    /// Small app-like icon for the "GitVelto" submenu.
    static func menuIcon() -> NSImage? {
        NSImage(systemSymbolName: "arrow.triangle.branch", accessibilityDescription: "GitVelto")
    }

    static func registerAll() {
        let ctl = FIFinderSyncController.default()
        for kind in BadgeKind.allCases {
            ctl.setBadgeImage(image(for: kind), label: kind.label, forBadgeIdentifier: kind.rawValue)
        }
    }

    static func color(_ kind: BadgeKind) -> NSColor {
        switch kind {
        case .normal: return NSColor(srgbRed: 0.18, green: 0.63, blue: 0.26, alpha: 1)
        case .modified: return NSColor(srgbRed: 0.84, green: 0.16, blue: 0.18, alpha: 1)
        case .added: return NSColor(srgbRed: 0.04, green: 0.41, blue: 0.85, alpha: 1)
        case .conflict: return NSColor(srgbRed: 0.93, green: 0.62, blue: 0.0, alpha: 1)
        case .untracked: return NSColor(srgbRed: 0.55, green: 0.58, blue: 0.62, alpha: 1)
        case .ignored: return NSColor(srgbRed: 0.34, green: 0.37, blue: 0.42, alpha: 1)
        }
    }

    static func symbol(_ kind: BadgeKind) -> String {
        switch kind {
        case .normal: return "checkmark"
        case .modified: return "exclamationmark"
        case .added: return "plus"
        case .conflict: return "bolt.fill"
        case .untracked: return "questionmark"
        case .ignored: return "minus"
        }
    }

    static func image(for kind: BadgeKind) -> NSImage {
        let px = 128
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px,
                                         bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                         colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return NSImage() }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        let outer = NSRect(x: 4, y: 4, width: px - 8, height: px - 8)
        NSColor.white.setFill()
        NSBezierPath(ovalIn: outer).fill()
        color(kind).setFill()
        NSBezierPath(ovalIn: outer.insetBy(dx: 9, dy: 9)).fill()
        let cfg = NSImage.SymbolConfiguration(pointSize: 58, weight: .black)
            .applying(NSImage.SymbolConfiguration(paletteColors: [.white]))
        if let sym = NSImage(systemSymbolName: symbol(kind), accessibilityDescription: nil)?.withSymbolConfiguration(cfg) {
            let s = sym.size
            let r = NSRect(x: (CGFloat(px) - s.width) / 2, y: (CGFloat(px) - s.height) / 2, width: s.width, height: s.height)
            sym.draw(in: r)
        }
        NSGraphicsContext.restoreGraphicsState()
        let img = NSImage(size: NSSize(width: px, height: px))
        img.addRepresentation(rep)
        return img
    }
}
