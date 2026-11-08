import SwiftUI

// MARK: - Add / Revert / Resolve

struct FileActionView: View {
    enum Mode { case add, revert, resolve }
    let mode: Mode
    @StateObject private var model: StatusModel
    @EnvironmentObject var handle: WindowHandle

    init(repo: Repo, paths: [String], mode: Mode) {
        self.mode = mode
        _model = StateObject(wrappedValue: StatusModel(repo: repo, paths: paths))
    }

    private var filtered: [StatusEntry] {
        switch mode {
        case .add: return model.entries.filter { $0.state == .untracked || $0.state == .ignored }
        case .revert: return model.entries.filter { $0.state.isTracked }
        case .resolve: return model.entries.filter { $0.state == .conflict }
        }
    }

    private var description: String {
        switch mode {
        case .add: return "追加するファイルを選択してください"
        case .revert: return "元に戻すファイルを選択してください (変更は失われます)"
        case .resolve: return "解決済みにするファイルを選択してください"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: model.repo)
            HStack {
                Text(description).font(.headline)
                if model.loading { ProgressView().controlSize(.small) }
                Spacer()
                if mode == .add { Toggle("無視されたファイルも表示", isOn: $model.showIgnored) }
                Button("全選択") { model.checked = Set(filtered.map(\.id)) }
                Button("全解除") { model.checked = [] }
            }
            FileTable(entries: filtered, checked: $model.checked, selection: $model.selection,
                      onOpen: { e in
                          if mode == .resolve { WindowManager.shared.perform(.editConflict, paths: [model.repo.absolute(e.path)]) } else { model.openDiff(e) }
                      }) { ids in
                if mode == .resolve {
                    let sel = model.entries(for: ids)
                    if sel.count == 1 { Button("競合を編集…") { WindowManager.shared.perform(.editConflict, paths: [model.repo.absolute(sel[0].path)]) } }
                    Button("自分の変更を使用 (ours)") { checkout(sel, "--ours") }
                    Button("相手の変更を使用 (theirs)") { checkout(sel, "--theirs") }
                    Button("エディタで開く") { sel.forEach { openFile(model.repo.absolute($0.path)) } }
                    Button("差分を表示") { if let e = sel.first { model.openDiff(e) } }
                } else {
                    WorkingTreeMenu(model: model, ids: ids)
                }
            }
            HStack {
                if mode == .resolve { operationButtons }
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button(okTitle) { run() }.keyboardShortcut(.defaultAction)
                    .disabled(filtered.filter { model.checked.contains($0.id) }.isEmpty)
            }
        }
        .padding(14)
        .onAppear {
            if mode == .add { model.showUntracked = true }
            model.reload()
        }
        .onChange(of: model.entries) {
            // Check everything that applies by default.
            model.checked = Set(filtered.map(\.id))
        }
    }

    @ViewBuilder
    private var operationButtons: some View {
        let st = model.opState
        if let cmd = st.command {
            Text(st.label).foregroundStyle(.orange)
            if st != .merging {
                Button("続行") {
                    WindowManager.shared.runProgress("\(st.label)の続行", dir: model.repo.root, steps: [.git([cmd, "--continue"])])
                    handle.close()
                }
            }
            Button("中止 (abort)") {
                if WindowManager.confirm("\(st.label)を中止しますか?", info: "操作前の状態に戻ります。", ok: "中止", destructive: true) {
                    WindowManager.shared.runProgress("\(st.label)の中止", dir: model.repo.root, steps: [.git([cmd, "--abort"])])
                    handle.close()
                }
            }
        }
    }

    private var okTitle: String {
        switch mode { case .add: return "追加"; case .revert: return "元に戻す"; case .resolve: return "解決済みにする" }
    }

    private func checkout(_ sel: [StatusEntry], _ side: String) {
        let rels = sel.map(\.path)
        let r = model.repo.git(["--literal-pathspecs", "checkout", side, "--"] + rels)
        if r.ok { model.repo.git(["--literal-pathspecs", "add", "--"] + rels) }
        else { WindowManager.alert("失敗しました", info: r.message) }
        model.reload()
    }

    private func run() {
        let repo = model.repo
        let sel = filtered.filter { model.checked.contains($0.id) }
        var steps: [ProgressStep] = []
        switch mode {
        case .add:
            let rels = sel.map(\.path)
            if let f = repo.pathspecFile(rels) {
                steps.append(.git(["--literal-pathspecs", "add", "-f", "--pathspec-from-file=\(f)", "--pathspec-file-nul"]))
            }
        case .revert:
            guard WindowManager.confirm("\(sel.count) 個のファイルの変更を元に戻しますか?", info: "この操作は取り消せません。", ok: "元に戻す", destructive: true) else { return }
            steps = GitOps.revertSteps(repo: repo, entries: sel)
        case .resolve:
            let rels = sel.map(\.path)
            if let f = repo.pathspecFile(rels) {
                steps.append(.git(["--literal-pathspecs", "add", "--pathspec-from-file=\(f)", "--pathspec-file-nul"]))
            }
        }
        let title = okTitle
        let root = repo.root
        let isResolve = mode == .resolve
        WindowManager.shared.runProgress(title, dir: root, steps: steps,
                                         successActions: isResolve ? [ProgressAction(title: "コミット…") {
                                             WindowManager.shared.perform(.commit, paths: [root])
                                         }] : [])
        handle.close()
    }
}

