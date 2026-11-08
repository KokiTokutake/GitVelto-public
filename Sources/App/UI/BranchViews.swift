import SwiftUI

// MARK: - Pull / Fetch / Push

struct RemoteOpView: View {
    enum Mode { case pull, fetch, push }
    let repo: Repo
    let mode: Mode
    @EnvironmentObject var handle: WindowHandle

    @State private var remotes: [String] = []
    @State private var refs: [RefInfo] = []
    @State private var remote = ""
    @State private var localBranch = ""
    @State private var remoteBranch = ""
    @State private var allRemotes = false
    @State private var rebase = false
    @State private var autostash = true
    @State private var prune = false
    @State private var tags = false
    @State private var setUpstream = false
    @State private var force = false
    @State private var ffOnly = false
    @State private var allBranches = false
    @State private var newRemoteName = "origin"
    @State private var newRemoteURL = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            if remotes.isEmpty {
                GroupBox("リモートが登録されていません") {
                    VStack(alignment: .leading) {
                        TextField("名前", text: $newRemoteName)
                        TextField("URL (https://… / git@…)", text: $newRemoteURL)
                        Button("リモートを追加") {
                            let r = repo.git(["remote", "add", newRemoteName, newRemoteURL])
                            if !r.ok { WindowManager.alert("リモートを追加できません", info: r.message) }
                            load()
                        }.disabled(newRemoteName.isEmpty || newRemoteURL.isEmpty)
                    }.padding(4)
                }
            } else {
                Form {
                    if mode == .push {
                        Picker("ローカルブランチ", selection: $localBranch) {
                            ForEach(refs.filter { $0.kind == .local }) { Text($0.name).tag($0.name) }
                        }
                        .onChange(of: localBranch) { updateRemoteBranchDefault() }
                    }
                    HStack {
                        Picker("リモート", selection: $remote) {
                            ForEach(remotes, id: \.self) { Text($0).tag($0) }
                        }.disabled(mode == .fetch && allRemotes)
                        Button("管理…") { WindowManager.shared.perform(.remotes, paths: [repo.root]) }
                    }
                    if mode != .fetch {
                        HStack {
                            TextField("リモートブランチ", text: $remoteBranch)
                            Menu("") {
                                ForEach(refs.filter { $0.kind == .remote && $0.name.hasPrefix(remote + "/") }) { r in
                                    let short = String(r.name.dropFirst(remote.count + 1))
                                    Button(short) { remoteBranch = short }
                                }
                            }.menuStyle(.borderlessButton).frame(width: 24)
                        }
                    }
                    switch mode {
                    case .pull:
                        Toggle("リベースする (--rebase)", isOn: $rebase)
                        Toggle("早送りのみ (--ff-only)", isOn: $ffOnly).disabled(rebase)
                        Toggle("ローカルの変更を自動退避 (--autostash)", isOn: $autostash)
                        Toggle("削除されたリモートブランチを整理 (--prune)", isOn: $prune)
                    case .fetch:
                        Toggle("すべてのリモート (--all)", isOn: $allRemotes)
                        Toggle("削除されたリモートブランチを整理 (--prune)", isOn: $prune)
                        Toggle("すべてのタグを取得 (--tags)", isOn: $tags)
                    case .push:
                        Toggle("すべてのブランチをプッシュ (--all)", isOn: $allBranches)
                        Toggle("上流ブランチとして設定 (-u)", isOn: $setUpstream)
                        Toggle("強制プッシュ (--force-with-lease)", isOn: $force)
                        Toggle("タグもプッシュ (--tags)", isOn: $tags)
                    }
                }
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button(okTitle) { run() }.keyboardShortcut(.defaultAction).disabled(remotes.isEmpty)
            }
        }
        .padding(16)
        .onAppear { load() }
    }

    private var okTitle: String {
        switch mode { case .pull: return "プル"; case .fetch: return "フェッチ"; case .push: return "プッシュ" }
    }

    private func load() {
        remotes = repo.remotes()
        refs = repo.refs()
        let cur = repo.currentBranch() ?? ""
        localBranch = cur
        if let up = repo.upstream(of: cur) {
            remote = up.remote
            remoteBranch = up.branch
        } else {
            remote = remotes.contains("origin") ? "origin" : (remotes.first ?? "")
            remoteBranch = cur
            setUpstream = true
        }
    }

    private func updateRemoteBranchDefault() {
        if let up = repo.upstream(of: localBranch) {
            remote = up.remote; remoteBranch = up.branch; setUpstream = false
        } else {
            remoteBranch = localBranch; setUpstream = true
        }
    }

    private func run() {
        var args: [String]
        let title: String
        switch mode {
        case .pull:
            title = "プル"
            args = ["pull", "--progress", rebase ? "--rebase" : "--no-rebase"]
            if autostash { args.append("--autostash") }
            if prune { args.append("--prune") }
            if ffOnly && !rebase { args.append("--ff-only") }
            args.append(remote)
            if !remoteBranch.isEmpty { args.append(remoteBranch) }
        case .fetch:
            title = "フェッチ"
            args = ["fetch", "--progress"]
            if prune { args.append("--prune") }
            if tags { args.append("--tags") }
            args.append(allRemotes ? "--all" : remote)
        case .push:
            title = "プッシュ"
            guard !localBranch.isEmpty else { return }
            if force && !WindowManager.confirm("強制プッシュしますか?", info: "リモートの履歴が上書きされる可能性があります。", ok: "強制プッシュ", destructive: true) { return }
            args = ["push", "--progress"]
            if setUpstream { args.append("-u") }
            if force { args.append("--force-with-lease") }
            if tags { args.append("--tags") }
            if allBranches {
                args += ["--all", remote]
            } else {
                args += [remote, "\(localBranch):\(remoteBranch.isEmpty ? localBranch : remoteBranch)"]
            }
        }
        let root = repo.root
        WindowManager.shared.runProgress(title, dir: root, steps: [.git(args)],
                                         successActions: [ProgressAction(title: "ログを表示") { WindowManager.shared.perform(.log, paths: [root]) }])
        handle.close()
    }
}

