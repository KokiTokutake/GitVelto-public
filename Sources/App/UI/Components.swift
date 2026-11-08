import AppKit
import SwiftUI
import UniformTypeIdentifiers

// MARK: - Colors / icons

extension FileState {
    var color: Color {
        switch self {
        case .modified, .typeChanged: return Color(nsColor: .systemRed)
        case .added, .copied: return Color(nsColor: .systemBlue)
        case .deleted: return Color(nsColor: .systemBrown)
        case .renamed: return Color(nsColor: .systemPurple)
        case .untracked: return .secondary
        case .ignored: return Color(nsColor: .tertiaryLabelColor)
        case .conflict: return Color(nsColor: .systemOrange)
        }
    }
}

enum FileIcons {
    private static var cache: [String: NSImage] = [:]
    static func icon(for path: String) -> NSImage {
        let ext = (path as NSString).pathExtension.lowercased()
        if let i = cache[ext] { return i }
        let type = UTType(filenameExtension: ext) ?? .data
        let img = NSWorkspace.shared.icon(for: ext.isEmpty ? .data : type)
        cache[ext] = img
        return img
    }
    static var folder: NSImage { NSWorkspace.shared.icon(for: .folder) }
}

let relativeDateFormatter: DateFormatter = {
    let f = DateFormatter()
    f.dateFormat = "yyyy/MM/dd HH:mm"
    return f
}()

// MARK: - File table

/// TortoiseGit-like file list with optional check boxes.
struct FileTable<MenuContent: View>: View {
    let entries: [StatusEntry]
    var checked: Binding<Set<String>>? = nil
    @Binding var selection: Set<String>
    var onOpen: (StatusEntry) -> Void
    @ViewBuilder var menu: (Set<String>) -> MenuContent

    var body: some View {
        Table(entries, selection: $selection) {
            TableColumn("パス") { e in
                HStack(spacing: 6) {
                    if let checked {
                        Toggle("", isOn: Binding(
                            get: { checked.wrappedValue.contains(e.id) },
                            set: { v in
                                if v { checked.wrappedValue.insert(e.id) } else { checked.wrappedValue.remove(e.id) }
                            })).labelsHidden().toggleStyle(.checkbox)
                    }
                    Image(nsImage: e.path.hasSuffix("/") ? FileIcons.folder : FileIcons.icon(for: e.path))
                        .resizable().frame(width: 16, height: 16)
                    Text(e.displayPath).lineLimit(1).truncationMode(.middle)
                        .foregroundStyle(e.state.color)
                }
            }
            TableColumn("状態") { e in
                Text(e.state.label + (e.staged && e.state.isTracked ? " (ステージ済)" : ""))
                    .foregroundStyle(e.state.color)
            }.width(min: 70, ideal: 110, max: 160)
            TableColumn("拡張子") { e in Text(e.ext).foregroundStyle(.secondary) }
                .width(min: 40, ideal: 60, max: 90)
        }
        .contextMenu(forSelectionType: String.self, menu: { ids in menu(ids) }, primaryAction: { ids in
            if let id = ids.first, let e = entries.first(where: { $0.id == id }) { onOpen(e) }
        })
    }
}

// MARK: - Progress dialog

enum ProgressStep {
    case git([String])
    case gitEnv([String], [String: String])
    case custom(String, () throws -> String)
}

struct ProgressAction {
    let title: String
    let action: () -> Void
}

struct OutputLine: Identifiable {
    enum Kind { case command, normal, error, warning, success }
    let id: Int
    let raw: String
    let kind: Kind
    var translated: String {
        kind == .normal || kind == .error || kind == .warning ? GitMessages.translate(line: raw) : raw
    }
}

final class CommandRunner: ObservableObject {
    enum State { case running, success, failed, cancelled }

    @Published var lines: [OutputLine] = []
    @Published var partial: String = ""
    @Published var state: State = .running
    var onFinish: ((Bool) -> Void)?

    private let dir: String
    private var steps: [ProgressStep]
    private var process: StreamingProcess?
    private var nextID = 0
    private var cancelled = false

    init(dir: String, steps: [ProgressStep]) {
        self.dir = dir
        self.steps = steps
    }

    func start() { runNext() }

    func cancel() {
        cancelled = true
        process?.cancel()
    }

    private func add(_ text: String, _ kind: OutputLine.Kind) {
        lines.append(OutputLine(id: nextID, raw: text, kind: kind))
        nextID += 1
    }

    private func classify(_ line: String) -> OutputLine.Kind {
        let l = line.lowercased()
        if l.hasPrefix("error") || l.hasPrefix("fatal") || l.contains("conflict") || l.contains("! [rejected]") || l.contains("! [remote rejected]") { return .error }
        if l.hasPrefix("warning") || l.hasPrefix("hint") { return .warning }
        return .normal
    }

