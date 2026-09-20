import SwiftUI

// MARK: - Liquid Glass View Modifiers (iOS 27 Design)

public struct LiquidGlassCardModifier: ViewModifier {
    var cornerRadius: CGFloat = 20
    var glowColor: Color = LiquidTheme.gold.opacity(0.12)
    var isInteractive: Bool = false

    @State private var isHovered: Bool = false

    public func body(content: Content) -> some View {
        content
            .background {
                ZStack {
                    // 1. Frosted ultra-thin ambient material
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(.ultraThinMaterial)

                    // 2. Liquid tint underlay
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    Color.white.opacity(0.06),
                                    Color(red: 0.1, green: 0.12, blue: 0.18).opacity(0.65),
                                    Color(red: 0.05, green: 0.07, blue: 0.1).opacity(0.85)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )

                    // 3. Ambient specular liquid glow
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(
                            RadialGradient(
                                colors: [glowColor, Color.clear],
                                center: .topLeading,
                                startRadius: 10,
                                endRadius: 160
                            )
                        )
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
            .overlay {
                // 4. Ultra-thin chromatic liquid glass border
                RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                    .strokeBorder(
                        LinearGradient(
                            colors: [
                                Color.white.opacity(0.40),
                                Color.white.opacity(0.10),
                                glowColor.opacity(0.6),
                                Color.white.opacity(0.15)
                            ],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        ),
                        lineWidth: 1.0
                    )
            }
            .shadow(color: Color.black.opacity(0.45), radius: 18, x: 0, y: 10)
            .shadow(color: glowColor.opacity(0.25), radius: 24, x: 0, y: 4)
    }
}

public struct LiquidButtonModifier: ViewModifier {
    var accent: Color = LiquidTheme.gold
    var isDestructive: Bool = false

    public func body(content: Content) -> some View {
        content
            .font(.system(size: 14, weight: .bold, design: .rounded))
            .foregroundColor(isDestructive ? .white : .black)
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background {
                ZStack {
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .fill(accent)

                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .strokeBorder(Color.white.opacity(0.35), lineWidth: 0.8)
                }
            }
            .shadow(color: accent.opacity(0.4), radius: 8, x: 0, y: 3)
    }
}

public struct LiquidCapsulePill: ViewModifier {
    var tint: Color

    public func body(content: Content) -> some View {
        content
            .font(.system(size: 11, weight: .bold, design: .rounded))
            .foregroundColor(tint)
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background {
                Capsule(style: .continuous)
                    .fill(tint.opacity(0.15))
            }
            .overlay {
                Capsule(style: .continuous)
                    .strokeBorder(tint.opacity(0.35), lineWidth: 0.8)
            }
    }
}

// MARK: - View Extension Helpers
public extension View {
    func liquidGlassCard(cornerRadius: CGFloat = 20, glow: Color = LiquidTheme.gold.opacity(0.15)) -> some View {
        self.modifier(LiquidGlassCardModifier(cornerRadius: cornerRadius, glowColor: glow))
    }

    func liquidButton(accent: Color = LiquidTheme.gold, isDestructive: Bool = false) -> some View {
        self.modifier(LiquidButtonModifier(accent: accent, isDestructive: isDestructive))
    }

    func liquidPill(tint: Color = LiquidTheme.gold) -> some View {
        self.modifier(LiquidCapsulePill(tint: tint))
    }
}
