import AppKit
import SwiftUI

// MARK: - "?" term help

/// Small "?" button next to a Git term. Shows the glossary entry in a popover.
struct TermHelp: View {
    let key: String
    @State private var shown = false

    init(_ key: String) { self.key = key }

    var body: some View {
        Button { shown.toggle() } label: {
            Image(systemName: "questionmark.circle").foregroundStyle(.secondary)
        }
        .buttonStyle(.borderless)
        .help(Glossary.entry(key)?.summary ?? "")
        .popover(isPresented: $shown, arrowEdge: .bottom) {
            if let e = Glossary.entry(key) { TermCard(entry: e, compact: true).frame(width: 380).padding(14) }
        }
        .accessibilityLabel("用語の説明: \(Glossary.entry(key)?.term ?? key)")
    }
}

/// Text label followed by a "?" help button.
struct TermLabel: View {
    let text: String
    let key: String
    init(_ text: String, _ key: String) { self.text = text; self.key = key }
    var body: some View {
        HStack(spacing: 3) { Text(text); TermHelp(key) }
    }
}

struct TermCard: View {
    let entry: GlossaryEntry
    var compact = false
    var onSelectRelated: ((String) -> Void)? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Text(entry.term).font(compact ? .headline : .title2).bold()
                Text(entry.english).foregroundStyle(.secondary).font(.callout)
            }
            Text(entry.summary).fontWeight(.medium)
            Text(entry.detail).font(.callout).fixedSize(horizontal: false, vertical: true)
            if !entry.command.isEmpty {
                VStack(alignment: .leading, spacing: 2) {
                    Text("コマンドでは").font(.caption).foregroundStyle(.secondary)
                    Text(entry.command)
                        .font(.system(size: 12, design: .monospaced))
                        .textSelection(.enabled)
                        .padding(6)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color(nsColor: .textBackgroundColor))
                        .clipShape(RoundedRectangle(cornerRadius: 4))
                }
            }
            if !entry.related.isEmpty {
                HStack(spacing: 6) {
                    Text("関連:").font(.caption).foregroundStyle(.secondary)
                    ForEach(entry.related, id: \.self) { key in
                        if let r = Glossary.entry(key) {
                            Button(r.term) {
                                if let onSelectRelated { onSelectRelated(key) } else { WindowManager.shared.showGlossary(select: key) }
                            }
                            .buttonStyle(.link).font(.caption)
                        }
                    }
                }
            }
            if compact {
                Button("用語集で開く") { WindowManager.shared.showGlossary(select: entry.id) }.controlSize(.small)
            }
        }
    }
}

// MARK: - Glossary window

struct GlossaryView: View {
    @State var selection: String?
    @State private var query = ""

    var body: some View {
        HSplitView {
            VStack(spacing: 0) {
                TextField("用語を検索 (例: リベース, push, HEAD)", text: $query)
                    .textFieldStyle(.roundedBorder).padding(8)
                ScrollViewReader { proxy in
                    List(Glossary.search(query), selection: $selection) { e in
                        VStack(alignment: .leading, spacing: 2) {
                            Text(e.term).fontWeight(.semibold)
                            Text(e.summary).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                        }
                        .tag(e.id)
                        .id(e.id)
                    }
                    .onAppear { if let s = selection { DispatchQueue.main.async { proxy.scrollTo(s, anchor: .center) } } }
                    .onChange(of: selection) { if let s = selection { proxy.scrollTo(s) } }
                }
            }
            .frame(minWidth: 240, idealWidth: 280, maxWidth: 360)
            ScrollView {
                if let id = selection, let e = Glossary.entry(id) {
                    TermCard(entry: e, onSelectRelated: { selection = $0 })
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(20)
                } else {
                    VStack(alignment: .leading, spacing: 10) {
                        Text("Git 用語集").font(.title2).bold()
                        Text("左の一覧から用語を選ぶと、意味・使いどころ・対応するコマンドを表示します。各画面の「?」ボタンからもここを開けます。")
                            .foregroundStyle(.secondary)
                        Text("はじめての方へのおすすめ順: リポジトリ → 作業ツリー → コミット → ブランチ → プッシュ → プル → マージ")
                            .font(.callout)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(20)
                }
            }
            .frame(minWidth: 360)
        }
        .onAppear { if selection == nil { selection = Glossary.entries.first?.id } }
    }
}

// MARK: - Learning mode: command preview

/// Shows the git commands a dialog will run, with explanations (learning mode only).
struct CommandPreview: View {
    let commands: [[String]]
    var note: String? = nil
    @EnvironmentObject var settings: AppSettings
    @State private var expanded = true

