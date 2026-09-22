import SwiftUI

public struct MainView: View {
    private enum AppTab: Hashable {
        case home, activity, history, logs
    }

    @StateObject private var api = PinayPalAPIService()
    @StateObject private var authManager = BiometricAuthManager()
    @State private var selectedTab: AppTab = .home
    @State private var showSettingsSheet = false
    @AppStorage("pp_theme_mode") private var themeMode: String = "dark"
    @Environment(\.colorScheme) private var systemColorScheme

    private var activeColorScheme: ColorScheme? {
        switch themeMode {
        case "light": return .light
        case "dark": return .dark
        default: return nil
        }
    }

    public var body: some View {
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
        .onReceive(NotificationService.shared.$navigationRequest) { request in
            guard let request else { return }
            selectedTab = request == .logs ? .logs : .activity
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
                LiveLogsView(api: api, showSettingsSheet: $showSettingsSheet)
            }
            .tag(AppTab.logs)
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
                Text(selectedTab == .home ? "PinayPal" : tabTitle)
                    .font(.system(size: 18, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.textPrimary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.82)
                Text(api.isOnline ? "Desktop connected" : "Desktop unavailable")
                    .font(.caption2.weight(.semibold))
                    .foregroundColor(api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
            }
            Spacer()
            Button {
                showSettingsSheet = true
            } label: {
                Image(systemName: "gearshape.fill")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(LiquidTheme.textPrimary)
                    .frame(width: 36, height: 36)
            }
            .background(Color.white.opacity(0.10), in: Circle())
        }
        .frame(maxWidth: .infinity)
        .padding(.horizontal, 14).padding(.vertical, 9)
        .liquidGlassBar()
        .padding(.horizontal, 10).padding(.top, 4)
    }

    private var liquidTabBar: some View {
        HStack(spacing: 4) {
            tabButton(.home, "Home", "house.fill")
            tabButton(.activity, "Activity", "waveform.path.ecg")
            tabButton(.history, "History", "clock.arrow.circlepath")
            tabButton(.logs, "Logs", "terminal.fill")
        }
        .padding(5)
        .liquidGlassBar()
        .padding(.horizontal, 10).padding(.bottom, 4)
    }

    private func tabButton(_ tab: AppTab, _ title: String, _ icon: String) -> some View {
        Button {
            withAnimation(.spring(response: 0.30, dampingFraction: 0.78)) { selectedTab = tab }
        } label: {
            VStack(spacing: 2) {
                Image(systemName: icon).font(.system(size: 14, weight: .bold))
                Text(title).font(.system(size: 9, weight: .bold)).lineLimit(1).minimumScaleFactor(0.75)
            }
            .foregroundColor(selectedTab == tab ? LiquidTheme.textPrimary : LiquidTheme.textSecondary)
            .frame(maxWidth: .infinity, minHeight: 45).padding(.vertical, 4)
            .background(selectedTab == tab ? LiquidTheme.gold.opacity(0.34) : .clear, in: Capsule())
        }
    }

    private var tabTitle: String {
        switch selectedTab {
        case .home: return "PinayPal"
        case .activity: return "Activity"
        case .history: return "History"
        case .logs: return "Live Logs"
        }
    }
}

private struct ActivityOverviewView: View {
    @ObservedObject var api: PinayPalAPIService
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("A clear timeline of protection activity, health, and recent results.")
                    .font(.subheadline).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

                activitySummary

                Text("Recent runs").font(.headline).foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                if api.history.isEmpty {
                    Label("No backup runs have been recorded yet.", systemImage: "clock.badge.questionmark")
                        .font(.caption).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        .padding(16).frame(maxWidth: .infinity, alignment: .leading)
                        .liquidGlassCard(cornerRadius: 16)
                } else {
                    ForEach(api.history.prefix(12)) { item in activityRow(item) }
                }
            }
            .padding(16).padding(.bottom, 90)
        }
        .background(LiquidTheme.background(for: colorScheme).ignoresSafeArea())
        .refreshable { await api.fetchAll() }
    }

    private var activitySummary: some View {
        HStack(spacing: 10) {
            activityMetric("Runs", value: "\(api.history.count)", icon: "checklist.checked", color: LiquidTheme.blue)
            activityMetric("Healthy", value: api.status?.health?.isHealthy == true ? "Yes" : "Check", icon: "heart.text.square.fill", color: api.status?.health?.isHealthy == true ? LiquidTheme.emerald : LiquidTheme.gold)
            activityMetric("Website", value: api.status?.website?.isOnline == true ? "Online" : "Check", icon: "network", color: api.status?.website?.isOnline == true ? LiquidTheme.emerald : LiquidTheme.coral)
        }
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
            VStack(alignment: .trailing, spacing: 3) {
                Text(success ? "Completed" : item.status).font(.caption.weight(.semibold)).foregroundColor(success ? LiquidTheme.emerald : LiquidTheme.coral)
                if let seconds = item.durationSeconds { Text(String(format: "%.1fs", seconds)).font(.caption2).foregroundColor(LiquidTheme.textSecondary(for: colorScheme)) }
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
