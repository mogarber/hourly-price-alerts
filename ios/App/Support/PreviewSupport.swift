#if DEBUG
import Foundation
import HourlyPriceKit

/// Offline price data and a throwaway model for SwiftUI previews.
struct FixtureFeed: PriceFeed {
    func fetchFiveMinuteFeed() async throws -> [PriceSample] { Self.samples() }
    func fetchCurrentHourAverage() async throws -> PriceSample { Self.samples().last! }

    /// 24 hours of 5-minute samples ending at the latest 5-minute mark, with a daily wave and one negative stretch.
    static func samples(now: Date = Date()) -> [PriceSample] {
        let step: TimeInterval = 300
        let last = (now.timeIntervalSince1970 / step).rounded(.down) * step
        return (0..<288).map { index in
            let t = last - Double(287 - index) * step
            let hourOfDay = (t / 3600).truncatingRemainder(dividingBy: 24)
            var price = 4 + 3.5 * sin((hourOfDay - 9) / 24 * 2 * .pi) + 0.6 * sin(Double(index) * 0.9)
            if index > 40 && index < 60 { price = -1.5 + 0.4 * sin(Double(index)) }
            return PriceSample(stamp: Date(timeIntervalSince1970: t), cents: PriceMath.round1(price))
        }
    }
}

extension AppModel {
    @MainActor
    static func preview(alertsAvailable: Bool = true, loadRules: Bool = false) -> AppModel {
        let config = AppConfig(apiBaseURL: nil, isMockBackend: alertsAvailable, version: "0.1.0", apnsEnvironment: "sandbox")
        let model = AppModel(config: config, feed: FixtureFeed(), store: .temporary())
        Task {
            await model.prices.refresh()
            if loadRules {
                try? await model.registrar.ensureRegistered()
                await model.alerts.refresh()
            }
        }
        return model
    }
}
#endif
