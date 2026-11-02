import HourlyPriceKit
import SwiftUI

struct PricesView: View {
    @Environment(AppModel.self) private var model
    @ScaledMetric(relativeTo: .largeTitle) private var bigNumberSize = 64

    var body: some View {
        let prices = model.prices
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    if let snapshot = prices.snapshot, let current = snapshot.currentHour, let latest = snapshot.latest {
                        statusBanner(prices: prices, snapshot: snapshot)
                        header(current: current, latest: latest)
                        latestRow(latest: latest)
                        PriceChartSection(snapshot: snapshot)
                        footer
                    } else if prices.isRefreshing {
                        ProgressView("Loading prices")
                            .frame(maxWidth: .infinity, minHeight: 240)
                    } else {
                        ContentUnavailableView {
                            Label("Can't load prices", systemImage: "wifi.slash")
                        } description: {
                            Text("Hourly Price Alerts couldn't reach ComEd and has no saved prices yet.")
                        } actions: {
                            Button("Try again") { Task { await prices.refresh() } }
                                .buttonStyle(.borderedProminent)
                        }
                    }
                }
                .padding()
            }
            .refreshable { await prices.refresh() }
            .navigationTitle("Prices")
            .navigationBarTitleDisplayMode(.inline)
        }
    }

    // MARK: Sections

    @ViewBuilder
    private func statusBanner(prices: PricesModel, snapshot: PriceSnapshot) -> some View {
        if prices.refreshFailed {
            Banner(
                symbol: "wifi.slash",
                text: "Offline. Showing saved prices from \(TimeText.stamp(snapshot.fetchedAt)).",
                tint: .orange
            )
        } else if prices.isFromCache {
            Banner(
                symbol: "clock.arrow.circlepath",
                text: "Saved prices from \(TimeText.stamp(snapshot.fetchedAt)). Updating.",
                tint: .secondary
            )
        } else if snapshot.isDelayed(at: Date()), let latest = snapshot.latest {
            Banner(
                symbol: "exclamationmark.triangle",
                text: "ComEd's newest reading is from \(TimeText.stamp(latest.stamp)). Prices may be delayed.",
                tint: .orange
            )
        }
    }

    private func header(current: HourAverage, latest: PriceSample) -> some View {
        let band = PriceBand(cents: current.average)
        let progress = current.isInProgress ? "\(current.count) of \(PriceMath.samplesPerHour) readings so far" : "all \(current.count) readings"
        return VStack(alignment: .leading, spacing: 8) {
            Text("Current hour average")
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(.secondary)
            Text(PriceFormat.cents(current.average))
                .font(.system(size: bigNumberSize, weight: .bold, design: .rounded))
                .minimumScaleFactor(0.5)
                .lineLimit(1)
                .contentTransition(.numericText())
            HStack {
                BandPill(band: band)
                Spacer(minLength: 0)
            }
            Text("Hour ending \(TimeText.hour(current.end)), \(progress)")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Current hour average")
        .accessibilityValue("\(PriceFormat.spoken(current.average)), \(band.label). Hour ending \(TimeText.hour(current.end)), \(progress).")
    }

    private func latestRow(latest: PriceSample) -> some View {
        let band = PriceBand(cents: latest.cents)
        return VStack(alignment: .leading, spacing: 4) {
            // Large text sizes don't fit on one line, and a band label must never break mid-word.
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Latest 5-minute price")
                        .font(.subheadline)
                        .fixedSize()
                    Spacer()
                    latestValue(latest: latest, band: band)
                }
                VStack(alignment: .leading, spacing: 4) {
                    Text("Latest 5-minute price")
                        .font(.subheadline)
                    latestValue(latest: latest, band: band)
                }
            }
            if let snapshot = model.prices.snapshot {
                Text("Reading from \(TimeText.stamp(latest.stamp)). Checked \(TimeText.stamp(snapshot.fetchedAt)).")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
        }
        .padding()
        .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Latest 5-minute price")
        .accessibilityValue("\(PriceFormat.spoken(latest.cents)), \(band.label), reading from \(TimeText.stamp(latest.stamp))")
    }

    private func latestValue(latest: PriceSample, band: PriceBand) -> some View {
        HStack(alignment: .firstTextBaseline) {
            Text(PriceFormat.cents(latest.cents))
                .font(.title3.weight(.semibold))
                .monospacedDigit()
            Text(band.label)
                .font(.subheadline.weight(.medium))
                .foregroundStyle(band.color)
        }
        .fixedSize()
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("ComEd bills hourly pricing customers on the hourly average. The 5-minute price is shown for information and moves around a lot.")
            Text("Prices are in cents per kilowatt hour (¢/kWh). Price levels are relative labels, not advice.")
        }
        .font(.footnote)
        .foregroundStyle(.secondary)
    }
}

private struct Banner: View {
    let symbol: String
    let text: String
    let tint: Color

    var body: some View {
        Label(text, systemImage: symbol)
            .font(.footnote.weight(.medium))
            .foregroundStyle(tint)
            .padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(tint.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
    }
}

#if DEBUG
#Preview("Prices") {
    PricesView().environment(AppModel.preview())
}

#Preview("Prices, dark, large type") {
    PricesView()
        .environment(AppModel.preview())
        .preferredColorScheme(.dark)
        .dynamicTypeSize(.accessibility2)
}
#endif
