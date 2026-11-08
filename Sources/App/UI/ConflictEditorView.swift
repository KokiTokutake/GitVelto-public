import AppKit
import SwiftUI

/// One piece of a conflicted file.
struct ConflictSegment: Identifiable {
    enum Resolution: Equatable { case ours, theirs, oursThenTheirs, theirsThenOurs, custom }
    let id: Int
    var isConflict: Bool
    var text: String            // for plain text segments
    var ours: String = ""
    var base: String?
    var theirs: String = ""
    var oursLabel: String = ""
    var theirsLabel: String = ""
    var resolution: Resolution?
    var resolved: String = ""

    var output: String {
        guard isConflict else { return text }
        if resolution != nil { return resolved }
        var s = "<<<<<<< \(oursLabel)\n" + ours
        if let base { s += "||||||| base\n" + base }
        s += "=======\n" + theirs + ">>>>>>> \(theirsLabel)\n"
        return s
    }
}

enum ConflictParser {
    static func parse(_ text: String) -> [ConflictSegment] {
        var segs: [ConflictSegment] = []
        var plain = ""
        var state = 0 // 0 plain, 1 ours, 2 base, 3 theirs
        var cur = ConflictSegment(id: 0, isConflict: true, text: "")
        var lines = text.components(separatedBy: "\n")
        let endsWithNewline = text.hasSuffix("\n")
        if endsWithNewline { lines.removeLast() }
        for (i, line) in lines.enumerated() {
            let l = line + (i < lines.count - 1 || endsWithNewline ? "\n" : "")
            switch state {
            case 0:
                if line.hasPrefix("<<<<<<<") {
                    if !plain.isEmpty { segs.append(ConflictSegment(id: segs.count, isConflict: false, text: plain)); plain = "" }
                    cur = ConflictSegment(id: 0, isConflict: true, text: "")
                    cur.oursLabel = String(line.dropFirst(7)).trimmingCharacters(in: .whitespaces)
                    state = 1
                } else { plain += l }
            case 1:
                if line.hasPrefix("|||||||") { cur.base = ""; state = 2 }
                else if line.hasPrefix("=======") { state = 3 }
                else { cur.ours += l }
            case 2:
                if line.hasPrefix("=======") { state = 3 } else { cur.base! += l }
            default:
                if line.hasPrefix(">>>>>>>") {
                    cur.theirsLabel = String(line.dropFirst(7)).trimmingCharacters(in: .whitespaces)
                    segs.append(ConflictSegment(id: segs.count, isConflict: true, text: "", ours: cur.ours, base: cur.base,
                                                theirs: cur.theirs, oursLabel: cur.oursLabel, theirsLabel: cur.theirsLabel))
                    state = 0
                } else { cur.theirs += l }
            }
        }
        if !plain.isEmpty { segs.append(ConflictSegment(id: segs.count, isConflict: false, text: plain)) }
        return segs
    }
}

/// Built-in 2-way conflict editor (a light-weight TortoiseGitMerge).
struct ConflictEditorView: View {
    let repo: Repo
    let path: String
    @EnvironmentObject var handle: WindowHandle
    @State private var segments: [ConflictSegment] = []
    @State private var loadError = ""
    @State private var jump: Int?

