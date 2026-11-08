import AppKit
import SwiftUI

// MARK: - Bisect

struct BisectStartView: View {
    let repo: Repo
    var good: String = ""
    var bad: String = "HEAD"
    @EnvironmentObject var handle: WindowHandle
    @State private var goodRef = ""
    @State private var badRef = ""
    @State private var refs: [RefInfo] = []

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            HStack { Text("不具合が入り込んだコミットを二分探索で特定します。").font(.headline); TermHelp("bisect") }
            Form {
                HStack {
                    TextField("良い (問題のない) コミット", text: $goodRef)
                    refMenu { goodRef = $0 }
                }
                HStack {
                    TextField("悪い (問題のある) コミット", text: $badRef)
                    refMenu { badRef = $0 }
                }
            }
            Text("開始後は、チェックアウトされたコミットを確認し、Finder の右クリックメニュー「二分探索: 良い / 悪い」で判定を進めます。")
                .font(.caption).foregroundStyle(.secondary)
            Spacer()
            CommandPreview(commands: [["bisect", "start", badRef, goodRef], ["bisect", "good"], ["bisect", "bad"], ["bisect", "reset"]],
                           note: "1 行目を実行した後、確認結果に応じて good / bad を繰り返し、最後に reset で終了します。")
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("開始") {
                    WindowManager.shared.runProgress("二分探索を開始", dir: repo.root, steps: [.git(["bisect", "start", badRef, goodRef])])
                    handle.close()
                }.keyboardShortcut(.defaultAction).disabled(goodRef.isEmpty || badRef.isEmpty)
            }
        }
        .padding(16)
        .onAppear {
            refs = repo.refs()
            goodRef = good.isEmpty ? (refs.first { $0.kind == .tag }?.name ?? "") : good
            badRef = bad
        }
    }

    private func refMenu(_ set: @escaping (String) -> Void) -> some View {
        Menu("") {
            ForEach(refs) { r in Button(r.name) { set(r.name) } }
        }.menuStyle(.borderlessButton).frame(width: 24)
    }
}

// MARK: - Export

struct ExportView: View {
    let repo: Repo
    var revision: String = "HEAD"
    @EnvironmentObject var handle: WindowHandle
    @State private var rev = ""
    @State private var format = "zip"
    @State private var prefix = true
    @State private var output = ""

    private var exportArgs: [String] {
        var args = ["archive", "--format=\(format)", "-o", output]
        if prefix { args.append("--prefix=\(repo.name)/") }
        return args + [rev]
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            Form {
                TextField("リビジョン", text: $rev)
                Picker("形式", selection: $format) {
                    Text("ZIP").tag("zip")
                    Text("tar.gz").tag("tar.gz")
                    Text("tar").tag("tar")
                }.pickerStyle(.segmented)
                Toggle("フォルダ名 (\(repo.name)/) を付ける", isOn: $prefix)
                CommandPreview(commands: [exportArgs])
                HStack {
                    TextField("出力先", text: $output)
                    Button("選択…") {
                        let p = NSSavePanel()
                        p.nameFieldStringValue = "\(repo.name)-\(rev.replacingOccurrences(of: "/", with: "-")).\(format)"
                        if p.runModal() == .OK, let u = p.url { output = u.path }
                    }
                }
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("エクスポート") {
                    let args = exportArgs
                    let out = output
                    WindowManager.shared.runProgress("エクスポート", dir: repo.root, steps: [.git(args)],
                                                     successActions: [ProgressAction(title: "Finder で表示") { revealInFinder([out]) }])
                    handle.close()
                }.keyboardShortcut(.defaultAction).disabled(output.isEmpty || rev.isEmpty)
            }
        }
        .padding(16)
        .onAppear {
            rev = revision
            let downloads = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first?.path ?? NSHomeDirectory()
            output = "\(downloads)/\(repo.name)-\(revision.replacingOccurrences(of: "/", with: "-")).zip"
        }
        .onChange(of: format) {
            let base = (output as NSString).deletingPathExtension.replacingOccurrences(of: ".tar", with: "")
            output = base + "." + format
        }
    }
}

// MARK: - Patches

