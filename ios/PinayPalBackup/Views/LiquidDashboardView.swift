import SwiftUI

public struct LiquidDashboardView: View {
    @Environment(\.colorScheme) var colorScheme
    @ObservedObject var api: PinayPalAPIService
    @Binding var showSettingsSheet: Bool

    @State private var triggeringService: String? = nil
    @State private var toastMessage: String? = nil

    public var body: some View {
        ZStack {
            // Ambient liquid background
            LiquidTheme.background(for: colorScheme).ignoresSafeArea()

            RadialGradient(
                colors: [LiquidTheme.gold.opacity(colorScheme == .light ? 0.06 : 0.12), Color.clear],
                center: .topTrailing,
                startRadius: 10,
                endRadius: 400
            )
            .ignoresSafeArea()

            ScrollView {
                VStack(spacing: 20) {
                    // Top App Bar
                    headerBar

                    // Real-time Active Backup Banner
                    if let active = api.status?.activeBackup, active.isBusy == true {
                        activeBackupBanner(active: active)
                    }

                    // Master Action Banner
                    masterActionBanner

                    // 4 Resource Metric Cards (Status, CPU, RAM, Disk)
                    metricsGrid

                    // Service Sync Cards (FTP, SQL, Mailchimp)
                    serviceCardsSection

                    // Storage Breakdown Visualizer
                    storageVisualizerCard

                    // All System Drives & Partitions
                    if let drives = api.status?.health?.drives, !drives.isEmpty {
                        drivesCard(drives: drives)
                    }

                    // Schedules Card
                    if let sched = api.status?.schedules {
                        schedulesCard(sched: sched)
                    }

                    // Live Activity Logs
                    liveLogsCard

                    Spacer().frame(height: 80)
                }
                .padding(.horizontal, 16)
                .padding(.top, 10)
            }
            .refreshable {
                let haptic = UIImpactFeedbackGenerator(style: .medium)
                haptic.impactOccurred()
                await api.fetchAll()
            }

            // In-App Toast
            if let toast = toastMessage {
                VStack {
                    Spacer()
                    HStack(spacing: 10) {
                        Image(systemName: "checkmark.circle.fill")
                            .foregroundColor(LiquidTheme.emerald)
                        Text(toast)
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(.white)
                    }
                    .padding(.horizontal, 18)
                    .padding(.vertical, 12)
                    .liquidGlassCard(cornerRadius: 30, glow: LiquidTheme.gold.opacity(0.4))
                    .padding(.bottom, 90)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                }
                .animation(.spring(), value: toastMessage)
            }
        }
    }

