import AppKit
import SwiftUI

// MARK: - Clone

struct CloneView: View {
    let parentDir: String
    @EnvironmentObject var handle: WindowHandle
    @State private var url = ""
    @State private var dir = ""
    @State private var dirEdited = false
    @State private var branch = ""
    @State private var depth = ""
    @State private var recursive = true
    @State private var origin = "origin"
    @State private var noCheckout = false
    @State private var bare = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Form {
                TextField("URL", text: $url)
                    .onChange(of: url) { if !dirEdited { dir = parentDir + "/" + Self.repoName(url) } }
                HStack {
                    TextField("クローン先", text: Binding(get: { dir }, set: { dir = $0; dirEdited = true }))
                    Button("選択…") {
                        let p = NSOpenPanel()
                        p.canChooseDirectories = true; p.canChooseFiles = false; p.canCreateDirectories = true
                        p.directoryURL = URL(fileURLWithPath: parentDir)
                        if p.runModal() == .OK, let u = p.url {
                            dir = u.path + "/" + Self.repoName(url); dirEdited = true
                        }
                    }
                }
                TextField("ブランチ (省略可)", text: $branch)
                TextField("深さ --depth (省略可)", text: $depth)
                TextField("リモート名 (-o)", text: $origin)
                Toggle("サブモジュールも取得 (--recursive)", isOn: $recursive)
                Toggle("チェックアウトしない (--no-checkout)", isOn: $noCheckout)
                Toggle("ベアリポジトリとしてクローン (--bare)", isOn: $bare)
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("クローン") { run() }.keyboardShortcut(.defaultAction).disabled(url.isEmpty || dir.isEmpty)
            }
        }
        .padding(16)
        .onAppear {
            if let s = NSPasteboard.general.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines),
               s.hasPrefix("https://") || s.hasPrefix("git@") || s.hasPrefix("ssh://"), !s.contains("\n") {
                url = s
            }
            dir = parentDir + "/" + Self.repoName(url)
        }
    }

    static func repoName(_ url: String) -> String {
        var s = url.trimmingCharacters(in: .whitespaces)
        while s.hasSuffix("/") { s.removeLast() }
        var name = s.split(whereSeparator: { $0 == "/" || $0 == ":" }).last.map(String.init) ?? ""
        if name.hasSuffix(".git") { name.removeLast(4) }
        return name
    }

    private func run() {
        var args = ["clone", "--progress"]
        if recursive { args.append("--recursive") }
        if !branch.isEmpty { args += ["-b", branch] }
        if let d = Int(depth), d > 0 { args += ["--depth", "\(d)"] }
        if !origin.isEmpty && origin != "origin" { args += ["-o", origin] }
        if noCheckout { args.append("--no-checkout") }
        if bare { args.append("--bare") }
        args += [url.trimmingCharacters(in: .whitespaces), dir]
        let target = dir
        let cwd = (dir as NSString).deletingLastPathComponent
        try? FileManager.default.createDirectory(atPath: cwd, withIntermediateDirectories: true)
        WindowManager.shared.runProgress("クローン", dir: cwd, steps: [.git(args)], successActions: [
            ProgressAction(title: "フォルダを開く") { NSWorkspace.shared.open(URL(fileURLWithPath: target)) },
            ProgressAction(title: "ログを表示") { WindowManager.shared.perform(.log, paths: [target]) },
        ])
        handle.close()
    }
}

// MARK: - Init

