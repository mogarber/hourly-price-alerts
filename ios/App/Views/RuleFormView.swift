import HourlyPriceKit
import SwiftUI

struct RuleFormView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss

    private let isNew: Bool
    @State private var draft: RuleDraft
    @State private var isSaving = false
    @State private var errorMessage: String?
    @FocusState private var thresholdFocused: Bool

    init(existing: Rule?) {
        isNew = existing == nil
        _draft = State(initialValue: existing.map(RuleDraft.init(rule:)) ?? RuleDraft())
    }

    private static let windows = [5, 10, 15, 30, 45, 60, 90, 120, 180]
    private static let cooldowns = [0, 15, 30, 60, 120, 180, 360, 720, 1440]

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("Alert me when", selection: $draft.kind) {
                        Text("Price goes above").tag(RuleKind.above)
                        Text("Price goes below").tag(RuleKind.below)
                        Text("Price moves by").tag(RuleKind.move)
                    }
                    Picker("Using", selection: $draft.signal) {
                        Text("Hourly average").tag(RuleSignal.hourAverage)
                        Text("5-minute price").tag(RuleSignal.fiveMinute)
                    }
                } footer: {
                    Text("5-minute prices are noisy and can jump around. The hourly average is steadier and is what ComEd bills.")
                }

                Section {
                    HStack {
                        Text(draft.kind == .move ? "By at least" : "Price")
                        TextField("10.0", text: $draft.thresholdText)
                            .keyboardType(.numbersAndPunctuation)
                            .multilineTextAlignment(.trailing)
                            .focused($thresholdFocused)
                            .accessibilityLabel(draft.kind == .move ? "Change in cents" : "Price in cents")
                        Text("¢")
                            .foregroundStyle(.secondary)
                            .accessibilityHidden(true)
                    }
                    if draft.usesDirection {
                        Picker("Direction", selection: $draft.direction) {
                            Text("Goes up").tag(RuleDirection.up)
                            Text("Goes down").tag(RuleDirection.down)
                            Text("Either way").tag(RuleDirection.either)
                        }
                    }
                    if draft.usesWindow {
                        Picker("Within the last", selection: $draft.windowMinutes) {
                            ForEach(Self.windows, id: \.self) { Text(TimeText.duration(minutes: $0)).tag($0) }
                        }
                    }
                } footer: {
                    Text(thresholdFooter)
                }

                Section {
                    Picker("Wait at least", selection: $draft.cooldownMinutes) {
                        ForEach(Self.cooldowns, id: \.self) { minutes in
                            Text(minutes == 0 ? "No wait" : TimeText.duration(minutes: minutes)).tag(minutes)
                        }
                    }
                } header: {
                    Text("Between alerts")
                } footer: {
                    Text("After an alert, the price has to move back past the limit before the next one can fire.")
                }

                Section {
                    Toggle("Quiet hours", isOn: $draft.quietHoursEnabled.animation())
                    if draft.quietHoursEnabled {
                        DatePicker("From", selection: minuteBinding(\.quietStartMinute), displayedComponents: .hourAndMinute)
                        DatePicker("Until", selection: minuteBinding(\.quietEndMinute), displayedComponents: .hourAndMinute)
                    }
                } footer: {
                    Text("Times use this iPhone's time zone. An alert that would arrive during quiet hours waits until they end, if the price still qualifies.")
                }

                if let errorMessage {
                    Section {
                        Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                            .foregroundStyle(.red)
                            .font(.subheadline)
                    }
                }
            }
            .navigationTitle(isNew ? "New alert" : "Edit alert")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    if isSaving {
                        ProgressView()
                    } else {
                        Button("Save") { Task { await save() } }
                    }
                }
                ToolbarItemGroup(placement: .keyboard) {
                    Spacer()
                    Button("Done") { thresholdFocused = false }
                }
            }
            .onChange(of: draft.thresholdText) { errorMessage = nil }
            .interactiveDismissDisabled(isSaving)
        }
    }

    private var thresholdFooter: String {
        switch (draft.kind, draft.signal) {
        case (.above, _): return "You get an alert when the price reaches this value or higher. Use at most one decimal."
        case (.below, _): return "You get an alert when the price reaches this value or lower. Negative prices are allowed."
        case (.move, .hourAverage): return "Compares this hour's average with last hour's average."
        case (.move, .fiveMinute): return "Compares the latest 5-minute price with the lowest or highest price in the window."
        }
    }

    private func minuteBinding(_ keyPath: WritableKeyPath<RuleDraft, Int>) -> Binding<Date> {
        Binding(
            get: {
                let minute = draft[keyPath: keyPath]
                return Calendar.current.date(bySettingHour: minute / 60, minute: minute % 60, second: 0, of: Date()) ?? Date()
            },
            set: { date in
                let parts = Calendar.current.dateComponents([.hour, .minute], from: date)
                draft[keyPath: keyPath] = (parts.hour ?? 0) * 60 + (parts.minute ?? 0)
            }
        )
    }

    private func save() async {
        thresholdFocused = false
        switch draft.build() {
        case .invalid(let message):
            errorMessage = message
        case .rule(let rule):
            isSaving = true
            defer { isSaving = false }
            do {
                try await model.alerts.save(rule)
                dismiss()
            } catch {
                errorMessage = model.alerts.describe(error)
                await model.registrar.refreshAuthorization()
            }
        }
    }
}

#if DEBUG
#Preview("New alert") {
    RuleFormView(existing: nil).environment(AppModel.preview())
}

#Preview("Edit move alert, dark") {
    RuleFormView(existing: BackendHolder.sampleRules[1])
        .environment(AppModel.preview())
        .preferredColorScheme(.dark)
}
#endif
