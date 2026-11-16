import AppKit

/// The 哗啦待办 sweep mark (three cards swept into one checked card), drawn in
/// code so the status bar gets a crisp template image at any size and the
/// island can tint it. Geometry follows assets/logo/mark-sweep.svg (128×100).
enum BrandIcon {
    static func image(size: CGFloat, template: Bool = true, color: NSColor = .black) -> NSImage {
        let img = NSImage(size: NSSize(width: size, height: size), flipped: true) { rect in
            let s = rect.width / 128
            // Centre the 128×100 artwork in the square.
            let dy = (rect.height - 100 * s) / 2
            func card(x: CGFloat, angle: CGFloat, alpha: CGFloat) {
                let r = NSRect(x: x * s, y: 30 * s + dy, width: 78 * s, height: 54 * s)
                let cx = r.midX, cy = r.midY
                let t = NSAffineTransform()
                t.translateX(by: cx, yBy: cy); t.rotate(byDegrees: angle); t.translateX(by: -cx, yBy: -cy)
                let p = NSBezierPath(roundedRect: r, xRadius: 11 * s, yRadius: 11 * s)
                p.transform(using: t as AffineTransform)
                color.withAlphaComponent(alpha).setFill(); p.fill()
            }
            // Flipped context: SVG's counter-clockwise -20° becomes +20°.
            card(x: 22, angle: 20, alpha: 0.22)
            card(x: 26, angle: 10, alpha: 0.5)
            card(x: 30, angle: 0, alpha: 1)
            let check = NSBezierPath()
            check.move(to: NSPoint(x: 53 * s, y: 57 * s + dy))
            check.line(to: NSPoint(x: 63 * s, y: 67 * s + dy))
            check.line(to: NSPoint(x: 84 * s, y: 45 * s + dy))
            check.lineWidth = 8 * s; check.lineCapStyle = .round; check.lineJoinStyle = .round
            // Knock the check out of the front card so the template mask shows paper through it.
            NSGraphicsContext.current?.cgContext.setBlendMode(template ? .clear : .normal)
            (template ? NSColor.black : NSColor(red: 1, green: 0.992, blue: 0.976, alpha: 1)).setStroke()
            check.stroke()
            return true
        }
        img.isTemplate = template
        return img
    }

    static let statusItem: NSImage = image(size: 18)
}
