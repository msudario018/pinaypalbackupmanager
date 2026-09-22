import SwiftUI
import LocalAuthentication

public struct ServerConfigSheet: View {
    @Environment(\.dismiss) var dismiss
    @ObservedObject var api: PinayPalAPIService
    @ObservedObject var authManager: BiometricAuthManager
    @ObservedObject private var notificationService = NotificationService.shared
    @ObservedObject private var liveActivityManager = BackupLiveActivityManager.shared

    @Environment(\.colorScheme) var colorScheme
    @AppStorage("pp_theme_mode") private var themeMode: String = "dark"
    @AppStorage("pp_biometrics_critical") private var biometricsCritical: Bool = false
    @AppStorage("pp_low_disk_threshold") private var lowDiskThreshold: Double = 88.0
    @AppStorage("pp_haptic_level") private var hapticLevel: String = "crisp"
    @AppStorage("pp_live_activities_enabled") private var liveActivitiesEnabled: Bool = true
    @AppStorage("pp_notify_reminder") private var notifyReminder: Bool = true
    @AppStorage("pp_notify_daily_digest") private var notifyDailyDigest: Bool = true

    @State private var selectedSection: Int = 0 // 0 = Security, 1 = Remote PC, 2 = Appearance, 3 = Alerts & Net

    // Connection States
    @State private var inputUrl: String = ""
    @State private var inputPin: String = ""
    @State private var failoverUrl: String = UserDefaults.standard.string(forKey: "pp_failover_url") ?? ""
    @State private var bgSyncEnabled: Bool = UserDefaults.standard.bool(forKey: "pp_bg_sync_enabled")
    @State private var notifyFailure: Bool = UserDefaults.standard.object(forKey: "pp_notify_failure") == nil ? true : UserDefaults.standard.bool(forKey: "pp_notify_failure")
    @State private var notifySuccess: Bool = UserDefaults.standard.bool(forKey: "pp_notify_success")
    @State private var pollIntervalSec: Int = UserDefaults.standard.integer(forKey: "pp_poll_interval") == 0 ? 4 : UserDefaults.standard.integer(forKey: "pp_poll_interval")
    @State private var isTesting = false
    @State private var testResult: String? = nil

    // Wake-on-LAN States
    @State private var wolMacAddress: String = UserDefaults.standard.string(forKey: "pp_wol_mac") ?? ""
    @State private var wolResult: String? = nil
    @State private var isSendingWol: Bool = false

    // Biometrics States
    @State private var enableBiometrics: Bool = UserDefaults.standard.bool(forKey: "pp_biometrics_enabled")
    @State private var isTestingBio: Bool = false
    @State private var bioTestResult: String? = nil
    @State private var bioTestSuccess: Bool = false

    // Remote PC Manager States
    @State private var ftpHour: Int = 22
    @State private var ftpMin: Int = 0
    @State private var sqlHour: Int = 17
    @State private var sqlMin: Int = 0
    @State private var mcHour: Int = 18
    @State private var mcMin: Int = 0
    @State private var healthHour: Int = 8
    @State private var retentionDays: Int = 7
    @State private var healthEnabled: Bool = true
    @State private var autoStartWindows: Bool = false
    @State private var notificationSound: Bool = true
    @State private var isSavingRemote: Bool = false
    @State private var remoteSaveResult: String? = nil
    @State private var showEmergencyAlert: Bool = false
    @State private var emergencyResult: String? = nil

