import HourlyPriceKit
import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model

    enum TestState: Equatable {
        case idle, sending, sent
        case failed(String)
    }

    @State private var testState: TestState = .idle
    @State private var confirmDelete = false
    @State private var isDeleting = false
    @State private var deleteResult: DeleteResult?

    enum DeleteResult: Identifiable {
        case done
        case failed(String)
        var id: String { switch self { case .done: "done"; case .failed: "failed" } }
    }

    var body: some View {
        NavigationStack {
            Form {
                if model.config.isMockBackend {
                    Section {
                        Label("Mock backend: alerts are kept in memory and no real notifications are sent.", systemImage: "wrench.and.screwdriver")
                            .font(.footnote)
                    }
                }

                Section {
                    Button {
                        Task { await sendTest() }
                    } label: {
                        HStack {
                            Text("Send test notification")
                            Spacer()
                            if testState == .sending { ProgressView() }
                        }
                    }
                    .disabled(!model.config.alertsAvailable || testState == .sending)
                } header: {
                    Text("Notifications")
                } footer: {
                    switch testState {
                    case .idle:
                        Text(model.config.alertsAvailable ? "Sends a push to this iPhone to check that alerts can reach you." : "Not available: this build has no alert service address.")
                    case .sending: Text("Sending.")
                    case .sent: Text(model.config.isMockBackend ? "Test accepted by the mock backend." : "Sent. It should arrive in a few seconds.")
                    case .failed(let message): Text(message).foregroundStyle(.red)
                    }
                }

                Section {
                    Button("Delete my data", role: .destructive) { confirmDelete = true }
                        .disabled(isDeleting)
                } header: {
                    Text("Your data")
                } footer: {
                    Text("Removes this iPhone and all its alerts from the alert service, and clears saved prices and alerts on this iPhone.")
                }

                Section("About the data") {
                    LabeledContent("Prices", value: "ComEd Hourly Pricing")
                    Link("hourlypricing.comed.com", destination: URL(string: "https://hourlypricing.comed.com")!)
                    Text("Hourly Price Alerts is not affiliated with ComEd.")
                    Link("Source code", destination: AppConfig.sourceCodeURL)
                }

                Section("Privacy") {
                    Text("Prices are fetched straight from ComEd by this app. If you create alerts, the alert service stores a random install ID, your iPhone's push token and time zone, and your alert settings. It stores no name, email or location, and there is no account. Delete my data removes all of it.")
                        .font(.footnote)
                    Link("Privacy policy", destination: AppConfig.privacyPolicyURL)
                }

                Section {
                    LabeledContent("App", value: "Hourly Price Alerts")
                    LabeledContent("Version", value: model.config.version)
                }
            }
            .navigationTitle("Settings")
            .confirmationDialog("Delete all your data?", isPresented: $confirmDelete, titleVisibility: .visible) {
                Button("Delete my data", role: .destructive) { Task { await deleteData() } }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("Your alerts will stop and can't be recovered.")
            }
            .alert(item: $deleteResult) { result in
                switch result {
                case .done: Alert(title: Text("Data deleted"), message: Text("Your device and alerts were removed."))
                case .failed(let message): Alert(title: Text("Couldn't delete your data"), message: Text(message))
                }
            }
        }
    }

    private func sendTest() async {
        testState = .sending
        do {
            try await model.sendTestNotification()
            testState = .sent
        } catch {
            testState = .failed(model.alerts.describe(error))
            await model.registrar.refreshAuthorization()
        }
    }

    private func deleteData() async {
        isDeleting = true
        defer { isDeleting = false }
        do {
            try await model.deleteMyData()
            testState = .idle
            deleteResult = .done
        } catch {
            deleteResult = .failed(model.alerts.describe(error))
        }
    }
}

#if DEBUG
#Preview("Settings") {
    SettingsView().environment(AppModel.preview())
}
#endif
