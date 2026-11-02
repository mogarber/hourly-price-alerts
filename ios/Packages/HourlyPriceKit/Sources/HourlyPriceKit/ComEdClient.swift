import Foundation

public enum ComEdError: Error, Equatable, Sendable {
    case badStatus(Int)
    case invalidResponse
    case emptyFeed
    case malformed
    case transport(String)
}

public protocol PriceFeed: Sendable {
    /// About 24 hours of 5-minute samples, normalized ascending.
    func fetchFiveMinuteFeed() async throws -> [PriceSample]
    /// ComEd's own current hour average (used only to cross-check the local math).
    func fetchCurrentHourAverage() async throws -> PriceSample
}

/// Reads prices straight from ComEd.
public struct ComEdClient: PriceFeed {
    public static let defaultBaseURL = URL(string: "https://hourlypricing.comed.com/api")!

    private let session: URLSession
    private let baseURL: URL
    private let userAgent: String

    public init(session: URLSession = .shared, baseURL: URL = ComEdClient.defaultBaseURL, version: String) {
        self.session = session
        self.baseURL = baseURL
        self.userAgent = "HourlyPriceAlerts/\(version)"
    }

    public func fetchFiveMinuteFeed() async throws -> [PriceSample] {
        let samples = try await fetch(type: "5minutefeed")
        if samples.isEmpty { throw ComEdError.emptyFeed }
        return samples
    }

    public func fetchCurrentHourAverage() async throws -> PriceSample {
        let samples = try await fetch(type: "currenthouraverage")
        guard let sample = samples.last else { throw ComEdError.emptyFeed }
        return sample
    }

    func request(type: String) -> URLRequest {
        var components = URLComponents(url: baseURL, resolvingAgainstBaseURL: false)!
        components.queryItems = [URLQueryItem(name: "type", value: type)]
        var request = URLRequest(url: components.url!)
        request.httpMethod = "GET"
        request.timeoutInterval = 20
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        return request
    }

    private func fetch(type: String) async throws -> [PriceSample] {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request(type: type))
        } catch {
            throw ComEdError.transport((error as? URLError).map { "\($0.code.rawValue)" } ?? error.localizedDescription)
        }
        guard let http = response as? HTTPURLResponse else { throw ComEdError.invalidResponse }
        guard (200..<300).contains(http.statusCode) else { throw ComEdError.badStatus(http.statusCode) }
        do {
            return try FeedParser.parse(data)
        } catch {
            throw ComEdError.malformed
        }
    }
}
