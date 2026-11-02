import Foundation
import HourlyPriceKit

/// Hands out the current `BackendClient`: the in-memory mock, the real one, or nil when the build has no
/// API_BASE_URL. The real client is created lazily so the Keychain identity is only made when alerts are used.
@MainActor
final class BackendHolder {
    private let config: AppConfig
    private let identity: IdentityStore
    private var cached: (any BackendClient)?
    private let mock: MockBackendClient?

    init(config: AppConfig, identity: IdentityStore = IdentityStore(), mockSeed: [Rule]? = nil) {
        self.config = config
        self.identity = identity
        self.mock = config.isMockBackend ? MockBackendClient(seed: mockSeed ?? BackendHolder.sampleRules) : nil
    }

    var client: (any BackendClient)? {
        if let mock { return mock }
        if let cached { return cached }
        guard let url = config.apiBaseURL else { return nil }
        let client = HTTPBackendClient(baseURL: url, credentials: identity.loadOrCreate(), version: config.version)
        cached = client
        return client
    }

    /// Drop the identity (after "Delete my data"); a new one is made the next time alerts are used.
    func resetIdentity() {
        identity.reset()
        cached = nil
    }

    static let sampleRules: [Rule] = [
        Rule(kind: .above, signal: .hourAverage, thresholdCents: 10, cooldownMinutes: 60),
        Rule(kind: .move, signal: .fiveMinute, thresholdCents: 6, direction: .either, windowMinutes: 30, cooldownMinutes: 120,
             quietHours: QuietHours(startMinute: 22 * 60, endMinute: 7 * 60), enabled: false),
    ]
}
