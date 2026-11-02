import Foundation
import Testing
@testable import HourlyPriceKit

struct FeedParserTests {
    /// The shape returned by the live feed on 2026-11-02: newest first, both fields are strings.
    static let liveShape = """
    [{"millisUTC":"1793643300000","price":"1.7"},{"millisUTC":"1793643000000","price":"1.4"},\
    {"millisUTC":"1793642700000","price":"1.4"},{"millisUTC":"1793642400000","price":"1.4"},\
    {"millisUTC":"1793640900000","price":"-2.0"}]
    """

    @Test func parsesStringFieldsAndNegativePrices() throws {
        let samples = try FeedParser.parse(Data(Self.liveShape.utf8))
        #expect(samples.count == 5)
        #expect(samples.last?.cents == 1.7)
        #expect(samples.first?.cents == -2.0)
        #expect(samples.last?.stamp == Date(timeIntervalSince1970: 1_793_643_300))
    }

    @Test func sortsAscendingFromNewestFirst() throws {
        let samples = try FeedParser.parse(Data(Self.liveShape.utf8))
        #expect(samples.map(\.stamp) == samples.map(\.stamp).sorted())
        #expect(samples.first?.stamp == Date(timeIntervalSince1970: 1_793_640_900))
    }

    @Test func toleratesAnyOrdering() throws {
        let json = #"[{"millisUTC":"1793642700000","price":"2"},{"millisUTC":"1793642100000","price":"1"},{"millisUTC":"1793643000000","price":"3"}]"#
        let samples = try FeedParser.parse(Data(json.utf8))
        #expect(samples.map(\.cents) == [1, 2, 3])
    }

    @Test func skipsBadRows() throws {
        let json = """
        [{"millisUTC":"1793642400000","price":"1.4"},
         {"millisUTC":"abc","price":"1.0"},
         {"millisUTC":"1793642700000","price":"n/a"},
         {"millisUTC":"1793643000000"},
         {"price":"3.0"},
         {"millisUTC":"1793643300000","price":"nan"},
         {"millisUTC":"1793643600000","price":"inf"},
         {"millisUTC":"0","price":"1.0"},
         {"millisUTC":"-5","price":"1.0"},
         {"millisUTC":null,"price":"1.0"},
         {"millisUTC":"1793643900000","price":true},
         "not an object",
         42,
         null,
         {"millisUTC":"1793644200000","price":"-0.5"}]
        """
        let samples = try FeedParser.parse(Data(json.utf8))
        #expect(samples.map(\.cents) == [1.4, -0.5])
    }

    @Test func acceptsNumericFieldsToo() throws {
        let json = #"[{"millisUTC":1793642400000,"price":2.5}]"#
        let samples = try FeedParser.parse(Data(json.utf8))
        #expect(samples == [PriceSample(stamp: Date(timeIntervalSince1970: 1_793_642_400), cents: 2.5)])
    }

    @Test func deduplicatesByStampKeepingFirstSeen() throws {
        let json = #"[{"millisUTC":"1793642400000","price":"1.0"},{"millisUTC":"1793642400000","price":"9.0"},{"millisUTC":"1793642700000","price":"2.0"}]"#
        let samples = try FeedParser.parse(Data(json.utf8))
        #expect(samples.map(\.cents) == [1.0, 2.0])
    }

    @Test func emptyArrayIsEmptyNotAnError() throws {
        #expect(try FeedParser.parse(Data("[]".utf8)).isEmpty)
    }

    @Test(arguments: ["", "not json", #"{"millisUTC":"1","price":"1"}"#, "null", "\"text\""])
    func nonArrayBodiesAreMalformed(body: String) {
        #expect(throws: FeedError.malformed) { try FeedParser.parse(Data(body.utf8)) }
    }

    @Test func currentHourAverageShapeIsOneElement() throws {
        let samples = try FeedParser.parse(Data(#"[{"millisUTC":"1793643300000","price":"1.5"}]"#.utf8))
        #expect(samples.count == 1)
        #expect(samples[0].cents == 1.5)
    }
}
