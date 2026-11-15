import Foundation

public enum PomoMode: String, Codable, Sendable, CaseIterable {
    case focus, rest

    /// Same lengths as the web widget: 25 minutes of focus, 5 of rest.
    public var seconds: Int { self == .focus ? 25 * 60 : 5 * 60 }
    public var other: PomoMode { self == .focus ? .rest : .focus }
}

/// The pomodoro state machine, a direct port of the web widget's
/// `pomoRead/pomoAction/renderPomo`. Pure value type with the clock injected,
/// so it is testable and the same JSON shape could even be shared with the
/// browser's `localStorage` entry.
public struct Pomodoro: Codable, Equatable, Sendable {
    public var mode: PomoMode
    /// Seconds left while paused. Ignored while running (`endsAt` is authoritative).
    public var left: Int
    /// Wall-clock end while running, nil while paused.
    public var endsAt: Date?

    public init(mode: PomoMode = .focus, left: Int? = nil, endsAt: Date? = nil) {
        self.mode = mode
        self.left = left ?? mode.seconds
        self.endsAt = endsAt
    }

    public static func fresh(_ mode: PomoMode) -> Pomodoro { Pomodoro(mode: mode) }

    public var isRunning: Bool { endsAt != nil }

    public func remaining(at now: Date) -> Int {
        if let end = endsAt { return max(0, Int(ceil(end.timeIntervalSince(now)))) }
        return max(0, left)
    }

    /// Elapsed fraction of the current session, 0...1.
    public func progress(at now: Date) -> Double {
        let total = Double(mode.seconds)
        return min(1, max(0, (total - Double(remaining(at: now))) / total))
    }

    /// Start when paused, pause when running.
    public mutating func toggle(at now: Date) {
        if let _ = endsAt {
            left = remaining(at: now)
            endsAt = nil
        } else {
            endsAt = now.addingTimeInterval(TimeInterval(remaining(at: now)))
        }
    }

    public mutating func reset() { self = .fresh(mode) }

    public mutating func switchMode() { self = .fresh(mode.other) }

    /// Call once a second (or on wake). When a running session has run out it
    /// flips to a fresh, *paused* session of the other mode, exactly like the
    /// web widget, and returns the mode that just finished so the UI can ring.
    @discardableResult
    public mutating func tick(at now: Date) -> PomoMode? {
        guard isRunning, remaining(at: now) == 0 else { return nil }
        let finished = mode
        self = .fresh(mode.other)
        return finished
    }

    // MARK: - Presentation helpers shared by every surface

    public static func clock(_ seconds: Int) -> String {
        String(format: "%02d:%02d", seconds / 60, seconds % 60)
    }

    public func clock(at now: Date) -> String { Pomodoro.clock(remaining(at: now)) }

    /// Tempo-style ASCII bar, e.g. `[####......] 40%`.
    public func asciiBar(at now: Date, width: Int = 10) -> String {
        let p = progress(at: now)
        let filled = Int((p * Double(width)).rounded(.down))
        let bar = String(repeating: "#", count: filled) + String(repeating: ".", count: max(0, width - filled))
        return "[\(bar)] \(Int((p * 100).rounded()))%"
    }

    // MARK: - Persistence (UserDefaults; not secret)

    public static let defaultsKey = "cn.hola.desktop.pomodoro"

    public static func load(from defaults: UserDefaults = .standard) -> Pomodoro {
        guard let data = defaults.data(forKey: defaultsKey),
              let p = try? JSONDecoder().decode(Pomodoro.self, from: data) else { return .fresh(.focus) }
        return p
    }

    public func save(to defaults: UserDefaults = .standard) {
        if let data = try? JSONEncoder().encode(self) { defaults.set(data, forKey: Pomodoro.defaultsKey) }
    }
}
