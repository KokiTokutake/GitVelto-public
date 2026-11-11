import SwiftUI

enum DiffSource {
    case workingTree(path: String, origPath: String?, state: FileState)
    case commit(hash: String, parent: String?, path: String, oldPath: String?)
    case range(from: String, to: String, path: String, oldPath: String?)
    case revToWorking(rev: String, path: String)
    case files(left: String, right: String)

    var path: String {
        switch self {
        case .workingTree(let p, _, _), .commit(_, _, let p, _), .range(_, _, let p, _), .revToWorking(_, let p): return p
        case .files(_, let r): return r
        }
    }

    var subtitle: String {
        switch self {
        case .workingTree: return "HEAD ↔ 作業ツリー"
        case .commit(let h, let p, _, _): return "\(p.map { String($0.prefix(8)) } ?? "(なし)") ↔ \(h.prefix(8))"
        case .range(let a, let b, _, _): return "\(a.prefix(12)) ↔ \(b.prefix(12))"
        case .revToWorking(let r, _): return "\(r.prefix(12)) ↔ 作業ツリー"
        case .files(let l, _): return "\((l as NSString).lastPathComponent) ↔"
        }
    }

    func args(repo: Repo, ignoreWhitespace: Bool) -> [String] {
        let ws = ignoreWhitespace ? ["-w"] : []
        switch self {
        case .workingTree(let p, let o, let state):
            if state == .untracked || (!repo.hasHead() && state != .deleted) {
                return ["diff", "--no-index"] + ws + ["--", "/dev/null", p]
            }
            return ["--literal-pathspecs", "diff", "-M", "HEAD"] + ws + ["--"] + [o, p].compactMap { $0 }
        case .commit(let h, let parent, let p, let o):
            if let parent {
                return ["--literal-pathspecs", "diff", "-M", parent, h] + ws + ["--"] + [o, p].compactMap { $0 }
            }
            return ["--literal-pathspecs", "show", "--format=", h] + ws + ["--", p]
        case .range(let a, let b, let p, let o):
            return ["--literal-pathspecs", "diff", "-M", a, b] + ws + ["--"] + [o, p].compactMap { $0 }
        case .revToWorking(let r, let p):
            return ["--literal-pathspecs", "diff", r] + ws + ["--", p]
        case .files(let l, let r):
            return ["diff", "--no-index"] + ws + ["--", l, r]
        }
    }

    /// Left/right files for an external diff tool.
    func externalFiles(repo: Repo) -> (String, String) {
        func blob(_ rev: String, _ p: String) -> String { repo.exportBlob(rev: rev, path: p) ?? ExternalTools.emptyFile() }
        switch self {
        case .workingTree(let p, let o, let state):
            let left = state == .untracked || state == .added ? ExternalTools.emptyFile() : blob("HEAD", o ?? p)
            return (left, repo.absolute(p))
        case .commit(let h, let parent, let p, let o):
            return (parent.map { blob($0, o ?? p) } ?? ExternalTools.emptyFile(), blob(h, p))
        case .range(let a, let b, let p, let o):
            return (blob(a, o ?? p), blob(b, p))
        case .revToWorking(let r, let p):
            return (blob(r, p), repo.absolute(p))
        case .files(let l, let r):
            return (l, r)
        }
    }
}

struct DiffView: View {
    let repo: Repo
    let source: DiffSource
    @EnvironmentObject var settings: AppSettings
    @State private var lines: [DiffLine] = []
    @State private var loading = true
    @State private var ignoreWhitespace = false
    @State private var error = ""
    @State private var changeIndex = -1
    @State private var jump: Int?

