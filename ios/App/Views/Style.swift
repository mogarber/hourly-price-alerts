import HourlyPriceKit
import SwiftUI

extension Color {
    /// A color that adapts to light and dark mode.
    init(light: (Double, Double, Double), dark: (Double, Double, Double)) {
        self.init(UIColor { traits in
            let c = traits.userInterfaceStyle == .dark ? dark : light
            return UIColor(red: c.0, green: c.1, blue: c.2, alpha: 1)
        })
    }
}

extension PriceBand {
    /// Colors chosen to keep text contrast in both appearances. The band is always also shown as text and a symbol.
    var color: Color {
        switch self {
        case .negative: return Color(light: (0.00, 0.40, 0.75), dark: (0.40, 0.75, 1.00))
        case .low: return Color(light: (0.08, 0.50, 0.22), dark: (0.40, 0.85, 0.50))
        case .moderate: return Color(light: (0.62, 0.42, 0.00), dark: (1.00, 0.82, 0.30))
        case .elevated: return Color(light: (0.80, 0.36, 0.00), dark: (1.00, 0.62, 0.28))
        case .high: return Color(light: (0.78, 0.10, 0.15), dark: (1.00, 0.45, 0.45))
        }
    }

    var symbol: String {
        switch self {
        case .negative: return "arrow.down.circle.fill"
        case .low: return "leaf.fill"
        case .moderate: return "equal.circle.fill"
        case .elevated: return "exclamationmark.triangle.fill"
        case .high: return "flame.fill"
        }
    }
}

/// Band as symbol + text + color, never color alone.
struct BandPill: View {
    let band: PriceBand

    var body: some View {
        Label(band.label, systemImage: band.symbol)
            .font(.headline)
            .foregroundStyle(band.color)
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(band.color.opacity(0.15), in: Capsule())
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Price level: \(band.label)")
    }
}

enum TimeText {
    /// "2:05 PM" for today, "Mon 2:05 PM" for other days.
    static func stamp(_ date: Date, now: Date = Date()) -> String {
        if Calendar.current.isDate(date, inSameDayAs: now) {
            return date.formatted(date: .omitted, time: .shortened)
        }
        return date.formatted(.dateTime.weekday(.abbreviated).hour().minute())
    }

    /// "7 PM" style label for the end of an hour.
    static func hour(_ date: Date) -> String {
        date.formatted(.dateTime.hour())
    }

    /// Minutes after midnight as a clock time in the current locale.
    static func minuteOfDay(_ minute: Int) -> String {
        let date = Calendar.current.date(bySettingHour: minute / 60, minute: minute % 60, second: 0, of: Date()) ?? Date()
        return date.formatted(date: .omitted, time: .shortened)
    }

    static func duration(minutes: Int) -> String {
        if minutes == 0 { return "no wait" }
        if minutes < 60 { return "\(minutes) min" }
        if minutes % 60 == 0 { return minutes == 60 ? "1 hour" : "\(minutes / 60) hours" }
        return "\(minutes / 60) h \(minutes % 60) min"
    }
}
