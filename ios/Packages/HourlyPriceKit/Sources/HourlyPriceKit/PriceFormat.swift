import Foundation

/// Number formatting that never depends on the user's locale: "." decimal separator, ASCII minus,
/// 1 decimal, rounded midpoint away from zero before display.
public enum PriceFormat {
    /// "1.4", "-1.4", "0.0" (never "-0.0").
    public static func number(_ cents: Double) -> String {
        let rounded = PriceMath.round1(cents)
        guard rounded.isFinite else { return "--" }
        return String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), rounded)
    }

    /// "1.4¢", "-1.4¢".
    public static func cents(_ value: Double) -> String {
        number(value) + "¢"
    }

    /// For VoiceOver: "minus 1.4 cents per kilowatt hour".
    public static func spoken(_ value: Double) -> String {
        let rounded = PriceMath.round1(value)
        let text = number(abs(rounded))
        return (rounded < 0 ? "minus " : "") + text + " cents per kilowatt hour"
    }
}
