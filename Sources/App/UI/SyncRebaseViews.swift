import SwiftUI

// MARK: - Output log (embeddable)

struct OutputLogView: View {
    @ObservedObject var runner: CommandRunner
    @AppStorage("progressShowRaw") private var showRaw = false
    @EnvironmentObject var settings: AppSettings

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 1) {
                    ForEach(runner.lines) { line in
                        let text = showRaw ? line.raw : line.translated
                        Text(text.isEmpty ? " " : text)
                            .foregroundStyle(OutputLogView.color(line.kind))
                            .fontWeight(line.kind == .command ? .semibold : .regular)
                            .frame(maxWidth: .infinity, alignment: .leading)
                        if settings.learningMode, let args = line.args { LearningNote(args: args) }
                    }
                    if !runner.partial.isEmpty {
                        Text(showRaw ? runner.partial : GitMessages.translate(line: runner.partial))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    Color.clear.frame(height: 1).id("bottom")
                }
                .font(.system(size: 12, design: .monospaced))
                .textSelection(.enabled)
                .padding(8)
            }
            .background(Color(nsColor: .textBackgroundColor))
            .onChange(of: runner.lines.count) { proxy.scrollTo("bottom", anchor: .bottom) }
        }
    }

    static func color(_ k: OutputLine.Kind) -> Color {
        switch k {
        case .command: return Color(nsColor: .systemBlue)
        case .normal: return .primary
        case .error: return Color(nsColor: .systemRed)
        case .warning: return Color(nsColor: .systemOrange)
        case .success: return Color(nsColor: .systemGreen)
        }
    }
}

/// Simple commit list used by sync / rebase / reflog.
struct CommitListTable: View {
    let commits: [Commit]
    @Binding var selection: Set<String>
    var body: some View {
        Table(commits, selection: $selection) {
            TableColumn("ハッシュ") { c in Text(c.short).font(.system(size: 11, design: .monospaced)) }.width(70)
            TableColumn("メッセージ") { c in
                HStack(spacing: 4) {
                    ForEach(c.refs, id: \.self) { RefBadge(ref: $0) }
                    Text(c.subject).lineLimit(1)
                }
            }
            TableColumn("作成者") { c in Text(c.author).lineLimit(1) }.width(min: 80, ideal: 120, max: 180)
            TableColumn("日時") { c in Text(relativeDateFormatter.string(from: c.date)).monospacedDigit() }.width(120)
        }
    }
}

// MARK: - Git Sync

