# GitVelto for macOS

macOS の Finder に統合された、**TortoiseGit 風の Git クライアント**です（UI はすべて日本語）。Windows 版は [`../windows`](../windows/README.md) にあります。
この README のコマンドとパスは、すべて `mac/` フォルダからの相対パスです。
Finder でファイルやフォルダを右クリックするだけで、コミット・ログ・差分・同期・ブランチ操作などを GUI ダイアログで行えます。
Finder 上のファイルには Git の状態を示すアイコンオーバーレイ（バッジ）が表示されます。

## TortoiseGit との機能対応

| TortoiseGit | GitVelto (Finder 右クリック) | 備考 |
| --- | --- | --- |
| Git Sync… | Git 同期… | プル / フェッチ / フェッチ＆リベース / プッシュ / プル＆プッシュ、受信予定・送信予定のコミット一覧 |
| Git Commit -> "branch"… | Git コミット -> "main"… | チェックしたファイルだけをコミット、amend、Signed-off-by、作成者・日時指定、新しいブランチでコミット、コミット後にプッシュ、最近のメッセージ |
| Show log | ログを表示 | コミットグラフ、全ブランチ、メッセージ/作成者/期間フィルタ、全履歴検索、作業ツリーの変更行、⌘/⇧ クリックで複数選択 |
| Diff / Diff with previous version | 差分 / 前のバージョンと差分 | 統合表示・左右並べて表示、空白無視、前/次の変更へ移動、外部ツール。2 ファイル選択で相互比較 |
| Check for modifications | 変更をチェック… | 未追跡/無視の表示、差分・元に戻す・追加・ステージ解除・無視・パッチとして保存 |
| Repo-browser | リポジトリブラウザ | 任意リビジョンのツリー閲覧、ファイルを開く/保存、Blame、作業ツリーと比較 |
| Browse References | 参照の一覧… | ブランチ/タグの切替・マージ・リベース・名前変更・上流設定・削除、リモートブランチ/タグの削除・プッシュ |
| Show Reflog | Reflog を表示 | ブランチ作成・チェックアウト・リセットで復元 |
| Pull… / Fetch… / Push… | プル… / フェッチ… / プッシュ… | rebase / autostash / ff-only / prune / tags / --all / -u / force-with-lease、リモート追加 |
| Switch/Checkout… | 切り替え/チェックアウト… | ブランチ・リモートブランチ（追跡ブランチ作成）・タグ・コミット |
| Merge… | マージ… | --no-ff / --squash / --no-commit / メッセージ。競合時は解決画面へ誘導 |
| Rebase… | リベース… | 対話的リベース：並べ替え（ドラッグ/↑↓）・squash・fixup・メッセージ変更・編集で停止・削除 |
| Create Branch… / Create Tag… | ブランチを作成… / タグを作成… | 作成元（HEAD/参照/コミット）、注釈付きタグ、作成後プッシュ |
| Stash Save / Pop / List | 変更を退避 / 復元 / 退避リスト… | 未追跡を含める、keep-index、適用/pop/削除、差分表示 |
| Bisect start / good / bad / reset | 二分探索を開始 / 良い / 悪い / スキップ / 終了 | ログのコミット右クリックからも開始・判定可能 |
| Edit conflicts (TortoiseGitMerge) | 競合を編集… | 内蔵の競合エディタ：自分/相手/両方/手動編集を競合箇所ごとに選択 |
| Resolve… | 競合を解決… | ours/theirs 採用、解決済みにする、merge/rebase/cherry-pick/am の続行・中止 |
| Add… / Revert… | 追加… / 元に戻す… | 追加ファイルは未追跡に戻す（ファイルは残す）、名前変更も復元 |
| Delete / Delete (keep local) | 削除 / 削除 (ローカルに残す) | git rm / git rm --cached |
| Rename… | 名前の変更 (git mv)… | |
| Add to ignore list | 無視リストに追加… | パス・名前・拡張子で指定、.gitignore / フォルダの .gitignore / info/exclude |
| Blame | Blame | コミットごとの色分け、1 つ前のリビジョンの Blame |
| Clean up… | クリーンアップ… | 未追跡/無視/両方、フォルダ含む、**ゴミ箱へ移動**（既定） |
| Submodule Add / Update / Sync | サブモジュールを追加 / 更新 / 同期 | init / recursive / remote / force |
| Create Patch Serial / Apply Patch Serial | パッチを作成… / パッチを適用… | format-patch（直近 N / 参照以降 / 選択コミット / 未コミットの差分）、git am / git apply |
| Export… | エクスポート… | ZIP / tar.gz / tar |
| Git Clone… / Create repository here… | Git クローン… / Git ここにリポジトリを作成… | ブランチ・深さ・サブモジュール・リモート名・--no-checkout・--bare |
| Settings | 設定… / リポジトリの設定… / リモートの管理… | 下記参照 |
| Git Bash here | ターミナルで開く | ターミナル / iTerm2 / Ghostty / Warp |
| Help / About | ヘルプ / GitVelto について | |

