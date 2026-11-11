using System.Text.RegularExpressions;

namespace GitVelto.Core;

/// <summary>
/// Translates common git output into Japanese (git ships no Japanese catalog).
/// Unknown lines are returned unchanged. Rules are generated from the macOS version (mac/Sources/App/Core/GitMessages.swift).
/// </summary>
public static class GitMessages
{
    private sealed record Rule(Regex Regex, string Template);

    private static Rule R(string pattern, string template) => new(new Regex(pattern, RegexOptions.CultureInvariant), template);

    /// <summary>Whole-line rules (first match wins).</summary>
    private static readonly Rule[] LineRules =
    [
        R(@"^(remote: )?Enumerating objects: (.*)$", "$1オブジェクトを列挙中: $2"),
        R(@"^(remote: )?Counting objects: (.*)$", "$1オブジェクトを数えています: $2"),
        R(@"^(remote: )?Compressing objects: (.*)$", "$1オブジェクトを圧縮中: $2"),
        R(@"^(remote: )?Total (\d+) \(delta (\d+)\), reused (\d+) \(delta (\d+)\)(.*)$", "$1合計 $2 (差分 $3)、再利用 $4 (差分 $5)$6"),
        R(@"^Receiving objects: (.*)$", "オブジェクトを受信中: $1"),
        R(@"^Resolving deltas: (.*)$", "差分を解決中: $1"),
        R(@"^Writing objects: (.*)$", "オブジェクトを書き込み中: $1"),
        R(@"^Delta compression using up to (\d+) threads$", "最大 $1 スレッドで差分圧縮を実行"),
        R(@"^Updating files: (.*)$", "ファイルを更新中: $1"),
        R(@"^Unpacking objects: (.*)$", "オブジェクトを展開中: $1"),
        R(@"^Checking connectivity: (.*)$", "接続性を確認中: $1"),
        R(@"^Filtering content: (.*)$", "内容をフィルタ中: $1"),
        R(@"^Cloning into bare repository '(.*)'\.\.\.$", "ベアリポジトリ '$1' にクローンしています…"),
        R(@"^Cloning into '(.*)'\.\.\.$", "'$1' にクローンしています…"),
        R(@"^From (.*)$", "取得元: $1"),
        R(@"^To (.*)$", "送信先: $1"),
        R(@"^Everything up-to-date$", "すべて最新です"),
        R(@"^Already up to date\.?$", "すでに最新です。"),
        R(@"^Already up-to-date\.?$", "すでに最新です。"),
        R(@"^Current branch (.*) is up to date\.$", "現在のブランチ $1 は最新です。"),
        R(@"^Fetching (.*)$", "$1 をフェッチしています"),
        R(@"^ \* \[new branch\]\s+(.*)$", " * [新しいブランチ]  $1"),
        R(@"^ \* \[new tag\]\s+(.*)$", " * [新しいタグ]  $1"),
        R(@"^ - \[deleted\]\s+(.*)$", " - [削除]  $1"),
        R(@"^ ! \[rejected\]\s+(.*) \(fetch first\)$", " ! [拒否]  $1 (先にフェッチが必要)"),
        R(@"^ ! \[rejected\]\s+(.*) \(non-fast-forward\)$", " ! [拒否]  $1 (早送りできません)"),
        R(@"^ ! \[rejected\]\s+(.*)$", " ! [拒否]  $1"),
        R(@"^ ! \[remote rejected\]\s+(.*)$", " ! [リモートが拒否]  $1"),
        R(@"^branch '(.*)' set up to track '(.*)'\.$", "ブランチ '$1' が '$2' を追跡するよう設定しました。"),
        R(@"^Branch '(.*)' set up to track remote branch '(.*)' from '(.*)'\.$", "ブランチ '$1' が '$3' のリモートブランチ '$2' を追跡するよう設定しました。"),
        R(@"^Updates were rejected because the remote contains work that you do not$", "リモートにローカルにない変更が含まれているため、更新が拒否されました。"),
        R(@"^Updates were rejected because the remote contains work that you do not have locally\..*$", "リモートにローカルにない変更が含まれているため、更新が拒否されました。先にプルしてください。"),
        R(@"^Updates were rejected because the tip of your current branch is behind$", "現在のブランチの先端がリモートより遅れているため、更新が拒否されました。"),
        R(@"^Permission denied \(publickey\)\.?$", "アクセスが拒否されました (公開鍵認証)。"),
        R(@"^Could not read from remote repository\.$", "リモートリポジトリから読み込めませんでした。"),
        R(@"^Please make sure you have the correct access rights$", "アクセス権限があること、"),
        R(@"^and the repository exists\.$", "およびリポジトリが存在することを確認してください。"),
        R(@"^Authentication failed for '(.*)'$", "'$1' の認証に失敗しました"),
        R(@"^Repository not found\.$", "リポジトリが見つかりません。"),
        R(@"^No configured push destination\.$", "プッシュ先が設定されていません。"),
        R(@"^The current branch (.*) has no upstream branch\.$", "現在のブランチ $1 には上流ブランチがありません。"),
        R(@"^There is no tracking information for the current branch\.$", "現在のブランチには追跡情報がありません。"),
        R(@"^Updating ([0-9a-f]+)\.\.([0-9a-f]+)$", "更新中 $1..$2"),
        R(@"^Fast-forward$", "早送り (fast-forward)"),
        R(@"^Merge made by the '(.*)' strategy\.$", "'$1' 戦略でマージしました。"),
        R(@"^Auto-merging (.*)$", "自動マージ中: $1"),
        R(@"^CONFLICT \(content\): Merge conflict in (.*)$", "競合 (内容): $1 でマージ競合が発生しました"),
        R(@"^CONFLICT \(modify/delete\): (.*)$", "競合 (変更/削除): $1"),
        R(@"^CONFLICT \(add/add\): Merge conflict in (.*)$", "競合 (追加/追加): $1 でマージ競合が発生しました"),
        R(@"^CONFLICT \((.*)\): (.*)$", "競合 ($1): $2"),
        R(@"^Automatic merge failed; fix conflicts and then commit the result\.$", "自動マージに失敗しました。競合を解決してからコミットしてください。"),
        R(@"^Squash commit -- not updating HEAD$", "スカッシュコミット — HEAD は更新されません"),
        R(@"^Automatic merge went well; stopped before committing as requested$", "自動マージは成功しました。指定どおりコミット前で停止しています"),
        R(@"^Not possible to fast-forward, aborting\.$", "早送りできないため中止しました。"),
        R(@"^Need to specify how to reconcile divergent branches\.$", "分岐したブランチをどう統合するか指定が必要です。"),
        R(@"^Successfully rebased and updated (.*)\.$", "リベースに成功し、$1 を更新しました。"),
        R(@"^Created autostash: (.*)$", "自動退避を作成しました: $1"),
        R(@"^Applied autostash\.$", "自動退避を適用しました。"),
        R(@"^Rebasing \((\d+)/(\d+)\)$", "リベース中 ($1/$2)"),
        R(@"^Stopped at ([0-9a-f]+)\.\.\.\s*(.*)$", "$1 で停止しました… $2"),
        R(@"^You can amend the commit now, with$", "次のコマンドでコミットを修正できます"),
        R(@"^Could not apply ([0-9a-f]+)\.\.\. (.*)$", "$1 を適用できませんでした… $2"),
        R(@"^ (\d+) files? changed(, (\d+) insertions?\(\+\))?(, (\d+) deletions?\(-\))?$", " $1 個のファイルを変更 (追加 $3 行 / 削除 $5 行)"),
        R(@"^ create mode (\d+) (.*)$", " 作成 (モード $1) $2"),
        R(@"^ delete mode (\d+) (.*)$", " 削除 (モード $1) $2"),
        R(@"^ rename (.*) \((\d+)%\)$", " 名前変更 $1 ($2%)"),
        R(@"^nothing to commit, working tree clean$", "コミットする変更はありません。作業ツリーはクリーンです"),
        R(@"^nothing added to commit but untracked files present.*$", "コミットに追加された変更はありません (未追跡ファイルがあります)"),
        R(@"^no changes added to commit.*$", "コミットに追加された変更はありません"),
        R(@"^Your branch is up to date with '(.*)'\.$", "ブランチは '$1' と同じ状態です。"),
        R(@"^Your branch is ahead of '(.*)' by (\d+) commits?\.$", "ブランチは '$1' より $2 コミット進んでいます。"),
        R(@"^Your branch is behind '(.*)' by (\d+) commits?, and can be fast-forwarded\.$", "ブランチは '$1' より $2 コミット遅れています (早送り可能)。"),
        R(@"^Your branch and '(.*)' have diverged,$", "ブランチと '$1' は分岐しています。"),
        R(@"^Author identity unknown$", "作成者情報が設定されていません"),
        R(@"^Version ([0-9a-f]+) of (.*) left in tree\.$", "$2 は $1 の版が作業ツリーに残されています。"),
        R(@"^(\s+)\(use ""git pull"" to update your local branch\)$", "$1(\"git pull\" でローカルブランチを更新)"),
        R(@"^(\s+)\(use ""git pull"" to merge the remote branch into yours\)$", "$1(\"git pull\" でリモートブランチを取り込み)"),
        R(@"^On branch (.*)$", "ブランチ $1"),
        R(@"^HEAD detached at (.*)$", "HEAD は $1 で切り離されています"),
        R(@"^Changes to be committed:$", "コミット予定の変更:"),
        R(@"^Changes not staged for commit:$", "ステージされていない変更:"),
        R(@"^Untracked files:$", "未追跡のファイル:"),
        R(@"^Unmerged paths:$", "マージされていないパス:"),
        R(@"^You have unmerged paths\.$", "マージされていないパスがあります。"),
        R(@"^All conflicts fixed but you are still merging\.$", "すべての競合は解決されましたが、マージ中です。"),
        R(@"^and have (\d+) and (\d+) different commits each, respectively\.$", "それぞれ $1 個と $2 個の異なるコミットがあります。"),
        R(@"^(\s+)\(use ""(.*)"" to unstage\)$", "$1(\"$2\" でステージを取り消し)"),
        R(@"^(\s+)\(use ""(.*)"" to update what will be committed\)$", "$1(\"$2\" でコミット対象を更新)"),
        R(@"^(\s+)\(use ""(.*)"" to discard changes in working directory\)$", "$1(\"$2\" で作業ディレクトリの変更を破棄)"),
        R(@"^(\s+)\(use ""(.*)"" to include in what will be committed\)$", "$1(\"$2\" でコミットに含める)"),
        R(@"^(\s+)\(use ""(.*)"" if you want to integrate the remote branch with yours\)$", "$1(リモートブランチを統合するには \"$2\")"),
        R(@"^(\s+)\(use ""(.*)"" to publish your local commits\)$", "$1(\"$2\" でローカルのコミットを公開)"),
        R(@"^(\s+)\(use ""(.*)"" to mark resolution\)$", "$1(\"$2\" で解決済みにする)"),
        R(@"^(\s+)\(fix conflicts and run ""(.*)""\)$", "$1(競合を解決して \"$2\" を実行)"),
        R(@"^(\s+)\(use ""(.*)"" to abort the merge\)$", "$1(\"$2\" でマージを中止)"),
        R(@"^(\s+)modified:\s+(.*)$", "$1変更:     $2"),
        R(@"^(\s+)new file:\s+(.*)$", "$1新規:     $2"),
        R(@"^(\s+)deleted:\s+(.*)$", "$1削除:     $2"),
        R(@"^(\s+)renamed:\s+(.*)$", "$1名前変更: $2"),
        R(@"^(\s+)both modified:\s+(.*)$", "$1両方で変更: $2"),
        R(@"^(\s+)both added:\s+(.*)$", "$1両方で追加: $2"),
        R(@"^(\s+)deleted by us:\s+(.*)$", "$1こちらで削除: $2"),
        R(@"^(\s+)deleted by them:\s+(.*)$", "$1相手側で削除: $2"),
        R(@"^no changes added to commit \(use ""git add"" and/or ""git commit -a""\)$", "コミットに追加された変更はありません"),
        R(@"^Switched to branch '(.*)'$", "ブランチ '$1' に切り替えました"),
        R(@"^Switched to a new branch '(.*)'$", "新しいブランチ '$1' に切り替えました"),
        R(@"^Switched to and reset branch '(.*)'$", "ブランチ '$1' をリセットして切り替えました"),
        R(@"^Already on '(.*)'$", "すでに '$1' にいます"),
        R(@"^HEAD is now at ([0-9a-f]+) (.*)$", "HEAD は $1 ($2) になりました"),
        R(@"^Previous HEAD position was ([0-9a-f]+) (.*)$", "以前の HEAD 位置: $1 $2"),
        R(@"^Deleted branch (.*) \(was (.*)\)\.$", "ブランチ $1 を削除しました (旧 $2)"),
        R(@"^Deleted tag '(.*)' \(was (.*)\)$", "タグ '$1' を削除しました (旧 $2)"),
        R(@"^Saved working directory and index state (.*)$", "作業ディレクトリとインデックスの状態を退避しました: $1"),
        R(@"^Dropped (.*) \((.*)\)$", "$1 を削除しました ($2)"),
        R(@"^No local changes to save$", "退避するローカルの変更はありません"),
        R(@"^No stash entries found\.$", "退避された変更はありません。"),
        R(@"^Unstaged changes after reset:$", "リセット後のステージされていない変更:"),
        R(@"^Initialized empty Git repository in (.*)$", "空の Git リポジトリを作成しました: $1"),
        R(@"^Reinitialized existing Git repository in (.*)$", "既存の Git リポジトリを再初期化しました: $1"),
        R(@"^Removing (.*)$", "削除: $1"),
        R(@"^Would remove (.*)$", "削除予定: $1"),
        R(@"^Applying: (.*)$", "適用中: $1"),
        R(@"^Using index info to reconstruct a base tree\.\.\.$", "インデックス情報からベースツリーを再構築しています…"),
        R(@"^Falling back to patching base and 3-way merge\.\.\.$", "ベースへのパッチ適用と 3-way マージに切り替えます…"),
        R(@"^Patch failed at (\d+) (.*)$", "パッチ $1 ($2) の適用に失敗しました"),
        R(@"^The copy of the patch that failed is found in: (.*)$", "失敗したパッチの写し: $1"),
        R(@"^Submodule '(.*)' \((.*)\) registered for path '(.*)'$", "サブモジュール '$1' ($2) をパス '$3' に登録しました"),
        R(@"^Submodule path '(.*)': checked out '(.*)'$", "サブモジュール '$1': '$2' をチェックアウトしました"),
        R(@"^Synchronizing submodule url for '(.*)'$", "サブモジュール '$1' の URL を同期しています"),
        R(@"^Bisecting: (\d+) revisions? left to test after this \(roughly (\d+) steps?\)$", "二分探索中: 残り $1 リビジョン (約 $2 ステップ)"),
        R(@"^([0-9a-f]+) is the first bad commit$", "$1 が最初の不具合コミットです"),
        R(@"^status: waiting for both good and bad commits$", "状態: 良いコミットと悪いコミットの両方を待っています"),
        R(@"^status: waiting for good commit\(s\), bad commit known$", "状態: 良いコミットを待っています (悪いコミットは指定済み)"),
        R(@"^We are not bisecting\.$", "二分探索中ではありません。"),
    ];