    /// ids (line ids) of the first line of each change block
    private var changeStarts: [Int] {
        var out: [Int] = []
        var prevChange = false
        for l in lines {
            let isChange = l.kind == .add || l.kind == .del
            if isChange && !prevChange { out.append(l.id) }
            prevChange = isChange
        }
        return out
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(nsImage: FileIcons.icon(for: source.path)).resizable().frame(width: 16, height: 16)
                Text(repo.relative(source.path)).fontWeight(.semibold).lineLimit(1).truncationMode(.middle)
                Text(source.subtitle).foregroundStyle(.secondary).font(.callout)
                Spacer()
                let adds = lines.filter { $0.kind == .add }.count
                let dels = lines.filter { $0.kind == .del }.count
                Text("+\(adds)").foregroundStyle(.green).monospacedDigit()
                Text("-\(dels)").foregroundStyle(.red).monospacedDigit()
                ControlGroup {
                    Button { go(-1) } label: { Image(systemName: "chevron.up") }.help("前の変更")
                    Button { go(1) } label: { Image(systemName: "chevron.down") }.help("次の変更")
                }.fixedSize()
                Text(changeStarts.isEmpty ? "" : "\(max(changeIndex + 1, 0))/\(changeStarts.count)").monospacedDigit().foregroundStyle(.secondary)
                Toggle("空白を無視", isOn: $ignoreWhitespace)
                Picker("", selection: $settings.diffSideBySide) {
                    Text("統合").tag(false)
                    Text("左右").tag(true)
                }.pickerStyle(.segmented).frame(width: 110)
                Button {
                    if settings.externalDiff.isEmpty {
                        WindowManager.alert("外部差分ツールが設定されていません", info: "設定 > 外部ツールで指定してください。", style: .informational)
                    } else {
                        let (l, r) = source.externalFiles(repo: repo)
                        ExternalTools.diff(left: l, right: r, dir: repo.root)
                    }
                } label: { Image(systemName: "arrow.up.forward.app") }.help("外部ツールで開く")
            }
            .padding(10)
            Divider()
            if loading {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if !error.isEmpty {
                Text(error).foregroundStyle(.red).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if lines.isEmpty {
                Text("差分はありません").foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if settings.diffSideBySide {
                SideBySideDiff(rows: DiffParser.sideBySide(lines.filter { l in
                    !(l.kind == .meta && (l.text.hasPrefix("diff --git") || l.text.hasPrefix("index ") || l.text.hasPrefix("--- ") || l.text.hasPrefix("+++ ")))
                }), jump: $jump)
            } else {
                UnifiedDiff(lines: lines, jump: $jump)
            }
        }
        .task(id: ignoreWhitespace) { await load() }
    }

    private func go(_ delta: Int) {
        let starts = changeStarts
        guard !starts.isEmpty else { return }
        changeIndex = max(0, min(starts.count - 1, changeIndex + delta))
        jump = starts[changeIndex]
    }

    private func load() async {
        loading = true
        let args = source.args(repo: repo, ignoreWhitespace: ignoreWhitespace)
        let r = await repo.git(args)
        // `diff --no-index` exits with 1 when files differ.
        if r.status > 1 || (r.status != 0 && r.stdout.isEmpty) {
            error = r.message
        } else {
            error = ""
            lines = DiffParser.parse(r.out)
            changeIndex = -1
        }
        loading = false
    }
}

private let diffFont = Font.system(size: 12, design: .monospaced)

private func diffBackground(_ kind: DiffLine.Kind) -> Color {
    switch kind {
    case .add: return Color.green.opacity(0.16)
    case .del: return Color.red.opacity(0.16)
    case .hunk: return Color.blue.opacity(0.10)
    case .meta: return Color.gray.opacity(0.10)
    default: return .clear
    }
}

private struct LineNo: View {
    let n: Int?
    var body: some View {
        Text(n.map(String.init) ?? "")
            .font(diffFont).foregroundStyle(.secondary)
            .frame(width: 44, alignment: .trailing)
            .padding(.trailing, 4)
    }
}

struct UnifiedDiff: View {
    let lines: [DiffLine]
    @Binding var jump: Int?
    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ForEach(lines) { l in
                        HStack(alignment: .top, spacing: 0) {
                            LineNo(n: l.oldNo)
                            LineNo(n: l.newNo)
                            Text(l.kind == .add ? "+" : l.kind == .del ? "-" : " ")
                                .font(diffFont).frame(width: 14)
                                .foregroundStyle(l.kind == .add ? .green : .red)
                            Text(l.text.isEmpty ? " " : l.text)
                                .font(diffFont)
                                .foregroundStyle(l.kind == .hunk ? Color.blue : l.kind == .meta ? Color.secondary : Color.primary)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                        .padding(.vertical, 0.5)
                        .background(diffBackground(l.kind))
                        .id(l.id)
                    }
                }
                .textSelection(.enabled)
            }
            .background(Color(nsColor: .textBackgroundColor))
            .onChange(of: jump) { if let j = jump { proxy.scrollTo(j, anchor: .center); jump = nil } }
        }
    }
}

struct SideBySideDiff: View {
    let rows: [DiffParser.SideRow]
    @Binding var jump: Int?
    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ForEach(rows) { row in
                        Group {
                            if let h = row.header {
                                Text(h.text.isEmpty ? " " : h.text)
                                    .font(diffFont)
                                    .foregroundStyle(h.kind == .hunk ? Color.blue : Color.secondary)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(.leading, 6)
                                    .background(diffBackground(h.kind))
                            } else {
                                HStack(alignment: .top, spacing: 0) {
                                    side(row.left, isLeft: true)
                                    Divider()
                                    side(row.right, isLeft: false)
                                }
                            }
                        }
                        .id(rowKey(row))
                    }
                }
                .textSelection(.enabled)
            }
            .background(Color(nsColor: .textBackgroundColor))
            .onChange(of: jump) {
                guard let j = jump else { return }
                if let row = rows.first(where: { $0.left?.id == j || $0.right?.id == j }) {
                    proxy.scrollTo(rowKey(row), anchor: .center)
                }
                jump = nil
            }
        }
    }

    private func rowKey(_ row: DiffParser.SideRow) -> String { "row-\(row.id)" }

    @ViewBuilder
    private func side(_ l: DiffLine?, isLeft: Bool) -> some View {
        HStack(alignment: .top, spacing: 0) {
            LineNo(n: isLeft ? l?.oldNo : l?.newNo)
            Text(l.map { $0.text.isEmpty ? " " : $0.text } ?? " ")
                .font(diffFont)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(l.map { $0.kind == .context ? Color.clear : diffBackground($0.kind) } ?? Color.gray.opacity(0.08))
    }
}
