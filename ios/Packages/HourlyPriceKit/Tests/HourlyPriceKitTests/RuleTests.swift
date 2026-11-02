import Foundation
import Testing
@testable import HourlyPriceKit

struct RuleTests {
    static let moveFiveMinuteJSON = """
    {
      "id": "0b9c3f1e-6a8d-4f6e-9a55-1d2c3b4a5f60",
      "kind": "move",
      "signal": "fiveMinute",
      "thresholdCents": 6.0,
      "direction": "either",
      "windowMinutes": 30,
      "cooldownMinutes": 60,
      "quietHours": { "startMinute": 1320, "endMinute": 420 },
      "enabled": true
    }
    """

    static let aboveHourJSON = """
    {
      "id": "7d1f0c52-3b9e-4d3a-8c0b-2e5a6f7b8c91",
      "kind": "above",
      "signal": "hourAverage",
      "thresholdCents": 10.0,
      "direction": null,
      "windowMinutes": null,
      "cooldownMinutes": 60,
      "quietHours": null,
      "enabled": false
    }
    """

    private func jsonObject(_ data: Data) throws -> NSDictionary {
        try #require(JSONSerialization.jsonObject(with: data) as? NSDictionary)
    }

    @Test(arguments: [moveFiveMinuteJSON, aboveHourJSON])
    func roundTripMatchesTheAPIExactly(json: String) throws {
        let original = Data(json.utf8)
        let rule = try JSONDecoder().decode(Rule.self, from: original)
        let encoded = try JSONEncoder().encode(rule)
        #expect(try jsonObject(encoded) == jsonObject(original))
        #expect(try JSONDecoder().decode(Rule.self, from: encoded) == rule)
    }

    @Test func nullFieldsAreEncodedExplicitly() throws {
        let rule = Rule(id: "7d1f0c52-3b9e-4d3a-8c0b-2e5a6f7b8c91", kind: .above, thresholdCents: 10)
        let object = try jsonObject(JSONEncoder().encode(rule))
        #expect(object.count == 9)
        #expect(object["direction"] is NSNull)
        #expect(object["windowMinutes"] is NSNull)
        #expect(object["quietHours"] is NSNull)
        #expect(Set(object.allKeys as! [String]) == ["id", "kind", "signal", "thresholdCents", "direction", "windowMinutes", "cooldownMinutes", "quietHours", "enabled"])
    }

    @Test func decodesFieldsOfTheSpecExample() throws {
        let rule = try JSONDecoder().decode(Rule.self, from: Data(Self.moveFiveMinuteJSON.utf8))
        #expect(rule.kind == .move)
        #expect(rule.signal == .fiveMinute)
        #expect(rule.thresholdCents == 6.0)
        #expect(rule.direction == .either)
        #expect(rule.windowMinutes == 30)
        #expect(rule.quietHours == QuietHours(startMinute: 1320, endMinute: 420))
        #expect(rule.enabled)
    }

    @Test func decodesMissingOptionalKeysAsNil() throws {
        let json = #"{"id":"7d1f0c52-3b9e-4d3a-8c0b-2e5a6f7b8c91","kind":"below","signal":"fiveMinute","thresholdCents":-1,"cooldownMinutes":0,"enabled":true}"#
        let rule = try JSONDecoder().decode(Rule.self, from: Data(json.utf8))
        #expect(rule.direction == nil && rule.windowMinutes == nil && rule.quietHours == nil)
        #expect(rule.thresholdCents == -1)
    }

    @Test func rulesResponseDecodes() throws {
        let json = #"{"rules":[\#(Self.aboveHourJSON),\#(Self.moveFiveMinuteJSON)]}"#
        let response = try JSONDecoder().decode(RulesResponse.self, from: Data(json.utf8))
        #expect(response.rules.count == 2)
        #expect(response.rules[0].enabled == false)
    }

    @Test func newIDsAreLowercaseUUIDs() {
        let id = Rule.newID()
        #expect(UUID(uuidString: id) != nil)
        #expect(id == id.lowercased())
    }

    // MARK: Validation

    private func rule(_ build: (inout Rule) -> Void) -> Rule {
        var r = Rule(kind: .above, thresholdCents: 10)
        build(&r)
        return r
    }

    @Test func validRulesPass() throws {
        #expect(Rule(kind: .above, thresholdCents: 10).isValid)
        #expect(Rule(kind: .below, signal: .fiveMinute, thresholdCents: -100).isValid)
        #expect(Rule(kind: .above, thresholdCents: 1000).isValid)
        #expect(Rule(kind: .move, thresholdCents: 0.1, direction: .up).isValid)
        #expect(Rule(kind: .move, signal: .fiveMinute, thresholdCents: 200, direction: .either, windowMinutes: 180, cooldownMinutes: 1440).isValid)
        #expect(Rule(kind: .above, thresholdCents: 5, quietHours: QuietHours(startMinute: 0, endMinute: 1439)).isValid)
        let decoded = try JSONDecoder().decode(Rule.self, from: Data(Self.moveFiveMinuteJSON.utf8))
        #expect(decoded.isValid)
    }

