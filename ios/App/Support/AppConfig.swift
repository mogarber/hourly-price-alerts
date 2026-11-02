import Foundation

/// Build and launch configuration.
struct AppConfig {
    /// From API_BASE_URL via Info.plist. Nil when empty or not a usable http(s) URL.
    var apiBaseURL: URL?
    /// `-MockBackend` launch argument: in-memory backend, no permission flow.
    var isMockBackend: Bool
    var version: String
    /// `sandbox` for Debug builds, `production` for Release builds.
    var apnsEnvironment: String

    /// Alerts need either a real backend URL or the mock backend.
    var alertsAvailable: Bool { isMockBackend || apiBaseURL != nil }

    static let sourceCodeURL = URL(string: "https://github.com/mogarber/hourly-price-alerts")!

    static func load(bundle: Bundle = .main, arguments: [String] = ProcessInfo.processInfo.arguments) -> AppConfig {
        let raw = (bundle.object(forInfoDictionaryKey: "HPAApiBaseURL") as? String)?
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        var url: URL?
        if let candidate = URL(string: raw), ["https", "http"].contains(candidate.scheme?.lowercased()), candidate.host != nil {
            url = candidate
        }
        #if DEBUG
        let environment = "sandbox"
        #else
        let environment = "production"
        #endif
        return AppConfig(
            apiBaseURL: url,
            isMockBackend: arguments.contains("-MockBackend"),
            version: (bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "0",
            apnsEnvironment: environment
        )
    }
}
