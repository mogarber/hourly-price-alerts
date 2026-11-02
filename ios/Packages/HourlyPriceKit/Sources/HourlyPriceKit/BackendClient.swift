import Foundation

/// What the app sends to register a device. Field names match the API.
public struct DeviceRegistration: Codable, Equatable, Sendable {
    public var apnsToken: String
    public var apnsEnvironment: String
    public var timeZone: String

    public init(apnsToken: String, apnsEnvironment: String, timeZone: String) {
        self.apnsToken = apnsToken
        self.apnsEnvironment = apnsEnvironment
        self.timeZone = timeZone
    }
}

/// The per-install identity: a lowercase UUID plus 32 random bytes as 64 lowercase hex chars.
public struct BackendCredentials: Equatable, Sendable, Codable {
    public let installId: String
    public let secret: String

    public init(installId: String, secret: String) {
        self.installId = installId
        self.secret = secret
    }

    public static func generate() -> BackendCredentials {
        var generator = SystemRandomNumberGenerator()
        let bytes = (0..<32).map { _ in UInt8.random(in: .min ... .max, using: &generator) }
        let secret = bytes.map { String(format: "%02x", $0) }.joined()
        return BackendCredentials(installId: UUID().uuidString.lowercased(), secret: secret)
    }

    /// True when both values have the shape the API expects.
    public var isWellFormed: Bool {
        UUID(uuidString: installId) != nil && installId == installId.lowercased()
            && secret.count == 64 && secret.allSatisfy { "0123456789abcdef".contains($0) }
    }
}

public enum BackendError: Error, Equatable, Sendable {
    /// 400, with the server's human readable message.
    case badRequest(String)
    /// 401: missing or wrong secret.
    case unauthorized
    /// 404.
    case notFound
    /// 429: for example, a second test notification within 60 seconds.
    case rateLimited
    /// Any other non-2xx status.
    case server(Int)
    /// A 2xx response whose body could not be read.
    case invalidResponse
    /// The request never completed (offline, timeout, TLS).
    case network(String)
    /// No API_BASE_URL is configured.
    case notConfigured
}

extension BackendError: LocalizedError {
    public var errorDescription: String? {
        switch self {
        case .badRequest(let message): return message
        case .unauthorized: return "The alert service did not accept this device's credentials."
        case .notFound: return "The alert service could not find this device."
        case .rateLimited: return "Too many requests. Try again in a minute."
        case .server(let status): return "The alert service had a problem (status \(status)). Try again later."
        case .invalidResponse: return "The alert service sent a reply the app could not read."
        case .network: return "Couldn't reach the alert service. Check your connection and try again."
        case .notConfigured: return "This build has no alert service configured."
        }
    }
}

/// The alert backend. Implemented by `HTTPBackendClient` and `MockBackendClient`.
public protocol BackendClient: Sendable {
    func registerDevice(_ registration: DeviceRegistration) async throws
    func deleteDevice() async throws
    func fetchRules() async throws -> [Rule]
    func saveRule(_ rule: Rule) async throws -> Rule
    func deleteRule(id: String) async throws
    func sendTestNotification() async throws
}

/// Talks to the Azure Functions API with `Authorization: Bearer <secret>`.
public struct HTTPBackendClient: BackendClient {
    private let baseURL: URL
    private let credentials: BackendCredentials
    private let session: URLSession
    private let userAgent: String

    /// `baseURL` is the API_BASE_URL setting. If it has no path (just a host), `/api` is added, because the
    /// Functions host serves every route under `/api`. Pass a URL that already ends in `/api` to use it as is.
    public init(baseURL: URL, credentials: BackendCredentials, session: URLSession = .shared, version: String) {
        self.baseURL = HTTPBackendClient.normalizedBase(baseURL)
        self.credentials = credentials
        self.session = session
        self.userAgent = "HourlyPriceAlerts/\(version)"
    }

    static func normalizedBase(_ url: URL) -> URL {
        var components = URLComponents(url: url, resolvingAgainstBaseURL: false) ?? URLComponents()
        var path = components.path
        while path.hasSuffix("/") { path.removeLast() }
        if path.isEmpty { path = "/api" }
        components.path = path
        components.query = nil
        components.fragment = nil
        return components.url ?? url
    }

    func makeRequest(method: String, path: [String], body: Data? = nil) -> URLRequest {
        var url = baseURL
        for component in path { url.append(path: component) }
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.timeoutInterval = 20
        request.setValue("Bearer \(credentials.secret)", forHTTPHeaderField: "Authorization")
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.httpBody = body
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        return request
    }

    public func registerDevice(_ registration: DeviceRegistration) async throws {
        let body = try JSONEncoder().encode(registration)
        _ = try await send(makeRequest(method: "PUT", path: ["v1", "devices", credentials.installId], body: body))
    }

    public func deleteDevice() async throws {
        _ = try await send(makeRequest(method: "DELETE", path: ["v1", "devices", credentials.installId]))
    }

    public func fetchRules() async throws -> [Rule] {
        let data = try await send(makeRequest(method: "GET", path: ["v1", "devices", credentials.installId, "rules"]))
        do {
            return try JSONDecoder().decode(RulesResponse.self, from: data).rules
        } catch {
            throw BackendError.invalidResponse
        }
    }

    public func saveRule(_ rule: Rule) async throws -> Rule {
        let body = try JSONEncoder().encode(rule)
        let data = try await send(makeRequest(method: "PUT", path: ["v1", "devices", credentials.installId, "rules", rule.id], body: body))
        do {
            return try JSONDecoder().decode(Rule.self, from: data)
        } catch {
            throw BackendError.invalidResponse
        }
    }

    public func deleteRule(id: String) async throws {
        _ = try await send(makeRequest(method: "DELETE", path: ["v1", "devices", credentials.installId, "rules", id]))
    }

    public func sendTestNotification() async throws {
        _ = try await send(makeRequest(method: "POST", path: ["v1", "devices", credentials.installId, "test"]))
    }

    /// Sends the request and maps the status. Returns the body of a 2xx response.
    private func send(_ request: URLRequest) async throws -> Data {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw BackendError.network((error as? URLError).map { "\($0.code.rawValue)" } ?? "error")
        }
        guard let http = response as? HTTPURLResponse else { throw BackendError.invalidResponse }
        switch http.statusCode {
        case 200..<300: return data
        case 400: throw BackendError.badRequest(HTTPBackendClient.errorMessage(in: data))
        case 401: throw BackendError.unauthorized
        case 404: throw BackendError.notFound
        case 429: throw BackendError.rateLimited
        default: throw BackendError.server(http.statusCode)
        }
    }

    private struct ErrorBody: Decodable { let error: String }

    static func errorMessage(in data: Data) -> String {
        (try? JSONDecoder().decode(ErrorBody.self, from: data))?.error ?? "The alert service rejected the request."
    }
}
