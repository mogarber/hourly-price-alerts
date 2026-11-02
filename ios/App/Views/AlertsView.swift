import HourlyPriceKit
import SwiftUI

struct AlertsView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openURL) private var openURL
    @State private var sheet: Sheet?
    @State private var showDeniedAlert = false

    enum Sheet: Identifiable {
        case explainer
        case form(Rule?)

        var id: String {
            switch self {
            case .explainer: return "explainer"
            case .form(let rule): return "form-\(rule?.id ?? "new")"
            }
        }
    }

    var body: some View {
        let alerts = model.alerts
        NavigationStack {
            Group {
                if !model.config.alertsAvailable {
                    ContentUnavailableView {
                        Label("Alerts aren't set up", systemImage: "bell.slash")
                    } description: {
                        Text("This build has no alert service address (API_BASE_URL is empty), so alerts can't be sent. Prices still work. See Config/Local.xcconfig.example to set it.")
                    }
                } else {
                    list(alerts)
                }
            }
            .navigationTitle("Alerts")
            .toolbar {
                if model.config.alertsAvailable {
                    ToolbarItem(placement: .primaryAction) {
                        Button("Add alert", systemImage: "plus") { beginAdd() }
                            .disabled(!alerts.canAddMore)
                    }
                }
            }
            .sheet(item: $sheet) { sheet in
                switch sheet {
                case .explainer:
                    NotificationExplainerView { granted in
                        self.sheet = granted ? .form(nil) : nil
                    }
                case .form(let rule):
                    RuleFormView(existing: rule)
                }
            }
            .alert("Notifications are off", isPresented: $showDeniedAlert) {
                Button("Open Settings") { openSettings() }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("Turn on notifications for Hourly Price Alerts in Settings to receive price alerts.")
            }
            .alert("Something went wrong", isPresented: Binding(get: { alerts.actionError != nil }, set: { if !$0 { alerts.actionError = nil } })) {
                Button("OK", role: .cancel) {}
            } message: {
                Text(alerts.actionError ?? "")
            }
        }
    }

    @ViewBuilder
    private func list(_ alerts: AlertsModel) -> some View {
        List {
            if model.registrar.authorization == .denied {
                Section {
                    VStack(alignment: .leading, spacing: 8) {
                        Label("Notifications are off", systemImage: "bell.slash.fill")
                            .font(.headline)
                            .foregroundStyle(.orange)
                        Text("Alerts can't reach you until you allow notifications for Hourly Price Alerts.")
                            .font(.subheadline)
                        Button("Open Settings") { openSettings() }
                            .buttonStyle(.borderedProminent)
                    }
                    .padding(.vertical, 4)
                }
            }
            if let error = alerts.loadError {
                Section {
                    Label(error, systemImage: "wifi.slash")
                        .font(.footnote)
                        .foregroundStyle(.orange)
                } footer: {
                    Text("Showing the alerts last saved on this iPhone.")
                }
            }
            if alerts.rules.isEmpty {
                Section {
                    ContentUnavailableView {
                        Label("No alerts yet", systemImage: "bell")
                    } description: {
                        Text("Get a push notification when the price goes above or below a limit you choose, or jumps quickly.")
                    } actions: {
                        Button("Add alert") { beginAdd() }
                            .buttonStyle(.borderedProminent)
                    }
                    .listRowBackground(Color.clear)
                }
            } else {
                Section {
                    ForEach(alerts.rules) { rule in
                        RuleRow(rule: rule) { enabled in
                            Task { await alerts.setEnabled(enabled, for: rule) }
                        }
                        .contentShape(Rectangle())
                        .onTapGesture { sheet = .form(rule) }
                    }
                    .onDelete { offsets in
                        let doomed = offsets.map { alerts.rules[$0] }
                        Task { for rule in doomed { await alerts.delete(rule) } }
                    }
                } footer: {
                    Text("\(alerts.rules.count) of \(Rule.maxRulesPerDevice) alerts. Values are compared after rounding to one decimal, exactly as shown on the Prices tab.")
                }
            }
        }
        .refreshable { await alerts.refresh() }
    }

    private func beginAdd() {
        switch model.registrar.authorization {
        case .denied: showDeniedAlert = true
        case .notDetermined: sheet = .explainer
        case .authorized, .unknown: sheet = .form(nil)
        }
    }

    private func openSettings() {
        if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
    }
}

private struct RuleRow: View {
    let rule: Rule
    let onToggle: (Bool) -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text(rule.title)
                    .font(.headline)
                Text(detail)
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            Toggle("Enabled", isOn: Binding(get: { rule.enabled }, set: onToggle))
                .labelsHidden()
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
        .accessibilityHint("Double tap to edit. Swipe up or down for actions.")
    }

    private var detail: String {
        var parts: [String] = []
        switch rule.signal {
        case .hourAverage: parts.append("Hourly average")
        case .fiveMinute:
            if rule.kind == .move, let window = rule.windowMinutes {
                parts.append("5-minute price, over \(TimeText.duration(minutes: window))")
            } else {
                parts.append("5-minute price")
            }
        }
        parts.append(rule.cooldownMinutes == 0 ? "no wait between alerts" : "at most every \(TimeText.duration(minutes: rule.cooldownMinutes))")
        if let q = rule.quietHours {
            parts.append("quiet \(TimeText.minuteOfDay(q.startMinute)) to \(TimeText.minuteOfDay(q.endMinute))")
        }
        if !rule.enabled { parts.append("off") }
        return parts.joined(separator: " \u{00B7} ")
    }
}

#if DEBUG
#Preview("Alerts with mock rules") {
    AlertsView().environment(AppModel.preview(loadRules: true))
}

#Preview("Alerts not configured") {
    AlertsView().environment(AppModel.preview(alertsAvailable: false))
}
#endif
