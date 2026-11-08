import Foundation

// MARK: - Glossary

/// One Git term explained for beginners.
struct GlossaryEntry: Identifiable, Hashable {
    let id: String          // key used by TermHelp ("commit", "rebase", ...)
    let term: String        // 日本語の見出し
    let english: String     // English / git keyword
    let summary: String     // one-line explanation
    let detail: String      // longer explanation with an example
    var command: String = ""  // typical command
    var related: [String] = []

    var searchText: String { [id, term, english, summary, detail, command].joined(separator: " ").lowercased() }
}

enum Glossary {
    static let entries: [GlossaryEntry] = [
        GlossaryEntry(id: "repository", term: "リポジトリ", english: "repository / repo",
                      summary: "ファイルとその変更履歴をまとめて管理する保管場所。",
                      detail: "プロジェクトのフォルダの中に隠しフォルダ .git があると、そのフォルダはリポジトリです。.git の中にすべてのコミット（履歴）が保存されています。自分のパソコンにあるものを「ローカルリポジトリ」、GitHub などのサーバーにあるものを「リモートリポジトリ」と呼びます。",
                      command: "git init / git clone <URL>", related: ["remote", "clone", "worktree"]),
        GlossaryEntry(id: "worktree", term: "作業ツリー", english: "working tree / working directory",
                      summary: "実際に編集しているファイルが置かれたフォルダの状態。",
                      detail: "Finder で見えているファイルそのものです。ここで編集した内容は、ステージしてコミットするまで履歴には残りません。「作業ツリーの変更」とは、最後のコミットから変わったファイルのことです。",
                      command: "git status", related: ["stage", "commit", "head"]),
        GlossaryEntry(id: "commit", term: "コミット", english: "commit",
                      summary: "その時点のファイルの状態を、メッセージ付きで履歴に記録すること（または記録そのもの）。",
                      detail: "セーブポイントのようなものです。コミットには作成者・日時・メッセージ・一意の ID（ハッシュ。例: 1e6d4ec）が付きます。あとから過去のコミットの状態を確認したり、戻したりできます。GitVelto では「Git コミット…」でチェックしたファイルだけを記録します。",
                      command: "git commit -m \"メッセージ\"", related: ["stage", "hash", "amend", "push"]),
        GlossaryEntry(id: "stage", term: "ステージ（インデックス）", english: "stage / index / staging area",
                      summary: "次のコミットに含める変更を一時的に置いておく場所。",
                      detail: "コマンドでは git add で変更をステージしてから git commit します。GitVelto のコミット画面ではチェックを付けたファイルを自動でステージしてからコミットするので、ステージを意識しなくても使えます。「ステージ済」と表示されているものは、すでに git add された変更です。",
                      command: "git add <ファイル> / git restore --staged <ファイル>", related: ["commit", "worktree", "untracked"]),
        GlossaryEntry(id: "untracked", term: "未追跡", english: "untracked",
                      summary: "Git がまだ管理していない新しいファイル。",
                      detail: "新しく作ったファイルは、追加（git add）してコミットするまで Git の管理外です。Finder では灰色の「?」アイコンで表示されます。管理したくないファイルは「無視リストに追加」します。",
                      command: "git add <ファイル>", related: ["stage", "ignore", "tracked"]),
        GlossaryEntry(id: "tracked", term: "追跡中", english: "tracked",
                      summary: "Git が管理しているファイル。",
                      detail: "一度でもコミットされたファイルは追跡中になり、変更があると「変更あり」として検出されます。",
                      related: ["untracked", "modified"]),
        GlossaryEntry(id: "modified", term: "変更あり", english: "modified",
                      summary: "最後のコミットから内容が変わった追跡中のファイル。",
                      detail: "Finder では赤い「!」アイコンで表示されます。差分で何が変わったか確認し、コミットするか、元に戻すかを選びます。",
                      command: "git diff", related: ["diff", "commit", "revert-file"]),
        GlossaryEntry(id: "ignore", term: "無視（.gitignore）", english: "ignore / .gitignore",
                      summary: "Git に管理させないファイルのパターンを書いておく仕組み。",
                      detail: "ビルド結果やログ、パスワードを含む設定ファイルなどは .gitignore に書いて無視させます。例: *.log と書くと .log で終わるファイルがすべて無視されます。",
                      command: "echo '*.log' >> .gitignore", related: ["untracked", "clean"]),
        GlossaryEntry(id: "diff", term: "差分", english: "diff",
                      summary: "2 つの状態のあいだで、どの行が追加・削除されたかを表したもの。",
                      detail: "緑（+）が追加された行、赤（-）が削除された行です。GitVelto では「差分」や、ファイルのダブルクリックで表示できます。",
                      command: "git diff / git diff HEAD~1 HEAD", related: ["modified", "commit"]),
        GlossaryEntry(id: "hash", term: "ハッシュ（コミット ID）", english: "hash / SHA",
                      summary: "コミットを一意に識別する英数字の ID。",
                      detail: "例: 1e6d4ec67c9d9bcfe06f0c...。先頭の 7 文字程度（短縮ハッシュ）で指定できます。ログ画面の右端に表示されます。",
                      command: "git log --oneline", related: ["commit", "head"]),
        GlossaryEntry(id: "branch", term: "ブランチ", english: "branch",
                      summary: "コミット履歴の枝分かれ。並行して別の作業を進めるための仕組み。",
                      detail: "例えば main ブランチを安定版にしたまま、feature/login ブランチで新機能を開発できます。ブランチの正体は「あるコミットを指す名札」で、コミットすると名札が新しいコミットへ進みます。完成したらマージで main に取り込みます。",
                      command: "git branch <名前> / git switch -c <名前>", related: ["switch", "merge", "head", "main"]),
        GlossaryEntry(id: "main", term: "main / master", english: "main / master",
                      summary: "多くのリポジトリで基本となるブランチの名前。",
                      detail: "以前は master が一般的でしたが、現在は main がよく使われます。特別な機能があるわけではなく、慣習的な名前です。",
                      related: ["branch"]),
        GlossaryEntry(id: "head", term: "HEAD", english: "HEAD",
                      summary: "「今いる場所（現在チェックアウトしているコミット）」を指す目印。",
                      detail: "通常は現在のブランチを指していて、そのブランチの最新コミットが HEAD です。HEAD~1 は 1 つ前のコミットを意味します。",
                      command: "git log -1 HEAD", related: ["detached", "branch", "switch"]),
        GlossaryEntry(id: "detached", term: "HEAD 切り離し", english: "detached HEAD",
                      summary: "ブランチではなく特定のコミットに直接移動している状態。",
                      detail: "過去の状態を確認するのに便利ですが、この状態でコミットするとどのブランチにも属さないため、後で見失いやすくなります。作業を続けるなら「新しいブランチを作成」してください。",
                      command: "git switch --detach <コミット>", related: ["head", "switch", "branch"]),
        GlossaryEntry(id: "switch", term: "切り替え / チェックアウト", english: "switch / checkout",
                      summary: "作業ツリーを別のブランチやコミットの状態に切り替えること。",
                      detail: "切り替えると Finder 上のファイルの中身もそのブランチの内容に変わります。未コミットの変更が衝突する場合は切り替えできないので、先にコミットするか退避（stash）します。",
                      command: "git switch <ブランチ>", related: ["branch", "detached", "stash"]),
        GlossaryEntry(id: "merge", term: "マージ", english: "merge",
                      summary: "別のブランチの変更を現在のブランチに取り込むこと。",
                      detail: "両方のブランチの変更を合わせた「マージコミット」が作られます（早送りできる場合を除く）。同じ箇所を両方で変更していると「競合」になり、どちらを採用するか決める必要があります。",
                      command: "git merge <ブランチ>", related: ["fast-forward", "conflict", "no-ff", "rebase"]),
        GlossaryEntry(id: "fast-forward", term: "早送り（fast-forward）", english: "fast-forward",
                      summary: "取り込む側に新しいコミットがないとき、ブランチの名札を先に進めるだけで済むマージ。",
                      detail: "マージコミットは作られず、履歴は一直線のままです。プルで「早送りのみ」を指定すると、早送りできない（分岐している）場合は何もせず止まります。",
                      command: "git merge --ff-only <ブランチ>", related: ["merge", "no-ff", "pull"]),
        GlossaryEntry(id: "no-ff", term: "早送りしない（--no-ff）", english: "--no-ff",
                      summary: "早送りできる場合でも、必ずマージコミットを作るオプション。",
                      detail: "「ここで feature ブランチを取り込んだ」という記録が履歴に残るので、機能単位のまとまりが分かりやすくなります。",
                      command: "git merge --no-ff <ブランチ>", related: ["merge", "fast-forward"]),
        GlossaryEntry(id: "squash", term: "スカッシュ", english: "squash",
                      summary: "複数のコミットを 1 つにまとめること。",
                      detail: "マージで --squash を使うと、相手ブランチの変更をまとめて 1 つの変更として取り込みます（自分でコミットが必要）。対話的リベースの「前と統合 (squash)」は、直前のコミットと合体させます。",
                      command: "git merge --squash <ブランチ>", related: ["merge", "rebase", "fixup"]),
        GlossaryEntry(id: "fixup", term: "fixup", english: "fixup",
                      summary: "対話的リベースで、直前のコミットに統合しつつメッセージは捨てる操作。",
                      detail: "「typo 修正」のような小さな修正コミットを、元のコミットにこっそり合体させたいときに使います。",
                      related: ["squash", "rebase"]),
        GlossaryEntry(id: "rebase", term: "リベース", english: "rebase",
                      summary: "自分のコミットを、別のブランチの最新コミットの上に付け替えること。",
                      detail: "マージと違いマージコミットができず、履歴が一直線になります。対話的リベースでは、コミットの順番を入れ替えたり、まとめたり（squash）、メッセージを直したり、削除したりできます。すでにプッシュして他の人が使っているコミットをリベースすると混乱するので注意してください。",
                      command: "git rebase <ブランチ> / git rebase -i <ブランチ>", related: ["merge", "squash", "force-push"]),
        GlossaryEntry(id: "conflict", term: "競合（コンフリクト）", english: "conflict",
                      summary: "同じ箇所が両方のブランチで変更されていて、Git が自動でまとめられない状態。",
                      detail: "ファイルに <<<<<<< ======= >>>>>>> という印が入ります。「競合を編集」で自分（HEAD）と相手のどちらを採用するか選び、解決済みにしてからコミット（またはリベースの続行）をします。",
                      command: "git status / git add <解決したファイル>", related: ["merge", "rebase", "ours-theirs"]),
        GlossaryEntry(id: "ours-theirs", term: "自分 / 相手（ours / theirs）", english: "ours / theirs",
                      summary: "競合したときの、現在のブランチ側（自分）と取り込もうとしている側（相手）。",
                      detail: "マージでは「自分」= 今いるブランチ、「相手」= マージしようとしているブランチです。リベース中は逆になる点に注意してください（リベースでは付け替え先が「自分」になります）。",
                      command: "git checkout --ours <ファイル> / git checkout --theirs <ファイル>", related: ["conflict"]),
        GlossaryEntry(id: "remote", term: "リモート", english: "remote",
                      summary: "GitHub などにある、共有用のリポジトリ（とその登録名）。",
                      detail: "クローンすると元の場所が origin という名前で登録されます。プッシュで自分のコミットを送り、プル・フェッチで他の人のコミットを受け取ります。",
                      command: "git remote -v", related: ["origin", "push", "pull", "fetch"]),
        GlossaryEntry(id: "origin", term: "origin", english: "origin",
                      summary: "クローン元のリモートに付く既定の名前。",
                      detail: "origin/main は「origin にある main ブランチを、最後にフェッチした時点で記録したもの（リモート追跡ブランチ）」です。",
                      related: ["remote", "upstream", "fetch"]),
        GlossaryEntry(id: "upstream", term: "上流ブランチ", english: "upstream / tracking branch",
                      summary: "ローカルブランチが対応しているリモートのブランチ。",
                      detail: "例えば main の上流が origin/main なら、プルやプッシュで相手を省略できます。初めてプッシュするときに「上流ブランチとして設定 (-u)」を付けると設定されます。「ahead 2」は自分が 2 コミット進んでいる、「behind 1」は 1 コミット遅れていることを表します。",
                      command: "git push -u origin <ブランチ> / git branch -u origin/<ブランチ>", related: ["remote", "push", "pull"]),
        GlossaryEntry(id: "clone", term: "クローン", english: "clone",
                      summary: "リモートリポジトリを丸ごと自分のパソコンにコピーすること。",
                      detail: "履歴もすべてコピーされ、元の場所は origin として登録されます。",
                      command: "git clone <URL>", related: ["remote", "origin", "depth"]),
        GlossaryEntry(id: "depth", term: "深さ（shallow clone）", english: "--depth",
                      summary: "最近のコミットだけを取得する、軽量なクローン。",
                      detail: "--depth 1 なら最新のコミットだけを取得します。大きなリポジトリを素早く取得したいときに使いますが、古い履歴は見られません。",
                      command: "git clone --depth 1 <URL>", related: ["clone"]),
        GlossaryEntry(id: "fetch", term: "フェッチ", english: "fetch",
                      summary: "リモートの新しいコミットを取得するだけで、作業中のブランチには反映しない操作。",
                      detail: "フェッチ後は origin/main などのリモート追跡ブランチが更新され、ログで他の人の変更を確認できます。安全に「最新の状況を見る」ための操作です。",
                      command: "git fetch origin", related: ["pull", "remote", "prune"]),
        GlossaryEntry(id: "pull", term: "プル", english: "pull",
                      summary: "リモートの変更を取得（フェッチ）して、現在のブランチに取り込む（マージ／リベース）操作。",
                      detail: "git pull は git fetch + git merge（またはリベース）です。自分にもリモートにも新しいコミットがあると、マージコミットが作られるか競合が起きることがあります。",
                      command: "git pull origin main", related: ["fetch", "merge", "rebase", "fast-forward", "autostash"]),
        GlossaryEntry(id: "push", term: "プッシュ", english: "push",
                      summary: "自分のコミットをリモートに送ること。",
                      detail: "リモートに自分の知らないコミットがある場合は拒否されます（rejected）。その場合は先にプルして取り込んでから、もう一度プッシュします。",
                      command: "git push origin main", related: ["pull", "upstream", "force-push"]),
        GlossaryEntry(id: "force-push", term: "強制プッシュ", english: "force push / --force-with-lease",
                      summary: "リモートの履歴を自分の履歴で上書きするプッシュ。",
                      detail: "amend やリベースで履歴を書き換えた後に必要になります。他の人のコミットを消してしまう危険があるため、GitVelto では「自分が最後に見た状態からリモートが変わっていない場合だけ上書きする」安全な --force-with-lease を使います。",
                      command: "git push --force-with-lease", related: ["push", "rebase", "amend"]),
        GlossaryEntry(id: "prune", term: "整理（prune）", english: "--prune",
                      summary: "リモートで削除されたブランチの記録（origin/xxx）をローカルからも消すこと。",
                      detail: "フェッチやプルに --prune を付けると、もう存在しないリモート追跡ブランチが一覧から消えて見通しが良くなります。",
                      command: "git fetch --prune", related: ["fetch", "remote"]),
        GlossaryEntry(id: "autostash", term: "自動退避（autostash）", english: "--autostash",
                      summary: "プルやリベースの前に未コミットの変更を自動で退避し、終わったら戻すオプション。",
                      detail: "作業途中の変更があってもプルできるようになります。戻すときに競合することもあります。",
                      command: "git pull --rebase --autostash", related: ["stash", "pull", "rebase"]),
        GlossaryEntry(id: "stash", term: "退避（stash）", english: "stash",
                      summary: "コミットせずに、作業中の変更を一時的にしまっておく機能。",
                      detail: "急いで別のブランチに切り替えたいときなどに使います。退避すると作業ツリーはきれいになり、あとで「退避した変更を復元 (pop)」で戻せます。",
                      command: "git stash push -m \"メモ\" / git stash pop", related: ["switch", "autostash"]),
        GlossaryEntry(id: "tag", term: "タグ", english: "tag",
                      summary: "特定のコミットに付ける、動かない名札（例: v1.0）。",
                      detail: "リリースしたバージョンの印などに使います。メッセージ付きの「注釈付きタグ」と名前だけの「軽量タグ」があります。タグはプッシュしないとリモートに送られません。",
                      command: "git tag -a v1.0 -m \"リリース\"", related: ["commit", "branch"]),
        GlossaryEntry(id: "amend", term: "直前のコミットをやり直す（amend）", english: "--amend",
                      summary: "最後のコミットのメッセージや内容を修正して作り直すこと。",
                      detail: "コミットメッセージの誤字や、入れ忘れたファイルの追加に使います。コミットの ID が変わるため、すでにプッシュしたコミットに使うと強制プッシュが必要になります。",
                      command: "git commit --amend", related: ["commit", "force-push"]),
        GlossaryEntry(id: "signoff", term: "Signed-off-by", english: "--signoff",
                      summary: "「この変更を提出する権利があります」という署名行をメッセージ末尾に付けること。",
                      detail: "Linux などのオープンソースで求められることがある慣習です。通常の開発では必須ではありません。",
                      command: "git commit -s", related: ["commit"]),
        GlossaryEntry(id: "revert-file", term: "元に戻す（変更の破棄）", english: "restore / discard changes",
                      summary: "まだコミットしていないファイルの変更を捨てて、最後のコミットの状態に戻すこと。",
                      detail: "捨てた変更は元に戻せません。新しく追加したファイルは「追加の取り消し」になり、ファイル自体は残ります。",
                      command: "git restore --source=HEAD --staged --worktree <ファイル>", related: ["revert", "reset", "stash"]),
        GlossaryEntry(id: "revert", term: "リバート（打ち消し）", english: "revert",
                      summary: "過去のコミットの変更を打ち消す、新しいコミットを作ること。",
                      detail: "履歴を消さずに「取り消した」という記録を残すため、プッシュ済みのコミットでも安全に使えます。",
                      command: "git revert <コミット>", related: ["reset", "commit"]),
        GlossaryEntry(id: "reset", term: "リセット", english: "reset (--soft / --mixed / --hard)",
                      summary: "ブランチの位置を過去のコミットに戻すこと。",
                      detail: "ソフト: コミットだけ取り消し、変更はステージされたまま残る。\n混合（既定）: コミットとステージを取り消し、変更はファイルに残る。\nハード: コミットもファイルの変更もすべて捨てる（元に戻せないので注意）。\nプッシュ済みのコミットを取り消すならリバートが安全です。",
                      command: "git reset --mixed HEAD~1", related: ["revert", "reflog", "head"]),
        GlossaryEntry(id: "cherry-pick", term: "チェリーピック", english: "cherry-pick",
                      summary: "別のブランチの特定のコミットだけを、現在のブランチにコピーして取り込むこと。",
                      detail: "「あのブランチのバグ修正だけ欲しい」というときに使います。内容は同じでも新しいコミット ID になります。",
                      command: "git cherry-pick <コミット>", related: ["merge", "commit"]),
        GlossaryEntry(id: "reflog", term: "Reflog", english: "reflog",
                      summary: "HEAD がどこへ移動したかの記録。消えたように見えるコミットを探すのに使う。",
                      detail: "リセットやブランチ削除で見えなくなったコミットも、Reflog から探してブランチを作れば復元できます（一定期間内）。",
                      command: "git reflog", related: ["reset", "head"]),
        GlossaryEntry(id: "bisect", term: "二分探索（bisect）", english: "bisect",
                      summary: "不具合が入り込んだコミットを、良い/悪いの判定を繰り返して効率よく特定する機能。",
                      detail: "問題がなかったコミットと問題があるコミットを指定すると、Git が中間のコミットを順に取り出します。毎回動作確認して「良い」「悪い」を答えるだけで原因のコミットが見つかります。",
                      command: "git bisect start <悪い> <良い>", related: ["commit", "log"]),
        GlossaryEntry(id: "log", term: "ログ（履歴）", english: "log",
                      summary: "コミットの一覧。誰が・いつ・何を変更したかが分かる。",
                      detail: "GitVelto のログ画面では、左のグラフでブランチの分岐や合流も見られます。",
                      command: "git log --oneline --graph --all", related: ["commit", "branch", "blame"]),
        GlossaryEntry(id: "blame", term: "Blame", english: "blame",
                      summary: "ファイルの各行を、最後に変更したコミットと作成者付きで表示する機能。",
                      detail: "「この行はいつ・なぜ変わったのか」を調べるのに使います。",
                      command: "git blame <ファイル>", related: ["log"]),
        GlossaryEntry(id: "clean", term: "クリーンアップ（clean）", english: "clean",
                      summary: "未追跡のファイルや無視されたファイルを削除して、作業ツリーをきれいにすること。",
                      detail: "ビルドで生成されたファイルなどを一掃できます。GitVelto では既定でゴミ箱に移動するので、間違えても元に戻せます。",
                      command: "git clean -n -d（確認） / git clean -f -d（削除）", related: ["untracked", "ignore"]),
        GlossaryEntry(id: "submodule", term: "サブモジュール", english: "submodule",
                      summary: "リポジトリの中に、別のリポジトリを特定のコミットで埋め込む仕組み。",
                      detail: "共通ライブラリなどを別リポジトリのまま取り込みたいときに使います。クローン後は「サブモジュールを更新（--init）」で中身を取得します。",
                      command: "git submodule update --init --recursive", related: ["clone"]),
        GlossaryEntry(id: "patch", term: "パッチ", english: "patch",
                      summary: "変更内容をファイルとして書き出したもの。メールなどで送って相手のリポジトリに適用できる。",
                      detail: "format-patch で作ったパッチは git am でコミットとして、git diff で作った差分は git apply で作業ツリーに適用します。",
                      command: "git format-patch -1 / git am <パッチ>", related: ["diff", "commit"]),
        GlossaryEntry(id: "bare", term: "ベアリポジトリ", english: "bare repository",
                      summary: "作業ツリーを持たず、履歴だけを持つリポジトリ。共有サーバー用。",
                      detail: "GitHub の裏側にあるリポジトリのようなものです。直接ファイルを編集することはありません。",
                      command: "git init --bare / git clone --bare <URL>", related: ["repository", "remote"]),
    ]

