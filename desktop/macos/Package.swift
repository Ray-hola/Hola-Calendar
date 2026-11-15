// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "HolaDesktop",
    defaultLocalization: "zh-Hans",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "HolaDesktop", targets: ["HolaDesktop"]),
        .executable(name: "holactl", targets: ["holactl"]),
        .library(name: "HolaCore", targets: ["HolaCore"]),
    ],
    targets: [
        // Pure Foundation: models, API client, Keychain, pomodoro engine, today filter.
        // Shares with a future iOS Live Activity app unchanged.
        .target(name: "HolaCore", swiftSettings: [.swiftLanguageMode(.v5)]),
        // The menu-bar app: AppKit + SwiftUI, Touch Bar, notch island.
        .executableTarget(name: "HolaDesktop", dependencies: ["HolaCore"],
                          swiftSettings: [.swiftLanguageMode(.v5)]),
        // A tiny command-line client used by the integration test and for debugging a server.
        .executableTarget(name: "holactl", dependencies: ["HolaCore"],
                          swiftSettings: [.swiftLanguageMode(.v5)]),
        // Tests are a plain executable with a tiny harness (see Tests/HolaCoreTests/Harness.swift):
        // `swift run HolaCoreTests` here, or `scripts/build.sh test` without SwiftPM.
        .executableTarget(name: "HolaCoreTests", dependencies: ["HolaCore"], path: "Tests/HolaCoreTests",
                          swiftSettings: [.swiftLanguageMode(.v5)]),
    ]
)
