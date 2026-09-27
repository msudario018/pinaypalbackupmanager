import ActivityKit
import SwiftUI
import WidgetKit

@main
struct PinayPalBackupWidgets: WidgetBundle {
    var body: some Widget {
        BackupLiveActivityWidget()
    }
}

struct BackupLiveActivityWidget: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: BackupActivityAttributes.self) { context in
            LockScreenLiveActivityView(context: context)
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    LiveActivityServiceBadge(
                        service: context.attributes.serviceName,
                        iconName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath",
                        isComplete: context.state.isComplete,
                        isFailed: isFailed(status: context.state.status),
                        size: 42
                    )
                }
                DynamicIslandExpandedRegion(.trailing) {
                    VStack(alignment: .trailing, spacing: 2) {
                        Text("\(Int(context.state.progress * 100))%")
                            .font(.system(size: 20, weight: .black, design: .rounded))
                            .monospacedDigit()
                            .foregroundStyle(
                                LinearGradient(
                                    colors: context.state.isComplete
                                        ? [Color.green, Color.mint]
                                        : [Color(red: 0.25, green: 0.85, blue: 1.0), Color.blue],
                                    startPoint: .topLeading,
                                    endPoint: .bottomTrailing
                                )
                            )
                        if let eta = context.state.etaText, !eta.isEmpty {
                            Text(eta)
                                .font(.system(size: 10, weight: .semibold).monospacedDigit())
                                .foregroundStyle(Color(white: 0.65))
                        }
                    }
                }
                DynamicIslandExpandedRegion(.center) {
                    VStack(alignment: .center, spacing: 2) {
                        HStack(spacing: 5) {
                            Text("PINAYPAL")
                                .font(.system(size: 10, weight: .black))
                                .tracking(0.6)
                                .foregroundStyle(Color(white: 0.55))
                            Text("•")
                                .font(.system(size: 9, weight: .bold))
                                .foregroundStyle(Color(white: 0.35))
                            Text(context.attributes.serviceName.uppercased())
                                .font(.system(size: 11, weight: .heavy))
                                .foregroundStyle(.cyan)
                        }
                        Text(context.state.message.isEmpty ? context.state.status : context.state.message)
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundStyle(.white)
                            .lineLimit(1)
                    }
                }
                DynamicIslandExpandedRegion(.bottom) {
                    VStack(spacing: 6) {
                        // Custom gradient progress bar
                        GeometryReader { geo in
                            ZStack(alignment: .leading) {
                                Capsule()
                                    .fill(Color.white.opacity(0.12))
                                    .frame(height: 6)
                                Capsule()
                                    .fill(
                                        LinearGradient(
                                            colors: context.state.isComplete
                                                ? [Color.green, Color.mint]
                                                : [Color.cyan, Color.blue, Color(red: 0.45, green: 0.35, blue: 0.95)],
                                            startPoint: .leading,
                                            endPoint: .trailing
                                        )
                                    )
                                    .frame(width: max(8, geo.size.width * CGFloat(min(1.0, max(0.0, context.state.progress)))), height: 6)
                                    .shadow(color: (context.state.isComplete ? Color.green : Color.cyan).opacity(0.6), radius: 3)
                            }
                        }
                        .frame(height: 6)

                        HStack {
                            Text(context.state.status)
                                .font(.system(size: 11, weight: .medium))
                                .foregroundStyle(Color(white: 0.65))
                            Spacer()
                            Text(context.state.speedText ?? (context.state.isComplete ? "Completed" : "Syncing"))
                                .font(.system(size: 11, weight: .semibold))
                                .foregroundStyle(.cyan)
                        }
                    }
                    .padding(.top, 4)
                }
            } compactLeading: {
                LiveActivityServiceBadge(
                    service: context.attributes.serviceName,
                    iconName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath",
                    isComplete: context.state.isComplete,
                    isFailed: isFailed(status: context.state.status),
                    size: 22
                )
            } compactTrailing: {
                Text("\(Int(context.state.progress * 100))%")
                    .font(.system(size: 12, weight: .black, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(
                        context.state.isComplete ? Color.green : Color(red: 0.25, green: 0.85, blue: 1.0)
                    )
            } minimal: {
                LiveActivityServiceBadge(
                    service: context.attributes.serviceName,
                    iconName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath",
                    isComplete: context.state.isComplete,
                    isFailed: isFailed(status: context.state.status),
                    size: 20
                )
            }
            .widgetURL(URL(string: "pinaypal://activity"))
            .keylineTint(.cyan)
        }
    }

    private func isFailed(status: String) -> Bool {
        let s = status.lowercased()
        return s.contains("fail") || s.contains("error") || s.contains("halt")
    }
}

