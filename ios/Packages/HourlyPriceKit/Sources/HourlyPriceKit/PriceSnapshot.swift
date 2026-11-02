import Foundation

/// What the Prices tab shows, and what is cached locally for offline use.
public struct PriceSnapshot: Codable, Hashable, Sendable {
    /// Normalized samples, ascending.
    public let samples: [PriceSample]
    /// When the app fetched them.
    public let fetchedAt: Date

    public init(samples: [PriceSample], fetchedAt: Date) {
        self.samples = PriceSample.normalized(samples)
        self.fetchedAt = fetchedAt
    }

    public var latest: PriceSample? { samples.last }
    public var currentHour: HourAverage? { PriceMath.currentHour(from: samples) }
    public var hourBars: [HourAverage] { PriceMath.chartHours(from: samples) }

    /// The feed is considered delayed when its newest sample is over 20 minutes old (same threshold as the backend).
    public func isDelayed(at now: Date) -> Bool {
        guard let latest else { return true }
        return now.timeIntervalSince(latest.stamp) > 20 * 60
    }
}
