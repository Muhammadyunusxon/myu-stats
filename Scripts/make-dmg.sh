#!/usr/bin/env bash
# Packs "MYU STATS.app" into build/MYU-STATS.dmg: the app, a link to /Applications to drag it onto,
# and the app icon as the volume icon. The file keeps one name in every release, so
# releases/latest/download/MYU-STATS.dmg always resolves to the newest one.
set -euo pipefail
cd "$(dirname "$0")/.."

./Scripts/bundle.sh

APP="build/MYU STATS.app"
DMG="build/MYU-STATS.dmg"
VOLUME="MYU STATS"
WORK="$(mktemp -d)"
trap 'hdiutil detach -quiet "$WORK/mnt" 2>/dev/null || true; rm -rf "$WORK"' EXIT

mkdir -p "$WORK/root"
ditto "$APP" "$WORK/root/$VOLUME.app"
ln -s /Applications "$WORK/root/Applications"
cp Resources/AppIcon.icns "$WORK/root/.VolumeIcon.icns"

# Read-write first, so the volume can be flagged as having a custom icon; then compressed.
hdiutil create -quiet -volname "$VOLUME" -srcfolder "$WORK/root" -fs HFS+ -format UDRW -ov "$WORK/rw.dmg"
mkdir "$WORK/mnt"
hdiutil attach -quiet -nobrowse -noautoopen -mountpoint "$WORK/mnt" "$WORK/rw.dmg"
if command -v SetFile >/dev/null; then SetFile -a C "$WORK/mnt"; fi
hdiutil detach -quiet "$WORK/mnt"

rm -f "$DMG"
hdiutil convert -quiet "$WORK/rw.dmg" -format UDZO -imagekey zlib-level=9 -o "$DMG"
echo "Built $DMG"
