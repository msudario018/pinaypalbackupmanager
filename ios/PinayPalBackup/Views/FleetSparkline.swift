import SwiftUI

/// A compact CPU/RAM trend line for a managed PC.
///
/// Deliberately axis-free: on a 60pt tall card, tick labels cost more space than they explain.
/// The legend outside the chart carries the actual current numbers.
struct FleetSparkline: View {
    let points: [PinayPalAPIService.TelemetryPoint]
    let metric: Metric

    enum Metric {
        case cpu, ram

        var label: String { self == .cpu ? "CPU" : "RAM" }
        var color: Color { self == .cpu ? .orange : .cyan }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(metric.label)
                .font(.system(size: 9, weight: .semibold))
                .foregroundStyle(.secondary)

            GeometryReader { geo in
                if points.count < 2 {
                    // Fewer than two samples means there is genuinely no trend to show yet.
                    RoundedRectangle(cornerRadius: 3)
                        .fill(.quaternary)
                        .frame(height: 22)
                } else {
                    path(in: geo.size)
                        .stroke(metric.color, style: StrokeStyle(lineWidth: 1.5, lineJoin: .round))
                }
            }
            .frame(height: 22)
        }
    }

    private func path(in size: CGSize) -> Path {
        var path = Path()
        let values = points.map { metric == .cpu ? $0.cpu : $0.ram }
        let peak = max(values.max() ?? 100, 1)

        for (index, value) in values.enumerated() {
            let x = size.width * CGFloat(index) / CGFloat(values.count - 1)
            // Clamp so a 0-100 scale can never draw outside its own box.
            let normalised = min(max(CGFloat(value / peak), 0), 1)
            let y = size.height - (normalised * size.height)
            let point = CGPoint(x: x, y: y)
            index == 0 ? path.move(to: point) : path.addLine(to: point)
        }
        return path
    }
}

#Preview {
    VStack(spacing: 12) {
        FleetSparkline(
            points: (0..<24).map { _ in .init(cpu: Double.random(in: 5...95), ram: Double.random(in: 20...80)) },
            metric: .cpu
        )
        FleetSparkline(points: [], metric: .ram)
    }
    .padding()
}