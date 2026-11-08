import AppKit
import SwiftUI

final class StatusModel: ObservableObject {
    let repo: Repo
    let paths: [String]
    @Published var entries: [StatusEntry] = []
    @Published var checked: Set<String> = []
    @Published var selection: Set<String> = []
    @Published var showUntracked = true { didSet { reload() } }
    @Published var showIgnored = false { didSet { reload() } }
    @Published var wholeProject = false { didSet { reload() } }
    var hasPathFilter: Bool { !paths.isEmpty }
    @Published var loading = false
    @Published var opState: RepoOperationState = .none
    private var firstLoad = true

    init(repo: Repo, paths: [String]) {
        self.repo = repo
        self.paths = paths.filter { $0.trimmedPath != repo.root }
    }

    func reload() {
        loading = true
        let repo = repo, paths = wholeProject ? [] : paths, untracked = showUntracked, ignored = showIgnored
        DispatchQueue.global(qos: .userInitiated).async {
            let list = repo.status(paths: paths, untracked: untracked ? "all" : "no", ignored: ignored)
                .sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
            let st = repo.operationState()
            DispatchQueue.main.async {
                let explicit = Set(paths.map { repo.relative($0) })
                if self.firstLoad {
                    // TortoiseGit default: tracked changes checked; untracked only if explicitly selected.
                    self.checked = Set(list.filter { $0.state.isTracked || explicit.contains($0.path) }.map(\.id))
                    self.firstLoad = false
                } else {
                    let known = Set(self.entries.map(\.id))
                    let newTracked = list.filter { !known.contains($0.id) && $0.state.isTracked }.map(\.id)
                    self.checked = self.checked.intersection(Set(list.map(\.id))).union(newTracked)
                }
                self.entries = list
                self.opState = st
                self.loading = false
            }
        }
    }

    func entries(for ids: Set<String>) -> [StatusEntry] { entries.filter { ids.contains($0.id) } }

    func openDiff(_ e: StatusEntry) {
        WindowManager.shared.showDiff(repo: repo, source: .workingTree(path: e.path, origPath: e.origPath, state: e.state))
    }
}

/// Shared context menu for working-tree file lists.
struct WorkingTreeMenu: View {
    let model: StatusModel
    let ids: Set<String>

    var body: some View {
        let sel = model.entries(for: ids)
        let abs = sel.map { model.repo.absolute($0.path) }
        if sel.count == 1, let e = sel.first {
            Button("差分を表示") { model.openDiff(e) }
        }
        if sel.contains(where: { $0.state == .untracked }) {
            Button("追加 (git add)") {
                let rels = sel.filter { $0.state == .untracked }.map(\.path)
                model.repo.git(["--literal-pathspecs", "add", "--"] + rels)
                model.reload()
            }
        }
        if sel.contains(where: { $0.state.isTracked }) {
            Button("元に戻す…") { WindowManager.shared.perform(.revert, paths: abs) }
        }
        if sel.contains(where: { $0.state == .conflict }) {
            if sel.count == 1 { Button("競合を編集…") { WindowManager.shared.perform(.editConflict, paths: abs) } }
            Button("競合を解決…") { WindowManager.shared.perform(.resolve, paths: abs) }
        }
        if sel.contains(where: { $0.staged }) {
            Button("ステージを解除") {
                model.repo.git(["--literal-pathspecs", "restore", "--staged", "--"] + sel.filter(\.staged).map(\.path))
                model.reload()
            }
        }
        Button("無視リストに追加…") { WindowManager.shared.perform(.ignore, paths: abs) }
        Divider()
        Button("開く") { abs.forEach(openFile) }
        Button("Finder で表示") { revealInFinder(abs) }
        Button("パスをコピー") { copyToPasteboard(sel.map(\.path).joined(separator: "\n")) }
        if sel.count == 1 {
            Button("ログを表示") { WindowManager.shared.perform(.log, paths: abs) }
            if sel[0].state != .untracked { Button("Blame") { WindowManager.shared.perform(.blame, paths: abs) } }
        }
    }
}

