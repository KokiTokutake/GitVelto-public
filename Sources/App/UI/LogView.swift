import SwiftUI

// MARK: - Commit graph

struct GraphRow {
    let node: Int
    let top: [String?]
    let bottom: [String?]
    let parents: [String]
    let hash: String
}

enum CommitGraph {
    static func compute(_ commits: [Commit]) -> [GraphRow] {
        var lanes: [String?] = []
        var rows: [GraphRow] = []
        rows.reserveCapacity(commits.count)
        for c in commits {
            let top = lanes
            var node = lanes.firstIndex(of: c.hash) ?? -1
            if node < 0 {
                if let free = lanes.firstIndex(of: nil) { node = free } else { node = lanes.count; lanes.append(nil) }
            }
            for i in lanes.indices where i != node && lanes[i] == c.hash { lanes[i] = nil }
            if c.parents.isEmpty {
                lanes[node] = nil
            } else {
                lanes[node] = c.parents[0]
                for p in c.parents.dropFirst() where !lanes.contains(p) {
                    if let free = lanes.firstIndex(of: nil) { lanes[free] = p } else { lanes.append(p) }
                }
            }
            while let last = lanes.last, last == nil { lanes.removeLast() }
            rows.append(GraphRow(node: node, top: top, bottom: lanes, parents: c.parents, hash: c.hash))
        }
        return rows
    }

    static let palette: [Color] = [
        Color(nsColor: .systemBlue), Color(nsColor: .systemGreen), Color(nsColor: .systemOrange),
        Color(nsColor: .systemPurple), Color(nsColor: .systemPink), Color(nsColor: .systemTeal),
        Color(nsColor: .systemRed), Color(nsColor: .systemBrown), Color(nsColor: .systemIndigo),
    ]
}

struct GraphCell: View {
    let row: GraphRow
    let laneWidth: CGFloat = 12

    var body: some View {
        Canvas { ctx, size in
            let h = size.height
            func x(_ i: Int) -> CGFloat { CGFloat(i) * laneWidth + laneWidth / 2 }
            func color(_ i: Int) -> Color { CommitGraph.palette[i % CommitGraph.palette.count] }
            func line(_ a: CGPoint, _ b: CGPoint, _ c: Color) {
                var p = Path()
                p.move(to: a)
                if a.x == b.x { p.addLine(to: b) } else {
                    let midY = (a.y + b.y) / 2
                    p.addCurve(to: b, control1: CGPoint(x: a.x, y: midY), control2: CGPoint(x: b.x, y: midY))
                }
                ctx.stroke(p, with: .color(c), lineWidth: 1.6)
            }
            let mid = CGPoint(x: x(row.node), y: h / 2)
            for (j, h0) in row.top.enumerated() {
                guard let hash = h0 else { continue }
                if hash == row.hash {
                    line(CGPoint(x: x(j), y: 0), mid, color(j))
                } else {
                    var k = row.bottom.firstIndex(of: hash) ?? j
                    if j < row.bottom.count, row.bottom[j] == hash { k = j }
                    line(CGPoint(x: x(j), y: 0), CGPoint(x: x(k), y: h), color(j))
                }
            }
            for p in row.parents {
                var k = row.bottom.firstIndex(of: p)
                if row.node < row.bottom.count, row.bottom[row.node] == p { k = row.node }
                if let k { line(mid, CGPoint(x: x(k), y: h), color(k)) }
            }
            let r: CGFloat = 4
            let dot = Path(ellipseIn: CGRect(x: mid.x - r, y: mid.y - r, width: r * 2, height: r * 2))
            if row.parents.count > 1 {
                ctx.fill(dot, with: .color(Color(nsColor: .textBackgroundColor)))
                ctx.stroke(dot, with: .color(color(row.node)), lineWidth: 2)
            } else {
                ctx.fill(dot, with: .color(color(row.node)))
            }
        }
    }
}

// MARK: - Model

