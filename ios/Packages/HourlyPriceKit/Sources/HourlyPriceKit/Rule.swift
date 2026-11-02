import Foundation

public enum RuleKind: String, Codable, CaseIterable, Sendable {
    case above, below, move
}

public enum RuleSignal: String, Codable, CaseIterable, Sendable {
    case hourAverage, fiveMinute
}

public enum RuleDirection: String, Codable, CaseIterable, Sendable {
    case up, down, either
}

/// Minutes after local midnight in the device's time zone. Wraps past midnight when start is after end.
/// Start equal to end means no quiet hours. Start is inclusive, end exclusive.
public struct QuietHours: Codable, Hashable, Sendable {
    public var startMinute: Int
    public var endMinute: Int

    public init(startMinute: Int, endMinute: Int) {
        self.startMinute = startMinute
        self.endMinute = endMinute
    }
}

/// An alert rule, in exactly the shape the API uses. Optional fields are always encoded, as JSON `null` when nil.
public struct Rule: Codable, Hashable, Sendable, Identifiable {
    /// Lowercase UUID; must match the route.
    public var id: String
    public var kind: RuleKind
    public var signal: RuleSignal
    public var thresholdCents: Double
    /// Required for `move`, otherwise nil.
    public var direction: RuleDirection?
    /// Required for `move` with `fiveMinute` (multiple of 5, 5 to 180), otherwise nil.
    public var windowMinutes: Int?
    public var cooldownMinutes: Int
    public var quietHours: QuietHours?
    public var enabled: Bool

    public init(
        id: String = Rule.newID(),
        kind: RuleKind,
        signal: RuleSignal = .hourAverage,
        thresholdCents: Double,
        direction: RuleDirection? = nil,
        windowMinutes: Int? = nil,
        cooldownMinutes: Int = 60,
        quietHours: QuietHours? = nil,
        enabled: Bool = true
    ) {
        self.id = id
        self.kind = kind
        self.signal = signal
        self.thresholdCents = thresholdCents
        self.direction = direction
        self.windowMinutes = windowMinutes
        self.cooldownMinutes = cooldownMinutes
        self.quietHours = quietHours
        self.enabled = enabled
    }

    public static func newID() -> String { UUID().uuidString.lowercased() }

    public static let maxRulesPerDevice = 10

    private enum CodingKeys: String, CodingKey {
        case id, kind, signal, thresholdCents, direction, windowMinutes, cooldownMinutes, quietHours, enabled
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        kind = try c.decode(RuleKind.self, forKey: .kind)
        signal = try c.decode(RuleSignal.self, forKey: .signal)
        thresholdCents = try c.decode(Double.self, forKey: .thresholdCents)
        direction = try c.decodeIfPresent(RuleDirection.self, forKey: .direction)
        windowMinutes = try c.decodeIfPresent(Int.self, forKey: .windowMinutes)
        cooldownMinutes = try c.decode(Int.self, forKey: .cooldownMinutes)
        quietHours = try c.decodeIfPresent(QuietHours.self, forKey: .quietHours)
        enabled = try c.decode(Bool.self, forKey: .enabled)
    }

    /// Synthesized encoding would omit nil fields; the API contract lists them as null, so encode them explicitly.
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(kind, forKey: .kind)
        try c.encode(signal, forKey: .signal)
        try c.encode(thresholdCents, forKey: .thresholdCents)
        try c.encode(direction, forKey: .direction)
        try c.encode(windowMinutes, forKey: .windowMinutes)
        try c.encode(cooldownMinutes, forKey: .cooldownMinutes)
        try c.encode(quietHours, forKey: .quietHours)
        try c.encode(enabled, forKey: .enabled)
    }
}

public struct RulesResponse: Codable, Equatable, Sendable {
    public var rules: [Rule]
    public init(rules: [Rule]) { self.rules = rules }
}

// MARK: - Validation

extension Rule {
    public static func thresholdRange(for kind: RuleKind) -> ClosedRange<Double> {
        kind == .move ? 0.1...200 : -100...1000
    }

    public static let windowRange = 5...180
    public static let cooldownRange = 0...1440

    /// The first problem found, in plain language, or nil when the rule matches the API's validation.
    public var validationMessage: String? {
        if UUID(uuidString: id) == nil || id != id.lowercased() {
            return "The alert id must be a lowercase UUID."
        }
        let range = Rule.thresholdRange(for: kind)
        if !thresholdCents.isFinite || !range.contains(thresholdCents) {
            return "Enter a price from \(PriceFormat.number(range.lowerBound)) to \(PriceFormat.number(range.upperBound)) cents."
        }
        if PriceMath.round1(thresholdCents) != thresholdCents {
            return "Use at most one decimal place."
        }
        if kind == .move {
            if direction == nil { return "Choose a direction." }
        } else if direction != nil {
            return "Direction only applies to \"moves by\" alerts."
        }
        if kind == .move && signal == .fiveMinute {
            guard let window = windowMinutes, Rule.windowRange.contains(window), window % 5 == 0 else {
                return "Choose a window from 5 to 180 minutes, in steps of 5."
            }
        } else if windowMinutes != nil {
            return "A window only applies to 5-minute \"moves by\" alerts."
        }
        if !Rule.cooldownRange.contains(cooldownMinutes) {
            return "The wait between alerts must be 0 to 1440 minutes."
        }
        if let q = quietHours {
            if !(0...1439).contains(q.startMinute) || !(0...1439).contains(q.endMinute) {
                return "Quiet hours must be within one day."
            }
            if q.startMinute == q.endMinute {
                return "Quiet hours need different start and end times."
            }
        }
        return nil
    }

    public var isValid: Bool { validationMessage == nil }

    /// "Price goes above 10.0¢", "Price goes below -1.0¢", "Price moves by 3.0¢".
    public var title: String {
        switch kind {
        case .above: return "Price goes above \(PriceFormat.cents(thresholdCents))"
        case .below: return "Price goes below \(PriceFormat.cents(thresholdCents))"
        case .move:
            switch direction ?? .either {
            case .up: return "Price rises by \(PriceFormat.cents(thresholdCents))"
            case .down: return "Price falls by \(PriceFormat.cents(thresholdCents))"
            case .either: return "Price moves by \(PriceFormat.cents(thresholdCents))"
            }
        }
    }
}
