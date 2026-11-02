import SwiftUI

@main
struct HourlyPriceAlertsApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var model = AppModel(config: .load())

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(model)
        }
    }
}
