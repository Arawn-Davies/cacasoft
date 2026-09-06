#!/bin/sh
# Builds CacaStudioMac as a real, double-clickable .app bundle instead of a
# bare executable — Finder/Spotlight/Dock all need the Contents/{MacOS,
# Info.plist} structure to treat something as an application at all; a raw
# Mach-O binary launched from Terminal never shows up as a proper app no
# matter how it's invoked.
set -e

cd "$(dirname "$0")"

CONFIG="${1:-release}"
APP_NAME="Caca Studio"
BUNDLE_ID="com.arawn-davies.cacastudiomac"
BUILD_DIR="build"
APP="$BUILD_DIR/$APP_NAME.app"

echo "── Building ($CONFIG) ──────────────────────"
swift build -c "$CONFIG"

BIN_PATH="$(swift build -c "$CONFIG" --show-bin-path)/CacaStudioMac"

echo "── Assembling $APP ──────────────────────"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp "$BIN_PATH" "$APP/Contents/MacOS/CacaStudioMac"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>CacaStudioMac</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>0.1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>14.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.developer-tools</string>
</dict>
</plist>
PLIST

echo "── Ad-hoc signing ──────────────────────"
codesign --force --deep --sign - "$APP"

echo "── Done: $APP ──────────────────────"
