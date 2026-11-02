import SwiftUI

struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        TabView {
            PricesView()
                .tabItem { Label("Prices", systemImage: "bolt.fill") }
            AlertsView()
                .tabItem { Label("Alerts", systemImage: "bell.badge.fill") }
            SettingsView()
                .tabItem { Label("Settings", systemImage: "gearshape.fill") }
        }
        // Runs while the scene is active and is cancelled as soon as it is not, so the 60 second
        // refresh never runs in the background.
        .task(id: scenePhase) {
            guard scenePhase == .active else { return }
            await model.sceneBecameActive()
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(60))
                if Task.isCancelled { break }
                await model.prices.refresh()
            }
        }
    }
}