    private static let byID: [String: GlossaryEntry] = Dictionary(uniqueKeysWithValues: entries.map { ($0.id, $0) })

    static func entry(_ id: String) -> GlossaryEntry? { byID[id] }

    static func search(_ text: String) -> [GlossaryEntry] {
        let q = text.trimmingCharacters(in: .whitespaces).lowercased()
        guard !q.isEmpty else { return entries }
        return entries.filter { $0.searchText.contains(q) }
    }

    /// Glossary key for a file state shown in file lists.
    static func key(for state: FileState) -> String {
        switch state {
        case .untracked: return "untracked"
        case .ignored: return "ignore"
        case .conflict: return "conflict"
        case .added: return "stage"
        default: return "modified"
        }
    }
}

// MARK: - Command explanations

struct CommandExplanation {
    var command: String          // "git commit -m ..."
    var summary: String          // what the sub-command does
    var options: [(String, String)]  // (option, meaning)
}

/// Explains git command lines in Japanese for the learning mode.
enum CommandExplainer {
    static let subcommands: [String: String] = [
        "add": "変更をステージ（次のコミットに含める準備）します",
        "commit": "ステージした変更をコミット（履歴に記録）します",
        "push": "ローカルのコミットをリモートに送ります",
        "pull": "リモートの変更を取得して現在のブランチに取り込みます (fetch + merge/rebase)",
        "fetch": "リモートの新しいコミットを取得します（作業中のブランチは変わりません）",
        "switch": "ブランチやコミットを切り替えます",
        "checkout": "ファイルやブランチの状態を取り出します",
        "branch": "ブランチを作成・削除・名前変更します",
        "tag": "タグ（コミットの名札）を作成・削除します",
        "merge": "別のブランチの変更を現在のブランチに取り込みます",
        "rebase": "現在のブランチのコミットを別のコミットの上に付け替えます",
        "cherry-pick": "指定したコミットの変更を現在のブランチにコピーします",
        "revert": "指定したコミットを打ち消す新しいコミットを作ります",
        "reset": "現在のブランチの位置を指定したコミットに戻します",
        "restore": "ファイルを指定した状態に戻します（変更の破棄・ステージ解除）",
        "rm": "ファイルを Git の管理から削除します",
        "mv": "ファイルの名前を変更・移動します（Git に名前変更として記録）",
        "stash": "作業中の変更を一時的に退避・復元します",
        "clean": "未追跡のファイルを削除します",
        "clone": "リモートリポジトリを丸ごとコピーします",
        "init": "新しいリポジトリを作成します",
        "remote": "リモートの登録・変更・削除をします",
        "submodule": "サブモジュールを操作します",
        "bisect": "二分探索で不具合の原因コミットを探します",
        "archive": "指定したリビジョンのファイルをアーカイブ (zip/tar) に書き出します",
        "format-patch": "コミットをパッチファイルとして書き出します",
        "am": "パッチファイルをコミットとして適用します",
        "apply": "差分（パッチ）を作業ツリーに適用します",
        "diff": "変更の差分を表示します",
        "log": "コミット履歴を表示します",
        "blame": "各行を最後に変更したコミットを表示します",
        "status": "作業ツリーの状態を表示します",
        "config": "Git の設定を読み書きします",
    ]

