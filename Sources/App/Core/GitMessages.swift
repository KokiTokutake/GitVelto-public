import Foundation

/// Translates common git output into Japanese (git itself ships no Japanese catalog).
/// Unknown lines are returned unchanged.
enum GitMessages {
    private struct Rule {
        let regex: NSRegularExpression
        let template: String
    }

    private static func r(_ pattern: String, _ template: String) -> Rule {
        // swiftlint:disable:next force_try
        Rule(regex: try! NSRegularExpression(pattern: pattern, options: []), template: template)
    }

    /// Whole-line rules (first match wins).
    private static let lineRules: [Rule] = [
        // progress
        r(#"^(remote: )?Enumerating objects: (.*)$"#, "$1オブジェクトを列挙中: $2"),
        r(#"^(remote: )?Counting objects: (.*)$"#, "$1オブジェクトを数えています: $2"),
        r(#"^(remote: )?Compressing objects: (.*)$"#, "$1オブジェクトを圧縮中: $2"),
        r(#"^(remote: )?Total (\d+) \(delta (\d+)\), reused (\d+) \(delta (\d+)\)(.*)$"#, "$1合計 $2 (差分 $3)、再利用 $4 (差分 $5)$6"),
        r(#"^Receiving objects: (.*)$"#, "オブジェクトを受信中: $1"),
        r(#"^Resolving deltas: (.*)$"#, "差分を解決中: $1"),
        r(#"^Writing objects: (.*)$"#, "オブジェクトを書き込み中: $1"),
        r(#"^Delta compression using up to (\d+) threads$"#, "最大 $1 スレッドで差分圧縮を実行"),
        r(#"^Updating files: (.*)$"#, "ファイルを更新中: $1"),
        r(#"^Unpacking objects: (.*)$"#, "オブジェクトを展開中: $1"),
        r(#"^Checking connectivity: (.*)$"#, "接続性を確認中: $1"),
        r(#"^Filtering content: (.*)$"#, "内容をフィルタ中: $1"),
        // clone / fetch / push
        r(#"^Cloning into bare repository '(.*)'\.\.\.$"#, "ベアリポジトリ '$1' にクローンしています…"),
        r(#"^Cloning into '(.*)'\.\.\.$"#, "'$1' にクローンしています…"),
        r(#"^From (.*)$"#, "取得元: $1"),
        r(#"^To (.*)$"#, "送信先: $1"),
        r(#"^Everything up-to-date$"#, "すべて最新です"),
        r(#"^Already up to date\.?$"#, "すでに最新です。"),
        r(#"^Already up-to-date\.?$"#, "すでに最新です。"),
        r(#"^Current branch (.*) is up to date\.$"#, "現在のブランチ $1 は最新です。"),
        r(#"^Fetching (.*)$"#, "$1 をフェッチしています"),
        r(#"^ \* \[new branch\]\s+(.*)$"#, " * [新しいブランチ]  $1"),
        r(#"^ \* \[new tag\]\s+(.*)$"#, " * [新しいタグ]  $1"),
        r(#"^ - \[deleted\]\s+(.*)$"#, " - [削除]  $1"),
        r(#"^ ! \[rejected\]\s+(.*) \(fetch first\)$"#, " ! [拒否]  $1 (先にフェッチが必要)"),
        r(#"^ ! \[rejected\]\s+(.*) \(non-fast-forward\)$"#, " ! [拒否]  $1 (早送りできません)"),
        r(#"^ ! \[rejected\]\s+(.*)$"#, " ! [拒否]  $1"),
        r(#"^ ! \[remote rejected\]\s+(.*)$"#, " ! [リモートが拒否]  $1"),
        r(#"^branch '(.*)' set up to track '(.*)'\.$"#, "ブランチ '$1' が '$2' を追跡するよう設定しました。"),
        r(#"^Branch '(.*)' set up to track remote branch '(.*)' from '(.*)'\.$"#, "ブランチ '$1' が '$3' のリモートブランチ '$2' を追跡するよう設定しました。"),
        r(#"^Updates were rejected because the remote contains work that you do not$"#, "リモートにローカルにない変更が含まれているため、更新が拒否されました。"),
        r(#"^Updates were rejected because the remote contains work that you do not have locally\..*$"#, "リモートにローカルにない変更が含まれているため、更新が拒否されました。先にプルしてください。"),
        r(#"^Updates were rejected because the tip of your current branch is behind$"#, "現在のブランチの先端がリモートより遅れているため、更新が拒否されました。"),
        r(#"^Permission denied \(publickey\)\.?$"#, "アクセスが拒否されました (公開鍵認証)。"),
        r(#"^Could not read from remote repository\.$"#, "リモートリポジトリから読み込めませんでした。"),
        r(#"^Please make sure you have the correct access rights$"#, "アクセス権限があること、"),
        r(#"^and the repository exists\.$"#, "およびリポジトリが存在することを確認してください。"),
        r(#"^Authentication failed for '(.*)'$"#, "'$1' の認証に失敗しました"),
        r(#"^Repository not found\.$"#, "リポジトリが見つかりません。"),
        r(#"^No configured push destination\.$"#, "プッシュ先が設定されていません。"),
        r(#"^The current branch (.*) has no upstream branch\.$"#, "現在のブランチ $1 には上流ブランチがありません。"),
        r(#"^There is no tracking information for the current branch\.$"#, "現在のブランチには追跡情報がありません。"),
        // merge / pull
        r(#"^Updating ([0-9a-f]+)\.\.([0-9a-f]+)$"#, "更新中 $1..$2"),
        r(#"^Fast-forward$"#, "早送り (fast-forward)"),
        r(#"^Merge made by the '(.*)' strategy\.$"#, "'$1' 戦略でマージしました。"),
        r(#"^Auto-merging (.*)$"#, "自動マージ中: $1"),
        r(#"^CONFLICT \(content\): Merge conflict in (.*)$"#, "競合 (内容): $1 でマージ競合が発生しました"),
        r(#"^CONFLICT \(modify/delete\): (.*)$"#, "競合 (変更/削除): $1"),
        r(#"^CONFLICT \(add/add\): Merge conflict in (.*)$"#, "競合 (追加/追加): $1 でマージ競合が発生しました"),
        r(#"^CONFLICT \((.*)\): (.*)$"#, "競合 ($1): $2"),
        r(#"^Automatic merge failed; fix conflicts and then commit the result\.$"#, "自動マージに失敗しました。競合を解決してからコミットしてください。"),
        r(#"^Squash commit -- not updating HEAD$"#, "スカッシュコミット — HEAD は更新されません"),
        r(#"^Automatic merge went well; stopped before committing as requested$"#, "自動マージは成功しました。指定どおりコミット前で停止しています"),
        r(#"^Not possible to fast-forward, aborting\.$"#, "早送りできないため中止しました。"),
        r(#"^Need to specify how to reconcile divergent branches\.$"#, "分岐したブランチをどう統合するか指定が必要です。"),
        r(#"^Successfully rebased and updated (.*)\.$"#, "リベースに成功し、$1 を更新しました。"),
        r(#"^Created autostash: (.*)$"#, "自動退避を作成しました: $1"),
        r(#"^Applied autostash\.$"#, "自動退避を適用しました。"),
        r(#"^Rebasing \((\d+)/(\d+)\)$"#, "リベース中 ($1/$2)"),
        r(#"^Stopped at ([0-9a-f]+)\.\.\.\s*(.*)$"#, "$1 で停止しました… $2"),
        r(#"^You can amend the commit now, with$"#, "次のコマンドでコミットを修正できます"),
        r(#"^Could not apply ([0-9a-f]+)\.\.\. (.*)$"#, "$1 を適用できませんでした… $2"),
        // commit
        r(#"^ (\d+) files? changed(, (\d+) insertions?\(\+\))?(, (\d+) deletions?\(-\))?$"#, " $1 個のファイルを変更 (追加 $3 行 / 削除 $5 行)"),
        r(#"^ create mode (\d+) (.*)$"#, " 作成 (モード $1) $2"),
        r(#"^ delete mode (\d+) (.*)$"#, " 削除 (モード $1) $2"),
        r(#"^ rename (.*) \((\d+)%\)$"#, " 名前変更 $1 ($2%)"),
        r(#"^nothing to commit, working tree clean$"#, "コミットする変更はありません。作業ツリーはクリーンです"),
        r(#"^nothing added to commit but untracked files present.*$"#, "コミットに追加された変更はありません (未追跡ファイルがあります)"),
        r(#"^no changes added to commit.*$"#, "コミットに追加された変更はありません"),
        r(#"^Your branch is up to date with '(.*)'\.$"#, "ブランチは '$1' と同じ状態です。"),
        r(#"^Your branch is ahead of '(.*)' by (\d+) commits?\.$"#, "ブランチは '$1' より $2 コミット進んでいます。"),
        r(#"^Your branch is behind '(.*)' by (\d+) commits?, and can be fast-forwarded\.$"#, "ブランチは '$1' より $2 コミット遅れています (早送り可能)。"),
        r(#"^Your branch and '(.*)' have diverged,$"#, "ブランチと '$1' は分岐しています。"),
        r(#"^Author identity unknown$"#, "作成者情報が設定されていません"),
        r(#"^Version ([0-9a-f]+) of (.*) left in tree\.$"#, "$2 は $1 の版が作業ツリーに残されています。"),
        r(#"^(\s+)\(use "git pull" to update your local branch\)$"#, "$1(\"git pull\" でローカルブランチを更新)"),
        r(#"^(\s+)\(use "git pull" to merge the remote branch into yours\)$"#, "$1(\"git pull\" でリモートブランチを取り込み)"),
        // status (long format)
        r(#"^On branch (.*)$"#, "ブランチ $1"),
        r(#"^HEAD detached at (.*)$"#, "HEAD は $1 で切り離されています"),
        r(#"^Changes to be committed:$"#, "コミット予定の変更:"),
        r(#"^Changes not staged for commit:$"#, "ステージされていない変更:"),
        r(#"^Untracked files:$"#, "未追跡のファイル:"),
        r(#"^Unmerged paths:$"#, "マージされていないパス:"),
        r(#"^You have unmerged paths\.$"#, "マージされていないパスがあります。"),
        r(#"^All conflicts fixed but you are still merging\.$"#, "すべての競合は解決されましたが、マージ中です。"),
        r(#"^and have (\d+) and (\d+) different commits each, respectively\.$"#, "それぞれ $1 個と $2 個の異なるコミットがあります。"),
        r(#"^(\s+)\(use "(.*)" to unstage\)$"#, "$1(\"$2\" でステージを取り消し)"),
        r(#"^(\s+)\(use "(.*)" to update what will be committed\)$"#, "$1(\"$2\" でコミット対象を更新)"),
        r(#"^(\s+)\(use "(.*)" to discard changes in working directory\)$"#, "$1(\"$2\" で作業ディレクトリの変更を破棄)"),
        r(#"^(\s+)\(use "(.*)" to include in what will be committed\)$"#, "$1(\"$2\" でコミットに含める)"),
        r(#"^(\s+)\(use "(.*)" if you want to integrate the remote branch with yours\)$"#, "$1(リモートブランチを統合するには \"$2\")"),
        r(#"^(\s+)\(use "(.*)" to publish your local commits\)$"#, "$1(\"$2\" でローカルのコミットを公開)"),
        r(#"^(\s+)\(use "(.*)" to mark resolution\)$"#, "$1(\"$2\" で解決済みにする)"),
        r(#"^(\s+)\(fix conflicts and run "(.*)"\)$"#, "$1(競合を解決して \"$2\" を実行)"),
        r(#"^(\s+)\(use "(.*)" to abort the merge\)$"#, "$1(\"$2\" でマージを中止)"),
        r(#"^(\s+)modified:\s+(.*)$"#, "$1変更:     $2"),
        r(#"^(\s+)new file:\s+(.*)$"#, "$1新規:     $2"),
        r(#"^(\s+)deleted:\s+(.*)$"#, "$1削除:     $2"),
        r(#"^(\s+)renamed:\s+(.*)$"#, "$1名前変更: $2"),
        r(#"^(\s+)both modified:\s+(.*)$"#, "$1両方で変更: $2"),
        r(#"^(\s+)both added:\s+(.*)$"#, "$1両方で追加: $2"),
        r(#"^(\s+)deleted by us:\s+(.*)$"#, "$1こちらで削除: $2"),
        r(#"^(\s+)deleted by them:\s+(.*)$"#, "$1相手側で削除: $2"),
        r(#"^no changes added to commit \(use "git add" and/or "git commit -a"\)$"#, "コミットに追加された変更はありません"),
        // branch / switch / stash / misc
        r(#"^Switched to branch '(.*)'$"#, "ブランチ '$1' に切り替えました"),
        r(#"^Switched to a new branch '(.*)'$"#, "新しいブランチ '$1' に切り替えました"),
        r(#"^Switched to and reset branch '(.*)'$"#, "ブランチ '$1' をリセットして切り替えました"),
        r(#"^Already on '(.*)'$"#, "すでに '$1' にいます"),
        r(#"^HEAD is now at ([0-9a-f]+) (.*)$"#, "HEAD は $1 ($2) になりました"),
        r(#"^Previous HEAD position was ([0-9a-f]+) (.*)$"#, "以前の HEAD 位置: $1 $2"),
        r(#"^Deleted branch (.*) \(was (.*)\)\.$"#, "ブランチ $1 を削除しました (旧 $2)"),
        r(#"^Deleted tag '(.*)' \(was (.*)\)$"#, "タグ '$1' を削除しました (旧 $2)"),
        r(#"^Saved working directory and index state (.*)$"#, "作業ディレクトリとインデックスの状態を退避しました: $1"),
        r(#"^Dropped (.*) \((.*)\)$"#, "$1 を削除しました ($2)"),
        r(#"^No local changes to save$"#, "退避するローカルの変更はありません"),
        r(#"^No stash entries found\.$"#, "退避された変更はありません。"),
        r(#"^Unstaged changes after reset:$"#, "リセット後のステージされていない変更:"),
        r(#"^Initialized empty Git repository in (.*)$"#, "空の Git リポジトリを作成しました: $1"),
        r(#"^Reinitialized existing Git repository in (.*)$"#, "既存の Git リポジトリを再初期化しました: $1"),
        r(#"^Removing (.*)$"#, "削除: $1"),
        r(#"^Would remove (.*)$"#, "削除予定: $1"),
        r(#"^Applying: (.*)$"#, "適用中: $1"),
        r(#"^Using index info to reconstruct a base tree\.\.\.$"#, "インデックス情報からベースツリーを再構築しています…"),
        r(#"^Falling back to patching base and 3-way merge\.\.\.$"#, "ベースへのパッチ適用と 3-way マージに切り替えます…"),
        r(#"^Patch failed at (\d+) (.*)$"#, "パッチ $1 ($2) の適用に失敗しました"),
        r(#"^The copy of the patch that failed is found in: (.*)$"#, "失敗したパッチの写し: $1"),
        r(#"^Submodule '(.*)' \((.*)\) registered for path '(.*)'$"#, "サブモジュール '$1' ($2) をパス '$3' に登録しました"),
        r(#"^Submodule path '(.*)': checked out '(.*)'$"#, "サブモジュール '$1': '$2' をチェックアウトしました"),
        r(#"^Synchronizing submodule url for '(.*)'$"#, "サブモジュール '$1' の URL を同期しています"),
        r(#"^Bisecting: (\d+) revisions? left to test after this \(roughly (\d+) steps?\)$"#, "二分探索中: 残り $1 リビジョン (約 $2 ステップ)"),
        r(#"^([0-9a-f]+) is the first bad commit$"#, "$1 が最初の不具合コミットです"),
        r(#"^status: waiting for both good and bad commits$"#, "状態: 良いコミットと悪いコミットの両方を待っています"),
        r(#"^status: waiting for good commit\(s\), bad commit known$"#, "状態: 良いコミットを待っています (悪いコミットは指定済み)"),
        r(#"^We are not bisecting\.$"#, "二分探索中ではありません。"),
    ]

    /// Prefix rules applied after line rules (on the remaining text).
    private static let prefixes: [(String, String)] = [
        ("fatal: ", "致命的エラー: "),
        ("error: ", "エラー: "),
        ("warning: ", "警告: "),
        ("hint: ", "ヒント: "),
        ("remote: ", "リモート: "),
    ]

    /// Phrases translated anywhere within a line (used after prefix stripping).
    private static let phrases: [Rule] = [
        r(#"^not a git repository \(or any of the parent directories\): \.git$"#, "Git リポジトリではありません (親ディレクトリにも .git がありません)"),
        r(#"^pathspec '(.*)' did not match any files?( known to git)?$"#, "パス指定 '$1' に一致するファイルがありません"),
        r(#"^A branch named '(.*)' already exists\.$"#, "'$1' という名前のブランチは既に存在します。"),
        r(#"^tag '(.*)' already exists$"#, "タグ '$1' は既に存在します"),
        r(#"^invalid reference: (.*)$"#, "無効な参照です: $1"),
        r(#"^couldn't find remote ref (.*)$"#, "リモート参照 $1 が見つかりません"),
        r(#"^'(.*)' is not a valid branch name\.?$"#, "'$1' は有効なブランチ名ではありません。"),
        r(#"^Your local changes to the following files would be overwritten by (checkout|merge):$"#, "次のファイルのローカルの変更が上書きされます ($1):"),
        r(#"^Please commit your changes or stash them before you (switch branches|merge)\.$"#, "切り替え/マージの前に変更をコミットするか退避してください。"),
        r(#"^Aborting$"#, "中止しました"),
        r(#"^failed to push some refs to '(.*)'$"#, "'$1' へ一部の参照をプッシュできませんでした"),
        r(#"^Not a valid object name:? '?(.*?)'?\.?$"#, "有効なオブジェクト名ではありません: $1"),
        r(#"^You have not concluded your merge \(MERGE_HEAD exists\)\.$"#, "マージが完了していません (MERGE_HEAD が存在します)。"),
        r(#"^Committing is not possible because you have unmerged files\.$"#, "マージされていないファイルがあるためコミットできません。"),
        r(#"^cannot do a partial commit during a merge\.$"#, "マージ中は一部のファイルだけをコミットできません。"),
        r(#"^the branch '(.*)' is not fully merged\.?$"#, "ブランチ '$1' は完全にはマージされていません。"),
        r(#"^could not apply (.*)$"#, "$1 を適用できませんでした"),
        r(#"^Failed to merge in the changes\.$"#, "変更をマージできませんでした。"),
        r(#"^Use 'git am --show-current-patch=diff' to see the failed patch$"#, "失敗したパッチは 'git am --show-current-patch=diff' で確認できます"),
        r(#"^When you have resolved this problem, run "git (am|rebase|cherry-pick|revert) --continue"\.$"#, "問題を解決したら「競合を解決」画面の「続行」(git $1 --continue) を実行してください。"),
        r(#"^If you prefer to skip this patch, run "git am --skip" instead\.$"#, "このパッチを飛ばす場合は \"git am --skip\" を実行してください。"),
        r(#"^To restore the original branch and stop patching, run "git am --abort"\.$"#, "元のブランチに戻して中止するには「中止 (abort)」(git am --abort) を実行してください。"),
        r(#"^Disable this message with "git config set advice\.(.*) false"$"#, "このメッセージは \"git config set advice.$1 false\" で無効にできます"),
        r(#"^Resolve all conflicts manually, mark them as resolved with$"#, "すべての競合を手動で解決し、解決済みにしてから"),
        r(#"^"git add/rm <conflicted_files>", then run "git (rebase|cherry-pick|revert) --continue"\.$"#, "\"git add/rm <競合ファイル>\" の後、\"git $1 --continue\" を実行してください。"),
        r(#"^You can instead skip this commit: run "git (rebase|cherry-pick) --skip"\.$"#, "このコミットを飛ばす場合は \"git $1 --skip\" を実行してください。"),
        r(#"^To abort and get back to the state before "git (rebase|cherry-pick)", run "git (rebase|cherry-pick) --abort"\.$"#, "中止して元に戻すには \"git $2 --abort\" を実行してください。"),
        r(#"^after resolving the conflicts, mark the corrected paths$"#, "競合を解決したら、修正したパスを"),
        r(#"^with 'git add <paths>' or 'git rm <paths>'$"#, "'git add <パス>' または 'git rm <パス>' で解決済みにしてください"),
        r(#"^Updates were rejected because the remote contains work that you do not$"#, "リモートにローカルにない変更が含まれているため、更新が拒否されました。"),
        r(#"^have locally\. This is usually caused by another repository pushing to$"#, "これは通常、別のリポジトリが同じ参照にプッシュしたことが原因です。"),
        r(#"^the same ref\. If you want to integrate the remote changes, use$"#, "リモートの変更を取り込むには、"),
        r(#"^the same ref\. You may want to first integrate the remote changes$"#, "先にリモートの変更を取り込んでください"),
        r(#"^'git pull' before pushing again\.$"#, "再度プッシュする前に 'git pull' を実行してください。"),
        r(#"^\(e\.g\., 'git pull \.\.\.'\) before pushing again\.$"#, "(例: 'git pull …') その後で再度プッシュしてください。"),
        r(#"^its remote counterpart\. If you want to integrate the remote changes,$"#, "リモート側より遅れています。リモートの変更を取り込むには、"),
        r(#"^use 'git pull' before pushing again\.$"#, "再度プッシュする前に 'git pull' を実行してください。"),
        r(#"^See the 'Note about fast-forwards' in 'git push --help' for details\.$"#, "詳しくは 'git push --help' の 'Note about fast-forwards' を参照してください。"),
        r(#"^Updates were rejected because the tip of your current branch is behind$"#, "現在のブランチの先端がリモートより遅れているため、更新が拒否されました。"),
        r(#"^unable to access '(.*)': (.*)$"#, "'$1' にアクセスできません: $2"),
        r(#"^destination path '(.*)' already exists and is not an empty directory\.$"#, "クローン先 '$1' は既に存在し、空ではありません。"),
        r(#"^remote (.*) already exists\.$"#, "リモート $1 は既に存在します。"),
        r(#"^No such remote: '?(.*?)'?$"#, "リモート $1 は存在しません"),
        r(#"^Unable to create '(.*)': File exists\.$"#, "'$1' を作成できません: ファイルが存在します (他の git が実行中の可能性があります)。"),
        r(#"^detected dubious ownership in repository at '(.*)'$"#, "リポジトリ '$1' の所有者が不審です (safe.directory の設定が必要です)"),
    ]

    static func translate(line: String) -> String {
        let ns = line as NSString
        let full = NSRange(location: 0, length: ns.length)
        for rule in lineRules {
            if let m = rule.regex.firstMatch(in: line, range: full) {
                return rule.regex.replacementString(for: m, in: line, offset: 0, template: rule.template)
            }
        }
        var rest = line
        var head = ""
        for (en, ja) in prefixes where rest.hasPrefix(en) {
            head = ja
            rest = String(rest.dropFirst(en.count))
            break
        }
        let rns = rest as NSString
        let rfull = NSRange(location: 0, length: rns.length)
        for rule in phrases {
            if let m = rule.regex.firstMatch(in: rest, range: rfull) {
                return head + rule.regex.replacementString(for: m, in: rest, offset: 0, template: rule.template)
            }
        }
        if !head.isEmpty, head != "リモート: " {
            // try line rules on the remainder too (e.g. "error: CONFLICT...")
            let t = translate(line: rest)
            return head + t
        }
        return head + rest
    }

    static func translate(_ text: String) -> String {
        text.split(separator: "\n", omittingEmptySubsequences: false)
            .map { translate(line: String($0)) }
            .joined(separator: "\n")
    }
}
