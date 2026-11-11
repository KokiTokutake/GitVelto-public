import Foundation

// MARK: - Process runner

struct GitResult {
    var status: Int32
    var stdout: Data
    var stderr: Data
    var ok: Bool { status == 0 }
    var out: String { String(decoding: stdout, as: UTF8.self) }
    var err: String { String(decoding: stderr, as: UTF8.self) }
    var rawMessage: String {
        let e = err.trimmingCharacters(in: .whitespacesAndNewlines)
        return e.isEmpty ? out.trimmingCharacters(in: .whitespacesAndNewlines) : e
    }
    /// Message translated to Japanese where possible.
    var message: String { GitMessages.translate(rawMessage) }
}

struct GitError: LocalizedError {
    var message: String
    var errorDescription: String? { message }
}

/// Temporary files used for pathspecs, messages, exported blobs, rebase todo lists.
enum KGTemp {
    static let dir: URL = {
        var candidates: [URL] = []
        if let t = ProcessInfo.processInfo.environment["TMPDIR"], !t.isEmpty { candidates.append(URL(fileURLWithPath: t)) }
        candidates.append(FileManager.default.temporaryDirectory)
        candidates.append(URL(fileURLWithPath: "/tmp"))
        for base in candidates {
            let d = base.appendingPathComponent("GitVelto", isDirectory: true)
            try? FileManager.default.createDirectory(at: d, withIntermediateDirectories: true)
            let probe = d.appendingPathComponent(".probe-\(UUID().uuidString)")
            if (try? Data().write(to: probe)) != nil {
                try? FileManager.default.removeItem(at: probe)
                return d
            }
        }
        return FileManager.default.temporaryDirectory
    }()

    static func url(_ name: String) -> URL { dir.appendingPathComponent(name) }
}

enum Git {
    static let candidates = ["/opt/homebrew/bin/git", "/usr/local/bin/git", "/usr/bin/git"]