// MARK: - Switch / Checkout

struct SwitchView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    enum Kind: String, CaseIterable { case branch = "ブランチ", tag = "タグ", commit = "コミット" }
    @State private var refs: [RefInfo] = []
    @State private var kind: Kind = .branch
    @State private var target = ""
    @State private var commit = ""
    @State private var createBranch = false
    @State private var newBranch = ""
    @State private var force = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            Form {
                Picker("種類", selection: $kind) {
                    ForEach(Kind.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                }.pickerStyle(.segmented)
                switch kind {
                case .branch:
                    RefPicker(title: "切り替え先", refs: refs, selection: $target, includeTags: false)
                        .onChange(of: target) { updateDefaults() }
                case .tag:
                    RefPicker(title: "タグ", refs: refs, selection: $target, includeLocal: false, includeRemote: false)
                case .commit:
                    TextField("コミット (ハッシュ等)", text: $commit)
                }
                Toggle("新しいブランチを作成", isOn: $createBranch)
                if createBranch { TextField("新しいブランチ名", text: $newBranch) }
                Toggle("ローカルの変更を破棄して切り替え (--discard-changes)", isOn: $force)
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("切り替え") { run() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(16)
        .onAppear {
            refs = repo.refs()
            target = refs.first { $0.kind == .local && !$0.isHead }?.name ?? refs.first?.name ?? ""
        }
    }

    private var isRemote: Bool { refs.first { $0.name == target }?.kind == .remote }

    private func updateDefaults() {
        if isRemote {
            let short = target.split(separator: "/").dropFirst().joined(separator: "/")
            let exists = refs.contains { $0.kind == .local && $0.name == short }
            createBranch = !exists
            newBranch = short
        } else {
            createBranch = false
        }
    }

    private func run() {
        let ref = kind == .commit ? commit.trimmingCharacters(in: .whitespaces) : target
        guard !ref.isEmpty else { return }
        var args = ["switch"]
        if force { args.append("--discard-changes") }
        if createBranch {
            guard !newBranch.isEmpty else { WindowManager.alert("ブランチ名を入力してください"); return }
            args += ["-c", newBranch]
            if isRemote { args.append("--track") }
            args.append(ref)
        } else if kind == .branch && !isRemote {
            args.append(ref)
        } else {
            args += ["--detach", ref]
        }
        WindowManager.shared.runProgress("切り替え", dir: repo.root, steps: [.git(args)])
        handle.close()
    }
}

// MARK: - Create branch / tag

struct CreateRefView: View {
    enum Kind { case branch, tag }
    let repo: Repo
    let kind: Kind
    let base: String?
    @EnvironmentObject var handle: WindowHandle
    @State private var refs: [RefInfo] = []
    @State private var name = ""
    @State private var baseKind = 0 // 0 HEAD, 1 ref, 2 commit
    @State private var baseRef = ""
    @State private var baseCommit = ""
    @State private var switchAfter = true
    @State private var force = false
    @State private var message = ""
    @State private var pushTag = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            Form {
                TextField(kind == .branch ? "ブランチ名" : "タグ名", text: $name)
                Picker("作成元", selection: $baseKind) {
                    Text("HEAD (現在の作業コピー)").tag(0)
                    Text("ブランチ/タグ").tag(1)
                    Text("コミット").tag(2)
                }.pickerStyle(.radioGroup)
                if baseKind == 1 { RefPicker(title: "参照", refs: refs, selection: $baseRef) }
                if baseKind == 2 { TextField("コミット", text: $baseCommit) }
                if kind == .branch {
                    Toggle("作成後に新しいブランチへ切り替え", isOn: $switchAfter)
                    Toggle("既存のブランチを上書き (-f)", isOn: $force)
                } else {
                    TextField("メッセージ (入力すると注釈付きタグ)", text: $message, axis: .vertical).lineLimit(2...4)
                    Toggle("既存のタグを上書き (-f)", isOn: $force)
                    Toggle("作成後にリモートへプッシュ", isOn: $pushTag)
                }
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("作成") { run() }.keyboardShortcut(.defaultAction).disabled(name.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(16)
        .onAppear {
            refs = repo.refs()
            baseRef = refs.first?.name ?? ""
            if let base { baseKind = 2; baseCommit = base }
        }
    }

    private func run() {
        let n = name.trimmingCharacters(in: .whitespaces)
        let start: String? = baseKind == 0 ? nil : (baseKind == 1 ? baseRef : baseCommit)
        var steps: [ProgressStep] = []
        if kind == .branch {
            if switchAfter {
                steps.append(.git(["switch", force ? "-C" : "-c", n] + (start.map { [$0] } ?? [])))
            } else {
                steps.append(.git(["branch"] + (force ? ["-f"] : []) + [n] + (start.map { [$0] } ?? [])))
            }
        } else {
            var args = ["tag"]
            if force { args.append("-f") }
            if !message.isEmpty { args += ["-a", "-m", message] }
            args.append(n)
            if let start { args.append(start) }
            steps.append(.git(args))
            if pushTag, let remote = repo.remotes().first {
                steps.append(.git(["push", "--progress", remote, "refs/tags/\(n)"] + (force ? ["--force"] : [])))
            }
        }
        WindowManager.shared.runProgress(kind == .branch ? "ブランチを作成" : "タグを作成", dir: repo.root, steps: steps)
        handle.close()
    }
}

// MARK: - Merge

struct MergeView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State private var refs: [RefInfo] = []
    @State private var target = ""
    @State private var noFF = false
    @State private var squash = false
    @State private var noCommit = false
    @State private var message = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            Form {
                RefPicker(title: "マージ元", refs: refs.filter { !$0.isHead }, selection: $target)
                Toggle("早送りしない (--no-ff)", isOn: $noFF)
                Toggle("スカッシュ (--squash)", isOn: $squash)
                Toggle("コミットしない (--no-commit)", isOn: $noCommit)
                TextField("マージメッセージ (省略可)", text: $message, axis: .vertical).lineLimit(2...5)
            }
            Text("競合が発生した場合は「競合を解決」から解決後にコミットしてください。").font(.caption).foregroundStyle(.secondary)
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("マージ") { run() }.keyboardShortcut(.defaultAction).disabled(target.isEmpty)
            }
        }
        .padding(16)
        .onAppear {
            refs = repo.refs()
            target = refs.first { !$0.isHead && $0.kind == .local }?.name ?? refs.first { !$0.isHead }?.name ?? ""
        }
    }

    private func run() {
        var args = ["merge", "--progress", "--no-edit"]
        if noFF { args.append("--no-ff") }
        if squash { args.append("--squash") }
        if noCommit { args.append("--no-commit") }
        if !message.isEmpty { args += ["-m", message] }
        args.append(target)
        let root = repo.root
        WindowManager.shared.runProgress("マージ", dir: root, steps: [.git(args)],
                                         successActions: [ProgressAction(title: "ログを表示") { WindowManager.shared.perform(.log, paths: [root]) }]) { ok in
            if !ok && Repo(root: root).operationState() == .merging {
                DispatchQueue.main.async {
                    if WindowManager.confirm("マージで競合が発生しました", info: "競合の解決画面を開きますか?", ok: "開く") {
                        WindowManager.shared.perform(.resolve, paths: [root])
                    }
                }
            }
        }
        handle.close()
    }
}

