import SwiftUI

public struct ServiceDetailView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var colorScheme
    @ObservedObject var api: PinayPalAPIService
    let serviceKey: String
    let title: String
    let icon: String
    let accent: Color

    @State private var isStarting = false

    private var service: ServiceItem? {
        switch serviceKey {
        case "ftp": return api.status?.services?.ftp
        case "sql": return api.status?.services?.sql
        default: return api.status?.services?.mailchimp
        }
    }

    private var relatedHistory: [BackupHistoryItem] {
        api.history.filter { $0.service.localizedCaseInsensitiveContains(serviceKey) }
    }

    private var relatedLogs: [String] {
        api.logs.filter { $0.localizedCaseInsensitiveContains(serviceKey) }.suffix(40).map { $0 }
    }

    private var activeService: ActiveBackupServiceSpec? {
        api.status?.activeBackup?.activeServices?.first {
            $0.service?.localizedCaseInsensitiveContains(serviceKey) == true
        }
    }

    private var isServiceRunning: Bool {
        activeService != nil || api.status?.activeBackup?.service?.localizedCaseInsensitiveContains(serviceKey) == true
    }

    public var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    statusCard
                    actionsCard
                    detailsCard
                    logsCard
                    historyCard
                }
                .padding(16)
            }
            .background(LiquidTheme.background(for: colorScheme).ignoresSafeArea())
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .refreshable { await api.fetchAll() }
        }
    }

    private var statusCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                Image(systemName: icon)
                    .font(.system(size: 24, weight: .bold))
                    .foregroundColor(accent)
                    .frame(width: 42, height: 42)
                    .background(accent.opacity(0.14), in: RoundedRectangle(cornerRadius: 12, style: .continuous))

                VStack(alignment: .leading, spacing: 3) {
                    Text(service?.freshness?.badgeText ?? "No backup recorded")
                        .font(.headline)
                        .foregroundColor(freshnessColor)
                    Text(service?.freshness?.relativeTime ?? "Run an individual backup to establish a baseline.")
                        .font(.caption)
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                }
                Spacer()
                if isServiceRunning {
                    ProgressView().tint(accent)
                }
            }

            if isServiceRunning {
                ProgressView(value: Double(activeService?.progress ?? api.status?.activeBackup?.progress ?? 0), total: 100)
                    .tint(accent)
                Text(activeService?.statusText ?? api.status?.activeBackup?.statusText ?? "Backup is running")
                    .font(.caption)
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18, glow: accent.opacity(0.2))
    }

    private var actionsCard: some View {
        HStack(spacing: 12) {
            Button {
                startBackup()
            } label: {
                Label(isStarting ? "Starting…" : "Run (title)", systemImage: "play.fill")
                    .frame(maxWidth: .infinity)
            }
            .disabled(isStarting || api.status?.activeBackup?.isBusy == true)
            .buttonStyle(.borderedProminent)
            .tint(accent)

            if isServiceRunning {
                Button(role: .destructive) {
                    Task { _ = await api.triggerEmergencyStop() }
                } label: {
                    Label("Stop", systemImage: "stop.fill")
                }
                .buttonStyle(.bordered)
            }
        }
    }

    private var detailsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("Backup details", systemImage: "info.circle.fill")
                .font(.headline)
                .foregroundColor(accent)
            detailRow("Destination", service?.folder ?? "Not configured")
            detailRow("Files", "\(service?.fileCount ?? 0)")
            detailRow("Stored", formatBytes(service?.sizeBytes ?? 0))
            detailRow("Last backup", service?.freshness?.lastBackupTime ?? "Never")
            if serviceKey == "ftp" {
                detailRow("Server", "\(service?.host ?? "Not configured"):\(service?.port ?? 21)")
            } else if serviceKey == "sql" {
                detailRow("Database user", service?.user ?? "Not configured")
            } else {
                detailRow("Audience", service?.audienceId ?? "Not configured")
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18)
    }

    private var logsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Label("Service console", systemImage: "terminal.fill")
                    .font(.headline)
                    .foregroundColor(accent)
                Spacer()
                Text("(relatedLogs.count) entries")
                    .font(.caption)
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            Group {
                if relatedLogs.isEmpty {
                    Text("No (title) log entries are available yet.")
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                } else {
                    ForEach(relatedLogs, id: \.self) { entry in
                        Text(entry)
                            .font(.system(size: 11, design: .monospaced))
                            .foregroundColor(logColor(entry))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            }
            .padding(10)
            .background(Color.black.opacity(colorScheme == .light ? 0.78 : 0.46), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18)
    }

    private var historyCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("Recent runs", systemImage: "clock.arrow.circlepath")
                .font(.headline)
                .foregroundColor(accent)
            if relatedHistory.isEmpty {
                Text("No completed (title) runs yet.")
                    .font(.caption)
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            } else {
                ForEach(relatedHistory.prefix(5)) { item in
                    HStack {
                        Image(systemName: item.status.localizedCaseInsensitiveContains("success") ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                            .foregroundColor(item.status.localizedCaseInsensitiveContains("success") ? LiquidTheme.emerald : LiquidTheme.coral)
                        VStack(alignment: .leading) {
                            Text(item.status).font(.subheadline.weight(.semibold))
                            Text(item.time).font(.caption).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        }
                        Spacer()
                        Text(formatBytes(item.sizeBytes ?? 0)).font(.caption)
                    }
                }
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18)
    }

    private var freshnessColor: Color {
        service?.freshness?.isOutdated == true ? LiquidTheme.gold : LiquidTheme.emerald
    }

    private func startBackup() {
        isStarting = true
        Task {
            _ = await api.triggerBackup(service: serviceKey)
            isStarting = false
        }
    }

    private func detailRow(_ label: String, _ value: String) -> some View {
        HStack(alignment: .firstTextBaseline) {
            Text(label).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            Spacer()
            Text(value).multilineTextAlignment(.trailing)
        }
        .font(.caption)
    }

    private func formatBytes(_ bytes: Int64) -> String {
        let value = Double(bytes)
        if value < 1024 * 1024 { return String(format: "%.1f KB", value / 1024) }
        if value < 1024 * 1024 * 1024 { return String(format: "%.1f MB", value / 1024 / 1024) }
        return String(format: "%.2f GB", value / 1024 / 1024 / 1024)
    }

    private func logColor(_ entry: String) -> Color {
        if entry.localizedCaseInsensitiveContains("error") || entry.localizedCaseInsensitiveContains("fail") { return LiquidTheme.coral }
        if entry.localizedCaseInsensitiveContains("warn") { return LiquidTheme.gold }
        return LiquidTheme.emerald
    }
}
