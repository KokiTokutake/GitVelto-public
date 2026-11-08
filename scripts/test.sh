#!/bin/bash
# Runs the functional tests against freshly created sample repositories.
set -euo pipefail
cd "$(dirname "$0")/.."
REPOS="${GITVELTO_TEST_REPOS:-${TMPDIR:-/tmp}/gitvelto-test-repos}"
mkdir -p build/ModuleCache
export CLANG_MODULE_CACHE_PATH="$PWD/build/ModuleCache"
scripts/make_test_repos.sh "$REPOS" >/dev/null
APP_SOURCES=$(ls Sources/App/*.swift Sources/App/Core/*.swift Sources/App/UI/*.swift | grep -v "Sources/App/main.swift")
xcrun swiftc -module-cache-path build/ModuleCache -module-name GitVelto \
    Sources/Shared/*.swift $APP_SOURCES Tests/Functional/main.swift -o build/functional-tests 2>&1 | grep -v xcrun_db | grep -E "error" || true
build/functional-tests "$REPOS"
