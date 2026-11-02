import Foundation
import Testing
@testable import HourlyPriceKit

struct PriceMathTests {
    // MARK: Rounding

    @Test(arguments: [
        (0.25, 0.3), (-0.25, -0.3), (0.35, 0.4), (-0.35, -0.4), (0.05, 0.1), (-0.05, -0.1),
        (1.45, 1.5), (0.15, 0.2), (-0.15, -0.2), (0.14, 0.1), (-0.14, -0.1), (0.0, 0.0), (10.0, 10.0),
        (2.449999, 2.4), (12.35, 12.4),
    ])
    func roundsMidpointAwayFromZero(input: Double, expected: Double) {
        #expect(PriceMath.round1(input) == expected)
    }

    @Test func tinyNegativeRoundsToPositiveZero() {
        let result = PriceMath.round1(-0.04)
        #expect(result == 0)
        #expect(result.sign == .plus)
        #expect(PriceFormat.number(-0.04) == "0.0")
    }

    @Test func meanOfTwoTenthsMidpointRoundsUp() {
        // (0.1 + 0.2) / 2 is 0.15000000000000002 in floating point and must round like the exact decimal.
        #expect(PriceMath.mean([0.1, 0.2]) == 0.2)
        #expect(PriceMath.mean([-0.1, -0.2]) == -0.2)
        #expect(PriceMath.mean([]) == nil)
    }

    // MARK: Hour buckets

    @Test func stampOnTheHourClosesThePreviousHour() {
        #expect(PriceMath.hourEnd(for: utc(18, 0)) == utc(18, 0))
    }

    @Test func stampPastTheHourBelongsToTheNextHour() {
        #expect(PriceMath.hourEnd(for: utc(18, 5)) == utc(19, 0))
        #expect(PriceMath.hourEnd(for: utc(18, 10)) == utc(19, 0))
        #expect(PriceMath.hourEnd(for: utc(18, 55)) == utc(19, 0))
        #expect(PriceMath.hourEnd(for: utc(23, 5)) == utc(0, 0, day: 3))
    }

    /// Verified against the live feed: stamp 18:00 returned 0.1 while the mean of the 12 samples 17:05 to 18:00 was 0.14.
    @Test func stamp1800BelongsToTheHourEnding1800() {
        // 17:05 ... 18:00. Means to 0.14 -> 0.1. Values chosen so a 17:00-17:55 grouping would give a different answer.
        let hour: [Double] = [0.1, 0.1, 0.2, 0.2, 0.1, 0.1, 0.2, 0.2, 0.1, 0.1, 0.1, 0.2] // sum 1.7 -> mean 0.1417
        var samples: [PriceSample] = []
        for (index, value) in hour.enumerated() {
            let minutes = 17 * 60 + 5 + index * 5
            samples.append(sample(minutes / 60, minutes % 60, value))
        }
        samples.append(sample(16, 55, 9.0)) // belongs to the hour ending 17:00 and must not leak in
        #expect(samples.dropLast().last?.stamp == utc(18, 0))

        let hours = PriceMath.hourAverages(from: samples)
        #expect(hours.count == 2)
        #expect(hours[0].end == utc(17, 0))
        #expect(hours[0].average == 9.0)
        #expect(hours[1].end == utc(18, 0))
        #expect(hours[1].count == 12)
        #expect(hours[1].average == 0.1)
        #expect(hours[1].isInProgress == false)

        let current = PriceMath.currentHour(from: samples)
        #expect(current?.end == utc(18, 0))
        #expect(current?.average == 0.1)
    }

    /// Verified against the live feed: stamp 18:10 returned 1.4, the mean of 18:05 and 18:10 only.
    @Test func stamp1810AveragesOnly1805And1810() {
        let samples = [
            sample(17, 55, 0.5), sample(18, 0, 0.1), // previous hour
            sample(18, 5, 1.3), sample(18, 10, 1.5),
        ]
        let current = PriceMath.currentHour(from: samples)
        #expect(current?.end == utc(19, 0))
        #expect(current?.count == 2)
        #expect(current?.average == 1.4)
        #expect(current?.isInProgress == true)

        let hours = PriceMath.hourAverages(from: samples)
        #expect(hours.map(\.end) == [utc(18, 0), utc(19, 0)])
        #expect(hours[0].count == 2)
        #expect(hours[0].average == 0.3)
    }