### 設定（TortoiseGit の Settings に相当）

- **コンテキストメニュー**: 各項目を「トップレベル / サブメニュー / 非表示」から選択（既定は TortoiseGit と同じく「Git 同期」「Git コミット」がトップレベル）、コミット項目へのブランチ名表示
- **アイコンオーバーレイ**: 表示の有無、オーバーレイを表示しないフォルダ
- **Git**: git 実行ファイル、user.name / user.email、core.autocrlf、pull.rebase、credential.helper、init.defaultBranch、~/.gitconfig・グローバル .gitignore の編集
- **外部ツール**: 差分 / マージツール（VS Code・Cursor・FileMerge・Kaleidoscope・Beyond Compare のプリセット）
- **リポジトリの設定**: リモート（追加・URL 変更・名前変更・削除・prune）、ローカルの user.name/email・autocrlf・pull.rebase・hooksPath、.git/config・.gitignore の編集

### 日本語化

UI はすべて日本語です。git 本体には日本語のメッセージカタログがないため、GitVelto が進捗ダイアログやエラー表示で **git の出力を日本語に変換**します（「原文 (英語) を表示」で切替可能）。

## 状態アイコン（オーバーレイ）

| アイコン | 意味 |
| --- | --- |
| 緑のチェック | 変更なし |
| 赤の ! | 変更あり（フォルダは中に変更あり） |
| 青の + | 追加（ステージ済みの新規ファイル） |
| 橙の稲妻 | 競合 |
| 灰の ? | 未追跡 |
| 濃灰の − | 無視 |

リポジトリの親フォルダ（例: `~/Projects`）では、各リポジトリ全体の状態が表示されます。

## 必要環境

- macOS 14 (Sonoma) 以降
- Xcode の Command Line Tools（`xcode-select --install`）— **Xcode 本体は不要**
- git（Homebrew の `/opt/homebrew/bin/git` を優先、なければ `/usr/bin/git`）

## ビルドとインストール

```bash
cd mac
./build.sh install
```

`build/GitVelto.app` をビルドし、`/Applications`（書き込めない場合は `~/Applications`）へコピーし、Finder 拡張を有効化（`pluginkit -e use`）して Finder を再起動します（Finder のウインドウは自動で復元されます）。

- ビルドのみ: `./build.sh`
- Intel / Apple Silicon 両対応: `ARCH=universal ./build.sh`
- 証明書で署名: `SIGN_ID="Apple Development: Your Name (XXXX)" ./build.sh`

### 初回起動時の許可

外付けボリュームや「書類」「デスクトップ」上のリポジトリを扱うと、macOS が GitVelto へのアクセス許可を求めます。**「許可」を選んでください**（状態アイコンの計算やメニューの判定に必要です）。ad-hoc 署名ですが designated requirement を固定しているため、再ビルドしても許可は維持されます。

### Finder 拡張が表示されない場合

1. GitVelto を起動する（メニューバーにアイコンが出ます）
2. 「システム設定 > 一般 > ログイン項目と機能拡張」の Finder 拡張機能で **GitVelto** をオン
   - またはターミナルで `pluginkit -e use -i com.ktoku.GitVelto.FinderSync`