struct CreatePatchView: View {
    let repo: Repo
    var commits: [String] = []
    @EnvironmentObject var handle: WindowHandle
    @State private var mode = 0 // 0 last N, 1 since ref, 2 selected commits, 3 working tree diff
    @State private var count = 1
    @State private var since = ""
    @State private var outDir = ""
    @State private var single = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            Form {
                HStack { Text("パッチ:").foregroundStyle(.secondary); TermHelp("patch") }
                Picker("対象", selection: $mode) {
                    Text("直近のコミット").tag(0)
                    Text("指定の参照以降のコミット").tag(1)
                    if !commits.isEmpty { Text("ログで選択したコミット (\(commits.count))").tag(2) }
                    Text("コミットされていない変更 (git diff)").tag(3)
                }.pickerStyle(.radioGroup)
                if mode == 0 { Stepper("コミット数: \(count)", value: $count, in: 1...500) }
                if mode == 1 { TextField("参照 (例: origin/main)", text: $since) }
                if mode != 3 { Toggle("1 つのファイルにまとめる", isOn: $single) }
                HStack {
                    TextField("保存先フォルダ", text: $outDir)
                    Button("選択…") {
                        let p = NSOpenPanel()
                        p.canChooseDirectories = true; p.canChooseFiles = false; p.canCreateDirectories = true
                        if p.runModal() == .OK, let u = p.url { outDir = u.path }
                    }
                }
            }
            Spacer()
            CommandPreview(commands: patchPreview)
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("作成") { run() }.keyboardShortcut(.defaultAction).disabled(outDir.isEmpty)
            }
        }
        .padding(16)
        .onAppear {
            if !commits.isEmpty { mode = 2 }
            outDir = FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask).first?.path ?? NSHomeDirectory()
        }
    }

    private var patchPreview: [[String]] {
        switch mode {
        case 3: return [["diff", "HEAD", "--binary"]]
        case 2: return commits.reversed().map { ["format-patch", "-1", String($0.prefix(8)), "-o", outDir] }
        default:
            let range = mode == 0 ? ["-\(count)", "HEAD"] : ["\(since.isEmpty ? "<参照>" : since)..HEAD"]
            return [single ? ["format-patch", "--stdout"] + range : ["format-patch", "-o", outDir] + range]
        }
    }

    private func run() {
        let repo = repo, out = outDir
        var steps: [ProgressStep] = []
        let stamp = Int(Date().timeIntervalSince1970)
        func toFile(_ args: [String], _ name: String) -> ProgressStep {
            .custom("git " + args.joined(separator: " ") + " > \(name)") {
                let r = repo.git(args)
                guard r.ok else { throw GitError(message: r.message) }
                try r.stdout.write(to: URL(fileURLWithPath: out + "/" + name))
                return "保存しました: \(out)/\(name)"
            }
        }
        switch mode {
        case 3:
            steps.append(toFile(["diff", "HEAD", "--binary"], "\(repo.name)-\(stamp).diff"))
        case 2:
            if single {
                steps.append(.custom("パッチを作成 (\(commits.count) コミット)") {
                    var data = Data()
                    for h in commits.reversed() {
                        let r = repo.git(["format-patch", "-1", "--stdout", h])
                        guard r.ok else { throw GitError(message: r.message) }
                        data.append(r.stdout)
                    }
                    try data.write(to: URL(fileURLWithPath: out + "/\(repo.name)-\(stamp).patch"))
                    return "保存しました: \(out)/\(repo.name)-\(stamp).patch"
                })
            } else {
                for (i, h) in commits.reversed().enumerated() {
                    steps.append(.git(["format-patch", "-1", h, "-o", out, "--start-number=\(i + 1)"]))
                }
            }
        default:
            let range = mode == 0 ? ["-\(count)", "HEAD"] : ["\(since)..HEAD"]
            if single {
                steps.append(toFile(["format-patch", "--stdout"] + range, "\(repo.name)-\(stamp).patch"))
            } else {
                steps.append(.git(["format-patch", "-o", out] + range))
            }
        }
        WindowManager.shared.runProgress("パッチを作成", dir: repo.root, steps: steps,
                                         successActions: [ProgressAction(title: "Finder で表示") { NSWorkspace.shared.open(URL(fileURLWithPath: out)) }])
        handle.close()
    }
}

