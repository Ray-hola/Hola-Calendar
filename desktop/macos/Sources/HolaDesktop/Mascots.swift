import SwiftUI
import HolaCore

/// What the mascot is doing; derived from the pomodoro.
enum MascotMood { case idle, focus, rest }

extension Pomodoro {
    var mood: MascotMood { isRunning ? (mode == .focus ? .focus : .rest) : .idle }
}

/// Picks the mascot for the current style.
struct Mascot: View {
    let style: Style
    let mood: MascotMood
    var size: CGFloat = 56
    var body: some View {
        switch style {
        case .anthropic: CardBuddy(mood: mood, size: size)
        case .tempo: PixelSprite(mood: mood, size: size)
        }
    }
}

// MARK: - Anthropic风: 小卡, an original paper-card character
//
// Deliberately *not* Anthropic's Claude mark or any official Claude character
// (those are Anthropic trademarks and this app ships to users). 小卡 is one of
// the brand's own cards from the sweep logo, given a face: it bounces while a
// focus session runs, dozes during the break and breathes slowly when idle.

struct CardBuddy: View {
    let mood: MascotMood
    var size: CGFloat = 56

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 24)) { ctx in
            let t = ctx.date.timeIntervalSinceReferenceDate
            Canvas { g, sz in draw(&g, sz, t) }
        }
        .frame(width: size, height: size)
        .accessibilityLabel(mood == .rest ? "小卡在休息" : mood == .focus ? "小卡在专注" : "小卡")
    }

    private func draw(_ g: inout GraphicsContext, _ sz: CGSize, _ t: Double) {
        let ink = Theme.inkColor, accent = Theme.terracotta, paper = Theme.warmPaper
        let w = sz.width * 0.72, h = sz.height * 0.56
        // Motion
        var dy: CGFloat = 0, squash: CGFloat = 1, tilt: Double = 0
        switch mood {
        case .focus:
            let phase = (t * 2.6).truncatingRemainder(dividingBy: 1)       // one hop per ~0.38 s
            dy = -CGFloat(abs(sin(phase * .pi))) * sz.height * 0.14
            squash = 1 + CGFloat(sin(phase * .pi * 2)) * 0.05
            tilt = sin(t * 2.6 * .pi) * 3
        case .rest:
            squash = 1 + CGFloat(sin(t * 1.2)) * 0.02                       // slow breathing
            tilt = -6
        case .idle:
            squash = 1 + CGFloat(sin(t * 1.6)) * 0.015
        }
        let cx = sz.width / 2, cy = sz.height * 0.6 + dy
        g.translateBy(x: cx, y: cy)
        g.rotate(by: .degrees(tilt))
        g.scaleBy(x: 1 / squash, y: squash)

        // Two faint cards behind, like the logo's sweep.
        for (i, a) in [(2, 0.18), (1, 0.4)] {
            var ghost = g
            ghost.rotate(by: .degrees(Double(-8 * i)))
            ghost.translateBy(x: CGFloat(-4 * i), y: 0)
            ghost.fill(Path(roundedRect: CGRect(x: -w / 2, y: -h / 2, width: w, height: h), cornerRadius: h * 0.2),
                       with: .color(accent.opacity(a)))
        }
        // The card itself: paper with a terracotta edge.
        let card = Path(roundedRect: CGRect(x: -w / 2, y: -h / 2, width: w, height: h), cornerRadius: h * 0.2)
        g.fill(card, with: .color(paper))
        g.stroke(card, with: .color(accent), lineWidth: max(1.5, sz.width / 32))

        // Face
        let eyeY = -h * 0.12, eyeDX = w * 0.18, eyeR = max(1.6, sz.width / 26)
        let blink = mood != .rest && (t.truncatingRemainder(dividingBy: 3.7)) < 0.12
        if mood == .rest || blink {
            for s in [-1.0, 1.0] {
                var p = Path(); p.move(to: CGPoint(x: s * eyeDX - eyeR * 1.3, y: eyeY)); p.addLine(to: CGPoint(x: s * eyeDX + eyeR * 1.3, y: eyeY))
                g.stroke(p, with: .color(ink), style: StrokeStyle(lineWidth: eyeR * 0.9, lineCap: .round))
            }
        } else {
            for s in [-1.0, 1.0] {
                g.fill(Path(ellipseIn: CGRect(x: s * eyeDX - eyeR, y: eyeY - eyeR, width: eyeR * 2, height: eyeR * 2)), with: .color(ink))
            }
        }
        var mouth = Path()
        let my = h * 0.18, mw = w * 0.14
        if mood == .rest {
            mouth.addEllipse(in: CGRect(x: -mw * 0.3, y: my - mw * 0.3, width: mw * 0.6, height: mw * 0.7))
            g.stroke(mouth, with: .color(ink), lineWidth: eyeR * 0.7)
        } else {
            mouth.move(to: CGPoint(x: -mw, y: my))
            mouth.addQuadCurve(to: CGPoint(x: mw, y: my), control: CGPoint(x: 0, y: my + mw * (mood == .focus ? 1.2 : 0.7)))
            g.stroke(mouth, with: .color(ink), style: StrokeStyle(lineWidth: eyeR * 0.8, lineCap: .round))
        }
        // Cheeks when focusing hard.
        if mood == .focus {
            for s in [-1.0, 1.0] {
                g.fill(Path(ellipseIn: CGRect(x: s * eyeDX * 1.5 - eyeR, y: my - eyeR * 1.4, width: eyeR * 2, height: eyeR * 1.2)), with: .color(accent.opacity(0.35)))
            }
        }
        // zZ drifting up while resting.
        if mood == .rest {
            for k in 0..<2 {
                let ph = ((t * 0.5) + Double(k) * 0.5).truncatingRemainder(dividingBy: 1)
                let x = w / 2 + CGFloat(6 + k * 6) + CGFloat(sin(ph * .pi * 2)) * 2
                let y = -h / 2 - CGFloat(ph) * sz.height * 0.35
                g.draw(Text("z").font(.system(size: sz.width / (k == 0 ? 7 : 9), weight: .bold, design: .serif)).foregroundColor(accent.opacity(1 - ph)),
                       at: CGPoint(x: x, y: y))
            }
        }
    }
}

