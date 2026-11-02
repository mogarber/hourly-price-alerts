import Foundation

/// Relative price labels. Defaults: below 0 Negative, below 3 Low, below 7 Moderate, below 14 Elevated,
/// otherwise High. The value is rounded to 1 decimal first, so the band always agrees with the number shown.
public enum PriceBand: String, CaseIterable, Sendable, Codable {
    case negative, low, moderate, elevated, high

    public init(cents: Double) {
        let value = PriceMath.round1(cents)
        switch value {
        case ..<0: self = .negative
        case ..<3: self = .low
        case ..<7: self = .moderate
        case ..<14: self = .elevated
        default: self = .high
        }
    }

    public var label: String {
        switch self {
        case .negative: return "Negative"
        case .low: return "Low"
        case .moderate: return "Moderate"
        case .elevated: return "Elevated"
        case .high: return "High"
        }
    }
}