// MARK: - Lock Screen Live Activity View
struct LockScreenLiveActivityView: View {
    let context: ActivityViewContext<BackupActivityAttributes>

    private var isFailed: Bool {
        let s = context.state.status.lowercased()
        return s.contains("fail") || s.contains("error") || s.contains("halt")
    }

    var body: some View {
        HStack(spacing: 14) {
            // High-end 3D service badge
            LiveActivityServiceBadge(
                service: context.attributes.serviceName,
                iconName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath",
                isComplete: context.state.isComplete,
                isFailed: isFailed,
                size: 50
            )

            // Central info
            VStack(alignment: .leading, spacing: 4) {
                // Header tags
                HStack(spacing: 6) {
                    Text("PINAYPAL")
                        .font(.system(size: 9.5, weight: .black))
                        .tracking(1.0)
                        .foregroundStyle(Color(white: 0.65))

                    Text("•")
                        .font(.system(size: 8, weight: .bold))
                        .foregroundStyle(Color(white: 0.40))

                    Text(context.attributes.serviceName.uppercased())
                        .font(.system(size: 10, weight: .heavy))
                        .foregroundStyle(Color(red: 0.35, green: 0.80, blue: 1.0))
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(
                            Capsule()
                                .fill(Color(red: 0.15, green: 0.45, blue: 0.85).opacity(0.28))
                                .overlay(
                                    Capsule().strokeBorder(Color(red: 0.35, green: 0.80, blue: 1.0).opacity(0.45), lineWidth: 0.8)
                                )
                        )

                    Spacer()

                    if !context.state.isComplete {
                        HStack(spacing: 4) {
                            Circle()
                                .fill(Color(red: 0.20, green: 0.90, blue: 0.95))
                                .frame(width: 5, height: 5)
                                .shadow(color: Color.cyan, radius: 2)
                            Text("LIVE")
                                .font(.system(size: 8.5, weight: .heavy))
                                .tracking(0.5)
                                .foregroundStyle(Color(red: 0.20, green: 0.90, blue: 0.95))
                        }
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(
                            Capsule().fill(Color.cyan.opacity(0.16))
                        )
                    }
                }

                // Main Status / Step Message
                Text(context.state.message.isEmpty ? context.state.status : context.state.message)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundStyle(.white)
                    .lineLimit(1)

                // Custom Gradient Progress Bar
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Capsule()
                            .fill(Color.white.opacity(0.12))
                            .frame(height: 6)

                        Capsule()
                            .fill(
                                LinearGradient(
                                    colors: context.state.isComplete
                                        ? [Color(red: 0.15, green: 0.85, blue: 0.45), Color(red: 0.35, green: 0.95, blue: 0.65)]
                                        : [Color(red: 0.20, green: 0.75, blue: 1.00), Color(red: 0.10, green: 0.50, blue: 0.95), Color(red: 0.55, green: 0.30, blue: 0.95)],
                                    startPoint: .leading,
                                    endPoint: .trailing
                                )
                            )
                            .frame(width: max(8, geo.size.width * CGFloat(min(1.0, max(0.0, context.state.progress)))), height: 6)
                            .shadow(color: (context.state.isComplete ? Color.green : Color.cyan).opacity(0.65), radius: 4)
                    }
                }
                .frame(height: 6)
                .padding(.vertical, 1)

                // Bottom Subtitle (Status & ETA/Speed)
                HStack {
                    Text(context.state.status)
                        .font(.system(size: 11, weight: .medium))
                        .foregroundStyle(Color(white: 0.60))

                    Spacer()

                    if let eta = context.state.etaText, !eta.isEmpty {
                        HStack(spacing: 3) {
                            Image(systemName: "clock.arrow.circlepath")
                                .font(.system(size: 9))
                            Text(eta)
                                .font(.system(size: 10, weight: .semibold).monospacedDigit())
                        }
                        .foregroundStyle(Color(white: 0.55))
                    } else if let speed = context.state.speedText, !speed.isEmpty {
                        Text(speed)
                            .font(.system(size: 10, weight: .semibold).monospacedDigit())
                            .foregroundStyle(Color(white: 0.55))
                    }
                }
            }