struct ApplyPatchView: View {
    let repo: Repo
    @State var files: [String]
    @EnvironmentObject var handle: WindowHandle
    @State private var useAm = true
    @State private var threeWay = true
    @State private var signOff = false
    @State private var check = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            HStack {
                Text("パッチファイル").font(.headline)
                Spacer()
                Button("追加…") {
                    let p = NSOpenPanel()
                    p.allowsMultipleSelection = true
                    p.allowedContentTypes = []
                    if p.runModal() == .OK { files += p.urls.map(\.path) }
                }
                Button("クリア") { files = [] }
            }
            List(files, id: \.self) { f in
                HStack {
                    Image(nsImage: FileIcons.icon(for: f)).resizable().frame(width: 16, height: 16)
                    Text((f as NSString).lastPathComponent)
                    Spacer()
                    Text((f as NSString).deletingLastPathComponent).foregroundStyle(.secondary).lineLimit(1).truncationMode(.head)
                }
            }
            Picker("適用方法", selection: $useAm) {
                Text("コミットとして適用 (git am) — format-patch で作成したパッチ").tag(true)
                Text("作業ツリーに適用 (git apply) — 差分のみ").tag(false)
            }.pickerStyle(.radioGroup)
            CommandPreview(commands: files.isEmpty ? [] : [applyArgs + files.map { ($0 as NSString).lastPathComponent }])
            HStack {
                TermHelp("patch")
                Toggle("3-way マージ (-3)", isOn: $threeWay)
                if useAm { Toggle("Signed-off-by を追加", isOn: $signOff) }
                if !useAm { Toggle("確認のみ (--check)", isOn: $check) }
            }
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("適用") { run() }.keyboardShortcut(.defaultAction).disabled(files.isEmpty)
            }
        }
        .padding(14)
    }

    private var applyArgs: [String] {
        var args: [String]
        if useAm {
            args = ["am"]
            if threeWay { args.append("-3") }
            if signOff { args.append("--signoff") }
        } else {
            args = ["apply", "-v"]
            if threeWay { args.append("--3way") }
            if check { args.append("--check") }
        }
        return args
    }

    private func run() {
        let args = applyArgs
        let root = repo.root
        WindowManager.shared.runProgress("パッチを適用", dir: root, steps: [.git(args + files)]) { ok in
            if !ok, Repo(root: root).operationState() == .applyingPatches {
                DispatchQueue.main.async {
                    if WindowManager.confirm("パッチの適用で競合が発生しました", info: "競合の解決画面を開きますか? (続行/中止もそこから行えます)", ok: "開く") {
                        WindowManager.shared.perform(.resolve, paths: [root])
                    }
                }
            }
        }
        handle.close()
    }
}

// MARK: - Submodules

struct SubmoduleAddView: View {
    let repo: Repo
    let dir: String
    @EnvironmentObject var handle: WindowHandle
    @State private var url = ""
    @State private var path = ""
    @State private var branch = ""
    @State private var edited = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            Form {
                HStack { Text("サブモジュール:").foregroundStyle(.secondary); TermHelp("submodule") }
                TextField("リポジトリ URL", text: $url).onChange(of: url) {
                    if !edited {
                        let rel = repo.relative(dir)
                        let name = CloneView.repoName(url)
                        path = rel.isEmpty ? name : rel + "/" + name
                    }
                }
                TextField("パス", text: Binding(get: { path }, set: { path = $0; edited = true }))
                TextField("ブランチ (省略可)", text: $branch)
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("追加") {
                    let u = url.trimmingCharacters(in: .whitespaces)
                    let local = u.hasPrefix("/") || u.hasPrefix("file://") || u.hasPrefix(".") || u.hasPrefix("~")
                    // Recent git refuses local-path submodules unless the file transport is allowed explicitly.
                    var args = (local ? ["-c", "protocol.file.allow=always"] : []) + ["submodule", "add", "--progress"]
                    if !branch.isEmpty { args += ["-b", branch] }
                    args += ["--", (u as NSString).expandingTildeInPath, path]
                    WindowManager.shared.runProgress("サブモジュールを追加", dir: repo.root, steps: [.git(args)])
                    handle.close()
                }.keyboardShortcut(.defaultAction).disabled(url.isEmpty || path.isEmpty)
            }
        }
        .padding(16)
    }
}

