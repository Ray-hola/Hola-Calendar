#!/bin/bash
# Builds 哗啦待办 桌面伴侣 with swiftc directly (no SwiftPM, no Xcode) and
# assembles a signed .app bundle. Works with Command Line Tools alone.
#
#   scripts/build.sh            release build of everything + .app
#   scripts/build.sh test       build and run the unit tests
#   scripts/build.sh debug      debug build (faster, -Onone)
#   scripts/build.sh run        build then open the app
#
# Environment:
#   SDKROOT   macOS SDK to use. Defaults to the newest SDK whose Swift module
#             matches this compiler (the CLT may ship a newer, unusable SDK).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build"
MODE="${1:-release}"
APP_NAME="哗啦待办"
BUNDLE_ID="cn.hola.desktop"
MIN_OS="13.0"
TARGET="arm64-apple-macosx$MIN_OS"
case "$(uname -m)" in x86_64) TARGET="x86_64-apple-macosx$MIN_OS";; esac

sdk_ok() {  # the SDK's stdlib must have been built by this exact compiler
  local probe; probe="$(mktemp -d)"; printf 'import Foundation\nlet x = Date()\n' > "$probe/p.swift"
  local out; out="$(swiftc -sdk "$1" -target "$TARGET" -module-cache-path "$probe/cache" -typecheck "$probe/p.swift" 2>&1)"
  rm -rf "$probe"; ! grep -q "error:" <<<"$out"
}
pick_sdk() {
  local sdk
  if [ -n "${SDKROOT:-}" ]; then
    if sdk_ok "$SDKROOT"; then echo "$SDKROOT"; return; fi
    echo "SDKROOT=$SDKROOT does not match this compiler; looking for another SDK" >&2
  fi
  local dir="$(dirname "$(xcrun --show-sdk-path --sdk macosx 2>/dev/null || echo /Library/Developer/CommandLineTools/SDKs/x)")"
  for sdk in $(ls -d "$dir"/MacOSX*.*.sdk 2>/dev/null | sort -rV); do
    if sdk_ok "$sdk"; then echo "$sdk"; return; fi
  done
  echo "no usable macOS SDK found under $dir" >&2; exit 1
}
SDK="$(pick_sdk)"
echo "SDK: $SDK"

OPT="-O"; [ "$MODE" = "debug" ] && OPT="-Onone -g"
COMMON=(-sdk "$SDK" -target "$TARGET" -swift-version 5 $OPT -module-cache-path "$OUT/module-cache")

mkdir -p "$OUT"
cd "$ROOT"

echo "→ HolaCore"
swiftc "${COMMON[@]}" -parse-as-library -emit-library -emit-module -module-name HolaCore \
  -emit-module-path "$OUT/HolaCore.swiftmodule" -o "$OUT/libHolaCore.dylib" \
  -Xlinker -install_name -Xlinker @rpath/libHolaCore.dylib \
  Sources/HolaCore/*.swift

LINK_CORE=(-I "$OUT" -L "$OUT" -lHolaCore -Xlinker -rpath -Xlinker @executable_path -Xlinker -rpath -Xlinker "$OUT")

if [ "$MODE" = "test" ]; then
  echo "→ HolaCoreTests"
  swiftc "${COMMON[@]}" "${LINK_CORE[@]}" -parse-as-library -module-name HolaCoreTests \
    -o "$OUT/HolaCoreTests" Tests/HolaCoreTests/*.swift
  echo "→ running"
  "$OUT/HolaCoreTests"
  exit $?
fi

echo "→ holactl"
swiftc "${COMMON[@]}" "${LINK_CORE[@]}" -module-name holactl -o "$OUT/holactl" Sources/holactl/main.swift

echo "→ HolaDesktop"
swiftc "${COMMON[@]}" "${LINK_CORE[@]}" -parse-as-library -module-name HolaDesktop \
  -framework AppKit -framework SwiftUI -framework ServiceManagement \
  -o "$OUT/HolaDesktop" Sources/HolaDesktop/*.swift

APP="$OUT/$APP_NAME.app"
echo "→ $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$OUT/HolaDesktop" "$APP/Contents/MacOS/HolaDesktop"
cp "$OUT/libHolaCore.dylib" "$APP/Contents/MacOS/libHolaCore.dylib"
sed -e "s/__BUNDLE_ID__/$BUNDLE_ID/g" -e "s/__MIN_OS__/$MIN_OS/g" Resources/Info.plist > "$APP/Contents/Info.plist"
echo -n "APPL????" > "$APP/Contents/PkgInfo"
if [ -f "$OUT/AppIcon.icns" ] || "$ROOT/scripts/make-icon.sh" "$OUT/AppIcon.icns" "$SDK" "$TARGET"; then
  cp "$OUT/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
fi
# Ad-hoc signature: enough for SMAppService and the Keychain on this Mac.
# Replace `-` with a Developer ID to distribute.
codesign --force --sign "${CODESIGN_IDENTITY:--}" --identifier "$BUNDLE_ID" "$APP/Contents/MacOS/libHolaCore.dylib"
codesign --force --sign "${CODESIGN_IDENTITY:--}" --identifier "$BUNDLE_ID" "$APP"
codesign --verify --deep --strict "$APP" && echo "signed ($(codesign -dv "$APP" 2>&1 | grep -o 'Signature=.*'))"

if [ "$MODE" = "run" ]; then open "$APP"; fi
echo "done: $APP"
