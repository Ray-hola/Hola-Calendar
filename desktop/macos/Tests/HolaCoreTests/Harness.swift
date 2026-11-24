import Foundation

// A deliberately tiny test harness. This machine's Command Line Tools ship a
// SwiftPM and swift-testing macro plugin that do not match the compiler, so
// the tests are a plain executable built by scripts/build.sh (and `swift run
// HolaCoreTests` when SwiftPM works). No macros, no XCTest.

nonisolated(unsafe) var passed = 0
nonisolated(unsafe) var failed = 0
nonisolated(unsafe) var currentTest = ""

func check(_ cond: Bool, _ msg: String = "", file: String = #fileID, line: Int = #line) {
    if cond { passed += 1 } else {
        failed += 1
        print("  FAIL \(currentTest) \(file):\(line) \(msg)")
    }
}

func check<T: Equatable>(_ a: T, _ b: T, file: String = #fileID, line: Int = #line) {
    check(a == b, "expected \(b), got \(a)", file: file, line: line)
}

func checkThrows<E: Error & Equatable>(_ expected: E, file: String = #fileID, line: Int = #line,
                                       _ body: () async throws -> Void) async {
    do { try await body(); check(false, "expected \(expected) to be thrown", file: file, line: line) }
    catch let e as E { check(e == expected, "expected \(expected), got \(e)", file: file, line: line) }
    catch { check(false, "expected \(expected), got \(error)", file: file, line: line) }
}

func test(_ name: String, _ body: () async throws -> Void) async {
    currentTest = name
    do { try await body() } catch { failed += 1; print("  FAIL \(name): threw \(error)") }
}

@main struct Runner {
    static func main() async {
        await pomodoroTests()
        await todayFilterTests()
        await apiTests()
        await connectTests()
        print("\(passed) checks passed, \(failed) failed")
        exit(failed == 0 ? 0 : 1)
    }
}
