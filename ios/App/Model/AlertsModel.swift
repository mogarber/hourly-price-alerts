import Foundation
import HourlyPriceKit
import Observation

@MainActor
@Observable
final class AlertsModel {
    private(set) var rules: [Rule]
    private(set) var isLoading = false
    /// Set when the server could not be reached; `rules` is then the cached copy.
    private(set) var loadError: String?
    /// A problem with the last toggle or delete, shown in an alert.
    var actionError: String?

    private let store: LocalStore
    private let holder: BackendHolder
    private let registrar: DeviceRegistrar

    init(store: LocalStore, holder: BackendHolder, registrar: DeviceRegistrar) {
        self.store = store
        self.holder = holder
        self.registrar = registrar
        self.rules = store.rules.load()?.rules ?? []
    }

    var canAddMore: Bool { rules.count < Rule.maxRulesPerDevice }

    /// Fetches the server's rules (the source of truth) and updates the local copy.
    func refresh() async {
        guard let backend = holder.client, registrar.isRegistered else { return }
        isLoading = true
        defer { isLoading = false }
        do {
            let fetched = try await backend.fetchRules()
            rules = fetched
            loadError = nil
            persist()
        } catch BackendError.notFound {
            // The server dropped this device (for example after a dead push token). Register again on the next save.
            registrar.forgetRegistration()
            rules = []
            persist()
        } catch {
            if Task.isCancelled { return }
            loadError = (error as? LocalizedError)?.errorDescription ?? "Couldn't load your alerts."
        }
    }

    /// Registers the device if needed, then PUTs the rule. Throws a user-readable error.
    func save(_ rule: Rule) async throws {
        guard let backend = holder.client else { throw BackendError.notConfigured }
        try await registrar.ensureRegistered()
        let saved: Rule
        do {
            saved = try await backend.saveRule(rule)
        } catch BackendError.notFound {
            // The device row is gone on the server. Re-register once and retry.
            try await registrar.ensureRegistered(force: true)
            saved = try await backend.saveRule(rule)
        }
        if let index = rules.firstIndex(where: { $0.id == saved.id }) {
            rules[index] = saved
        } else {
            rules.append(saved)
        }
        loadError = nil
        persist()
    }

    func setEnabled(_ enabled: Bool, for rule: Rule) async {
        var updated = rule
        updated.enabled = enabled
        replaceLocally(updated)
        do {
            try await save(updated)
        } catch {
            replaceLocally(rule)
            actionError = describe(error)
        }
    }

    func delete(_ rule: Rule) async {
        guard let backend = holder.client else { return }
        let backup = rules
        rules.removeAll { $0.id == rule.id }
        do {
            try await backend.deleteRule(id: rule.id)
            persist()
        } catch BackendError.notFound {
            persist() // already gone on the server
        } catch {
            rules = backup
            actionError = describe(error)
        }
    }

    func reset() {
        rules = []
        loadError = nil
        actionError = nil
    }

    func describe(_ error: Error) -> String {
        (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
    }

    private func replaceLocally(_ rule: Rule) {
        if let index = rules.firstIndex(where: { $0.id == rule.id }) { rules[index] = rule }
    }

    private func persist() {
        store.rules.save(CachedRules(rules: rules, savedAt: Date()))
    }
}
