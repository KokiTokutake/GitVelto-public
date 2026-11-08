import AppKit
import SwiftUI

// MARK: - Browse references

struct RefBrowserView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State private var refs: [RefDetail] = []
    @State private var filter = ""
    @State private var kind = 0 // 0 all, 1 local, 2 remote, 3 tag
    @State private var selection: Set<String> = []
    @State private var sortOrder = [KeyPathComparator(\RefDetail.name)]
    @State private var firstRemote: String?

    private var shown: [RefDetail] {
        refs.filter { r in
            (kind == 0 || (kind == 1 && r.kind == .local) || (kind == 2 && r.kind == .remote) || (kind == 3 && r.kind == .tag))
            && (filter.isEmpty || r.name.localizedCaseInsensitiveContains(filter) || r.subject.localizedCaseInsensitiveContains(filter))
        }.sorted(using: sortOrder)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            HStack {
                Picker("", selection: $kind) {
                    Text("すべて").tag(0); Text("ローカル").tag(1); Text("リモート").tag(2); Text("タグ").tag(3)
                }.pickerStyle(.segmented).labelsHidden().frame(width: 300)
                TextField("フィルタ", text: $filter).textFieldStyle(.roundedBorder)
                Button { load() } label: { Image(systemName: "arrow.clockwise") }.help("再読み込み")
            }
            Table(shown, selection: $selection, sortOrder: $sortOrder) {
                TableColumn("名前", value: \.name) { r in
                    HStack(spacing: 6) {
                        Image(systemName: r.kind == .tag ? "tag" : (r.kind == .remote ? "network" : "arrow.triangle.branch"))
                            .foregroundStyle(r.kind == .tag ? .yellow : (r.kind == .remote ? .orange : .green))
                        Text(r.name).fontWeight(r.isHead ? .bold : .regular)
                    }
                }
                TableColumn("種類") { r in Text(r.kindLabel) }.width(60)
                TableColumn("最新のコミット") { r in Text(r.subject).lineLimit(1) }
                TableColumn("日時", value: \.date) { r in Text(relativeDateFormatter.string(from: r.date)).monospacedDigit() }.width(120)
                TableColumn("作成者") { r in Text(r.author).lineLimit(1) }.width(min: 70, ideal: 110)
                TableColumn("上流 / 状態") { r in Text([r.upstream, r.track].filter { !$0.isEmpty }.joined(separator: " ")).foregroundStyle(.secondary) }
            }
            .contextMenu(forSelectionType: String.self, menu: { ids in menu(ids) }, primaryAction: { ids in
                if let r = refs.first(where: { ids.contains($0.id) }) { showLog(r) }
            })
            HStack {
                Text("\(shown.count) 件  (ダブルクリックでログ表示 / 右クリックで操作)").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("ブランチを作成…") { WindowManager.shared.perform(.branch, paths: [repo.root]) }
                Button("閉じる") { handle.close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(14)
        .onAppear(perform: load)
    }

    private func load() {
        refs = repo.refsDetailed()
        firstRemote = repo.remotes().first
    }

    private func showLog(_ r: RefDetail) {
        let repo = repo
        WindowManager.shared.show("GitVelto - ログ - \(r.name)", size: NSSize(width: 1080, height: 760)) { _ in
            LogView(repo: repo, paths: [], startRef: r.name)
        }
    }

    private func run(_ title: String, _ steps: [ProgressStep]) {
        WindowManager.shared.runProgress(title, dir: repo.root, steps: steps) { _ in DispatchQueue.main.async { load() } }
    }

    @ViewBuilder
    private func menu(_ ids: Set<String>) -> some View {
        let sel = refs.filter { ids.contains($0.id) }
        if sel.count == 1, let r = sel.first {
            Button("ログを表示") { showLog(r) }
            Button("切り替え/チェックアウト") {
                if r.kind == .local { run("切り替え", [.git(["switch", r.name])]) }
                else if r.kind == .remote {
                    let short = r.name.split(separator: "/").dropFirst().joined(separator: "/")
                    run("切り替え", [.git(["switch", "-c", short, "--track", r.name])])
                } else { run("切り替え", [.git(["switch", "--detach", r.name])]) }
            }.disabled(r.isHead)
            Button("現在のブランチにマージ") {
                if WindowManager.confirm("\(r.name) を現在のブランチにマージしますか?", ok: "マージ") {
                    run("マージ", [.git(["merge", "--no-edit", r.name])])
                }
            }.disabled(r.isHead)
            Button("現在のブランチをこの参照にリベース") {
                let repo = repo
                WindowManager.shared.show("GitVelto - リベース", size: NSSize(width: 760, height: 560)) { _ in RebaseView(repo: repo, initialUpstream: r.name) }
            }.disabled(r.isHead)
            Button("ここからブランチを作成…") {
                let repo = repo
                WindowManager.shared.show("GitVelto - ブランチを作成", size: NSSize(width: 520, height: 340)) { _ in CreateRefView(repo: repo, kind: .branch, base: r.name) }
            }
            Button("HEAD と比較") {
                let files = repo.changedFiles(from: r.name, to: "HEAD")
                let repo = repo
                WindowManager.shared.show("GitVelto - \(r.name) ↔ HEAD", size: NSSize(width: 640, height: 480)) { _ in
                    ChangedFilesView(repo: repo, files: files, from: r.name, to: "HEAD")
                }
            }
            Divider()
            if r.kind == .local {
                Button("名前を変更…") {
                    if let n = WindowManager.prompt("ブランチ名を変更", info: r.name, defaultValue: r.name), !n.isEmpty, n != r.name {
                        run("名前を変更", [.git(GitOps.renameBranch(r.name, to: n))])
                    }
                }
                Button("上流ブランチを設定…") {
                    if let n = WindowManager.prompt("上流ブランチ (例: origin/\(r.name))", defaultValue: r.upstream.isEmpty ? "origin/\(r.name)" : r.upstream), !n.isEmpty {
                        run("上流ブランチを設定", [.git(GitOps.setUpstream(n, for: r.name))])
                    }
                }
            }
            Button("参照名をコピー") { copyToPasteboard(r.name) }
            Button("ハッシュをコピー") { copyToPasteboard(r.hash) }
        }
        Divider()
        let locals = sel.filter { $0.kind == .local && !$0.isHead }
        let remotes = sel.filter { $0.kind == .remote }
        let tags = sel.filter { $0.kind == .tag }
        if !locals.isEmpty {
            Button("ブランチを削除 (\(locals.count))") {
                guard WindowManager.confirm("\(locals.map(\.name).joined(separator: ", ")) を削除しますか?", ok: "削除", destructive: true) else { return }
                let r = repo.git(GitOps.deleteBranches(locals.map(\.name), force: false))
                if !r.ok {
                    if WindowManager.confirm("マージされていないブランチがあります", info: r.message + "\n\n強制的に削除しますか?", ok: "強制削除", destructive: true) {
                        repo.git(GitOps.deleteBranches(locals.map(\.name), force: true))
                    }
                }
                load()
            }
        }
        if !remotes.isEmpty {
            Button("リモートブランチを削除 (\(remotes.count))") {
                guard WindowManager.confirm("リモートから \(remotes.map(\.name).joined(separator: ", ")) を削除しますか?", info: "サーバー上のブランチが削除されます。", ok: "削除", destructive: true) else { return }
                let steps = GitOps.deleteRemoteBranches(remotes.map(\.name))
                run("リモートブランチを削除", steps)
            }
        }
        if !tags.isEmpty {
            Button("タグを削除 (\(tags.count))") {
                guard WindowManager.confirm("タグ \(tags.map(\.name).joined(separator: ", ")) を削除しますか?", ok: "削除", destructive: true) else { return }
                run("タグを削除", [.git(GitOps.deleteTags(tags.map(\.name)))])
            }
            if let remote = firstRemote {
                Button("タグをリモートからも削除 (\(remote))") {
                    guard WindowManager.confirm("\(remote) からタグ \(tags.map(\.name).joined(separator: ", ")) を削除しますか?", ok: "削除", destructive: true) else { return }
                    run("リモートのタグを削除", [.git(GitOps.deleteRemoteTags(tags.map(\.name), remote: remote))])
                }
                Button("タグをプッシュ (\(remote))") {
                    run("タグをプッシュ", [.git(["push", "--progress", remote] + tags.map { "refs/tags/\($0.name)" })])
                }
            }
        }
    }
}

// MARK: - Reflog

struct ReflogView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State private var entries: [ReflogEntry] = []
    @State private var selection: Set<String> = []
    @State private var filter = ""

    private var shown: [ReflogEntry] {
        filter.isEmpty ? entries : entries.filter { $0.subject.localizedCaseInsensitiveContains(filter) || $0.hash.hasPrefix(filter) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            HStack {
                Text("HEAD の移動履歴 (Reflog)").font(.headline)
                Spacer()
                TextField("フィルタ", text: $filter).textFieldStyle(.roundedBorder).frame(width: 220)
                Button { entries = repo.reflog() } label: { Image(systemName: "arrow.clockwise") }
            }
            Table(shown, selection: $selection) {
                TableColumn("参照") { e in Text(e.selector).font(.system(size: 11, design: .monospaced)) }.width(90)
                TableColumn("ハッシュ") { e in Text(e.short).font(.system(size: 11, design: .monospaced)) }.width(80)
                TableColumn("操作") { e in Text(e.subject).lineLimit(1) }
                TableColumn("日時") { e in Text(relativeDateFormatter.string(from: e.date)).monospacedDigit() }.width(120)
            }
            .contextMenu(forSelectionType: String.self, menu: { ids in
                if let e = entries.first(where: { ids.contains($0.id) }) { menu(e) }
            }, primaryAction: { ids in
                if let e = entries.first(where: { ids.contains($0.id) }) { showFiles(e) }
            })
            HStack {
                Text("誤ってリセット/削除したコミットもここから復元できます。").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("閉じる") { handle.close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(14)
        .onAppear { entries = repo.reflog() }
    }

    private func commit(_ e: ReflogEntry) -> Commit {
        Commit(hash: e.hash, short: e.short, parents: [], author: "", email: "", date: e.date, refs: [], subject: e.subject, body: "")
    }

    private func showFiles(_ e: ReflogEntry) {
        let parent = repo.git(["rev-parse", "\(e.hash)^"]).out.trimmingCharacters(in: .whitespacesAndNewlines)
        let files = parent.isEmpty ? [] : repo.changedFiles(from: parent, to: e.hash)
        let repo = repo
        WindowManager.shared.show("GitVelto - \(e.short)", size: NSSize(width: 640, height: 480)) { _ in
            ChangedFilesView(repo: repo, files: files, from: parent, to: e.hash)
        }
    }

    @ViewBuilder
    private func menu(_ e: ReflogEntry) -> some View {
        let repo = repo
        Button("変更ファイルを表示") { showFiles(e) }
        Button("ハッシュをコピー") { copyToPasteboard(e.hash) }
        Divider()
        Button("ここからブランチを作成…") {
            WindowManager.shared.show("GitVelto - ブランチを作成", size: NSSize(width: 520, height: 340)) { _ in CreateRefView(repo: repo, kind: .branch, base: e.hash) }
        }
        Button("このコミットに切り替え (HEAD 切り離し)") {
            WindowManager.shared.runProgress("チェックアウト", dir: repo.root, steps: [.git(["switch", "--detach", e.hash])])
        }
        Button("このコミットまでリセット…") {
            let c = commit(e)
            WindowManager.shared.show("GitVelto - リセット", size: NSSize(width: 480, height: 280)) { _ in ResetView(repo: repo, commit: c) {} }
        }
    }
}

// MARK: - Repository browser

struct RepoBrowserView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State var revision: String
    @State private var revInput = ""
    @State var path: String
    @State private var entries: [TreeEntry] = []
    @State private var selection: Set<String> = []
    @State private var error = ""

    init(repo: Repo, revision: String = "HEAD", path: String = "") {
        self.repo = repo
        _revision = State(initialValue: revision)
        _path = State(initialValue: path)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("リビジョン:")
                TextField("HEAD / ブランチ / タグ / ハッシュ", text: $revInput).frame(width: 260).onSubmit { revision = revInput; path = ""; load() }
                Button("表示") { revision = revInput; path = ""; load() }
                Spacer()
                Text(repo.name).foregroundStyle(.secondary)
            }
            HStack(spacing: 2) {
                Button { path = ""; load() } label: { Label(repo.name, systemImage: "house") }.buttonStyle(.borderless)
                let comps = path.split(separator: "/").map(String.init)
                ForEach(Array(comps.enumerated()), id: \.offset) { i, c in
                    Image(systemName: "chevron.right").font(.caption).foregroundStyle(.secondary)
                    Button(c) { path = comps.prefix(i + 1).joined(separator: "/"); load() }.buttonStyle(.borderless)
                }
                Spacer()
                Button { goUp() } label: { Image(systemName: "arrow.up") }.disabled(path.isEmpty).help("上の階層へ")
            }
            if !error.isEmpty { Text(error).foregroundStyle(.red) }
            Table(entries, selection: $selection) {
                TableColumn("名前") { e in
                    HStack(spacing: 6) {
                        Image(nsImage: e.isDir ? FileIcons.folder : FileIcons.icon(for: e.name)).resizable().frame(width: 16, height: 16)
                        Text(e.name)
                        if e.isSubmodule { Text("(サブモジュール)").foregroundStyle(.secondary) }
                    }
                }
                TableColumn("サイズ") { e in Text(e.size >= 0 ? ByteCountFormatter.string(fromByteCount: Int64(e.size), countStyle: .file) : "").monospacedDigit() }.width(90)
                TableColumn("ハッシュ") { e in Text(String(e.hash.prefix(10))).font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary) }.width(90)
            }
            .contextMenu(forSelectionType: String.self, menu: { ids in
                if let e = entries.first(where: { ids.contains($0.id) }) { menu(e) }
            }, primaryAction: { ids in
                if let e = entries.first(where: { ids.contains($0.id) }) { open(e) }
            })
            HStack {
                Text("\(entries.count) 項目 @ \(revision)").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("閉じる") { handle.close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(14)
        .onAppear { revInput = revision; load() }
    }

    private func load() {
        let r = repo.git(["rev-parse", "--verify", "-q", revision + "^{commit}"])
        if !r.ok { error = "リビジョン '\(revision)' が見つかりません"; entries = []; return }
        error = ""
        entries = repo.lsTree(rev: revision, path: path)
        selection = []
    }

    private func goUp() {
        path = (path as NSString).deletingLastPathComponent
        load()
    }

    private func open(_ e: TreeEntry) {
        if e.isDir { path = e.path; load(); return }
        if let f = repo.exportBlob(rev: revision, path: e.path) { openFile(f) }
    }

    @ViewBuilder
    private func menu(_ e: TreeEntry) -> some View {
        let repo = repo
        Button(e.isDir ? "開く" : "開く (一時ファイル)") { open(e) }
        if !e.isDir {
            Button("名前を付けて保存…") {
                let p = NSSavePanel()
                p.nameFieldStringValue = e.name
                if p.runModal() == .OK, let url = p.url {
                    let r = repo.git(["show", "\(revision):\(e.path)"])
                    do { try r.stdout.write(to: url) } catch { WindowManager.alert("保存できません", info: error.localizedDescription) }
                }
            }
            Button("Blame") {
                let rev = revision
                WindowManager.shared.show("GitVelto - Blame - \(e.name)", size: NSSize(width: 1000, height: 720)) { _ in
                    BlameView(repo: repo, path: repo.absolute(e.path), revision: rev)
                }
            }
            Button("作業ツリーと比較") {
                WindowManager.shared.showDiff(repo: repo, source: .revToWorking(rev: revision, path: e.path))
            }
        }
        Button("ログを表示") {
            let rev = revision
            WindowManager.shared.show("GitVelto - ログ - \(e.name)", size: NSSize(width: 1080, height: 760)) { _ in
                LogView(repo: repo, paths: [repo.absolute(e.path)], startRef: rev)
            }
        }
        Button("パスをコピー") { copyToPasteboard(e.path) }
        Button("ハッシュをコピー") { copyToPasteboard(e.hash) }
    }
}