    static var executable: String {
        let custom = AppSettings.shared.gitPath.trimmingCharacters(in: .whitespaces)
        if !custom.isEmpty, FileManager.default.isExecutableFile(atPath: custom) { return custom }
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) } ?? "/usr/bin/git"
    }

    static var environment: [String: String] {
        var env = ProcessInfo.processInfo.environment
        let extra = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin"]
        let path = (env["PATH"] ?? "").split(separator: ":").map(String.init)
        env["PATH"] = (path + extra.filter { !path.contains($0) }).joined(separator: ":")
        env["GIT_TERMINAL_PROMPT"] = "0"
        env["GIT_PAGER"] = "cat"
        env["PAGER"] = "cat"
        env["GIT_EDITOR"] = "true"
        env["GIT_MERGE_AUTOEDIT"] = "no"
        if let exe = Bundle.main.executablePath {
            env["GIT_ASKPASS"] = exe
            env["SSH_ASKPASS"] = exe
            env["SSH_ASKPASS_REQUIRE"] = "force"
            env["DISPLAY"] = env["DISPLAY"] ?? ":0"
            env["GITVELTO_ASKPASS"] = "1"
        }
        env["LC_ALL"] = "en_US.UTF-8"
        env["LANG"] = "en_US.UTF-8"
        return env
    }

    /// Run git synchronously. Do not call on the main thread for potentially slow commands.
    @discardableResult
    static func run(_ args: [String], in dir: String, stdin: Data? = nil, env: [String: String] = [:]) -> GitResult {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: executable)
        p.arguments = ["-c", "core.quotepath=off", "-c", "color.ui=false"] + args
        p.currentDirectoryURL = URL(fileURLWithPath: dir)
        p.environment = environment.merging(env) { _, n in n }
        let outPipe = Pipe(), errPipe = Pipe()
        p.standardOutput = outPipe
        p.standardError = errPipe
        let inPipe = Pipe()
        p.standardInput = inPipe
        // Wait with a semaphore instead of waitUntilExit(): waitUntilExit spins the run loop,
        // which can re-enter AppKit/SwiftUI layout when called on the main thread.
        let exited = DispatchSemaphore(value: 0)
        p.terminationHandler = { _ in exited.signal() }
        do { try p.run() } catch {
            return GitResult(status: -1, stdout: Data(), stderr: Data("git を起動できません: \(error.localizedDescription)".utf8))
        }
        if let stdin { inPipe.fileHandleForWriting.write(stdin) }
        try? inPipe.fileHandleForWriting.close()
        var errData = Data()
        let group = DispatchGroup()
        group.enter()
        DispatchQueue.global().async {
            errData = errPipe.fileHandleForReading.readDataToEndOfFile()
            group.leave()
        }
        let outData = outPipe.fileHandleForReading.readDataToEndOfFile()
        group.wait()
        exited.wait()
        return GitResult(status: p.terminationStatus, stdout: outData, stderr: errData)
    }

    static func runAsync(_ args: [String], in dir: String, stdin: Data? = nil, env: [String: String] = [:]) async -> GitResult {
        await withCheckedContinuation { cont in
            DispatchQueue.global(qos: .userInitiated).async {
                cont.resume(returning: run(args, in: dir, stdin: stdin, env: env))
            }
        }
    }

    static func version() -> String {
        run(["--version"], in: NSTemporaryDirectory()).out.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    static func globalConfig(_ key: String) -> String {
        run(["config", "--global", "--get", key], in: NSHomeDirectory()).out.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

/// Streams the output of a running git process (used by the progress window).
final class StreamingProcess {
    private let process = Process()
    private var decoder = UTF8ChunkDecoder()
    private let lock = NSLock()

    var onOutput: ((String) -> Void)?
    var onExit: ((Int32) -> Void)?

    init(executable: String, args: [String], dir: String, env: [String: String]) {
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = args
        process.currentDirectoryURL = URL(fileURLWithPath: dir)
        process.environment = env
    }

    func start() throws {
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        process.standardInput = FileHandle.nullDevice
        pipe.fileHandleForReading.readabilityHandler = { [weak self] h in
            let data = h.availableData
            guard let self, !data.isEmpty else { return }
            self.lock.lock()
            let s = self.decoder.decode(data)
            self.lock.unlock()
            if !s.isEmpty { self.onOutput?(s) }
        }
        process.terminationHandler = { [weak self] p in
            pipe.fileHandleForReading.readabilityHandler = nil
            let rest = pipe.fileHandleForReading.readDataToEndOfFile()
            guard let self else { return }
            self.lock.lock()
            let s = self.decoder.decode(rest) + self.decoder.flush()
            self.lock.unlock()
            if !s.isEmpty { self.onOutput?(s) }
            self.onExit?(p.terminationStatus)
        }
        try process.run()
    }

    func cancel() {
        if process.isRunning { process.interrupt() }
        DispatchQueue.global().asyncAfter(deadline: .now() + 2) { [process] in
            if process.isRunning { process.terminate() }
        }
    }
}

/// Decodes UTF-8 incrementally, keeping incomplete trailing sequences for the next chunk.
struct UTF8ChunkDecoder {
    private var pending = Data()

    mutating func decode(_ data: Data) -> String {
        pending.append(data)
        var cut = pending.count
        // Find a safe boundary: drop at most 3 trailing bytes of an incomplete sequence.
        var i = pending.count - 1
        var back = 0
        while i >= 0 && back < 4 {
            let b = pending[pending.startIndex + i]
            if b & 0x80 == 0 { break }               // ASCII
            if b & 0xC0 == 0xC0 {                     // lead byte
                let need = b & 0xF0 == 0xF0 ? 4 : (b & 0xE0 == 0xE0 ? 3 : 2)
                if pending.count - i < need { cut = i }
                break
            }
            i -= 1; back += 1
        }
        let head = pending.prefix(cut)
        pending = Data(pending.suffix(from: pending.startIndex + cut))
        return String(decoding: head, as: UTF8.self)
    }

    mutating func flush() -> String {
        defer { pending = Data() }
        return String(decoding: pending, as: UTF8.self)
    }
}

// MARK: - Models

enum FileState: String {
    case modified, added, deleted, renamed, copied, typeChanged, untracked, ignored, conflict

    var label: String {
        switch self {
        case .modified: return "変更"
        case .added: return "追加"
        case .deleted: return "削除"
        case .renamed: return "名前変更"
        case .copied: return "コピー"
        case .typeChanged: return "種類変更"
        case .untracked: return "未追跡"
        case .ignored: return "無視"
        case .conflict: return "競合"
        }
    }

    var isTracked: Bool { self != .untracked && self != .ignored }
}

struct StatusEntry: Identifiable, Hashable {
    var path: String          // relative to repo root
    var origPath: String?     // for renames / copies
    var x: Character
    var y: Character

    var id: String { path }

    var state: FileState {
        if x == "?" { return .untracked }
        if x == "!" { return .ignored }
        if x == "U" || y == "U" || (x == "A" && y == "A") || (x == "D" && y == "D") { return .conflict }
        if x == "R" || y == "R" { return .renamed }
        if x == "C" || y == "C" { return .copied }
        if x == "A" { return y == "D" ? .deleted : .added }
        if x == "D" || y == "D" { return .deleted }
        if x == "T" || y == "T" { return .typeChanged }
        return .modified
    }

    var staged: Bool { x != " " && x != "?" && x != "!" && state != .conflict }
    var fileName: String { (path as NSString).lastPathComponent }
    var ext: String { (path as NSString).pathExtension }
    var displayPath: String {
        if let o = origPath { return "\(o) → \(path)" }
        return path
    }
}

struct Commit: Identifiable, Hashable {
    var id: String { hash }
    var hash: String
    var short: String
    var parents: [String]
    var author: String
    var email: String
    var date: Date
    var refs: [String]
    var subject: String
    var body: String
}

struct ChangedFile: Identifiable, Hashable {
    var id: String { path + (oldPath ?? "") }
    var status: String   // M, A, D, R100 ...
    var path: String
    var oldPath: String?

    var state: FileState {
        switch status.first {
        case "A": return .added
        case "D": return .deleted
        case "R": return .renamed
        case "C": return .copied
        case "T": return .typeChanged
        case "U": return .conflict
        case "?": return .untracked
        default: return .modified
        }
    }
}

struct RefInfo: Hashable, Identifiable {
    enum Kind { case local, remote, tag }
    var id: String { fullName }
    var fullName: String
    var name: String
    var kind: Kind
    var hash: String
    var upstream: String
    var isHead: Bool
}

struct StashEntry: Identifiable, Hashable {
    var id: String { ref }
    var ref: String
    var message: String
    var date: Date
}

struct RefDetail: Identifiable, Hashable {
    var id: String { fullName }
    var fullName: String
    var name: String
    var kind: RefInfo.Kind
    var hash: String
    var upstream: String
    var isHead: Bool
    var date: Date
    var subject: String
    var track: String
    var author: String

    var kindLabel: String {
        switch kind { case .local: return "ローカル"; case .remote: return "リモート"; case .tag: return "タグ" }
    }
}

struct ReflogEntry: Identifiable, Hashable {
    var id: String { selector }
    var hash: String
    var short: String
    var selector: String
    var subject: String
    var date: Date
}

struct TreeEntry: Identifiable, Hashable {
    var id: String { path }
    var mode: String
    var type: String
    var hash: String
    var size: Int
    var name: String
    var path: String
    var isDir: Bool { type == "tree" }
    var isSubmodule: Bool { type == "commit" }
}

struct RemoteInfo: Identifiable, Hashable {
    var id: String { name }
    var name: String
    var fetchURL: String
    var pushURL: String
}

struct SubmoduleInfo: Identifiable, Hashable {
    var id: String { path }
    var state: Character
    var hash: String
    var path: String
    var describe: String
    var stateLabel: String {
        switch state {
        case "-": return "未初期化"
        case "+": return "コミットが異なる"
        case "U": return "競合"
        default: return "最新"
        }
    }
}

enum RepoOperationState {
    case none, merging, rebasing, cherryPicking, reverting, applyingPatches, bisecting
    var label: String {
        switch self {
        case .none: return ""
        case .merging: return "マージ中"
        case .rebasing: return "リベース中"
        case .cherryPicking: return "チェリーピック中"
        case .reverting: return "リバート中"
        case .applyingPatches: return "パッチ適用中"
        case .bisecting: return "二分探索中"
        }
    }
    /// git sub-command used for --continue / --abort
    var command: String? {
        switch self {
        case .merging: return "merge"
        case .rebasing: return "rebase"
        case .cherryPicking: return "cherry-pick"
        case .reverting: return "revert"
        case .applyingPatches: return "am"
        case .none, .bisecting: return nil
        }
    }
}

// MARK: - Repository

final class Repo {
    let root: String
    init(root: String) { self.root = root.trimmedPath }

    var name: String { (root as NSString).lastPathComponent }

    @discardableResult
    func git(_ args: [String], stdin: Data? = nil) -> GitResult { Git.run(args, in: root, stdin: stdin) }

    func git(_ args: [String], stdin: Data? = nil) async -> GitResult { await Git.runAsync(args, in: root, stdin: stdin) }

    static func at(_ path: String) -> Repo? {
        PathUtil.findRepoRoot(path).map { Repo(root: $0) }
    }

    /// Path relative to the repository root ("" for root itself).
    func relative(_ path: String) -> String {
        let p = path.trimmedPath
        if p == root { return "" }
        if p.hasPrefix(root + "/") { return String(p.dropFirst(root.count + 1)) }
        // Possibly differing unicode normalization
        let pn = p.nfc, rn = root.nfc
        if pn.hasPrefix(rn + "/") { return String(pn.dropFirst(rn.count + 1)) }
        return p
    }

    func absolute(_ rel: String) -> String { rel.isEmpty ? root : root + "/" + rel }

    lazy var gitDir: String = {
        let r = git(["rev-parse", "--absolute-git-dir"])
        let s = r.out.trimmingCharacters(in: .whitespacesAndNewlines)
        return s.isEmpty ? root + "/.git" : s
    }()

    func currentBranch() -> String? {
        let r = git(["symbolic-ref", "--short", "-q", "HEAD"])
        let s = r.out.trimmingCharacters(in: .whitespacesAndNewlines)
        return s.isEmpty ? nil : s
    }

    func headDescription() -> String {
        if let b = currentBranch() { return b }
        let r = git(["rev-parse", "--short", "HEAD"])
        let s = r.out.trimmingCharacters(in: .whitespacesAndNewlines)
        return s.isEmpty ? "(コミットなし)" : "HEAD 切り離し @ \(s)"
    }

    func hasHead() -> Bool { git(["rev-parse", "--verify", "-q", "HEAD"]).ok }

    func operationState() -> RepoOperationState {
        let fm = FileManager.default
        if fm.fileExists(atPath: gitDir + "/rebase-apply/applying") { return .applyingPatches }
        if fm.fileExists(atPath: gitDir + "/rebase-merge") || fm.fileExists(atPath: gitDir + "/rebase-apply") { return .rebasing }
        if fm.fileExists(atPath: gitDir + "/MERGE_HEAD") { return .merging }
        if fm.fileExists(atPath: gitDir + "/CHERRY_PICK_HEAD") { return .cherryPicking }
        if fm.fileExists(atPath: gitDir + "/REVERT_HEAD") { return .reverting }
        if fm.fileExists(atPath: gitDir + "/BISECT_LOG") { return .bisecting }
        return .none
    }

    func mergeMessage() -> String? {
        try? String(contentsOfFile: gitDir + "/MERGE_MSG", encoding: .utf8)
    }

    // MARK: status

    func status(paths: [String] = [], untracked: String = "all", ignored: Bool = false) -> [StatusEntry] {
        var args = ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=\(untracked)"]
        if ignored { args.append("--ignored=matching") }
        let rels = paths.map { relative($0) }.filter { !$0.isEmpty }
        if !rels.isEmpty { args.append("--"); args += rels }
        let r = git(args)
        return Repo.parseStatus(r.stdout)
    }

    static func parseStatus(_ data: Data) -> [StatusEntry] {
        let tokens = data.split(separator: 0, omittingEmptySubsequences: true).map { String(decoding: $0, as: UTF8.self) }
        var result: [StatusEntry] = []
        var i = 0
        while i < tokens.count {
            let t = tokens[i]
            guard t.count >= 4 else { i += 1; continue }
            let chars = Array(t)
            let x = chars[0], y = chars[1]
            let path = String(chars[3...])
            var e = StatusEntry(path: path, origPath: nil, x: x, y: y)
            if x == "R" || x == "C" || y == "R" || y == "C" {
                if i + 1 < tokens.count { e.origPath = tokens[i + 1]; i += 1 }
            }
            result.append(e)
            i += 1
        }
        return result
    }

    // MARK: log

    static let logFormat = "%H%x1f%h%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s%x1f%b%x1e"

    func log(limit: Int, skip: Int = 0, all: Bool = false, paths: [String] = [], search: String = "",
             rev: String? = nil, since: Date? = nil, until: Date? = nil, author: String = "") -> [Commit] {
        var args = ["log", "--date-order", "--format=" + Repo.logFormat, "-n", "\(limit)", "--skip=\(skip)"]
        if all { args.append("--all") } else if let rev, !rev.isEmpty { args.append(rev) }
        if let since { args.append("--since=\(Int(since.timeIntervalSince1970))") }
        if let until { args.append("--until=\(Int(until.timeIntervalSince1970))") }
        if !author.isEmpty { args += ["-i", "--author=\(author)"] }
        let rels = paths.map { relative($0) }.filter { !$0.isEmpty }
        if !rels.isEmpty { args.append("--parents") }
        if !search.isEmpty { args += ["-i", "--grep=\(search)"] }
        if !rels.isEmpty { args.append("--"); args += rels }
        let r = git(args)
        return Repo.parseLog(r.out)
    }

    /// Commits in a revision range such as "origin/main..HEAD".
    func commits(range: String, limit: Int = 300, reverse: Bool = false, noMerges: Bool = false) -> [Commit] {
        var args = ["log", "--format=" + Repo.logFormat, "-n", "\(limit)"]
        if reverse { args.append("--reverse") }
        if noMerges { args.append("--no-merges") }
        args.append(range)
        let r = git(args)
        return r.ok ? Repo.parseLog(r.out) : []
    }

    static func parseLog(_ out: String) -> [Commit] {
        out.split(separator: "\u{1e}").compactMap { rec in
            var s = String(rec)
            if s.hasPrefix("\n") { s.removeFirst() }
            let f = s.components(separatedBy: "\u{1f}")
            guard f.count >= 9 else { return nil }
            return Commit(hash: f[0], short: f[1],
                          parents: f[2].split(separator: " ").map(String.init),
                          author: f[3], email: f[4],
                          date: Date(timeIntervalSince1970: TimeInterval(f[5]) ?? 0),
                          refs: f[6].isEmpty ? [] : f[6].components(separatedBy: ", "),
                          subject: f[7], body: f[8].trimmingCharacters(in: .whitespacesAndNewlines))
        }
    }

    func changedFiles(of commit: Commit) -> [ChangedFile] {
        let r: GitResult
        if let p = commit.parents.first {
            r = git(["diff", "--name-status", "-z", "-M", p, commit.hash])
        } else {
            r = git(["diff-tree", "--root", "--no-commit-id", "-r", "--name-status", "-z", "-M", commit.hash])
        }
        return Repo.parseNameStatus(r.stdout)
    }

    func changedFiles(from: String, to: String) -> [ChangedFile] {
        Repo.parseNameStatus(git(["diff", "--name-status", "-z", "-M", from, to]).stdout)
    }

    static func parseNameStatus(_ data: Data) -> [ChangedFile] {
        let tokens = data.split(separator: 0, omittingEmptySubsequences: true).map { String(decoding: $0, as: UTF8.self) }
        var result: [ChangedFile] = []
        var i = 0
        while i < tokens.count {
            let st = tokens[i]
            if st.first == "R" || st.first == "C" {
                guard i + 2 < tokens.count else { break }
                result.append(ChangedFile(status: st, path: tokens[i + 2], oldPath: tokens[i + 1]))
                i += 3
            } else {
                guard i + 1 < tokens.count else { break }
                result.append(ChangedFile(status: st, path: tokens[i + 1], oldPath: nil))
                i += 2
            }
        }
        return result
    }

    // MARK: refs

    func refs() -> [RefInfo] {
        let fmt = "%(refname)%1f%(refname:short)%1f%(objectname:short)%1f%(upstream:short)%1f%(HEAD)"
        let r = git(["for-each-ref", "--format=" + fmt, "refs/heads", "refs/remotes", "refs/tags"])
        return r.out.split(separator: "\n").compactMap { line in
            let f = line.components(separatedBy: "\u{1f}")
            guard f.count >= 5 else { return nil }
            let kind: RefInfo.Kind
            if f[0].hasPrefix("refs/heads/") { kind = .local }
            else if f[0].hasPrefix("refs/remotes/") { kind = .remote }
            else { kind = .tag }
            if kind == .remote && f[0].hasSuffix("/HEAD") { return nil }
            return RefInfo(fullName: f[0], name: f[1], kind: kind, hash: f[2], upstream: f[3], isHead: f[4] == "*")
        }
    }

    func remotes() -> [String] {
        git(["remote"]).out.split(separator: "\n").map(String.init)
    }

    func upstream(of branch: String) -> (remote: String, branch: String)? {
        let remote = git(["config", "--get", "branch.\(branch).remote"]).out.trimmingCharacters(in: .whitespacesAndNewlines)
        let merge = git(["config", "--get", "branch.\(branch).merge"]).out.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !remote.isEmpty, !merge.isEmpty else { return nil }
        return (remote, merge.replacingOccurrences(of: "refs/heads/", with: ""))
    }

    func stashes() -> [StashEntry] {
        let r = git(["stash", "list", "--format=%gd%x1f%s%x1f%ct"])
        return r.out.split(separator: "\n").compactMap { line in
            let f = line.components(separatedBy: "\u{1f}")
            guard f.count >= 3 else { return nil }
            return StashEntry(ref: f[0], message: f[1], date: Date(timeIntervalSince1970: TimeInterval(f[2]) ?? 0))
        }
    }

    var isBisecting: Bool { FileManager.default.fileExists(atPath: gitDir + "/BISECT_LOG") }
    var hasSubmodules: Bool { FileManager.default.fileExists(atPath: root + "/.gitmodules") }

    func refsDetailed() -> [RefDetail] {
        let fmt = "%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)%1f%(creatordate:unix)%1f%(subject)%1f%(upstream:track)%1f%(authorname)%(taggername)"
        let r = git(["for-each-ref", "--format=" + fmt, "refs/heads", "refs/remotes", "refs/tags"])
        return r.out.split(separator: "\n").compactMap { line in
            let f = line.components(separatedBy: "\u{1f}")
            guard f.count >= 9 else { return nil }
            let kind: RefInfo.Kind
            if f[0].hasPrefix("refs/heads/") { kind = .local }
            else if f[0].hasPrefix("refs/remotes/") { kind = .remote }
            else { kind = .tag }
            if kind == .remote && f[0].hasSuffix("/HEAD") { return nil }
            return RefDetail(fullName: f[0], name: f[1], kind: kind, hash: f[2], upstream: f[3], isHead: f[4] == "*",
                             date: Date(timeIntervalSince1970: TimeInterval(f[5]) ?? 0), subject: f[6], track: f[7], author: f[8])
        }
    }

    func reflog(limit: Int = 1000) -> [ReflogEntry] {
        let r = git(["reflog", "-n", "\(limit)", "--format=%H%x1f%h%x1f%gd%x1f%gs%x1f%ct"])
        return r.out.split(separator: "\n").compactMap { line in
            let f = line.components(separatedBy: "\u{1f}")
            guard f.count >= 5 else { return nil }
            return ReflogEntry(hash: f[0], short: f[1], selector: f[2], subject: f[3],
                               date: Date(timeIntervalSince1970: TimeInterval(f[4]) ?? 0))
        }
    }

    func lsTree(rev: String, path: String) -> [TreeEntry] {
        let spec = rev + ":" + path
        let r = git(["ls-tree", "-z", "-l", spec])
        guard r.ok else { return [] }
        return r.stdout.split(separator: 0).compactMap { rec in
            let s = String(decoding: rec, as: UTF8.self)
            guard let tab = s.firstIndex(of: "\t") else { return nil }
            let meta = s[..<tab].split(separator: " ", omittingEmptySubsequences: true)
            guard meta.count >= 4 else { return nil }
            let name = String(s[s.index(after: tab)...])
            return TreeEntry(mode: String(meta[0]), type: String(meta[1]), hash: String(meta[2]),
                             size: Int(meta[3]) ?? -1, name: name, path: path.isEmpty ? name : path + "/" + name)
        }.sorted { a, b in
            if a.isDir != b.isDir { return a.isDir }
            return a.name.localizedStandardCompare(b.name) == .orderedAscending
        }
    }

    func remotesDetailed() -> [RemoteInfo] {
        var dict: [String: RemoteInfo] = [:]
        var order: [String] = []
        for line in git(["remote", "-v"]).out.split(separator: "\n") {
            let parts = line.split(whereSeparator: { $0 == "\t" || $0 == " " }).map(String.init)
            guard parts.count >= 3 else { continue }
            if dict[parts[0]] == nil { dict[parts[0]] = RemoteInfo(name: parts[0], fetchURL: "", pushURL: ""); order.append(parts[0]) }
            if parts[2] == "(fetch)" { dict[parts[0]]?.fetchURL = parts[1] } else { dict[parts[0]]?.pushURL = parts[1] }
        }
        for name in remotes() where dict[name] == nil { dict[name] = RemoteInfo(name: name, fetchURL: "", pushURL: ""); order.append(name) }
        return order.compactMap { dict[$0] }
    }

    func submodules() -> [SubmoduleInfo] {
        git(["submodule", "status", "--recursive"]).out.split(separator: "\n").compactMap { line in
            guard let first = line.first else { return nil }
            let body = line.dropFirst()
            let parts = body.split(separator: " ", maxSplits: 2).map(String.init)
            guard parts.count >= 2 else { return nil }
            return SubmoduleInfo(state: first, hash: parts[0], path: parts[1], describe: parts.count > 2 ? parts[2] : "")
        }
    }

    /// Writes the blob at rev:path into a temp file and returns its path.
    func exportBlob(rev: String, path: String) -> String? {
        let r = git(["show", "\(rev):\(path)"])
        guard r.ok else { return nil }
        let dir = KGTemp.url("blob-\(UUID().uuidString.prefix(8))")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let short = rev.count > 10 && rev.allSatisfy(\.isHexDigit) ? String(rev.prefix(8)) : rev.replacingOccurrences(of: "/", with: "_")
        let name = (path as NSString).lastPathComponent
        let ext = (name as NSString).pathExtension
        let base = (name as NSString).deletingPathExtension
        let url = dir.appendingPathComponent(ext.isEmpty ? "\(base)-\(short)" : "\(base)-\(short).\(ext)")
        do { try r.stdout.write(to: url) } catch { return nil }
        return url.path
    }

    func config(_ key: String) -> String {
        git(["config", "--get", key]).out.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Writes pathspecs to a temp file for `--pathspec-from-file` (avoids argv limits).
    func pathspecFile(_ rels: [String]) -> String? {
        let url = KGTemp.url("pathspec-\(UUID().uuidString)")
        var data = Data()
        for r in rels { data.append(Data(r.utf8)); data.append(0) }
        do { try data.write(to: url) } catch { return nil }
        return url.path
    }

    func messageFile(_ message: String) -> String? {
        let url = KGTemp.url("msg-\(UUID().uuidString).txt")
        do { try Data(message.utf8).write(to: url) } catch { return nil }
        return url.path
    }
}

// MARK: - Diff model

struct DiffLine: Identifiable {
    enum Kind { case meta, hunk, context, add, del, note }
    let id: Int
    let kind: Kind
    let oldNo: Int?
    let newNo: Int?
    let text: String
}

enum DiffParser {
    static func parse(_ text: String) -> [DiffLine] {
        var lines: [DiffLine] = []
        var oldNo = 0, newNo = 0
        var inHunk = false
        var idx = 0
        for raw in text.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = String(raw)
            if line.hasPrefix("@@") {
                inHunk = true
                // @@ -a,b +c,d @@
                let parts = line.split(separator: " ")
                if parts.count >= 3 {
                    oldNo = Int(parts[1].dropFirst().split(separator: ",").first ?? "") ?? 0
                    newNo = Int(parts[2].dropFirst().split(separator: ",").first ?? "") ?? 0
                }
                lines.append(DiffLine(id: idx, kind: .hunk, oldNo: nil, newNo: nil, text: line))
            } else if line.hasPrefix("diff ") {
                inHunk = false
                lines.append(DiffLine(id: idx, kind: .meta, oldNo: nil, newNo: nil, text: line))
            } else if inHunk, line.hasPrefix("+") {
                lines.append(DiffLine(id: idx, kind: .add, oldNo: nil, newNo: newNo, text: String(line.dropFirst())))
                newNo += 1
            } else if inHunk, line.hasPrefix("-") {
                lines.append(DiffLine(id: idx, kind: .del, oldNo: oldNo, newNo: nil, text: String(line.dropFirst())))
                oldNo += 1
            } else if inHunk, line.hasPrefix(" ") {
                lines.append(DiffLine(id: idx, kind: .context, oldNo: oldNo, newNo: newNo, text: String(line.dropFirst())))
                oldNo += 1; newNo += 1
            } else if line.hasPrefix("\\") {
                lines.append(DiffLine(id: idx, kind: .note, oldNo: nil, newNo: nil, text: line))
            } else if inHunk && line.isEmpty {
                continue
            } else {
                inHunk = false
                lines.append(DiffLine(id: idx, kind: .meta, oldNo: nil, newNo: nil, text: line))
            }
            idx += 1
        }
        while let last = lines.last, last.kind == .meta, last.text.isEmpty { lines.removeLast() }
        return lines
    }

    struct SideRow: Identifiable {
        let id: Int
        let left: DiffLine?
        let right: DiffLine?
        let header: DiffLine?
    }

    static func sideBySide(_ lines: [DiffLine]) -> [SideRow] {
        var rows: [SideRow] = []
        var dels: [DiffLine] = [], adds: [DiffLine] = []
        var n = 0
        func flush() {
            let c = max(dels.count, adds.count)
            for i in 0..<c {
                rows.append(SideRow(id: n, left: i < dels.count ? dels[i] : nil, right: i < adds.count ? adds[i] : nil, header: nil))
                n += 1
            }
            dels.removeAll(); adds.removeAll()
        }
        for l in lines {
            switch l.kind {
            case .del: if !adds.isEmpty { flush() }; dels.append(l)
            case .add: adds.append(l)
            case .context: flush(); rows.append(SideRow(id: n, left: l, right: l, header: nil)); n += 1
            default: flush(); rows.append(SideRow(id: n, left: nil, right: nil, header: l)); n += 1
            }
        }
        flush()
        return rows
    }
}