// MARK: - Delete

struct DeleteView: View {
    let repo: Repo
    let paths: [String]
    var keepLocalDefault = false
    @EnvironmentObject var handle: WindowHandle
    @State private var keepLocal = false
    @State private var force = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("次の項目を Git から削除します").font(.headline)
            List(paths, id: \.self) { p in Text(repo.relative(p)) }
            Toggle("ローカルのファイルは残す (--cached)", isOn: $keepLocal)
            Toggle("変更があっても削除する (-f)", isOn: $force)
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("削除") {
                    var args = ["--literal-pathspecs", "rm", "-r"]
                    if keepLocal { args.append("--cached") }
                    if force { args.append("-f") }
                    args.append("--")
                    args += paths.map { repo.relative($0) }
                    WindowManager.shared.runProgress("削除", dir: repo.root, steps: [.git(args)])
                    handle.close()
                }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(14)
        .onAppear { keepLocal = keepLocalDefault }
    }
}

// MARK: - Rename

struct RenameView: View {
    let repo: Repo
    let path: String
    @EnvironmentObject var handle: WindowHandle
    @State private var newName = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("\(repo.relative(path)) の新しい名前").font(.headline)
            TextField("新しい名前 (フォルダを含めて指定すると移動)", text: $newName)
            Spacer()
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("名前を変更") {
                    let dir = (repo.relative(path) as NSString).deletingLastPathComponent
                    let target = newName.contains("/") ? newName : (dir.isEmpty ? newName : dir + "/" + newName)
                    let r = repo.git(["mv", "--", repo.relative(path), target])
                    if r.ok {
                        StatusService.shared.invalidate(path: repo.root)
                        handle.close()
                    } else {
                        WindowManager.alert("名前を変更できません", info: r.message)
                    }
                }.keyboardShortcut(.defaultAction).disabled(newName.isEmpty)
            }
        }
        .padding(16)
        .onAppear { newName = (path as NSString).lastPathComponent }
    }
}

// MARK: - Ignore

struct IgnoreView: View {
    let repo: Repo
    let paths: [String]
    @EnvironmentObject var handle: WindowHandle
    enum How: String, CaseIterable { case path = "パスで指定", name = "名前で指定", ext = "拡張子で指定" }
    @State private var how: How = .path
    @State private var untrack = true
    @State private var target = 0 // 0 = root .gitignore, 1 = folder .gitignore, 2 = .git/info/exclude

    private var patterns: [String] {
        var out: [String] = []
        for p in paths {
            let rel = repo.relative(p)
            let isDir = (try? URL(fileURLWithPath: p).resourceValues(forKeys: [.isDirectoryKey]).isDirectory) ?? false
            let name = (rel as NSString).lastPathComponent
            switch how {
            case .path:
                if target == 1 { out.append("/" + name + (isDir ? "/" : "")) }
                else { out.append("/" + rel + (isDir ? "/" : "")) }
            case .name: out.append(name + (isDir ? "/" : ""))
            case .ext:
                let ext = (name as NSString).pathExtension
                out.append(ext.isEmpty ? name : "*." + ext)
            }
        }
        var seen = Set<String>()
        return out.filter { seen.insert($0).inserted }
    }