struct SyncView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @EnvironmentObject var settings: AppSettings
    @State private var refs: [RefInfo] = []
    @State private var remotes: [String] = []
    @State private var localBranch = ""
    @State private var remote = ""
    @State private var remoteBranch = ""
    @State private var tab = 0
    @State private var runner: CommandRunner?
    @State private var incoming: [Commit] = []
    @State private var outgoing: [Commit] = []
    @State private var sel: Set<String> = []
    @State private var running = false
    @State private var opState: RepoOperationState = .none

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 8) {
                GridRow {
                    Text("ローカルブランチ")
                    Picker("", selection: $localBranch) {
                        ForEach(refs.filter { $0.kind == .local }) { Text($0.name).tag($0.name) }
                    }.labelsHidden().onChange(of: localBranch) { defaultsForBranch(); refreshCommits() }
                    Text("リモート")
                    Picker("", selection: $remote) {
                        ForEach(remotes, id: \.self) { Text($0).tag($0) }
                    }.labelsHidden().frame(width: 140).onChange(of: remote) { refreshCommits() }
                }
                GridRow {
                    Text("リモートブランチ")
                    TextField("", text: $remoteBranch).onSubmit { refreshCommits() }
                    Button("リモートの管理…") { WindowManager.shared.perform(.remotes, paths: [repo.root]) }
                        .gridCellColumns(2)
                }
            }
            HStack(spacing: 8) {
                Button { run("プル", [["pull", "--progress", "--no-rebase", remote, remoteBranch]]) } label: { Label("プル", systemImage: "arrow.down.circle") }
                Button { run("フェッチ", [["fetch", "--progress", remote]]) } label: { Label("フェッチ", systemImage: "arrow.down.to.line") }
                Button { run("フェッチ＆リベース", [["fetch", "--progress", remote], ["rebase", "--autostash", "\(remote)/\(remoteBranch)"]]) } label: { Label("フェッチ＆リベース", systemImage: "arrow.up.and.down") }
                Button { run("プッシュ", [pushArgs]) } label: { Label("プッシュ", systemImage: "arrow.up.circle") }
                Button { run("プル＆プッシュ", [["pull", "--progress", "--no-rebase", remote, remoteBranch], pushArgs]) } label: { Label("プル＆プッシュ", systemImage: "arrow.triangle.2.circlepath") }
                Spacer()
                if running { ProgressView().controlSize(.small) }
                Button { load() } label: { Image(systemName: "arrow.clockwise") }.help("状態を再読み込み")
                Button("すべてフェッチ") { run("フェッチ", [["fetch", "--progress", "--all", "--prune"]]) }
            }
            .disabled(remotes.isEmpty || running)

            Picker("", selection: $tab) {
                Text("出力").tag(0)
                Text("受信予定のコミット (\(incoming.count))").tag(1)
                Text("送信予定のコミット (\(outgoing.count))").tag(2)
            }.pickerStyle(.segmented).labelsHidden()
            if settings.learningMode {
                HStack(spacing: 4) {
                    Image(systemName: "graduationcap").foregroundStyle(Color.accentColor)
                    Text("プル = git pull（取得＋取り込み）/ フェッチ = git fetch（取得のみ）/ フェッチ＆リベース = git fetch + git rebase / プッシュ = git push。受信予定 = リモートにあって手元にないコミット、送信予定 = 手元にあってリモートにないコミット。")
                        .font(.caption).foregroundStyle(.secondary)
                    TermHelp("upstream")
                }
            }

            Group {
                switch tab {
                case 1: CommitListTable(commits: incoming, selection: $sel)
                case 2: CommitListTable(commits: outgoing, selection: $sel)
                default:
                    if let runner { OutputLogView(runner: runner) } else {
                        Text(remotes.isEmpty ? "リモートが登録されていません。「リモートの管理…」から追加してください。" : "操作ボタンを押すと、ここに git の出力が表示されます。")
                            .foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }
            }
            .frame(minHeight: 200)
            .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color(nsColor: .separatorColor)))

            HStack {
                Button("ログを表示") { WindowManager.shared.perform(.log, paths: [repo.root]) }
                Button("コミット…") { WindowManager.shared.perform(.commit, paths: [repo.root]) }
                if opState != .none && opState != .bisecting {
                    Label(opState.label, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                    Button("競合を解決…") { WindowManager.shared.perform(.resolve, paths: [repo.root]) }
                        .buttonStyle(.borderedProminent)
                }
                Spacer()
                Button("閉じる") { handle.close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(14)
        .onAppear(perform: load)
        .onRepoChanged(repo.root) { if !running { load() } }
    }

    private var pushArgs: [String] {
        var a = ["push", "--progress"]
        if repo.upstream(of: localBranch) == nil { a.append("-u") }
        return a + [remote, "\(localBranch):\(remoteBranch.isEmpty ? localBranch : remoteBranch)"]
    }

    private func load() {
        refs = repo.refs()
        remotes = repo.remotes()
        opState = repo.operationState()
        localBranch = repo.currentBranch() ?? refs.first { $0.kind == .local }?.name ?? ""
        defaultsForBranch()
        refreshCommits()
    }

    private func defaultsForBranch() {
        if let up = repo.upstream(of: localBranch) {
            remote = up.remote; remoteBranch = up.branch
        } else {
            remote = remotes.contains("origin") ? "origin" : (remotes.first ?? "")
            remoteBranch = localBranch
        }
    }

    private func refreshCommits() {
        guard !remote.isEmpty, !localBranch.isEmpty else { return }
        let r = "\(remote)/\(remoteBranch)"
        let repo = repo, lb = localBranch
        DispatchQueue.global().async {
            let exists = repo.git(["rev-parse", "--verify", "-q", r]).ok
            let inc = exists ? repo.commits(range: "\(lb)..\(r)") : []
            let out = exists ? repo.commits(range: "\(r)..\(lb)") : repo.commits(range: lb, limit: 100)
            DispatchQueue.main.async { incoming = inc; outgoing = out }
        }
    }

    private func run(_ title: String, _ commands: [[String]]) {
        let r = CommandRunner(dir: repo.root, steps: commands.map { .git($0) })
        r.onFinish = { _ in
            running = false
            StatusService.shared.invalidate(path: repo.root)
            refs = repo.refs()
            opState = repo.operationState()
            refreshCommits()
        }
        runner = r
        running = true
        tab = 0
        r.start()
    }
}

// MARK: - Interactive rebase

struct RebaseItem: Identifiable, Hashable {
    enum Action: String, CaseIterable {
        case pick, reword, edit, squash, fixup, drop
        var label: String {
            switch self {
            case .pick: return "採用 (pick)"
            case .reword: return "メッセージ変更"
            case .edit: return "編集で停止"
            case .squash: return "前と統合 (squash)"
            case .fixup: return "前と統合・メッセージ破棄"
            case .drop: return "削除 (drop)"
            }
        }
    }
    var id: String { commit.hash }
    var commit: Commit
    var action: Action = .pick
    var message: String
}

struct RebaseView: View {
    let repo: Repo
    let initialUpstream: String?
    @EnvironmentObject var handle: WindowHandle
    @State private var refs: [RefInfo] = []
    @State private var upstream = ""
    @State private var custom = ""
    @State private var useCustom = false
    @State private var items: [RebaseItem] = []
    @State private var originalOrder: [String] = []
    @State private var selection: String?
    @State private var autostash = true
    @State private var keepEmpty = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            HStack {
                Text("リベース先 (upstream):")
                if useCustom {
                    TextField("コミット/参照", text: $custom).frame(width: 220).onSubmit(load)
                } else {
                    RefPicker(title: "", refs: refs.filter { !$0.isHead }, selection: $upstream).labelsHidden().frame(width: 260)
                        .onChange(of: upstream) { load() }
                }
                Toggle("コミットを指定", isOn: $useCustom).onChange(of: useCustom) { load() }
                Spacer()
                Button { load() } label: { Image(systemName: "arrow.clockwise") }
            }
            HStack {
                Text("\(items.count) 個のコミットを並べ替え・統合・削除できます (ドラッグまたは ↑↓ で並べ替え)。上が古いコミットです。")
                    .font(.caption).foregroundStyle(.secondary)
                TermHelp("rebase"); TermHelp("squash"); TermHelp("fixup")
            }
            List(selection: $selection) {
                ForEach($items) { $item in
                    HStack {
                        Picker("", selection: $item.action) {
                            ForEach(RebaseItem.Action.allCases, id: \.self) { Text($0.label).tag($0) }
                        }.labelsHidden().frame(width: 190)
                        Text(item.commit.short).font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary)
                        if item.action == .reword {
                            TextField("新しいメッセージ", text: $item.message)
                        } else {
                            Text(item.commit.subject).lineLimit(1)
                                .strikethrough(item.action == .drop)
                                .foregroundStyle(item.action == .drop ? .secondary : .primary)
                        }
                        Spacer()
                        Text(item.commit.author).foregroundStyle(.secondary).lineLimit(1)
                    }
                    .tag(item.id)
                }
                .onMove { from, to in items.move(fromOffsets: from, toOffset: to) }
            }
            CommandPreview(commands: rebasePreview,
                           note: items.map(\.id) == originalOrder && items.allSatisfy { $0.action == .pick } ? nil :
                               "対話的リベースでは、次の作業リスト (todo) を git に渡します:\n" + todoPreview)
            HStack {
                Button { move(-1) } label: { Image(systemName: "arrow.up") }.disabled(selection == nil)
                Button { move(1) } label: { Image(systemName: "arrow.down") }.disabled(selection == nil)
                Menu("一括設定") {
                    ForEach(RebaseItem.Action.allCases, id: \.self) { a in
                        Button(a.label) { for i in items.indices.dropFirst() { items[i].action = a }; if a != .squash && a != .fixup { items[0].action = a } }
                    }
                }.fixedSize()
                Toggle("ローカルの変更を自動退避 (--autostash)", isOn: $autostash)
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("リベース開始") { start() }.keyboardShortcut(.defaultAction).buttonStyle(.borderedProminent)
                    .disabled(target.isEmpty)
            }
        }
        .padding(14)
        .onAppear {
            refs = repo.refs()
            if let u = initialUpstream {
                if refs.contains(where: { $0.name == u }) { upstream = u } else { useCustom = true; custom = u }
            } else if let b = repo.currentBranch(), let up = repo.upstream(of: b) {
                upstream = "\(up.remote)/\(up.branch)"
            } else {
                upstream = refs.first { $0.kind == .local && !$0.isHead && ($0.name == "main" || $0.name == "master") }?.name
                    ?? refs.first { !$0.isHead }?.name ?? ""
            }
            load()
        }
    }

    private var target: String { useCustom ? custom.trimmingCharacters(in: .whitespaces) : upstream }

    private var rebasePreview: [[String]] {
        guard !target.isEmpty else { return [] }
        let unchanged = items.map(\.id) == originalOrder && items.allSatisfy { $0.action == .pick }
        return [["rebase"] + (autostash ? ["--autostash"] : []) + (unchanged ? [] : ["-i"]) + [target]]
    }

    private var todoPreview: String {
        items.map { "\($0.action == .reword ? "reword" : $0.action.rawValue) \($0.commit.short) \($0.commit.subject)" }.joined(separator: "\n")
    }

    private func load() {
        guard !target.isEmpty else { items = []; return }
        let list = repo.commits(range: "\(target)..HEAD", limit: 500, reverse: true, noMerges: true)
        items = list.map { RebaseItem(commit: $0, message: $0.subject + ($0.body.isEmpty ? "" : "\n\n" + $0.body)) }
        originalOrder = list.map(\.hash)
    }

    private func move(_ delta: Int) {
        guard let s = selection, let i = items.firstIndex(where: { $0.id == s }) else { return }
        let j = i + delta
        guard items.indices.contains(j) else { return }
        items.swapAt(i, j)
    }

    private func start() {
        let step: ProgressStep
        do {
            step = try GitOps.rebaseStep(repo: repo, items: items, originalOrder: originalOrder, upstream: target, autostash: autostash)
        } catch {
            WindowManager.alert(error.localizedDescription); return
        }
        let root = repo.root
        WindowManager.shared.runProgress("リベース", dir: root, steps: [step],
                                         successActions: [ProgressAction(title: "ログを表示") { WindowManager.shared.perform(.log, paths: [root]) }]) { ok in
            let st = Repo(root: root).operationState()
            if st == .rebasing {
                DispatchQueue.main.async {
                    if WindowManager.confirm(ok ? "リベースが途中で停止しました" : "リベース中に競合が発生しました",
                                             info: "競合の解決/続行画面を開きますか?", ok: "開く") {
                        WindowManager.shared.perform(.resolve, paths: [root])
                    }
                }
            }
        }
        handle.close()
    }
}
