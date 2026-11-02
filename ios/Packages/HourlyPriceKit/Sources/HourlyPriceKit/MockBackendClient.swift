import Foundation

/// An in-memory backend for the simulator (`-MockBackend`) and for previews. It enforces the same
/// validation and the 10 rule limit as the real API.
public actor MockBackendClient: BackendClient {
    private var rules: [String: Rule]
    private var order: [String]
    private var registered = false
    private var lastTestAt: Date?
    private let latency: Duration

    public init(seed: [Rule] = [], latency: Duration = .milliseconds(250)) {
        self.rules = Dictionary(uniqueKeysWithValues: seed.map { ($0.id, $0) })
        self.order = seed.map(\.id)
        self.latency = latency
    }

    public func registerDevice(_ registration: DeviceRegistration) async throws {
        try await Task.sleep(for: latency)
        registered = true
    }

    public func deleteDevice() async throws {
        try await Task.sleep(for: latency)
        registered = false
        rules = [:]
        order = []
    }

    public func fetchRules() async throws -> [Rule] {
        try await Task.sleep(for: latency)
        return order.compactMap { rules[$0] }
    }

    public func saveRule(_ rule: Rule) async throws -> Rule {
        try await Task.sleep(for: latency)
        if let message = rule.validationMessage { throw BackendError.badRequest(message) }
        if rules[rule.id] == nil {
            guard rules.count < Rule.maxRulesPerDevice else {
                throw BackendError.badRequest("You can have at most \(Rule.maxRulesPerDevice) alerts.")
            }
            order.append(rule.id)
        }
        rules[rule.id] = rule
        return rule
    }

    public func deleteRule(id: String) async throws {
        try await Task.sleep(for: latency)
        rules[id] = nil
        order.removeAll { $0 == id }
    }

    public func sendTestNotification() async throws {
        try await Task.sleep(for: latency)
        if let last = lastTestAt, Date().timeIntervalSince(last) < 60 { throw BackendError.rateLimited }
        lastTestAt = Date()
    }
}