3. `killall Finder` で Finder を再起動
4. GitVelto の設定 > 一般 の「拡張機能の状態」が「有効」になっているか確認

Finder が起動すると拡張が GitVelto 本体を自動で起動します（状態アイコンは本体が計算します）。

### 他の Mac にコピーする場合

ad-hoc 署名のため Gatekeeper にブロックされることがあります。

```bash
xattr -dr com.apple.quarantine /Applications/GitVelto.app
```

## 開発

```bash
./build.sh              # ビルド
scripts/test.sh         # 機能テスト（サンプルリポジトリでコミット/リベース/競合解決/プッシュ等を実行）
scripts/snapshot.sh     # 全ダイアログをオフスクリーン描画して build/snapshots/*.png に出力
scripts/window-probe.sh # 全ダイアログを実ウインドウで 2 回ずつ開き、クラッシュがないか確認（画面に一瞬ウインドウが出ます）
scripts/finder-e2e.sh   # インストール済みアプリに Finder メニューと同じ gitvelto:// URL を送り、全操作のダイアログが開くか確認
../shared/make_test_repos.sh <dir> # 動作確認用のサンプルリポジトリを作成（Windows 版のテストと共通）
```

### アーキテクチャ

```
GitVelto.app（非サンドボックス, LSUIElement）
├── Contents/MacOS/GitVelto                     … GUI 本体 + 状態キャッシュ（TGitCache 相当）+ 認証ダイアログ
└── Contents/PlugIns/GitVeltoFinderSync.appex   … Finder Sync 拡張（サンドボックス）
```

- **右クリック → 操作**: 拡張が `gitvelto://<command>?path=...` URL を開き、本体が対応するダイアログを表示します。
- **状態アイコン**: Finder がフォルダを表示すると、拡張が Distributed Notification でパスを本体へ送ります。本体は `git status --porcelain -z --ignored` の結果をリポジトリ単位でキャッシュし、子要素ごとの状態を JSON で返します。FSEvents で作業ツリーを監視し、変更があれば再計算して差分だけを送ります。
- **メニュー構成**: 本体の設定を Distributed Notification で拡張へ配信し、拡張側でも保存します。
- **認証**: git/ssh がパスワードを求めると、本体の実行ファイルが `GIT_ASKPASS` / `SSH_ASKPASS` として起動され、入力ダイアログを表示します。HTTPS は osxkeychain ヘルパーがあればキーチェーンを利用します。

### ソース構成

| パス | 内容 |
| --- | --- |
| `Sources/Shared/Shared.swift` | 本体と拡張で共有する定数・コマンド定義・メニュー設定・バッジ種別 |
| `Sources/FinderSync/FinderSync.swift` | Finder Sync 拡張（メニュー構築・バッジ描画） |
| `Sources/App/Core/Git.swift` | git 実行、ステータス/ログ/参照/ツリー/差分のパーサ |
| `Sources/App/Core/GitOps.swift` | コミット・元に戻す・リベースのコマンド組み立て（テスト対象） |
| `Sources/App/Core/GitMessages.swift` | git 出力の日本語変換 |
| `Sources/App/Core/StatusService.swift` | 状態アイコン用キャッシュ・FSEvents 監視・設定配信 |
| `Sources/App/UI/*.swift` | 各ダイアログ（SwiftUI） |
| `Tests/Functional` / `Tests/Snapshot` / `Tests/IPC` | 機能テスト / 画面スナップショット / 拡張との通信テスト |
| `Resources/AppIcon.png` | アプリアイコンの原画（ビルド時に `AppIcon.icns` を自動生成。差し替えて `./build.sh` するだけで更新） |
| `build.sh` | Xcode なしでビルド・署名・インストール |

## アンインストール

```bash
pluginkit -e ignore -i com.ktoku.GitVelto.FinderSync
pkill -x GitVelto; rm -rf /Applications/GitVelto.app
killall Finder
```