final class LogModel: ObservableObject {
    static let worktreeID = "__WORKTREE__"
    let repo: Repo
    let paths: [String]
    let startRef: String?
    @Published var commits: [Commit] = []
    @Published var graph: [String: GraphRow] = [:]
    @Published var maxLanes = 1
    @Published var allBranches = false { didSet { reload() } }
    @Published var loading = false
    @Published var selected: String? { didSet { loadFiles() } }
    @Published var multi: Set<String> = []
    @Published var files: [ChangedFile] = []
    @Published var fileSelection: Set<String> = []
    @Published var filter = ""
    @Published var author = ""
    @Published var useDates = false
    @Published var since = Calendar.current.date(byAdding: .month, value: -1, to: Date()) ?? Date()
    @Published var until = Date()
    @Published var canLoadMore = false
    @Published var worktree: [StatusEntry] = []
    @Published var bisecting = false
    @Published var wholeProject = false { didSet { reload() } }
    /// Server-side message search (git log --grep) across the whole history.
    @Published var serverSearch = ""
    private var limit: Int

    init(repo: Repo, paths: [String], startRef: String? = nil) {
        self.repo = repo
        self.paths = paths
        self.startRef = startRef
        self.limit = AppSettings.shared.logLimit
    }

    var showsWorktree: Bool { startRef == nil && !worktree.isEmpty }

    var visible: [Commit] {
        let f = filter.trimmingCharacters(in: .whitespaces).lowercased()
        guard !f.isEmpty else { return commits }
        return commits.filter {
            $0.subject.lowercased().contains(f) || $0.body.lowercased().contains(f) ||
            $0.author.lowercased().contains(f) || $0.hash.hasPrefix(f) || $0.refs.joined().lowercased().contains(f)
        }
    }

    var selectedCommit: Commit? { commits.first { $0.hash == selected } }
    var selectedCommits: [Commit] { commits.filter { multi.contains($0.hash) } }

    func reload() {
        loading = true
        let repo = repo, paths = wholeProject ? [] : paths, all = allBranches, limit = limit, rev = startRef
        let since = useDates ? since : nil, until = useDates ? until : nil, author = author, search = serverSearch
        DispatchQueue.global(qos: .userInitiated).async {
            let list = repo.log(limit: limit, all: all, paths: paths, search: search, rev: rev, since: since, until: until, author: author)
            let rows = CommitGraph.compute(list)
            var dict: [String: GraphRow] = [:]
            var maxL = 1
            for r in rows {
                dict[r.hash] = r
                maxL = max(maxL, r.top.count, r.bottom.count, r.node + 1)
            }
            let wt = rev == nil ? repo.status(paths: paths, untracked: "all") : []
            let bis = repo.isBisecting
            DispatchQueue.main.async {
                self.bisecting = bis
                self.commits = list
                self.graph = dict
                self.maxLanes = min(maxL, 24)
                self.canLoadMore = list.count >= limit
                self.worktree = wt
                self.loading = false
                if self.selected == nil || (self.selected != LogModel.worktreeID && !list.contains(where: { $0.hash == self.selected })) {
                    self.select(list.first?.hash)
                } else if self.selected == LogModel.worktreeID {
                    self.loadFiles()
                }
            }
        }
    }

    func loadMore() {
        limit += AppSettings.shared.logLimit
        reload()
    }

    func select(_ hash: String?, extend: Bool = false, range: Bool = false) {
        guard let hash else { selected = nil; multi = []; return }
        if extend {
            if multi.contains(hash) { multi.remove(hash) } else { multi.insert(hash) }
        } else if range, let cur = selected {
            let list = visible.map(\.hash)
            if let a = list.firstIndex(of: cur), let b = list.firstIndex(of: hash) {
                multi = Set(list[min(a, b)...max(a, b)])
            }
        } else {
            multi = hash == LogModel.worktreeID ? [] : [hash]
        }
        selected = hash
    }

    private func loadFiles() {
        if selected == LogModel.worktreeID {
            files = worktree.map { ChangedFile(status: $0.state == .untracked ? "?" : String($0.x == " " ? $0.y : $0.x), path: $0.path, oldPath: $0.origPath) }
            fileSelection = []
            return
        }
        guard let c = selectedCommit else { files = []; return }
        let repo = repo
        DispatchQueue.global(qos: .userInitiated).async {
            let f = repo.changedFiles(of: c)
            DispatchQueue.main.async {
                if self.selected == c.hash { self.files = f; self.fileSelection = [] }
            }
        }
    }

    func openDiff(_ f: ChangedFile) {
        if selected == LogModel.worktreeID {
            if let e = worktree.first(where: { $0.path == f.path }) {
                WindowManager.shared.showDiff(repo: repo, source: .workingTree(path: e.path, origPath: e.origPath, state: e.state))
            }
            return
        }
        guard let c = selectedCommit else { return }
        WindowManager.shared.showDiff(repo: repo, source: .commit(hash: c.hash, parent: c.parents.first, path: f.path, oldPath: f.oldPath))
    }

