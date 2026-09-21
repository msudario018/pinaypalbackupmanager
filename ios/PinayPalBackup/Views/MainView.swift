import SwiftUI

public struct MainView: View {
    private enum AppTab: Hashable {
        case overview, activity, console, dashboard
    }

    @StateObject private var api = PinayPalAPIService()
    @StateObject private var authManager = BiometricAuthManager()
    @State private var selectedTab: AppTab = .overview
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
            selectedTab = request == .logs ? .console : .activity
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
                    .toolbar { globalToolbar }
            }
            .tabItem { Label("Overview", systemImage: "rectangle.3.group.fill") }
            .tag(AppTab.overview)

            NavigationStack {
                BackupHistoryView(api: api, showSettingsSheet: $showSettingsSheet)
                    .toolbar { globalToolbar }
            }
            .tabItem { Label("Activity", systemImage: "clock.arrow.circlepath") }
            .tag(AppTab.activity)

            NavigationStack {
                LiveLogsView(api: api, showSettingsSheet: $showSettingsSheet)
                    .toolbar { globalToolbar }
            }
            .tabItem { Label("Console", systemImage: "terminal.fill") }
            .tag(AppTab.console)

            NavigationStack {
                LiquidWebView(api: api, showSettingsSheet: $showSettingsSheet)
                    .toolbar { globalToolbar }
            }
            .tabItem { Label("Dashboard", systemImage: "globe") }
            .tag(AppTab.dashboard)
        }
        .tint(LiquidTheme.gold)
    }

    @ToolbarContentBuilder
    private var globalToolbar: some ToolbarContent {
        ToolbarItem(placement: .topBarTrailing) {
            Button {
                showSettingsSheet = true
            } label: {
                Label("Settings", systemImage: "gearshape")
            }
        }
    }
}
