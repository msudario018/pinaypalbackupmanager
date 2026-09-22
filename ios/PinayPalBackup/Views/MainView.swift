import SwiftUI

public struct MainView: View {
    private enum AppTab: Hashable {
        case home, backups, history, logs
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
            selectedTab = request == .logs ? .logs : .backups
            NotificationService.shared.clearNavigationRequest()
        }
        .onReceive(NotificationService.shared.$retryService) { service in
            guard let service else { return }
            selectedTab = .backups
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
                BackupsHubView(api: api)
            }
            .tag(AppTab.backups)

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
        .padding(.horizontal, 18).padding(.vertical, 10)
        .liquidGlassBar()
        .padding(.horizontal, 12).padding(.top, 6)
    }

    private var liquidTabBar: some View {
        HStack(spacing: 4) {
            tabButton(.home, "Home", "house.fill")
            tabButton(.backups, "Backups", "externaldrive.fill")
            tabButton(.history, "History", "clock.arrow.circlepath")
            tabButton(.logs, "Logs", "terminal.fill")
        }
        .padding(6)
        .liquidGlassBar()
        .padding(.horizontal, 12).padding(.bottom, 6)
    }

    private func tabButton(_ tab: AppTab, _ title: String, _ icon: String) -> some View {
        Button {
            withAnimation(.spring(response: 0.30, dampingFraction: 0.78)) { selectedTab = tab }
        } label: {
            VStack(spacing: 3) {
                Image(systemName: icon).font(.system(size: 15, weight: .bold))
                Text(title).font(.system(size: 10, weight: .bold))
            }
            .foregroundColor(selectedTab == tab ? LiquidTheme.textPrimary : LiquidTheme.textSecondary)
            .frame(maxWidth: .infinity).padding(.vertical, 7)
            .background(selectedTab == tab ? LiquidTheme.gold.opacity(0.34) : .clear, in: Capsule())
        }
    }

    private var tabTitle: String {
        switch selectedTab {
        case .home: return "PinayPal"
        case .backups: return "Backups"
        case .history: return "History"
        case .logs: return "Live Logs"
        }
    }
}

private struct BackupsHubView: View {
    @ObservedObject var api: PinayPalAPIService
    @Environment(\.colorScheme) private var colorScheme
    @State private var selectedService: BackupService?
    @State private var expandedService: BackupService?

    private enum BackupService: String, Identifiable, CaseIterable {
        case ftp, sql, mailchimp
        var id: String { rawValue }
        var title: String { self == .ftp ? "Website / FTP" : self == .sql ? "SQL Database" : "Mailchimp" }
        var icon: String { self == .ftp ? "globe" : self == .sql ? "cylinder.split.1x2" : "envelope.fill" }
        var color: Color { self == .ftp ? LiquidTheme.emerald : self == .sql ? LiquidTheme.purple : LiquidTheme.cyan }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Run, monitor, and inspect each protected service.")
                    .font(.subheadline).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                ForEach(BackupService.allCases) { service in
                    Button {
                        withAnimation(.spring(response: 0.30, dampingFraction: 0.82)) {
                            expandedService = expandedService == service ? nil : service
                        }
                    } label: { serviceCard(service) }
                    .buttonStyle(.plain)
                    if expandedService == service { expandedDetails(service) }
                }
                Text("Recent history").font(.headline)
                ForEach(api.history.prefix(8)) { item in
                    HStack { Text(item.service).fontWeight(.semibold); Spacer(); Text(item.status).foregroundColor(item.status.localizedCaseInsensitiveContains("success") ? LiquidTheme.emerald : LiquidTheme.coral) }
                        .font(.caption).padding(12).liquidGlassCard(cornerRadius: 14, glow: LiquidTheme.blue.opacity(0.12))
                }
            }
            .padding(16).padding(.bottom, 90)
        }
        .background(LiquidTheme.background(for: colorScheme).ignoresSafeArea())
        .sheet(item: $selectedService) { service in
            ServiceDetailView(api: api, serviceKey: service.rawValue, title: service.title, icon: service.icon, accent: service.color)
        }
        .refreshable { await api.fetchAll() }
    }

    private func serviceCard(_ service: BackupService) -> some View {
        HStack(spacing: 13) {
            Image(systemName: service.icon).font(.title3).foregroundColor(service.color).frame(width: 34)
            VStack(alignment: .leading, spacing: 4) {
                Text(service.title).font(.headline).foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Text("Tap to expand status, history, and controls").font(.caption).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            Spacer(); Image(systemName: expandedService == service ? "chevron.up" : "chevron.down").font(.caption.weight(.bold)).foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
        }
        .padding(16).liquidGlassCard(cornerRadius: 18, glow: service.color.opacity(0.22))
    }

    private func expandedDetails(_ service: BackupService) -> some View {
        let item = service == .ftp ? api.status?.services?.ftp : service == .sql ? api.status?.services?.sql : api.status?.services?.mailchimp
        let recent = api.history.filter { $0.service.localizedCaseInsensitiveContains(service.rawValue) }.prefix(3)
        return VStack(alignment: .leading, spacing: 10) {
            HStack { Label(item?.freshness?.badgeText ?? "No backup recorded", systemImage: "checkmark.seal"); Spacer(); Text(item?.freshness?.relativeTime ?? "--").foregroundColor(LiquidTheme.textSecondary(for: colorScheme)) }
                .font(.caption.weight(.semibold))
            if recent.isEmpty { Text("No recent execution records.").font(.caption).foregroundColor(LiquidTheme.textSecondary(for: colorScheme)) }
            ForEach(Array(recent)) { record in
                HStack { Text(record.time).lineLimit(1); Spacer(); Text(record.status) }.font(.caption2)
            }
            Button { selectedService = service } label: { Label("Open service details", systemImage: "arrow.up.right.square") .frame(maxWidth: .infinity) }
                .buttonStyle(.borderedProminent).tint(service.color)
        }
        .padding(14).liquidGlassCard(cornerRadius: 16, glow: service.color.opacity(0.12))
    }
}