    @Test func thresholdRanges() {
        #expect(!rule { $0.thresholdCents = -100.1 }.isValid)
        #expect(!rule { $0.thresholdCents = 1000.1 }.isValid)
        #expect(!rule { $0.thresholdCents = 12.34 }.isValid)
        #expect(!rule { $0.thresholdCents = .nan }.isValid)
        #expect(!Rule(kind: .move, thresholdCents: 0, direction: .up).isValid)
        #expect(!Rule(kind: .move, thresholdCents: 200.1, direction: .up).isValid)
        #expect(!Rule(kind: .move, thresholdCents: -1, direction: .up).isValid)
    }

    @Test func directionAndWindowRules() {
        #expect(!Rule(kind: .move, thresholdCents: 3).isValid) // direction required
        #expect(!rule { $0.direction = .up }.isValid) // direction only for move
        #expect(!Rule(kind: .move, signal: .fiveMinute, thresholdCents: 3, direction: .up).isValid) // window required
        #expect(!Rule(kind: .move, signal: .fiveMinute, thresholdCents: 3, direction: .up, windowMinutes: 7).isValid)
        #expect(!Rule(kind: .move, signal: .fiveMinute, thresholdCents: 3, direction: .up, windowMinutes: 185).isValid)
        #expect(!Rule(kind: .move, signal: .fiveMinute, thresholdCents: 3, direction: .up, windowMinutes: 0).isValid)
        #expect(!Rule(kind: .move, signal: .hourAverage, thresholdCents: 3, direction: .up, windowMinutes: 30).isValid)
        #expect(!rule { $0.windowMinutes = 30 }.isValid)
    }

    @Test func cooldownAndQuietHours() {
        #expect(!rule { $0.cooldownMinutes = -1 }.isValid)
        #expect(!rule { $0.cooldownMinutes = 1441 }.isValid)
        #expect(!rule { $0.quietHours = QuietHours(startMinute: 600, endMinute: 600) }.isValid)
        #expect(!rule { $0.quietHours = QuietHours(startMinute: 1440, endMinute: 0) }.isValid)
        #expect(!rule { $0.quietHours = QuietHours(startMinute: -1, endMinute: 0) }.isValid)
    }

    @Test func idMustBeLowercaseUUID() {
        #expect(!rule { $0.id = "not-a-uuid" }.isValid)
        #expect(!rule { $0.id = UUID().uuidString.uppercased() }.isValid)
    }

    @Test func titles() {
        #expect(Rule(kind: .above, thresholdCents: 10).title == "Price goes above 10.0¢")
        #expect(Rule(kind: .below, thresholdCents: -1).title == "Price goes below -1.0¢")
        #expect(Rule(kind: .move, thresholdCents: 3, direction: .either).title == "Price moves by 3.0¢")
        #expect(Rule(kind: .move, thresholdCents: 3, direction: .up).title == "Price rises by 3.0¢")
        #expect(Rule(kind: .move, thresholdCents: 3, direction: .down).title == "Price falls by 3.0¢")
    }
}

struct CredentialsAndCacheTests {
    @Test func generatedCredentialsHaveTheExpectedShape() {
        let a = BackendCredentials.generate()
        let b = BackendCredentials.generate()
        #expect(a.isWellFormed)
        #expect(a.secret.count == 64)
        #expect(a.installId.count == 36)
        #expect(a != b)
        #expect(a.secret != b.secret)
    }

    @Test func malformedCredentialsAreDetected() {
        #expect(!BackendCredentials(installId: "x", secret: String(repeating: "a", count: 64)).isWellFormed)
        #expect(!BackendCredentials(installId: UUID().uuidString.lowercased(), secret: "abc").isWellFormed)
        #expect(!BackendCredentials(installId: UUID().uuidString.lowercased(), secret: String(repeating: "A", count: 64)).isWellFormed)
    }

    @Test func jsonCacheRoundTripsAndRemoves() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: dir) }
        let cache = JSONCache<PriceSnapshot>(url: dir.appendingPathComponent("nested/prices.json"))
        #expect(cache.load() == nil)
        let snapshot = PriceSnapshot(samples: [sample(18, 5, -1.5), sample(18, 10, 2.5)], fetchedAt: utc(18, 11))
        cache.save(snapshot)
        #expect(cache.load() == snapshot)
        cache.remove()
        #expect(cache.load() == nil)
    }

    @Test func corruptCacheLoadsAsNil() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).json")
        defer { try? FileManager.default.removeItem(at: url) }
        try Data("garbage".utf8).write(to: url)
        #expect(JSONCache<PriceSnapshot>(url: url).load() == nil)
    }
}