struct InitRepoView: View {
    let dir: String
    @EnvironmentObject var handle: WindowHandle
    @State private var bare = false
    @State private var branch = "main"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("次の場所に Git リポジトリを作成します").font(.headline)
            Text(dir).foregroundStyle(.secondary).lineLimit(2).truncationMode(.middle)
            Form {
                TextField("初期ブランチ名", text: $branch)
                Toggle("ベアリポジトリにする (--bare)", isOn: $bare)
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("作成") {
                    var args = ["init"]
                    if !branch.isEmpty { args += ["-b", branch] }
                    if bare { args.append("--bare") }
                    let r = Git.run(args, in: dir)
                    if r.ok {
                        StatusService.shared.invalidate(path: dir)
                        WindowManager.alert("リポジトリを作成しました", info: r.message, style: .informational)
                        handle.close()
                    } else {
                        WindowManager.alert("作成できません", info: r.message)
                    }
                }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(16)
        .onAppear {
            let def = Git.globalConfig("init.defaultBranch")
            if !def.isEmpty { branch = def }
        }
    }
}

// MARK: - Blame

struct BlameLine: Identifiable {
    let id: Int
    let hash: String
    let text: String
}

struct BlameCommit {
    var author = ""
    var time = Date()
    var summary = ""
    /// Path of the file in that commit (differs from the current path after renames).
    var filename = ""
}

struct BlameView: View {
    let repo: Repo
    let path: String
    var revision: String? = nil
    @State private var lines: [BlameLine] = []
    @State private var commits: [String: BlameCommit] = [:]
    @State private var selectedHash: String?
    @State private var error = ""
    @State private var loading = true

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(repo.relative(path)).fontWeight(.semibold)
                if let revision { Text("@ \(revision)").foregroundStyle(.secondary) }
                Spacer()
                if let h = selectedHash, let c = commits[h] {
                    Text("\(h.prefix(8))  \(c.author)  \(relativeDateFormatter.string(from: c.time))  \(c.summary)")
                        .lineLimit(1).foregroundStyle(.secondary)
                    Button("このコミットの変更") {
                        if let c = commits[h] {
                            let parent = repo.git(["rev-parse", "\(h)^"]).out.trimmingCharacters(in: .whitespacesAndNewlines)
                            let files = parent.isEmpty ? [] : repo.changedFiles(from: parent, to: h)
                            let repo = repo
                            WindowManager.shared.show("GitVelto - \(h.prefix(8)) \(c.summary)", size: NSSize(width: 640, height: 480)) { _ in
                                ChangedFilesView(repo: repo, files: files, from: parent, to: h)
                            }
                        }
                    }
                    Button("この行より前の Blame") {
                        let repo = repo
                        let original = commits[h]?.filename ?? ""
                        let p = original.isEmpty ? path : repo.absolute(original)
                        WindowManager.shared.show("GitVelto - Blame - \(h.prefix(8))^", size: NSSize(width: 1000, height: 720)) { _ in
                            BlameView(repo: repo, path: p, revision: "\(h)^")
                        }
                    }
                    Button("ファイルのログ") { WindowManager.shared.perform(.log, paths: [path]) }
                }
            }.padding(10)
            Divider()
            if loading {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if !error.isEmpty {
                Text(error).foregroundStyle(.red).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 0) {
                        ForEach(lines) { l in row(l) }
                    }
                    .font(.system(size: 12, design: .monospaced))
                }
                .background(Color(nsColor: .textBackgroundColor))
            }
        }
        .task { await load() }
    }

    private func row(_ l: BlameLine) -> some View {
        let c = commits[l.hash]
        let firstOfBlock = l.id == 0 || lines[l.id - 1].hash != l.hash
        let hue = Double(abs(l.hash.hashValue % 360)) / 360
        let selected = selectedHash == l.hash
        return HStack(alignment: .top, spacing: 0) {
            Text(firstOfBlock ? String(l.hash.prefix(8)) : "").frame(width: 72, alignment: .leading)
            Text(firstOfBlock ? (c?.author ?? "") : "").lineLimit(1).frame(width: 120, alignment: .leading)
            Text(firstOfBlock ? (c.map { relativeDateFormatter.string(from: $0.time) } ?? "") : "").frame(width: 130, alignment: .leading)
            Text("\(l.id + 1)").foregroundStyle(.secondary).frame(width: 44, alignment: .trailing).padding(.trailing, 8)
            Text(l.text.isEmpty ? " " : l.text).frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 6)
        .background(selected ? Color.accentColor.opacity(0.25) : Color(hue: hue, saturation: 0.25, brightness: 1).opacity(0.18))
        .contentShape(Rectangle())
        .onTapGesture { selectedHash = l.hash }
    }

    private func load() async {
        let r = await repo.git(["blame", "--porcelain"] + (revision.map { [$0] } ?? []) + ["--", repo.relative(path)])
        guard r.ok else { error = r.message; loading = false; return }
        var out: [BlameLine] = []
        var info: [String: BlameCommit] = [:]
        var current = ""
        for raw in r.out.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = String(raw)
            if line.hasPrefix("\t") {
                out.append(BlameLine(id: out.count, hash: current, text: String(line.dropFirst())))
            } else if let first = line.split(separator: " ").first, first.count == 40, first.allSatisfy({ $0.isHexDigit }) {
                current = String(first)
                if info[current] == nil { info[current] = BlameCommit() }
            } else if line.hasPrefix("author ") {
                info[current]?.author = String(line.dropFirst(7))
            } else if line.hasPrefix("author-time ") {
                info[current]?.time = Date(timeIntervalSince1970: TimeInterval(line.dropFirst(12)) ?? 0)
            } else if line.hasPrefix("summary ") {
                info[current]?.summary = String(line.dropFirst(8))
            } else if line.hasPrefix("filename ") {
                info[current]?.filename = String(line.dropFirst(9))
            }
        }
        lines = out
        commits = info
        loading = false
    }
}

