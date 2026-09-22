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
            HStack(spacing: 12) {
                ZStack {
                    Circle().fill(Color.accentColor.opacity(0.20))
                    Image(systemName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath")
                        .font(.title3.weight(.bold))
                        .foregroundStyle(.tint)
                }
                .frame(width: 40, height: 40)
                VStack(alignment: .leading, spacing: 4) {
                    Text("PINAYPAL BACKUP")
                        .font(.caption2.weight(.bold))
                        .foregroundStyle(.secondary)
                    Text(context.attributes.serviceName)
                        .font(.headline.weight(.bold))
                    Text(context.state.status)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    ProgressView(value: context.state.progress)
                }
                Spacer()
                Text("\(Int(context.state.progress * 100))%")
                    .font(.headline.monospacedDigit())
            }
            .padding(14)
            .activityBackgroundTint(Color(red: 0.03, green: 0.06, blue: 0.13).opacity(0.88))
            .activitySystemActionForegroundColor(.white)
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Image(systemName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath")
                        .font(.title3.weight(.bold))
                        .foregroundStyle(.tint)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    Text("\(Int(context.state.progress * 100))%")
                        .font(.headline.monospacedDigit())
                }
                DynamicIslandExpandedRegion(.center) {
                    VStack(spacing: 1) {
                        Text("PINAYPAL").font(.caption2.weight(.bold)).foregroundStyle(.secondary)
                        Text(context.attributes.serviceName).font(.headline.weight(.bold))
                    }
                }
                DynamicIslandExpandedRegion(.bottom) {
                    VStack(spacing: 5) {
                        ProgressView(value: context.state.progress)
                        HStack {
                            Text(context.state.status)
                            Spacer()
                            Text(context.state.etaText ?? context.state.speedText ?? "Working")
                        }
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    }
                }
            } compactLeading: {
                Image(systemName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath")
            } compactTrailing: {
                Text("\(Int(context.state.progress * 100))%")
                    .monospacedDigit()
            } minimal: {
                Image(systemName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath")
            }
            .widgetURL(URL(string: "pinaypal://activity"))
            .keylineTint(.accentColor)
        }
    }
}
