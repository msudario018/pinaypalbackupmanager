import SwiftUI

public struct MainView: View {
    @StateObject private var api = PinayPalAPIService()
    @StateObject private var authManager = BiometricAuthManager()

    // 0 = Liquid HUD, 1 = Backup Snapshots, 2 = Live Console, 3 = Web Dashboard
    @State private var selectedTab: Int = 0
    @State private var showSettingsSheet: Bool = false

    public var body: some View {
        ZStack {
            if !api.isConfigured {
                ConnectionSetupView()
                    .environmentObject(api)
                    .transition(.asymmetric(insertion: .move(edge: .leading), removal: .opacity))
            } else if !api.isLoggedIn {
                LoginView()
                    .environmentObject(api)
                    .environmentObject(authManager)
                    .transition(.asymmetric(insertion: .move(edge: .trailing), removal: .opacity))
            } else if !authManager.isUnlocked {
                BiometricShieldView(authManager: authManager) {
                    // Unlocked
                }
                .transition(.opacity)
            } else {
                ZStack {
                    // Tab Content
                    Group {
                        switch selectedTab {
                        case 0:
                            LiquidDashboardView(api: api, showSettingsSheet: $showSettingsSheet)
                        case 1:
                            BackupHistoryView(api: api, showSettingsSheet: $showSettingsSheet)
                        case 2:
                            LiveLogsView(api: api, showSettingsSheet: $showSettingsSheet)
                        case 3:
                            LiquidWebView(api: api, showSettingsSheet: $showSettingsSheet)
                        default:
                            LiquidDashboardView(api: api, showSettingsSheet: $showSettingsSheet)
                        }
                    }
                    .transition(.opacity)

                    // 4-Tab Liquid Glass Dock at bottom
                    VStack {
                        Spacer()
                        floatingCapsuleBar
                            .padding(.bottom, 16)
                    }
                }
                .sheet(isPresented: $showSettingsSheet) {
                    ServerConfigSheet(api: api, authManager: authManager)
                }
            }
        }
        .animation(.easeInOut(duration: 0.3), value: api.isConfigured)
        .animation(.easeInOut(duration: 0.3), value: api.isLoggedIn)
        .animation(.easeInOut(duration: 0.3), value: authManager.isUnlocked)
        .preferredColorScheme(.dark)
    }

    private var floatingCapsuleBar: some View {
        HStack(spacing: 4) {
            tabButton(title: "HUD", icon: "sparkles", index: 0)
            tabButton(title: "Snapshots", icon: "clock.arrow.circlepath", index: 1)
            tabButton(title: "Console", icon: "terminal.fill", index: 2)
            tabButton(title: "Web", icon: "globe", index: 3)
        }
        .padding(5)
        .background(.ultraThinMaterial)
        .clipShape(Capsule(style: .continuous))
        .overlay {
            Capsule(style: .continuous)
                .strokeBorder(LinearGradient(
                    colors: [Color.white.opacity(0.35), Color.white.opacity(0.1)],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                ), lineWidth: 1)
        }
        .shadow(color: Color.black.opacity(0.6), radius: 20, y: 10)
    }

    private func tabButton(title: String, icon: String, index: Int) -> some View {
        let isSelected = selectedTab == index
        let isBackupBusy = api.status?.activeBackup?.isBusy == true
        let hasActiveActivity = isBackupBusy && (index == 0 || index == 2)

        return Button {
            withAnimation(.spring(response: 0.35, dampingFraction: 0.75)) {
                selectedTab = index
            }
            let haptic = UIImpactFeedbackGenerator(style: .light)
            haptic.impactOccurred()
        } label: {
            HStack(spacing: 5) {
                ZStack(alignment: .topTrailing) {
                    Image(systemName: icon)
                        .font(.system(size: 13, weight: .bold))

                    if hasActiveActivity {
                        Circle()
                            .fill(LiquidTheme.gold)
                            .frame(width: 6, height: 6)
                            .offset(x: 4, y: -4)
                            .shadow(color: LiquidTheme.gold, radius: 3)
                    }
                }

                if isSelected {
                    Text(title)
                        .font(.system(size: 12, weight: .bold, design: .rounded))
                        .transition(.opacity.combined(with: .scale))

                    if isBackupBusy && index == 0 {
                        Text("•")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(isSelected ? .black : LiquidTheme.gold)
                    }
                }
            }
            .foregroundColor(isSelected ? .black : (hasActiveActivity ? LiquidTheme.gold : LiquidTheme.textSecondary))
            .padding(.horizontal, isSelected ? 14 : 10)
            .padding(.vertical, 9)
            .background {
                if isSelected {
                    Capsule(style: .continuous)
                        .fill(LiquidTheme.gold)
                        .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 8, y: 3)
                }
            }
        }
    }
}
