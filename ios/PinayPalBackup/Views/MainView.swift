import SwiftUI

public struct MainView: View {
    @StateObject private var api = PinayPalAPIService()
    @StateObject private var authManager = BiometricAuthManager()

    @State private var selectedTab: Int = 0 // 0 = Native Liquid HUD, 1 = Live Web View
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
                    // Content
                    if selectedTab == 0 {
                        LiquidDashboardView(api: api, showSettingsSheet: $showSettingsSheet)
                            .transition(.opacity)
                    } else {
                        LiquidWebView(api: api, showSettingsSheet: $showSettingsSheet)
                            .transition(.opacity)
                    }

                    // Floating Liquid Glass Capsule Switcher at bottom
                    VStack {
                        Spacer()
                        floatingCapsuleBar
                            .padding(.bottom, 20)
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
        HStack(spacing: 8) {
            Button {
                withAnimation(.spring(response: 0.35, dampingFraction: 0.75)) {
                    selectedTab = 0
                }
                let haptic = UIImpactFeedbackGenerator(style: .light)
                haptic.impactOccurred()
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: "sparkles")
                    Text("Liquid HUD")
                        .font(.system(size: 13, weight: .bold, design: .rounded))
                }
                .foregroundColor(selectedTab == 0 ? .black : LiquidTheme.textSecondary)
                .padding(.horizontal, 16)
                .padding(.vertical, 10)
                .background {
                    if selectedTab == 0 {
                        Capsule(style: .continuous)
                            .fill(LiquidTheme.gold)
                            .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 8, y: 3)
                    }
                }
            }

            Button {
                withAnimation(.spring(response: 0.35, dampingFraction: 0.75)) {
                    selectedTab = 1
                }
                let haptic = UIImpactFeedbackGenerator(style: .light)
                haptic.impactOccurred()
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: "globe")
                    Text("Web Dashboard")
                        .font(.system(size: 13, weight: .bold, design: .rounded))
                }
                .foregroundColor(selectedTab == 1 ? .black : LiquidTheme.textSecondary)
                .padding(.horizontal, 16)
                .padding(.vertical, 10)
                .background {
                    if selectedTab == 1 {
                        Capsule(style: .continuous)
                            .fill(LiquidTheme.gold)
                            .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 8, y: 3)
                    }
                }
            }
        }
        .padding(6)
        .background(.ultraThinMaterial)
        .clipShape(Capsule(style: .continuous))
        .overlay {
            Capsule(style: .continuous)
                .strokeBorder(LinearGradient(
                    colors: [Color.white.opacity(0.4), Color.white.opacity(0.1)],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                ), lineWidth: 1)
        }
        .shadow(color: Color.black.opacity(0.5), radius: 20, y: 10)
    }
}
