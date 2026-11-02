import Foundation
import Testing
@testable import HourlyPriceKit

private let installId = "0b9c3f1e-6a8d-4f6e-9a55-1d2c3b4a5f60"
private let secret = String(repeating: "ab", count: 32)
private let credentials = BackendCredentials(installId: installId, secret: secret)

@Suite(.serialized)
struct BackendClientTests {
    private func client(base: String = "https://kw.example.net/api") -> HTTPBackendClient {
        StubURLProtocol.reset()
        return HTTPBackendClient(baseURL: URL(string: base)!, credentials: credentials, session: StubURLProtocol.session(), version: "1.2.3")
    }

    private var ruleJSON: String { RuleTests.aboveHourJSON }

    @Test func registerDeviceBuildsPUTWithBearerAndBody() async throws {
        let client = client()
        StubURLProtocol.handler = { StubURLProtocol.respond(200, "{}", for: $0) }
        try await client.registerDevice(DeviceRegistration(apnsToken: String(repeating: "a1", count: 32), apnsEnvironment: "sandbox", timeZone: "America/Chicago"))

        let captured = try #require(StubURLProtocol.captured.first)
        #expect(captured.request.httpMethod == "PUT")
        #expect(captured.request.url?.absoluteString == "https://kw.example.net/api/v1/devices/\(installId)")
        #expect(captured.request.value(forHTTPHeaderField: "Authorization") == "Bearer \(secret)")
        #expect(captured.request.value(forHTTPHeaderField: "Content-Type") == "application/json")
        #expect(captured.request.value(forHTTPHeaderField: "User-Agent") == "HourlyPriceAlerts/1.2.3")
        let bodyData = try #require(captured.body)
        let body = try #require(JSONSerialization.jsonObject(with: bodyData) as? [String: String])
        #expect(body == ["apnsToken": String(repeating: "a1", count: 32), "apnsEnvironment": "sandbox", "timeZone": "America/Chicago"])
    }

    @Test func deleteDeviceIsDELETEOnTheDevicePath() async throws {
        let client = client()
        StubURLProtocol.handler = { StubURLProtocol.respond(204, for: $0) }
        try await client.deleteDevice()
        let request = try #require(StubURLProtocol.captured.first?.request)
        #expect(request.httpMethod == "DELETE")
        #expect(request.url?.path == "/api/v1/devices/\(installId)")
        #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer \(secret)")
        #expect(StubURLProtocol.captured.first?.body == nil)
    }

