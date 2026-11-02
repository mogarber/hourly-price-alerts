import SwiftUI

/// Shown before the system permission prompt, the first time the user adds an alert.
/// "Continue" asks for permission, registers for remote notifications and registers the device.
struct NotificationExplainerView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let onFinish: (_ granted: Bool) -> Void

    @State private var isWorking = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 20) {
                Image(systemName: "bell.badge.fill")
                    .font(.system(size: 48))
                    .foregroundStyle(Color.accentColor)
                    .accessibilityHidden(true)
                Text("Alerts are push notifications")
                    .font(.title2.bold())
                Text("Hourly Price Alerts can only warn you about a price if it is allowed to send notifications. iOS will ask for permission next.")
                VStack(alignment: .leading, spacing: 8) {
                    Label("Your iPhone's notification token and time zone are sent to the alert service, plus the alerts you create.", systemImage: "lock.shield")
                    Label("No name, email or location is collected.", systemImage: "person.crop.circle.badge.xmark")
                }
                .font(.subheadline)
                .foregroundStyle(.secondary)
                if let errorMessage {
                    Text(errorMessage)
                        .font(.footnote)
                        .foregroundStyle(.red)
                }
                Spacer()
                Button {
                    Task { await proceed() }
                } label: {
                    HStack {
                        if isWorking { ProgressView() }
                        Text(errorMessage == nil ? "Continue" : "Try again")
                    }
                    .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(isWorking)
            }
            .padding()
            .navigationTitle("Turn on notifications")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Not now") { onFinish(false) }
                }
            }
        }
        .presentationDetents([.large])
    }

    private func proceed() async {
        isWorking = true
        errorMessage = nil
        defer { isWorking = false }
        do {
            try await model.registrar.ensureRegistered()
            onFinish(true)
        } catch {
            await model.registrar.refreshAuthorization()
            errorMessage = model.alerts.describe(error)
        }
    }
}
