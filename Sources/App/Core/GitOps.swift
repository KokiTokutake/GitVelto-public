import Foundation

/// Builds the git command sequences for the main operations (kept out of the views so they can be tested).
enum GitOps {
    struct CommitOptions {
        var message: String
        var amend = false
        var author: String? = nil
        var date: Date? = nil
        var newBranch: String? = nil
        var push = false
    }

    /// TortoiseGit-style commit: only the checked entries are committed (staged state of others is kept).
    static func commitSteps(repo: Repo, entries: [StatusEntry], options o: CommitOptions) -> [ProgressStep] {
        var rels: [String] = []
        for e in entries {
            rels.append(e.path)
            if let orig = e.origPath { rels.append(orig) }
        }
        let merging = repo.operationState() == .merging
        var steps: [ProgressStep] = []
        if let b = o.newBranch, !b.isEmpty { steps.append(.git(["switch", "-c", b])) }
        let tempError = ProgressStep.custom("一時ファイルを作成しています") {
            throw GitError(message: "一時ファイルを作成できません: \(KGTemp.dir.path)")
        }
        if !rels.isEmpty {
            guard let f = repo.pathspecFile(rels) else { return [tempError] }
            steps.append(.git(["--literal-pathspecs", "add", "-A", "--pathspec-from-file=\(f)", "--pathspec-file-nul"]))
        }
        guard let msgFile = repo.messageFile(o.message) else { return [tempError] }
        var args = ["--literal-pathspecs", "commit", "-F", msgFile, "--cleanup=strip"]
        if o.amend { args.append("--amend") }
        if let a = o.author, !a.isEmpty { args.append("--author=\(a)") }
        if let d = o.date {
            args.append("--date=" + ISO8601DateFormatter.string(from: d, timeZone: .current, formatOptions: [.withInternetDateTime]))
        }
        if merging {
            // Partial commits are not allowed during a merge: commit the whole index.
        } else if rels.isEmpty {
            args.append(o.amend ? "--only" : "--allow-empty")
        } else {
            guard let f = repo.pathspecFile(rels) else { return [tempError] }
            args += ["--pathspec-from-file=\(f)", "--pathspec-file-nul"]
        }
        steps.append(.git(args))

        if o.push {
            let branch = (o.newBranch?.isEmpty == false) ? o.newBranch : repo.currentBranch()
            if let b = branch, repo.upstream(of: b) != nil {
                steps.append(.git(["push", "--progress"] + (o.amend ? ["--force-with-lease"] : [])))
            } else if let b = branch, let remote = repo.remotes().first {
                steps.append(.git(["push", "--progress", "-u", remote, b]))
            }
        }
        return steps
    }

    /// Revert (discard) changes of the given entries. Added files become untracked again (file is kept).
    static func revertSteps(repo: Repo, entries: [StatusEntry]) -> [ProgressStep] {
        var steps: [ProgressStep] = []
        let added = entries.filter { $0.state == .added }.map(\.path)
        let renamedNew = entries.filter { $0.state == .renamed || $0.state == .copied }.map(\.path)
        var restore = entries.filter { $0.state != .added && $0.state != .renamed && $0.state != .copied && $0.state.isTracked }.map(\.path)
        restore += entries.compactMap(\.origPath)
        let unstage = added + renamedNew
        if !unstage.isEmpty, let f = repo.pathspecFile(unstage) {
            steps.append(.git(["--literal-pathspecs", "rm", "--cached", "-r", "-q", "--pathspec-from-file=\(f)", "--pathspec-file-nul"]))
        }
        if !restore.isEmpty, let f = repo.pathspecFile(restore) {
            if repo.hasHead() {
                steps.append(.git(["--literal-pathspecs", "restore", "--source=HEAD", "--staged", "--worktree", "--pathspec-from-file=\(f)", "--pathspec-file-nul"]))
            } else {
                steps.append(.git(["--literal-pathspecs", "restore", "--worktree", "--pathspec-from-file=\(f)", "--pathspec-file-nul"]))
            }
        }
        return steps
    }

    /// Interactive rebase driven by a prepared todo list (GIT_SEQUENCE_EDITOR copies it into place).
    static func rebaseStep(repo: Repo, items: [RebaseItem], originalOrder: [String], upstream: String, autostash: Bool) throws -> ProgressStep {
        if let first = items.first(where: { $0.action != .drop }), first.action == .squash || first.action == .fixup {
            throw GitError(message: "最初のコミットは統合 (squash/fixup) にできません")
        }
        let unchanged = items.map(\.id) == originalOrder && items.allSatisfy { $0.action == .pick }
        var args = ["rebase"]
        if autostash { args.append("--autostash") }
        var env: [String: String] = [:]
        if !unchanged {
            var todo: [String] = []
            for it in items {
                switch it.action {
                case .reword:
                    todo.append("pick \(it.commit.hash) \(it.commit.subject)")
                    if let f = repo.messageFile(it.message) {
                        todo.append("exec git commit --amend --no-verify -F '\(f)'")
                    }
                default:
                    todo.append("\(it.action.rawValue) \(it.commit.hash) \(it.commit.subject)")
                }
            }
            let url = KGTemp.url("rebase-todo-\(UUID().uuidString)")
            try Data((todo.joined(separator: "\n") + "\n").utf8).write(to: url)
            args.append("-i")
            env["GIT_SEQUENCE_EDITOR"] = "cp '\(url.path)'"
        }
        args.append(upstream)
        return .gitEnv(args, env)
    }

    /// Runs steps synchronously (used by tests and simple flows). Returns combined output and success.
    @discardableResult
    static func runSync(_ steps: [ProgressStep], in dir: String) -> (ok: Bool, output: String) {
        var out = ""
        for s in steps {
            switch s {
            case .git(let a):
                let r = Git.run(a, in: dir); out += r.out + r.err
                if !r.ok { return (false, out) }
            case .gitEnv(let a, let env):
                let r = Git.run(a, in: dir, env: env); out += r.out + r.err
                if !r.ok { return (false, out) }
            case .custom(_, let work):
                do { out += try work() } catch { return (false, out + error.localizedDescription) }
            }
        }
        return (true, out)
    }
}
