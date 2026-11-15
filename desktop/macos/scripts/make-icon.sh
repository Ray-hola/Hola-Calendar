#!/bin/bash
# Renders the brand mark (assets/logo/icon-sweep.svg at the repo root) into an
# .icns with a throwaway Swift program: NSImage reads SVG natively on macOS 11+.
# Usage: make-icon.sh <out.icns> <sdk> <target>
set -euo pipefail
OUT="$1"; SDK="$2"; TARGET="$3"
HERE="$(cd "$(dirname "$0")" && pwd)"
SVG="$HERE/../../../assets/logo/icon-sweep.svg"
[ -f "$SVG" ] || { echo "no logo at $SVG"; exit 1; }
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
cat > "$WORK/render.swift" <<'EOF'
import AppKit
let args = CommandLine.arguments
guard let svg = NSImage(contentsOfFile: args[1]) else { fatalError("cannot read svg") }
let setDir = args[2]
for (name, px) in [("16x16", 16), ("16x16@2x", 32), ("32x32", 32), ("32x32@2x", 64), ("128x128", 128), ("128x128@2x", 256),
                   ("256x256", 256), ("256x256@2x", 512), ("512x512", 512), ("512x512@2x", 1024)] {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4,
                               hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    // macOS icons sit inside a rounded square with ~10% margin.
    let side = CGFloat(px), inset = side * 0.08
    let box = NSRect(x: inset, y: inset, width: side - 2 * inset, height: side - 2 * inset)
    let path = NSBezierPath(roundedRect: box, xRadius: side * 0.2, yRadius: side * 0.2)
    NSColor(red: 1.0, green: 0.992, blue: 0.976, alpha: 1).setFill(); path.fill()
    let logoBox = box.insetBy(dx: box.width * 0.14, dy: box.height * 0.14)
    svg.draw(in: logoBox, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.high])
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(setDir)/icon_\(name).png"))
}
EOF
swiftc -sdk "$SDK" -target "$TARGET" -O "$WORK/render.swift" -o "$WORK/render" 2>/dev/null
mkdir -p "$WORK/AppIcon.iconset"
"$WORK/render" "$SVG" "$WORK/AppIcon.iconset"
iconutil -c icns "$WORK/AppIcon.iconset" -o "$OUT"
echo "icon: $OUT"
