import SwiftUI

public struct SplashScreenView: View {
    @State private var logoScale: CGFloat = 0.7
    @State private var logoOpacity: Double = 0.0
    @State private var ringScale: CGFloat = 0.6
    @State private var ringOpacity: Double = 0.0
    @State private var textOpacity: Double = 0.0
    @State private var subtitleOffset: CGFloat = 10
    @State private var pulseGlow: Bool = false

    public init() {}

    public var body: some View {
        ZStack {
            // Deep obsidian background
            LiquidTheme.backgroundDark.ignoresSafeArea()

            // Dynamic Radiant Aura
            RadialGradient(
                colors: [
                    LiquidTheme.purple.opacity(pulseGlow ? 0.35 : 0.18),
                    LiquidTheme.gold.opacity(pulseGlow ? 0.22 : 0.08),
                    Color.clear
                ],
                center: .center,
                startRadius: 20,
                endRadius: 380
            )
            .ignoresSafeArea()
            .animation(.easeInOut(duration: 1.6).repeatForever(autoreverses: true), value: pulseGlow)

            VStack(spacing: 24) {
                Spacer()

                // Animated Logo Container
                ZStack {
                    // Outer Pulsing Ring
                    Circle()
                        .stroke(
                            LinearGradient(
                                colors: [LiquidTheme.gold.opacity(0.8), LiquidTheme.purple.opacity(0.8), LiquidTheme.cyan.opacity(0.6)],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            ),
                            lineWidth: 2
                        )
                        .frame(width: 140, height: 140)
                        .scaleEffect(ringScale)
                        .opacity(ringOpacity)

                    // Ambient Ring Glow
                    Circle()
                        .fill(
                            RadialGradient(
                                colors: [LiquidTheme.gold.opacity(0.30), LiquidTheme.purple.opacity(0.15), Color.clear],
                                center: .center,
                                startRadius: 10,
                                endRadius: 70
                            )
                        )
                        .frame(width: 130, height: 130)
                        .scaleEffect(pulseGlow ? 1.15 : 0.95)

                    // Logo Emblem Card
                    ZStack {
                        RoundedRectangle(cornerRadius: 28, style: .continuous)
                            .fill(
                                LinearGradient(
                                    colors: [LiquidTheme.surfaceDark, Color(red: 0.12, green: 0.14, blue: 0.24)],
                                    startPoint: .topLeading,
                                    endPoint: .bottomTrailing
                                )
                            )
                            .frame(width: 96, height: 96)
                            .overlay(
                                RoundedRectangle(cornerRadius: 28, style: .continuous)
                                    .stroke(
                                        LinearGradient(
                                            colors: [LiquidTheme.gold.opacity(0.6), Color.white.opacity(0.2)],
                                            startPoint: .topLeading,
                                            endPoint: .bottomTrailing
                                        ),
                                        lineWidth: 1.5
                                    )
                            )
                            .shadow(color: LiquidTheme.gold.opacity(0.40), radius: 18, x: 0, y: 8)

                        Image("AppLogo")
                            .resizable()
                            .aspectRatio(contentMode: .fit)
                            .frame(width: 58, height: 58)
                            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                    }
                    .scaleEffect(logoScale)
                    .opacity(logoOpacity)
                }

                // Brand Typography
                VStack(spacing: 8) {
                    Text("PINAYPAL")
                        .font(.system(size: 28, weight: .black, design: .rounded))
                        .foregroundStyle(
                            LinearGradient(
                                colors: [.white, Color(red: 0.92, green: 0.94, blue: 1.0)],
                                startPoint: .top,
                                endPoint: .bottom
                            )
                        )
                        .tracking(1.8)

                    Text("BACKUP MANAGER")
                        .font(.system(size: 12, weight: .heavy, design: .rounded))
                        .foregroundColor(LiquidTheme.gold)
                        .tracking(3.5)

                    Text("High-Performance Desktop Sync & Engine")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(LiquidTheme.textSecondary)
                        .offset(y: subtitleOffset)
                }
                .opacity(textOpacity)

                Spacer()

                // Animated Loading Indicator & Version
                VStack(spacing: 12) {
                    HStack(spacing: 6) {
                        ForEach(0..<3) { i in
                            Circle()
                                .fill(LiquidTheme.gold)
                                .frame(width: 6, height: 6)
                                .scaleEffect(pulseGlow ? 1.0 : 0.5)
                                .animation(
                                    .easeInOut(duration: 0.6)
                                        .repeatForever(autoreverses: true)
                                        .delay(Double(i) * 0.2),
                                    value: pulseGlow
                                )
                        }
                    }

                    Text("v3.6.4 • Enterprise Companion")
                        .font(.system(size: 10, weight: .semibold, design: .monospaced))
                        .foregroundColor(LiquidTheme.textSecondary.opacity(0.6))
                }
                .opacity(textOpacity)
                .padding(.bottom, 36)
            }
        }
        .onAppear {
            // Animate In Sequence
            withAnimation(.spring(response: 0.8, dampingFraction: 0.65)) {
                logoScale = 1.0
                logoOpacity = 1.0
            }

            withAnimation(.easeOut(duration: 1.1).delay(0.2)) {
                ringScale = 1.0
                ringOpacity = 0.9
            }

            withAnimation(.easeOut(duration: 0.7).delay(0.4)) {
                textOpacity = 1.0
                subtitleOffset = 0
            }

            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                pulseGlow = true
            }
        }
    }
}