    private var targetFile: String {
        switch target {
        case 1:
            let first = paths.first.map { ($0 as NSString).deletingLastPathComponent } ?? repo.root
            return first + "/.gitignore"
        case 2: return repo.gitDir + "/info/exclude"
        default: return repo.root + "/.gitignore"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Form {
                Picker("指定方法", selection: $how) {
                    ForEach(How.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                }.pickerStyle(.segmented)
                Picker("追加先", selection: $target) {
                    Text("リポジトリ直下の .gitignore").tag(0)
                    Text("同じフォルダの .gitignore").tag(1)
                    Text(".git/info/exclude (共有しない)").tag(2)
                }
                Toggle("追跡中の場合はインデックスから削除 (git rm --cached)", isOn: $untrack)
            }
            Text("追加されるパターン:").font(.headline)
            Text(patterns.joined(separator: "\n"))
                .font(.system(size: 12, design: .monospaced))
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .padding(6)
                .background(Color(nsColor: .textBackgroundColor))
            Text(targetFile).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.head)
            HStack {
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("追加") { run() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(14)
    }

    private func run() {
        let file = targetFile
        var content = (try? String(contentsOfFile: file, encoding: .utf8)) ?? ""
        let existing = Set(content.split(separator: "\n").map { $0.trimmingCharacters(in: .whitespaces) })
        let add = patterns.filter { !existing.contains($0) }
        if !add.isEmpty {
            if !content.isEmpty && !content.hasSuffix("\n") { content += "\n" }
            content += add.joined(separator: "\n") + "\n"
            do {
                try FileManager.default.createDirectory(atPath: (file as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
                try content.write(toFile: file, atomically: true, encoding: .utf8)
            } catch {
                WindowManager.alert("書き込めません", info: error.localizedDescription); return
            }
        }
        if untrack {
            let rels = paths.map { repo.relative($0) }
            repo.git(["--literal-pathspecs", "rm", "-r", "--cached", "-q", "--ignore-unmatch", "--"] + rels)
        }
        StatusService.shared.invalidate(path: repo.root)
        handle.close()
    }
}

// MARK: - Clean up

struct CleanView: View {
    let repo: Repo
    let paths: [String]
    @EnvironmentObject var handle: WindowHandle
    @EnvironmentObject var settings: AppSettings
    enum Scope: String, CaseIterable { case untracked = "未追跡のみ", ignored = "無視のみ", both = "未追跡+無視" }
    @State private var scope: Scope = .untracked
    @State private var dirs = true
    @State private var items: [String] = []
    @State private var checked: Set<String> = []
    @State private var loading = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            RepoHeader(repo: repo)
            HStack {
                Picker("対象", selection: $scope) {
                    ForEach(Scope.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                }.pickerStyle(.segmented).frame(width: 320)
                Toggle("フォルダも含める", isOn: $dirs)
                Spacer()
                if loading { ProgressView().controlSize(.small) }
            }
            List(items, id: \.self) { p in
                Toggle(isOn: Binding(get: { checked.contains(p) }, set: { v in if v { checked.insert(p) } else { checked.remove(p) } })) {
                    HStack {
                        Image(nsImage: p.hasSuffix("/") ? FileIcons.folder : FileIcons.icon(for: p)).resizable().frame(width: 16, height: 16)
                        Text(p)
                    }
                }.toggleStyle(.checkbox)
            }
            HStack {
                Text("\(checked.count) / \(items.count) 件").foregroundStyle(.secondary)
                Toggle("ゴミ箱に移動する", isOn: $settings.cleanToTrash)
                Spacer()
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("削除") { run() }.keyboardShortcut(.defaultAction).disabled(checked.isEmpty)
            }
        }
        .padding(14)
        .onAppear(perform: preview)
        .onChange(of: scope) { preview() }
        .onChange(of: dirs) { preview() }
    }

    private func preview() {
        loading = true
        var args = ["clean", "-n"]
        if dirs { args.append("-d") }
        switch scope {
        case .untracked: break
        case .ignored: args.append("-X")
        case .both: args.append("-x")
        }
        let rels = paths.map { repo.relative($0) }.filter { !$0.isEmpty }
        if !rels.isEmpty { args.append("--"); args += rels }
        let r = repo.git(args)
        items = r.out.split(separator: "\n").compactMap { l in
            let s = String(l)
            return s.hasPrefix("Would remove ") ? String(s.dropFirst("Would remove ".count)) : nil
        }
        checked = Set(items)
        loading = false
    }

    private func run() {
        let sel = items.filter { checked.contains($0) }
        guard WindowManager.confirm("\(sel.count) 件を削除しますか?", info: settings.cleanToTrash ? "ゴミ箱に移動します。" : "完全に削除されます。", ok: "削除", destructive: !settings.cleanToTrash) else { return }
        let root = repo.root
        if settings.cleanToTrash {
            WindowManager.shared.runProgress("クリーンアップ", dir: root, steps: [.custom("ゴミ箱に移動しています…") {
                var out: [String] = []
                for rel in sel {
                    let url = URL(fileURLWithPath: root + "/" + rel.trimmedPath)
                    try FileManager.default.trashItem(at: url, resultingItemURL: nil)
                    out.append("ゴミ箱へ: \(rel)")
                }
                return out.joined(separator: "\n")
            }])
        } else {
            var args = ["clean", "-f"]
            if dirs { args.append("-d") }
            switch scope {
            case .untracked: break
            case .ignored: args.append("-X")
            case .both: args.append("-x")
            }
            args.append("--")
            args += sel
            WindowManager.shared.runProgress("クリーンアップ", dir: root, steps: [.git(args)])
        }
        handle.close()
    }
}
