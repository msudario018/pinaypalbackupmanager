import SwiftUI

public struct ServerConfigSheet: View {
    @Environment(\.dismiss) var dismiss
    @ObservedObject var api: PinayPalAPIService

    @State private var inputUrl: String = ""
    @State private var inputPin: String = ""
    @State private var enableBiometrics: Bool = UserDefaults.standard.bool(forKey: "pp_biometrics_enabled")
    @State private var isTesting = false
    @State private var testResult: String? = nil

    public var body: some View {
        NavigationStack {
            ZStack {
                LiquidTheme.backgroundDark.ignoresSafeArea()

                ScrollView {
                    VStack(spacing: 24) {
                        // Header Badge
                        HStack {
                            Image(systemName: "antenna.radiowaves.left.and.right")
                                .font(.system(size: 24))
                                .foregroundColor(LiquidTheme.gold)

                            VStack(alignment: .leading, spacing: 2) {
                                Text("Server Connection")
                                    .font(.system(size: 20, weight: .bold, design: .rounded))
                                    .foregroundColor(LiquidTheme.textPrimary)

                                Text("Cloudflare Tunnel or Local LAN Host")
                                    .font(.system(size: 12))
                                    .foregroundColor(LiquidTheme.textSecondary)
                            }
                            Spacer()
                        }
                        .padding(.top, 10)

                        // Input Card
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

                            // Biometrics Toggle
                            Toggle(isOn: $enableBiometrics) {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("Require Face ID / Touch ID")
                                        .font(.system(size: 14, weight: .semibold))
                                        .foregroundColor(LiquidTheme.textPrimary)

                                    Text("Biometric protection when opening app")
                                        .font(.system(size: 11))
                                        .foregroundColor(LiquidTheme.textSecondary)
                                }
                            }
                            .tint(LiquidTheme.gold)
                            .padding(.top, 6)
                        }
                        .padding(20)
                        .liquidGlassCard(cornerRadius: 18)

                        // Cloudflare Tunnel Guide Note
                        VStack(alignment: .leading, spacing: 8) {
                            HStack {
                                Image(systemName: "lightbulb.fill")
                                    .foregroundColor(LiquidTheme.gold)
                                Text("Tip for Cloudflare Tunnels")
                                    .font(.system(size: 13, weight: .bold))
                                    .foregroundColor(LiquidTheme.gold)
                            }

                            Text("Always run your tunnel on Windows with:")
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

                        // Test Connection & Save Buttons
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
                                saveAndDismiss()
                            } label: {
                                Text("Save Configuration")
                                    .font(.system(size: 15, weight: .bold, design: .rounded))
                                    .foregroundColor(.black)
                                    .frame(maxWidth: .infinity)
                                    .padding(.vertical, 15)
                                    .background(LiquidTheme.gold)
                                    .cornerRadius(12)
                            }
                            .shadow(color: LiquidTheme.gold.opacity(0.35), radius: 10, y: 4)
                        }
                        .padding(.top, 10)
                    }
                    .padding(20)
                }
            }
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

    private func saveAndDismiss() {
        UserDefaults.standard.set(enableBiometrics, forKey: "pp_biometrics_enabled")
        api.saveSettings(url: inputUrl, pin: inputPin)
        dismiss()
    }
}
