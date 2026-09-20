import SwiftUI

public struct BiometricShieldView: View {
    @ObservedObject var authManager: BiometricAuthManager
    var onUnlockSuccess: () -> Void

    @State private var isPulsing = false

    public var body: some View {
        ZStack {
            // Ambient dark glass background
            LiquidTheme.backgroundDark
                .ignoresSafeArea()

            RadialGradient(
                colors: [LiquidTheme.gold.opacity(0.18), Color.clear],
                center: .center,
                startRadius: 20,
                endRadius: 320
            )
            .ignoresSafeArea()

            VStack(spacing: 32) {
                Spacer()

                // Iridescent glowing Shield Icon
                ZStack {
                    Circle()
                        .fill(LiquidTheme.gold.opacity(0.15))
                        .frame(width: 140, height: 140)
                        .scaleEffect(isPulsing ? 1.12 : 0.96)
                        .animation(.easeInOut(duration: 2.2).repeatForever(autoreverses: true), value: isPulsing)

                    Circle()
                        .strokeBorder(LiquidTheme.glassBorderGradient, lineWidth: 1.5)
                        .frame(width: 120, height: 120)

                    Image(systemName: "shield.lefthalf.filled.badge.checkmark")
                        .font(.system(size: 52, weight: .light))
                        .foregroundStyle(LiquidTheme.liquidGoldGradient)
                }

                VStack(spacing: 8) {
                    Text("PinayPal")
                        .font(.system(size: 32, weight: .black, design: .rounded))
                        .foregroundColor(LiquidTheme.gold)

                    Text("Liquid Glass Security Shield")
                        .font(.system(size: 14, weight: .semibold, design: .rounded))
                        .foregroundColor(LiquidTheme.textSecondary)
                }

                Spacer()

                // Face ID Unlock Button
                Button {
                    let impact = UIImpactFeedbackGenerator(style: .medium)
                    impact.impactOccurred()
                    Task {
                        await authManager.authenticate()
                        if authManager.isUnlocked {
                            onUnlockSuccess()
                        }
                    }
                } label: {
                    HStack(spacing: 12) {
                        Image(systemName: authManager.biometricType == .faceID ? "faceid" : "touchid")
                            .font(.system(size: 22))

                        Text("Unlock with \(authManager.biometricType == .faceID ? "Face ID" : "Touch ID")")
                            .font(.system(size: 16, weight: .bold, design: .rounded))
                    }
                    .foregroundColor(.black)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 16)
                    .background {
                        ZStack {
                            RoundedRectangle(cornerRadius: 16, style: .continuous)
                                .fill(LiquidTheme.gold)

                            RoundedRectangle(cornerRadius: 16, style: .continuous)
                                .strokeBorder(Color.white.opacity(0.4), lineWidth: 1)
                        }
                    }
                    .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 14, x: 0, y: 6)
                }
                .padding(.horizontal, 36)

                if let err = authManager.errorMessage {
                    Text(err)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(LiquidTheme.coral)
                        .padding(.horizontal, 24)
                        .multilineTextAlignment(.center)
                }

                Spacer().frame(height: 24)
            }
        }
        .onAppear {
            isPulsing = true
            Task {
                await authManager.authenticate()
                if authManager.isUnlocked {
                    onUnlockSuccess()
                }
            }
        }
    }
}