    var body: some View {
        if settings.learningMode && !commands.isEmpty {
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Label("学習モード: 実行される git コマンド", systemImage: "graduationcap").font(.caption).bold()
                    Spacer()
                    Button {
                        copyToPasteboard(commands.map { CommandExplainer.explain($0).command }.joined(separator: "\n"))
                    } label: { Image(systemName: "doc.on.doc") }.buttonStyle(.borderless).help("コマンドをコピー")
                    Button { expanded.toggle() } label: { Image(systemName: expanded ? "chevron.up" : "chevron.down") }
                        .buttonStyle(.borderless).help(expanded ? "説明を隠す" : "説明を表示")
                }
                ForEach(Array(commands.enumerated()), id: \.offset) { _, args in
                    CommandExplanationRow(args: args, expanded: expanded)
                }
                if let note, expanded { Text(note).font(.caption).foregroundStyle(.secondary) }
            }
            .padding(8)
            .background(Color.accentColor.opacity(0.08))
            .clipShape(RoundedRectangle(cornerRadius: 6))
        }
    }
}

struct CommandExplanationRow: View {
    let args: [String]
    var expanded = true
    var body: some View {
        let ex = CommandExplainer.explain(args)
        VStack(alignment: .leading, spacing: 2) {
            Text("$ " + ex.command)
                .font(.system(size: 12, design: .monospaced))
                .textSelection(.enabled)
                .lineLimit(expanded ? nil : 1)
            if expanded {
                if !ex.summary.isEmpty { Text("→ " + ex.summary).font(.caption).foregroundStyle(.secondary) }
                ForEach(Array(ex.options.enumerated()), id: \.offset) { _, o in
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text(o.0).font(.system(size: 11, design: .monospaced)).foregroundStyle(Color.accentColor)
                        Text(o.1).font(.caption).foregroundStyle(.secondary)
                    }
                    .padding(.leading, 12)
                }
            }
        }
    }
}

// MARK: - Command history

struct CommandHistoryView: View {
    @ObservedObject var history = CommandHistory.shared
    @State private var selection: UUID?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("GitVelto が実行した git コマンド").font(.headline)
                Spacer()
                Button("すべてコピー") {
                    copyToPasteboard(history.entries.reversed().map { "cd " + CommandExplainer.quote($0.dir) + " && " + CommandExplainer.explain($0.args).command }.joined(separator: "\n"))
                }
                Button("クリア") { history.clear() }
            }
            Text("ボタン操作の裏で実際に何が実行されたかを確認できます。コピーしてターミナルで同じ操作を試すこともできます。")
                .font(.caption).foregroundStyle(.secondary)
            if history.entries.isEmpty {
                Text("まだコマンドは実行されていません。").foregroundStyle(.secondary).frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                List(history.entries, selection: $selection) { e in
                    VStack(alignment: .leading, spacing: 3) {
                        HStack {
                            Image(systemName: e.ok == nil ? "clock" : (e.ok! ? "checkmark.circle.fill" : "xmark.octagon.fill"))
                                .foregroundStyle(e.ok == nil ? Color.secondary : (e.ok! ? Color.green : Color.red))
                            Text(relativeDateFormatter.string(from: e.date)).font(.caption).foregroundStyle(.secondary)
                            Text((e.dir as NSString).lastPathComponent).font(.caption).foregroundStyle(.secondary)
                        }
                        CommandExplanationRow(args: e.args, expanded: selection == e.id)
                    }
                    .padding(.vertical, 2)
                    .tag(e.id)
                }
            }
        }
        .padding(14)
    }
}