    @Test func orderAndDuplicatesDoNotChangeTheResult() {
        let ascending = [sample(18, 5, 1.0), sample(18, 10, 2.0), sample(18, 15, 3.0)]
        let shuffled = [ascending[2], ascending[0], ascending[1], sample(18, 10, 99), ascending[0]]
        #expect(PriceMath.hourAverages(from: shuffled) == PriceMath.hourAverages(from: ascending))
        #expect(PriceMath.currentHour(from: shuffled)?.average == 2.0)
    }

    @Test func gapsJustProduceFewerSamples() {
        let samples = [sample(18, 5, 2.0), sample(18, 40, 4.0)]
        let current = PriceMath.currentHour(from: samples)
        #expect(current?.count == 2)
        #expect(current?.average == 3.0)
    }

    @Test func negativePricesAverageCorrectly() {
        let samples = [sample(18, 5, -2.0), sample(18, 10, 1.0)]
        #expect(PriceMath.currentHour(from: samples)?.average == -0.5)
    }

    @Test func emptyInputHasNoCurrentHour() {
        #expect(PriceMath.currentHour(from: []) == nil)
        #expect(PriceMath.hourAverages(from: []).isEmpty)
    }

    @Test func chartDropsATruncatedOldestBucketAndCapsAt24() {
        var samples: [PriceSample] = []
        // 26 hours of samples starting at 3 minutes into... start with a partial first bucket (only 4 samples).
        var stamp = utc(0, 35, day: 1)
        let end = utc(2, 0, day: 2)
        while stamp <= end {
            samples.append(PriceSample(stamp: stamp, cents: 1.0))
            stamp = stamp.addingTimeInterval(300)
        }
        let all = PriceMath.hourAverages(from: samples)
        #expect(all.first?.count == 6) // 00:35 ... 01:00; the 01:00 stamp closes the hour ending 01:00
        let bars = PriceMath.chartHours(from: samples)
        #expect(bars.count == 24)
        #expect(bars.first?.count == 12)
        #expect(bars.last?.end == utc(2, 0, day: 2))
    }

    @Test func chartKeepsTheOnlyBucketEvenIfPartial() {
        let bars = PriceMath.chartHours(from: [sample(18, 5, 1.0)])
        #expect(bars.count == 1)
    }

    @Test func snapshotDelayedAfter20Minutes() {
        let snapshot = PriceSnapshot(samples: [sample(18, 0, 1.0)], fetchedAt: utc(18, 1))
        #expect(!snapshot.isDelayed(at: utc(18, 20)))
        #expect(snapshot.isDelayed(at: utc(18, 21)))
        #expect(PriceSnapshot(samples: [], fetchedAt: utc(18, 1)).isDelayed(at: utc(18, 1)))
    }
}

struct PriceBandTests {
    @Test(arguments: [
        (-100.0, PriceBand.negative), (-0.1, .negative), (-0.04, .low), (0.0, .low), (2.9, .low), (2.94, .low),
        (2.95, .moderate), (3.0, .moderate), (6.9, .moderate), (7.0, .elevated), (13.9, .elevated),
        (14.0, .high), (13.95, .high), (250.0, .high),
    ])
    func bandsAtEachBoundary(cents: Double, expected: PriceBand) {
        #expect(PriceBand(cents: cents) == expected)
    }

    @Test func labels() {
        #expect(PriceBand.allCases.map(\.label) == ["Negative", "Low", "Moderate", "Elevated", "High"])
    }
}

struct PriceFormatTests {
    @Test func formatting() {
        #expect(PriceFormat.cents(1.4) == "1.4¢")
        #expect(PriceFormat.cents(-1.4) == "-1.4¢")
        #expect(PriceFormat.cents(12) == "12.0¢")
        #expect(PriceFormat.cents(0.25) == "0.3¢")
        #expect(PriceFormat.cents(-0.25) == "-0.3¢")
        #expect(PriceFormat.number(1234.56) == "1234.6")
    }

    @Test func spoken() {
        #expect(PriceFormat.spoken(-1.4) == "minus 1.4 cents per kilowatt hour")
        #expect(PriceFormat.spoken(2) == "2.0 cents per kilowatt hour")
    }
}