            // Percentage pill badge
            VStack {
                Text("\(Int(context.state.progress * 100))%")
                    .font(.system(size: 15, weight: .black, design: .rounded))
                    .monospacedDigit()
                    .foregroundStyle(
                        context.state.isComplete
                            ? Color(red: 0.25, green: 0.90, blue: 0.50)
                            : Color.white
                    )
                    .padding(.horizontal, 9)
                    .padding(.vertical, 5)
                    .background(
                        RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .fill(Color.white.opacity(0.08))
                            .overlay(
                                RoundedRectangle(cornerRadius: 10, style: .continuous)
                                    .strokeBorder(
                                        context.state.isComplete
                                            ? Color.green.opacity(0.4)
                                            : Color.white.opacity(0.15),
                                        lineWidth: 0.8
                                    )
                            )
                    )
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 13)
        .activityBackgroundTint(Color(red: 0.04, green: 0.07, blue: 0.15).opacity(0.92))
        .activitySystemActionForegroundColor(.white)
    }
}

// MARK: - 3D Beveled Live Activity Icon Badge
struct LiveActivityServiceBadge: View {
    let service: String
    let iconName: String
    let isComplete: Bool
    let isFailed: Bool
    var size: CGFloat = 46

    private var gradientColors: [Color] {
        if isComplete {
            return [Color(red: 0.18, green: 0.85, blue: 0.48), Color(red: 0.06, green: 0.60, blue: 0.32)]
        }
        if isFailed {
            return [Color(red: 0.98, green: 0.28, blue: 0.28), Color(red: 0.78, green: 0.10, blue: 0.16)]
        }
        let s = service.lowercased()
        if s.contains("sql") || s.contains("database") {
            // Amber Gold / Bronze gradient
            return [Color(red: 1.00, green: 0.72, blue: 0.20), Color(red: 0.88, green: 0.42, blue: 0.08)]
        }
        if s.contains("mailchimp") || s.contains("email") {
            // Electric Cyan to Royal Blue gradient
            return [Color(red: 0.22, green: 0.70, blue: 1.00), Color(red: 0.08, green: 0.38, blue: 0.92)]
        }
        if s.contains("ftp") || s.contains("website") {
            // Emerald Jade gradient
            return [Color(red: 0.16, green: 0.84, blue: 0.65), Color(red: 0.04, green: 0.56, blue: 0.42)]
        }
        // Brand Indigo/Cyan gradient
        return [Color(red: 0.30, green: 0.65, blue: 1.00), Color(red: 0.12, green: 0.35, blue: 0.88)]
    }

    var body: some View {
        ZStack {
            // Outer ambient glow
            RoundedRectangle(cornerRadius: size * 0.28, style: .continuous)
                .fill(LinearGradient(colors: gradientColors, startPoint: .topLeading, endPoint: .bottomTrailing))
                .blur(radius: size > 30 ? 5 : 2)
                .opacity(0.48)
                .frame(width: size, height: size)

            // Squircle body with 3D gradient
            RoundedRectangle(cornerRadius: size * 0.28, style: .continuous)
                .fill(LinearGradient(colors: gradientColors, startPoint: .topLeading, endPoint: .bottomTrailing))
                .frame(width: size, height: size)
                .overlay(
                    // Specular highlight stroke
                    RoundedRectangle(cornerRadius: size * 0.28, style: .continuous)
                        .strokeBorder(
                            LinearGradient(
                                colors: [Color.white.opacity(0.70), Color.white.opacity(0.15), Color.clear],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            ),
                            lineWidth: size > 30 ? 1.2 : 0.8
                        )
                )
                .shadow(color: .black.opacity(0.4), radius: 3, x: 0, y: 2)

            // Centered icon
            Image(systemName: iconName)
                .font(.system(size: size * 0.44, weight: .bold))
                .foregroundColor(.white)
                .shadow(color: .black.opacity(0.35), radius: 1.5, x: 0, y: 1)

            // Corner indicator dot/badge (for larger sizes)
            if size >= 36 {
                VStack {
                    Spacer()
                    HStack {
                        Spacer()
                        ZStack {
                            Circle()
                                .fill(Color.black.opacity(0.65))
                                .frame(width: size * 0.30, height: size * 0.30)
                            Circle()
                                .fill(isComplete ? Color.green : (isFailed ? Color.red : Color(red: 0.2, green: 0.9, blue: 1.0)))
                                .frame(width: size * 0.22, height: size * 0.22)
                                .shadow(color: (isComplete ? Color.green : Color.cyan).opacity(0.8), radius: 2)
                            if isComplete {
                                Image(systemName: "checkmark")
                                    .font(.system(size: size * 0.14, weight: .heavy))
                                    .foregroundColor(.white)
                            }
                        }
                        .offset(x: size * 0.06, y: size * 0.06)
                    }
                }
                .frame(width: size, height: size)
            }
        }
    }
}
