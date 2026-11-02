import Foundation
import HourlyPriceKit

/// What was last successfully PUT to the server for this device.
struct RegistrationRecord: Codable, Equatable {
    var token: String
    var timeZone: String
    var environment: String
}

struct CachedRules: Codable, Equatable {
    var rules: [Rule]
    var savedAt: Date
}

/// Local JSON cache in Application Support: last prices, last known rules, and the registration record.
/// The server stays the source of truth for rules.
struct LocalStore {
    let directory: URL

    var prices: JSONCache<PriceSnapshot> { .init(url: directory.appendingPathComponent("prices.json")) }
    var rules: JSONCache<CachedRules> { .init(url: directory.appendingPathComponent("rules.json")) }
    var registration: JSONCache<RegistrationRecord> { .init(url: directory.appendingPathComponent("registration.json")) }

    static func standard(mock: Bool) -> LocalStore {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        // The mock backend gets its own folder so fake rules never mix with real cached ones.
        return LocalStore(directory: base.appendingPathComponent(mock ? "HourlyPriceAlerts-Mock" : "HourlyPriceAlerts", isDirectory: true))
    }

    static func temporary() -> LocalStore {
        LocalStore(directory: FileManager.default.temporaryDirectory.appendingPathComponent("kw-\(UUID().uuidString)", isDirectory: true))
    }

    func clearAll() {
        prices.remove()
        rules.remove()
        registration.remove()
    }
}