struct SubmoduleUpdateView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State private var subs: [SubmoduleInfo] = []
    @State private var checked: Set<String> = []
    @State private var initialize = true
    @State private var recursive = true
    @State private var remote = false
    @State private var force = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            HStack { Text("更新するサブモジュール").font(.headline); TermHelp("submodule") }
            List(subs) { s in
                Toggle(isOn: Binding(get: { checked.contains(s.path) }, set: { v in if v { checked.insert(s.path) } else { checked.remove(s.path) } })) {
                    HStack {
                        Text(s.path)
                        Spacer()
                        Text(s.stateLabel).foregroundStyle(s.state == " " ? Color.secondary : Color.orange)
                        Text(String(s.hash.prefix(8))).font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary)
                    }
                }.toggleStyle(.checkbox)
            }
            HStack {
                Toggle("初期化 (--init)", isOn: $initialize)
                Toggle("再帰的 (--recursive)", isOn: $recursive)
                Toggle("リモートの最新にする (--remote)", isOn: $remote)
                Toggle("強制 (--force)", isOn: $force)
            }
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("更新") {
                    var args = ["-c", "protocol.file.allow=always", "submodule", "update", "--progress"]
                    if initialize { args.append("--init") }
                    if recursive { args.append("--recursive") }
                    if remote { args.append("--remote") }
                    if force { args.append("--force") }
                    args.append("--")
                    args += subs.filter { checked.contains($0.path) }.map(\.path)
                    WindowManager.shared.runProgress("サブモジュールを更新", dir: repo.root, steps: [.git(args)])
                    handle.close()
                }.keyboardShortcut(.defaultAction).disabled(checked.isEmpty)
            }
        }
        .padding(14)
        .onAppear {
            subs = repo.submodules()
            checked = Set(subs.map(\.path))
        }
    }
}

// MARK: - Remotes / repository settings

struct RemotesView: View {
    let repo: Repo
    @State private var remotes: [RemoteInfo] = []
    @State private var selection: String?
    @State private var name = ""
    @State private var url = ""
    @State private var pushURL = ""
    @State private var isNew = false

    var body: some View {
        HSplitView {
            VStack(alignment: .leading) {
                List(remotes, selection: $selection) { r in
                    VStack(alignment: .leading) {
                        Text(r.name).fontWeight(.semibold)
                        Text(r.fetchURL).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                    }.tag(r.name)
                }
                HStack {
                    Button { isNew = true; selection = nil; name = remotes.isEmpty ? "origin" : ""; url = ""; pushURL = "" } label: { Image(systemName: "plus") }
                    Button { remove() } label: { Image(systemName: "minus") }.disabled(selection == nil)
                }
            }
            .frame(minWidth: 160, idealWidth: 190, maxWidth: 260)
            VStack(alignment: .leading, spacing: 10) {
                HStack { Text(isNew ? "新しいリモート" : "リモートの編集").font(.headline); TermHelp("remote"); TermHelp("origin") }
                Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 8) {
                    GridRow {
                        Text("名前").gridColumnAlignment(.trailing)
                        TextField("origin", text: $name).textFieldStyle(.roundedBorder)
                    }
                    GridRow {
                        Text("URL")
                        TextField("https://… または git@…", text: $url).textFieldStyle(.roundedBorder)
                    }
                    GridRow {
                        Text("プッシュ URL")
                        TextField("省略時は URL と同じ", text: $pushURL).textFieldStyle(.roundedBorder)
                    }
                }
                HStack {
                    Button(isNew ? "追加" : "保存") { save() }.keyboardShortcut(.defaultAction).disabled(name.isEmpty || url.isEmpty)
                    if !isNew, let s = selection {
                        Button("フェッチ") { WindowManager.shared.runProgress("フェッチ", dir: repo.root, steps: [.git(["fetch", "--progress", s])]) }
                        Button("整理 (prune)") { WindowManager.shared.runProgress("リモートブランチの整理", dir: repo.root, steps: [.git(["remote", "prune", s])]) }
                    }
                }
                Spacer()
            }
            .padding(12)
            .frame(minWidth: 340, maxWidth: .infinity)
        }
        .onAppear(perform: load)
        .onChange(of: selection) {
            guard let s = selection, let r = remotes.first(where: { $0.name == s }) else { return }
            isNew = false
            name = r.name; url = r.fetchURL; pushURL = r.pushURL == r.fetchURL ? "" : r.pushURL
        }
    }

    private func load() {
        remotes = repo.remotesDetailed()
        if selection == nil { selection = remotes.first?.name }
        if remotes.isEmpty { isNew = true; name = "origin" }
    }

    private func check(_ r: GitResult) -> Bool {
        if !r.ok { WindowManager.alert("失敗しました", info: r.message) }
        return r.ok
    }

    private func save() {
        if isNew {
            guard check(repo.git(["remote", "add", name, url])) else { return }
        } else if let old = selection {
            if old != name { guard check(repo.git(["remote", "rename", old, name])) else { return } }
            guard check(repo.git(["remote", "set-url", name, url])) else { return }
        }
        if !pushURL.isEmpty {
            _ = check(repo.git(["remote", "set-url", "--push", name, pushURL]))
        } else if !isNew {
            repo.git(["config", "--unset-all", "remote.\(name).pushurl"])
        }
        isNew = false
        selection = name
        load()
    }

    private func remove() {
        guard let s = selection, WindowManager.confirm("リモート \(s) を削除しますか?", info: "リモート追跡ブランチも削除されます。", ok: "削除", destructive: true) else { return }
        _ = check(repo.git(["remote", "remove", s]))
        selection = nil
        load()
    }
}