    @Test func fetchRulesGetsAndDecodes() async throws {
        let client = client()
        StubURLProtocol.handler = { StubURLProtocol.respond(200, #"{"rules":[\#(ruleJSON)]}"#, for: $0) }
        let rules = try await client.fetchRules()
        #expect(rules.count == 1)
        #expect(rules[0].thresholdCents == 10)
        let request = try #require(StubURLProtocol.captured.first?.request)
        #expect(request.httpMethod == "GET")
        #expect(request.url?.path == "/api/v1/devices/\(installId)/rules")
        #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer \(secret)")
    }

    @Test func saveRulePutsTheRuleOnItsOwnPathAndReturnsTheServerCopy() async throws {
        let client = client()
        let rule = try JSONDecoder().decode(Rule.self, from: Data(ruleJSON.utf8))
        StubURLProtocol.handler = { StubURLProtocol.respond(200, self.ruleJSON, for: $0) }
        let saved = try await client.saveRule(rule)
        #expect(saved == rule)
        let captured = try #require(StubURLProtocol.captured.first)
        #expect(captured.request.httpMethod == "PUT")
        #expect(captured.request.url?.path == "/api/v1/devices/\(installId)/rules/\(rule.id)")
        let sentData = try #require(captured.body)
        let sent = try JSONSerialization.jsonObject(with: sentData) as? NSDictionary
        let expected = try JSONSerialization.jsonObject(with: Data(ruleJSON.utf8)) as? NSDictionary
        #expect(sent == expected)
    }

    @Test func deleteRuleAndTestNotificationPaths() async throws {
        let client = client()
        StubURLProtocol.handler = { StubURLProtocol.respond($0.httpMethod == "POST" ? 202 : 204, for: $0) }
        try await client.deleteRule(id: "abc")
        try await client.sendTestNotification()
        let requests = StubURLProtocol.captured.map(\.request)
        #expect(requests[0].httpMethod == "DELETE")
        #expect(requests[0].url?.path == "/api/v1/devices/\(installId)/rules/abc")
        #expect(requests[1].httpMethod == "POST")
        #expect(requests[1].url?.path == "/api/v1/devices/\(installId)/test")
        #expect(requests[1].value(forHTTPHeaderField: "Authorization") == "Bearer \(secret)")
    }

    @Test(arguments: [
        (400, #"{"error":"Maximum 10 rules."}"#, BackendError.badRequest("Maximum 10 rules.")),
        (400, "not json", BackendError.badRequest("The alert service rejected the request.")),
        (401, "", BackendError.unauthorized),
        (404, "", BackendError.notFound),
        (429, "", BackendError.rateLimited),
        (500, "", BackendError.server(500)),
        (503, "", BackendError.server(503)),
        (302, "", BackendError.server(302)),
    ])
    func statusMapping(status: Int, body: String, expected: BackendError) async {
        let client = client()
        StubURLProtocol.handler = { StubURLProtocol.respond(status, body, for: $0) }
        await #expect(throws: expected) { try await client.sendTestNotification() }
    }

    @Test func undecodableSuccessBodyIsInvalidResponse() async {
        let client = client()
        StubURLProtocol.handler = { StubURLProtocol.respond(200, "<html>", for: $0) }
        await #expect(throws: BackendError.invalidResponse) { _ = try await client.fetchRules() }
        await #expect(throws: BackendError.invalidResponse) {
            _ = try await client.saveRule(Rule(kind: .above, thresholdCents: 1))
        }
    }

    @Test func transportFailureIsNetworkError() async {
        let client = client()
        StubURLProtocol.handler = { _ in throw URLError(.notConnectedToInternet) }
        do {
            try await client.deleteDevice()
            Issue.record("expected a throw")
        } catch let error as BackendError {
            guard case .network = error else { Issue.record("wrong error \(error)"); return }
        } catch {
            Issue.record("wrong error type \(error)")
        }
    }

    @Test func baseURLHandling() {
        #expect(HTTPBackendClient.normalizedBase(URL(string: "https://kw.example.net")!).absoluteString == "https://kw.example.net/api")
        #expect(HTTPBackendClient.normalizedBase(URL(string: "https://kw.example.net/")!).absoluteString == "https://kw.example.net/api")
        #expect(HTTPBackendClient.normalizedBase(URL(string: "https://kw.example.net/api/")!).absoluteString == "https://kw.example.net/api")
        #expect(HTTPBackendClient.normalizedBase(URL(string: "https://kw.example.net/api")!).absoluteString == "https://kw.example.net/api")
    }

    @Test func requestPathUsesTheBaseWithoutDoubleSlashes() async throws {
        let client = client(base: "https://kw.example.net/api/")
        StubURLProtocol.handler = { StubURLProtocol.respond(204, for: $0) }
        try await client.deleteDevice()
        #expect(StubURLProtocol.captured.first?.request.url?.absoluteString == "https://kw.example.net/api/v1/devices/\(installId)")
    }

    // MARK: ComEdClient (same suite: the stub protocol is shared state, so these must not run in parallel)

    private func comEd() -> ComEdClient {
        StubURLProtocol.reset()
        return ComEdClient(session: StubURLProtocol.session(), version: "1.2.3")
    }

    @Test func fiveMinuteFeedRequestAndParse() async throws {
        let client = comEd()
        StubURLProtocol.handler = { StubURLProtocol.respond(200, FeedParserTests.liveShape, for: $0) }
        let samples = try await client.fetchFiveMinuteFeed()
        #expect(samples.count == 5)
        let request = try #require(StubURLProtocol.captured.first?.request)
        #expect(request.url?.absoluteString == "https://hourlypricing.comed.com/api?type=5minutefeed")
        #expect(request.value(forHTTPHeaderField: "User-Agent") == "HourlyPriceAlerts/1.2.3")
        #expect(request.httpMethod == "GET")
        #expect(request.value(forHTTPHeaderField: "Authorization") == nil)
    }

    @Test func currentHourAverageRequest() async throws {
        let client = comEd()
        StubURLProtocol.handler = { StubURLProtocol.respond(200, #"[{"millisUTC":"1793643300000","price":"1.5"}]"#, for: $0) }
        let sample = try await client.fetchCurrentHourAverage()
        #expect(sample.cents == 1.5)
        #expect(StubURLProtocol.captured.first?.request.url?.absoluteString == "https://hourlypricing.comed.com/api?type=currenthouraverage")
    }

    @Test func errorMapping() async {
        let client = comEd()
        StubURLProtocol.handler = { StubURLProtocol.respond(503, for: $0) }
        await #expect(throws: ComEdError.badStatus(503)) { _ = try await client.fetchFiveMinuteFeed() }
        StubURLProtocol.handler = { StubURLProtocol.respond(200, "[]", for: $0) }
        await #expect(throws: ComEdError.emptyFeed) { _ = try await client.fetchFiveMinuteFeed() }
        StubURLProtocol.handler = { StubURLProtocol.respond(200, "<html>", for: $0) }
        await #expect(throws: ComEdError.malformed) { _ = try await client.fetchFiveMinuteFeed() }
        StubURLProtocol.handler = { StubURLProtocol.respond(200, #"[{"millisUTC":"x","price":"y"}]"#, for: $0) }
        await #expect(throws: ComEdError.emptyFeed) { _ = try await client.fetchFiveMinuteFeed() }
    }
}

struct MockBackendTests {
    @Test func storesRulesAndEnforcesLimitsAndRateLimit() async throws {
        let mock = MockBackendClient(latency: .milliseconds(1))
        let rule = Rule(kind: .above, thresholdCents: 5)
        _ = try await mock.saveRule(rule)
        #expect(try await mock.fetchRules() == [rule])
        await #expect(throws: BackendError.self) { _ = try await mock.saveRule(Rule(kind: .above, thresholdCents: 5000)) }
        for _ in 0..<9 { _ = try await mock.saveRule(Rule(kind: .below, thresholdCents: 1)) }
        await #expect(throws: BackendError.self) { _ = try await mock.saveRule(Rule(kind: .below, thresholdCents: 1)) }
        try await mock.deleteRule(id: rule.id)
        #expect(try await mock.fetchRules().count == 9)
        try await mock.sendTestNotification()
        await #expect(throws: BackendError.rateLimited) { try await mock.sendTestNotification() }
        try await mock.deleteDevice()
        #expect(try await mock.fetchRules().isEmpty)
    }
}