    private var conflicts: [ConflictSegment] { segments.filter(\.isConflict) }
    private var unresolved: Int { conflicts.filter { $0.resolution == nil }.count }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
                Text(repo.relative(path)).fontWeight(.semibold)
                Text("競合 \(conflicts.count) 箇所 / 未解決 \(unresolved)").foregroundStyle(unresolved > 0 ? .orange : .green)
                TermHelp("conflict")
                Spacer()
                Button("次の未解決へ") {
                    jump = conflicts.first { $0.resolution == nil }?.id
                }.disabled(unresolved == 0)
                Button("すべて自分の変更") { resolveAll(.ours) }
                Button("すべて相手の変更") { resolveAll(.theirs) }
            }
            .padding(10)
            Divider()
            if !loadError.isEmpty {
                Text(loadError).foregroundStyle(.red).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 0) {
                            ForEach($segments) { $seg in
                                if seg.isConflict {
                                    ConflictBlock(seg: $seg).id(seg.id).padding(.vertical, 6)
                                } else {
                                    PlainBlock(text: seg.text)
                                }
                            }
                        }
                        .padding(8)
                    }
                    .background(Color(nsColor: .textBackgroundColor))
                    .onChange(of: jump) { if let j = jump { withAnimation { proxy.scrollTo(j, anchor: .top) }; jump = nil } }
                }
            }
            Divider()
            HStack {
                Text("「自分」= 現在のブランチ (HEAD)、「相手」= マージ/適用しようとしている変更").font(.caption).foregroundStyle(.secondary)
                TermHelp("ours-theirs")
                Spacer()
                if !AppSettings.shared.externalMerge.isEmpty {
                    Button("外部マージツール") { ExternalTools.merge(repo: repo, path: path); handle.close() }
                }
                Button("キャンセル") { handle.close() }.keyboardShortcut(.cancelAction)
                Button("保存") { save(markResolved: false) }
                Button("保存して解決済みにする") { save(markResolved: true) }
                    .keyboardShortcut(.return, modifiers: .command)
                    .buttonStyle(.borderedProminent)
                    .disabled(unresolved > 0)
            }
            .padding(10)
        }
        .onAppear(perform: load)
    }

    private func load() {
        guard let data = FileManager.default.contents(atPath: path) else { loadError = "ファイルを読み込めません"; return }
        guard let text = String(data: data, encoding: .utf8) else { loadError = "テキストファイルではありません (バイナリの競合は「競合を解決」で自分/相手を選んでください)"; return }
        segments = ConflictParser.parse(text)
        if conflicts.isEmpty { loadError = "競合マーカーが見つかりません" }
    }

    private func resolveAll(_ r: ConflictSegment.Resolution) {
        for i in segments.indices where segments[i].isConflict { ConflictBlock.apply(r, to: &segments[i]) }
    }

    private func save(markResolved: Bool) {
        let out = segments.map(\.output).joined()
        do {
            try out.write(toFile: path, atomically: true, encoding: .utf8)
        } catch {
            WindowManager.alert("保存できません", info: error.localizedDescription); return
        }
        if markResolved {
            CommandHistory.shared.record(["add", "--", repo.relative(path)], dir: repo.root)
            let r = repo.git(["--literal-pathspecs", "add", "--", repo.relative(path)])
            if !r.ok { WindowManager.alert("解決済みにできません", info: r.message); return }
            StatusService.shared.invalidate(path: path)
            handle.close()
            let root = repo.root
            if Repo(root: root).status(paths: [], untracked: "no").allSatisfy({ $0.state != .conflict }) {
                if WindowManager.confirm("すべての競合が解決されました", info: "続けてコミット (またはリベースの続行) を行いますか?", ok: "開く") {
                    let st = Repo(root: root).operationState()
                    if st == .rebasing || st == .applyingPatches || st == .cherryPicking {
                        WindowManager.shared.perform(.resolve, paths: [root])
                    } else {
                        WindowManager.shared.perform(.commit, paths: [root])
                    }
                }
            }
        } else {
            StatusService.shared.invalidate(path: path)
        }
    }
}