    /// Option meanings. Key "*" applies to every sub-command.
    static let options: [String: [String: String]] = [
        "*": [
            "--progress": "進捗を表示します",
            "--literal-pathspecs": "パス中の * などを特殊文字として扱いません（GitVelto が安全のために付けています）",
            "-c": "このコマンドだけで有効な設定を指定します",
        ],
        "add": ["-A": "新規・変更・削除をまとめてステージします", "-f": "無視されているファイルも追加します",
                "--pathspec-from-file": "対象ファイルの一覧をファイルから読み込みます（GitVelto が内部で使用）"],
        "commit": ["-m": "コミットメッセージを指定します", "-F": "コミットメッセージをファイルから読み込みます",
                   "--amend": "直前のコミットを作り直します", "--author": "作成者を指定します", "--date": "日時を指定します",
                   "--allow-empty": "変更のない空のコミットを許可します", "--only": "指定したパスだけをコミットします",
                   "--cleanup=strip": "メッセージの前後の空行やコメント行を取り除きます", "-s": "Signed-off-by 行を追加します",
                   "--pathspec-from-file": "コミットするファイルの一覧をファイルから読み込みます（GitVelto が内部で使用）"],
        "push": ["-u": "上流ブランチとして設定します（以降は git push だけで送れる）",
                 "--force-with-lease": "安全な強制プッシュ（リモートが想定外に変わっていたら中止）",
                 "--tags": "タグもプッシュします", "--all": "すべてのローカルブランチをプッシュします",
                 "--delete": "リモートのブランチ・タグを削除します", "--force": "強制的に上書きします"],
        "pull": ["--rebase": "マージではなくリベースで取り込みます", "--no-rebase": "マージで取り込みます",
                 "--ff-only": "早送りできる場合だけ取り込みます", "--autostash": "未コミットの変更を自動で退避・復元します",
                 "--prune": "リモートで削除されたブランチの記録を消します"],
        "fetch": ["--all": "すべてのリモートから取得します", "--prune": "リモートで削除されたブランチの記録を消します",
                  "--tags": "すべてのタグを取得します"],
        "switch": ["-c": "新しいブランチを作成して切り替えます", "-C": "ブランチを作成（既存なら上書き）して切り替えます",
                   "--detach": "ブランチではなくコミットに直接移動します（HEAD 切り離し）",
                   "--track": "リモートブランチを上流として追跡します", "--discard-changes": "ローカルの変更を破棄して切り替えます"],
        "checkout": ["--ours": "競合したファイルで自分（現在のブランチ）の内容を採用します",
                     "--theirs": "競合したファイルで相手の内容を採用します"],
        "branch": ["-d": "ブランチを削除します（マージ済みのみ）", "-D": "ブランチを強制削除します",
                   "-m": "ブランチ名を変更します", "-u": "上流ブランチを設定します", "-f": "既存のブランチを上書きします"],
        "tag": ["-a": "注釈付きタグを作成します", "-m": "タグのメッセージを指定します", "-d": "タグを削除します", "-f": "既存のタグを上書きします"],
        "merge": ["--no-ff": "早送りせず必ずマージコミットを作ります", "--squash": "変更を 1 つにまとめて取り込みます（コミットは別途）",
                  "--no-commit": "マージ結果をコミットせずに止めます", "--no-edit": "マージメッセージを編集せず既定のものを使います",
                  "-m": "マージメッセージを指定します", "--abort": "マージを中止して元に戻します", "--ff-only": "早送りできる場合だけマージします"],
        "rebase": ["-i": "対話的リベース（並べ替え・統合・削除など）", "--autostash": "未コミットの変更を自動で退避・復元します",
                   "--continue": "競合を解決した後にリベースを続けます", "--abort": "リベースを中止して元に戻します"],
        "cherry-pick": ["--continue": "競合を解決した後に続けます", "--abort": "中止して元に戻します"],
        "revert": ["--no-edit": "既定のメッセージで打ち消しコミットを作ります", "-m": "マージコミットのどちらの親を基準にするか指定します"],
        "reset": ["--soft": "コミットだけ取り消し、変更はステージに残します", "--mixed": "コミットとステージを取り消し、変更はファイルに残します",
                  "--hard": "コミットもファイルの変更もすべて捨てます（元に戻せません）"],
        "restore": ["--source=HEAD": "最後のコミットの内容を元にします", "--staged": "ステージを取り消します", "--worktree": "ファイルの変更を破棄します"],
        "rm": ["-r": "フォルダの中身も対象にします", "--cached": "Git の管理から外すだけでファイルは残します", "-f": "変更があっても削除します", "-q": "メッセージを表示しません"],
        "stash": ["push": "変更を退避します", "pop": "退避した変更を戻して一覧から消します", "apply": "退避した変更を戻します（一覧に残します）",
                  "drop": "退避した変更を捨てます", "-u": "未追跡ファイルも退避します", "--keep-index": "ステージ済みの変更は残します", "-m": "メモを付けます"],
        "clean": ["-n": "削除せず、削除される予定のファイルを表示します", "-f": "実際に削除します", "-d": "フォルダも対象にします",
                  "-x": "無視されたファイルも対象にします", "-X": "無視されたファイルだけを対象にします"],
        "clone": ["--recursive": "サブモジュールも取得します", "-b": "取得後に切り替えるブランチを指定します", "--depth": "直近のコミットだけを取得します",
                  "-o": "リモート名を指定します（既定 origin）", "--no-checkout": "ファイルを取り出さずに履歴だけ取得します", "--bare": "作業ツリーなしで取得します"],
        "init": ["-b": "最初のブランチ名を指定します", "--bare": "作業ツリーのないリポジトリを作ります"],
        "remote": ["add": "リモートを登録します", "remove": "リモートを削除します", "rename": "リモート名を変更します",
                   "set-url": "リモートの URL を変更します", "prune": "削除されたリモートブランチの記録を消します"],
        "submodule": ["add": "サブモジュールを追加します", "update": "記録されたコミットに合わせて取得します", "sync": "URL を .gitmodules に合わせます",
                      "--init": "未初期化のものを初期化します", "--recursive": "入れ子のサブモジュールも対象にします", "--remote": "リモートの最新に更新します"],
        "bisect": ["start": "二分探索を開始します", "good": "このコミットは問題なし", "bad": "このコミットは問題あり",
                   "skip": "このコミットは判定できないので飛ばします", "reset": "二分探索を終了して元に戻ります"],
        "archive": ["--format": "形式 (zip/tar.gz/tar)", "-o": "出力ファイル", "--prefix": "アーカイブ内のフォルダ名"],
        "format-patch": ["-o": "出力先フォルダ", "--stdout": "標準出力に書き出します", "-1": "直近 1 コミット"],
        "am": ["-3": "3-way マージで適用します", "--signoff": "Signed-off-by を付けます", "--continue": "競合解決後に続けます", "--abort": "中止します"],
        "apply": ["--3way": "3-way マージで適用します", "--check": "適用できるか確認だけします", "-v": "詳しく表示します"],
    ]

