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
            version: "v3.9.1 (Build 30)",
            releaseDate: "October 2026",
            isLatest: true,
            highlight: "Hotfix for FTP/SQL auto-sync: resolved WinSCP 'Unknown switch resume' error",
            changes: [
                ChangelogItem(
                    type: .fix,
                    title: "Fixed FTP & SQL Auto-Sync Failure",
                    description: "Removed incompatible -resume switch from WinSCP directory synchronisation options which caused transfers to halt with 'Unknown switch resume'."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.9.0 (Build 29)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "Telegram Bot remote control, instant QR code reconnection, persistent AI memory & learning, and Zero-Leak shield v2",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Telegram Bot Integration & Alerts",
                    description: "Control backups, receive real-time start/complete/failure notifications, and check status directly inside Telegram without opening ports."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Reconnection QR Code via Telegram",
                    description: "Whenever disconnected, type /qr or /connect in Telegram to immediately receive a pairing QR code with your PC's active LAN and tunnel credentials."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "AI Persistent Memory & Learning",
                    description: "The AI assistant learns and remembers your preferences, backup habits, and notes across sessions. Strictly kept local to your PC."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Agent Profiles & Hardware Auto-Tune",
                    description: "Switch between SRE Guardian, Backup Specialist, or Speedy Assistant. Auto-tune configures optimal threads and memory retention."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Zero-Leak Sanitizer v2",
                    description: "Strips Telegram bot tokens, Cloudflare tunnel tokens, URL passwords, API keys, and database strings before anything leaves your device."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Superseded Gmail with Telegram",
                    description: "Replaced legacy email/SMTP alerts with instant Telegram notifications for backup starts, completions, errors, and disconnects."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.8.2 (Build 28)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "Find every PC on your network automatically, and watch CPU and RAM trends from your phone",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Find Your Computers Automatically",
                    description: "PinayPal sweeps your home network and finds every PC, then works out which ones are already running PinayPal so you can just tap to add them. MAC addresses fill themselves in."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "CPU and RAM Trends",
                    description: "Each PC card now shows a small chart of how its processor and memory have been doing, so you can spot the machine that runs hot every night."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Your PC List Updates Itself",
                    description: "Add, rename or remove a PC in the desktop app and it appears on your phone within seconds. No more pulling to refresh."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "The Chat Bubble Was See-Through",
                    description: "A colour typo made the AI chat window almost invisible against the page behind it. It is properly solid now, and the same problem was fixed across the whole app."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Schedules Save Correctly",
                    description: "Backup times were being stored as the text 'ComboBoxItem' instead of the time you picked. They save properly now."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Title No Longer Collides With Content",
                    description: "Scrolling text could slide under the page title on some iPhones. The header now stays solid so the two never overlap."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.8.1 (Build 27)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "Backups that respect your time, your bandwidth and your RAM - with a preview before every sync and a one-tap undo",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Per-Service Sync Windows",
                    description: "Tell each backup when it is allowed to run. Mailchimp can stay off during business hours while FTP runs overnight, and windows that cross midnight are handled correctly."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Pauses While You're Busy",
                    description: "If you start a big upload, game or stream, scheduled backups wait until the link calms down instead of fighting for bandwidth."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Preview Before You Sync",
                    description: "See exactly which files a sync would upload, and how much data that is, before anything is transferred. Ask the assistant \"what would change?\" and it reads it back to you."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "One-Tap Undo for a Sync",
                    description: "Before a sync overwrites anything, the previous version is saved automatically. Say \"roll back the sync\" to restore it."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Alerts When a PC Drops Off",
                    description: "PinayPal quietly watches your Dev PC and Main PC and tells you when one goes offline for more than a couple of minutes, and when it comes back."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "More Room for Backups",
                    description: "The local AI model is released from memory automatically before a scheduled backup, so a 16 GB machine has plenty of headroom for the transfer."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Smart Scheduling Settings Now Save",
                    description: "The new scheduling options were not persisting across restarts. They are saved correctly now."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.8.0 (Build 26)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "Remote computer control (wake, restart, shutdown), a genuinely conversational AI with multi-turn memory, and major idle-CPU savings",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "My Computers & Remote Power",
                    description: "Register your Dev PC and Main PC to see live hardware telemetry and remotely wake, restart, shut down, lock or sleep them. Peer commands are authenticated by the target PC itself."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Conversational AI That Remembers",
                    description: "The assistant now holds real multi-turn conversations instead of treating every message in isolation, resolves follow-ups like \"run that one\", and suggests tappable next questions after each reply."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Personality & Proactive Updates",
                    description: "Tune talkativeness, creativity and memory depth, rename your assistant, and let it proactively reach out when a backup fails, memory is tight, or the remote tunnel drops."
                ),
                ChangelogItem(
                    type: .security,
                    title: "Online AI, Still Private",
                    description: "Cloud escalation now redacts hostnames, IP addresses, URLs and file paths before anything leaves your PC, and only sends aggregate telemetry."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Collapsible Settings Cards",
                    description: "Settings are now tidy click-to-expand cards, so the AI options and every other section only take up space when you actually open them."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Faster, Resumable FTP Syncs",
                    description: "Backups no longer crawl at kilobytes per second. Transfers are unlimited and resumable, so an interrupted upload continues instead of restarting from zero."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Lower Idle CPU & Battery Use",
                    description: "Hardware temperature and GPU sensors are sampled far less often, the dashboard payload is cached between polls, and the app backs off when backgrounded."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.7.2 (Build 25)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "AI Assistant twin-sparkle emblem overhaul, horizontal scroll prompt chips with bilateral glass arrows, in-drawer & dedicated settings, and intelligent diagnostic actions",
            changes: [
                ChangelogItem(
                    type: .improvement,
                    title: "Bespoke AI Twin-Sparkle Iconography",
                    description: "Replaced generic star icons with custom Bezier twin-sparkle AI emblem, and upgraded the floating launcher to a 58x58 cybernetic orb with metallic trim and live emerald status beacon."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Horizontal Prompt Chips Scroll",
                    description: "Wrapped quick prompt chips in a horizontal ScrollViewer with mouse wheel delta translation and sleek bilateral glass arrow navigation controls for effortless exploration."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Dedicated AI Assistant Settings",
                    description: "Added comprehensive AI configuration in desktop settings: toggle floating widget, login greetings, sound chimes, switch between Hybrid/Ollama/Cloud/Offline engines, and live Ollama connection tester."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "In-Drawer Quick Settings",
                    description: "Added header gear button in the assistant drawer for instant 1-click provider switching, connection testing, and direct shortcut to full settings."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Expanded Intelligence & 1-Click Retries",
                    description: "Added real-time backup run records in markdown tables, active backup queue monitoring, network failover routing diagnostics, and automated error troubleshooting with 1-click retry proposals."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.7.1 (Build 24)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "In-App Updater Overhaul, Email Alerts Persistence Fix, iOS Navigation Bar Facebook Spring Bounce & Scroll Optimization",
            changes: [
                ChangelogItem(
                    type: .fix,
                    title: "In-App PC Updater Overhaul",
                    description: "Added live interactive download progress bar to UpdateAvailableDialog, graceful background worker shutdown before applying updates, and intelligent direct GitHub release fallback for portable installations."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Email Settings Persistence",
                    description: "Fixed email settings not saving by implementing automatic lazy loading on boot, saving to standard AppData directory, and eliminating duplicate JSON alias properties."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Facebook-Style Tab Bar Animation",
                    description: "Added tactile Facebook spring pop and bounce animation on tab button taps, with double-pop feedback on active tab re-taps and haptic pulses."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "iOS Sleek Navigation Bar & Scroll Scrims",
                    description: "Made the navigation bar more compact and added soft gradient background scrims so content scrolls seamlessly beneath floating glass islands."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "AI Assistant Orb Spacing",
                    description: "Refined vertical padding between the floating AI Assistant orb and the compact navigation bar for perfectly balanced ergonomics."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.7.0 (Build 23)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "Conversational AI assistant with Zero-Leak security, floating desktop & iOS widgets, luxury email overhaul, and 24/7 PC stability",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Smart Conversational AI Engine",
                    description: "Supports local Ollama, cloud LLMs, and offline heuristics with Zero-Leak credential sanitization and human-in-the-loop action approval."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Floating Assistant Widget (PC & iOS)",
                    description: "Interactive glassmorphic avatar with login greetings, backup completion popups, and full conversational drawer with action proposal cards."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Luxury Obsidian Email Overhaul",
                    description: "Executive glassmorphic HTML email alerts with 3-column metric cards, live host PC CPU/RAM/disk telemetry footer, and direct action buttons."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Resolved Long-Run PC Crashing & Exiting",
                    description: "Eliminated transient PerformanceCounter allocations in RealtimeMonitoringService and PerformanceMetricsService, resolving registry handle thrashing and Perflib unmanaged heap corruption."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Background Auto-Update Watchdog",
                    description: "Added a recurring 4-hour background update polling timer in UpdateService to automatically notify and update long-running PC instances."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "iOS Multi-Route Ping Diagnostics",
                    description: "Real-time latency testing across Local LAN, Cloudflare Tunnel, and Tailscale VPN in Settings → Network, with one-tap Route Cache flushing."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Battery Saver & Low Data Mode",
                    description: "Dynamically relaxes background polling to 10s when idle, significantly reducing iPhone battery drain and mobile data consumption."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Unified Ecosystem Versioning",
                    description: "Synchronized PC Desktop, Web API, and iOS companion app to v3.7.0 (Build 23), eliminating outdated fallback badges."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.9 (Build 22)",
            releaseDate: "October 2026",
            isLatest: false,
            highlight: "PC 24/7 background stability & tray persistence, Tailscale failover launcher, and Cloudflare tunnel recreate controls",
            changes: [
                ChangelogItem(
                    type: .fix,
                    title: "PC 24/7 Long-Running Stability",
                    description: "Fixed desktop app exiting after long runs by locking Avalonia ShutdownMode to explicit shutdown, preventing hidden background closures."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Auto-Restoring System Tray Icon",
                    description: "Hooked Win32 TaskbarCreated message to automatically restore the PC system tray icon after Windows Explorer restarts, sleep, or lock screen."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "WMI Leak & Timer Exception Hardening",
                    description: "Disposed COM searchers in HardwareTelemetryService to eliminate resource leaks and guarded all background timers against unhandled crash states."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "iOS Tailscale Failover Assistant",
                    description: "Added actionable offline banner that lets you launch Tailscale VPN directly when Cloudflare Tunnel and local Wi-Fi are unreachable."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Manual Tunnel Recreate (iOS & Web)",
                    description: "Added one-tap Cloudflare Quick Tunnel recreation from both the iOS dashboard and Web Dashboard to quickly generate a fresh public URL."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.8 (Build 21)",
            releaseDate: "September 2026",
            isLatest: false,
            highlight: "Tailscale third-tier failover, Cloudflare tunnel auto-restart watchdog, and off-site 'Enable Tailscale' alerts",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Tailscale Failover Route",
                    description: "The app now falls back from LAN to Cloudflare to a private Tailscale 100.x address automatically, and hops back as soon as faster routes return."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Cloudflare Tunnel Auto-Recreate",
                    description: "When a connection is established while the managed Quick Tunnel is down, the app asks the PC to rerun cloudflared and adopts the fresh trycloudflare.com URL."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Enable Tailscale Reminder",
                    description: "A local notification prompts you to switch on the Tailscale VPN when Cloudflare and LAN are both unreachable while you're off-site."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.7 (Build 20)",
            releaseDate: "September 2026",
            isLatest: false,
            highlight: "PC Setup Wizard deadlock resolution, Velopack 1.2 update lifecycle integration, win-x64 packaging alignment, and UI thread safety",
            changes: [
                ChangelogItem(
                    type: .fix,
                    title: "PC Setup Wizard Deadlock Resolution",
                    description: "Fixed startup routing in App.axaml.cs to seamlessly bypass the initial account creation wizard when user accounts already exist in the database, preventing 'Username already exists' lockouts."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Setup Wizard Credential Linking",
                    description: "SetupWizardWindow now automatically verifies existing account credentials if entered, linking the session and completing setup rather than halting with an error."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Velopack 1.2 & Entry Point Alignment",
                    description: "Upgraded Velopack library to 1.2.158, placed VelopackApp.Build().Run() at the direct entry point of Program.Main, and added explicit win-x64 runtime targeting."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Graceful Optional Service Setup",
                    description: "Defaulted FTP, SQL, and Mailchimp integration checkboxes to unchecked in initial setup, and auto-unchecks empty configurations so users can complete setup friction-free."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "Desktop UI Thread Safety & Shutdown Protection",
                    description: "Configured ShutdownMode to OnLastWindowClose to safeguard window transitions, and wrapped background telemetry control lookups in UI thread dispatchers."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.6 (Build 19)",
            releaseDate: "September 2026",
            isLatest: false,
            highlight: "Intel Arc GPU telemetry fix, Live Activities & Dynamic Island lifecycle resolution, auto-dismissing backup completion banners, and authentic branding",
            changes: [
                ChangelogItem(
                    type: .fix,
                    title: "Intel Arc & Non-NVIDIA GPU Telemetry Fix",
                    description: "Resolved 'N/A' temperature, load, and VRAM for Intel Arc A380 and AMD GPUs. Dedicated VRAM usage, 64-bit capacity, and active thermal states now report live across Web, Desktop, and iOS."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Auto-Dismissing Backup Completion Banner",
                    description: "Backup completion banner in iOS Companion now displays 'COMPLETED' upon successful sync, includes a quick-dismiss ('X') button, and automatically hides after 10 seconds."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "iOS Dynamic Island & Live Activities Fix",
                    description: "Fixed Dynamic Island activity state restoration and stale session handling, added frequent update entitlements, ensuring Live Activities present reliably."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Real-time Host Telemetry & Dynamic Polling",
                    description: "Desktop app now refreshes hardware metrics every 3 seconds; Web Dashboard and iOS companion poll every 2.5s and accelerate during active backups for true real-time visibility."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Web Dashboard Active Transfer Progress Bar",
                    description: "Added a sleek, high-visibility animated progress bar and live completion percentage in the Web Dashboard active backup banner."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Authentic App Branding & Logo Integration",
                    description: "Replaced generic emoji shield with the authentic PinayPal app icon from Assets/logo.ico in Web Dashboard headers and login screens."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.5 (Build 18)",
            releaseDate: "September 2026",
            isLatest: false,
            highlight: "Host PC hardware telemetry with live CPU & GPU temperatures, dedicated resource cards across Web, Desktop, and iOS Companion apps",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Live CPU & GPU Temperature Telemetry",
                    description: "Direct real-time hardware sensor readings for CPU temperature (°C) and GPU temperature (°C) from the PC running the backup engine."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Dedicated Host PC Hardware Cards",
                    description: "Brand-new dedicated cards in Web Dashboard, Desktop App, and iOS Companion clearly highlighting metrics as host resources of the PC where the backup engine is executing."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Extended GPU Diagnostics & Power Metrics",
                    description: "Live GPU model identification, load percentage, dedicated VRAM allocation (used / total), and active power draw (Watts) via direct GPU monitor."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Host Processor & Thermal Health Status",
                    description: "CPU model identification, physical cores / logical threads, processor load %, and thermal health classification (Cool / Optimal / Warm / Hot)."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Restful Hardware Telemetry Endpoint",
                    description: "Added /api/hardware/telemetry and enriched /api/status payload with host system specifications and sensor data for remote monitoring."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.4 (Build 17)",
            releaseDate: "September 2026",
            isLatest: false,
            highlight: "Cloudflare Quick Tunnel (temp websites), dual-tier LAN & Tunnel pairing, iOS profile avatar upload, and multi-channel email & disconnect alerts",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "Cloudflare Quick Tunnel (Temp Website)",
                    description: "Spin up instant, zero-account public temporary websites via Cloudflare tunnel for remote pairing and monitoring anywhere outside your local Wi-Fi."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Dual-Tier Failover & Routing Indicator",
                    description: "iOS app seamlessly fails over between local LAN and Cloudflare Tunnel fallback, with live routing chips (🟢 LAN vs 🟣 Tunnel) and manual toggle."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Profile Avatar Upload on iOS",
                    description: "Upload profile photos straight from iOS Photo Library via PhotosPicker with instant real-time synchronization to desktop PC and Web Dashboard."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Multi-Channel Email & Disconnect Alerts",
                    description: "Desktop engine and Web Dashboard now send customizable email alerts for connection loss, tunnel drops, backup success/failures, or stale backups (>24h)."
                ),
                ChangelogItem(
                    type: .fix,
                    title: "PC Desktop Profile Avatar Persistence",
                    description: "Resolved issue where desktop profile avatars were not persisted upon app restart; optimized with lock-free file streaming and per-user cache."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Enhanced QR Pairing Experience",
                    description: "Pairing QR codes embed local network endpoints alongside active Cloudflare Quick Tunnel fallback for instant one-tap scanning and connection."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.3",
            releaseDate: "September 2026",
            isLatest: false,
            highlight: "120Hz ProMotion display unlock, background Live Activity sync engine, Home backup progress HUD, WWDC 2025 Liquid Glass navigation, and refreshed app & notification icon suite",
            changes: [
                ChangelogItem(
                    type: .feature,
                    title: "120Hz ProMotion Display Performance",
                    description: "Removed iOS 60Hz frame rate clamp with CADisableMinimumFrameDurationOnPhone, enabling buttery-smooth 120fps animations on iPhone 13 Pro through iPhone 17."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Background Live Activity & Dynamic Island Engine",
                    description: "Added background execution tasks and UIBackgroundModes so Dynamic Island and Lock Screen Live Activities continuously update even when the device is locked or minimized."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Home Screen Real-Time Backup Progress HUD",
                    description: "Re-engineered active backup card with real-time percentage indicators, dynamic service accent glows, a high-precision progress bar, and instant emergency stop."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "WWDC 2025 Liquid Glass Navigation Island",
                    description: "Re-engineered navigation and top header into floating Liquid Glass islands with continuous sliding active lens, viscous spring physics, 135° specular rim highlights, and tactile rigid haptics."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Reimagined Liquid Glass App Icon & PinayPal Backup Name",
                    description: "Updated app name to 'PinayPal Backup'. Rebuilt app icon with a solid porcelain-white 3D volumetric emblem, concentric liquid glass rings, and dedicated notification icon suite."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Remote Sync Verification & Outdated Detection",
                    description: "Directly inspects local archive files and remote server timestamps to accurately detect stale backups across Web Dashboard, PC app, and iOS app."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Automations & Schedules Tab",
                    description: "Brand new high-value main tab featuring live countdown clocks, auto-scan interval chips, quick-run backup actions, and instant remote sync checks."
                ),
                ChangelogItem(
                    type: .feature,
                    title: "Outdated Backup Alerts & App Icon Badges",
                    description: "iOS app alerts you when backups are outdated, displaying warning banners, badged service cards, and updating the home screen app icon badge count."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Settings Tabs & Console Overhaul",
                    description: "Moved the Live Console directly into Settings with a redesigned, card-based category picker, replacing the main logs tab with Automations."
                ),
                ChangelogItem(
                    type: .improvement,
                    title: "Light Theme Contrast Polish",
                    description: "Dynamic color tokens and high-contrast styling across all views, eliminating hardcoded white text on light pearl glass cards."
                )
            ]
        ),
        ChangelogRelease(
            version: "v3.6.0",
            releaseDate: "September 2026",
            isLatest: false,
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
