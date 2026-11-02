import Foundation

/// One 5-minute reading from the ComEd feed. `cents` is the price in cents per kWh and can be negative.
public struct PriceSample: Codable, Hashable, Sendable, Identifiable {
    public let stamp: Date
    public let cents: Double

    public var id: Date { stamp }

    public init(stamp: Date, cents: Double) {
        self.stamp = stamp
        self.cents = cents
    }

    /// Sorts ascending by stamp and removes duplicate stamps, keeping the first one seen for each stamp.
    /// The feed is untrusted, so every consumer goes through this before doing any math.
    public static func normalized(_ samples: [PriceSample]) -> [PriceSample] {
        let sorted = samples.enumerated().sorted { lhs, rhs in
            if lhs.element.stamp != rhs.element.stamp { return lhs.element.stamp < rhs.element.stamp }
            return lhs.offset < rhs.offset
        }
        var result: [PriceSample] = []
        result.reserveCapacity(sorted.count)
        for item in sorted where result.last?.stamp != item.element.stamp {
            result.append(item.element)
        }
        return result
    }
}