    /// <summary>Prefix rules applied after line rules (on the remaining text).</summary>
    private static readonly (string En, string Ja)[] Prefixes =
    [
        ("fatal: ", "致命的エラー: "),
        ("error: ", "エラー: "),
        ("warning: ", "警告: "),
        ("hint: ", "ヒント: "),
        ("remote: ", "リモート: "),
    ];

    /// <summary>Phrases translated after prefix stripping.</summary>
    private static readonly Rule[] Phrases =
    [
        R(@"^not a git repository \(or any of the parent directories\): \.git$", "Git リポジトリではありません (親ディレクトリにも .git がありません)"),
        R(@"^pathspec '(.*)' did not match any files?( known to git)?$", "パス指定 '$1' に一致するファイルがありません"),
        R(@"^A branch named '(.*)' already exists\.$", "'$1' という名前のブランチは既に存在します。"),
        R(@"^tag '(.*)' already exists$", "タグ '$1' は既に存在します"),
        R(@"^invalid reference: (.*)$", "無効な参照です: $1"),
        R(@"^couldn't find remote ref (.*)$", "リモート参照 $1 が見つかりません"),
        R(@"^'(.*)' is not a valid branch name\.?$", "'$1' は有効なブランチ名ではありません。"),
        R(@"^Your local changes to the following files would be overwritten by (checkout|merge):$", "次のファイルのローカルの変更が上書きされます ($1):"),
        R(@"^Please commit your changes or stash them before you (switch branches|merge)\.$", "切り替え/マージの前に変更をコミットするか退避してください。"),
        R(@"^Aborting$", "中止しました"),
        R(@"^failed to push some refs to '(.*)'$", "'$1' へ一部の参照をプッシュできませんでした"),
        R(@"^Not a valid object name:? '?(.*?)'?\.?$", "有効なオブジェクト名ではありません: $1"),
        R(@"^You have not concluded your merge \(MERGE_HEAD exists\)\.$", "マージが完了していません (MERGE_HEAD が存在します)。"),
        R(@"^Committing is not possible because you have unmerged files\.$", "マージされていないファイルがあるためコミットできません。"),
        R(@"^cannot do a partial commit during a merge\.$", "マージ中は一部のファイルだけをコミットできません。"),
        R(@"^the branch '(.*)' is not fully merged\.?$", "ブランチ '$1' は完全にはマージされていません。"),
        R(@"^could not apply (.*)$", "$1 を適用できませんでした"),
        R(@"^If you are sure you want to delete it, run 'git branch -D (.*)'\.?$", "本当に削除する場合は 'git branch -D $1' (強制削除) を実行してください。"),
        R(@"^Failed to merge in the changes\.$", "変更をマージできませんでした。"),
        R(@"^Use 'git am --show-current-patch=diff' to see the failed patch$", "失敗したパッチは 'git am --show-current-patch=diff' で確認できます"),
        R(@"^When you have resolved this problem, run ""git (am|rebase|cherry-pick|revert) --continue""\.$", "問題を解決したら「競合を解決」画面の「続行」(git $1 --continue) を実行してください。"),
        R(@"^If you prefer to skip this patch, run ""git am --skip"" instead\.$", "このパッチを飛ばす場合は \"git am --skip\" を実行してください。"),
        R(@"^To restore the original branch and stop patching, run ""git am --abort""\.$", "元のブランチに戻して中止するには「中止 (abort)」(git am --abort) を実行してください。"),
        R(@"^Disable this message with ""git config set advice\.(.*) false""$", "このメッセージは \"git config set advice.$1 false\" で無効にできます"),
        R(@"^Resolve all conflicts manually, mark them as resolved with$", "すべての競合を手動で解決し、解決済みにしてから"),
        R(@"^""git add/rm <conflicted_files>"", then run ""git (rebase|cherry-pick|revert) --continue""\.$", "\"git add/rm <競合ファイル>\" の後、\"git $1 --continue\" を実行してください。"),
        R(@"^You can instead skip this commit: run ""git (rebase|cherry-pick) --skip""\.$", "このコミットを飛ばす場合は \"git $1 --skip\" を実行してください。"),
        R(@"^To abort and get back to the state before ""git (rebase|cherry-pick)"", run ""git (rebase|cherry-pick) --abort""\.$", "中止して元に戻すには \"git $2 --abort\" を実行してください。"),
        R(@"^after resolving the conflicts, mark the corrected paths$", "競合を解決したら、修正したパスを"),
        R(@"^with 'git add <paths>' or 'git rm <paths>'$", "'git add <パス>' または 'git rm <パス>' で解決済みにしてください"),
        R(@"^Updates were rejected because the remote contains work that you do not$", "リモートにローカルにない変更が含まれているため、更新が拒否されました。"),
        R(@"^have locally\. This is usually caused by another repository pushing to$", "これは通常、別のリポジトリが同じ参照にプッシュしたことが原因です。"),
        R(@"^the same ref\. If you want to integrate the remote changes, use$", "リモートの変更を取り込むには、"),
        R(@"^the same ref\. You may want to first integrate the remote changes$", "先にリモートの変更を取り込んでください"),
        R(@"^'git pull' before pushing again\.$", "再度プッシュする前に 'git pull' を実行してください。"),
        R(@"^\(e\.g\., 'git pull \.\.\.'\) before pushing again\.$", "(例: 'git pull …') その後で再度プッシュしてください。"),
        R(@"^its remote counterpart\. If you want to integrate the remote changes,$", "リモート側より遅れています。リモートの変更を取り込むには、"),
        R(@"^use 'git pull' before pushing again\.$", "再度プッシュする前に 'git pull' を実行してください。"),
        R(@"^See the 'Note about fast-forwards' in 'git push --help' for details\.$", "詳しくは 'git push --help' の 'Note about fast-forwards' を参照してください。"),
        R(@"^Updates were rejected because the tip of your current branch is behind$", "現在のブランチの先端がリモートより遅れているため、更新が拒否されました。"),
        R(@"^unable to access '(.*)': (.*)$", "'$1' にアクセスできません: $2"),
        R(@"^destination path '(.*)' already exists and is not an empty directory\.$", "クローン先 '$1' は既に存在し、空ではありません。"),
        R(@"^remote (.*) already exists\.$", "リモート $1 は既に存在します。"),
        R(@"^No such remote: '?(.*?)'?$", "リモート $1 は存在しません"),
        R(@"^Unable to create '(.*)': File exists\.$", "'$1' を作成できません: ファイルが存在します (他の git が実行中の可能性があります)。"),
        R(@"^detected dubious ownership in repository at '(.*)'$", "リポジトリ '$1' の所有者が不審です (safe.directory の設定が必要です)"),
    ];

    public static string TranslateLine(string line)
    {
        foreach (var rule in LineRules)
        {
            var m = rule.Regex.Match(line);
            if (m.Success) return m.Result(rule.Template);
        }
        var rest = line;
        var head = "";
        foreach (var (en, ja) in Prefixes)
        {
            if (!rest.StartsWith(en, StringComparison.Ordinal)) continue;
            head = ja;
            rest = rest[en.Length..];
            break;
        }
        foreach (var rule in Phrases)
        {
            var m = rule.Regex.Match(rest);
            if (m.Success) return head + m.Result(rule.Template);
        }
        if (head.Length > 0 && head != "リモート: ")
        {
            // try line rules on the remainder too (e.g. "error: CONFLICT...")
            return head + TranslateLine(rest);
        }
        return head + rest;
    }

    public static string Translate(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(TranslateLine));
}