    /// Handles \r progress updates the way a terminal would.
    private func consume(_ chunk: String) {
        for ch in chunk {
            if ch == "\n" {
                add(partial, classify(partial))
                partial = ""
            } else if ch == "\r" {
                // Next text overwrites the current line
                if !partial.isEmpty { pendingCR = true }
            } else {
                if pendingCR { partial = ""; pendingCR = false }
                partial.append(ch)
            }
        }
    }
    private var pendingCR = false

    private func finish(_ ok: Bool) {
        if !partial.isEmpty { add(partial, classify(partial)); partial = "" }
        if cancelled {
            state = .cancelled
            add("中止しました", .error)
        } else {
            state = ok ? .success : .failed
            add(ok ? "成功" : "失敗しました", ok ? .success : .error)
        }
        onFinish?(ok && !cancelled)
    }

    private func runNext() {
        guard !cancelled else { finish(false); return }
        guard !steps.isEmpty else { finish(true); return }
        let step = steps.removeFirst()
        switch step {
        case .git(let args):
            runGit(args, env: [:])
        case .gitEnv(let args, let env):
            runGit(args, env: env)
        case .custom(let title, let work):
            add(title, .command)
            DispatchQueue.global(qos: .userInitiated).async {
                do {
                    let out = try work()
                    DispatchQueue.main.async {
                        if !out.isEmpty { self.consume(out + "\n") }
                        self.runNext()
                    }
                } catch {
                    DispatchQueue.main.async {
                        self.add(error.localizedDescription, .error)
                        self.finish(false)
                    }
                }
            }
        }
    }

    private func runGit(_ args: [String], env: [String: String]) {
            let shown = args.filter { $0 != "--literal-pathspecs" }
            add("git " + shown.map { $0.contains(" ") ? "\"\($0)\"" : $0 }.joined(separator: " "), .command)
            let p = StreamingProcess(executable: Git.executable,
                                     args: ["-c", "core.quotepath=off", "-c", "color.ui=false"] + args,
                                     dir: dir, env: Git.environment.merging(env) { _, n in n })
            p.onOutput = { [weak self] s in DispatchQueue.main.async { self?.consume(s) } }
            p.onExit = { [weak self] code in
                DispatchQueue.main.async {
                    guard let self else { return }
                    if !self.partial.isEmpty { self.add(self.partial, self.classify(self.partial)); self.partial = "" }
                    if code == 0 { self.runNext() } else {
                        self.add("(終了コード \(code))", .error)
                        self.finish(false)
                    }
                }
            }
            process = p
            do { try p.start() } catch {
                add("git を起動できません: \(error.localizedDescription)", .error)
                finish(false)
            }
    }
}

struct ProgressOutputView: View {
    let title: String
    @ObservedObject var runner: CommandRunner
    var successActions: [ProgressAction]
    @EnvironmentObject var handle: WindowHandle
    @AppStorage("progressShowRaw") private var showRaw = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                switch runner.state {
                case .running: ProgressView().controlSize(.small); Text("\(title) を実行中…")
                case .success: Image(systemName: "checkmark.circle.fill").foregroundStyle(.green); Text("\(title): 成功")
                case .failed: Image(systemName: "xmark.octagon.fill").foregroundStyle(.red); Text("\(title): 失敗")
                case .cancelled: Image(systemName: "stop.circle.fill").foregroundStyle(.orange); Text("\(title): 中止")
                }
                Spacer()
            }.font(.headline)

            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 1) {
                        ForEach(runner.lines) { line in
                            let text = showRaw ? line.raw : line.translated
                            Text(text.isEmpty ? " " : text)
                                .foregroundStyle(color(line.kind))
                                .fontWeight(line.kind == .command ? .semibold : .regular)
                                .frame(maxWidth: .infinity, alignment: .leading)
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
                .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color(nsColor: .separatorColor)))
                .onChange(of: runner.lines.count) { proxy.scrollTo("bottom", anchor: .bottom) }
                .onChange(of: runner.partial) { proxy.scrollTo("bottom", anchor: .bottom) }
            }

            HStack {
                if runner.state == .success {
                    ForEach(Array(successActions.enumerated()), id: \.offset) { _, a in
                        Button(a.title) { a.action(); handle.close() }
                    }
                }
                Spacer()
                Toggle("原文 (英語) を表示", isOn: $showRaw).toggleStyle(.checkbox)
                Button {
                    copyToPasteboard(runner.lines.map { showRaw ? $0.raw : $0.translated }.joined(separator: "\n"))
                } label: { Image(systemName: "doc.on.doc") }.help("出力をコピー")
                if runner.state == .running {
                    Button("中止") { runner.cancel() }
                } else {
                    Button("閉じる") { handle.close() }.keyboardShortcut(.defaultAction)
                }
            }
        }
        .padding(14)
    }

    private func color(_ k: OutputLine.Kind) -> Color {
        switch k {
        case .command: return Color(nsColor: .systemBlue)
        case .normal: return .primary
        case .error: return Color(nsColor: .systemRed)
        case .warning: return Color(nsColor: .systemOrange)
        case .success: return Color(nsColor: .systemGreen)
        }
    }
}

