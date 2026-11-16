import SwiftUI

/// The two looks the web app is getting. Anthropic风 is today's warm editorial
/// style (paper, terracotta, serif headings); Tempo风 is monospace,
/// typewriter-retro with ASCII furniture.
enum Style: String, CaseIterable, Identifiable {
    case anthropic, tempo
    var id: String { rawValue }
    var label: String { self == .anthropic ? "Anthropic风" : "Tempo风" }
    static let defaultsKey = "cn.hola.desktop.style"
}

struct Theme {
    let style: Style
    let paper: Color, ink: Color, accent: Color, muted: Color, line: Color, panel: Color
    let title: Font, body: Font, small: Font, clock: Font, mono: Font

    static let terracotta = Color(red: 0xD9 / 255, green: 0x77 / 255, blue: 0x57 / 255)
    static let warmPaper = Color(red: 0xFF / 255, green: 0xFD / 255, blue: 0xF9 / 255)
    static let inkColor = Color(red: 0x1F / 255, green: 0x1B / 255, blue: 0x15 / 255)

    static func of(_ style: Style) -> Theme {
        switch style {
        case .anthropic:
            return Theme(style: style,
                         paper: warmPaper, ink: inkColor, accent: terracotta,
                         muted: Color(red: 0x7C / 255, green: 0x74 / 255, blue: 0x66 / 255),
                         line: Color(red: 0xE2 / 255, green: 0xDD / 255, blue: 0xD0 / 255),
                         panel: Color(red: 0xF7 / 255, green: 0xF4 / 255, blue: 0xED / 255),
                         title: .system(size: 17, weight: .semibold, design: .serif),
                         body: .system(size: 13),
                         small: .system(size: 11),
                         clock: .system(size: 30, weight: .medium, design: .rounded).monospacedDigit(),
                         mono: .system(size: 12, design: .monospaced))
        case .tempo:
            return Theme(style: style,
                         paper: Color(red: 0xF3 / 255, green: 0xEF / 255, blue: 0xE4 / 255),
                         ink: Color(red: 0x2B / 255, green: 0x27 / 255, blue: 0x22 / 255),
                         accent: Color(red: 0xB4 / 255, green: 0x4C / 255, blue: 0x2E / 255),
                         muted: Color(red: 0x7A / 255, green: 0x73 / 255, blue: 0x68 / 255),
                         line: Color(red: 0xC9 / 255, green: 0xC2 / 255, blue: 0xB3 / 255),
                         panel: Color(red: 0xEC / 255, green: 0xE6 / 255, blue: 0xD8 / 255),
                         title: .system(size: 13, weight: .bold, design: .monospaced),
                         body: .system(size: 12, design: .monospaced),
                         small: .system(size: 11, design: .monospaced),
                         clock: .system(size: 28, weight: .bold, design: .monospaced),
                         mono: .system(size: 12, design: .monospaced))
        }
    }

    var isTempo: Bool { style == .tempo }
    /// Section heading: serif in Anthropic风, `== 标题 ==` in Tempo风.
    func heading(_ s: String) -> String { isTempo ? "== \(s) ==" : s }
    /// Button caption: `[ 开始 ]` in Tempo风.
    func bracket(_ s: String) -> String { isTempo ? "[ \(s) ]" : s }
}

// MARK: - Buttons

struct ThemedButtonStyle: ButtonStyle {
    let theme: Theme
    var primary = false
    func makeBody(configuration: Configuration) -> some View {
        let label = configuration.label
            .font(theme.isTempo ? theme.body : .system(size: 12, weight: .medium))
            .padding(.horizontal, theme.isTempo ? 2 : 10)
            .padding(.vertical, theme.isTempo ? 2 : 5)
        Group {
            if theme.isTempo {
                label.foregroundStyle(primary ? theme.accent : theme.ink)
                    .underline(configuration.isPressed)
            } else {
                label.foregroundStyle(primary ? Color.white : theme.ink)
                    .background(RoundedRectangle(cornerRadius: 8).fill(primary ? theme.accent : theme.panel))
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(primary ? theme.accent : theme.line))
            }
        }
        .opacity(configuration.isPressed ? 0.7 : 1)
        .contentShape(Rectangle())
    }
}

/// A themed checkbox: a terracotta rounded box, or `[ ]` / `[x]` in Tempo风.
struct ThemedCheck: View {
    let theme: Theme
    let checked: Bool
    let enabled: Bool
    var body: some View {
        if theme.isTempo {
            Text(checked ? "[x]" : "[ ]").font(theme.mono).foregroundStyle(enabled || checked ? theme.ink : theme.muted)
        } else {
            ZStack {
                RoundedRectangle(cornerRadius: 4).stroke(checked ? theme.accent : theme.line, lineWidth: 1.5)
                    .background(RoundedRectangle(cornerRadius: 4).fill(checked ? theme.accent : Color.clear))
                if checked { Image(systemName: "checkmark").font(.system(size: 9, weight: .bold)).foregroundStyle(.white) }
            }
            .frame(width: 15, height: 15)
            .opacity(enabled || checked ? 1 : 0.45)
        }
    }
}

/// Progress: a thin terracotta bar, or an ASCII `[####......] 40%` line.
struct ThemedProgress: View {
    let theme: Theme
    let progress: Double
    let ascii: String
    var body: some View {
        if theme.isTempo {
            Text(ascii).font(theme.mono).foregroundStyle(theme.ink)
        } else {
            GeometryReader { g in
                ZStack(alignment: .leading) {
                    Capsule().fill(theme.line)
                    Capsule().fill(theme.accent).frame(width: max(4, g.size.width * progress))
                }
            }.frame(height: 4)
        }
    }
}
