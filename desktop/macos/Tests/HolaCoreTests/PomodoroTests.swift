import Foundation
import HolaCore

func pomodoroTests() async {
    let t0 = Date(timeIntervalSince1970: 1_700_000_000)

    await test("fresh focus is paused at 25 minutes") {
        let p = Pomodoro.fresh(.focus)
        check(!p.isRunning)
        check(p.remaining(at: t0), 1500)
        check(p.clock(at: t0), "25:00")
        check(p.progress(at: t0), 0)
    }

    await test("toggle starts, then pauses keeping the remaining time") {
        var p = Pomodoro.fresh(.focus)
        p.toggle(at: t0)
        check(p.isRunning)
        check(p.remaining(at: t0.addingTimeInterval(47)), 1500 - 47)
        p.toggle(at: t0.addingTimeInterval(47))
        check(!p.isRunning)
        check(p.left, 1453)
        check(p.remaining(at: t0.addingTimeInterval(500)), 1453) // paused time does not tick
        p.toggle(at: t0.addingTimeInterval(500))
        check(p.remaining(at: t0.addingTimeInterval(503)), 1450)
    }

    await test("tick flips to a paused rest when focus runs out, rings once") {
        var p = Pomodoro.fresh(.focus)
        p.toggle(at: t0)
        check(p.tick(at: t0.addingTimeInterval(1499)) == nil)
        check(p.tick(at: t0.addingTimeInterval(1500)) == .focus)
        check(p.mode, .rest)
        check(!p.isRunning)
        check(p.remaining(at: t0.addingTimeInterval(1500)), 300)
        check(p.tick(at: t0.addingTimeInterval(1501)) == nil)
    }

    await test("a tick long after sleep still finishes the session") {
        var p = Pomodoro.fresh(.rest)
        p.toggle(at: t0)
        check(p.tick(at: t0.addingTimeInterval(9_999)) == .rest)
        check(p.mode, .focus)
    }

    await test("reset and switch") {
        var p = Pomodoro.fresh(.focus)
        p.toggle(at: t0)
        p.reset()
        check(p, .fresh(.focus))
        p.switchMode()
        check(p, .fresh(.rest))
        p.switchMode()
        check(p, .fresh(.focus))
    }

    await test("progress and ASCII bar") {
        var p = Pomodoro.fresh(.focus)
        p.toggle(at: t0)
        let at = t0.addingTimeInterval(600) // 40 %
        check(abs(p.progress(at: at) - 0.4) < 0.001)
        check(p.asciiBar(at: at), "[####......] 40%")
        check(Pomodoro.fresh(.rest).asciiBar(at: t0), "[..........] 0%")
    }

    await test("clock formatting") {
        check(Pomodoro.clock(0), "00:00")
        check(Pomodoro.clock(65), "01:05")
        check(Pomodoro.clock(1500), "25:00")
    }

    await test("round-trips through UserDefaults") {
        let d = UserDefaults(suiteName: "cn.hola.desktop.tests.\(UUID().uuidString)")!
        var p = Pomodoro.fresh(.rest); p.toggle(at: t0)
        p.save(to: d)
        let back = Pomodoro.load(from: d)
        check(back.mode, .rest)
        check(back.isRunning)
        check(abs(back.endsAt!.timeIntervalSince(p.endsAt!)) < 0.001)
        check(Pomodoro.load(from: UserDefaults(suiteName: "cn.hola.desktop.tests.empty.\(UUID().uuidString)")!), .fresh(.focus))
    }
}
