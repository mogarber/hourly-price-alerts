import Foundation

/// The editable state behind the add/edit alert form, with the logic that turns it into a valid `Rule`.
public struct RuleDraft: Equatable, Sendable {
    public var id: String
    public var kind: RuleKind
    public var signal: RuleSignal
    /// What the user typed. Accepts "," as the decimal separator and the Unicode minus sign.
    public var thresholdText: String
    /// Only used when `kind == .move`.
    public var direction: RuleDirection
    /// Only used when `kind == .move` and `signal == .fiveMinute`.
    public var windowMinutes: Int
    public var cooldownMinutes: Int
    public var quietHoursEnabled: Bool
    public var quietStartMinute: Int
    public var quietEndMinute: Int
    public var enabled: Bool

    /// A new draft with the defaults: hourly average, wait 60 minutes between alerts, quiet hours off.
    public init() {
        id = Rule.newID()
        kind = .above
        signal = .hourAverage
        thresholdText = ""
        direction = .either
        windowMinutes = 30
        cooldownMinutes = 60
        quietHoursEnabled = false
        quietStartMinute = 22 * 60
        quietEndMinute = 7 * 60
        enabled = true
    }

    public init(rule: Rule) {
        id = rule.id
        kind = rule.kind
        signal = rule.signal
        thresholdText = PriceFormat.number(rule.thresholdCents)
        direction = rule.direction ?? .either
        windowMinutes = rule.windowMinutes ?? 30
        cooldownMinutes = rule.cooldownMinutes
        quietHoursEnabled = rule.quietHours != nil
        quietStartMinute = rule.quietHours?.startMinute ?? 22 * 60
        quietEndMinute = rule.quietHours?.endMinute ?? 7 * 60
        enabled = rule.enabled
    }

    public enum Outcome: Equatable, Sendable {
        case rule(Rule)
        case invalid(String)
    }

    public var usesWindow: Bool { kind == .move && signal == .fiveMinute }
    public var usesDirection: Bool { kind == .move }

    /// Parses the typed threshold; nil when it is not a number.
    public static func parseThreshold(_ text: String) -> Double? {
        let cleaned = text
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: ",", with: ".")
            .replacingOccurrences(of: "\u{2212}", with: "-")
        guard !cleaned.isEmpty, let value = Double(cleaned), value.isFinite else { return nil }
        return value
    }

    public func build() -> Outcome {
        let trimmed = thresholdText.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return .invalid("Enter a price.") }
        guard let threshold = RuleDraft.parseThreshold(trimmed) else {
            return .invalid("Enter a number, like 10 or -1.5.")
        }
        let rule = Rule(
            id: id,
            kind: kind,
            signal: signal,
            thresholdCents: threshold,
            direction: usesDirection ? direction : nil,
            windowMinutes: usesWindow ? windowMinutes : nil,
            cooldownMinutes: cooldownMinutes,
            quietHours: quietHoursEnabled ? QuietHours(startMinute: quietStartMinute, endMinute: quietEndMinute) : nil,
            enabled: enabled
        )
        if let message = rule.validationMessage { return .invalid(message) }
        return .rule(rule)
    }
}
