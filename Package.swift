// swift-tools-version:5.10
import PackageDescription

let package = Package(
    name: "MYUStats",
    platforms: [.macOS(.v14)],
    targets: [
        .target(
            name: "SMCKit",
            path: "Sources/SMCKit",
            linkerSettings: [.linkedFramework("IOKit")]
        ),
        .executableTarget(
            name: "MYUStats",
            dependencies: ["SMCKit"],
            path: "Sources/MYUStats",
            linkerSettings: [.linkedFramework("IOKit")]
        ),
        // Privileged helper bundled next to the app binary; see Sources/myustats-fan/main.swift.
        .executableTarget(
            name: "myustats-fan",
            dependencies: ["SMCKit"],
            path: "Sources/myustats-fan"
        ),
        .testTarget(
            name: "MYUStatsTests",
            dependencies: ["MYUStats", "SMCKit"],
            path: "Tests/MYUStatsTests"
        ),
    ]
)
