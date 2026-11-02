import Foundation
import HourlyPriceKit
import Observation
import UIKit
import UserNotifications

enum RegistrationError: LocalizedError {
    case notificationsDenied
    case noToken(String)

    var errorDescription: String? {
        switch self {
        case .notificationsDenied:
            return "Notifications are turned off for Hourly Price Alerts. Turn them on in Settings to receive alerts."
        case .noToken(let detail):
            return "Couldn't get a notification token from Apple, so alerts can't be delivered to this iPhone. Check your connection and try again. (\(detail))"
        }
    }
}

/// Owns notification permission and the device registration with the backend.
@MainActor
@Observable
final class DeviceRegistrar {
    enum Authorization: Equatable {
        case unknown, notDetermined, denied, authorized
    }

    private(set) var authorization: Authorization = .unknown
    /// A device row has been PUT to the server for this install.
    private(set) var isRegistered: Bool

    private let config: AppConfig
    private let store: LocalStore
    private let holder: BackendHolder
    private let tokens: PushTokenCenter

    init(config: AppConfig, store: LocalStore, holder: BackendHolder, tokens: PushTokenCenter? = nil) {
        self.config = config
        self.store = store
        self.holder = holder
        self.tokens = tokens ?? .shared
        self.isRegistered = store.registration.load() != nil
        if config.isMockBackend { authorization = .authorized; isRegistered = true }
    }

    func refreshAuthorization() async {
        if config.isMockBackend { authorization = .authorized; return }
        let settings = await UNUserNotificationCenter.current().notificationSettings()
        switch settings.authorizationStatus {
        case .notDetermined: authorization = .notDetermined
        case .denied: authorization = .denied
        default: authorization = .authorized
        }
    }

    /// Shows the system permission prompt. Returns true when notifications are allowed.
    func requestPermission() async -> Bool {
        if config.isMockBackend { return true }
        _ = try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound])
        await refreshAuthorization()
        return authorization == .authorized
    }

    /// Makes sure the server has this device with its current token and time zone.
    /// Requests permission first if it has not been asked yet.
    func ensureRegistered(force: Bool = false) async throws {
        guard let backend = holder.client else { throw BackendError.notConfigured }
        if config.isMockBackend {
            try await backend.registerDevice(DeviceRegistration(apnsToken: String(repeating: "0", count: 64), apnsEnvironment: config.apnsEnvironment, timeZone: TimeZone.current.identifier))
            isRegistered = true
            return
        }
        await refreshAuthorization()
        if authorization == .notDetermined { _ = await requestPermission() }
        guard authorization == .authorized else { throw RegistrationError.notificationsDenied }
        // Already registered: a changed token or time zone is picked up by syncIfNeeded() on launch.
        if isRegistered && !force { return }

        let token: String
        do {
            token = try await tokens.requestToken()
        } catch {
            throw RegistrationError.noToken(error.localizedDescription)
        }
        try await put(token: token, force: force)
    }

    /// On launch and when the app becomes active: re-PUT the device if the token or time zone changed
    /// since the last successful PUT. Does nothing for installs that never registered.
    func syncIfNeeded() async {
        guard !config.isMockBackend, store.registration.load() != nil, holder.client != nil else { return }
        await refreshAuthorization()
        guard authorization == .authorized else { return }
        let record = store.registration.load()
        let timeZone = TimeZone.current.identifier
        if let token = try? await tokens.requestToken(timeout: .seconds(10)) {
            try? await put(token: token, force: false)
        } else if let record, record.timeZone != timeZone {
            try? await put(token: record.token, force: false)
        }
    }

    private func put(token: String, force: Bool) async throws {
        guard let backend = holder.client else { throw BackendError.notConfigured }
        let registration = DeviceRegistration(
            apnsToken: token,
            apnsEnvironment: config.apnsEnvironment,
            timeZone: TimeZone.current.identifier
        )
        let record = RegistrationRecord(token: registration.apnsToken, timeZone: registration.timeZone, environment: registration.apnsEnvironment)
        if !force, store.registration.load() == record { isRegistered = true; return }
        try await backend.registerDevice(registration)
        store.registration.save(record)
        isRegistered = true
    }

    /// Forget the local registration (after the server lost the device, or after "Delete my data").
    func forgetRegistration() {
        store.registration.remove()
        isRegistered = false
    }
}