    func move(_ delta: Int) {
        var list = visible.map(\.hash)
        if showsWorktree { list.insert(LogModel.worktreeID, at: 0) }
        guard !list.isEmpty else { return }
        let idx = list.firstIndex { $0 == selected } ?? -1
        let n = max(0, min(list.count - 1, idx + delta))
        select(list[n])
    }
}

// MARK: - Views

struct RefBadge: View {
    let ref: String
    var body: some View {
        let (text, color, bold) = style
        Text(text)
            .font(.system(size: 10, weight: bold ? .bold : .medium))
            .padding(.horizontal, 5).padding(.vertical, 1)
            .background(color.opacity(0.22))
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(color.opacity(0.7), lineWidth: 0.8))
            .clipShape(RoundedRectangle(cornerRadius: 3))
            .lineLimit(1)
    }

    private var style: (String, Color, Bool) {
        if ref.hasPrefix("HEAD -> ") { return (String(ref.dropFirst(8)), .green, true) }
        if ref == "HEAD" { return ("HEAD", .red, true) }
        if ref.hasPrefix("tag: ") { return (String(ref.dropFirst(5)), .yellow, false) }
        if ref.contains("/") { return (ref, .orange, false) }
        return (ref, .green, false)
    }
}

struct LogView: View {
    @StateObject private var model: LogModel
    @EnvironmentObject var handle: WindowHandle
    @FocusState private var listFocused: Bool
    @AppStorage("logSplitV") private var splitV: Double = 0.55
    @AppStorage("logSplitH") private var splitH: Double = 0.45

    init(repo: Repo, paths: [String], startRef: String? = nil) {
        _model = StateObject(wrappedValue: LogModel(repo: repo, paths: paths, startRef: startRef))
    }

    private let rowHeight: CGFloat = 22

    var body: some View {
        VStack(spacing: 0) {
            toolbar.padding(10)
            Divider()
            SplitPane(axis: .vertical, fraction: Binding(get: { CGFloat(splitV) }, set: { splitV = Double($0) }),
                      minFirst: 160, minSecond: 140) {
                commitList
            } second: {
                SplitPane(axis: .horizontal, fraction: Binding(get: { CGFloat(splitH) }, set: { splitH = Double($0) }),
                          minFirst: 240, minSecond: 240) {
                    details
                } second: {
                    filesList
                }
            }
        }
        .onAppear { model.reload() }
    }

