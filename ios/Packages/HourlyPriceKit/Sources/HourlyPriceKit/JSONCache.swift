import Foundation

/// A tiny JSON file store for the local cache (last prices, rules, registration record).
public struct JSONCache<Value: Codable & Sendable>: Sendable {
    public let url: URL

    public init(url: URL) {
        self.url = url
    }

    public func load() -> Value? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(Value.self, from: data)
    }

    public func save(_ value: Value) {
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try JSONEncoder().encode(value).write(to: url, options: .atomic)
        } catch {
            // The cache is best effort; a failed write only costs the offline copy.
        }
    }

    public func remove() {
        try? FileManager.default.removeItem(at: url)
    }
}
