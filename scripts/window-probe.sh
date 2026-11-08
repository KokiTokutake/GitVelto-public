#!/bin/bash
# Opens every dialog in a real window (twice each) to catch AppKit/SwiftUI layout crashes
# that off-screen snapshots cannot reproduce. Windows appear briefly on screen.
#   scripts/window-probe.sh
set -uo pipefail
cd "$(dirname "$0")/.."
REPOS="${GITVELTO_TEST_REPOS:-${TMPDIR:-/tmp}/gitvelto-test-repos}"
mkdir -p build/ModuleCache
export CLANG_MODULE_CACHE_PATH="$PWD/build/ModuleCache"
scripts/make_test_repos.sh "$REPOS" >/dev/null
APP_SOURCES=$(ls Sources/App/*.swift Sources/App/Core/*.swift Sources/App/UI/*.swift | grep -v "Sources/App/main.swift")
xcrun swiftc -module-cache-path build/ModuleCache -module-name GitVelto \
    Sources/Shared/*.swift $APP_SOURCES Tests/WindowProbe/main.swift -o build/window-probe 2>&1 | grep -v xcrun_db | grep error || true
W="$REPOS/work"; C="$REPOS/conflict"; FAIL=0
run() {
    local ok=0
    for i in 1 2; do [ "$(build/window-probe "$1" "$2" 2>/dev/null | tail -1)" = "survived" ] && ok=$((ok+1)); done
    if [ $ok -eq 2 ]; then echo "ok    $1"; else echo "CRASH $1"; FAIL=1; fi
}
for a in commit log status sync pull fetch push switch branch tag merge rebase stashSave stashList add revert \
         delete deleteKeep ignore clean repoBrowser reflog refBrowser bisectStart submoduleAdd submoduleUpdate \
         createPatch applyPatch export remotes repoSettings settings help about clone init; do run $a "$W"; done
run diff "$W/README.md"; run diffPrevious "$W/src/main.swift"; run blame "$W/src/main.swift"; run rename "$W/src/main.swift"
run resolve "$C"; run editConflict "$C/src/main.swift"
exit $FAIL
