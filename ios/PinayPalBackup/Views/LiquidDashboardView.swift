import SwiftUI

public struct LiquidDashboardView: View {
    @Environment(\.colorScheme) var colorScheme
    @ObservedObject var api: PinayPalAPIService
    @Binding var showSettingsSheet: Bool

    @State private var triggeringService: String? = nil
    @State private var toastMessage: String? = nil
    @State private var selectedService: ServiceDestination? = nil

    private enum ServiceDestination: Identifiable {
        case ftp, sql, mailchimp
        var id: String { key }
        var key: String {
            switch self { case .ftp: return "ftp"; case .sql: return "sql"; case .mailchimp: return "mailchimp" }
        }
        var title: String {
            switch self { case .ftp: return "FTP Website Sync"; case .sql: return "SQL Database"; case .mailchimp: return "Mailchimp Sync" }
        }
        var icon: String {
            switch self { case .ftp: return "globe"; case .sql: return "cylinder.split.1x2"; case .mailchimp: return "envelope.fill" }
        }
        var accent: Color {
            switch self { case .ftp: return LiquidTheme.emerald; case .sql: return LiquidTheme.gold; case .mailchimp: return LiquidTheme.cyan }
        }
    }

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
                    // Real-time Active Backup Banner
                    if let active = api.status?.activeBackup, active.isBusy == true {
                        activeBackupBanner(active: active)
                    } else {
                        // Last Backup Result Banner (shown when no backup is running)
                        lastBackupResultBanner
                    }

                    // Master Action Banner
                    masterActionBanner

                    // Freshness Summary Strip
                    freshnessSummaryStrip

                    // Fast service selection belongs on Home, directly below the main action.
                    backupCarouselSection

                    if let website = api.status?.website {
                        websiteStatusCard(website)
                    }

