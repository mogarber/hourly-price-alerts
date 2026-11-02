import Foundation
import Testing
@testable import HourlyPriceKit

struct RuleDraftTests {
    private func draft(_ build: (inout RuleDraft) -> Void) -> RuleDraft {
        var d = RuleDraft()
        build(&d)
        return d
    }

    @Test func defaultsAreHourlyAboveWithAnHourCooldown() {
        let d = RuleDraft()
        #expect(d.signal == .hourAverage)
        #expect(d.kind == .above)
        #expect(d.cooldownMinutes == 60)
        #expect(!d.quietHoursEnabled)
        #expect(UUID(uuidString: d.id) != nil && d.id == d.id.lowercased())
    }

    @Test func buildsAboveRuleWithNullOptionals() {
        guard case .rule(let rule) = draft({ $0.thresholdText = "10" }).build() else { Issue.record("expected a rule"); return }
        #expect(rule.kind == .above && rule.thresholdCents == 10)
        #expect(rule.direction == nil && rule.windowMinutes == nil && rule.quietHours == nil)
    }

    @Test func staleMoveFieldsAreDroppedWhenKindChanges() {
        let d = draft { $0.thresholdText = "5"; $0.kind = .below; $0.direction = .up; $0.windowMinutes = 90 }
        guard case .rule(let rule) = d.build() else { Issue.record("expected a rule"); return }
        #expect(rule.direction == nil && rule.windowMinutes == nil)
    }

    @Test func moveHourlyHasDirectionButNoWindow() {
        let d = draft { $0.thresholdText = "3"; $0.kind = .move; $0.direction = .down }
        guard case .rule(let rule) = d.build() else { Issue.record("expected a rule"); return }
        #expect(rule.direction == .down && rule.windowMinutes == nil)
    }

    @Test func moveFiveMinuteHasWindow() {
        let d = draft { $0.thresholdText = "6"; $0.kind = .move; $0.signal = .fiveMinute; $0.windowMinutes = 45 }
        guard case .rule(let rule) = d.build() else { Issue.record("expected a rule"); return }
        #expect(rule.windowMinutes == 45 && rule.direction == .either)
    }

    @Test(arguments: [("-1.5", -1.5), ("1,5", 1.5), (" 12 ", 12.0), ("\u{2212}2", -2.0), ("0.1", 0.1)])
    func parsesTypedNumbers(text: String, expected: Double) {
        #expect(RuleDraft.parseThreshold(text) == expected)
    }

    @Test(arguments: ["", "abc", "1.2.3", "nan", "inf"])
    func rejectsNonNumbers(text: String) {
        let d = draft { $0.thresholdText = text }
        guard case .invalid = d.build() else { Issue.record("expected invalid for \(text)"); return }
    }

    @Test func rejectsOutOfRangeAndExtraDecimals() {
        guard case .invalid(let tooHigh) = draft({ $0.thresholdText = "1001" }).build() else { Issue.record("expected invalid"); return }
        #expect(tooHigh.contains("1000.0"))
        guard case .invalid(let decimals) = draft({ $0.thresholdText = "1.25" }).build() else { Issue.record("expected invalid"); return }
        #expect(decimals == "Use at most one decimal place.")
        guard case .invalid = draft({ $0.thresholdText = "0"; $0.kind = .move }).build() else { Issue.record("expected invalid"); return }
    }

    @Test func quietHoursAreIncludedOnlyWhenEnabled() {
        var d = draft { $0.thresholdText = "5"; $0.quietHoursEnabled = true }
        guard case .rule(let withQuiet) = d.build() else { Issue.record("expected a rule"); return }
        #expect(withQuiet.quietHours == QuietHours(startMinute: 1320, endMinute: 420))
        d.quietEndMinute = d.quietStartMinute
        guard case .invalid = d.build() else { Issue.record("expected invalid"); return }
        d.quietHoursEnabled = false
        guard case .rule(let without) = d.build() else { Issue.record("expected a rule"); return }
        #expect(without.quietHours == nil)
    }

    @Test func editingARuleRoundTripsThroughTheDraft() {
        let original = Rule(kind: .move, signal: .fiveMinute, thresholdCents: 6, direction: .up, windowMinutes: 30, cooldownMinutes: 120, quietHours: QuietHours(startMinute: 60, endMinute: 120), enabled: false)
        #expect(RuleDraft(rule: original).build() == .rule(original))
    }
}
