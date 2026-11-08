#!/bin/bash
# Renders every dialog off-screen into PNG files for visual review (no windows are shown).
#   scripts/snapshot.sh [out-dir] [filter]
set -euo pipefail
cd "$(dirname "$0")/.."
OUT="${1:-build/snapshots}"
FILTER="${2:-}"
REPOS="${GITVELTO_TEST_REPOS:-${TMPDIR:-/tmp}/gitvelto-test-repos}"
mkdir -p build/ModuleCache
export CLANG_MODULE_CACHE_PATH="$PWD/build/ModuleCache"
scripts/make_test_repos.sh "$REPOS" >/dev/null
APP_SOURCES=$(ls Sources/App/*.swift Sources/App/Core/*.swift Sources/App/UI/*.swift | grep -v "Sources/App/main.swift")
xcrun swiftc -module-cache-path build/ModuleCache -module-name GitVelto -O \
    Sources/Shared/*.swift $APP_SOURCES Tests/Snapshot/main.swift -o build/snapshot 2>&1 | grep -v xcrun_db | grep -E "error" || true
build/snapshot "$REPOS" "$OUT" "$FILTER"
