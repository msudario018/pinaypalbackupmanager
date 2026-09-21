import SwiftUI

public struct ConnectionSetupView: View {
    @EnvironmentObject var api: PinayPalAPIService

    @State private var serverUrlInput: String = "http://192.168.1.100:8080"
    @State private var pinInput: String = ""
    @State private var isScanning: Bool = false
    @State private var isTesting: Bool = false
    @State private var discoveredServers: [(url: String, ping: PingResponse?)] = []
    @State private var testResult: (success: Bool, message: String)? = nil
    @State private var activeTab: Int = 0 // 0: Auto Discover, 1: Manual / Tunnel

    public init() {}

    public var body: some View {
        ZStack {
            LiquidTheme.backgroundDark.ignoresSafeArea()

            // Ambient background glow
            RadialGradient(
                colors: [LiquidTheme.gold.opacity(0.12), LiquidTheme.cyan.opacity(0.06), Color.clear],
                center: .topLeading,
                startRadius: 40,
                endRadius: 400
            )
            .ignoresSafeArea()

            ScrollView(showsIndicators: false) {
                VStack(spacing: 24) {
                    // Header Emblem
                    VStack(spacing: 12) {
                        ZStack {
                            Circle()
                                .fill(LiquidTheme.surfaceDark)
                                .frame(width: 80, height: 80)
                                .overlay(
                                    Circle()
                                        .stroke(LiquidTheme.liquidGoldGradient, lineWidth: 2)
                                )
                                .shadow(color: LiquidTheme.gold.opacity(0.3), radius: 16, x: 0, y: 6)

                            Image(systemName: "antenna.radiowaves.left.and.right")
                                .font(.system(size: 34, weight: .bold))
                                .foregroundStyle(LiquidTheme.liquidGoldGradient)
                        }
                        .padding(.top, 24)

                        Text("Connect to PC Server")
                            .font(.system(size: 24, weight: .black, design: .rounded))
                            .foregroundColor(LiquidTheme.textPrimary)

                        Text("Pair this iPhone with your desktop PinayPal Backup Manager via Wi-Fi or Cloudflare Tunnel")
                            .font(.system(size: 13, weight: .medium, design: .rounded))
                            .foregroundColor(LiquidTheme.textSecondary)
                            .multilineTextAlignment(.center)
                            .padding(.horizontal, 24)
                    }

                    // Mode Switcher (Wi-Fi Auto-Discovery vs Manual/Tunnel)
                    HStack(spacing: 0) {
                        Button(action: { withAnimation(.spring()) { activeTab = 0 } }) {
                            HStack(spacing: 6) {
                                Image(systemName: "wifi")
                                Text("Wi-Fi Discovery")
                            }
                            .font(.system(size: 13, weight: .bold, design: .rounded))
                            .foregroundColor(activeTab == 0 ? .black : LiquidTheme.textSecondary)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 10)
                            .background(activeTab == 0 ? LiquidTheme.gold : Color.clear)
                            .cornerRadius(10)
                        }

                        Button(action: { withAnimation(.spring()) { activeTab = 1 } }) {
                            HStack(spacing: 6) {
                                Image(systemName: "link")
                                Text("Manual / Tunnel")
                            }
                            .font(.system(size: 13, weight: .bold, design: .rounded))
                            .foregroundColor(activeTab == 1 ? .black : LiquidTheme.textSecondary)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 10)
                            .background(activeTab == 1 ? LiquidTheme.gold : Color.clear)
                            .cornerRadius(10)
                        }
                    }
                    .padding(4)
                    .background(Color.white.opacity(0.06))
                    .cornerRadius(14)
                    .padding(.horizontal, 20)

                    // Tab 0: Auto-Discovery
                    if activeTab == 0 {
                        VStack(alignment: .leading, spacing: 16) {
                            HStack {
                                Label("LOCAL WI-FI SCANNER", systemImage: "network")
                                    .font(.system(size: 12, weight: .bold, design: .rounded))
                                    .foregroundColor(LiquidTheme.cyan)

                                Spacer()

                                if isScanning {
                                    ProgressView()
                                        .progressViewStyle(CircularProgressViewStyle(tint: LiquidTheme.cyan))
                                        .scaleEffect(0.8)
                                }
                            }

                            Text("Scan your local Wi-Fi network to automatically locate running PinayPal instances on port 8080.")
                                .font(.system(size: 12))
                                .foregroundColor(LiquidTheme.textSecondary)

                            Button(action: startDiscovery) {
                                HStack {
                                    Image(systemName: isScanning ? "arrow.triangle.2.circlepath" : "magnifyingglass")
                                    Text(isScanning ? "Scanning Local Network..." : "Start Auto-Discovery")
                                }
                                .frame(maxWidth: .infinity)
                            }
                            .modifier(LiquidButtonModifier(accent: LiquidTheme.cyan))
                            .disabled(isScanning)

                            // Discovered Items List
                            if !discoveredServers.isEmpty {
                                VStack(alignment: .leading, spacing: 10) {
                                    Text("DISCOVERED SERVERS (\(discoveredServers.count))")
                                        .font(.system(size: 11, weight: .bold))
                                        .foregroundColor(LiquidTheme.emerald)

                                    ForEach(discoveredServers, id: \.url) { item in
                                        Button(action: { selectDiscoveredServer(item.url) }) {
                                            HStack {
                                                VStack(alignment: .leading, spacing: 3) {
                                                    HStack(spacing: 6) {
                                                        Circle()
                                                            .fill(LiquidTheme.emerald)
                                                            .frame(width: 8, height: 8)
                                                        Text(item.ping?.hostname ?? "Desktop PC")
                                                            .font(.system(size: 14, weight: .bold))
                                                            .foregroundColor(LiquidTheme.textPrimary)
                                                        if let version = item.ping?.version {
                                                            Text("v\(version)")
                                                                .font(.system(size: 10, weight: .bold))
                                                                .padding(.horizontal, 6)
                                                                .padding(.vertical, 2)
                                                                .background(LiquidTheme.emerald.opacity(0.2))
                                                                .foregroundColor(LiquidTheme.emerald)
                                                                .cornerRadius(6)
                                                        }
                                                    }
                                                    Text(item.url)
                                                        .font(.system(size: 12, design: .monospaced))
                                                        .foregroundColor(LiquidTheme.textSecondary)
                                                }

                                                Spacer()

                                                Image(systemName: "chevron.right")
                                                    .font(.system(size: 12, weight: .bold))
                                                    .foregroundColor(LiquidTheme.gold)
                                            }
                                            .padding(14)
                                            .background(LiquidTheme.surfaceDark.opacity(0.8))
                                            .cornerRadius(12)
                                            .overlay(
                                                RoundedRectangle(cornerRadius: 12)
                                                    .stroke(LiquidTheme.gold.opacity(serverUrlInput == item.url ? 0.8 : 0.2), lineWidth: 1)
                                            )
                                        }
                                    }
                                }
                                .padding(.top, 6)
                            }
                        }
                        .padding(20)
                        .modifier(LiquidGlassCardModifier(cornerRadius: 20, glowColor: LiquidTheme.cyan.opacity(0.2)))
                        .padding(.horizontal, 20)
                    }

                    // Tab 1: Manual / Cloudflare Tunnel
                    if activeTab == 1 {
                        VStack(alignment: .leading, spacing: 16) {
                            Label("REMOTE TUNNEL OR LOCAL IP", systemImage: "link.badge.plus")
                                .font(.system(size: 12, weight: .bold, design: .rounded))
                                .foregroundColor(LiquidTheme.gold)

                            VStack(alignment: .leading, spacing: 6) {
                                Text("Server Base URL")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(LiquidTheme.textSecondary)

                                TextField("http://192.168.1.100:8080", text: $serverUrlInput)
                                    .keyboardType(.URL)
                                    .autocapitalization(.none)
                                    .disableAutocorrection(true)
                                    .font(.system(size: 14, design: .monospaced))
                                    .padding(12)
                                    .background(Color.white.opacity(0.06))
                                    .cornerRadius(10)
                                    .overlay(
                                        RoundedRectangle(cornerRadius: 10)
                                            .stroke(Color.white.opacity(0.15), lineWidth: 1)
                                    )
                                    .foregroundColor(LiquidTheme.textPrimary)
                            }

                            // Quick Presets
                            VStack(alignment: .leading, spacing: 6) {
                                Text("QUICK PRESETS")
                                    .font(.system(size: 10, weight: .bold))
                                    .foregroundColor(LiquidTheme.textSecondary)

                                ScrollView(.horizontal, showsIndicators: false) {
                                    HStack(spacing: 8) {
                                        PresetChip(title: "Localhost", value: "http://127.0.0.1:8080", selectedUrl: $serverUrlInput)
                                        PresetChip(title: "192.168.1.x", value: "http://192.168.1.100:8080", selectedUrl: $serverUrlInput)
                                        PresetChip(title: "192.168.0.x", value: "http://192.168.0.100:8080", selectedUrl: $serverUrlInput)
                                        PresetChip(title: "Cloudflare", value: "https://backup.yourdomain.com", selectedUrl: $serverUrlInput)
                                    }
                                }
                            }

                            VStack(alignment: .leading, spacing: 6) {
                                Text("Web Dashboard PIN (Optional)")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(LiquidTheme.textSecondary)

                                SecureField("Leave blank if no PIN required", text: $pinInput)
                                    .keyboardType(.numberPad)
                                    .font(.system(size: 14, design: .monospaced))
                                    .padding(12)
                                    .background(Color.white.opacity(0.06))
                                    .cornerRadius(10)
                                    .overlay(
                                        RoundedRectangle(cornerRadius: 10)
                                            .stroke(Color.white.opacity(0.15), lineWidth: 1)
                                    )
                                    .foregroundColor(LiquidTheme.textPrimary)
                            }
                        }
                        .padding(20)
                        .modifier(LiquidGlassCardModifier(cornerRadius: 20, glowColor: LiquidTheme.gold.opacity(0.2)))
                        .padding(.horizontal, 20)
                    }

                    // Test Connection & Ping Area
                    VStack(spacing: 12) {
                        Button(action: testConnection) {
                            HStack {
                                if isTesting {
                                    ProgressView()
                                        .progressViewStyle(CircularProgressViewStyle(tint: .white))
                                        .scaleEffect(0.8)
                                    Text("Testing Latency...")
                                } else {
                                    Image(systemName: "bolt.fill")
                                    Text("Test Connection")
                                }
                            }
                            .frame(maxWidth: .infinity)
                        }
                        .modifier(LiquidButtonModifier(accent: Color.white.opacity(0.12)))
                        .disabled(isTesting || serverUrlInput.isEmpty)

                        if let result = testResult {
                            HStack(spacing: 8) {
                                Image(systemName: result.success ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
                                    .foregroundColor(result.success ? LiquidTheme.emerald : LiquidTheme.coral)
                                Text(result.message)
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(result.success ? LiquidTheme.emerald : LiquidTheme.coral)
                            }
                            .padding(12)
                            .frame(maxWidth: .infinity)
                            .background((result.success ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.12))
                            .cornerRadius(10)
                        }

                        // Connect and Continue Button
                        Button(action: connectAndProceed) {
                            HStack {
                                Image(systemName: "arrow.right.circle.fill")
                                Text("Connect Server & Sign In")
                            }
                            .font(.system(size: 15, weight: .bold))
                            .foregroundColor(.black)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 14)
                            .background(LiquidTheme.liquidGoldGradient)
                            .cornerRadius(14)
                            .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 12, x: 0, y: 4)
                        }
                        .padding(.top, 8)
                    }
                    .padding(.horizontal, 20)
                    .padding(.bottom, 40)
                }
            }
        }
        .onAppear {
            if !api.serverUrl.isEmpty {
                serverUrlInput = api.serverUrl
            }
            if !api.accessPin.isEmpty {
                pinInput = api.accessPin
            }
        }
    }

    private func startDiscovery() {
        isScanning = true
        discoveredServers.removeAll()
        Task {
            let found = await api.autoDiscoverLocalPc()
            for url in found {
                let pingResult = await api.pingServer(url: url)
                discoveredServers.append((url: url, ping: pingResult.ping))
            }
            if let first = discoveredServers.first {
                serverUrlInput = first.url
            }
            isScanning = false
        }
    }

    private func selectDiscoveredServer(_ url: String) {
        serverUrlInput = url
        testConnection()
    }

    private func testConnection() {
        isTesting = true
        testResult = nil
        Task {
            let (success, ping, error) = await api.pingServer(url: serverUrlInput)
            isTesting = false
            if success {
                let host = ping?.hostname ?? "Host"
                let ver = ping?.version ?? "3.2.6"
                testResult = (true, "Reachable! Connected to \(host) (v\(ver))")
            } else {
                testResult = (false, error ?? "Could not connect. Check IP and port.")
            }
        }
    }

    private func connectAndProceed() {
        api.saveSettings(url: serverUrlInput, pin: pinInput)
    }
}

private struct PresetChip: View {
    let title: String
    let value: String
    @Binding var selectedUrl: String

    var body: some View {
        Button(action: { selectedUrl = value }) {
            Text(title)
                .font(.system(size: 11, weight: .semibold, design: .rounded))
                .foregroundColor(selectedUrl == value ? .black : LiquidTheme.textPrimary)
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(selectedUrl == value ? LiquidTheme.gold : Color.white.opacity(0.08))
                .cornerRadius(8)
        }
    }
}
