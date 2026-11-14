# GitVelto

macOS の Finder と Windows のエクスプローラーに統合された、**TortoiseGit 風の Git クライアント**です。UI はすべて日本語です。

- ファイルやフォルダを右クリックするだけで、コミット・ログ・差分・同期・ブランチ操作などを GUI のダイアログで行えます
- ファイルとフォルダに、Git の状態を示すアイコン（オーバーレイ）を表示します
- git の出力を日本語に変換して表示します（原文の英語にも切り替えられます）
- 学習モードでは、実行する git コマンドとその意味を確認できます。Git 用語集とコマンド履歴もあります
  - 非エンジニアのGit操作を想定しています 

## 版ごとの説明


| 版         | フォルダ                            | 技術                             | 説明                                                    |
| --------- | ------------------------------- | ------------------------------ | ----------------------------------------------------- |
| macOS 版   | [`mac/`](mac/README.md)         | Swift / SwiftUI、Finder Sync 拡張 | macOS 14 以降。Xcode 本体なしでビルドできます                        |
| Windows 版 | [`windows/`](windows/README.md) | C# / WPF、C++ のシェル拡張            | Windows 10 / 11 (x64)。.NET 10 と Git for Windows が必要です |


TortoiseGit の機能との対応表は [`mac/README.md`](mac/README.md#tortoisegit-との機能対応) にあります。

## フォルダ構成

```
mac/       macOS 版（ソース、テスト、ビルドスクリプト）
windows/   Windows 版（ソース、テスト、ビルドスクリプト、インストーラー）
shared/    両方のテストで使うスクリプト（サンプルリポジトリの作成）
```

## 位置づけ

- 個人が自分用に作っているプロジェクトです。動作は無保証です（[LICENSE](LICENSE) を参照）
- ビルド済みのアプリは配布していません。使う場合は、各版の README の手順でソースからビルドしてください
- Windows 版は、Windows 11 の新しい右クリックメニューに対応するため、ビルドした PC で作る自己署名証明書を使います。インストール前に [自己署名証明書についての注意](windows/README.md#自己署名証明書についての注意) を読んでください
- TortoiseGit とは無関係の非公式プロジェクトです。TortoiseGit の名前は、機能や画面構成を比べるためにだけ使っています

## ライセンス

[MIT License](LICENSE) です。

Windows 版のアイコンは [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)（MIT）の図形から作っています。第三者ソフトウェアのライセンス表示は [`windows/THIRD-PARTY-NOTICES.txt`](windows/THIRD-PARTY-NOTICES.txt) にあります。