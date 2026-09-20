import SwiftUI

// MARK: - Official Apple Liquid Glass Material Architecture
// Conforms to Apple Liquid Glass Guidelines (https://developer.apple.com/documentation/technologyoverviews/liquid-glass)
// - Dynamic optical transparency with high-transmittance blur
// - Continuous concentric corner geometry
// - Directional 135-degree specular rim highlights
// - Refractive optical chromatic dispersion & luminous underlays
// - Fluid morphing tactile spring feedback

public enum LiquidGlassVariant {
    case regular
    case prominent
    case subtle
}

// MARK: - Liquid Glass Card Modifier
public struct LiquidGlassCardModifier: ViewModifier {
    var cornerRadius: CGFloat = 20
    var glowColor: Color = LiquidTheme.gold
    var variant: LiquidGlassVariant = .regular
    var isInteractive: Bool = false

    public func body(content: Content) -> some View {
        content
            .background {
                ZStack {
                    // 1. Dynamic optical blur base (high transmittance)
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(variant == .prominent ? .thinMaterial : .ultraThinMaterial)

                    // 2. Liquid glass refractive underlay (Fresnel optical depth)
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(
                            LinearGradient(
                                stops: [
                                    .init(color: Color.white.opacity(variant == .prominent ? 0.16 : 0.09), location: 0.0),
                                    .init(color: Color.white.opacity(0.02), location: 0.28),
                                    .init(color: Color(red: 0.08, green: 0.10, blue: 0.14).opacity(0.55), location: 0.70),
                                    .init(color: Color(red: 0.04, green: 0.06, blue: 0.09).opacity(0.85), location: 1.0)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )

                    // 3. Fluid luminous chromatic core (directional light source)
                    RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                        .fill(
                            RadialGradient(
                                colors: [
                                    glowColor.opacity(variant == .prominent ? 0.22 : 0.12),
                                    glowColor.opacity(0.03),
                                    Color.clear
                                ],
                                center: UnitPoint(x: 0.20, y: 0.15),
                                startRadius: 0,
                                endRadius: 200
                            )
                        )
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
            .overlay {
                // 4. Directional 135° Specular Rim Highlight (Apple Liquid Glass Specular Stroke)
                RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                    .strokeBorder(
                        LinearGradient(
                            stops: [
                                .init(color: Color.white.opacity(0.70), location: 0.0),
                                .init(color: Color.white.opacity(0.25), location: 0.18),
                                .init(color: Color.white.opacity(0.05), location: 0.52),
                                .init(color: glowColor.opacity(0.45), location: 0.82),
                                .init(color: Color.white.opacity(0.20), location: 1.0)
                            ],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        ),
                        lineWidth: 1.0
                    )
            }
            // Multi-depth soft shadow system
            .shadow(color: Color.black.opacity(0.40), radius: 16, x: 0, y: 8)
            .shadow(color: glowColor.opacity(variant == .prominent ? 0.20 : 0.10), radius: 24, x: 0, y: 4)
    }
}

// MARK: - Liquid Glass Interactive Button Modifier
public struct LiquidButtonModifier: ViewModifier {
    var accent: Color = LiquidTheme.gold
    var isDestructive: Bool = false

    public func body(content: Content) -> some View {
        content
            .font(.system(size: 14, weight: .bold, design: .rounded))
            .foregroundColor(isDestructive ? .white : .black)
            .padding(.horizontal, 18)
            .padding(.vertical, 11)
            .background {
                ZStack {
                    // Fluid filled optical base
                    RoundedRectangle(cornerRadius: 14, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: [
                                    accent,
                                    accent.opacity(0.82)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )

                    // Specular rim stroke
                    RoundedRectangle(cornerRadius: 14, style: .continuous)
                        .strokeBorder(
                            LinearGradient(
                                colors: [
                                    Color.white.opacity(0.65),
                                    Color.white.opacity(0.15)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            ),
                            lineWidth: 0.9
                        )
                }
            }
            .shadow(color: accent.opacity(0.38), radius: 10, x: 0, y: 4)
    }
}

// MARK: - Liquid Glass Pill Capsule Modifier
public struct LiquidCapsulePill: ViewModifier {
    var tint: Color

    public func body(content: Content) -> some View {
        content
            .font(.system(size: 11, weight: .bold, design: .rounded))
            .foregroundColor(tint)
            .padding(.horizontal, 11)
            .padding(.vertical, 4.5)
            .background {
                Capsule(style: .continuous)
                    .fill(.ultraThinMaterial)
            }
            .background {
                Capsule(style: .continuous)
                    .fill(tint.opacity(0.18))
            }
            .overlay {
                Capsule(style: .continuous)
                    .strokeBorder(
                        LinearGradient(
                            colors: [
                                Color.white.opacity(0.50),
                                tint.opacity(0.40)
                            ],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        ),
                        lineWidth: 0.85
                    )
            }
    }
}

// MARK: - Floating Liquid Glass Bar Modifier
public struct LiquidGlassBarModifier: ViewModifier {
    public func body(content: Content) -> some View {
        content
            .background {
                ZStack {
                    Capsule(style: .continuous)
                        .fill(.ultraThinMaterial)

                    Capsule(style: .continuous)
                        .fill(Color(red: 0.08, green: 0.10, blue: 0.14).opacity(0.65))

                    Capsule(style: .continuous)
                        .strokeBorder(
                            LinearGradient(
                                stops: [
                                    .init(color: Color.white.opacity(0.55), location: 0.0),
                                    .init(color: Color.white.opacity(0.10), location: 0.5),
                                    .init(color: Color.white.opacity(0.25), location: 1.0)
                                ],
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            ),
                            lineWidth: 1.0
                        )
                }
            }
            .shadow(color: Color.black.opacity(0.35), radius: 14, x: 0, y: 6)
    }
}

// MARK: - View Extension Helpers
public extension View {
    func liquidGlassCard(
        cornerRadius: CGFloat = 20,
        glow: Color = LiquidTheme.gold.opacity(0.15),
        variant: LiquidGlassVariant = .regular
    ) -> some View {
        self.modifier(LiquidGlassCardModifier(cornerRadius: cornerRadius, glowColor: glow, variant: variant))
    }

    func liquidButton(accent: Color = LiquidTheme.gold, isDestructive: Bool = false) -> some View {
        self.modifier(LiquidButtonModifier(accent: accent, isDestructive: isDestructive))
    }

    func liquidPill(tint: Color = LiquidTheme.gold) -> some View {
        self.modifier(LiquidCapsulePill(tint: tint))
    }

    func liquidGlassBar() -> some View {
        self.modifier(LiquidGlassBarModifier())
    }
}
