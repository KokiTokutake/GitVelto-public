import Foundation
import Combine

final class AppSettings: ObservableObject {
    static let shared = AppSettings()
    private let d = UserDefaults.standard

    @Published var gitPath: String { didSet { d.set(gitPath, forKey: "gitPath") } }
    @Published var badgesEnabled: Bool { didSet { d.set(badgesEnabled, forKey: "badgesEnabled"); StatusService.shared.settingsChanged() } }
    @Published var showMenuBarIcon: Bool { didSet { d.set(showMenuBarIcon, forKey: "showMenuBarIcon"); AppDelegate.shared?.updateStatusItem() } }
    @Published var logLimit: Int { didSet { d.set(logLimit, forKey: "logLimit") } }
    @Published var diffSideBySide: Bool { didSet { d.set(diffSideBySide, forKey: "diffSideBySide") } }
    @Published var recentMessages: [String] { didSet { d.set(recentMessages, forKey: "recentMessages") } }
    @Published var pushAfterCommit: Bool { didSet { d.set(pushAfterCommit, forKey: "pushAfterCommit") } }
    @Published var cleanToTrash: Bool { didSet { d.set(cleanToTrash, forKey: "cleanToTrash") } }
    @Published var menuConfig: MenuConfig {
        didSet {
            if let data = try? JSONEncoder().encode(menuConfig) { d.set(data, forKey: "menuConfig") }
            StatusService.shared.broadcastConfig()
        }
    }
    /// External diff command. $LOCAL / $REMOTE are replaced with file paths. Empty = built-in viewer.
    @Published var externalDiff: String { didSet { d.set(externalDiff, forKey: "externalDiff") } }
    /// External merge command. $BASE / $LOCAL / $REMOTE / $MERGED. Empty = built-in conflict editor.
    @Published var externalMerge: String { didSet { d.set(externalMerge, forKey: "externalMerge") } }
    @Published var badgeExcludes: [String] { didSet { d.set(badgeExcludes, forKey: "badgeExcludes"); StatusService.shared.settingsChanged() } }
    @Published var terminalBundleID: String { didSet { d.set(terminalBundleID, forKey: "terminalBundleID") } }
    @Published var signOff: Bool { didSet { d.set(signOff, forKey: "signOff") } }
    /// Learning mode: show the git commands (with explanations) that each dialog runs.
    @Published var learningMode: Bool { didSet { d.set(learningMode, forKey: "learningMode") } }

    private init() {
        d.register(defaults: [
            "gitPath": "",
            "badgesEnabled": true,
            "showMenuBarIcon": true,
            "logLimit": 500,
            "diffSideBySide": false,
            "recentMessages": [String](),
            "pushAfterCommit": false,
            "cleanToTrash": true,
            "externalDiff": "",
            "externalMerge": "",
            "badgeExcludes": [String](),
            "terminalBundleID": "",
            "signOff": false,
            "learningMode": true,
        ])
        gitPath = d.string(forKey: "gitPath") ?? ""
        badgesEnabled = d.bool(forKey: "badgesEnabled")
        showMenuBarIcon = d.bool(forKey: "showMenuBarIcon")
        logLimit = max(50, d.integer(forKey: "logLimit"))
        diffSideBySide = d.bool(forKey: "diffSideBySide")
        recentMessages = d.stringArray(forKey: "recentMessages") ?? []
        pushAfterCommit = d.bool(forKey: "pushAfterCommit")
        cleanToTrash = d.bool(forKey: "cleanToTrash")
        if let data = d.data(forKey: "menuConfig"), let c = try? JSONDecoder().decode(MenuConfig.self, from: data) {
            menuConfig = c
        } else {
            menuConfig = MenuConfig()
        }
        externalDiff = d.string(forKey: "externalDiff") ?? ""
        externalMerge = d.string(forKey: "externalMerge") ?? ""
        badgeExcludes = d.stringArray(forKey: "badgeExcludes") ?? []
        terminalBundleID = d.string(forKey: "terminalBundleID") ?? ""
        signOff = d.bool(forKey: "signOff")
        learningMode = d.bool(forKey: "learningMode")
    }

    func remember(message: String) {
        let m = message.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !m.isEmpty else { return }
        var list = recentMessages.filter { $0 != m }
        list.insert(m, at: 0)
        recentMessages = Array(list.prefix(20))
    }
}
