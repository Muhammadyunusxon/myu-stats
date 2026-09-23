#!/usr/bin/env bash
# Builds release binaries and wraps them into an ad-hoc signed "MYU STATS.app".
set -euo pipefail
cd "$(dirname "$0")/.."

swift build -c release
BIN_DIR="$(swift build -c release --show-bin-path)"
APP="build/MYU STATS.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/MYUStats" "$APP/Contents/MacOS/MYUStats"
# Privileged fan helper; only ever run through an administrator prompt.
cp "$BIN_DIR/myustats-fan" "$APP/Contents/MacOS/myustats-fan"
cp Resources/Info.plist "$APP/Contents/Info.plist"
# Sign the nested helper first, then the bundle around it.
codesign --force --sign - "$APP/Contents/MacOS/myustats-fan"
codesign --force --sign - "$APP"

echo "Built $APP"
