import Charts
import HourlyPriceKit
import SwiftUI

/// The 24-hour chart: hourly average bars (default) or the 5-minute line, with drag to inspect.
struct PriceChartSection: View {
    @Environment(AppModel.self) private var model
    let snapshot: PriceSnapshot
    @State private var selectedDate: Date?

    var body: some View {
        @Bindable var prices = model.prices
        VStack(alignment: .leading, spacing: 12) {
            Text("Last 24 hours")
                .font(.headline)
            Picker("Chart", selection: $prices.chartMode) {
                ForEach(PricesModel.ChartMode.allCases) { mode in
                    Text(mode.rawValue).tag(mode)
                }
            }
            .pickerStyle(.segmented)
            .onChange(of: prices.chartMode) { selectedDate = nil }

            readout
            chart
                .frame(height: 220)
        }
    }

    // MARK: Data

    private var bars: [HourAverage] { snapshot.hourBars }
    private var samples: [PriceSample] { snapshot.samples }

    private var selectedBar: HourAverage? {
        guard let date = selectedDate else { return nil }
        return bars.first { $0.start < date && date <= $0.end } ?? bars.min { abs($0.end.timeIntervalSince(date)) < abs($1.end.timeIntervalSince(date)) }
    }

    private var selectedSample: PriceSample? {
        guard let date = selectedDate else { return nil }
        return samples.min { abs($0.stamp.timeIntervalSince(date)) < abs($1.stamp.timeIntervalSince(date)) }
    }

    // MARK: Readout

    @ViewBuilder
    private var readout: some View {
        if let readoutValues {
            HStack(alignment: .firstTextBaseline) {
                Text(readoutValues.title)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Spacer()
                Text(PriceFormat.cents(readoutValues.value))
                    .font(.title3.weight(.semibold))
                    .monospacedDigit()
                Text(readoutValues.band.label)
                    .font(.subheadline.weight(.medium))
                    .foregroundStyle(readoutValues.band.color)
            }
            .accessibilityElement(children: .combine)
        } else {
            Text("Touch and drag across the chart to inspect a value.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
    }

    private var readoutValues: (title: String, value: Double, band: PriceBand)? {
        switch model.prices.chartMode {
        case .hourly:
            guard let bar = selectedBar else { return nil }
            let suffix = bar.isInProgress ? " (so far)" : ""
            return ("Hour ending \(TimeText.hour(bar.end))\(suffix)", bar.average, PriceBand(cents: bar.average))
        case .fiveMinute:
            guard let sample = selectedSample else { return nil }
            return (sample.stamp.formatted(date: .omitted, time: .shortened), sample.cents, PriceBand(cents: sample.cents))
        }
    }

    // MARK: Chart

    @ViewBuilder
    private var chart: some View {
        switch model.prices.chartMode {
        case .hourly: hourlyChart
        case .fiveMinute: fiveMinuteChart
        }
    }

    private var hourlyChart: some View {
        Chart {
            ForEach(bars) { bar in
                RectangleMark(
                    xStart: .value("Start", bar.start.addingTimeInterval(180)),
                    xEnd: .value("End", bar.end.addingTimeInterval(-180)),
                    yStart: .value("Zero", 0),
                    yEnd: .value("Cents per kWh", PriceMath.round1(bar.average))
                )
                .foregroundStyle(PriceBand(cents: bar.average).color.opacity(bar.isInProgress ? 0.55 : 1))
            }
            if let bar = selectedBar {
                RuleMark(x: .value("Selected", bar.start.addingTimeInterval(PriceMath.hour / 2)))
                    .foregroundStyle(.secondary)
                    .lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 3]))
            }
        }
        .chartXSelection(value: $selectedDate)
        .chartXScale(domain: (bars.first?.start ?? Date())...(bars.last?.end ?? Date()))
        .modifier(ChartAxes())
        .accessibilityLabel("Hourly average price, last 24 hours")
        .accessibilityChartDescriptor(PriceChartDescriptor(title: "Hourly average price, last 24 hours", isContinuous: false, points: bars.map { ($0.end, PriceMath.round1($0.average)) }))
    }

    private var fiveMinuteChart: some View {
        Chart {
            ForEach(samples) { sample in
                LineMark(
                    x: .value("Time", sample.stamp),
                    y: .value("Cents per kWh", PriceMath.round1(sample.cents))
                )
                .foregroundStyle(Color.accentColor)
                .interpolationMethod(.stepEnd)
            }
            if let sample = selectedSample {
                RuleMark(x: .value("Selected", sample.stamp))
                    .foregroundStyle(.secondary)
                    .lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 3]))
                PointMark(x: .value("Time", sample.stamp), y: .value("Cents per kWh", PriceMath.round1(sample.cents)))
                    .foregroundStyle(PriceBand(cents: sample.cents).color)
                    .symbolSize(80)
            }
        }
        .chartXSelection(value: $selectedDate)
        .modifier(ChartAxes())
        .accessibilityLabel("5-minute price, last 24 hours")
        .accessibilityChartDescriptor(PriceChartDescriptor(title: "5-minute price, last 24 hours", isContinuous: true, points: samples.map { ($0.stamp, PriceMath.round1($0.cents)) }))
    }
}

