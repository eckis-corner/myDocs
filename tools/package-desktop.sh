#!/usr/bin/env bash
# Baut den Desktop-Client für eine Plattform und packt ihn (M4.4).
#   tools/package-desktop.sh <rid> <version> <ausgabeordner>
# rid: win-x64, osx-arm64, osx-x64, linux-x64. macOS bekommt ein .app-Bundle (nicht signiert), die anderen ein Archiv.
set -euo pipefail
rid="$1"; version="$2"; out="$(mkdir -p "$3" && cd "$3" && pwd)"
root="$(cd "$(dirname "$0")/.." && pwd)"
build="$(mktemp -d)"


dotnet publish "$root/src/MyDocs.Desktop/MyDocs.Desktop.csproj" -c Release -r "$rid" --self-contained \
    -p:Version="$version" -p:PublishSingleFile=false -p:DebugType=none -o "$build/myDocs"

case "$rid" in
  osx-*)
    app="$build/myDocs.app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$build/myDocs/." "$app/Contents/MacOS/"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>myDocs</string>
  <key>CFBundleDisplayName</key><string>myDocs</string>
  <key>CFBundleIdentifier</key><string>de.eckis-corner.mydocs</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleExecutable</key><string>myDocs</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    chmod +x "$app/Contents/MacOS/myDocs"
    (cd "$build" && ditto -c -k --keepParent myDocs.app "$out/myDocs-$version-$rid.zip")
    ;;
  win-*)
    (cd "$build" && 7z a -tzip "$out/myDocs-$version-$rid.zip" myDocs > /dev/null)
    ;;
  *)
    chmod +x "$build/myDocs/myDocs"
    tar -C "$build" -czf "$out/myDocs-$version-$rid.tar.gz" myDocs
    ;;
esac
rm -rf "$build"
echo "$out"
ls -l "$out"
