import AppKit
import SwiftUI
import ServiceManagement

final class AppDelegate: NSObject, NSApplicationDelegate {
    static weak var shared: AppDelegate?
    private var statusItem: NSStatusItem?
    private var receivedURL = false

    override init() {
        super.init()
        AppDelegate.shared = self
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMainMenu()
        updateStatusItem()
        StatusService.shared.start()
        // Restore the login item after a rename/migration when the user had it enabled.
        if UserDefaults.standard.bool(forKey: "launchAtLoginPreferred") && !AppDelegate.launchAtLogin {
            AppDelegate.launchAtLogin = true
        }
        // Launched directly by the user (not by Finder via URL) -> show the welcome/settings window.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
            if !self.receivedURL && !Self.launchedAsLoginItem {
                WindowManager.shared.showSettings()
            }
        }
    }

    private static var launchedAsLoginItem: Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        return event.eventID == kAEOpenApplication &&
            event.paramDescriptor(forKeyword: keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        receivedURL = true
        for url in urls { handle(url: url) }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { WindowManager.shared.showSettings() }
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func handle(url: URL) {
        guard url.scheme == KG.urlScheme,
              let comps = URLComponents(url: url, resolvingAgainstBaseURL: false),
              let host = comps.host,
              let action = GitAction(rawValue: host) else { return }
        let paths = (comps.queryItems ?? []).filter { $0.name == "path" }.compactMap { $0.value }
        NSLog("GitVelto: URL action %@ (%d paths)", action.rawValue, paths.count)
        WindowManager.shared.perform(action, paths: paths)
    }

    // MARK: - Menus

    private func buildMainMenu() {
        let main = NSMenu()

        let appItem = NSMenuItem()
        let appMenu = NSMenu(title: "GitVelto")
        appMenu.addItem(withTitle: "GitVelto について", action: #selector(showAbout), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "設定…", action: #selector(showSettings), keyEquivalent: ",")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "GitVelto を隠す", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "GitVelto を終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        main.addItem(appItem)

        let fileItem = NSMenuItem()
        let fileMenu = NSMenu(title: "ファイル")
        fileMenu.addItem(withTitle: "リポジトリを開く…", action: #selector(openRepository), keyEquivalent: "o")
        fileMenu.addItem(withTitle: "クローン…", action: #selector(cloneRepository), keyEquivalent: "")
        fileMenu.addItem(.separator())
        fileMenu.addItem(withTitle: "ウインドウを閉じる", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        fileItem.submenu = fileMenu
        main.addItem(fileItem)

        let editItem = NSMenuItem()
        let edit = NSMenu(title: "編集")
        edit.addItem(withTitle: "取り消す", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = edit.addItem(withTitle: "やり直す", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        edit.addItem(.separator())
        edit.addItem(withTitle: "カット", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "コピー", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "ペースト", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "すべてを選択", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit
        main.addItem(editItem)

        let helpItem = NSMenuItem()
        let help = NSMenu(title: "ヘルプ")
        help.addItem(withTitle: "GitVelto ヘルプ", action: #selector(showHelp), keyEquivalent: "?")
        help.addItem(withTitle: "Git 用語集", action: #selector(showGlossary), keyEquivalent: "")
        help.addItem(withTitle: "実行したコマンドの履歴", action: #selector(showHistory), keyEquivalent: "")
        helpItem.submenu = help

        let winItem = NSMenuItem()
        let win = NSMenu(title: "ウインドウ")
        win.addItem(withTitle: "しまう", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        winItem.submenu = win
        main.addItem(winItem)
        NSApp.windowsMenu = win
        main.addItem(helpItem)
        NSApp.helpMenu = help

        NSApp.mainMenu = main
    }

    func updateStatusItem() {
        if AppSettings.shared.showMenuBarIcon {
            guard statusItem == nil else { return }
            let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
            item.button?.image = NSImage(systemSymbolName: "arrow.triangle.branch", accessibilityDescription: "GitVelto")
            let menu = NSMenu()
            menu.addItem(withTitle: "リポジトリのログを開く…", action: #selector(openRepository), keyEquivalent: "")
            menu.addItem(withTitle: "変更をチェック…", action: #selector(openStatus), keyEquivalent: "")
            menu.addItem(withTitle: "同期…", action: #selector(openSync), keyEquivalent: "")
            menu.addItem(withTitle: "クローン…", action: #selector(cloneRepository), keyEquivalent: "")
            menu.addItem(.separator())
            menu.addItem(withTitle: "設定…", action: #selector(showSettings), keyEquivalent: ",")
            menu.addItem(withTitle: "Git 用語集", action: #selector(showGlossary), keyEquivalent: "")
            menu.addItem(withTitle: "実行したコマンドの履歴", action: #selector(showHistory), keyEquivalent: "")
            menu.addItem(withTitle: "ヘルプ", action: #selector(showHelp), keyEquivalent: "")
            menu.addItem(withTitle: "GitVelto について", action: #selector(showAbout), keyEquivalent: "")
            menu.addItem(.separator())
            menu.addItem(withTitle: "終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
            for mi in menu.items where mi.action != #selector(NSApplication.terminate(_:)) { mi.target = self }
            item.menu = menu
            statusItem = item
        } else if let item = statusItem {
            NSStatusBar.system.removeStatusItem(item)
            statusItem = nil
        }
    }

    @objc func showSettings() { WindowManager.shared.showSettings() }
    @objc func showAbout() { WindowManager.shared.perform(.about, paths: []) }
    @objc func showHelp() { WindowManager.shared.perform(.help, paths: []) }
    @objc func showGlossary() { WindowManager.shared.showGlossary() }
    @objc func showHistory() { WindowManager.shared.showCommandHistory() }
    @objc func openSync() {
        if let p = chooseFolder("同期するリポジトリを選択") { WindowManager.shared.perform(.sync, paths: [p]) }
    }

    private func chooseFolder(_ title: String) -> String? {
        NSApp.activate(ignoringOtherApps: true)
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.message = title
        return panel.runModal() == .OK ? panel.url?.path : nil
    }

    @objc func openRepository() {
        if let p = chooseFolder("ログを表示するリポジトリを選択") { WindowManager.shared.perform(.log, paths: [p]) }
    }

    @objc func openStatus() {
        if let p = chooseFolder("変更をチェックするリポジトリを選択") { WindowManager.shared.perform(.status, paths: [p]) }
    }

    @objc func cloneRepository() {
        if let p = chooseFolder("クローン先の親フォルダを選択") { WindowManager.shared.perform(.clone, paths: [p]) }
    }

    // MARK: - Login item

    static var launchAtLogin: Bool {
        get { SMAppService.mainApp.status == .enabled }
        set {
            UserDefaults.standard.set(newValue, forKey: "launchAtLoginPreferred")
            do {
                if newValue { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            } catch {
                NSLog("GitVelto: login item error \(error)")
            }
        }
    }
}

/// Credential prompt used as GIT_ASKPASS / SSH_ASKPASS.
enum AskPass {
    static func run(prompt: String) -> Never {
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        app.activate(ignoringOtherApps: true)
        let lower = prompt.lowercased()
        let secret = lower.contains("password") || lower.contains("passphrase") || lower.contains("pin") || lower.contains("token")
        let isConfirm = lower.contains("yes/no")

        let alert = NSAlert()
        alert.messageText = "GitVelto - 認証"
        alert.informativeText = prompt.isEmpty ? "入力してください" : prompt
        alert.addButton(withTitle: "OK")
        alert.addButton(withTitle: "キャンセル")
        let field: NSTextField = secret ? NSSecureTextField() : NSTextField()
        field.frame = NSRect(x: 0, y: 0, width: 320, height: 24)
        if isConfirm { field.stringValue = "yes" }
        alert.accessoryView = field
        alert.window.initialFirstResponder = field
        let resp = alert.runModal()
        if resp == .alertFirstButtonReturn {
            print(field.stringValue)
            exit(0)
        }
        exit(1)
    }
}