// MARK: - Tempo风: original pixel sprites, drawn as code grids
//
// `#` ink, `o` accent (sand / coffee), `w` paper highlight, `s` steam, `.` empty.
// An hourglass runs while focusing, a mug steams during the break, and the
// hourglass sits still (with a blinking grain) when idle.

enum PixelArt {
    static let hourglass: [[String]] = [
        ["#########",
         ".#wwwww#.",
         ".#ooooo#.",
         "..#ooo#..",
         "...#o#...",
         "....#....",
         "...#.#...",
         "..#...#..",
         ".#.....#.",
         ".#.....#.",
         "#########"],
        ["#########",
         ".#.wwww#.",
         ".#.oooo#.",
         "..#ooo#..",
         "...#o#...",
         "....o....",
         "...#o#...",
         "..#...#..",
         ".#.....#.",
         ".#..o..#.",
         "#########"],
        ["#########",
         ".#.....#.",
         ".#.ooo.#.",
         "..#ooo#..",
         "...#o#...",
         "....o....",
         "...#.#...",
         "..#.o.#..",
         ".#..o..#.",
         ".#.ooo.#.",
         "#########"],
        ["#########",
         ".#.....#.",
         ".#.....#.",
         "..#.o.#..",
         "...#o#...",
         "....o....",
         "...#.#...",
         "..#...#..",
         ".#.ooo.#.",
         ".#ooooo#.",
         "#########"],
    ]

    static let mug: [[String]] = [
        ["..s...s...",
         ".s..s..s..",
         "..........",
         ".#######..",
         ".#ooooo###",
         ".#ooooo#.#",
         ".#ooooo#.#",
         ".#ooooo###",
         ".#######..",
         "..#####..."],
        ["...s...s..",
         "..s..s....",
         ".s........",
         ".#######..",
         ".#ooooo###",
         ".#ooooo#.#",
         ".#ooooo#.#",
         ".#ooooo###",
         ".#######..",
         "..#####..."],
        [".s...s....",
         "..s...s.s.",
         "....s.....",
         ".#######..",
         ".#ooooo###",
         ".#ooooo#.#",
         ".#ooooo#.#",
         ".#ooooo###",
         ".#######..",
         "..#####..."],
    ]

    /// Idle: the full hourglass with one grain blinking.
    static let idle: [[String]] = [hourglass[0], hourglass[0].enumerated().map { i, row in
        i == 2 ? ".#oo.oo#." : row
    }]
}

struct PixelSprite: View {
    let mood: MascotMood
    var size: CGFloat = 56
    @Environment(\.islandDark) private var dark
    /// On the black island the ink pixels turn to paper so the sprite stays visible.
    private var ink: Color { dark ? Theme.warmPaper : Theme.of(.tempo).ink }
    private var accent: Color { Theme.of(.tempo).accent }
    private var paper: Color { dark ? Color.white.opacity(0.5) : Color.white.opacity(0.9) }
    private var steam: Color { dark ? Color.white.opacity(0.45) : Theme.of(.tempo).muted }

    var frames: [[String]] {
        switch mood {
        case .focus: return PixelArt.hourglass
        case .rest: return PixelArt.mug
        case .idle: return PixelArt.idle
        }
    }

    var body: some View {
        let fps: Double = mood == .idle ? 0.8 : 3.3   // retro: a few frames per second
        TimelineView(.periodic(from: .now, by: 1 / fps)) { ctx in
            let fs = frames
            let n = Int((ctx.date.timeIntervalSinceReferenceDate * fps).rounded(.down)) % fs.count
            Canvas { g, sz in
                let frame = fs[n]
                let cols = frame.map(\.count).max() ?? 1, rows = frame.count
                let px = floor(min(sz.width / CGFloat(cols), sz.height / CGFloat(rows)))
                let ox = (sz.width - px * CGFloat(cols)) / 2, oy = (sz.height - px * CGFloat(rows)) / 2
                for (r, line) in frame.enumerated() {
                    for (c, ch) in line.enumerated() {
                        let color: Color?
                        switch ch { case "#": color = ink; case "o": color = accent; case "w": color = paper; case "s": color = steam; default: color = nil }
                        if let color {
                            g.fill(Path(CGRect(x: ox + CGFloat(c) * px, y: oy + CGFloat(r) * px, width: px, height: px)), with: .color(color))
                        }
                    }
                }
            }
        }
        .frame(width: size, height: size)
        .accessibilityLabel(mood == .rest ? "休息中" : mood == .focus ? "专注中" : "待机")
    }
}
