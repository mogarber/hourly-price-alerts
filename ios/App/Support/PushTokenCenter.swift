import Foundation
import UIKit

enum PushTokenError: LocalizedError {
    case timedOut
    case failed(String)

    var errorDescription: String? {
        switch self {
        case .timedOut: return "Apple did not send a notification token in time."
        case .failed(let detail): return detail
        }
    }
}

/// Bridges the AppDelegate's token callbacks to async/await.
@MainActor
final class PushTokenCenter {
    static let shared = PushTokenCenter()

    private var waiters: [UUID: CheckedContinuation<String, Error>] = [:]

    /// Calls `registerForRemoteNotifications()` and waits for the token (or an error, or the timeout).
    func requestToken(timeout: Duration = .seconds(15)) async throws -> String {
        let id = UUID()
        return try await withCheckedThrowingContinuation { continuation in
            waiters[id] = continuation
            Task { [weak self] in
                try? await Task.sleep(for: timeout)
                self?.resume(id, with: .failure(PushTokenError.timedOut))
            }
            UIApplication.shared.registerForRemoteNotifications()
        }
    }

    func didRegister(deviceToken: Data) {
        let token = deviceToken.map { String(format: "%02x", $0) }.joined()
        resumeAll(with: .success(token))
    }

    func didFail(_ error: Error) {
        resumeAll(with: .failure(PushTokenError.failed(error.localizedDescription)))
    }

    private func resume(_ id: UUID, with result: Result<String, Error>) {
        guard let continuation = waiters.removeValue(forKey: id) else { return }
        continuation.resume(with: result)
    }

    private func resumeAll(with result: Result<String, Error>) {
        for id in Array(waiters.keys) { resume(id, with: result) }
    }
}
