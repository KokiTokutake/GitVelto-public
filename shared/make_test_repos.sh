#!/bin/bash
# Creates sample repositories for manual / snapshot testing.
#   shared/make_test_repos.sh <dir>   (used by both the macOS and Windows tests)
# <dir>/remote.git   bare "origin"
# <dir>/work         main working copy: outgoing + incoming commits, local changes, stash, tags, branches
# <dir>/conflict     a repository in the middle of a merge conflict
set -euo pipefail
D="${1:?usage: make_test_repos.sh <dir>}"
rm -rf "$D"; mkdir -p "$D"; cd "$D"
export GIT_AUTHOR_NAME="山田 太郎" GIT_AUTHOR_EMAIL="taro@example.com"
export GIT_COMMITTER_NAME="山田 太郎" GIT_COMMITTER_EMAIL="taro@example.com"
g() { git -c init.defaultBranch=main -c core.hooksPath=/dev/null "$@"; }

g init -q --bare remote.git
g clone -q remote.git work 2>/dev/null
cd work
cat > README.md <<'EOF'
# サンプルプロジェクト
これはテスト用のリポジトリです。
EOF
mkdir -p src docs
printf 'func hello() {\n    print("hello")\n}\n' > src/main.swift
printf 'ドキュメント\n' > docs/guide.md
printf 'build/\n*.log\n' > .gitignore
g add -A && g commit -qm "最初のコミット"
g tag v1.0
printf 'func add(a: Int, b: Int) -> Int {\n    a + b\n}\n' > src/math.swift
g add -A && g commit -qm "足し算を追加" -m "詳細な説明文。複数行のコミットメッセージのテスト。"
g push -q -u origin main 2>/dev/null
g switch -qc feature/login
printf 'struct Login {\n    var user: String\n}\n' > src/login.swift
g add -A && g commit -qm "ログイン機能の雛形"
printf 'struct Login {\n    var user: String\n    var password: String\n}\n' > src/login.swift
g commit -qam "パスワード欄を追加"
g push -q -u origin feature/login 2>/dev/null
g switch -q main
g merge -q --no-ff --no-edit feature/login
g tag -a v1.1 -m "リリース 1.1"
g push -q origin main --tags 2>/dev/null
# incoming commit made by someone else
cd "$D"; g clone -q remote.git other 2>/dev/null; cd other
printf 'ドキュメント\n追記しました\n' > docs/guide.md
g commit -qam "ドキュメントを更新 (他の人)"
g push -q origin main 2>/dev/null
cd "$D/work"
g fetch -q origin
# outgoing commits
printf 'func hello() {\n    print("こんにちは")\n}\n' > src/main.swift
g commit -qam "挨拶を日本語化"
printf 'func sub(a: Int, b: Int) -> Int {\n    a - b\n}\n' >> src/math.swift
g commit -qam "引き算を追加"
# stash
printf 'stash test\n' >> README.md
g stash push -q -m "作業途中の README"
# local changes
printf '\n## 使い方\nビルドして実行します。\n' >> README.md
printf 'new file\n' > src/new_feature.swift
printf 'log\n' > debug.log
mkdir -p build && printf 'obj\n' > build/out.o
printf 'メモ\n' > 日本語メモ.txt
g add src/new_feature.swift
rm docs/guide.md

# conflict repository
cd "$D"; g clone -q remote.git conflict 2>/dev/null; cd conflict
g switch -qc topic
printf 'func hello() {\n    print("Hello, topic!")\n}\n' > src/main.swift
printf 'README from topic\n' > README.md
g commit -qam "topic での変更"
g switch -q main
printf 'func hello() {\n    print("Hello, main!")\n}\n' > src/main.swift
printf 'README from main\n' > README.md
g commit -qam "main での変更"
g merge topic --no-edit >/dev/null 2>&1 || true
echo "created test repositories in $D"