struct CommitView: View {
    @StateObject private var model: StatusModel
    @EnvironmentObject var handle: WindowHandle
    @EnvironmentObject var settings: AppSettings
    @State private var message = ""
    @State private var amend = false
    @State private var push = AppSettings.shared.pushAfterCommit
    @State private var userName = ""
    @State private var userEmail = ""
    @State private var needsIdentity = false
    @State private var setAuthor = false
    @State private var author = ""
    @State private var setDate = false
    @State private var date = Date()
    @State private var newBranch = false
    @State private var branchName = ""

    init(repo: Repo, paths: [String]) {
        _model = StateObject(wrappedValue: StatusModel(repo: repo, paths: paths))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: model.repo)

            HStack {
                Text("メッセージ:").font(.headline)
                Spacer()
                Menu("最近のメッセージ") {
                    ForEach(settings.recentMessages, id: \.self) { m in
                        Button(String(m.prefix(80)).replacingOccurrences(of: "\n", with: " ")) { message = m }
                    }
                }.menuStyle(.borderlessButton).fixedSize()
            }
            TextEditor(text: $message)
                .monospacedEditor()
                .frame(minHeight: 110, idealHeight: 150)
            HStack(spacing: 12) {
                Button("Signed-off-by を追加") { addSignOff() }.controlSize(.small)
                Toggle("作成者を指定", isOn: $setAuthor)
                if setAuthor { TextField("名前 <メール>", text: $author).frame(width: 220) }
                Toggle("日時を指定", isOn: $setDate)
                if setDate { DatePicker("", selection: $date).labelsHidden() }
                Spacer()
            }
            .font(.callout)
            HStack {
                Toggle("新しいブランチを作成してコミット", isOn: $newBranch)
                if newBranch { TextField("ブランチ名", text: $branchName).frame(width: 200) }
                Spacer()
            }
            .font(.callout)
            HStack {
                Toggle("直前のコミットをやり直す (amend)", isOn: $amend)
                    .onChange(of: amend) {
                        if amend && message.isEmpty {
                            message = model.repo.git(["log", "-1", "--format=%B"]).out.trimmingCharacters(in: .whitespacesAndNewlines)
                        }
                    }
                Spacer()
                let first = message.split(separator: "\n", omittingEmptySubsequences: false).first.map(String.init) ?? ""
                Text("1行目: \(first.count) 文字").font(.caption).foregroundStyle(first.count > 72 ? .orange : .secondary)
            }

            if needsIdentity {
                GroupBox("作成者情報が未設定です (git config --global)") {
                    HStack {
                        TextField("名前 (user.name)", text: $userName)
                        TextField("メール (user.email)", text: $userEmail)
                    }
                }
            }

            Divider()
            HStack {
                Text("変更されたファイル").font(.headline)
                Text("\(model.checked.count) / \(model.entries.count) 選択").foregroundStyle(.secondary)
                if model.loading { ProgressView().controlSize(.small) }
                Spacer()
                Toggle("未追跡を表示", isOn: $model.showUntracked)
                if model.hasPathFilter { Toggle("プロジェクト全体", isOn: $model.wholeProject) }
                Button("全選択") { model.checked = Set(model.entries.map(\.id)) }
                Button("全解除") { model.checked = [] }
                Button { model.reload() } label: { Image(systemName: "arrow.clockwise") }.help("再読み込み")
            }
            FileTable(entries: model.entries, checked: $model.checked, selection: $model.selection,
                      onOpen: { model.openDiff($0) }) { ids in
                WorkingTreeMenu(model: model, ids: ids)
            }
            .frame(minHeight: 180)

            HStack {
                Toggle("コミット後にプッシュ", isOn: $push)
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button(push ? "コミットしてプッシュ" : "コミット") { commit() }
                    .keyboardShortcut(.return, modifiers: .command)
                    .buttonStyle(.borderedProminent)
                    .disabled(message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        }
        .padding(14)
        .onAppear {
            model.reload()
            if let m = model.repo.mergeMessage(), model.repo.operationState() != .none {
                message = m.split(separator: "\n").filter { !$0.hasPrefix("#") }.joined(separator: "\n")
            }
            let n = model.repo.config("user.name"), e = model.repo.config("user.email")
            needsIdentity = n.isEmpty || e.isEmpty
            userName = n; userEmail = e
        }
    }

