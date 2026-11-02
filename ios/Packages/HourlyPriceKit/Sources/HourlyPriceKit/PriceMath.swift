import Foundation

/// The mean of one hour bucket. The bucket for hour ending `end` holds samples stamped in `(end - 60 min, end]`.
public struct HourAverage: Hashable, Sendable, Identifiable, Codable {
    /// The end of the hour (a whole UTC hour).
    public let end: Date
    /// Mean of the samples, already rounded to 1 decimal (midpoint away from zero).
    public let average: Double
    /// How many 5-minute samples are in the bucket (12 when complete).
    public let count: Int
    /// True while the latest sample is earlier than `end`, so more samples are still to come.
    public let isInProgress: Bool

    public var id: Date { end }
    public var start: Date { end.addingTimeInterval(-PriceMath.hour) }

    public init(end: Date, average: Double, count: Int, isInProgress: Bool) {
        self.end = end
        self.average = average
        self.count = count
        self.isInProgress = isInProgress
    }
}

public enum PriceMath {
    public static let hour: TimeInterval = 3600
    /// A complete bucket holds 12 five-minute samples.
    public static let samplesPerHour = 12

    /// Rounds to 1 decimal, midpoint away from zero (0.25 -> 0.3, -0.25 -> -0.3).
    /// Values within floating point noise of a midpoint are treated as the midpoint, so a mean such as
    /// (0.1 + 0.2) / 2 rounds to 0.2 like the exact decimal does.
    public static func round1(_ value: Double) -> Double {
        guard value.isFinite else { return value }
        let scaled = value * 10
        let cleaned = (scaled * 1e6).rounded() / 1e6
        let rounded = cleaned.rounded(.toNearestOrAwayFromZero) / 10
        return rounded == 0 ? 0 : rounded // folds -0.0 into 0.0
    }

    /// Mean of the values rounded to 1 decimal, or nil when there are none.
    public static func mean(_ values: [Double]) -> Double? {
        guard !values.isEmpty else { return nil }
        return round1(values.reduce(0, +) / Double(values.count))
    }

    /// The end of the hour bucket a sample stamped `stamp` belongs to: the stamp rounded up to the next
    /// whole UTC hour, or the stamp itself when already on the hour. A stamp of exactly 18:00 closes the
    /// hour ending 18:00.
    public static func hourEnd(for stamp: Date) -> Date {
        let seconds = stamp.timeIntervalSince1970
        return Date(timeIntervalSince1970: (seconds / hour).rounded(.up) * hour)
    }

    /// All hour buckets, ascending by hour end. Input may be in any order and may contain duplicates.
    public static func hourAverages(from samples: [PriceSample]) -> [HourAverage] {
        let ordered = PriceSample.normalized(samples)
        guard let latest = ordered.last?.stamp else { return [] }
        var groups: [Date: [Double]] = [:]
        for sample in ordered {
            groups[hourEnd(for: sample.stamp), default: []].append(sample.cents)
        }
        return groups.keys.sorted().compactMap { end in
            guard let values = groups[end], let average = mean(values) else { return nil }
            return HourAverage(end: end, average: average, count: values.count, isInProgress: end > latest)
        }
    }

    /// The bucket that the latest sample belongs to. This is the "current hour average" the app shows.
    public static func currentHour(from samples: [PriceSample]) -> HourAverage? {
        hourAverages(from: samples).last
    }

    /// Hourly bars for the chart: at most `limit` buckets. The oldest bucket is dropped when it holds fewer
    /// than 12 samples, because the feed window cut it short and its mean would mislead.
    public static func chartHours(from samples: [PriceSample], limit: Int = 24) -> [HourAverage] {
        var hours = hourAverages(from: samples)
        if hours.count > 1, let first = hours.first, first.count < samplesPerHour {
            hours.removeFirst()
        }
        return Array(hours.suffix(limit))
    }
}