private struct ChartAxes: ViewModifier {
    func body(content: Content) -> some View {
        content
            .chartXAxis {
                AxisMarks(values: .stride(by: .hour, count: 6)) { _ in
                    AxisGridLine()
                    AxisTick()
                    AxisValueLabel(format: .dateTime.hour())
                }
            }
            .chartYAxis {
                AxisMarks(position: .leading) { value in
                    AxisGridLine()
                    AxisValueLabel {
                        if let v = value.as(Double.self) { Text(PriceFormat.number(v)) }
                    }
                }
            }
            .chartYAxisLabel("¢ per kWh", position: .top, alignment: .leading)
    }
}

/// Lets VoiceOver users explore the chart point by point (the Audio Graph).
private struct PriceChartDescriptor: AXChartDescriptorRepresentable {
    let title: String
    let isContinuous: Bool
    let points: [(Date, Double)]

    func makeChartDescriptor() -> AXChartDescriptor {
        let times = points.map { $0.0.timeIntervalSince1970 }
        let values = points.map(\.1)
        let xRange = (times.min() ?? 0)...max(times.max() ?? 1, (times.min() ?? 0) + 1)
        let yMin = min(values.min() ?? 0, 0)
        let yRange = yMin...max(values.max() ?? 1, yMin + 1)
        let xAxis = AXNumericDataAxisDescriptor(title: "Time", range: xRange, gridlinePositions: []) { value in
            Date(timeIntervalSince1970: value).formatted(date: .omitted, time: .shortened)
        }
        let yAxis = AXNumericDataAxisDescriptor(title: "Cents per kilowatt hour", range: yRange, gridlinePositions: []) { value in
            PriceFormat.spoken(value)
        }
        let series = AXDataSeriesDescriptor(
            name: title,
            isContinuous: isContinuous,
            dataPoints: points.map { AXDataPoint(x: $0.0.timeIntervalSince1970, y: $0.1) }
        )
        let summary: String
        if let low = values.min(), let high = values.max(), let last = values.last {
            summary = "Latest \(PriceFormat.spoken(last)). Range \(PriceFormat.number(low)) to \(PriceFormat.number(high)) cents."
        } else {
            summary = "No data."
        }
        return AXChartDescriptor(title: title, summary: summary, xAxis: xAxis, yAxis: yAxis, additionalAxes: [], series: [series])
    }

    func updateChartDescriptor(_ descriptor: AXChartDescriptor) {
        let fresh = makeChartDescriptor()
        descriptor.title = fresh.title
        descriptor.summary = fresh.summary
        descriptor.series = fresh.series
    }
}