// MARK: - Settings

struct SettingsView: View {
    @EnvironmentObject var settings: AppSettings
    @State private var userName = ""
    @State private var userEmail = ""
    @State private var gitVersion = ""
    @State private var extensionState = ""
    @State private var launchAtLogin = AppDelegate.launchAtLogin
    @State private var autocrlf = ""
    @State private var pullRebase = ""
    @State private var credential = ""
    @State private var defaultBranch = ""
    @State private var newExclude = ""
    @State private var tab = 0

    var body: some View {
        VStack(spacing: 0) {
            Picker("", selection: $tab) {
                Label("一般", systemImage: "gearshape").tag(0)
                Label("メニュー", systemImage: "contextualmenu.and.cursorarrow").tag(1)
                Label("オーバーレイ", systemImage: "checkmark.circle").tag(2)
                Label("Git", systemImage: "arrow.triangle.branch").tag(3)
                Label("外部ツール", systemImage: "wrench.and.screwdriver").tag(4)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .padding(10)
            Divider()
            Group {
                switch tab {
                case 1: menuLayout
                case 2: overlays
                case 3: git
                case 4: tools
                default: general
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .onAppear(perform: refresh)
    }

    private var general: some View {
        Form {
            Section("Finder 統合") {
                LabeledContent("拡張機能の状態", value: extensionState)
                HStack {
                    Button("拡張機能を有効にする") { setExtension(true) }
                    Button("システム設定を開く") {
                        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.ExtensionsPreferences")!)
                    }
                    Button("Finder を再起動") {
                        let p = Process()
                        p.executableURL = URL(fileURLWithPath: "/usr/bin/killall")
                        p.arguments = ["Finder"]
                        try? p.run()
                    }
                }
                Text("Finder で右クリック → 「Git 同期…」「Git コミット…」「GitVelto ▸」から操作できます。メニューが出ない場合は「システム設定 > 一般 > ログイン項目と機能拡張」の Finder 拡張機能で GitVelto を有効にしてください。")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("一般") {
                Toggle("メニューバーにアイコンを表示", isOn: $settings.showMenuBarIcon)
                Toggle("ログイン時に起動 (アイコンオーバーレイに必要)", isOn: $launchAtLogin)
                    .onChange(of: launchAtLogin) { AppDelegate.launchAtLogin = launchAtLogin }
                Stepper("ログの読み込み件数: \(settings.logLimit)", value: $settings.logLimit, in: 100...5000, step: 100)
                Toggle("差分を左右に並べて表示", isOn: $settings.diffSideBySide)
                Toggle("クリーンアップ時にゴミ箱へ移動", isOn: $settings.cleanToTrash)
                Picker("ターミナル", selection: $settings.terminalBundleID) {
                    Text("自動").tag("")
                    Text("ターミナル").tag("com.apple.Terminal")
                    Text("iTerm2").tag("com.googlecode.iterm2")
                    Text("Ghostty").tag("com.mitchellh.ghostty")
                    Text("Warp").tag("dev.warp.Warp-Stable")
                }
            }
        }
        .formStyle(.grouped)
    }

    private var menuLayout: some View {
        Form {
            Section {
                Toggle("「Git コミット」にブランチ名を表示", isOn: $settings.menuConfig.showBranch)
                HStack {
                    Button("TortoiseGit と同じ既定に戻す") { settings.menuConfig = MenuConfig() }
                    Text("(トップレベル: Git 同期 / Git コミット)").font(.caption).foregroundStyle(.secondary)
                }
            } header: {
                Text("各項目をトップレベル・サブメニュー・非表示から選べます。変更は次回メニューを開いたときに反映されます。")
            }
            Section("項目") {
                ForEach(GitAction.menuConfigurable, id: \.self) { a in
                    Picker(selection: placement(a)) {
                        Text("トップレベル").tag(0)
                        Text("サブメニュー").tag(1)
                        Text("非表示").tag(2)
                    } label: {
                        Label(a.title, systemImage: a.symbol)
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func placement(_ a: GitAction) -> Binding<Int> {
        Binding(get: {
            settings.menuConfig.isHidden(a) ? 2 : (settings.menuConfig.isTop(a) ? 0 : 1)
        }, set: { v in
            var c = settings.menuConfig
            c.topLevel.removeAll { $0 == a.rawValue }
            c.hidden.removeAll { $0 == a.rawValue }
            if v == 0 { c.topLevel.append(a.rawValue) }
            if v == 2 { c.hidden.append(a.rawValue) }
            settings.menuConfig = c
        })
    }

    private var overlays: some View {
        Form {
            Section("アイコンオーバーレイ") {
                Toggle("Finder にアイコンオーバーレイ (状態バッジ) を表示", isOn: $settings.badgesEnabled)
                HStack(spacing: 14) {
                    ForEach(BadgeKind.allCases, id: \.self) { k in
                        VStack {
                            Image(nsImage: BadgeLegend.image(k)).resizable().frame(width: 22, height: 22)
                            Text(k.label).font(.caption)
                        }
                    }
                }
            }
            Section("オーバーレイを表示しないフォルダ") {
                ForEach(settings.badgeExcludes, id: \.self) { p in
                    HStack {
                        Text(p)
                        Spacer()
                        Button { settings.badgeExcludes.removeAll { $0 == p } } label: { Image(systemName: "minus.circle") }.buttonStyle(.borderless)
                    }
                }
                HStack {
                    TextField("パス (例: ~/Library)", text: $newExclude)
                    Button("選択…") {
                        let p = NSOpenPanel()
                        p.canChooseDirectories = true; p.canChooseFiles = false
                        if p.runModal() == .OK, let u = p.url { newExclude = u.path }
                    }
                    Button("追加") {
                        if !newExclude.isEmpty { settings.badgeExcludes.append(newExclude); newExclude = "" }
                    }
                }
                Text("ネットワークドライブや巨大なフォルダを除外すると動作が軽くなります。").font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private var git: some View {
        Form {
            Section("Git 実行ファイル") {
                TextField("パス (空欄で自動検出)", text: $settings.gitPath)
                LabeledContent("使用中", value: Git.executable)
                LabeledContent("バージョン", value: gitVersion)
            }
            Section("ユーザー情報 (git config --global)") {
                TextField("名前 (user.name)", text: $userName)
                TextField("メール (user.email)", text: $userEmail)
            }
            Section("グローバル設定") {
                Picker("改行コード変換 (core.autocrlf)", selection: $autocrlf) {
                    Text("未設定").tag(""); Text("false").tag("false"); Text("input").tag("input"); Text("true").tag("true")
                }
                Picker("プル時の動作 (pull.rebase)", selection: $pullRebase) {
                    Text("未設定").tag(""); Text("マージ (false)").tag("false"); Text("リベース (true)").tag("true")
                }
                Picker("認証情報の保存 (credential.helper)", selection: $credential) {
                    Text("未設定").tag(""); Text("macOS キーチェーン (osxkeychain)").tag("osxkeychain"); Text("メモリに一時保存 (cache)").tag("cache")
                }
                TextField("新規リポジトリの既定ブランチ (init.defaultBranch)", text: $defaultBranch)
            }
            HStack {
                Button("保存") { saveGlobal() }.keyboardShortcut(.defaultAction)
                Button("~/.gitconfig を編集") { openInTextEditor(NSHomeDirectory() + "/.gitconfig") }
                Button("グローバル .gitignore を編集") {
                    var p = Git.globalConfig("core.excludesfile")
                    if p.isEmpty { p = NSHomeDirectory() + "/.gitignore_global"; Git.run(["config", "--global", "core.excludesfile", p], in: NSHomeDirectory()) }
                    p = (p as NSString).expandingTildeInPath
                    if !FileManager.default.fileExists(atPath: p) { FileManager.default.createFile(atPath: p, contents: Data()) }
                    openInTextEditor(p)
                }
            }
        }
        .formStyle(.grouped)
    }

    private var tools: some View {
        Form {
            Section("差分ツール ($LOCAL / $REMOTE)") {
                TextField("コマンド (空欄で内蔵ビューア)", text: $settings.externalDiff)
                Menu("プリセット") {
                    Button("Visual Studio Code") { settings.externalDiff = "code --wait --diff \"$LOCAL\" \"$REMOTE\"" }
                    Button("Cursor") { settings.externalDiff = "cursor --wait --diff \"$LOCAL\" \"$REMOTE\"" }
                    Button("FileMerge (opendiff, Xcode が必要)") { settings.externalDiff = "opendiff \"$LOCAL\" \"$REMOTE\"" }
                    Button("Kaleidoscope") { settings.externalDiff = "ksdiff \"$LOCAL\" \"$REMOTE\"" }
                    Button("Beyond Compare") { settings.externalDiff = "bcomp \"$LOCAL\" \"$REMOTE\"" }
                    Button("内蔵ビューア") { settings.externalDiff = "" }
                }.fixedSize()
            }
            Section("マージツール ($BASE / $LOCAL / $REMOTE / $MERGED)") {
                TextField("コマンド (空欄で内蔵の競合エディタ)", text: $settings.externalMerge)
                Menu("プリセット") {
                    Button("Visual Studio Code") { settings.externalMerge = "code --wait --merge \"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\"" }
                    Button("FileMerge (opendiff, Xcode が必要)") { settings.externalMerge = "opendiff \"$LOCAL\" \"$REMOTE\" -ancestor \"$BASE\" -merge \"$MERGED\"" }
                    Button("Kaleidoscope") { settings.externalMerge = "ksdiff --merge --output \"$MERGED\" --base \"$BASE\" -- \"$LOCAL\" \"$REMOTE\"" }
                    Button("内蔵の競合エディタ") { settings.externalMerge = "" }
                }.fixedSize()
            }
        }
        .formStyle(.grouped)
    }

    private func refresh() {
        userName = Git.globalConfig("user.name")
        userEmail = Git.globalConfig("user.email")
        autocrlf = Git.globalConfig("core.autocrlf")
        pullRebase = Git.globalConfig("pull.rebase")
        credential = Git.globalConfig("credential.helper")
        defaultBranch = Git.globalConfig("init.defaultBranch")
        gitVersion = Git.version()
        extensionState = Self.pluginState()
    }

    private func saveGlobal() {
        func set(_ k: String, _ v: String) {
            if v.isEmpty { Git.run(["config", "--global", "--unset", k], in: NSHomeDirectory()) }
            else { Git.run(["config", "--global", k, v], in: NSHomeDirectory()) }
        }
        set("user.name", userName); set("user.email", userEmail)
        set("core.autocrlf", autocrlf); set("pull.rebase", pullRebase)
        set("credential.helper", credential); set("init.defaultBranch", defaultBranch)
    }

    /// Runs a small system tool without spinning the run loop (see Git.run).
    static func runTool(_ path: String, _ args: [String]) -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        let done = DispatchSemaphore(value: 0)
        p.terminationHandler = { _ in done.signal() }
        do { try p.run() } catch { return "" }
        let out = String(decoding: pipe.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        done.wait()
        return out
    }

    static func pluginState() -> String {
        let t = runTool("/usr/bin/pluginkit", ["-m", "-i", KG.extensionBundleID]).trimmingCharacters(in: .whitespaces)
        if t.isEmpty { return "未登録 (アプリを /Applications に置いて起動してください)" }
        if t.hasPrefix("+") { return "有効" }
        if t.hasPrefix("-") { return "無効" }
        return "登録済み (未選択)"
    }

    private func setExtension(_ on: Bool) {
        _ = Self.runTool("/usr/bin/pluginkit", ["-e", on ? "use" : "ignore", "-i", KG.extensionBundleID])
        extensionState = Self.pluginState()
    }
}

/// Same badge drawing as the extension (for the settings legend).
enum BadgeLegend {
    static func image(_ kind: BadgeKind) -> NSImage {
        let size = NSSize(width: 44, height: 44)
        return NSImage(size: size, flipped: false) { rect in
            let colors: [BadgeKind: NSColor] = [
                .normal: NSColor(srgbRed: 0.18, green: 0.63, blue: 0.26, alpha: 1),
                .modified: NSColor(srgbRed: 0.84, green: 0.16, blue: 0.18, alpha: 1),
                .added: NSColor(srgbRed: 0.04, green: 0.41, blue: 0.85, alpha: 1),
                .conflict: NSColor(srgbRed: 0.93, green: 0.62, blue: 0.0, alpha: 1),
                .untracked: NSColor(srgbRed: 0.55, green: 0.58, blue: 0.62, alpha: 1),
                .ignored: NSColor(srgbRed: 0.34, green: 0.37, blue: 0.42, alpha: 1),
            ]
            let symbols: [BadgeKind: String] = [.normal: "checkmark", .modified: "exclamationmark", .added: "plus",
                                                .conflict: "bolt.fill", .untracked: "questionmark", .ignored: "minus"]
            NSColor.white.setFill(); NSBezierPath(ovalIn: rect.insetBy(dx: 1, dy: 1)).fill()
            colors[kind]?.setFill(); NSBezierPath(ovalIn: rect.insetBy(dx: 4, dy: 4)).fill()
            let cfg = NSImage.SymbolConfiguration(pointSize: 20, weight: .black).applying(.init(paletteColors: [.white]))
            if let s = NSImage(systemSymbolName: symbols[kind] ?? "circle", accessibilityDescription: nil)?.withSymbolConfiguration(cfg) {
                s.draw(in: NSRect(x: (rect.width - s.size.width) / 2, y: (rect.height - s.size.height) / 2, width: s.size.width, height: s.size.height))
            }
            return true
        }
    }
}

struct AboutView: View {
    var body: some View {
        VStack(spacing: 10) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 96, height: 96)
            Text("GitVelto").font(.title).bold()
            Text("バージョン \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "-")")
                .foregroundStyle(.secondary)
            Text("Finder に統合された Git クライアント").font(.callout)
            Text(Git.version()).font(.caption).foregroundStyle(.secondary)
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