struct RepoSettingsView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State private var userName = ""
    @State private var userEmail = ""
    @State private var autocrlf = ""
    @State private var pullRebase = ""
    @State private var ignoreCase = ""
    @State private var hooksPath = ""
    @State private var tab = 0

    var body: some View {
        VStack(spacing: 0) {
            Picker("", selection: $tab) {
                Label("リモート", systemImage: "network").tag(0)
                Label("Git", systemImage: "slider.horizontal.3").tag(1)
            }
            .pickerStyle(.segmented).labelsHidden().frame(width: 260).padding(10)
            Divider()
            if tab == 0 {
                RemotesView(repo: repo).padding(8)
            } else {
            Form {
                Section("このリポジトリのユーザー情報 (空欄ならグローバル設定)") {
                    TextField("名前 (user.name)", text: $userName)
                    TextField("メール (user.email)", text: $userEmail)
                }
                Section("動作") {
                    Picker("改行コード変換 (core.autocrlf)", selection: $autocrlf) {
                        Text("既定").tag(""); Text("false").tag("false"); Text("input").tag("input"); Text("true").tag("true")
                    }
                    Picker("プル時の動作 (pull.rebase)", selection: $pullRebase) {
                        Text("既定").tag(""); Text("マージ (false)").tag("false"); Text("リベース (true)").tag("true")
                    }
                    Picker("大文字小文字を無視 (core.ignorecase)", selection: $ignoreCase) {
                        Text("既定").tag(""); Text("true").tag("true"); Text("false").tag("false")
                    }
                    TextField("フックのパス (core.hooksPath)", text: $hooksPath)
                }
                HStack {
                    Button("保存") { save() }.keyboardShortcut(.defaultAction)
                    Button(".git/config を編集") { openInTextEditor(repo.gitDir + "/config") }
                    Button(".gitignore を編集") {
                        let p = repo.root + "/.gitignore"
                        if !FileManager.default.fileExists(atPath: p) { FileManager.default.createFile(atPath: p, contents: Data()) }
                        openInTextEditor(p)
                    }
                    Button("フォルダを開く") { NSWorkspace.shared.open(URL(fileURLWithPath: repo.root)) }
                }
            }
            .formStyle(.grouped)
            }
        }
        .onAppear {
            func local(_ k: String) -> String { repo.git(["config", "--local", "--get", k]).out.trimmingCharacters(in: .whitespacesAndNewlines) }
            userName = local("user.name"); userEmail = local("user.email")
            autocrlf = local("core.autocrlf"); pullRebase = local("pull.rebase")
            ignoreCase = local("core.ignorecase"); hooksPath = local("core.hooksPath")
        }
    }

    private func save() {
        func set(_ k: String, _ v: String) {
            if v.isEmpty { repo.git(["config", "--local", "--unset", k]) } else { repo.git(["config", "--local", k, v]) }
        }
        set("user.name", userName); set("user.email", userEmail)
        set("core.autocrlf", autocrlf); set("pull.rebase", pullRebase)
        set("core.ignorecase", ignoreCase); set("core.hooksPath", hooksPath)
        handle.close()
    }
}

func openInTextEditor(_ path: String) {
    let url = URL(fileURLWithPath: path)
    let cfg = NSWorkspace.OpenConfiguration()
    for id in ["com.coteditor.CotEditor", "com.microsoft.VSCode", "com.apple.TextEdit"] {
        if let app = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) {
            NSWorkspace.shared.open([url], withApplicationAt: app, configuration: cfg, completionHandler: nil)
            return
        }
    }
    NSWorkspace.shared.open(url)
}

// MARK: - Help