    private func addSignOff() {
        let n = model.repo.config("user.name"), e = model.repo.config("user.email")
        let line = "Signed-off-by: \(n) <\(e)>"
        if message.contains(line) { return }
        var m = message.trimmingCharacters(in: .newlines)
        m += m.isEmpty ? line : (m.contains("Signed-off-by:") ? "\n" : "\n\n") + line
        message = m
    }

    private func commit() {
        let repo = model.repo
        if model.checked.isEmpty && !amend && model.opState != .merging {
            guard WindowManager.confirm("ファイルが選択されていません", info: "変更を含まない空のコミットを作成しますか?", ok: "空のコミットを作成") else { return }
        }
        if newBranch && branchName.trimmingCharacters(in: .whitespaces).isEmpty {
            WindowManager.alert("ブランチ名を入力してください"); return
        }
        if needsIdentity {
            guard !userName.isEmpty, !userEmail.isEmpty else {
                WindowManager.alert("作成者の名前とメールアドレスを入力してください")
                return
            }
            repo.git(["config", "--global", "user.name", userName])
            repo.git(["config", "--global", "user.email", userEmail])
        }
        let selected = model.entries.filter { model.checked.contains($0.id) }
        var opts = GitOps.CommitOptions(message: message, amend: amend)
        if setAuthor && !author.isEmpty { opts.author = author }
        if setDate { opts.date = date }
        if newBranch { opts.newBranch = branchName.trimmingCharacters(in: .whitespaces) }
        opts.push = push
        let steps = GitOps.commitSteps(repo: repo, entries: selected, options: opts)
        settings.remember(message: message)
        settings.pushAfterCommit = push
        let root = repo.root
        WindowManager.shared.runProgress(push ? "コミットしてプッシュ" : "コミット", dir: root, steps: steps,
                                         successActions: [ProgressAction(title: "ログを表示") {
                                             WindowManager.shared.perform(.log, paths: [root])
                                         }])
        handle.close()
    }
}

/// "Check for modifications" window.
struct StatusView: View {
    @StateObject private var model: StatusModel
    @EnvironmentObject var handle: WindowHandle

    init(repo: Repo, paths: [String]) {
        _model = StateObject(wrappedValue: StatusModel(repo: repo, paths: paths))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: model.repo)
            HStack {
                Text("\(model.entries.count) 件の変更").font(.headline)
                if model.loading { ProgressView().controlSize(.small) }
                Spacer()
                Toggle("未追跡を表示", isOn: $model.showUntracked)
                Toggle("無視を表示", isOn: $model.showIgnored)
                if model.hasPathFilter { Toggle("プロジェクト全体", isOn: $model.wholeProject) }
                Button { model.reload() } label: { Image(systemName: "arrow.clockwise") }.help("再読み込み")
            }
            FileTable(entries: model.entries, selection: $model.selection, onOpen: { model.openDiff($0) }) { ids in
                WorkingTreeMenu(model: model, ids: ids)
            }
            HStack {
                Text("ダブルクリックで差分を表示 / 右クリックで操作").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("パッチとして保存…") { savePatch() }
                Button("変更を退避…") { WindowManager.shared.perform(.stashSave, paths: [model.repo.root]) }
                Button("コミット…") { WindowManager.shared.perform(.commit, paths: model.paths.isEmpty ? [model.repo.root] : model.paths) }
                Button("閉じる") { handle.close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(14)
        .onAppear { model.reload() }
    }

    private func savePatch() {
        let p = NSSavePanel()
        p.nameFieldStringValue = "\(model.repo.name).diff"
        guard p.runModal() == .OK, let url = p.url else { return }
        let rels = model.paths.map { model.repo.relative($0) }.filter { !$0.isEmpty }
        let r = model.repo.git(["diff", "HEAD", "--binary", "--"] + rels)
        do { try r.stdout.write(to: url) } catch { WindowManager.alert("保存できません", info: error.localizedDescription) }
    }
}