    // MARK: - Header Bar
    private var headerBar: some View {
        HStack {
            HStack(spacing: 8) {
                Image("AppLogo")
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(width: 24, height: 24)
                    .cornerRadius(6)

                Text("PinayPal")
                    .font(.system(size: 22, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.gold)

                Text(api.status?.version ?? "v3.3.1")
                    .font(.system(size: 10, weight: .bold))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Color.white.opacity(0.1))
                    .cornerRadius(4)
                    .foregroundColor(LiquidTheme.textSecondary)
            }

            Spacer()

            HStack(spacing: 8) {
                // Online Pill
                HStack(spacing: 5) {
                    Circle()
                        .fill(api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
                        .frame(width: 7, height: 7)
                        .shadow(color: api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral, radius: 4)

                    Text(api.isOnline ? "ONLINE" : "OFFLINE")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 4)
                .background(colorScheme == .light ? Color.black.opacity(0.06) : Color.black.opacity(0.3))
                .cornerRadius(12)

                // Settings Gear
                Button {
                    showSettingsSheet = true
                } label: {
                    Image(systemName: "gearshape.fill")
                        .font(.system(size: 16))
                        .foregroundColor(LiquidTheme.textSecondary)
                        .padding(8)
                        .background(Color.white.opacity(0.06))
                        .clipShape(Circle())
                }
            }
        }
        .padding(.vertical, 4)
    }

    // MARK: - Active Backup Banner
    private func activeBackupBanner(active: ActiveBackupSpec) -> some View {
        HStack(spacing: 12) {
            ZStack {
                Circle()
                    .fill(LiquidTheme.gold.opacity(0.2))
                    .frame(width: 36, height: 36)

                ProgressView()
                    .progressViewStyle(CircularProgressViewStyle(tint: LiquidTheme.gold))
                    .scaleEffect(0.85)
            }

            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text("BACKUP IN PROGRESS")
                        .font(.system(size: 11, weight: .black))
                        .foregroundColor(LiquidTheme.gold)

                    Text("• \((active.service ?? "Backup").uppercased())")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(.white)
                }

                Text(active.statusText ?? "Executing backup routine...")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(LiquidTheme.textSecondary)
                    .lineLimit(1)
            }

            Spacer()

            Button {
                Task {
                    _ = await api.triggerEmergencyStop()
                }
            } label: {
                Text("STOP")
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(LiquidTheme.coral)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(LiquidTheme.coral.opacity(0.15))
                    .cornerRadius(8)
            }
        }
        .padding(14)
        .liquidGlassCard(cornerRadius: 16, glow: LiquidTheme.gold.opacity(0.4), variant: .prominent)
        .transition(.scale.combined(with: .opacity))
    }

    // MARK: - 4 Top Metrics Grid
    private var metricsGrid: some View {
        LazyVGrid(columns: [GridItem(.flexible(), spacing: 14), GridItem(.flexible(), spacing: 14)], spacing: 14) {
            // 1. System Status
            let isHealthy = api.status?.health?.isHealthy ?? false
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("SYSTEM STATUS")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)
                    Spacer()
                    Image(systemName: isHealthy ? "checkmark.shield.fill" : "exclamationmark.shield.fill")
                        .foregroundColor(isHealthy ? LiquidTheme.emerald : LiquidTheme.coral)
                        .font(.system(size: 12))
                }

                Text(api.status?.health?.status ?? "Checking...")
                    .font(.system(size: 18, weight: .heavy, design: .rounded))
                    .foregroundColor(isHealthy ? LiquidTheme.emerald : LiquidTheme.coral)

                Text(api.status?.system?.hostname ?? "Localhost")
                    .font(.system(size: 11))
                    .foregroundColor(LiquidTheme.textSecondary)
                    .lineLimit(1)
            }
            .padding(14)
            .liquidGlassCard(cornerRadius: 16, glow: (isHealthy ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.15))

            // 2. CPU Usage
            let cpuPct = Int(api.status?.health?.cpu ?? 0)
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("CPU USAGE")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)
                    Spacer()
                    Text("\(api.status?.system?.cores ?? 0) Cores")
                        .font(.system(size: 9, weight: .bold))
                        .padding(.horizontal, 4)
                        .padding(.vertical, 1)
                        .background(Color.white.opacity(0.1))
                        .cornerRadius(4)
                        .foregroundColor(LiquidTheme.blue)
                }

                Text("\(cpuPct)%")
                    .font(.system(size: 20, weight: .heavy, design: .rounded))
                    .foregroundColor(LiquidTheme.textPrimary)

                ProgressView(value: Double(cpuPct), total: 100)
                    .tint(LiquidTheme.blue)
            }
            .padding(14)
            .liquidGlassCard(cornerRadius: 16, glow: LiquidTheme.blue.opacity(0.15))

            // 3. Memory Usage (Accurate Hardware RAM)
            let mem = api.status?.health?.memory
            let memPct = Int(mem?.percent ?? 0)
            let usedGb = String(format: "%.1f", Double(mem?.usedBytes ?? 0) / (1024*1024*1024))
            let totalGb = String(format: "%.1f", Double(mem?.totalBytes ?? 0) / (1024*1024*1024))
            let freeGb = String(format: "%.1f", Double(mem?.availableBytes ?? 0) / (1024*1024*1024))

            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("MEMORY (RAM)")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)
                    Spacer()
                    Text("🟢 \(freeGb)G Free")
                        .font(.system(size: 9, weight: .bold))
                        .foregroundColor(LiquidTheme.emerald)
                }

                Text("\(memPct)%")
                    .font(.system(size: 20, weight: .heavy, design: .rounded))
                    .foregroundColor(LiquidTheme.purple)

                Text("\(usedGb) GB / \(totalGb) GB")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(LiquidTheme.textSecondary)

                ProgressView(value: Double(memPct), total: 100)
                    .tint(LiquidTheme.purple)
            }
            .padding(14)
            .liquidGlassCard(cornerRadius: 16, glow: LiquidTheme.purple.opacity(0.15))

            // 4. Disk Usage
            let disk = api.status?.health?.disk
            let diskPct = Int(disk?.percent ?? 0)
            let freeDisk = disk?.availableGB ?? 0
            let totalDisk = disk?.totalGB ?? 0

            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("DISK USAGE")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)
                    Spacer()
                    Text(disk?.primaryDriveLetter ?? "Drive")
                        .font(.system(size: 9, weight: .bold))
                        .padding(.horizontal, 4)
                        .padding(.vertical, 1)
                        .background(Color.white.opacity(0.1))
                        .cornerRadius(4)
                        .foregroundColor(LiquidTheme.gold)
                }

                Text("\(diskPct)%")
                    .font(.system(size: 20, weight: .heavy, design: .rounded))
                    .foregroundColor(diskPct >= 90 ? LiquidTheme.coral : LiquidTheme.gold)

                Text("\(freeDisk) GB Free / \(totalDisk) GB")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(LiquidTheme.textSecondary)

                ProgressView(value: Double(diskPct), total: 100)
                    .tint(diskPct >= 90 ? LiquidTheme.coral : LiquidTheme.gold)
            }
            .padding(14)
            .liquidGlassCard(cornerRadius: 16, glow: LiquidTheme.gold.opacity(0.15))
        }
    }

    // MARK: - Service Cards Section
    private var serviceCardsSection: some View {
        VStack(spacing: 14) {
            // FTP Website
            serviceRow(
                title: "FTP Website Sync",
                icon: "globe",
                accent: LiquidTheme.emerald,
                meta: "Host: \(api.status?.services?.ftp?.host ?? "--") (Port \(api.status?.services?.ftp?.port ?? 21))",
                files: api.status?.services?.ftp?.fileCount ?? 0,
                bytes: api.status?.services?.ftp?.sizeBytes ?? 0,
                serviceKey: "ftp"
            )

            // SQL Database
            serviceRow(
                title: "SQL Database",
                icon: "cylinder.split.1x2",
                accent: LiquidTheme.gold,
                meta: "User: \(api.status?.services?.sql?.user ?? "--") | Remote Sync",
                files: api.status?.services?.sql?.fileCount ?? 0,
                bytes: api.status?.services?.sql?.sizeBytes ?? 0,
                serviceKey: "sql"
            )

            // Mailchimp
            serviceRow(
                title: "Mailchimp Sync",
                icon: "envelope.fill",
                accent: LiquidTheme.cyan,
                meta: "Audience ID: \(api.status?.services?.mailchimp?.audienceId ?? "Default")",
                files: api.status?.services?.mailchimp?.fileCount ?? 0,
                bytes: api.status?.services?.mailchimp?.sizeBytes ?? 0,
                serviceKey: "mailchimp"
            )
        }
    }

    private func serviceRow(title: String, icon: String, accent: Color, meta: String, files: Int, bytes: Int64, serviceKey: String) -> some View {
        let isTriggering = triggeringService == serviceKey
        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                HStack(spacing: 8) {
                    Image(systemName: icon)
                        .foregroundColor(accent)
                        .font(.system(size: 16, weight: .bold))

                    Text(title)
                        .font(.system(size: 15, weight: .bold, design: .rounded))
                        .foregroundColor(.white)
                }

                Spacer()

                Button {
                    triggerBackup(service: serviceKey)
                } label: {
                    HStack(spacing: 6) {
                        if isTriggering {
                            ProgressView().tint(.black).scaleEffect(0.8)
                        }
                        Text(isTriggering ? "Starting..." : "Backup")
                            .font(.system(size: 12, weight: .bold))
                    }
                    .padding(.horizontal, 14)
                    .padding(.vertical, 7)
                    .background(accent)
                    .foregroundColor(.black)
                    .cornerRadius(10)
                }
            }

            Text(meta)
                .font(.system(size: 12))
                .foregroundColor(LiquidTheme.textSecondary)

            HStack(spacing: 8) {
                Text("\(files) Files")
                    .font(.system(size: 10, weight: .bold))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color.white.opacity(0.06))
                    .cornerRadius(6)
                    .foregroundColor(LiquidTheme.textSecondary)

                Text(formatBytes(bytes))
                    .font(.system(size: 10, weight: .bold))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(accent.opacity(0.12))
                    .cornerRadius(6)
                    .foregroundColor(accent)
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18, glow: accent.opacity(0.12))
    }

    // MARK: - Drives Card
    private func drivesCard(drives: [DriveSpec]) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("💾 ALL PHYSICAL PARTITIONS")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary)

                Spacer()
                Text("\(drives.count) Drives Mounted")
                    .font(.system(size: 11))
                    .foregroundColor(LiquidTheme.gold)
            }

            VStack(spacing: 10) {
                ForEach(drives) { d in
                    let pct = Int(d.usedPercent ?? 0)
                    let free = String(format: "%.0f", Double(d.freeBytes ?? 0) / (1024*1024*1024))
                    let total = String(format: "%.0f", Double(d.totalBytes ?? 0) / (1024*1024*1024))

                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Text(d.name ?? "Drive")
                                .font(.system(size: 13, weight: .bold))
                                .foregroundColor(.white)

                            if d.isBackupDrive == true {
                                Text("BACKUP")
                                    .font(.system(size: 9, weight: .bold))
                                    .padding(.horizontal, 4)
                                    .padding(.vertical, 1)
                                    .background(LiquidTheme.gold.opacity(0.2))
                                    .cornerRadius(4)
                                    .foregroundColor(LiquidTheme.gold)
                            } else if d.isSystemDrive == true {
                                Text("SYSTEM")
                                    .font(.system(size: 9, weight: .bold))
                                    .padding(.horizontal, 4)
                                    .padding(.vertical, 1)
                                    .background(LiquidTheme.blue.opacity(0.2))
                                    .cornerRadius(4)
                                    .foregroundColor(LiquidTheme.blue)
                            }

                            Spacer()

                            Text("\(free) GB / \(total) GB (\(pct)%)")
                                .font(.system(size: 11))
                                .foregroundColor(LiquidTheme.textSecondary)
                        }

                        ProgressView(value: Double(pct), total: 100)
                            .tint(pct >= 90 ? LiquidTheme.coral : (pct >= 75 ? LiquidTheme.gold : LiquidTheme.emerald))
                    }
                }
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18)
    }

    // MARK: - Schedules Card
    private func schedulesCard(sched: ScheduleSpecs) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("⏰ AUTOMATED DAILY SCHEDULES")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary)

                Spacer()

                Button {
                    showSettingsSheet = true
                } label: {
                    HStack(spacing: 4) {
                        Image(systemName: "slider.horizontal.3")
                        Text("Manage")
                    }
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(LiquidTheme.gold)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(LiquidTheme.gold.opacity(0.12))
                    .cornerRadius(8)
                }
            }

            VStack(spacing: 8) {
                scheduleRow(title: "Website FTP", time: sched.ftpDaily ?? "10:00 PM MNL", interval: sched.ftpInterval ?? "3h")
                scheduleRow(title: "SQL Database", time: sched.sqlDaily ?? "5:00 PM MNL", interval: sched.sqlInterval ?? "2h 15m")
                scheduleRow(title: "Mailchimp", time: sched.mailchimpDaily ?? "6:00 PM MNL", interval: sched.mailchimpInterval ?? "2h")
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18)
    }

    private func scheduleRow(title: String, time: String, interval: String) -> some View {
        HStack {
            Text(title)
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(.white)
            Spacer()
            Text(time)
                .font(.system(size: 12, weight: .bold))
                .foregroundColor(LiquidTheme.gold)
            Text("(\(interval))")
                .font(.system(size: 11))
                .foregroundColor(LiquidTheme.textSecondary)
        }
    }

    // MARK: - Live Logs Card
    private var liveLogsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                HStack(spacing: 6) {
                    Circle()
                        .fill(LiquidTheme.emerald)
                        .frame(width: 7, height: 7)
                    Text("LIVE ACTIVITY STREAM")
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)
                }
                Spacer()
                Button("Clear") {
                    api.logs.removeAll()
                }
                .font(.system(size: 11))
                .foregroundColor(LiquidTheme.textSecondary)
            }

            VStack(alignment: .leading, spacing: 4) {
                ForEach(api.logs.suffix(6), id: \.self) { log in
                    Text(log)
                        .font(.system(size: 10, design: .monospaced))
                        .foregroundColor(logColor(log))
                        .lineLimit(2)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(10)
            .background(Color.black.opacity(0.45))
            .cornerRadius(10)
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 18)
    }

    private func logColor(_ log: String) -> Color {
        if log.contains("[ERROR]") || log.contains("FAIL") { return LiquidTheme.coral }
        if log.contains("[WARN]") || log.contains("CANCEL") { return LiquidTheme.gold }
        if log.contains("[SUCCESS]") || log.contains("COMPLETE") { return LiquidTheme.emerald }
        return LiquidTheme.blue
    }

    private func triggerBackup(service: String) {
        let impact = UIImpactFeedbackGenerator(style: .heavy)
        impact.impactOccurred()
        triggeringService = service
        Task {
            let ok = await api.triggerBackup(service: service)
            triggeringService = nil
            if ok {
                showToast("Backup started for \(service.uppercased())")
            } else {
                showToast("Failed to trigger \(service)")
            }
        }
    }

    private func showToast(_ msg: String) {
        toastMessage = msg
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
            if toastMessage == msg {
                toastMessage = nil
            }
        }
    }

    private func formatBytes(_ bytes: Int64) -> String {
        let k: Double = 1024
        let b = Double(bytes)
        if b < k { return "\(bytes) B" }
        if b < k * k { return String(format: "%.1f KB", b / k) }
        if b < k * k * k { return String(format: "%.1f MB", b / (k * k)) }
        return String(format: "%.2f GB", b / (k * k * k))
    }

    // MARK: - Master Action Banner
    private var masterActionBanner: some View {
        Button {
            let impact = UIImpactFeedbackGenerator(style: .heavy)
            impact.impactOccurred()
            triggerBackup(service: "all")
        } label: {
            HStack(spacing: 14) {
                ZStack {
                    Circle()
                        .fill(Color.black.opacity(0.2))
                        .frame(width: 44, height: 44)

                    Image(systemName: "bolt.shield.fill")
                        .font(.system(size: 22))
                        .foregroundColor(.black)
                }

                VStack(alignment: .leading, spacing: 2) {
                    Text("BACKUP ALL SERVICES")
                        .font(.system(size: 14, weight: .black, design: .rounded))
                        .foregroundColor(.black)

                    Text("Sequentially execute FTP, SQL, and Mailchimp")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(Color.black.opacity(0.75))
                }

                Spacer()

                if triggeringService == "all" {
                    ProgressView()
                        .progressViewStyle(CircularProgressViewStyle(tint: .black))
                } else {
                    Image(systemName: "arrow.right.circle.fill")
                        .font(.system(size: 22))
                        .foregroundColor(.black)
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)
            .background(
                LinearGradient(
                    colors: [LiquidTheme.gold, Color(red: 235/255, green: 145/255, blue: 0/255)],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
            )
            .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 10, y: 4)
        }
    }

    // MARK: - Storage Visualizer
    private var storageVisualizerCard: some View {
        let ftpSize = api.status?.services?.ftp?.sizeBytes ?? 0
        let sqlSize = api.status?.services?.sql?.sizeBytes ?? 0
        let mcSize = api.status?.services?.mailchimp?.sizeBytes ?? 0
        let totalBackups = ftpSize + sqlSize + mcSize

        return VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("STORAGE BREAKDOWN")
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary)
                Spacer()
                Text("Total: \(formatBytes(totalBackups))")
                    .font(.system(size: 12, weight: .heavy))
                    .foregroundColor(LiquidTheme.gold)
            }

            // Multi-segment bar
            GeometryReader { geo in
                let w = geo.size.width
                let ftpW = totalBackups > 0 ? CGFloat(ftpSize) / CGFloat(totalBackups) * w : 0
                let sqlW = totalBackups > 0 ? CGFloat(sqlSize) / CGFloat(totalBackups) * w : 0
                let mcW = totalBackups > 0 ? CGFloat(mcSize) / CGFloat(totalBackups) * w : 0

                HStack(spacing: 2) {
                    if ftpW > 0 {
                        RoundedRectangle(cornerRadius: 3)
                            .fill(LiquidTheme.emerald)
                            .frame(width: max(ftpW, 4))
                    }
                    if sqlW > 0 {
                        RoundedRectangle(cornerRadius: 3)
                            .fill(LiquidTheme.gold)
                            .frame(width: max(sqlW, 4))
                    }
                    if mcW > 0 {
                        RoundedRectangle(cornerRadius: 3)
                            .fill(LiquidTheme.cyan)
                            .frame(width: max(mcW, 4))
                    }
                    if totalBackups == 0 {
                        RoundedRectangle(cornerRadius: 3)
                            .fill(Color.white.opacity(0.1))
                            .frame(width: w)
                    }
                }
            }
            .frame(height: 8)

            // Legend
            HStack(spacing: 12) {
                legendItem(title: "FTP", bytes: ftpSize, color: LiquidTheme.emerald)
                legendItem(title: "SQL", bytes: sqlSize, color: LiquidTheme.gold)
                legendItem(title: "MC", bytes: mcSize, color: LiquidTheme.cyan)
            }
        }
        .padding(16)
        .liquidGlassCard(cornerRadius: 16)
    }

    private func legendItem(title: String, bytes: Int64, color: Color) -> some View {
        HStack(spacing: 5) {
            Circle()
                .fill(color)
                .frame(width: 7, height: 7)
            Text(title)
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(LiquidTheme.textSecondary)
            Text(formatBytes(bytes))
                .font(.system(size: 10, weight: .semibold))
                .foregroundColor(.white)
        }
    }
}