    /// Splits off leading "-c key=value" config overrides and returns the sub-command index.
    private static func subcommandIndex(_ args: [String]) -> Int? {
        var i = 0
        while i < args.count {
            let a = args[i]
            if a == "-c" { i += 2; continue }
            if a.hasPrefix("-") { i += 1; continue }
            return i
        }
        return nil
    }

    static func explain(_ args: [String]) -> CommandExplanation {
        let shown = args.filter { $0 != "--literal-pathspecs" }
        let cmd = "git " + shown.map(quote).joined(separator: " ")
        guard let si = subcommandIndex(args) else { return CommandExplanation(command: cmd, summary: "", options: []) }
        let sub = args[si]
        var opts: [(String, String)] = []
        var seen = Set<String>()
        let table = (options[sub] ?? [:]).merging(options["*"] ?? [:]) { a, _ in a }
        for a in args[(si + 1)...] {
            let key: String
            if let eq = a.firstIndex(of: "="), a.hasPrefix("--") { key = table[a] != nil ? a : String(a[..<eq]) } else { key = a }
            guard let meaning = table[key], !seen.contains(key) else { continue }
            if key == "--literal-pathspecs" || key == "--pathspec-from-file" { continue }
            seen.insert(key)
            opts.append((key, meaning))
        }
        return CommandExplanation(command: cmd, summary: subcommands[sub] ?? "", options: opts)
    }