                    // 4 Resource Metric Cards (Status, CPU, RAM, Disk)
                    metricsGrid

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
                // The header is an overlay shared by every tab; reserve its full
                // footprint so the first card is never obscured beneath it.
                .padding(.top, 68)
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
        .sheet(item: $selectedService) { destination in
            ServiceDetailView(api: api, serviceKey: destination.key, title: destination.title, icon: destination.icon, accent: destination.accent)
        }
    }

    private func websiteStatusCard(_ website: WebsiteStatusSpec) -> some View {
        let online = website.isOnline == true
        return HStack(spacing: 12) {
            Image(systemName: online ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                .font(.system(size: 25, weight: .semibold))
                .foregroundColor(online ? LiquidTheme.emerald : LiquidTheme.coral)
            VStack(alignment: .leading, spacing: 3) {
                Text("PINAYPAL.NET")
                    .font(.system(size: 11, weight: .black))
                Text(websiteDetail(website, online: online))
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    .lineLimit(2)
            }
            Spacer()
            Text(online ? "ONLINE" : "OFFLINE")
                .font(.system(size: 10, weight: .black))
                .foregroundColor(online ? LiquidTheme.emerald : LiquidTheme.coral)
        }
        .padding(15)
        .liquidGlassCard(cornerRadius: 18, glow: (online ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.25))
    }

    private func websiteDetail(_ website: WebsiteStatusSpec, online: Bool) -> String {
        if online {
            return "Online - HTTP \(website.statusCode ?? 0) - \(website.responseTimeMs ?? 0) ms"
        }
        let failures = website.consecutiveFailures.map { " - \($0) failed checks" } ?? ""
        return "\(website.error ?? "Website is unavailable")\(failures)"
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

                    Text("- \((active.service ?? "Backup").uppercased())")
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

                if let uptime = api.status?.system?.appUptime, !uptime.isEmpty {
                    Text("⏱ \(uptime)")
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundColor(LiquidTheme.blue)
                }
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

    // MARK: - Backup Service Carousel
    private var backupCarouselSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Label("Backup services", systemImage: "square.stack.3d.up.fill")
                    .font(.system(size: 15, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Spacer()
                Text("Swipe to choose")
                    .font(.caption.weight(.semibold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            TabView {
                carouselServiceCard(title: "Website / FTP", icon: "globe.americas.fill", accent: LiquidTheme.emerald, meta: "\(api.status?.services?.ftp?.host ?? "Not configured") : \(api.status?.services?.ftp?.port ?? 21)", files: api.status?.services?.ftp?.fileCount ?? 0, bytes: api.status?.services?.ftp?.sizeBytes ?? 0, serviceKey: "ftp", destination: .ftp)
                carouselServiceCard(title: "SQL database", icon: "cylinder.split.1x2.fill", accent: LiquidTheme.purple, meta: api.status?.services?.sql?.user ?? "Not configured", files: api.status?.services?.sql?.fileCount ?? 0, bytes: api.status?.services?.sql?.sizeBytes ?? 0, serviceKey: "sql", destination: .sql)
                carouselServiceCard(title: "Mailchimp", icon: "envelope.badge.fill", accent: LiquidTheme.cyan, meta: "Audience: \(api.status?.services?.mailchimp?.audienceId ?? "Not configured")", files: api.status?.services?.mailchimp?.fileCount ?? 0, bytes: api.status?.services?.mailchimp?.sizeBytes ?? 0, serviceKey: "mailchimp", destination: .mailchimp)
            }
            .tabViewStyle(.page(indexDisplayMode: .automatic))
            .frame(height: 236)
        }
    }

    private func carouselServiceCard(title: String, icon: String, accent: Color, meta: String, files: Int, bytes: Int64, serviceKey: String, destination: ServiceDestination) -> some View {
        let isTriggering = triggeringService == serviceKey
        return VStack(alignment: .leading, spacing: 12) {
            Button { selectedService = destination } label: {
                HStack {
                    HStack(spacing: 8) {
                        Image(systemName: icon).foregroundColor(accent).font(.system(size: 18, weight: .bold))
                        Text(title).font(.system(size: 15, weight: .black, design: .rounded)).foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    }
                    Spacer()
                    Image(systemName: "chevron.right.circle.fill").foregroundColor(accent)
                }
            }
            .buttonStyle(.plain)
            Text(meta).font(.caption).foregroundColor(LiquidTheme.textSecondary(for: colorScheme)).lineLimit(1)
            HStack(spacing: 8) {
                Text("\(files) files").liquidPill(tint: LiquidTheme.textSecondary(for: colorScheme))
                Text(formatBytes(bytes)).liquidPill(tint: accent)
            }
            Button { triggerBackup(service: serviceKey) } label: {
                Label(isTriggering ? "Starting..." : "Quick run", systemImage: isTriggering ? "arrow.triangle.2.circlepath" : "play.fill")
                    .font(.caption.weight(.bold)).frame(maxWidth: .infinity, minHeight: 38)
            }
            .buttonStyle(.borderedProminent).tint(accent)
            .disabled(isTriggering || api.status?.activeBackup?.isBusy == true)
        }
        .frame(width: min(UIScreen.main.bounds.width - 64, 340), alignment: .leading).padding(16)
        .liquidGlassCard(cornerRadius: 20, glow: accent.opacity(0.20), variant: .prominent)
        .frame(maxWidth: .infinity, alignment: .center)
    }

    // MARK: - Legacy vertical service cards
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
                serviceKey: "ftp",
                destination: .ftp
            )

            // SQL Database
            serviceRow(
                title: "SQL Database",
                icon: "cylinder.split.1x2",
                accent: LiquidTheme.gold,
                meta: "User: \(api.status?.services?.sql?.user ?? "--") | Remote Sync",
                files: api.status?.services?.sql?.fileCount ?? 0,
                bytes: api.status?.services?.sql?.sizeBytes ?? 0,
                serviceKey: "sql",
                destination: .sql
            )

            // Mailchimp
            serviceRow(
                title: "Mailchimp Sync",
                icon: "envelope.fill",
                accent: LiquidTheme.cyan,
                meta: "Audience ID: \(api.status?.services?.mailchimp?.audienceId ?? "Default")",
                files: api.status?.services?.mailchimp?.fileCount ?? 0,
                bytes: api.status?.services?.mailchimp?.sizeBytes ?? 0,
                serviceKey: "mailchimp",
                destination: .mailchimp
            )
        }
    }

    private func serviceRow(title: String, icon: String, accent: Color, meta: String, files: Int, bytes: Int64, serviceKey: String, destination: ServiceDestination) -> some View {
        let isTriggering = triggeringService == serviceKey
        return VStack(alignment: .leading, spacing: 12) {
            Button {
                selectedService = destination
            } label: {
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
                Image(systemName: "chevron.right")
                    .font(.caption.weight(.bold))
                    .foregroundColor(LiquidTheme.textSecondary)
            }
            }
            .buttonStyle(.plain)

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

                Spacer()

                Button {
                    triggerBackup(service: serviceKey)
                } label: {
                    HStack(spacing: 5) {
                        if isTriggering { ProgressView().tint(.black).scaleEffect(0.7) }
                        Text(isTriggering ? "Starting…" : "Run backup")
                    }
                    .font(.system(size: 11, weight: .bold))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(accent, in: Capsule())
                    .foregroundColor(.black)
                }
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
        TimelineView(.periodic(from: .now, by: 60)) { timeline in
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
                    scheduleRow(title: "Website FTP", time: sched.ftpDaily ?? "10:00 PM MNL", interval: sched.ftpInterval ?? "3h", now: timeline.date)
                    scheduleRow(title: "SQL Database", time: sched.sqlDaily ?? "5:00 PM MNL", interval: sched.sqlInterval ?? "2h 15m", now: timeline.date)
                    scheduleRow(title: "Mailchimp", time: sched.mailchimpDaily ?? "6:00 PM MNL", interval: sched.mailchimpInterval ?? "2h", now: timeline.date)
                }
            }
            .padding(16)
            .liquidGlassCard(cornerRadius: 18)
        }
    }

    private func scheduleRow(title: String, time: String, interval: String, now: Date) -> some View {
        let countdown = countdownString(for: time, from: now)
        return HStack {
            Text(title)
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(.white)
            Spacer()
            if let countdown = countdown {
                Text(countdown)
                    .font(.system(size: 10, weight: .bold, design: .monospaced))
                    .foregroundColor(.black)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(LiquidTheme.gold, in: Capsule())
            }
            Text(time)
                .font(.system(size: 12, weight: .bold))
                .foregroundColor(LiquidTheme.gold)
            Text("(\(interval))")
                .font(.system(size: 11))
                .foregroundColor(LiquidTheme.textSecondary)
        }
    }

    /// Parse schedule time like "10:00 PM MNL" and compute countdown from `now`
    private func countdownString(for scheduleTime: String, from now: Date) -> String? {
        // Strip timezone suffix for parsing
        let cleaned = scheduleTime
            .replacingOccurrences(of: " MNL", with: "")
            .replacingOccurrences(of: " PHT", with: "")
            .trimmingCharacters(in: .whitespaces)

        let formatter = DateFormatter()
        formatter.dateFormat = "h:mm a"
        formatter.locale = Locale(identifier: "en_US_POSIX")
        // Manila is UTC+8
        formatter.timeZone = TimeZone(identifier: "Asia/Manila")

        guard let parsedTime = formatter.date(from: cleaned) else { return nil }

        // Build target date in Manila timezone for today
        let manila = TimeZone(identifier: "Asia/Manila")!
        var calendar = Calendar.current
        calendar.timeZone = manila

        let timeComponents = calendar.dateComponents([.hour, .minute], from: parsedTime)
        guard let hour = timeComponents.hour, let minute = timeComponents.minute else { return nil }

        var target = calendar.dateComponents([.year, .month, .day], from: now)
        target.hour = hour
        target.minute = minute
        target.second = 0

        guard var targetDate = calendar.date(from: target) else { return nil }

        // If the target time already passed today, it's tomorrow
        if targetDate <= now {
            targetDate = calendar.date(byAdding: .day, value: 1, to: targetDate) ?? targetDate
        }

        let diff = targetDate.timeIntervalSince(now)
        let totalMinutes = Int(diff) / 60
        let hours = totalMinutes / 60
        let minutes = totalMinutes % 60

        if hours > 0 {
            return "in \(hours)h \(minutes)m"
        } else {
            return "in \(minutes)m"
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

    // MARK: - Freshness Summary Strip
    private var freshnessSummaryStrip: some View {
        let services: [(String, ServiceItem?)] = [
            ("FTP", api.status?.services?.ftp),
            ("SQL", api.status?.services?.sql),
            ("Mailchimp", api.status?.services?.mailchimp)
        ]
        let updated = services.filter { $0.1?.freshness?.isOutdated == false && $0.1?.freshness?.status != "never" }.count
        let outdated = services.filter { $0.1?.freshness?.isOutdated == true }.count
        let never = services.filter { $0.1?.freshness?.status == "never" || $0.1?.freshness == nil }.count

        return HStack(spacing: 8) {
            if outdated > 0 {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundColor(LiquidTheme.gold)
                    .font(.system(size: 13))
                Text("\(outdated) Outdated")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.gold)
                Text("·")
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                Text("\(updated) Updated")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(LiquidTheme.emerald)
            } else if updated > 0 {
                Image(systemName: "checkmark.seal.fill")
                    .foregroundColor(LiquidTheme.emerald)
                    .font(.system(size: 13))
                Text("All Backups Fresh")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.emerald)
            } else {
                Image(systemName: "questionmark.circle")
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    .font(.system(size: 13))
                Text("No backups recorded")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            Spacer()
            if never > 0 {
                Text("\(never) pending")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .liquidGlassCard(cornerRadius: 12)
    }

    // MARK: - Last Backup Result Banner
    @ViewBuilder
    private var lastBackupResultBanner: some View {
        if let last = api.history.first {
            let success = last.status.localizedCaseInsensitiveContains("success")
            HStack(spacing: 10) {
                Image(systemName: success ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    .foregroundColor(success ? LiquidTheme.emerald : LiquidTheme.coral)
                    .font(.system(size: 16))

                VStack(alignment: .leading, spacing: 2) {
                    Text("\(last.service.uppercased()) \(success ? "completed" : last.status)")
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    HStack(spacing: 6) {
                        Text(last.time)
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        if let bytes = last.sizeBytes, bytes > 0 {
                            Text("· \(formatBytes(bytes))")
                                .font(.system(size: 10))
                                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        }
                        if let dur = last.durationSeconds, dur > 0 {
                            Text("· \(String(format: "%.1fs", dur))")
                                .font(.system(size: 10))
                                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        }
                    }
                }

                Spacer()

                if !success {
                    Button {
                        triggerBackup(service: last.service.lowercased())
                    } label: {
                        Text("Retry")
                            .font(.system(size: 11, weight: .bold))
                            .foregroundColor(.black)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 5)
                            .background(LiquidTheme.gold, in: Capsule())
                    }
                }
            }
            .padding(12)
            .liquidGlassCard(cornerRadius: 14, glow: (success ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.15))
        }
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