    private var toolbar: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                RepoHeader(repo: model.repo).frame(maxWidth: 420)
                if let r = model.startRef {
                    Text("リビジョン: \(r)").font(.callout).foregroundStyle(.secondary)
                }
                if !model.paths.isEmpty {
                    Text("対象: " + model.paths.map { model.repo.relative($0) }.joined(separator: ", "))
                        .font(.callout).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
                        .strikethrough(model.wholeProject)
                    Toggle("プロジェクト全体", isOn: $model.wholeProject)
                }
                Spacer()
                Toggle("すべてのブランチ", isOn: $model.allBranches)
                TextField("フィルタ (メッセージ/作成者/ハッシュ/参照)", text: $model.filter)
                    .textFieldStyle(.roundedBorder).frame(width: 260)
                if model.loading { ProgressView().controlSize(.small) }
                Button { model.reload() } label: { Image(systemName: "arrow.clockwise") }.help("再読み込み (⌘R)")
                    .keyboardShortcut("r", modifiers: .command)
            }
            HStack(spacing: 10) {
                TextField("作成者で絞り込み", text: $model.author).textFieldStyle(.roundedBorder).frame(width: 180)
                    .onSubmit { model.reload() }
                Toggle("期間:", isOn: $model.useDates).onChange(of: model.useDates) { model.reload() }
                DatePicker("", selection: $model.since, displayedComponents: .date).labelsHidden().disabled(!model.useDates)
                    .onChange(of: model.since) { if model.useDates { model.reload() } }
                Text("〜")
                DatePicker("", selection: $model.until, displayedComponents: .date).labelsHidden().disabled(!model.useDates)
                    .onChange(of: model.until) { if model.useDates { model.reload() } }
                if !model.filter.isEmpty && model.serverSearch != model.filter {
                    Button("全履歴のメッセージから検索") { model.serverSearch = model.filter; model.reload() }
                }
                if !model.serverSearch.isEmpty {
                    Button("検索を解除: \(model.serverSearch)") { model.serverSearch = ""; model.filter = ""; model.reload() }
                }
                Spacer()
                Text("\(model.visible.count) コミット" + (model.multi.count > 1 ? " / \(model.multi.count) 個選択" : ""))
                    .font(.caption).foregroundStyle(.secondary)
                Button("Reflog") { WindowManager.shared.perform(.reflog, paths: [model.repo.root]) }
                Button("参照の一覧") { WindowManager.shared.perform(.refBrowser, paths: [model.repo.root]) }
            }
        }
    }

    private var commitList: some View {
        let list = model.visible
        let showGraph = model.filter.isEmpty
        let graphWidth = CGFloat(model.maxLanes) * 12 + 6
        return VStack(spacing: 0) {
            HStack(spacing: 0) {
                if showGraph { Text("グラフ").frame(width: graphWidth, alignment: .leading) }
                Text("メッセージ").frame(maxWidth: .infinity, alignment: .leading)
                Text("作成者").frame(width: 150, alignment: .leading)
                Text("日時").frame(width: 130, alignment: .leading)
                Text("ハッシュ").frame(width: 72, alignment: .leading)
            }
            .font(.caption).foregroundStyle(.secondary)
            .padding(.horizontal, 8).padding(.vertical, 4)
            Divider()
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 0) {
                        if model.showsWorktree && model.filter.isEmpty {
                            worktreeRow(graphWidth: showGraph ? graphWidth : 0).id(LogModel.worktreeID)
                        }
                        ForEach(list) { c in
                            row(c, showGraph: showGraph, graphWidth: graphWidth).id(c.hash)
                        }
                        if model.canLoadMore && model.filter.isEmpty {
                            Button("さらに読み込む…") { model.loadMore() }.padding(8)
                        }
                    }
                }
                .focusable()
                .focused($listFocused)
                .focusEffectDisabled()
                .onMoveCommand { dir in
                    switch dir {
                    case .up: model.move(-1)
                    case .down: model.move(1)
                    default: break
                    }
                    if let s = model.selected { proxy.scrollTo(s) }
                }
            }
        }
        .background(Color(nsColor: .controlBackgroundColor))
    }

    private func worktreeRow(graphWidth: CGFloat) -> some View {
        let isSel = model.selected == LogModel.worktreeID
        return HStack(spacing: 0) {
            if graphWidth > 0 {
                Image(systemName: "pencil.circle.fill").foregroundStyle(isSel ? .white : .orange)
                    .frame(width: graphWidth, alignment: .leading).padding(.leading, 2)
            }
            Text("作業ツリーの変更 (\(model.worktree.count) ファイル)").italic()
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .font(.system(size: 12))
        .foregroundStyle(isSel ? Color.white : Color.primary)
        .padding(.horizontal, 8)
        .frame(height: rowHeight)
        .background(isSel ? Color.accentColor : Color.orange.opacity(0.08))
        .contentShape(Rectangle())
        .onTapGesture { model.select(LogModel.worktreeID); listFocused = true }
        .contextMenu {
            Button("コミット…") { WindowManager.shared.perform(.commit, paths: [model.repo.root]) }
            Button("変更をチェック…") { WindowManager.shared.perform(.status, paths: [model.repo.root]) }
            Button("変更を退避 (stash)…") { WindowManager.shared.perform(.stashSave, paths: [model.repo.root]) }
        }
    }

    private func row(_ c: Commit, showGraph: Bool, graphWidth: CGFloat) -> some View {
        let isSel = model.selected == c.hash || model.multi.contains(c.hash)
        return HStack(spacing: 0) {
            if showGraph, let g = model.graph[c.hash] {
                GraphCell(row: g).frame(width: graphWidth, height: rowHeight)
            }
            HStack(spacing: 4) {
                ForEach(c.refs, id: \.self) { RefBadge(ref: $0) }
                Text(c.subject).lineLimit(1)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Text(c.author).lineLimit(1).frame(width: 150, alignment: .leading)
            Text(relativeDateFormatter.string(from: c.date)).monospacedDigit().frame(width: 130, alignment: .leading)
            Text(c.short).font(.system(size: 11, design: .monospaced)).frame(width: 72, alignment: .leading)
        }
        .font(.system(size: 12))
        .foregroundStyle(isSel ? Color.white : Color.primary)
        .padding(.horizontal, 8)
        .frame(height: rowHeight)
        .background(isSel ? Color.accentColor : Color.clear)
        .contentShape(Rectangle())
        .onTapGesture {
            let flags = NSEvent.modifierFlags
            model.select(c.hash, extend: flags.contains(.command), range: flags.contains(.shift))
            listFocused = true
        }
        .contextMenu { commitMenu(c) }
    }

    @ViewBuilder
    private func commitMenu(_ c: Commit) -> some View {
        let repo = model.repo
        let sel = model.multi.contains(c.hash) ? model.selectedCommits : [c]
        if sel.count == 2 {
            Button("選択した 2 つのコミットを比較") {
                let ordered = Array(sel.reversed()) // log order is newest first
                let files = repo.changedFiles(from: ordered[0].hash, to: ordered[1].hash)
                WindowManager.shared.show("GitVelto - \(ordered[0].short) ↔ \(ordered[1].short)", size: NSSize(width: 640, height: 480)) { _ in
                    ChangedFilesView(repo: repo, files: files, from: ordered[0].hash, to: ordered[1].hash)
                }
            }
        }
        if sel.count > 1 {
            Button("選択した \(sel.count) コミットのパッチを作成…") {
                let hashes = sel.map(\.hash)
                WindowManager.shared.show("GitVelto - パッチを作成", size: NSSize(width: 520, height: 360)) { _ in CreatePatchView(repo: repo, commits: hashes) }
            }
            Button("選択した \(sel.count) コミットをチェリーピック") { cherryPick(sel) }
            Divider()
        }
        Button("ハッシュをコピー") { copyToPasteboard(sel.map(\.hash).joined(separator: "\n")) }
        Button("メッセージをコピー") { copyToPasteboard(c.subject + (c.body.isEmpty ? "" : "\n\n" + c.body)) }
        Divider()
        Button("このコミットからブランチを作成…") { createRef(.branch, c) }
        Button("このコミットにタグを作成…") { createRef(.tag, c) }
        Button("このコミットに切り替え (HEAD 切り離し)") { detach(c) }
        Button("このコミットまでリセット…") { reset(c) }
        Divider()
        Button("このコミットを打ち消す (revert)") { revert(c) }
        Button("チェリーピック") { cherryPick([c]) }
        Button("現在のブランチをこのコミットへリベース…") {
            WindowManager.shared.show("GitVelto - リベース", size: NSSize(width: 760, height: 560)) { _ in RebaseView(repo: repo, initialUpstream: c.hash) }
        }
        Divider()
        Button("HEAD と比較 (変更ファイル一覧)") {
            let files = repo.changedFiles(from: c.hash, to: "HEAD")
            WindowManager.shared.show("GitVelto - \(c.short) ↔ HEAD", size: NSSize(width: 640, height: 480)) { _ in
                ChangedFilesView(repo: repo, files: files, from: c.hash, to: "HEAD")
            }
        }
        Button("このリビジョンをリポジトリブラウザで開く") {
            WindowManager.shared.show("GitVelto - リポジトリブラウザ - \(c.short)", size: NSSize(width: 760, height: 560)) { _ in RepoBrowserView(repo: repo, revision: c.hash) }
        }
        Button("このリビジョンをエクスポート…") {
            WindowManager.shared.show("GitVelto - エクスポート", size: NSSize(width: 520, height: 300)) { _ in ExportView(repo: repo, revision: c.hash) }
        }
        Button("パッチを作成…") {
            WindowManager.shared.show("GitVelto - パッチを作成", size: NSSize(width: 520, height: 360)) { _ in CreatePatchView(repo: repo, commits: [c.hash]) }
        }
        Divider()
        if model.bisecting {
            Button("二分探索: このコミットは良い") { bisect("good", c) }
            Button("二分探索: このコミットは悪い") { bisect("bad", c) }
        } else {
            Button("二分探索を開始 (このコミットを良いとする)") {
                WindowManager.shared.show("GitVelto - 二分探索", size: NSSize(width: 520, height: 300)) { _ in BisectStartView(repo: repo, good: c.hash, bad: "HEAD") }
            }
            Button("二分探索を開始 (このコミットを悪いとする)") {
                WindowManager.shared.show("GitVelto - 二分探索", size: NSSize(width: 520, height: 300)) { _ in BisectStartView(repo: repo, good: "", bad: c.hash) }
            }
        }
    }

    // MARK: commit actions (shared by the context menu and the action bar)

    private func createRef(_ kind: CreateRefView.Kind, _ c: Commit) {
        let repo = model.repo
        WindowManager.shared.show(kind == .branch ? "GitVelto - ブランチを作成" : "GitVelto - タグを作成",
                                  size: NSSize(width: 520, height: kind == .branch ? 340 : 400)) { _ in
            CreateRefView(repo: repo, kind: kind, base: c.hash)
        }
    }

    private func detach(_ c: Commit) {
        if WindowManager.confirm("\(c.short) をチェックアウトしますか?", info: "HEAD が切り離された状態になります。") {
            WindowManager.shared.runProgress("チェックアウト", dir: model.repo.root, steps: [.git(GitOps.detach(c.hash))]) { _ in model.reload() }
        }
    }

    private func reset(_ c: Commit) {
        let repo = model.repo
        WindowManager.shared.show("GitVelto - リセット", size: NSSize(width: 480, height: 280)) { _ in
            ResetView(repo: repo, commit: c) { model.reload() }
        }
    }

    private func revert(_ c: Commit) {
        if WindowManager.confirm("\(c.short) を打ち消すコミットを作成しますか?", info: c.subject) {
            WindowManager.shared.runProgress("リバート", dir: model.repo.root, steps: [.git(GitOps.revertCommit(c))]) { _ in model.reload() }
        }
    }

    private func cherryPick(_ sel: [Commit]) {
        let msg = sel.count == 1 ? "\(sel[0].short) を現在のブランチにチェリーピックしますか?" : "\(sel.count) 個のコミットを現在のブランチにチェリーピックしますか?"
        if WindowManager.confirm(msg, info: sel.count == 1 ? sel[0].subject : "") {
            WindowManager.shared.runProgress("チェリーピック", dir: model.repo.root, steps: [.git(GitOps.cherryPick(sel))]) { _ in model.reload() }
        }
    }

    /// Buttons for the selected commit(s), shown above the details (same actions as the context menu).
    private func actionBar(_ c: Commit) -> some View {
        let sel = model.multi.contains(c.hash) ? model.selectedCommits : [c]
        return HStack(spacing: 6) {
            Button("ブランチ…") { createRef(.branch, c) }.help("このコミットからブランチを作成")
            Button("タグ…") { createRef(.tag, c) }.help("このコミットにタグを作成")
            Button("切り替え") { detach(c) }.help("このコミットに切り替え (HEAD 切り離し)")
            Button("リセット…") { reset(c) }.help("現在のブランチをこのコミットまでリセット")
            Button("リバート") { revert(c) }.help("このコミットを打ち消すコミットを作成")
            Button(sel.count > 1 ? "チェリーピック (\(sel.count))" : "チェリーピック") { cherryPick(sel) }
                .help("現在のブランチにチェリーピック")
        }
        .controlSize(.small)
    }

    private func bisect(_ verb: String, _ c: Commit) {
        WindowManager.shared.runProgress("二分探索", dir: model.repo.root, steps: [.git(["bisect", verb, c.hash])]) { _ in
            DispatchQueue.main.async { model.reload() }
        }
    }

    private var details: some View {
        ScrollView {
            if model.selected == LogModel.worktreeID {
                VStack(alignment: .leading, spacing: 8) {
                    Text("コミットされていない変更").font(.headline)
                    Text("\(model.worktree.count) 個のファイルが変更されています。右側のファイルをダブルクリックすると差分を表示します。")
                        .foregroundStyle(.secondary)
                    HStack {
                        Button("コミット…") { WindowManager.shared.perform(.commit, paths: [model.repo.root]) }
                        Button("変更をチェック…") { WindowManager.shared.perform(.status, paths: [model.repo.root]) }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(10)
            } else if let c = model.selectedCommit {
                VStack(alignment: .leading, spacing: 6) {
                    actionBar(c)
                    Divider()
                    HStack {
                        Text("コミット:").foregroundStyle(.secondary)
                        Text(c.hash).font(.system(size: 12, design: .monospaced))
                        Button { copyToPasteboard(c.hash) } label: { Image(systemName: "doc.on.doc") }.buttonStyle(.borderless)
                    }
                    if !c.parents.isEmpty {
                        HStack { Text("親:").foregroundStyle(.secondary); Text(c.parents.map { String($0.prefix(10)) }.joined(separator: ", ")).font(.system(size: 12, design: .monospaced)) }
                    }
                    HStack { Text("作成者:").foregroundStyle(.secondary); Text("\(c.author) <\(c.email)>") }
                    HStack { Text("日時:").foregroundStyle(.secondary); Text(relativeDateFormatter.string(from: c.date)) }
                    if !c.refs.isEmpty { HStack { ForEach(c.refs, id: \.self) { RefBadge(ref: $0) } } }
                    Divider()
                    Text(c.subject).font(.headline)
                    if !c.body.isEmpty { Text(c.body).font(.system(size: 12)) }
                }
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(10)
            } else {
                Text("コミットを選択してください").foregroundStyle(.secondary).padding()
            }
        }
    }

    private var filesList: some View {
        Table(model.files, selection: $model.fileSelection) {
            TableColumn("状態") { f in Text(f.state.label).foregroundStyle(f.state.color) }.width(min: 50, ideal: 70, max: 90)
            TableColumn("パス") { f in
                HStack(spacing: 6) {
                    Image(nsImage: FileIcons.icon(for: f.path)).resizable().frame(width: 16, height: 16)
                    Text(f.oldPath.map { "\($0) → \(f.path)" } ?? f.path).lineLimit(1).truncationMode(.middle)
                }
            }
        }
        .contextMenu(forSelectionType: String.self, menu: { ids in
            let sel = model.files.filter { ids.contains($0.id) }
            if let f = sel.first {
                Button("差分を表示") { model.openDiff(f) }
                Button("このファイルのログ") { WindowManager.shared.perform(.log, paths: [model.repo.absolute(f.path)]) }
                Button("Blame") { WindowManager.shared.perform(.blame, paths: [model.repo.absolute(f.path)]) }
                Button("Finder で表示") { revealInFinder([model.repo.absolute(f.path)]) }
                Button("パスをコピー") { copyToPasteboard(sel.map(\.path).joined(separator: "\n")) }
            }
        }, primaryAction: { ids in
            if let f = model.files.first(where: { ids.contains($0.id) }) { model.openDiff(f) }
        })
    }
}

/// Generic list of changed files between two revisions.
struct ChangedFilesView: View {
    let repo: Repo
    let files: [ChangedFile]
    let from: String
    let to: String
    @State private var selection: Set<String> = []

    var body: some View {
        VStack(alignment: .leading) {
            Text("\(files.count) 個のファイル").font(.headline)
            Table(files, selection: $selection) {
                TableColumn("状態") { f in Text(f.state.label).foregroundStyle(f.state.color) }.width(70)
                TableColumn("パス") { f in Text(f.oldPath.map { "\($0) → \(f.path)" } ?? f.path) }
            }
            .contextMenu(forSelectionType: String.self, menu: { _ in EmptyView() }, primaryAction: { ids in
                if let f = files.first(where: { ids.contains($0.id) }) {
                    WindowManager.shared.showDiff(repo: repo, source: .range(from: from, to: to, path: f.path, oldPath: f.oldPath))
                }
            })
        }
        .padding(12)
    }
}

struct ResetView: View {
    let repo: Repo
    let commit: Commit
    var onDone: () -> Void
    @EnvironmentObject var handle: WindowHandle
    @State private var mode = "mixed"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("現在のブランチを \(commit.short) にリセットします").font(.headline)
            Text(commit.subject).foregroundStyle(.secondary)
            Picker("リセットの種類", selection: $mode) {
                Text("ソフト: 作業ツリーとインデックスを維持").tag("soft")
                Text("混合: 作業ツリーを維持し、インデックスをリセット").tag("mixed")
                Text("ハード: 作業ツリーとインデックスをリセット (変更は失われます)").tag("hard")
            }
            .pickerStyle(.radioGroup)
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("リセット") {
                    if mode == "hard" && !WindowManager.confirm("ハードリセットしますか?", info: "コミットされていない変更はすべて失われます。", ok: "リセット", destructive: true) { return }
                    WindowManager.shared.runProgress("リセット", dir: repo.root, steps: [.git(GitOps.reset(mode: mode, to: commit.hash))]) { _ in onDone() }
                    handle.close()
                }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(16)
    }
}
