import Foundation
@testable import HourlyPriceKit

/// Builds a Date from a UTC time on 2026-11-02 (the day the bucket rule was verified).
func utc(_ hour: Int, _ minute: Int, day: Int = 2) -> Date {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = TimeZone(identifier: "UTC")!
    return calendar.date(from: DateComponents(year: 2026, month: 11, day: day, hour: hour, minute: minute))!
}

func sample(_ hour: Int, _ minute: Int, _ cents: Double, day: Int = 2) -> PriceSample {
    PriceSample(stamp: utc(hour, minute, day: day), cents: cents)
}

/// Intercepts every request made through a session built with `StubURLProtocol.session()`.
final class StubURLProtocol: URLProtocol, @unchecked Sendable {
    struct Captured {
        let request: URLRequest
        let body: Data?
    }

    nonisolated(unsafe) static var handler: ((URLRequest) throws -> (HTTPURLResponse, Data))?
    nonisolated(unsafe) static var captured: [Captured] = []

    static func session() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubURLProtocol.self]
        return URLSession(configuration: configuration)
    }

    static func reset() {
        handler = nil
        captured = []
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        var body = request.httpBody
        if body == nil, let stream = request.httpBodyStream {
            stream.open()
            var data = Data()
            var buffer = [UInt8](repeating: 0, count: 1024)
            while stream.hasBytesAvailable {
                let read = stream.read(&buffer, maxLength: buffer.count)
                if read <= 0 { break }
                data.append(buffer, count: read)
            }
            stream.close()
            body = data
        }
        StubURLProtocol.captured.append(Captured(request: request, body: body))
        do {
            guard let handler = StubURLProtocol.handler else { throw URLError(.badServerResponse) }
            let (response, data) = try handler(request)
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        } catch {
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}

    static func respond(_ status: Int, _ body: String = "", for request: URLRequest) -> (HTTPURLResponse, Data) {
        let response = HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!
        return (response, Data(body.utf8))
    }
}