// MARK: - Ref picker

struct RefPicker: View {
    let title: String
    let refs: [RefInfo]
    @Binding var selection: String
    var includeLocal = true
    var includeRemote = true
    var includeTags = true

    var body: some View {
        Picker(title, selection: $selection) {
            if includeLocal {
                Section("ローカルブランチ") {
                    ForEach(refs.filter { $0.kind == .local }) { r in Text(r.name).tag(r.name) }
                }
            }
            if includeRemote {
                Section("リモートブランチ") {
                    ForEach(refs.filter { $0.kind == .remote }) { r in Text(r.name).tag(r.name) }
                }
            }
            if includeTags {
                Section("タグ") {
                    ForEach(refs.filter { $0.kind == .tag }) { r in Text(r.name).tag(r.name) }
                }
            }
        }
    }
}

/// Header showing repository + branch, used by most dialogs.
struct RepoHeader: View {
    let repo: Repo
    @State private var branch = ""
    @State private var opState: RepoOperationState = .none

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "folder").foregroundStyle(.secondary)
            Text(repo.root).lineLimit(1).truncationMode(.head).foregroundStyle(.secondary)
            Spacer()
            if opState != .none {
                Text(opState.label).padding(.horizontal, 6).padding(.vertical, 2)
                    .background(Color.orange.opacity(0.25)).clipShape(Capsule())
            }
            Image(systemName: "arrow.triangle.branch")
            Text(branch).fontWeight(.semibold)
        }
        .font(.callout)
        .task {
            let r = repo
            let (b, s) = await Task.detached { (r.headDescription(), r.operationState()) }.value
            branch = b; opState = s
        }
    }
}

extension View {
    func monospacedEditor() -> some View {
        self.font(.system(size: 13, design: .monospaced))
            .scrollContentBackground(.hidden)
            .padding(4)
            .background(Color(nsColor: .textBackgroundColor))
            .overlay(RoundedRectangle(cornerRadius: 4).stroke(Color(nsColor: .separatorColor)))
    }
}

func revealInFinder(_ paths: [String]) {
    NSWorkspace.shared.activateFileViewerSelecting(paths.map { URL(fileURLWithPath: $0) })
}

func openFile(_ path: String) {
    NSWorkspace.shared.open(URL(fileURLWithPath: path))
}

func copyToPasteboard(_ s: String) {
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(s, forType: .string)
}

// MARK: - Split pane

/// Lightweight draggable split view (avoids NSSplitView-backed VSplitView/HSplitView layout re-entrancy).
struct SplitPane<First: View, Second: View>: View {
    enum Axis { case vertical, horizontal }
    let axis: Axis
    @Binding var fraction: CGFloat
    var minFirst: CGFloat = 120
    var minSecond: CGFloat = 120
    @ViewBuilder var first: () -> First
    @ViewBuilder var second: () -> Second
    @State private var dragStart: CGFloat?

    var body: some View {
        GeometryReader { geo in
            let total = axis == .vertical ? geo.size.height : geo.size.width
            let handle: CGFloat = 6
            let avail = max(total - handle, 1)
            let firstSize = min(max(avail * fraction, minFirst), max(avail - minSecond, minFirst))
            let layout = axis == .vertical ? AnyLayout(VStackLayout(spacing: 0)) : AnyLayout(HStackLayout(spacing: 0))
            layout {
                first()
                    .frame(width: axis == .horizontal ? firstSize : nil, height: axis == .vertical ? firstSize : nil)
                ZStack {
                    Color(nsColor: .windowBackgroundColor)
                    if axis == .vertical { Divider() } else { Divider() }
                }
                .frame(width: axis == .horizontal ? handle : nil, height: axis == .vertical ? handle : nil)
                .contentShape(Rectangle())
                .onHover { inside in
                    if inside { (axis == .vertical ? NSCursor.resizeUpDown : NSCursor.resizeLeftRight).push() } else { NSCursor.pop() }
                }
                .gesture(DragGesture(minimumDistance: 1)
                    .onChanged { v in
                        if dragStart == nil { dragStart = firstSize }
                        let delta = axis == .vertical ? v.translation.height : v.translation.width
                        fraction = min(max(((dragStart ?? firstSize) + delta) / avail, 0.05), 0.95)
                    }
                    .onEnded { _ in dragStart = nil })
                second()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }
}
