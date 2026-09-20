import SwiftUI
import LocalAuthentication

public struct ServerConfigSheet: View {
    @Environment(\.dismiss) var dismiss
    @ObservedObject var api: PinayPalAPIService
    @ObservedObject var authManager: BiometricAuthManager

    @State private var selectedSection: Int = 0 // 0 = Security & Face ID, 1 = Remote PC Manager, 2 = Connection

    // Connection States
    @State private var inputUrl: String = ""
    @State private var inputPin: String = ""
    @State private var isTesting = false
    @State private var testResult: String? = nil

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
                LiquidTheme.backgroundDark.ignoresSafeArea()

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
        HStack(spacing: 6) {
            segmentButton(title: "Face ID", icon: "faceid", index: 0)
            segmentButton(title: "Remote PC", icon: "desktopcomputer", index: 1)
            segmentButton(title: "Connection", icon: "antenna.radiowaves.left.and.right", index: 2)
        }
        .padding(4)
        .background(Color.white.opacity(0.06))
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
                            Text(String(format: "%02d:00", h)).tag(h)
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
        HStack {
            Text(label)
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(LiquidTheme.textPrimary)

            Spacer()

            HStack(spacing: 4) {
                Picker("Hour", selection: hour) {
                    ForEach(0..<24, id: \.self) { h in
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
            }
        }
    }

    // MARK: - Section 2: Connection
    private var connectionSection: some View {
        VStack(spacing: 16) {
            VStack(spacing: 16) {
                VStack(alignment: .leading, spacing: 8) {
                    Text("TUNNEL / HOST URL")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)

                    TextField("https://your-tunnel.trycloudflare.com", text: $inputUrl)
                        .keyboardType(.URL)
                        .autocapitalization(.none)
                        .disableAutocorrection(true)
                        .padding(14)
                        .background(Color(red: 0.05, green: 0.07, blue: 0.1))
                        .cornerRadius(10)
                        .foregroundColor(.white)
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .strokeBorder(Color.white.opacity(0.12), lineWidth: 1)
                        )
                }

                VStack(alignment: .leading, spacing: 8) {
                    Text("WEB ACCESS PIN (OPTIONAL)")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundColor(LiquidTheme.textSecondary)

                    SecureField("Enter PIN if configured", text: $inputPin)
                        .padding(14)
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

            // Cloudflare Tunnel Guide Note
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Image(systemName: "lightbulb.fill")
                        .foregroundColor(LiquidTheme.gold)
                    Text("Cloudflare Tunnel Host Flag")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(LiquidTheme.gold)
                }

                Text("Run your tunnel on Windows with the host header flag:")
                    .font(.system(size: 12))
                    .foregroundColor(LiquidTheme.textSecondary)

                Text("cloudflared tunnel --url http://localhost:8080 --http-host-header localhost")
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundColor(LiquidTheme.textPrimary)
                    .padding(10)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.black.opacity(0.4))
                    .cornerRadius(8)
            }
            .padding(16)
            .liquidGlassCard(cornerRadius: 14, glow: LiquidTheme.cyan.opacity(0.1))

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
                    Text("Save Connection")
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
        api.saveSettings(url: inputUrl, pin: inputPin)
        dismiss()
    }
}