struct HelpView: View {
    private struct Topic: Identifiable { let id = UUID(); let title: String; let symbol: String; let text: String }
    private let topics: [Topic] = [
        Topic(title: "基本的な使い方", symbol: "cursorarrow.click.2", text: "Finder でファイルやフォルダを右クリックすると「Git 同期…」「Git コミット…」と「GitVelto ▸」サブメニューが表示されます。リポジトリの外では「Git クローン…」「Git ここにリポジトリを作成…」が表示されます。表示する項目やトップレベルに出す項目は「設定 > コンテキストメニュー」で変更できます。"),
        Topic(title: "状態アイコン (オーバーレイ)", symbol: "checkmark.circle.fill", text: "緑のチェック: 変更なし / 赤の !: 変更あり / 青の +: 追加 / 橙の稲妻: 競合 / 灰の ?: 未追跡 / 濃灰の −: 無視。フォルダには中のファイルの最も重要な状態が表示されます。アイコンは GitVelto が起動している間更新されます (設定で「ログイン時に起動」をオン推奨)。"),
        Topic(title: "コミット", symbol: "checkmark.circle", text: "コミットしたいファイルにチェックを入れ、メッセージを入力して ⌘↩ でコミットします。ダブルクリックで差分、右クリックで元に戻す・無視などができます。「直前のコミットをやり直す」で amend、「Signed-off-by」で署名行、「作成者を指定」で別の作成者としてコミットできます。"),
        Topic(title: "ログ", symbol: "clock.arrow.circlepath", text: "コミットグラフ付きの履歴を表示します。⌘クリック/⇧クリックで複数選択し、2 つのコミットを比較したりパッチを作成できます。コミットを右クリックすると、ブランチ/タグの作成、リセット、リバート、チェリーピック、リベース、二分探索などが行えます。一番上の「作業ツリーの変更」で未コミットの変更を確認できます。"),
        Topic(title: "同期 (プル/プッシュ)", symbol: "arrow.triangle.2.circlepath", text: "「Git 同期…」ではプル・フェッチ・プッシュ・フェッチ＆リベースを 1 つの画面で行え、受信予定/送信予定のコミットも確認できます。認証が必要な場合はダイアログが表示されます (HTTPS は macOS のキーチェーンを利用)。"),
        Topic(title: "ブランチとマージ", symbol: "arrow.triangle.branch", text: "切り替え/チェックアウト、ブランチ・タグの作成、マージ、リベース (並べ替え・統合・削除・メッセージ変更が可能な対話的リベース) を行えます。「参照の一覧」ではブランチやタグの削除・名前変更・上流設定ができます。"),
        Topic(title: "競合の解決", symbol: "exclamationmark.triangle", text: "マージやリベースで競合したファイルは橙色のアイコンになります。「競合を編集…」で各競合箇所ごとに自分/相手/両方を選んで保存できます。「競合を解決…」で解決済みにし、マージ・リベースの続行/中止もそこから行えます。"),
        Topic(title: "その他の機能", symbol: "ellipsis.circle", text: "退避 (stash)、二分探索 (bisect)、Blame、リポジトリブラウザ、Reflog、サブモジュール、パッチの作成/適用、エクスポート (ZIP)、クリーンアップ (ゴミ箱へ移動)、無視リストへの追加、リモートの管理など、TortoiseGit の主な機能を利用できます。"),
        Topic(title: "用語がわからないとき", symbol: "book", text: "各画面の用語の横にある「?」ボタンを押すと、その場で意味と使いどころが表示されます。すべての用語は「Git 用語集」（Finder の GitVelto メニュー、メニューバー、ヘルプメニューから開けます）で検索できます。"),
        Topic(title: "コマンドを学ぶ（学習モード）", symbol: "graduationcap", text: "学習モードがオンのとき、各ダイアログの下に「実行される git コマンド」とオプションの意味が表示され、進捗画面にも各コマンドの解説が出ます。「実行したコマンドの履歴」では、これまでの操作で実行されたコマンドを確認・コピーでき、ターミナルで同じ操作を試せます。設定 > 一般 > 学習 でオン/オフを切り替えられます。"),
        Topic(title: "外部ツール", symbol: "wrench.and.screwdriver", text: "設定 > 外部ツールで、差分/マージに VS Code などの外部アプリを指定できます。$LOCAL・$REMOTE・$BASE・$MERGED がファイルパスに置き換わります。"),
    ]

    var body: some View {
        List(topics) { t in
            VStack(alignment: .leading, spacing: 6) {
                Label(t.title, systemImage: t.symbol).font(.headline)
                Text(t.text).font(.callout).fixedSize(horizontal: false, vertical: true)
            }
            .padding(.vertical, 6)
        }
    }
}
