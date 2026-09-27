import SwiftUI

public struct AutomationsView: View {
    @ObservedObject var api: PinayPalAPIService
    @Environment(\.colorScheme) private var colorScheme

    @State private var isRunningSyncCheck = false
    @State private var syncCheckMessage: String? = nil
    @State private var triggeringService: String? = nil
    @State private var showEmergencyAlert = false

    public init(api: PinayPalAPIService) {
        self.api = api
    }

    public var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                // Header description
                VStack(alignment: .leading, spacing: 4) {
                    Text("Automations & Schedules")
                        .font(.system(size: 22, weight: .black, design: .rounded))
                        .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    Text("Monitor automated routines, countdown clocks, and remote sync verification.")
                        .font(.subheadline)
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.top, 4)

                // Remote Sync Verification Card
                syncCheckCard

                // Countdown Schedules Section
                schedulesSection

                // Quick Automation Triggers
                quickTriggersSection

                // Auto-Scan & Maintenance
                maintenanceCard

                Spacer().frame(height: 80)
            }
            .padding(.horizontal, 16)
            .padding(.top, 68)
        }
        .background(LiquidTheme.background(for: colorScheme).ignoresSafeArea())
        .refreshable {
            let haptic = UIImpactFeedbackGenerator(style: .medium)
            haptic.impactOccurred()
            await api.fetchAll()
        }
        .alert("Emergency Stop All Backups?", isPresented: $showEmergencyAlert) {
            Button("Stop Now", role: .destructive) {
                Task { _ = await api.triggerEmergencyStop() }
            }
            Button("Cancel", role: .cancel) { }
        } message: {
            Text("This immediately halts any running or queued backup processes on your desktop PC.")
        }
    }

    // MARK: - Sync Check Card
    private var syncCheckCard: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                ZStack {
                    Circle()
                        .fill(LiquidTheme.blue.opacity(0.18))
                        .frame(width: 38, height: 38)
                    Image(systemName: "arrow.triangle.2.circlepath.circle.fill")
                        .font(.system(size: 20))
                        .foregroundColor(LiquidTheme.blue)
                }

                VStack(alignment: .leading, spacing: 2) {
                    Text("REMOTE SYNC VERIFICATION")
                        .font(.system(size: 11, weight: .black))
                        .foregroundColor(LiquidTheme.blue)
                    Text("Compares local backups with remote FTP & SQL servers")
                        .font(.system(size: 12))
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                }

                Spacer()

                if api.outdatedCount > 0 {
                    Text("\(api.outdatedCount) OUTDATED")
                        .font(.system(size: 10, weight: .black))
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(LiquidTheme.coral.opacity(0.18))
                        .foregroundColor(LiquidTheme.coral)
                        .cornerRadius(6)
                } else {
                    Text("SYNCED")
                        .font(.system(size: 10, weight: .black))
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(LiquidTheme.emerald.opacity(0.18))
                        .foregroundColor(LiquidTheme.emerald)
                        .cornerRadius(6)
                }
            }

            if let msg = syncCheckMessage {
                Text(msg)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    .padding(10)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(LiquidTheme.card(for: colorScheme))
                    .cornerRadius(8)
            }

            // Sync Status Rows for FTP, SQL, Mailchimp
            VStack(spacing: 8) {
                syncRow(
                    name: "Website FTP",
                    icon: "globe",
                    color: LiquidTheme.emerald,
                    freshness: api.status?.services?.ftp?.freshness
                )
                syncRow(
                    name: "SQL Database",
                    icon: "cylinder.split.1x2",
                    color: LiquidTheme.gold,
                    freshness: api.status?.services?.sql?.freshness
                )
                syncRow(
                    name: "Mailchimp Audience",
                    icon: "envelope.fill",
                    color: LiquidTheme.cyan,
                    freshness: api.status?.services?.mailchimp?.freshness
                )
            }

            // Trigger Button
            Button {
                triggerSyncCheck()
            } label: {
                HStack(spacing: 8) {
                    if isRunningSyncCheck {
                        ProgressView()
                            .tint(.white)
                            .scaleEffect(0.8)
                    } else {
                        Image(systemName: "arrow.clockwise")
                            .font(.system(size: 13, weight: .bold))
                    }
                    Text(isRunningSyncCheck ? "Verifying Remote Files..." : "Run Remote Sync Check")
                        .font(.system(size: 13, weight: .bold))
                }
                .foregroundColor(.white)
                .frame(maxWidth: .infinity, minHeight: 42)
                .background(LiquidTheme.blue, in: RoundedRectangle(cornerRadius: 12))
            }
            .disabled(isRunningSyncCheck)
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18, glow: LiquidTheme.blue.opacity(0.20))
    }

    private func syncRow(name: String, icon: String, color: Color, freshness: ServiceFreshnessSpec?) -> some View {
        HStack(spacing: 10) {
            Image(systemName: icon)
                .font(.system(size: 14))
                .foregroundColor(color)
                .frame(width: 20)

            Text(name)
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

            Spacer()

            if let f = freshness {
                if f.isOutdated == true {
                    HStack(spacing: 4) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .font(.system(size: 10))
                        Text(f.relativeTime ?? "Outdated")
                            .font(.system(size: 11, weight: .bold))
                    }
                    .foregroundColor(LiquidTheme.coral)
                } else if f.status == "fresh" {
                    HStack(spacing: 4) {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 10))
                        Text(f.relativeTime ?? "Fresh")
                            .font(.system(size: 11, weight: .bold))
                    }
                    .foregroundColor(LiquidTheme.emerald)
                } else {
                    Text("Pending")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                }
            } else {
                Text("--")
                    .font(.system(size: 11))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
        }
        .padding(.vertical, 4)
    }

    // MARK: - Schedules Section
    private var schedulesSection: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Label("Daily Scheduled Times", systemImage: "clock.badge.checkmark.fill")
                    .font(.system(size: 15, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Spacer()
                Text("MNL Time (UTC+8)")
                    .font(.caption.weight(.semibold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }

            TimelineView(.periodic(from: .now, by: 1.0)) { timeline in
                let sched = api.status?.schedules
                VStack(spacing: 12) {
                    countdownScheduleRow(
                        service: "Website FTP",
                        icon: "globe",
                        color: LiquidTheme.emerald,
                        time: sched?.ftpDaily ?? "10:00 PM MNL",
                        interval: sched?.ftpInterval ?? "3h",
                        now: timeline.date
                    )

                    countdownScheduleRow(
                        service: "SQL Database",
                        icon: "cylinder.split.1x2",
                        color: LiquidTheme.gold,
                        time: sched?.sqlDaily ?? "5:00 PM MNL",
                        interval: sched?.sqlInterval ?? "2h 15m",
                        now: timeline.date
                    )

                    countdownScheduleRow(
                        service: "Mailchimp Sync",
                        icon: "envelope.fill",
                        color: LiquidTheme.cyan,
                        time: sched?.mailchimpDaily ?? "6:00 PM MNL",
                        interval: sched?.mailchimpInterval ?? "2h",
                        now: timeline.date
                    )

                    countdownScheduleRow(
                        service: "System Health Scan",
                        icon: "waveform.path.ecg",
                        color: LiquidTheme.purple,
                        time: sched?.healthDaily ?? "8:00 AM MNL",
                        interval: "Daily",
                        now: timeline.date
                    )
                }
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18)
    }

    private func countdownScheduleRow(service: String, icon: String, color: Color, time: String, interval: String, now: Date) -> some View {
        let countdown = countdownString(for: time, from: now)
        return HStack(spacing: 10) {
            ZStack {
                Circle()
                    .fill(color.opacity(0.18))
                    .frame(width: 32, height: 32)
                Image(systemName: icon)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(color)
            }

            VStack(alignment: .leading, spacing: 2) {
                Text(service)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Text("Interval: \(interval)")
                    .font(.system(size: 11))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }

            Spacer()

            VStack(alignment: .trailing, spacing: 2) {
                Text(time)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(color)

                if let cd = countdown {
                    Text("in \(cd)")
                        .font(.system(size: 10, weight: .bold, design: .monospaced))
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(color.opacity(0.20))
                        .foregroundColor(color)
                        .cornerRadius(4)
                }
            }
        }
        .padding(.vertical, 2)
    }

    // MARK: - Quick Automation Triggers
    private var quickTriggersSection: some View {
        VStack(alignment: .leading, spacing: 14) {
            Label("Instant Automation Triggers", systemImage: "bolt.fill")
                .font(.system(size: 15, weight: .black, design: .rounded))
                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

            LazyVGrid(columns: [GridItem(.flexible(), spacing: 12), GridItem(.flexible(), spacing: 12)], spacing: 12) {
                quickActionButton(
                    title: "Sync FTP Now",
                    subtitle: "Remote file check",
                    icon: "globe",
                    color: LiquidTheme.emerald,
                    key: "ftp"
                )

                quickActionButton(
                    title: "Backup SQL",
                    subtitle: "Dump database",
                    icon: "cylinder.split.1x2",
                    color: LiquidTheme.gold,
                    key: "sql"
                )

                quickActionButton(
                    title: "Sync Mailchimp",
                    subtitle: "Audience export",
                    icon: "envelope.fill",
                    color: LiquidTheme.cyan,
                    key: "mailchimp"
                )

                Button {
                    showEmergencyAlert = true
                } label: {
                    VStack(alignment: .leading, spacing: 6) {
                        Image(systemName: "xmark.octagon.fill")
                            .font(.system(size: 20))
                            .foregroundColor(LiquidTheme.coral)
                        Text("Emergency Stop")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(LiquidTheme.coral)
                        Text("Halt desktop PC tasks")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(14)
                    .liquidGlassCard(cornerRadius: 14, glow: LiquidTheme.coral.opacity(0.18))
                }
                .buttonStyle(.plain)
            }
        }
    }

    private func quickActionButton(title: String, subtitle: String, icon: String, color: Color, key: String) -> some View {
        let isRunning = triggeringService == key
        return Button {
            triggerBackup(key)
        } label: {
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Image(systemName: icon)
                        .font(.system(size: 18))
                        .foregroundColor(color)
                    Spacer()
                    if isRunning {
                        ProgressView()
                            .scaleEffect(0.7)
                    }
                }

                Text(title)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

                Text(isRunning ? "Running..." : subtitle)
                    .font(.system(size: 10))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(14)
            .liquidGlassCard(cornerRadius: 14, glow: color.opacity(0.15))
        }
        .buttonStyle(.plain)
        .disabled(isRunning || api.status?.activeBackup?.isBusy == true)
    }

    // MARK: - Maintenance & Cleanup
    private var maintenanceCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            Label("Automated Maintenance Policies", systemImage: "gearshape.2.fill")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

            HStack {
                Text("Rolling Archive Retention")
                    .font(.system(size: 12))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                Spacer()
                Text("7 Days Auto-Purge")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.emerald)
            }

            Divider()

            HStack {
                Text("Outdated Backup Alerts")
                    .font(.system(size: 12))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                Spacer()
                Text(api.outdatedCount > 0 ? "\(api.outdatedCount) Warning Active" : "All Up To Date")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(api.outdatedCount > 0 ? LiquidTheme.coral : LiquidTheme.emerald)
            }

            Divider()

            HStack {
                Text("Windows Desktop Auto-Start")
                    .font(.system(size: 12))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                Spacer()
                Text("Enabled (Background Service)")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.blue)
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 16)
    }

    // MARK: - Actions & Helpers
    private func triggerSyncCheck() {
        isRunningSyncCheck = true
        let haptic = UIImpactFeedbackGenerator(style: .medium)
        haptic.impactOccurred()

        Task {
            let res = await api.triggerSyncCheck()
            await MainActor.run {
                isRunningSyncCheck = false
                if res.success {
                    syncCheckMessage = res.message
                } else {
                    syncCheckMessage = "Sync check failed: \(res.message)"
                }
            }
            await api.fetchAll()
        }
    }

    private func triggerBackup(_ service: String) {
        triggeringService = service
        let haptic = UIImpactFeedbackGenerator(style: .medium)
        haptic.impactOccurred()

        Task {
            _ = await api.triggerBackup(service: service)
            await MainActor.run {
                triggeringService = nil
            }
            await api.fetchAll()
        }
    }

    private func countdownString(for rawTime: String, from now: Date) -> String? {
        let clean = rawTime
            .replacingOccurrences(of: "MNL", with: "")
            .replacingOccurrences(of: "PHT", with: "")
            .trimmingCharacters(in: .whitespaces)

        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(identifier: "Asia/Manila") ?? TimeZone(secondsFromGMT: 8 * 3600)!

        for fmt in ["h:mm a", "hh:mm a", "H:mm", "HH:mm"] {
            formatter.dateFormat = fmt
            if let parsed = formatter.date(from: clean) {
                var cal = Calendar(identifier: .gregorian)
                cal.timeZone = formatter.timeZone
                let nowComps = cal.dateComponents([.year, .month, .day], from: now)
                let targetComps = cal.dateComponents([.hour, .minute], from: parsed)

                var combined = DateComponents()
                combined.year = nowComps.year
                combined.month = nowComps.month
                combined.day = nowComps.day
                combined.hour = targetComps.hour
                combined.minute = targetComps.minute
                combined.second = 0

                guard var targetDate = cal.date(from: combined) else { continue }
                if targetDate < now {
                    targetDate = cal.date(byAdding: .day, value: 1, to: targetDate) ?? targetDate
                }

                let diffSec = Int(targetDate.timeIntervalSince(now))
                let hours = diffSec / 3600
                let mins = (diffSec % 3600) / 60
                let secs = diffSec % 60
                return String(format: "%02dh %02dm %02ds", hours, mins, secs)
            }
        }
        return nil
    }
}