private struct PlainBlock: View {
    let text: String
    @State private var expanded = false
    var body: some View {
        let shown = text.hasSuffix("\n") ? String(text.dropLast()) : text
        let lines = shown.components(separatedBy: "\n")
        let long = lines.count > 12
        VStack(alignment: .leading, spacing: 0) {
            if long && !expanded {
                Text(lines.prefix(4).joined(separator: "\n"))
                Button("… \(lines.count - 8) 行を表示 …") { expanded = true }.buttonStyle(.link).padding(.vertical, 2)
                Text(lines.suffix(4).joined(separator: "\n"))
            } else {
                Text(shown)
            }
        }
        .font(.system(size: 12, design: .monospaced))
        .foregroundStyle(.secondary)
        .textSelection(.enabled)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct ConflictBlock: View {
    @Binding var seg: ConflictSegment

    static func apply(_ r: ConflictSegment.Resolution, to seg: inout ConflictSegment) {
        seg.resolution = r
        switch r {
        case .ours: seg.resolved = seg.ours
        case .theirs: seg.resolved = seg.theirs
        case .oursThenTheirs: seg.resolved = seg.ours + seg.theirs
        case .theirsThenOurs: seg.resolved = seg.theirs + seg.ours
        case .custom: if seg.resolved.isEmpty { seg.resolved = seg.ours }
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .top, spacing: 8) {
                side("自分: \(seg.oursLabel)", seg.ours, .green)
                side("相手: \(seg.theirsLabel)", seg.theirs, .blue)
            }
            HStack {
                Button("自分を使用") { Self.apply(.ours, to: &seg) }
                Button("相手を使用") { Self.apply(.theirs, to: &seg) }
                Button("両方 (自分 → 相手)") { Self.apply(.oursThenTheirs, to: &seg) }
                Button("両方 (相手 → 自分)") { Self.apply(.theirsThenOurs, to: &seg) }
                Button("手動で編集") { Self.apply(.custom, to: &seg) }
                Spacer()
                if seg.resolution != nil {
                    Label("解決済み", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                    Button("元に戻す") { seg.resolution = nil; seg.resolved = "" }
                } else {
                    Label("未解決", systemImage: "exclamationmark.circle.fill").foregroundStyle(.orange)
                }
            }
            .controlSize(.small)
            if seg.resolution != nil {
                Text("結果:").font(.caption).foregroundStyle(.secondary)
                TextEditor(text: Binding(get: { seg.resolved }, set: { seg.resolved = $0; seg.resolution = .custom }))
                    .font(.system(size: 12, design: .monospaced))
                    .frame(minHeight: CGFloat(max(3, seg.resolved.components(separatedBy: "\n").count)) * 16 + 10)
                    .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color.green.opacity(0.6)))
            }
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 6).fill(Color.orange.opacity(0.07)))
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.orange.opacity(0.5)))
    }

    private func side(_ title: String, _ text: String, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.caption).bold().foregroundStyle(color)
            Text(text.isEmpty ? "(なし)" : (text.hasSuffix("\n") ? String(text.dropLast()) : text))
                .font(.system(size: 12, design: .monospaced))
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(6)
                .background(color.opacity(0.10))
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
    }
}

// MARK: - External tools

enum ExternalTools {
    static func quote(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }

    static func runTemplate(_ template: String, _ vars: [String: String], dir: String) {
        var cmd = template
        for (k, v) in vars {
            cmd = cmd.replacingOccurrences(of: "\"$\(k)\"", with: quote(v)).replacingOccurrences(of: "$\(k)", with: quote(v))
        }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/zsh")
        p.arguments = ["-lc", cmd]
        p.currentDirectoryURL = URL(fileURLWithPath: dir)
        p.environment = Git.environment
        do { try p.run() } catch { WindowManager.alert("外部ツールを起動できません", info: error.localizedDescription) }
    }

    static func diff(left: String, right: String, dir: String) {
        runTemplate(AppSettings.shared.externalDiff, ["LOCAL": left, "REMOTE": right], dir: dir)
    }

    /// Writes stage 1/2/3 versions and launches the external merge tool for a conflicted file.
    static func merge(repo: Repo, path: String) {
        let rel = repo.relative(path)
        let base = repo.exportBlob(rev: ":1", path: rel) ?? emptyFile()
        let local = repo.exportBlob(rev: ":2", path: rel) ?? emptyFile()
        let remote = repo.exportBlob(rev: ":3", path: rel) ?? emptyFile()
        runTemplate(AppSettings.shared.externalMerge, ["BASE": base, "LOCAL": local, "REMOTE": remote, "MERGED": path], dir: repo.root)
    }

    static func emptyFile() -> String {
        let url = KGTemp.url("empty-\(UUID().uuidString.prefix(6))")
        try? Data().write(to: url)
        return url.path
    }
}
