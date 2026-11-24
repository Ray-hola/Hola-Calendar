#!/bin/bash
# Builds 哗啦待办 桌面伴侣 with swiftc directly (no SwiftPM, no Xcode) and
# assembles a signed .app bundle. Works with Command Line Tools alone.
#
#   scripts/build.sh            release build of everything + a universal .app
#   scripts/build.sh test       build and run the unit tests (this Mac's architecture)
#   scripts/build.sh debug      debug build (faster, -Onone, this Mac's architecture)
#   scripts/build.sh run        build then open the app
#
# Environment:
#   SDKROOT   macOS SDK to use. Defaults to the newest SDK whose Swift module
#             matches this compiler (the CLT may ship a newer, unusable SDK).
#   ARCHS     architectures for the release .app, default "arm64 x86_64". The
#             other one is cross-compiled, so no Rosetta is needed.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/build"
MODE="${1:-release}"
APP_NAME="哗啦待办"
BUNDLE_ID="cn.hola.desktop"
MIN_OS="13.0"
HOST_ARCH="$(uname -m)"
TARGET="$HOST_ARCH-apple-macosx$MIN_OS"
ARCHS="${ARCHS:-arm64 x86_64}"
if [ "$MODE" = "test" ] || [ "$MODE" = "debug" ]; then ARCHS="$HOST_ARCH"; fi

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

mkdir -p "$OUT"
cd "$ROOT"

# One folder per architecture. Every binary sits next to libHolaCore.dylib, so
# @executable_path is the only search path: none points back at this machine.
build_arch() {
  local arch="$1" dir="$OUT/$1"
  local common=(-sdk "$SDK" -target "$arch-apple-macosx$MIN_OS" -swift-version 5 $OPT -module-cache-path "$OUT/module-cache")
  local link=(-I "$dir" -L "$dir" -lHolaCore -Xlinker -rpath -Xlinker @executable_path)
  mkdir -p "$dir"
  echo "→ HolaCore ($arch)"
  swiftc "${common[@]}" -parse-as-library -emit-library -emit-module -module-name HolaCore \
    -emit-module-path "$dir/HolaCore.swiftmodule" -o "$dir/libHolaCore.dylib" \
    -Xlinker -install_name -Xlinker @rpath/libHolaCore.dylib \
    Sources/HolaCore/*.swift
  if [ "$MODE" = "test" ]; then
    echo "→ HolaCoreTests ($arch)"
    swiftc "${common[@]}" "${link[@]}" -parse-as-library -module-name HolaCoreTests \
      -o "$dir/HolaCoreTests" Tests/HolaCoreTests/*.swift
    return
  fi
  echo "→ holactl ($arch)"
  swiftc "${common[@]}" "${link[@]}" -module-name holactl -o "$dir/holactl" Sources/holactl/main.swift
  echo "→ HolaDesktop ($arch)"
  swiftc "${common[@]}" "${link[@]}" -parse-as-library -module-name HolaDesktop \
    -framework AppKit -framework SwiftUI -framework ServiceManagement \
    -o "$dir/HolaDesktop" Sources/HolaDesktop/*.swift
}
for arch in $ARCHS; do build_arch "$arch"; done

if [ "$MODE" = "test" ]; then
  echo "→ running"
  "$OUT/$HOST_ARCH/HolaCoreTests"
  exit $?
fi

echo "→ lipo ($ARCHS)"
for bin in libHolaCore.dylib holactl HolaDesktop; do
  # An array keeps each path whole when the checkout path has spaces in it.
  inputs=()
  for arch in $ARCHS; do inputs+=("$OUT/$arch/$bin"); done
  lipo -create "${inputs[@]}" -output "$OUT/$bin"
done

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