// MARK: - Stash

struct StashSaveView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State private var message = ""
    @State private var untracked = false
    @State private var keepIndex = false

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            RepoHeader(repo: repo)
            Form {
                TextField("メッセージ (省略可)", text: $message)
                Toggle("未追跡ファイルも含める (-u)", isOn: $untracked)
                Toggle("ステージ済みの変更は残す (--keep-index)", isOn: $keepIndex)
            }
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("退避") {
                    var args = ["stash", "push"]
                    if untracked { args.append("-u") }
                    if keepIndex { args.append("--keep-index") }
                    if !message.isEmpty { args += ["-m", message] }
                    WindowManager.shared.runProgress("変更を退避", dir: repo.root, steps: [.git(args)])
                    handle.close()
                }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(16)
    }
}

struct StashListView: View {
    let repo: Repo
    @EnvironmentObject var handle: WindowHandle
    @State private var stashes: [StashEntry] = []
    @State private var selected: String?
    @State private var files: [ChangedFile] = []
    @State private var fileSel: Set<String> = []

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            HSplitView {
                List(stashes, selection: $selected) { s in
                    VStack(alignment: .leading) {
                        Text(s.message).lineLimit(1)
                        Text("\(s.ref)  \(relativeDateFormatter.string(from: s.date))").font(.caption).foregroundStyle(.secondary)
                    }.tag(s.ref)
                }
                .frame(minWidth: 260)
                Table(files, selection: $fileSel) {
                    TableColumn("状態") { f in Text(f.state.label).foregroundStyle(f.state.color) }.width(70)
                    TableColumn("パス") { f in Text(f.path) }
                }
                .contextMenu(forSelectionType: String.self, menu: { _ in EmptyView() }, primaryAction: { ids in
                    if let s = selected, let f = files.first(where: { ids.contains($0.id) }) {
                        WindowManager.shared.showDiff(repo: repo, source: .range(from: "\(s)^1", to: s, path: f.path, oldPath: f.oldPath))
                    }
                })
                .frame(minWidth: 280)
            }
            HStack {
                Button("適用") { act(["stash", "apply"], "退避を適用") }.disabled(selected == nil)
                Button("復元して削除 (pop)") { act(["stash", "pop"], "退避を復元") }.disabled(selected == nil)
                Button("削除") {
                    if let s = selected, WindowManager.confirm("\(s) を削除しますか?", ok: "削除", destructive: true) {
                        act(["stash", "drop"], "退避を削除")
                    }
                }.disabled(selected == nil)
                Spacer()
                Button("閉じる") { handle.close() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(14)
        .onAppear(perform: load)
        .onRepoChanged(repo.root) { load() }
        .onChange(of: selected) {
            guard let s = selected else { files = []; return }
            files = repo.changedFiles(from: "\(s)^1", to: s)
        }
    }

    private func load() {
        stashes = repo.stashes()
        if selected == nil { selected = stashes.first?.ref }
    }

    private func act(_ args: [String], _ title: String) {
        guard let s = selected else { return }
        WindowManager.shared.runProgress(title, dir: repo.root, steps: [.git(args + [s])]) { _ in
            DispatchQueue.main.async { selected = nil; load() }
        }
    }
}
