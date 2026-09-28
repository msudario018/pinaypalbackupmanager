import SwiftUI

public struct MainView: View {
    private enum AppTab: Hashable {
        case home, activity, history, automations
    }

    @StateObject private var api = PinayPalAPIService()
    @StateObject private var authManager = BiometricAuthManager()
    @State private var selectedTab: AppTab = .home
    @State private var showSettingsSheet = false
    @State private var showProfileSheet = false
    @State private var isShowingSplash = true
    @AppStorage("pp_theme_mode") private var themeMode: String = "dark"
    @Environment(\.colorScheme) private var systemColorScheme
    @Namespace private var tabNamespace

    private var activeColorScheme: ColorScheme? {
        switch themeMode {
        case "light": return .light
        case "dark": return .dark
        default: return nil
        }
    }

    private var routeChipLabel: String {
        if api.isUsingTailscale { return "Tailscale" }
        if api.isUsingFallback { return "Tunnel" }
        return "LAN"
    }

    private var routeChipColor: Color {
        if api.isUsingTailscale { return LiquidTheme.cyan }
        if api.isUsingFallback { return LiquidTheme.purple }
        return LiquidTheme.emerald
    }

    public var body: some View {
        ZStack {
            Group {
                if !api.isConfigured {
                    ConnectionSetupView()
                        .environmentObject(api)
                } else if !api.isLoggedIn {
                    LoginView()
                        .environmentObject(api)
                        .environmentObject(authManager)
                } else if !authManager.isUnlocked {
                    BiometricShieldView(authManager: authManager) { }
                } else {
                    appTabs
                }
            }
            .animation(.easeInOut(duration: 0.25), value: api.isConfigured)
            .animation(.easeInOut(duration: 0.25), value: api.isLoggedIn)
            .animation(.easeInOut(duration: 0.25), value: authManager.isUnlocked)
            .preferredColorScheme(activeColorScheme)
            .sheet(isPresented: $showSettingsSheet) {
                ServerConfigSheet(api: api, authManager: authManager)
            }
            .sheet(isPresented: $showProfileSheet) {
                ProfileSheetView(api: api, authManager: authManager)
            }

            if isShowingSplash {
                SplashScreenView()
                    .transition(.opacity)
                    .zIndex(100)
            }
        }
        .onAppear {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.8) {
                withAnimation(.easeOut(duration: 0.45)) {
                    isShowingSplash = false
                }
            }
        }
        .onReceive(NotificationService.shared.$navigationRequest) { request in
            guard let request else { return }
            selectedTab = request == .logs ? .automations : .activity
            NotificationService.shared.clearNavigationRequest()
        }
        .onReceive(NotificationService.shared.$retryService) { service in
            guard let service else { return }
            selectedTab = .activity
            Task {
                _ = await api.triggerBackup(service: service)
                NotificationService.shared.clearRetryRequest()
            }
        }
    }

    private var appTabs: some View {
        TabView(selection: $selectedTab) {
            NavigationStack {
                LiquidDashboardView(api: api, showSettingsSheet: $showSettingsSheet)
            }
            .tag(AppTab.home)

            NavigationStack {
                ActivityOverviewView(api: api)
            }
            .tag(AppTab.activity)

            NavigationStack {
                BackupHistoryView(api: api, showSettingsSheet: $showSettingsSheet)
            }
            .tag(AppTab.history)

            NavigationStack {
                AutomationsView(api: api)
            }
            .tag(AppTab.automations)
        }
        .toolbar(.hidden, for: .tabBar)
        .safeAreaInset(edge: .top, spacing: 0) { persistentHeader }
        .safeAreaInset(edge: .bottom, spacing: 0) { liquidTabBar }
        .tint(LiquidTheme.gold)
    }

    private var persistentHeader: some View {
        HStack(spacing: 10) {
            Image("AppLogo")
                .resizable().aspectRatio(contentMode: .fit)
                .frame(width: 28, height: 28).clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
            VStack(alignment: .leading, spacing: 1) {
                Text(selectedTab == .home ? "PinayPal Backup" : tabTitle)
                    .font(.system(size: 18, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.textPrimary(for: systemColorScheme))
                    .lineLimit(1)
                    .minimumScaleFactor(0.82)
                HStack(spacing: 4) {
                    Text(api.isOnline ? "Connected" : "Unavailable")
                        .font(.caption2.weight(.semibold))
                        .foregroundColor(api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
                    if api.isOnline, let ms = api.latencyMs {
                        Text("·")
                            .font(.caption2)
                            .foregroundColor(LiquidTheme.textSecondary(for: systemColorScheme))
                        Text("\(ms)ms")
                            .font(.system(size: 9, weight: .bold, design: .monospaced))
                            .foregroundColor(ms < 100 ? LiquidTheme.emerald : (ms < 500 ? LiquidTheme.gold : LiquidTheme.coral))
                    }
                    if api.isOnline {
                        Text("·")
                            .font(.caption2)
                            .foregroundColor(LiquidTheme.textSecondary(for: systemColorScheme))
                        Button {
                            Task { await api.toggleConnectionMode() }
                        } label: {
                            HStack(spacing: 3) {
                                Circle()
                                    .fill(routeChipColor)
                                    .frame(width: 5, height: 5)
                                Text(routeChipLabel)
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(routeChipColor)
                            }
                            .padding(.horizontal, 5)
                            .padding(.vertical, 2)
                            .background(routeChipColor.opacity(0.15))
                            .cornerRadius(4)
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            Spacer()
            HStack(spacing: 8) {
                Button {
                    showProfileSheet = true
                } label: {
                    ZStack {
                        Circle()
                            .fill(LiquidTheme.gold.opacity(0.18))
                            .frame(width: 36, height: 36)
                        AsyncImage(url: URL(string: "\(api.activeBaseUrl)/api/user/avatar")) { phase in
                            switch phase {
                            case .success(let img):
                                img.resizable().aspectRatio(contentMode: .fill)
                                    .frame(width: 36, height: 36)
                                    .clipShape(Circle())
                            default:
                                Image(systemName: "person.crop.circle.fill")
                                    .font(.system(size: 22))
                                    .foregroundColor(LiquidTheme.gold)
                            }
                        }
                    }
                    .overlay(
                        Circle()
                            .stroke(LiquidTheme.gold.opacity(0.4), lineWidth: 1)
                    )
                }

                Button {
                    showSettingsSheet = true
                } label: {
                    Image(systemName: "gearshape.fill")
                        .font(.system(size: 15, weight: .bold))
                        .foregroundColor(LiquidTheme.textPrimary(for: systemColorScheme))
                        .frame(width: 36, height: 36)
                }
                .background(Color.white.opacity(0.10), in: Circle())
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.horizontal, 14).padding(.vertical, 8)
        .liquidGlassNavigationIsland()
        .padding(.horizontal, 14).padding(.top, 4)
    }

    private var liquidTabBar: some View {
        HStack(spacing: 4) {
            tabButton(.home, "Home", "house.fill")
            tabButton(.activity, "Activity", "waveform.path.ecg")
            tabButton(.history, "History", "clock.arrow.circlepath")
            tabButton(.automations, "Automations", "bolt.shield.fill")
        }
        .padding(6)
        .liquidGlassNavigationIsland()
        .padding(.horizontal, 14)
        .padding(.bottom, 6)
    }

    private func tabButton(_ tab: AppTab, _ title: String, _ icon: String) -> some View {
        let isSelected = selectedTab == tab
        return Button {
            UIImpactFeedbackGenerator(style: .rigid).impactOccurred()
            withAnimation(.spring(response: 0.35, dampingFraction: 0.72)) {
                selectedTab = tab
            }
        } label: {
            VStack(spacing: 3) {
                Image(systemName: icon)
                    .font(.system(size: 15, weight: isSelected ? .bold : .semibold))
                    .foregroundColor(isSelected ? LiquidTheme.gold : LiquidTheme.textSecondary(for: systemColorScheme))
                    .scaleEffect(isSelected ? 1.12 : 1.0)
                    .animation(.spring(response: 0.30, dampingFraction: 0.65), value: isSelected)

                Text(title)
                    .font(.system(size: 10, weight: isSelected ? .bold : .medium))
                    .foregroundColor(isSelected ? LiquidTheme.textPrimary(for: systemColorScheme) : LiquidTheme.textSecondary(for: systemColorScheme))
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
            }
            .frame(maxWidth: .infinity, minHeight: 46)
            .padding(.vertical, 3)
            .background {
                if isSelected {
                    ZStack {
                        Capsule(style: .continuous)
                            .fill(
                                LinearGradient(
                                    stops: [
                                        .init(color: LiquidTheme.gold.opacity(0.42), location: 0.0),
                                        .init(color: LiquidTheme.gold.opacity(0.18), location: 1.0)
                                    ],
                                    startPoint: .topLeading,
                                    endPoint: .bottomTrailing
                                )
                            )
                        Capsule(style: .continuous)
                            .strokeBorder(
                                LinearGradient(
                                    stops: [
                                        .init(color: Color.white.opacity(0.70), location: 0.0),
                                        .init(color: Color.white.opacity(0.18), location: 0.40),
                                        .init(color: LiquidTheme.gold.opacity(0.45), location: 1.0)
                                    ],
                                    startPoint: .topLeading,
                                    endPoint: .bottomTrailing
                                ),
                                lineWidth: 1.0
                            )
                    }
                    .shadow(color: LiquidTheme.gold.opacity(0.28), radius: 8, x: 0, y: 2)
                    .matchedGeometryEffect(id: "liquid_active_tab_lens", in: tabNamespace)
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var tabTitle: String {
        switch selectedTab {
        case .home: return "PinayPal Backup"
        case .activity: return "Activity"
        case .history: return "History"
        case .automations: return "Automations"
        }
    }
}

private struct ActivityOverviewView: View {
    @ObservedObject var api: PinayPalAPIService
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Protection activity, health, and recent results.")
                    .font(.subheadline).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

                // Active Queue (shown when a backup is running)
                if let active = api.status?.activeBackup, active.isBusy == true {
                    activeQueueCard(active)
                }

                activitySummary

                // Date-grouped history
                if api.history.isEmpty {
                    Label("No backup runs have been recorded yet.", systemImage: "clock.badge.questionmark")
                        .font(.caption).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        .padding(16).frame(maxWidth: .infinity, alignment: .leading)
                        .liquidGlassCard(cornerRadius: 16)
                } else {
                    ForEach(groupedHistory, id: \.key) { group in
                        Text(group.key)
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                            .padding(.top, 4)
                        ForEach(group.items) { item in activityRow(item) }
                    }
                }
            }
            .padding(.horizontal, 16).padding(.top, 68).padding(.bottom, 90)
        }
        .background(LiquidTheme.background(for: colorScheme).ignoresSafeArea())
        .refreshable { await api.fetchAll() }
    }

    // MARK: - Active Queue Card
    private func activeQueueCard(_ active: ActiveBackupSpec) -> some View {
        HStack(spacing: 12) {
            ProgressView(value: Double(active.progress ?? 0), total: 100)
                .progressViewStyle(CircularProgressViewStyle(tint: LiquidTheme.gold))
                .frame(width: 36, height: 36)
            VStack(alignment: .leading, spacing: 3) {
                Text("\(active.service?.uppercased() ?? "BACKUP") IN PROGRESS")
                    .font(.system(size: 12, weight: .black))
                    .foregroundColor(LiquidTheme.gold)
                Text(active.statusText ?? "Running...")
                    .font(.caption)
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    .lineLimit(1)
            }
            Spacer()
            if let pct = active.progress {
                Text("\(pct)%")
                    .font(.system(size: 16, weight: .heavy, design: .monospaced))
                    .foregroundColor(LiquidTheme.gold)
            }
        }
        .padding(14)
        .liquidGlassCard(cornerRadius: 16, glow: LiquidTheme.gold.opacity(0.25))
    }

    // MARK: - Summary Metrics
    private var activitySummary: some View {
        HStack(spacing: 10) {
            activityMetric("Runs", value: "\(api.history.count)", icon: "checklist.checked", color: LiquidTheme.blue)
            let successRate = computeSuccessRate()
            activityMetric("7d Rate", value: successRate, icon: "chart.line.uptrend.xyaxis", color: successRate == "100%" ? LiquidTheme.emerald : LiquidTheme.gold)
            activityMetric("Healthy", value: api.status?.health?.isHealthy == true ? "Yes" : "Check", icon: "heart.text.square.fill", color: api.status?.health?.isHealthy == true ? LiquidTheme.emerald : LiquidTheme.gold)
        }
    }

    private func computeSuccessRate() -> String {
        let recent = api.history.prefix(20) // approximate last 7 days
        guard !recent.isEmpty else { return "--" }
        let successes = recent.filter { $0.status.localizedCaseInsensitiveContains("success") }.count
        let pct = Int(Double(successes) / Double(recent.count) * 100)
        return "\(pct)%"
    }

    private func activityMetric(_ title: String, value: String, icon: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Image(systemName: icon).foregroundColor(color)
            Text(value).font(.headline).foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
            Text(title).font(.caption2).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
        }
        .frame(maxWidth: .infinity, alignment: .leading).padding(12)
        .liquidGlassCard(cornerRadius: 16, glow: color.opacity(0.14))
    }

    // MARK: - Date Grouping
    private struct DateGroup: Identifiable {
        let key: String
        let items: [BackupHistoryItem]
        var id: String { key }
    }

    private var groupedHistory: [DateGroup] {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"

        let calendar = Calendar.current
        let today = calendar.startOfDay(for: Date())
        let yesterday = calendar.date(byAdding: .day, value: -1, to: today)!

        var groups: [String: [BackupHistoryItem]] = [:]
        var groupOrder: [String] = []

        for item in api.history.prefix(30) {
            let label: String
            if let date = formatter.date(from: String(item.time.prefix(19))) {
                let day = calendar.startOfDay(for: date)
                if day == today {
                    label = "Today"
                } else if day == yesterday {
                    label = "Yesterday"
                } else {
                    let display = DateFormatter()
                    display.dateFormat = "MMM d, yyyy"
                    label = display.string(from: date)
                }
            } else {
                label = "Earlier"
            }
            if groups[label] == nil { groupOrder.append(label) }
            groups[label, default: []].append(item)
        }

        return groupOrder.map { DateGroup(key: $0, items: groups[$0]!) }
    }

    // MARK: - Activity Row (with inline retry)
    private func activityRow(_ item: BackupHistoryItem) -> some View {
        let success = item.status.localizedCaseInsensitiveContains("success")
        return HStack(spacing: 12) {
            Image(systemName: success ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
                .foregroundColor(success ? LiquidTheme.emerald : LiquidTheme.coral)
            VStack(alignment: .leading, spacing: 3) {
                Text(item.service).font(.subheadline.weight(.bold)).foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Text(activitySubtitle(item)).font(.caption).foregroundColor(LiquidTheme.textSecondary(for: colorScheme)).lineLimit(1)
            }
            Spacer()
            if !success {
                Button {
                    Task { _ = await api.triggerBackup(service: item.service.lowercased()) }
                } label: {
                    Text("Retry")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(.black)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(LiquidTheme.gold, in: Capsule())
                }
            } else {
                VStack(alignment: .trailing, spacing: 3) {
                    Text("Completed").font(.caption.weight(.semibold)).foregroundColor(LiquidTheme.emerald)
                    if let seconds = item.durationSeconds { Text(String(format: "%.1fs", seconds)).font(.caption2).foregroundColor(LiquidTheme.textSecondary(for: colorScheme)) }
                }
            }
        }
        .padding(14).liquidGlassCard(cornerRadius: 16, glow: (success ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.10))
    }

    private func activitySubtitle(_ item: BackupHistoryItem) -> String {
        var details = [item.time]
        if let type = item.type, !type.isEmpty { details.append(type) }
        if let bytes = item.sizeBytes, bytes > 0 { details.append(byteString(bytes)) }
        return details.joined(separator: " - ")
    }

    private func byteString(_ bytes: Int64) -> String {
        let value = Double(bytes)
        if value < 1_048_576 { return String(format: "%.1f KB", value / 1_024) }
        if value < 1_073_741_824 { return String(format: "%.1f MB", value / 1_048_576) }
        return String(format: "%.2f GB", value / 1_073_741_824)
    }
}
