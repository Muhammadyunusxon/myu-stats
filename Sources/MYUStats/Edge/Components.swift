import SwiftUI

struct RingGauge: View {
    var fraction: Double
    var color: Color
    var lineWidth: CGFloat = 4

    var body: some View {
        ZStack {
            Circle().stroke(Color.white.opacity(0.16), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: max(0.001, min(fraction, 1)))
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
    }
}

struct BarGauge: View {
    var fraction: Double
    var color: Color

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.white.opacity(0.14))
                Capsule()
                    .fill(color)
                    .frame(width: max(4, proxy.size.width * max(0, min(fraction, 1))))
            }
        }
        .frame(height: 4)
    }
}

/// Filled line chart; values are normalised to the largest sample (or `ceiling`).
struct Sparkline: View {
    var values: [Double]
    var color: Color
    var ceiling: Double?

    var body: some View {
        GeometryReader { proxy in
            let top = max(ceiling ?? values.max() ?? 1, .leastNonzeroMagnitude)
            let points = values.enumerated().map { index, value in
                CGPoint(
                    x: values.count > 1 ? proxy.size.width * CGFloat(index) / CGFloat(values.count - 1) : 0,
                    y: proxy.size.height * (1 - CGFloat(min(value / top, 1)))
                )
            }
            ZStack {
                Path { path in
                    guard let first = points.first, let last = points.last else { return }
                    path.move(to: CGPoint(x: first.x, y: proxy.size.height))
                    points.forEach { path.addLine(to: $0) }
                    path.addLine(to: CGPoint(x: last.x, y: proxy.size.height))
                    path.closeSubpath()
                }
                .fill(LinearGradient(colors: [color.opacity(0.35), color.opacity(0.02)], startPoint: .top, endPoint: .bottom))

                Path { path in
                    guard let first = points.first else { return }
                    path.move(to: first)
                    points.dropFirst().forEach { path.addLine(to: $0) }
                }
                .stroke(color, style: StrokeStyle(lineWidth: 1.4, lineJoin: .round))
            }
        }
    }
}

// MARK: - Card building blocks

struct CardHeader: View {
    var symbol: String
    var title: String
    var subtitle: String

    var body: some View {
        HStack(spacing: 9) {
            Image(systemName: symbol)
                .font(.system(size: 16, weight: .medium))
                .foregroundStyle(.white)
                .frame(width: 22)
            VStack(alignment: .leading, spacing: 1) {
                Text(title)
                    .font(.system(size: 14, weight: .bold))
                    .foregroundStyle(.white)
                Text(subtitle)
                    .font(.system(size: 10))
                    .foregroundStyle(.white.opacity(0.6))
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
        }
    }
}

struct UsageSection: View {
    var title: String
    var trailing: String
    var fraction: Double
    var color: Color
    var caption: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title).foregroundStyle(.white)
                Spacer()
                Text(trailing).foregroundStyle(.white.opacity(0.6)).monospacedDigit()
            }
            .font(.system(size: 11, weight: .medium))
            BarGauge(fraction: fraction, color: color)
            Text(caption)
                .font(.system(size: 10).monospacedDigit())
                .foregroundStyle(.white.opacity(0.75))
        }
    }
}

struct DetailRow: View {
    var label: String
    var value: String
    var valueColor: Color = .white.opacity(0.6)
    var fontSize: CGFloat = 11

    var body: some View {
        HStack(spacing: 8) {
            Text(label)
                .foregroundStyle(.white)
                .lineLimit(1)
                .truncationMode(.tail)
            Spacer(minLength: 6)
            Text(value)
                .foregroundStyle(valueColor)
                .monospacedDigit()
                .lineLimit(1)
                .fixedSize()
        }
        .font(.system(size: fontSize))
    }
}

struct CardDivider: View {
    var body: some View {
        Rectangle().fill(Color.white.opacity(0.14)).frame(height: 0.5)
    }
}

struct SectionTitle: View {
    var text: String

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 9, weight: .semibold))
            .foregroundStyle(.white.opacity(0.45))
            .tracking(0.5)
    }
}

struct ProcessList: View {
    var title: String
    var processes: [ProcessUsage]
    var value: (ProcessUsage) -> String

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            SectionTitle(text: title)
            if processes.isEmpty {
                Text("Measuring…")
                    .font(.system(size: 11))
                    .foregroundStyle(.white.opacity(0.5))
            } else {
                ForEach(processes) { process in
                    DetailRow(label: process.name, value: value(process), fontSize: 10)
                }
            }
        }
    }
}
