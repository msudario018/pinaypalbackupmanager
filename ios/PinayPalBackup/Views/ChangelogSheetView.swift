import SwiftUI

public struct ChangelogRelease: Identifiable {
    public let id = UUID()
    public let version: String
    public let releaseDate: String
    public let isLatest: Bool
    public let highlight: String
    public let changes: [ChangelogItem]
}

public struct ChangelogItem: Identifiable {
    public let id = UUID()
    public let type: ChangeType
    public let title: String
    public let description: String

    public enum ChangeType {
        case feature, improvement, fix, security

        var label: String {
            switch self {
            case .feature: return "NEW"
            case .improvement: return "IMPROVED"
            case .fix: return "FIX"
            case .security: return "SECURITY"
            }
        }

        var color: Color {
            switch self {
            case .feature: return LiquidTheme.gold
            case .improvement: return LiquidTheme.cyan
            case .fix: return LiquidTheme.emerald
            case .security: return LiquidTheme.purple
            }
        }

        var icon: String {
            switch self {
            case .feature: return "sparkles"
            case .improvement: return "arrow.up.circle.fill"
            case .fix: return "wrench.and.screwdriver.fill"
            case .security: return "lock.shield.fill"
            }
        }
    }
}

public struct ChangelogSheetView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var colorScheme

    private let releases: [ChangelogRelease] = [
        ChangelogRelease(
            version: "v3.6.0",
            releaseDate: "September 2026",
            isLatest: true,
            highlight: "3D Beveled Live Activity badges, rich notification system, PC toast gradients, and browser notification icons",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "3D Beveled Live Activity & Dynamic Island",
                    description: "Overhauled with vibrant service-specific gradients (Mailchimp cyan-blue, SQL amber gold, FTP emerald jade), specular gloss highlights, ambient drop shadows, and active pulse dots."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Rich iOS Notifications",
                    description: "Added contextual service emoji badges, descriptive subtitles, and time-sensitive interruption levels to cut through Focus modes for urgent alerts."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "PC Desktop Toast Gradient Badges",
                    description: "Modernized desktop notifications to 38x38 squircle badges with vibrant multi-stop linear gradients and crisp white iconography."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Web Dashboard Brand Favicon & Push Icons",
                    description: "Added /api/logo endpoint serving the official PinayPal emblem for browser desktop notifications and browser tab favicons."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.5.0",
            releaseDate: "September 2026",
            isLatest: false,
            highlight: "Live Activity overhaul, Profile management, App icon refresh, and UX polish",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Live Activity & Dynamic Island Overhaul",
                    description: "Rebuilt with dynamic service-specific icons, glowing gradient progress bar, real-time speed & ETA tracking, and completed state animations."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "User Profile & Credential Management",
                    description: "Added dedicated profile avatar button in header. Change username, change login password, switch user account, and view active session details directly from mobile."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Backup Carousel Node Alignment",
                    description: "Resolved overlapping pagination indicators on the service backup card. Replaced with responsive interactive indicator capsules."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Official High-Res App Icon",
                    description: "Integrated official PinayPal emblem centered on deep obsidian gradient with subtle inner squircle plate conforming to iOS guidelines."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Animated Launch Splash Screen",
                    description: "Fluid startup animation with glowing emblem pulsation and smooth transition into your dashboard or login screen."
                )
            ]
        ),
        ChangelogRelease(
            version: "v2.3.0",
            releaseDate: "August 2026",
            isLatest: false,
            highlight: "Wake-on-LAN, Emergency Halt, and Biometric Shield",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Wake-on-LAN PC Power On",
                    description: "Send magic packets across local subnets to wake sleeping desktop backup workstations remotely."
                ),
                ChangelogItem(
                    type: .security,
                    title: "Face ID / Touch ID Biometric Shield",
                    description: "Hardware-backed biometric lock protecting backup triggers and sensitive configuration sheets."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Desktop Emergency Stop",
                    description: "Instantly halt runaway or queued backup tasks directly from your iPhone."
                )
            ]
        ),
        ChangelogRelease(
            version: "v2.2.0",
            releaseDate: "July 2026",
            isLatest: false,
            highlight: "Triple-service backup orchestration & live logs",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "FTP, SQL & Mailchimp Remote Triggers",
                    description: "Trigger backups individually or run Master Backup across all desktop services sequentially."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Real-Time Terminal Logs",
                    description: "Streaming console view with search filtering, log export, and color-coded severity levels."
                )
            ]
        )
    ]

    public init() {}

    public var body: some View {
        NavigationStack {
            ZStack {
                LiquidTheme.background(for: colorScheme).ignoresSafeArea()

                RadialGradient(
                    colors: [LiquidTheme.cyan.opacity(0.12), LiquidTheme.gold.opacity(0.06), Color.clear],
                    center: .topLeading,
                    startRadius: 20,
                    endRadius: 400
                )
                .ignoresSafeArea()

                ScrollView {
                    VStack(spacing: 24) {
                        // Header Banner
                        VStack(spacing: 8) {
                            Image("AppLogo")
                                .resizable().aspectRatio(contentMode: .fit)
                                .frame(width: 48, height: 48)
                                .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                                .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 8, x: 0, y: 3)

                            Text("PinayPal Mobile Companion")
                                .font(.system(size: 20, weight: .black, design: .rounded))
                                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

                            Text("Version Changelogs & Release History")
                                .font(.system(size: 13, weight: .medium))
                                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        }
                        .padding(.top, 10)

                        // Release Cards
                        ForEach(releases) { release in
                            releaseCard(release: release)
                        }

                        Spacer().frame(height: 30)
                    }
                    .padding(.horizontal, 16)
                    .padding(.top, 10)
                }
            }
            .navigationTitle("What's New")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                        .font(.body.weight(.semibold))
                        .foregroundColor(LiquidTheme.gold)
                }
            }
        }
    }

    private func releaseCard(release: ChangelogRelease) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            // Version Header
            HStack(alignment: .center) {
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 8) {
                        Text(release.version)
                            .font(.system(size: 18, weight: .black, design: .rounded))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

                        if release.isLatest {
                            Text("LATEST")
                                .font(.system(size: 9, weight: .heavy, design: .rounded))
                                .foregroundColor(.black)
                                .padding(.horizontal, 7)
                                .padding(.vertical, 3)
                                .background(LiquidTheme.gold)
                                .clipShape(Capsule())
                        }
                    }

                    Text(release.releaseDate)
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                }

                Spacer()
            }

            Text(release.highlight)
                .font(.system(size: 13, weight: .semibold, design: .rounded))
                .foregroundColor(LiquidTheme.gold)

            Divider().background(Color.white.opacity(0.08))

            // Changes List
            VStack(spacing: 12) {
                ForEach(release.changes) { change in
                    HStack(alignment: .top, spacing: 10) {
                        // Badge
                        HStack(spacing: 4) {
                            Image(systemName: change.type.icon)
                                .font(.system(size: 9, weight: .bold))
                            Text(change.type.label)
                                .font(.system(size: 9, weight: .heavy, design: .rounded))
                        }
                        .foregroundColor(change.type.color)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 3)
                        .background(change.type.color.opacity(0.14))
                        .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
                        .frame(width: 80, alignment: .leading)

                        VStack(alignment: .leading, spacing: 3) {
                            Text(change.title)
                                .font(.system(size: 13, weight: .bold, design: .rounded))
                                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

                            Text(change.description)
                                .font(.system(size: 12))
                                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                                .fixedSize(horizontal: false, vertical: true)
                        }

                        Spacer()
                    }
                }
            }
        }
        .padding(18)
        .liquidGlassCard(
            cornerRadius: 20,
            glow: release.isLatest ? LiquidTheme.gold.opacity(0.18) : Color.white.opacity(0.05),
            variant: release.isLatest ? .prominent : .regular
        )
    }
}
