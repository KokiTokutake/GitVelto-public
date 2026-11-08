#!/bin/bash
# Builds GitGlide.app (main app + embedded Finder Sync extension) without Xcode.
# Requires only the Command Line Tools (swiftc, codesign).
#
#   ./build.sh            # release build into build/GitGlide.app
#   ./build.sh install    # build, copy to /Applications (or ~/Applications) and enable the extension
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(pwd)"
VERSION="1.0.0"
BUILD_NUMBER="$(date +%Y%m%d%H%M)"
ARCH="${ARCH:-$(uname -m)}"
MIN_OS="14.0"
BUILD="$ROOT/build"
APP="$BUILD/GitGlide.app"
APPEX="$APP/Contents/PlugIns/GitGlideFinderSync.appex"
SIGN_ID="${SIGN_ID:--}"   # "-" = ad-hoc. Set SIGN_ID="Apple Development: ..." to use a real certificate.

export TMPDIR="${TMPDIR:-/tmp}"
MODULE_CACHE="$BUILD/ModuleCache"
mkdir -p "$MODULE_CACHE"
export CLANG_MODULE_CACHE_PATH="$MODULE_CACHE"

SDK="$(xcrun --show-sdk-path 2>/dev/null)"
SWIFTC=(xcrun swiftc -sdk "$SDK" -module-cache-path "$MODULE_CACHE" -O -whole-module-optimization)

build_arch() {
    local arch="$1" out_app="$2" out_ext="$3"
    local target="$arch-apple-macos$MIN_OS"
    echo "==> Compiling app ($arch)"
    "${SWIFTC[@]}" -target "$target" -module-name GitGlide \
        Sources/Shared/*.swift Sources/App/*.swift Sources/App/Core/*.swift Sources/App/UI/*.swift \
        -o "$out_app"
    echo "==> Compiling Finder Sync extension ($arch)"
    "${SWIFTC[@]}" -target "$target" -module-name GitGlideFinderSync -application-extension \
        -Xlinker -e -Xlinker _NSExtensionMain \
        Sources/Shared/*.swift Sources/FinderSync/*.swift \
        -o "$out_ext"
}

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APPEX/Contents/MacOS"

if [ "$ARCH" = "universal" ]; then
    build_arch arm64 "$BUILD/GitGlide-arm64" "$BUILD/GitGlideFinderSync-arm64"
    build_arch x86_64 "$BUILD/GitGlide-x86_64" "$BUILD/GitGlideFinderSync-x86_64"
    lipo -create "$BUILD/GitGlide-arm64" "$BUILD/GitGlide-x86_64" -output "$APP/Contents/MacOS/GitGlide"
    lipo -create "$BUILD/GitGlideFinderSync-arm64" "$BUILD/GitGlideFinderSync-x86_64" -output "$APPEX/Contents/MacOS/GitGlideFinderSync"
else
    build_arch "$ARCH" "$APP/Contents/MacOS/GitGlide" "$APPEX/Contents/MacOS/GitGlideFinderSync"
fi

echo "==> Bundling"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD_NUMBER/" Resources/App-Info.plist > "$APP/Contents/Info.plist"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD_NUMBER/" Resources/FinderSync-Info.plist > "$APPEX/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# Icon: Resources/AppIcon.png (artwork) -> Resources/AppIcon.icns. Without artwork the built-in drawing is used.
if [ ! -f Resources/AppIcon.icns ] || { [ -f Resources/AppIcon.png ] && [ Resources/AppIcon.png -nt Resources/AppIcon.icns ]; }; then
    echo "==> Generating icon"
    "${SWIFTC[@]}" -target "$(uname -m)-apple-macos$MIN_OS" scripts/make_icon.swift -o "$BUILD/make_icon"
    if [ -f Resources/AppIcon.png ]; then
        "$BUILD/make_icon" Resources/AppIcon.icns Resources/AppIcon.png
    else
        "$BUILD/make_icon" Resources/AppIcon.icns
    fi
fi
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

echo "==> Signing ($SIGN_ID)"
# A stable designated requirement keeps macOS privacy permissions (TCC) valid across rebuilds,
# which an ad-hoc cdhash-based requirement would not.
if [ "$SIGN_ID" = "-" ]; then
    EXT_REQ=(--requirements '=designated => identifier "com.ktoku.GitGlide.FinderSync"')
    APP_REQ=(--requirements '=designated => identifier "com.ktoku.GitGlide"')
else
    EXT_REQ=(); APP_REQ=()
fi
codesign --force --sign "$SIGN_ID" "${EXT_REQ[@]}" --entitlements Resources/FinderSync.entitlements --timestamp=none "$APPEX"
codesign --force --sign "$SIGN_ID" "${APP_REQ[@]}" --timestamp=none "$APP"
codesign --verify --deep --strict "$APP"
echo "==> Built $APP"

if [ "${1:-}" = "install" ]; then
    DEST="/Applications"
    [ -w "$DEST" ] || DEST="$HOME/Applications"
    mkdir -p "$DEST"
    echo "==> Installing to $DEST"
    FIRST_INSTALL=1
    pluginkit -m -i com.ktoku.GitGlide.FinderSync 2>/dev/null | grep -q GitGlide && FIRST_INSTALL=0
    pkill -x GitGlide 2>/dev/null || true
    rm -rf "$DEST/GitGlide.app"
    cp -R "$APP" "$DEST/"
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$DEST/GitGlide.app" || true
    pluginkit -a "$DEST/GitGlide.app/Contents/PlugIns/GitGlideFinderSync.appex" || true
    pluginkit -e use -i com.ktoku.GitGlide.FinderSync || true
    open "$DEST/GitGlide.app"
    # Finder only loads the new extension binary after a restart (windows are restored automatically).
    pkill -f GitGlideFinderSync.appex 2>/dev/null || true
    killall Finder 2>/dev/null || true
    [ "$FIRST_INSTALL" = "1" ] && echo "==> 初回インストール: Finder 拡張を有効化しました"
    echo "==> Installed. Finder を右クリックして GitGlide メニューを確認してください。"
fi
