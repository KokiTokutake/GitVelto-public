#!/bin/bash
# Usage: scripts/finder-e2e.sh   (requires the app installed in /Applications; dialogs appear on screen)
# Opens every Finder-menu action through the gitglide:// URL (exactly what the Finder extension does)
# against the installed app, then checks the app log for the opened window.
W="$TMPDIR/gitglide-test-repos/work"; C="$TMPDIR/gitglide-test-repos/conflict"
"$(dirname "$0")/make_test_repos.sh" "$TMPDIR/gitglide-test-repos" >/dev/null
enc() { python3 -c 'import sys,urllib.parse;print(urllib.parse.quote(sys.argv[1]))' "$1"; }
pass=0; fail=0
check() { # action path expected-title-fragment
  local a=$1 p=$2 want=$3
  local since=$(date "+%Y-%m-%d %H:%M:%S")
  open -g "gitglide://$a?path=$(enc "$p")"
  sleep 1.5
  if ! pgrep -f "MacOS/GitGlide$" >/dev/null; then echo "CRASH $a"; fail=$((fail+1)); open -g "gitglide://start"; sleep 2; return; fi
  local got=$(log show --start "$since" --style compact --predicate 'subsystem == "com.ktoku.GitGlide" AND category == "Window"' 2>/dev/null | grep "window opened" | tail -1 | sed 's/.*window opened: //')
  if [[ "$got" == *"$want"* ]]; then echo "ok    $a -> $got"; pass=$((pass+1)); else echo "NG    $a (got: '$got')"; fail=$((fail+1)); fi
}
open -g "gitglide://start"; sleep 2
check sync "$W" "同期"; check commit "$W" "コミット"; check log "$W" "ログ"; check diff "$W/README.md" "差分"
check diffPrevious "$W/src/main.swift" "差分"; check status "$W" "変更をチェック"; check repoBrowser "$W" "リポジトリブラウザ"
check refBrowser "$W" "参照の一覧"; check reflog "$W" "Reflog"; check pull "$W" "プル"; check fetch "$W" "フェッチ"; check push "$W" "プッシュ"
check switch "$W" "切り替え"; check merge "$W" "マージ"; check rebase "$W" "リベース"; check branch "$W" "ブランチを作成"; check tag "$W" "タグを作成"
check stashSave "$W" "変更を退避"; check stashList "$W" "退避リスト"; check stashPop "$W" "退避した変更を復元"
check bisectStart "$W" "二分探索"; check bisectGood "$W" "二分探索: 良い"; check bisectBad "$W" "二分探索: 悪い"; check bisectSkip "$W" "二分探索: スキップ"; check bisectReset "$W" "二分探索を終了"
check editConflict "$C/src/main.swift" "競合の編集"; check resolve "$C" "競合を解決"; check add "$W/日本語メモ.txt" "追加"; check revert "$W/README.md" "元に戻す"
check delete "$W/src/math.swift" "削除"; check deleteKeep "$W/src/math.swift" "ローカルに残す"; check rename "$W/src/math.swift" "名前の変更"
check ignore "$W/debug.log" "無視リストに追加"; check blame "$W/src/main.swift" "Blame"; check clean "$W" "クリーンアップ"
check submoduleAdd "$W" "サブモジュールを追加"; check submoduleUpdate "$W" "サブモジュールを更新"; check submoduleSync "$W" "サブモジュールを同期"
check createPatch "$W" "パッチを作成"; check applyPatch "$W" "パッチを適用"; check export "$W" "エクスポート"
check remotes "$W" "リモートの管理"; check repoSettings "$W" "リポジトリの設定"
check clone "$TMPDIR" "クローン"; check init "$TMPDIR" "リポジトリを作成"; check settings "$W" "設定"; check help "$W" "ヘルプ"; check about "$W" "について"
echo "結果: $pass 件成功, $fail 件失敗"
pkill -x GitGlide; sleep 1; open -g "gitglide://start"
