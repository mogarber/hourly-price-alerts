import Foundation
import HourlyPriceKit
import Observation

@MainActor
@Observable
final class PricesModel {
    enum ChartMode: String, CaseIterable, Identifiable {
        case hourly = "Hourly"
        case fiveMinute = "5-minute"
        var id: String { rawValue }
    }

    private(set) var snapshot: PriceSnapshot?
    private(set) var isRefreshing = false
    /// The last refresh failed; the snapshot (if any) is cached data.
    private(set) var refreshFailed = false
    /// True until a refresh succeeds in this session, so cached data is labelled as saved data.
    private(set) var isFromCache = false
    var chartMode: ChartMode = .hourly

    private let feed: any PriceFeed
    private let store: LocalStore

    init(feed: any PriceFeed, store: LocalStore) {
        self.feed = feed
        self.store = store
        if let cached = store.prices.load() {
            snapshot = cached
            isFromCache = true
        }
    }

    func refresh() async {
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }
        do {
            let samples = try await feed.fetchFiveMinuteFeed()
            let fresh = PriceSnapshot(samples: samples, fetchedAt: Date())
            snapshot = fresh
            store.prices.save(fresh)
            refreshFailed = false
            isFromCache = false
        } catch {
            if Task.isCancelled || (error as? ComEdError) == .transport("\(URLError.cancelled.rawValue)") { return }
            refreshFailed = true
        }
    }

    func reset() {
        snapshot = nil
        isFromCache = false
        refreshFailed = false
    }
}
