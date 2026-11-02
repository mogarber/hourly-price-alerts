import Foundation
import HourlyPriceKit
import Security

/// Stores the install id and secret in the Keychain. They never leave the device except as the bearer
/// secret on API calls, and they are not synced to iCloud.
struct IdentityStore {
    private let service: String

    init(service: String = Bundle.main.bundleIdentifier ?? "io.github.mogarber.hourlypricealerts") {
        self.service = service
    }

    func loadOrCreate() -> BackendCredentials {
        if let installId = read("installId"), let secret = read("secret") {
            let existing = BackendCredentials(installId: installId, secret: secret)
            if existing.isWellFormed { return existing }
        }
        let fresh = BackendCredentials.generate()
        write("installId", fresh.installId)
        write("secret", fresh.secret)
        return fresh
    }

    func reset() {
        delete("installId")
        delete("secret")
    }

    private func query(_ account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }

    private func read(_ account: String) -> String? {
        var query = query(account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    private func write(_ account: String, _ value: String) {
        delete(account)
        var attributes = query(account)
        attributes[kSecValueData as String] = Data(value.utf8)
        attributes[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        SecItemAdd(attributes as CFDictionary, nil)
    }

    private func delete(_ account: String) {
        SecItemDelete(query(account) as CFDictionary)
    }
}
