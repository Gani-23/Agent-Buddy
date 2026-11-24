#!/bin/bash
# Production macOS build of AgentBuddy -> ~/Desktop/AgentBuddy.app (+ zip)
# Mirrors the "build-macos" job in .github/workflows/binaries.yml
set -euo pipefail

cd "$(dirname "$0")/.."            # repo root (AgentBuddy/)
RID="${1:-osx-arm64}"              # Apple Silicon by default; pass osx-x64 for Intel
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' AgentBuddy.csproj | head -1)"
APP_NAME="AgentBuddy"
PUBLISH_DIR="bin/Release/net10.0/$RID/publish"
STAGE_DIR="dist/$APP_NAME.app"
DESKTOP="$HOME/Desktop"

echo "==> AgentBuddy $VERSION ($RID) production build"
command -v dotnet >/dev/null || { echo "❌ dotnet SDK not found (need .NET 10)"; exit 1; }

echo "==> Publishing (Release, self-contained)"
rm -rf "$PUBLISH_DIR"
dotnet publish AgentBuddy.csproj -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" -p:AssemblyVersion="$VERSION.0" -p:FileVersion="$VERSION.0" -p:InformationalVersion="$VERSION"

echo "==> Building .app bundle"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR/Contents/MacOS" "$STAGE_DIR/Contents/Resources"
# Copy the whole publish output (executable + dlls + native libs)
cp -R "$PUBLISH_DIR/." "$STAGE_DIR/Contents/MacOS/"
chmod +x "$STAGE_DIR/Contents/MacOS/$APP_NAME"

cat > "$STAGE_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>AgentBuddy</string>
  <key>CFBundleDisplayName</key><string>AgentBuddy</string>
  <key>CFBundleIdentifier</key><string>com.gani.agentbuddy</string>
  <key>CFBundleExecutable</key><string>AgentBuddy</string>
  <key>CFBundleIconFile</key><string>AgentBuddy</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

if [ -f "Assets/AgentBuddy.ico" ]; then
  echo "==> Building app icon"
  ICON_PNG="$(mktemp -d)/AgentBuddy.png"; ICONSET="$(mktemp -d)/AgentBuddy.iconset"
  sips -s format png "Assets/AgentBuddy.ico" --out "$ICON_PNG" >/dev/null
  mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    sips -z $s $s "$ICON_PNG" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    d=$((s*2)); sips -z $d $d "$ICON_PNG" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$STAGE_DIR/Contents/Resources/AgentBuddy.icns" || echo "⚠️  icon build skipped"
fi

echo "==> Zipping"
ZIP="dist/AgentBuddy-macos-$RID.zip"
rm -f "$ZIP"
ditto -c -k --sequesterRsrc --keepParent "$STAGE_DIR" "$ZIP"

if [ "${NO_DESKTOP:-0}" = "1" ]; then
  echo ""
  echo "✅ Done: $ZIP  (version $VERSION, $RID; Desktop copy skipped)"
  exit 0
fi

echo "==> Putting it on the Desktop"
if [ -d "$DESKTOP/$APP_NAME.app" ]; then
  rm -rf "$DESKTOP/$APP_NAME.previous.app"
  mv "$DESKTOP/$APP_NAME.app" "$DESKTOP/$APP_NAME.previous.app"
  echo "   (old Desktop copy kept as AgentBuddy.previous.app)"
fi
ditto "$STAGE_DIR" "$DESKTOP/$APP_NAME.app"
cp "$ZIP" "$DESKTOP/"

echo ""
echo "✅ Done: $DESKTOP/$APP_NAME.app  (version $VERSION, $RID)"
echo "   Zip:  $DESKTOP/$(basename "$ZIP")"
