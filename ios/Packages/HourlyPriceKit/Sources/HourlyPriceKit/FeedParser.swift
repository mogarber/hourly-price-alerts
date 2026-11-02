import Foundation

public enum FeedError: Error, Equatable, Sendable {
    /// The body was not a JSON array.
    case malformed
}

/// Parses the ComEd feed: `[{"millisUTC":"1793642400000","price":"1.4"}, ...]`.
/// Both fields are strings in the real feed; JSON numbers are tolerated too. Rows that cannot be
/// parsed are skipped. The result is sorted ascending and de-duplicated by stamp.
public enum FeedParser {
    public static func parse(_ data: Data) throws -> [PriceSample] {
        let object: Any
        do {
            object = try JSONSerialization.jsonObject(with: data)
        } catch {
            throw FeedError.malformed
        }
        guard let rows = object as? [Any] else { throw FeedError.malformed }
        var samples: [PriceSample] = []
        samples.reserveCapacity(rows.count)
        for row in rows {
            guard let dict = row as? [String: Any],
                  let millis = int64(dict["millisUTC"]), millis > 0,
                  let price = double(dict["price"]) else { continue }
            samples.append(PriceSample(stamp: Date(timeIntervalSince1970: Double(millis) / 1000), cents: price))
        }
        return PriceSample.normalized(samples)
    }

    private static func int64(_ value: Any?) -> Int64? {
        switch value {
        case let string as String:
            return Int64(string.trimmingCharacters(in: .whitespaces))
        case let number as NSNumber where CFGetTypeID(number) != CFBooleanGetTypeID():
            let d = number.doubleValue
            guard d.isFinite, d == d.rounded(), abs(d) < 9e15 else { return nil }
            return Int64(d)
        default:
            return nil
        }
    }

    private static func double(_ value: Any?) -> Double? {
        let result: Double?
        switch value {
        case let string as String:
            result = Double(string.trimmingCharacters(in: .whitespaces))
        case let number as NSNumber where CFGetTypeID(number) != CFBooleanGetTypeID():
            result = number.doubleValue
        default:
            result = nil
        }
        guard let result, result.isFinite else { return nil }
        return result
    }
}
