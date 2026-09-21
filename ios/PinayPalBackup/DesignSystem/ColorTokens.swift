import SwiftUI

public struct LiquidTheme {
    // Core brand & accent tones
    public static let backgroundDark = Color(red: 0.043, green: 0.055, blue: 0.078) // #0B0E14
    public static let surfaceDark = Color(red: 0.086, green: 0.106, blue: 0.133)   // #161B22
    public static let gold = Color(red: 0.988, green: 0.639, blue: 0.067)          // #FCA311
    public static let goldDark = Color(red: 0.850, green: 0.510, blue: 0.035)      // Deeper Gold
    public static let emerald = Color(red: 0.247, green: 0.725, blue: 0.314)       // #3FB950
    public static let cyan = Color(red: 0.282, green: 0.792, blue: 0.894)          // #48CAE4
    public static let purple = Color(red: 0.639, green: 0.443, blue: 0.969)        // #A371F7
    public static let blue = Color(red: 0.345, green: 0.651, blue: 1.000)          // #58A6FF
    public static let coral = Color(red: 0.973, green: 0.318, blue: 0.286)         // #F85149
    public static let textPrimary = Color(red: 0.941, green: 0.965, blue: 0.988)   // #F0F6FC
    public static let textSecondary = Color(red: 0.545, green: 0.580, blue: 0.620) // #8B949E

    // Light Mode tokens
    public static let backgroundLight = Color(red: 0.957, green: 0.965, blue: 0.976) // #F4F6F9
    public static let surfaceLight = Color.white                                      // #FFFFFF
    public static let cardLight = Color(red: 0.973, green: 0.980, blue: 0.988)       // #F8FAFC
    public static let textPrimaryLight = Color(red: 0.059, green: 0.090, blue: 0.165) // #0F172A
    public static let textSecondaryLight = Color(red: 0.392, green: 0.455, blue: 0.545) // #64748B
    public static let borderLight = Color(red: 0.886, green: 0.910, blue: 0.941)     // #E2E8F0

    // Adaptive Colors based on ColorScheme
    public static func background(for scheme: ColorScheme) -> Color {
        scheme == .light ? backgroundLight : backgroundDark
    }

    public static func surface(for scheme: ColorScheme) -> Color {
        scheme == .light ? surfaceLight : surfaceDark
    }

    public static func card(for scheme: ColorScheme) -> Color {
        scheme == .light ? cardLight : surfaceDark
    }

    public static func textPrimary(for scheme: ColorScheme) -> Color {
        scheme == .light ? textPrimaryLight : textPrimary
    }

    public static func textSecondary(for scheme: ColorScheme) -> Color {
        scheme == .light ? textSecondaryLight : textSecondary
    }

    public static func border(for scheme: ColorScheme) -> Color {
        scheme == .light ? borderLight : Color(red: 0.188, green: 0.212, blue: 0.239)
    }

    // Liquid Glass Gradients
    public static let specularRimGradient = LinearGradient(
        stops: [
            .init(color: Color.white.opacity(0.70), location: 0.0),
            .init(color: Color.white.opacity(0.25), location: 0.18),
            .init(color: Color.white.opacity(0.05), location: 0.52),
            .init(color: gold.opacity(0.45), location: 0.82),
            .init(color: Color.white.opacity(0.20), location: 1.0)
        ],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )

    public static let glassBorderGradient = LinearGradient(
        colors: [
            Color.white.opacity(0.35),
            Color.white.opacity(0.08),
            Color.white.opacity(0.18)
        ],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )

    public static let liquidGoldGradient = LinearGradient(
        colors: [gold, gold.opacity(0.7), Color(red: 1.0, green: 0.8, blue: 0.3)],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )

    public static let liquidEmeraldGradient = LinearGradient(
        colors: [emerald, emerald.opacity(0.7)],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )

    public static let ambientGlow = RadialGradient(
        colors: [gold.opacity(0.15), Color.clear],
        center: .topTrailing,
        startRadius: 20,
        endRadius: 280
    )
}

public enum AppThemeMode: String, CaseIterable, Identifiable {
    case system = "system"
    case dark = "dark"
    case light = "light"

    public var id: String { rawValue }
    public var displayName: String {
        switch self {
        case .system: return "Auto (System)"
        case .dark: return "Dark (Obsidian)"
        case .light: return "Light (Pearlescent)"
        }
    }
}
