import Foundation
import HourlyPriceKit
import Observation

@MainActor
@Observable
final class AppModel {
    let config: AppConfig
    let prices: PricesModel
    let alerts: AlertsModel
    let registrar: DeviceRegistrar

    private let store: LocalStore
    private let holder: BackendHolder

    init(config: AppConfig, feed: (any PriceFeed)? = nil, store: LocalStore? = nil, holder: BackendHolder? = nil) {
        let store = store ?? LocalStore.standard(mock: config.isMockBackend)
        let holder = holder ?? BackendHolder(config: config)
        let registrar = DeviceRegistrar(config: config, store: store, holder: holder)
        self.config = config
        self.store = store
        self.holder = holder
        self.registrar = registrar
        self.prices = PricesModel(feed: feed ?? ComEdClient(version: config.version), store: store)
        self.alerts = AlertsModel(store: store, holder: holder, registrar: registrar)
    }

    /// The app became active: refresh prices, notification state, device registration and rules.
    func sceneBecameActive() async {
        async let pricesDone: Void = prices.refresh()
        await registrar.refreshAuthorization()
        await registrar.syncIfNeeded()
        await alerts.refresh()
        await pricesDone
    }

    func sendTestNotification() async throws {
        guard let backend = holder.client else { throw BackendError.notConfigured }
        try await registrar.ensureRegistered()
        do {
            try await backend.sendTestNotification()
        } catch BackendError.notFound {
            try await registrar.ensureRegistered(force: true)
            try await backend.sendTestNotification()
        }
    }

    /// Deletes the device and its rules on the server, then clears everything stored locally.
    /// If the server cannot be reached, nothing is cleared and the error is thrown so the user can retry.
    func deleteMyData() async throws {
        if registrar.isRegistered, let backend = holder.client {
            do {
                try await backend.deleteDevice()
            } catch BackendError.notFound {
                // Already gone.
            }
        }
        store.clearAll()
        holder.resetIdentity()
        registrar.forgetRegistration()
        alerts.reset()
        prices.reset()
        await prices.refresh()
    }
}