    public var body: some View {
        NavigationStack {
            ZStack {
                LiquidTheme.background(for: colorScheme).ignoresSafeArea()

                VStack(spacing: 0) {
                    // Segmented Section Picker
                    segmentedPicker
                        .padding(.horizontal, 16)
                        .padding(.top, 12)
                        .padding(.bottom, 16)

                    ScrollView {
                        VStack(spacing: 20) {
                            if selectedSection == 0 {
                                securityFaceIdSection
                            } else if selectedSection == 1 {
                                remotePcManagerSection
                            } else if selectedSection == 2 {
                                appearanceSection
                            } else {
                                connectionSection
                            }

                            Spacer().frame(height: 40)
                        }
                        .padding(.horizontal, 16)
                    }
                }
            }
            .navigationTitle("Dashboard Settings")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                        .foregroundColor(LiquidTheme.textSecondary)
                }
            }
        }
        .onAppear {
            inputUrl = api.serverUrl
            inputPin = api.accessPin
            loadRemoteSettingsIntoState()
        }
        .alert("Emergency Stop PC Backups?", isPresented: $showEmergencyAlert) {
            Button("Stop All Backups", role: .destructive) {
                executeEmergencyStop()
            }
            Button("Cancel", role: .cancel) { }
        } message: {
            Text("This will immediately send a halt signal to all running and queued backup tasks on your desktop PC.")
        }
    }

    // MARK: - Segmented Header
    private var segmentedPicker: some View {
        HStack(spacing: 4) {
            segmentButton(title: "Security", icon: "faceid", index: 0)
            segmentButton(title: "Remote PC", icon: "desktopcomputer", index: 1)
            segmentButton(title: "Theme", icon: "circle.lefthalf.filled", index: 2)
            segmentButton(title: "Alerts", icon: "bell.badge.fill", index: 3)
        }
        .padding(4)
        .background(colorScheme == .light ? Color.black.opacity(0.06) : Color.white.opacity(0.06))
        .clipShape(Capsule(style: .continuous))
    }

    private func segmentButton(title: String, icon: String, index: Int) -> some View {
        Button {
            withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) {
                selectedSection = index
            }
            let haptic = UIImpactFeedbackGenerator(style: .light)
            haptic.impactOccurred()
        } label: {
            HStack(spacing: 5) {
                Image(systemName: icon)
                    .font(.system(size: 12))
                Text(title)
                    .font(.system(size: 12, weight: .bold, design: .rounded))
            }
            .foregroundColor(selectedSection == index ? .black : LiquidTheme.textSecondary)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 8)
            .background {
                if selectedSection == index {
                    Capsule(style: .continuous)
                        .fill(LiquidTheme.gold)
                        .shadow(color: LiquidTheme.gold.opacity(0.35), radius: 6, y: 2)
                }
            }
        }
    }

    // MARK: - Section 0: Face ID & Biometric Security
    private var securityFaceIdSection: some View {
        VStack(spacing: 16) {
            // Hardware Status Card
            HStack(spacing: 16) {
                ZStack {
                    Circle()
                        .fill(LiquidTheme.gold.opacity(0.15))
                        .frame(width: 54, height: 54)

                    Image(systemName: authManager.biometricType == .faceID ? "faceid" : "touchid")
                        .font(.system(size: 26))
                        .foregroundColor(LiquidTheme.gold)
                }

                VStack(alignment: .leading, spacing: 4) {
                    Text(authManager.biometricType == .faceID ? "Face ID Recognition" : "Touch ID Security")
                        .font(.system(size: 16, weight: .bold, design: .rounded))
                        .foregroundColor(LiquidTheme.textPrimary)

                    HStack(spacing: 6) {
                        Circle()
                            .fill(enableBiometrics ? LiquidTheme.emerald : LiquidTheme.textSecondary)
                            .frame(width: 6, height: 6)

                        Text(enableBiometrics ? "Active & Enforced on Launch" : "Disabled (App Unlocked)")
                            .font(.system(size: 12))
                            .foregroundColor(enableBiometrics ? LiquidTheme.emerald : LiquidTheme.textSecondary)
                    }
                }
                Spacer()
            }
            .padding(16)
            .liquidGlassCard(cornerRadius: 16)

            // Settings & Toggle Card
            VStack(spacing: 16) {
                Toggle(isOn: Binding(
                    get: { enableBiometrics },
                    set: { newVal in
                        enableBiometrics = newVal
                        UserDefaults.standard.set(newVal, forKey: "pp_biometrics_enabled")
                        let haptic = UINotificationFeedbackGenerator()
                        haptic.notificationOccurred(.success)
                    }
                )) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text("Require Face ID / Touch ID")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(LiquidTheme.textPrimary)

                        Text("Prompt for biometric unlock each time you open PinayPal")
                            .font(.system(size: 11))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                }
                .tint(LiquidTheme.gold)

                Divider().background(Color.white.opacity(0.1))

                // Test Face ID Button
                Button {
                    testFaceId()
                } label: {
                    HStack(spacing: 8) {
                        if isTestingBio {
                            ProgressView().tint(.white).padding(.trailing, 4)
                        } else {
                            Image(systemName: "checkmark.shield.fill")
                                .foregroundColor(LiquidTheme.cyan)
                        }
                        Text(isTestingBio ? "Scanning Biometrics..." : "Test Face ID Recognition Now")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(.white)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 12)
                    .background(Color.white.opacity(0.08))
                    .cornerRadius(10)
                    .overlay(
                        RoundedRectangle(cornerRadius: 10)
                            .strokeBorder(Color.white.opacity(0.15), lineWidth: 1)
                    )
                }

                if let res = bioTestResult {
                    HStack(spacing: 6) {
                        Image(systemName: bioTestSuccess ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                        Text(res)
                    }
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(bioTestSuccess ? LiquidTheme.emerald : LiquidTheme.coral)
                    .padding(.top, 2)
                }

                Divider().background(Color.white.opacity(0.1))

                // Lock App Shield Immediately
                Button {
                    lockAppImmediately()
                } label: {
                    HStack(spacing: 8) {
                        Image(systemName: "lock.fill")
                        Text("Lock App Now")
                    }
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(LiquidTheme.coral)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 12)
                    .background(LiquidTheme.coral.opacity(0.1))
                    .cornerRadius(10)
                    .overlay(
                        RoundedRectangle(cornerRadius: 10)
                            .strokeBorder(LiquidTheme.coral.opacity(0.25), lineWidth: 1)
                    )
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)

            // Critical Action Confirmation Card
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Image(systemName: "shield.lefthalf.filled")
                        .foregroundColor(LiquidTheme.coral)
                    Text("CRITICAL ACTION PROTECTION")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.coral)
                }

                Toggle(isOn: $biometricsCritical) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Require Face ID for Critical Actions")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                        Text("Prompts for biometric verification before Emergency Stop, manual backups, or schedule edits")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                }
                .tint(LiquidTheme.coral)
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)

            // Explanatory note
            VStack(alignment: .leading, spacing: 8) {
                HStack(spacing: 6) {
                    Image(systemName: "info.circle.fill")
                        .foregroundColor(LiquidTheme.cyan)
                    Text("Security Information")
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(LiquidTheme.cyan)
                }

                Text("Face ID credentials are processed strictly on-device through Apple's Secure Enclave. Enabling Face ID guarantees that unauthorized users cannot view your database backups, FTP credentials, or remotely trigger server tasks.")
                    .font(.system(size: 11))
                    .foregroundColor(LiquidTheme.textSecondary)
                    .lineSpacing(3)
            }
            .padding(16)
            .liquidGlassCard(cornerRadius: 14)
        }
    }

    // MARK: - Section 2: Theme, Appearance & Live Activities
    private var appearanceSection: some View {
        VStack(spacing: 16) {
            // Theme Mode Card
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Image(systemName: "paintpalette.fill")
                        .foregroundColor(LiquidTheme.gold)
                    Text("APPEARANCE & DISPLAY THEME")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.gold)
                }

                Text("Choose your visual style. Obsidian Dark features deep blacks and radiant specular glows. Pearlescent Light delivers a crisp, frosted optical glass feel.")
                    .font(.system(size: 12))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    .lineSpacing(3)

                Picker("Theme Mode", selection: $themeMode) {
                    Text("Auto (System)").tag("system")
                    Text("Dark (Obsidian)").tag("dark")
                    Text("Light (Pearlescent)").tag("light")
                }
                .pickerStyle(.segmented)
                .onChange(of: themeMode) { _ in
                    let haptic = UIImpactFeedbackGenerator(style: .medium)
                    haptic.impactOccurred()
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)

            // Live Activities & Dynamic Island Card
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Image(systemName: "waveform.circle.fill")
                        .foregroundColor(LiquidTheme.cyan)
                    Text("LIVE ACTIVITIES & DYNAMIC ISLAND")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.cyan)
                }

                Toggle(isOn: $liveActivitiesEnabled) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Enable Live Activities")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                        Text("Show active backup progress ring and transfer telemetry in Dynamic Island & Lock Screen")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                }
                .tint(LiquidTheme.cyan)

                Divider().background(Color.white.opacity(0.1))

                VStack(alignment: .leading, spacing: 8) {
                    diagnosticsRow(
                        title: "Live Activity status",
                        value: liveActivityManager.availabilityDescription,
                        isHealthy: liveActivityManager.availabilityDescription == "Ready on this device"
                    )
                    diagnosticsRow(
                        title: "Notification status",
                        value: notificationService.authorizationDescription,
                        isHealthy: notificationService.isAuthorized
                    )

                    Text(liveActivityManager.lastDiagnosticMessage)
                        .font(.system(size: 10))
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    Text(notificationService.lastDiagnosticMessage)
                        .font(.system(size: 10))
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

                    HStack(spacing: 10) {
                        Button {
                            liveActivityManager.startTestActivity()
                        } label: {
                            Label("Test Live Activity", systemImage: "rectangle.inset.filled.and.person.filled")
                                .frame(maxWidth: .infinity, minHeight: 44)
                        }
                        .buttonStyle(.bordered)
                        .tint(LiquidTheme.cyan)

                        Button {
                            Task {
                                if !notificationService.isAuthorized {
                                    _ = await notificationService.requestAuthorization()
                                }
                                if notificationService.isAuthorized {
                                    notificationService.sendTestNotification()
                                }
                            }
                        } label: {
                            Label("Test Alert", systemImage: "bell.and.waves.left.and.right.fill")
                                .frame(maxWidth: .infinity, minHeight: 44)
                        }
                        .buttonStyle(.bordered)
                        .tint(LiquidTheme.gold)
                    }
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)

            // Tactile Haptics Card
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Image(systemName: "hand.tap.fill")
                        .foregroundColor(LiquidTheme.purple)
                    Text("TACTILE HAPTIC INTENSITY")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.purple)
                }

                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Haptic Feedback Profile")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                        Text("Vibration tactile response level on buttons and dock drag")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                    Spacer()
                    Picker("Haptics", selection: $hapticLevel) {
                        Text("Subtle").tag("subtle")
                        Text("Crisp").tag("crisp")
                        Text("Heavy").tag("heavy")
                        Text("Off").tag("off")
                    }
                    .pickerStyle(.menu)
                    .tint(LiquidTheme.gold)
                    .onChange(of: hapticLevel) { level in
                        switch level {
                        case "subtle": UIImpactFeedbackGenerator(style: .light).impactOccurred()
                        case "crisp": UIImpactFeedbackGenerator(style: .medium).impactOccurred()
                        case "heavy": UIImpactFeedbackGenerator(style: .heavy).impactOccurred()
                        default: break
                        }
                    }
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)
        }
    }

    private func diagnosticsRow(title: String, value: String, isHealthy: Bool) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: isHealthy ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                .foregroundColor(isHealthy ? LiquidTheme.emerald : LiquidTheme.gold)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Text(value)
                    .font(.system(size: 10))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
        }
    }

    // MARK: - Section 1: Remote PC Management (Realtime)
    private var remotePcManagerSection: some View {
        VStack(spacing: 16) {
            // Live Real-Time Banner
            HStack(spacing: 10) {
                Image(systemName: "bolt.fill")
                    .foregroundColor(LiquidTheme.gold)
                    .font(.system(size: 16))

                VStack(alignment: .leading, spacing: 2) {
                    Text("Real-Time Desktop Synchronization")
                        .font(.system(size: 13, weight: .bold, design: .rounded))
                        .foregroundColor(LiquidTheme.textPrimary)

                    Text("Changes saved here update your PC app and GUI instantly.")
                        .font(.system(size: 11))
                        .foregroundColor(LiquidTheme.textSecondary)
                }
                Spacer()
            }
            .padding(14)
            .liquidGlassCard(cornerRadius: 14, glow: LiquidTheme.gold.opacity(0.15))

            // Manila Daily Schedules Card
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Image(systemName: "clock.fill")
                        .foregroundColor(LiquidTheme.gold)
                    Text("DAILY BACKUP SCHEDULES (MANILA TIME UTC+8)")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.gold)
                }

                // FTP Sync
                schedulePickerRow(label: "Website (FTP) Daily Sync", hour: $ftpHour, minute: $ftpMin)
                Divider().background(Color.white.opacity(0.08))

                // SQL Sync
                schedulePickerRow(label: "SQL Database Daily Sync", hour: $sqlHour, minute: $sqlMin)
                Divider().background(Color.white.opacity(0.08))

                // Mailchimp Sync
                schedulePickerRow(label: "Mailchimp Daily Sync", hour: $mcHour, minute: $mcMin)
                Divider().background(Color.white.opacity(0.08))

                // Health Check
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Automated Health Check")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary)
                        Text("Runs diagnostics and verifies disk")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                    Spacer()
                    Picker("Hour", selection: $healthHour) {
                        ForEach(0..<24, id: \.self) { h in
                            let h12 = (h % 12 == 0) ? 12 : (h % 12)
                            let ampm = h < 12 ? "AM" : "PM"
                            Text(String(format: "%d:00 %@", h12, ampm)).tag(h)
                        }
                    }
                    .pickerStyle(.menu)
                    .tint(LiquidTheme.gold)
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)

            // Operation & Retention Card
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Image(systemName: "slider.horizontal.3")
                        .foregroundColor(LiquidTheme.cyan)
                    Text("RETENTION & SYSTEM AUTOMATION")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.cyan)
                }

                // Retention Stepper
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Backup Retention Period")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary)
                        Text("Files older than this are safely pruned")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                    Spacer()
                    Stepper("\(retentionDays) Days", value: $retentionDays, in: 1...90)
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(LiquidTheme.gold)
                }

                Divider().background(Color.white.opacity(0.08))

                Toggle(isOn: $healthEnabled) {
                    Text("Daily Health Check Enabled")
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(LiquidTheme.textPrimary)
                }
                .tint(LiquidTheme.gold)

                Divider().background(Color.white.opacity(0.08))

                Toggle(isOn: $autoStartWindows) {
                    Text("Auto-Start on Windows Boot")
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(LiquidTheme.textPrimary)
                }
                .tint(LiquidTheme.gold)

                Divider().background(Color.white.opacity(0.08))

                Toggle(isOn: $notificationSound) {
                    Text("PC Notification Audio Alerts")
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(LiquidTheme.textPrimary)
                }
                .tint(LiquidTheme.gold)
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)

            // Save Remote Settings Button
            Button {
                saveRemoteSettingsToPc()
            } label: {
                HStack(spacing: 8) {
                    if isSavingRemote {
                        ProgressView().tint(.black)
                    } else {
                        Image(systemName: "arrow.triangle.2.circlepath.circle.fill")
                    }
                    Text(isSavingRemote ? "Syncing with Desktop..." : "Sync Settings to Desktop PC")
                        .font(.system(size: 14, weight: .bold, design: .rounded))
                }
                .foregroundColor(.black)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 15)
                .background(LiquidTheme.gold)
                .cornerRadius(12)
                .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 10, y: 4)
            }

            if let result = remoteSaveResult {
                HStack(spacing: 6) {
                    Image(systemName: result.contains("success") ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    Text(result)
                }
                .font(.system(size: 12, weight: .medium))
                .foregroundColor(result.contains("success") ? LiquidTheme.emerald : LiquidTheme.coral)
                .multilineTextAlignment(.center)
            }

            // Emergency Stop Section
            VStack(spacing: 10) {
                Button {
                    showEmergencyAlert = true
                } label: {
                    HStack(spacing: 8) {
                        Image(systemName: "stop.circle.fill")
                        Text("Emergency Stop PC Backups")
                    }
                    .font(.system(size: 14, weight: .bold, design: .rounded))
                    .foregroundColor(.white)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 14)
                    .background(LiquidTheme.coral)
                    .cornerRadius(12)
                    .shadow(color: LiquidTheme.coral.opacity(0.4), radius: 8, y: 3)
                }

                if let err = emergencyResult {
                    Text(err)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(LiquidTheme.coral)
                }
            }
            .padding(.top, 6)
        }
    }

    private func schedulePickerRow(label: String, hour: Binding<Int>, minute: Binding<Int>) -> some View {
        let hour12Binding = Binding<Int>(
            get: {
                let h = hour.wrappedValue % 12
                return h == 0 ? 12 : h
            },
            set: { new12 in
                let isPM = hour.wrappedValue >= 12
                if isPM {
                    hour.wrappedValue = (new12 == 12 ? 12 : new12 + 12)
                } else {
                    hour.wrappedValue = (new12 == 12 ? 0 : new12)
                }
            }
        )

        let isPmBinding = Binding<Bool>(
            get: { hour.wrappedValue >= 12 },
            set: { newIsPm in
                let cur12 = hour.wrappedValue % 12
                if newIsPm {
                    hour.wrappedValue = (cur12 == 0 ? 12 : cur12 + 12)
                } else {
                    hour.wrappedValue = (cur12 == 0 ? 0 : cur12)
                }
            }
        )

        return HStack {
            Text(label)
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(LiquidTheme.textPrimary)

            Spacer()

            HStack(spacing: 4) {
                Picker("Hour", selection: hour12Binding) {
                    ForEach(1...12, id: \.self) { h in
                        Text(String(format: "%02d", h)).tag(h)
                    }
                }
                .pickerStyle(.menu)
                .tint(LiquidTheme.gold)

                Text(":")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary)

                Picker("Minute", selection: minute) {
                    ForEach([0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55], id: \.self) { m in
                        Text(String(format: "%02d", m)).tag(m)
                    }
                }
                .pickerStyle(.menu)
                .tint(LiquidTheme.gold)

                Picker("AM/PM", selection: isPmBinding) {
                    Text("AM").tag(false)
                    Text("PM").tag(true)
                }
                .pickerStyle(.menu)
                .tint(LiquidTheme.gold)
            }
        }
    }

    // MARK: - Section 2: App, Account & Network Connection
    private var connectionSection: some View {
        VStack(spacing: 18) {
            // 1. Current Authenticated User Account Card
            VStack(alignment: .leading, spacing: 14) {
                HStack(spacing: 12) {
                    ZStack {
                        Circle()
                            .fill(LiquidTheme.gold.opacity(0.15))
                            .frame(width: 44, height: 44)
                        Image(systemName: "person.crop.circle.fill")
                            .font(.system(size: 24))
                            .foregroundColor(LiquidTheme.gold)
                    }

                    VStack(alignment: .leading, spacing: 2) {
                        HStack(spacing: 6) {
                            Text(api.currentUser?.username ?? "Desktop User")
                                .font(.system(size: 15, weight: .bold, design: .rounded))
                                .foregroundColor(LiquidTheme.textPrimary)

                            Text(api.currentUser?.role ?? "User")
                                .font(.system(size: 10, weight: .black))
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(LiquidTheme.gold.opacity(0.2))
                                .foregroundColor(LiquidTheme.gold)
                                .cornerRadius(6)
                        }

                        Text(api.currentUser?.email ?? "Connected to desktop SQLite database")
                            .font(.system(size: 11))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }

                    Spacer()
                }

                Divider().background(Color.white.opacity(0.08))

                HStack(spacing: 10) {
                    Button {
                        Task {
                            await api.logout()
                            dismiss()
                        }
                    } label: {
                        HStack(spacing: 4) {
                            Image(systemName: "rectangle.portrait.and.arrow.right")
                            Text("Sign Out")
                        }
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(LiquidTheme.coral)
                        .padding(.vertical, 8)
                        .padding(.horizontal, 12)
                        .background(LiquidTheme.coral.opacity(0.12))
                        .cornerRadius(8)
                    }

                    Button {
                        api.disconnectServer()
                        dismiss()
                    } label: {
                        HStack(spacing: 4) {
                            Image(systemName: "bolt.slash.fill")
                            Text("Disconnect Server")
                        }
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)
                        .padding(.vertical, 8)
                        .padding(.horizontal, 12)
                        .background(Color.white.opacity(0.08))
                        .cornerRadius(8)
                    }
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18, glow: LiquidTheme.gold.opacity(0.15))

            // 2. iOS App Settings: Background Monitoring & Local Alerts
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Image(systemName: "bell.badge.fill")
                        .foregroundColor(LiquidTheme.cyan)
                    Text("IOS BACKGROUND MONITORING & ALERTS")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.cyan)
                }

                Toggle(isOn: $bgSyncEnabled) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Run App in Background")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary)
                        Text("Periodically syncs status & verifies scheduled jobs")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                }
                .tint(LiquidTheme.cyan)

                Divider().background(Color.white.opacity(0.08))

                Toggle(isOn: $notifyFailure) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Alert on Backup Failures")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary)
                        Text("Trigger critical notification if FTP or SQL job fails")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                }
                .tint(LiquidTheme.coral)

                Divider().background(Color.white.opacity(0.08))

                Toggle(isOn: $notifySuccess) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Alert on Backup Success")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary)
                        Text("Send quiet summary when daily backups complete")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                }
                .tint(LiquidTheme.emerald)

                Divider().background(Color.white.opacity(0.08))

                Toggle(isOn: $notifyReminder) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Pre-Backup Schedule Reminders")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                        Text("Notify 15 minutes before daily automated backups run")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                }
                .tint(LiquidTheme.gold)

                Divider().background(Color.white.opacity(0.08))

                Toggle(isOn: $notifyDailyDigest) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Daily Summary Digest (9:00 PM)")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                        Text("Daily evening briefing of completed runs and storage consumed")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                }
                .tint(LiquidTheme.purple)

                Divider().background(Color.white.opacity(0.08))

                // Low Disk Space Threshold Slider
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Low Disk Warning Threshold")
                                .font(.system(size: 13, weight: .medium))
                                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                            Text("Alert when storage capacity reaches \(Int(lowDiskThreshold))%")
                                .font(.system(size: 10))
                                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                        }
                        Spacer()
                        Text("\(Int(lowDiskThreshold))%")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(lowDiskThreshold > 90 ? LiquidTheme.coral : LiquidTheme.gold)
                    }

                    Slider(value: $lowDiskThreshold, in: 70...95, step: 1)
                        .tint(lowDiskThreshold > 90 ? LiquidTheme.coral : LiquidTheme.gold)
                }

                Divider().background(Color.white.opacity(0.08))

                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("HUD Live Polling Interval")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(LiquidTheme.textPrimary)
                        Text("How often active HUD requests live metrics")
                            .font(.system(size: 10))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                    Spacer()
                    Picker("Interval", selection: $pollIntervalSec) {
                        Text("2 seconds").tag(2)
                        Text("4 seconds").tag(4)
                        Text("8 seconds").tag(8)
                        Text("15 seconds").tag(15)
                    }
                    .pickerStyle(.menu)
                    .tint(LiquidTheme.gold)
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18, glow: LiquidTheme.cyan.opacity(0.12))

            // 3. Network Connection & Failover Configuration
            VStack(spacing: 14) {
                VStack(alignment: .leading, spacing: 6) {
                    Text("PRIMARY HOST / LOCAL WI-FI URL")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)

                    TextField("http://192.168.1.100:8080", text: $inputUrl)
                        .keyboardType(.URL)
                        .autocapitalization(.none)
                        .disableAutocorrection(true)
                        .padding(12)
                        .background(Color(red: 0.05, green: 0.07, blue: 0.1))
                        .cornerRadius(10)
                        .foregroundColor(.white)
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                        )
                }

                VStack(alignment: .leading, spacing: 6) {
                    Text("REMOTE FAILOVER URL (CLOUDFLARE TUNNEL)")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)

                    TextField("https://backup.yourdomain.com", text: $failoverUrl)
                        .keyboardType(.URL)
                        .autocapitalization(.none)
                        .disableAutocorrection(true)
                        .padding(12)
                        .background(Color(red: 0.05, green: 0.07, blue: 0.1))
                        .cornerRadius(10)
                        .foregroundColor(.white)
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                        )
                }

                VStack(alignment: .leading, spacing: 6) {
                    Text("WEB ACCESS PIN (OPTIONAL)")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)

                    SecureField("Enter PIN if configured", text: $inputPin)
                        .padding(12)
                        .background(Color(red: 0.05, green: 0.07, blue: 0.1))
                        .cornerRadius(10)
                        .foregroundColor(.white)
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                        )
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18)

            // Wake-on-LAN (WOL) Card
            VStack(alignment: .leading, spacing: 14) {
                HStack(spacing: 8) {
                    Image(systemName: "power.circle.fill")
                        .font(.system(size: 16))
                        .foregroundColor(LiquidTheme.emerald)
                    Text("WAKE-ON-LAN (WOL)")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)
                }

                Text("Send a magic packet across your local network to wake up your Windows PC from Sleep or Hibernation.")
                    .font(.system(size: 11))
                    .foregroundColor(LiquidTheme.textSecondary)

                VStack(alignment: .leading, spacing: 6) {
                    Text("PC ETHERNET MAC ADDRESS")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)

                    TextField("e.g. 00:1A:2B:3C:4D:5E", text: $wolMacAddress)
                        .autocapitalization(.allCharacters)
                        .disableAutocorrection(true)
                        .padding(12)
                        .background(Color(red: 0.05, green: 0.07, blue: 0.1))
                        .cornerRadius(10)
                        .foregroundColor(.white)
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                        )
                }

                Button {
                    sendWakeOnLan()
                } label: {
                    HStack(spacing: 8) {
                        if isSendingWol {
                            ProgressView().tint(.white)
                        } else {
                            Image(systemName: "bolt.fill")
                        }
                        Text(isSendingWol ? "Broadcasting Packet..." : "Wake Up PC Now")
                    }
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(.white)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 12)
                    .background(Color.white.opacity(0.1))
                    .cornerRadius(10)
                    .overlay(
                        RoundedRectangle(cornerRadius: 10)
                            .strokeBorder(LiquidTheme.emerald.opacity(0.4), lineWidth: 1)
                    )
                }
                .disabled(isSendingWol || wolMacAddress.isEmpty)

                if let res = wolResult {
                    Text(res)
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(res.contains("sent") ? LiquidTheme.emerald : LiquidTheme.coral)
                }
            }
            .padding(18)
            .liquidGlassCard(cornerRadius: 18, glow: LiquidTheme.emerald.opacity(0.12))

            // Test Connection & Save
            VStack(spacing: 12) {
                Button {
                    testConnection()
                } label: {
                    HStack {
                        if isTesting {
                            ProgressView().tint(.white).padding(.trailing, 6)
                        }
                        Image(systemName: "network")
                        Text(isTesting ? "Testing Connection..." : "Test Connection")
                    }
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(.white)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 14)
                    .background(Color.white.opacity(0.08))
                    .cornerRadius(12)
                    .overlay(
                        RoundedRectangle(cornerRadius: 12)
                            .strokeBorder(Color.white.opacity(0.18), lineWidth: 1)
                    )
                }

                if let res = testResult {
                    Text(res)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(res.contains("Connected") ? LiquidTheme.emerald : LiquidTheme.coral)
                }

                Button {
                    saveConnectionAndDismiss()
                } label: {
                    Text("Save App & Network Settings")
                        .font(.system(size: 15, weight: .bold, design: .rounded))
                        .foregroundColor(.black)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 15)
                        .background(LiquidTheme.gold)
                        .cornerRadius(12)
                }
                .shadow(color: LiquidTheme.gold.opacity(0.35), radius: 10, y: 4)
            }
        }
    }

    // MARK: - Actions
    private func testFaceId() {
        isTestingBio = true
        bioTestResult = nil
        Task {
            let res = await authManager.testBiometrics()
            isTestingBio = false
            bioTestSuccess = res.success
            bioTestResult = res.message
            let feedback = UINotificationFeedbackGenerator()
            feedback.notificationOccurred(res.success ? .success : .error)
        }
    }

    private func sendWakeOnLan() {
        guard !wolMacAddress.isEmpty else { return }
        UserDefaults.standard.set(wolMacAddress, forKey: "pp_wol_mac")
        isSendingWol = true
        wolResult = nil
        Task {
            let res = await WakeOnLanHelper.sendMagicPacket(macAddress: wolMacAddress)
            isSendingWol = false
            if res.success {
                wolResult = "Magic packet sent to \(wolMacAddress)!"
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.success)
            } else {
                wolResult = res.error ?? "Failed to send packet"
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.error)
            }
        }
    }

    private func lockAppImmediately() {
        UserDefaults.standard.set(true, forKey: "pp_biometrics_enabled")
        enableBiometrics = true
        authManager.lock()
        dismiss()
    }

    private func loadRemoteSettingsIntoState() {
        if let s = api.remoteSettings {
            ftpHour = s.ftpDailySyncHourMnl
            ftpMin = s.ftpDailySyncMinuteMnl
            sqlHour = s.sqlDailySyncHourMnl
            sqlMin = s.sqlDailySyncMinuteMnl
            mcHour = s.mailchimpDailySyncHourMnl
            mcMin = s.mailchimpDailySyncMinuteMnl
            healthHour = s.dailyHealthCheckHour
            retentionDays = s.retentionDays
            healthEnabled = s.dailyHealthCheckEnabled
            autoStartWindows = s.autoStartWindows
            notificationSound = s.notificationSound
        } else {
            Task {
                await api.fetchRemoteSettings()
                if let s = api.remoteSettings {
                    ftpHour = s.ftpDailySyncHourMnl
                    ftpMin = s.ftpDailySyncMinuteMnl
                    sqlHour = s.sqlDailySyncHourMnl
                    sqlMin = s.sqlDailySyncMinuteMnl
                    mcHour = s.mailchimpDailySyncHourMnl
                    mcMin = s.mailchimpDailySyncMinuteMnl
                    healthHour = s.dailyHealthCheckHour
                    retentionDays = s.retentionDays
                    healthEnabled = s.dailyHealthCheckEnabled
                    autoStartWindows = s.autoStartWindows
                    notificationSound = s.notificationSound
                }
            }
        }
    }

    private func saveRemoteSettingsToPc() {
        isSavingRemote = true
        remoteSaveResult = nil

        let updated = RemoteSettings(
            ftpDailySyncHourMnl: ftpHour,
            ftpDailySyncMinuteMnl: ftpMin,
            sqlDailySyncHourMnl: sqlHour,
            sqlDailySyncMinuteMnl: sqlMin,
            mailchimpDailySyncHourMnl: mcHour,
            mailchimpDailySyncMinuteMnl: mcMin,
            retentionDays: retentionDays,
            dailyHealthCheckEnabled: healthEnabled,
            dailyHealthCheckHour: healthHour,
            autoStartWindows: autoStartWindows,
            notificationSound: notificationSound
        )

        Task {
            let ok = await api.saveRemoteSettings(updated)
            isSavingRemote = false
            if ok {
                remoteSaveResult = "✓ Settings synced to desktop PC in real-time!"
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.success)
            } else {
                remoteSaveResult = "Failed to sync settings. Ensure PC server is online."
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.error)
            }
        }
    }

    private func executeEmergencyStop() {
        Task {
            let ok = await api.triggerEmergencyStop()
            if ok {
                emergencyResult = "Emergency stop broadcasted to PC!"
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.warning)
            } else {
                emergencyResult = "Failed to broadcast emergency stop."
            }
        }
    }

    private func testConnection() {
        isTesting = true
        testResult = nil
        Task {
            var clean = inputUrl.trimmingCharacters(in: .whitespacesAndNewlines)
            if clean.hasSuffix("/") { clean.removeLast() }
            guard let url = URL(string: "\(clean)/api/status") else {
                isTesting = false
                testResult = "Invalid URL syntax"
                return
            }

            var req = URLRequest(url: url)
            req.timeoutInterval = 5
            if !inputPin.isEmpty {
                req.addValue("Bearer \(inputPin)", forHTTPHeaderField: "Authorization")
            }

            do {
                let (_, response) = try await URLSession.shared.data(for: req)
                if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                    testResult = "✓ Connected Successfully to PinayPal Server!"
                } else {
                    testResult = "Server returned error code: \((response as? HTTPURLResponse)?.statusCode ?? 0)"
                }
            } catch {
                testResult = "Failed to connect: \(error.localizedDescription)"
            }
            isTesting = false
        }
    }

    private func saveConnectionAndDismiss() {
        UserDefaults.standard.set(enableBiometrics, forKey: "pp_biometrics_enabled")
        UserDefaults.standard.set(failoverUrl.trimmingCharacters(in: .whitespacesAndNewlines), forKey: "pp_failover_url")
        UserDefaults.standard.set(bgSyncEnabled, forKey: "pp_bg_sync_enabled")
        UserDefaults.standard.set(notifyFailure, forKey: "pp_notify_failure")
        UserDefaults.standard.set(notifySuccess, forKey: "pp_notify_success")
        UserDefaults.standard.set(pollIntervalSec, forKey: "pp_poll_interval")
        api.saveSettings(url: inputUrl, pin: inputPin)
        dismiss()
    }
}
