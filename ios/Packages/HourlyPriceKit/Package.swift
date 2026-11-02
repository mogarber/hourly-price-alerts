// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "HourlyPriceKit",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(name: "HourlyPriceKit", targets: ["HourlyPriceKit"]),
    ],
    targets: [
        .target(name: "HourlyPriceKit"),
        .testTarget(name: "HourlyPriceKitTests", dependencies: ["HourlyPriceKit"]),
    ],
    swiftLanguageModes: [.v5]
)