    static func quote(_ s: String) -> String {
        if s.isEmpty { return "\"\"" }
        if s.contains(where: { " \"'$`\\\n".contains($0) }) {
            return "\"" + s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
        }
        return s
    }
}

// MARK: - Command history

/// Commands GitVelto ran on the user's behalf (for the learning mode / "コマンド履歴").
final class CommandHistory: ObservableObject {
    static let shared = CommandHistory()

    struct Entry: Identifiable, Codable {
        var id = UUID()
        let date: Date
        let dir: String
        let args: [String]
        var ok: Bool?
    }

    @Published private(set) var entries: [Entry] = [] { didSet { scheduleSave() } }
    private let storeKey = "commandHistory"
    private var saveWork: DispatchWorkItem?

    init() {
        if let data = UserDefaults.standard.data(forKey: storeKey),
           let saved = try? JSONDecoder().decode([Entry].self, from: data) {
            entries = saved
        }
    }

    private func scheduleSave() {
        saveWork?.cancel()
        let snapshot = entries
        let work = DispatchWorkItem { [storeKey] in
            if let data = try? JSONEncoder().encode(snapshot) { UserDefaults.standard.set(data, forKey: storeKey) }
        }
        saveWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 1, execute: work)
    }

    @discardableResult
    func record(_ args: [String], dir: String, ok: Bool? = nil) -> UUID {
        let e = Entry(date: Date(), dir: dir, args: args, ok: ok)
        let apply = {
            self.entries.insert(e, at: 0)
            if self.entries.count > 500 { self.entries.removeLast(self.entries.count - 500) }
        }
        if Thread.isMainThread { apply() } else { DispatchQueue.main.async(execute: apply) }
        return e.id
    }

    func finish(_ id: UUID, ok: Bool) {
        let apply = { if let i = self.entries.firstIndex(where: { $0.id == id }) { self.entries[i].ok = ok } }
        if Thread.isMainThread { apply() } else { DispatchQueue.main.async(execute: apply) }
    }

    func clear() { entries.removeAll() }
}
