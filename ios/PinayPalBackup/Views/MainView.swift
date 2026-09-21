import SwiftUI

public struct MainView: View {
    @StateObject private var api = PinayPalAPIService()
    @StateObject private var authManager = BiometricAuthManager()

    // 0 = Liquid HUD, 1 = Backup Snapshots, 2 = Live Console, 3 = Web Dashboard
    @State private var selectedTab: Int = 0
    @State private var showSettingsSheet: Bool = false

    // MARK: - Apple Liquid Glass Dragging Physics State
    @State private var dragOffset: CGSize = .zero
    @State private var isDragging: Bool = false
    @Namespace private var tabNamespace

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

                    // Apple Liquid Glass Floating Dock with Dragging Ability
                    VStack {
                        Spacer()
                        liquidGlassDraggableDock
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

    // MARK: - Liquid Glass Draggable Navigation Dock
    // Conforms strictly to Apple Liquid Glass Guidelines:
    // - Continuous concentric capsule geometry
    // - High-transmittance optical blur with dynamic refraction
    // - Dynamic angle-shifted specular rim highlights
    // - Matched geometry sliding fluid tab pill
    // - Viscous rubber-band drag gesture with fluid squish/stretch & spring return
    private var liquidGlassDraggableDock: some View {
        HStack(spacing: 6) {
            liquidTabButton(title: "HUD", icon: "sparkles", index: 0)
            liquidTabButton(title: "Snapshots", icon: "clock.arrow.circlepath", index: 1)
            liquidTabButton(title: "Console", icon: "terminal.fill", index: 2)
            liquidTabButton(title: "Web", icon: "globe", index: 3)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 7)
        .background {
            ZStack {
                // 1. Dynamic optical blur base (ultra-thin material)
                Capsule(style: .continuous)
                    .fill(.ultraThinMaterial)

                // 2. Optical dark Fresnel absorption layer
                Capsule(style: .continuous)
                    .fill(Color(red: 0.06, green: 0.08, blue: 0.12).opacity(0.72))

                // 3. Fluid luminous chromatic underlay
                Capsule(style: .continuous)
                    .fill(
                        RadialGradient(
                            colors: [
                                LiquidTheme.gold.opacity(isDragging ? 0.22 : 0.09),
                                LiquidTheme.gold.opacity(0.02),
                                Color.clear
                            ],
                            center: UnitPoint(x: 0.5 - Double(dragOffset.width) / 500.0, y: 0.5 - Double(dragOffset.height) / 300.0),
                            startRadius: 10,
                            endRadius: 180
                        )
                    )
            }
        }
        .clipShape(Capsule(style: .continuous))
        .overlay {
            // 4. Directional 135° Specular Rim Highlight (shifts with drag angle)
            Capsule(style: .continuous)
                .strokeBorder(
                    LinearGradient(
                        stops: [
                            .init(color: Color.white.opacity(isDragging ? 0.85 : 0.65), location: 0.0),
                            .init(color: Color.white.opacity(0.25), location: 0.20),
                            .init(color: Color.white.opacity(0.04), location: 0.55),
                            .init(color: LiquidTheme.gold.opacity(isDragging ? 0.50 : 0.35), location: 0.80),
                            .init(color: Color.white.opacity(0.25), location: 1.0)
                        ],
                        startPoint: specularStartPoint,
                        endPoint: specularEndPoint
                    ),
                    lineWidth: isDragging ? 1.3 : 1.0
                )
        }
        // Multi-tier dynamic shadow system responding to drag elevation
        .shadow(
            color: Color.black.opacity(isDragging ? 0.70 : 0.50),
            radius: isDragging ? 26 : 18,
            x: dragOffset.width * 0.15,
            y: (isDragging ? 14 : 8) + (dragOffset.height * 0.15)
        )
        .shadow(
            color: LiquidTheme.gold.opacity(isDragging ? 0.32 : 0.12),
            radius: isDragging ? 28 : 14,
            x: 0,
            y: 4
        )
        // Fluid stretch / squish distortion physics
        .scaleEffect(x: dragScaleX, y: dragScaleY)
        .offset(dragOffset)
        .gesture(dragGesture)
    }

    // MARK: - Dynamic Optical Vector Calculations
    private var specularStartPoint: UnitPoint {
        let offsetX = 0.0 - Double(dragOffset.width) / 350.0
        let offsetY = 0.0 - Double(dragOffset.height) / 250.0
        return UnitPoint(x: max(-0.4, min(1.4, offsetX)), y: max(-0.4, min(1.4, offsetY)))
    }

    private var specularEndPoint: UnitPoint {
        let offsetX = 1.0 - Double(dragOffset.width) / 350.0
        let offsetY = 1.0 - Double(dragOffset.height) / 250.0
        return UnitPoint(x: max(-0.4, min(1.4, offsetX)), y: max(-0.4, min(1.4, offsetY)))
    }

    private var dragScaleX: CGFloat {
        let stretch = 1.0 + (abs(dragOffset.width) * 0.0009) - (abs(dragOffset.height) * 0.0005)
        return max(0.92, min(1.12, stretch))
    }

    private var dragScaleY: CGFloat {
        let stretch = 1.0 + (abs(dragOffset.height) * 0.0009) - (abs(dragOffset.width) * 0.0005)
        return max(0.92, min(1.12, stretch))
    }

    // MARK: - Interactive Drag Gesture Physics
    private var dragGesture: some Gesture {
        DragGesture(minimumDistance: 3)
            .onChanged { value in
                if !isDragging {
                    isDragging = true
                    let haptic = UIImpactFeedbackGenerator(style: .light)
                    haptic.impactOccurred()
                }
                // Viscous fluid resistance (logarithmic deceleration)
                let dampedX = value.translation.width * 0.42
                let dampedY = value.translation.height * 0.35
                dragOffset = CGSize(width: dampedX, height: dampedY)
            }
            .onEnded { value in
                isDragging = false

                // If flicked/swiped horizontally, fluidly transition to neighbor tab
                if value.predictedEndTranslation.width > 100 || value.translation.width > 70 {
                    if selectedTab > 0 {
                        withAnimation(.spring(response: 0.36, dampingFraction: 0.72)) {
                            selectedTab -= 1
                        }
                        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
                    }
                } else if value.predictedEndTranslation.width < -100 || value.translation.width < -70 {
                    if selectedTab < 3 {
                        withAnimation(.spring(response: 0.36, dampingFraction: 0.72)) {
                            selectedTab += 1
                        }
                        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
                    }
                }

                // Apple liquid viscous spring rebound
                withAnimation(.spring(response: 0.44, dampingFraction: 0.64, blendDuration: 0.25)) {
                    dragOffset = .zero
                }
                let haptic = UIImpactFeedbackGenerator(style: .rigid)
                haptic.impactOccurred(intensity: 0.6)
            }
    }

    // MARK: - Liquid Tab Button with Matched Geometry Indicator
    private func liquidTabButton(title: String, icon: String, index: Int) -> some View {
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
                        .transition(.opacity.combined(with: .scale(scale: 0.9)))

                    if isBackupBusy && index == 0 {
                        Text("•")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(.black)
                    }
                }
            }
            .foregroundColor(isSelected ? .black : (hasActiveActivity ? LiquidTheme.gold : LiquidTheme.textSecondary))
            .padding(.horizontal, isSelected ? 15 : 11)
            .padding(.vertical, 9)
            .background {
                if isSelected {
                    Capsule(style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    LiquidTheme.gold,
                                    LiquidTheme.goldDark
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )
                        .matchedGeometryEffect(id: "active_tab_liquid_pill", in: tabNamespace)
                        .overlay {
                            Capsule(style: .continuous)
                                .strokeBorder(
                                    LinearGradient(
                                        colors: [
                                            Color.white.opacity(0.65),
                                            Color.white.opacity(0.15)
                                        ],
                                        startPoint: .topLeading,
                                        endPoint: .bottomTrailing
                                    ),
                                    lineWidth: 0.8
                                )
                        }
                        .shadow(color: LiquidTheme.gold.opacity(0.42), radius: 10, y: 3)
                }
            }
        }
        .buttonStyle(.plain)
    }
}
