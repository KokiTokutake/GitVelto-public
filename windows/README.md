# GitVelto for Windows

macOS 版 GitVelto（このリポジトリのルート）の Windows 版です。エクスプローラーに統合された、TortoiseGit 風の Git クライアントです。UI はすべて日本語で、git の出力も日本語に変換して表示します。学習モード（実行される git コマンドとその意味の表示）、Git 用語集、コマンド履歴も Mac 版と同じように使えます。

## 構成

| プロジェクト | 内容 | Mac 版での対応 |
| --- | --- | --- |
| `src/GitVelto.Core` | git の実行、出力の解析、コマンドの組み立て、日本語化、学習モード、設定、メニュー判定 (C#) | `Sources/App/Core`、`Sources/Shared` |
| `src/GitVelto` | GitVelto.exe。各ダイアログ (WPF)、タスクトレイ、認証ダイアログ (GIT_ASKPASS) | `Sources/App/UI`、`AppDelegate` |
| `src/GitVeltoCache` | GitVeltoCache.exe。オーバーレイ用の状態キャッシュ (TGitCache 相当)。名前付きパイプで応答し、FileSystemWatcher で変更を監視して SHChangeNotify でエクスプローラーを更新 | `StatusService` |
| `src/GitVeltoShell` | GitVeltoShell.dll。エクスプローラーのシェル拡張 (C++/COM)。右クリックメニューとアイコンオーバーレイ 6 種類 | `Sources/FinderSync` |
| `src/GitVeltoSetup` | GitVeltoSetup.exe。アプリ一式を埋め込んだインストーラー（ランタイム同梱の単一ファイル） | `build.sh install` |
| `package` | Windows 11 の右クリックメニュー用スパースパッケージのマニフェストとロゴ | — |
| `tests/GitVelto.Tests` | xUnit。Mac 版の機能テストの移植、メニュー判定、日本語化、シェル拡張を直接読み込むテスト | `Tests/*` |

- 右クリック → 操作: シェル拡張が `GitVelto.exe <操作> --pathfile <一時ファイル>` を起動します。GitVelto.exe がすでに起動していれば、名前付きパイプで要求を渡して終了します（単一インスタンス）。
- 状態アイコン: シェル拡張がフォルダ単位で GitVeltoCache に問い合わせます（`dir\t<フォルダ>`）。キャッシュは `git status --porcelain -z --ignored` の結果をリポジトリ単位で持ち、変化があった項目だけエクスプローラーに通知します。キャッシュが起動していなければシェル拡張が起動します。
- 設定は `HKCU\Software\GitVelto` に保存します（シェル拡張もメニュー構成とオーバーレイの有効/無効をここから読みます）。

## 必要環境

- Windows 10 / 11 (x64)
- .NET 10 デスクトップ ランタイム
- Git for Windows
- ビルドする場合: .NET 10 SDK、Visual Studio 2026 の C++ ツール (v145)

## インストール

`windows\out\GitVeltoSetup-<バージョン>.exe` を実行します（管理者の許可が必要）。.NET 10 デスクトップ ランタイムと Git for Windows の有無を確認してからインストールし、最後にエクスプローラーを再起動します。`/quiet` を付けると確認なしでインストールします。アンインストールは「設定 > アプリ」から行えます。

## ビルド

```powershell
powershell -ExecutionPolicy Bypass -File windows\build.ps1            # windows\out\GitVelto にビルド
powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Test      # ビルド + テスト
powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Setup     # ビルド + インストーラー (windows\out\GitVeltoSetup-<バージョン>.exe)
powershell -ExecutionPolicy Bypass -File windows\build.ps1 -Install   # ビルド + このPCにインストール (管理者の許可が必要)
```

インストールでは次のことを行います（`scripts\install.ps1`）。

- `%ProgramFiles%\GitVelto` にコピーし、シェル拡張を登録 (`regsvr32`)
- Windows 11 の新しい右クリックメニュー用のスパースパッケージ (`GitVelto.msix`) を登録。署名はビルドした PC で作る自己署名証明書 (`CN=GitVelto Local Build`) で行い、インストール時にその証明書を「信頼されたユーザー」に入れます
- スタートメニューと「アプリと機能」に登録し、オーバーレイを読み込ませるためにエクスプローラーを再起動

アンインストールは「アプリと機能」から、または `"%ProgramFiles%\GitVelto\install.ps1" -Uninstall`（管理者）で行えます。

### アイコンオーバーレイについて

Windows が表示できるオーバーレイは、登録名の順で先頭 15 個までです。GitVelto は名前の先頭に空白を 3 つ付けて登録します（空白 4 つの Google Drive の後、空白 2 つの OneDrive・TortoiseGit の前）。設定 > アイコンオーバーレイ に、GitVelto の 6 個のうち何個が有効かを表示します。

TortoiseGit と併用すると右クリックメニューに両方が表示されます。置き換える場合は TortoiseGit をアンインストールしてください。

### Windows 11 の右クリックメニュー

最初のメニューに「GitVelto ▸」（IExplorerCommand）、「その他のオプションを確認」の中に従来のメニュー（「Git 同期…」「Git コミット…」「GitVelto ▸」）が表示されます。

## 開発

```powershell
dotnet test windows\tests\GitVelto.Tests                     # テスト (シェル拡張のテストは C++ のビルド後に実行される)
windows\src\GitVelto\bin\Debug\net10.0-windows\GitVelto.exe --snapshot <出力先> <サンプルリポジトリ> [light|dark]
                                                             # 全ダイアログを画面外で描画して PNG に保存
bash scripts/make_test_repos.sh <dir>                        # サンプルリポジトリの作成 (Mac 版と共通、Git Bash で実行)
powershell -File windows\scripts\cache-query.ps1 "dir`t<フォルダ>"  # キャッシュへの問い合わせ (デバッグ用)
powershell -File windows\scripts\make-overlays.ps1           # オーバーレイアイコンの再生成
powershell -File windows\scripts\make-menu-icons.ps1         # 右クリックメニューのアイコンの再生成
powershell -File windows\scripts\make-icon.ps1               # Resources/AppIcon.png からアプリアイコンを再生成
```

GitVeltoCache.exe を `--log` 付きで起動すると `%TEMP%\GitVelto\cache.log` にログを出します。

オーバーレイとメニューのアイコンは [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT) の図形から作っています (`windows\third_party\fluentui-system-icons`)。第三者ライセンスの表示は `windows\THIRD-PARTY-NOTICES.txt` にあり、ビルド時に .NET のライセンス原文 (`dotnet-LICENSE.txt` / `dotnet-THIRD-PARTY-NOTICES.txt`) と一緒に出力先へコピーされます。インストーラーが同梱する .NET ランタイムは `GitVeltoSetup.csproj` の `RuntimeFrameworkVersion` で固定しています。
