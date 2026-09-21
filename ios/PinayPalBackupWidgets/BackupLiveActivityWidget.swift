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
                Image(systemName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath")
                    .font(.title2)
                    .foregroundStyle(.tint)
                VStack(alignment: .leading, spacing: 4) {
                    Text(context.attributes.serviceName)
                        .font(.headline)
                    Text(context.state.status)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    ProgressView(value: context.state.progress)
                }
                Spacer()
                Text("\(Int(context.state.progress * 100))%")
                    .font(.headline.monospacedDigit())
            }
            .padding()
            .activityBackgroundTint(Color.black.opacity(0.16))
            .activitySystemActionForegroundColor(.white)
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Image(systemName: context.state.serviceIcon ?? "arrow.triangle.2.circlepath")
                        .foregroundStyle(.tint)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    Text("\(Int(context.state.progress * 100))%")
                        .font(.headline.monospacedDigit())
                }
                DynamicIslandExpandedRegion(.center) {
                    Text(context.attributes.serviceName)
                        .font(.headline)
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
