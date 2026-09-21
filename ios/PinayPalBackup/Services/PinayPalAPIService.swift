import Foundation
import Combine

@MainActor
public class PinayPalAPIService: ObservableObject {
    @Published public var serverUrl: String = UserDefaults.standard.string(forKey: "pp_server_url") ?? ""
    @Published public var accessPin: String = UserDefaults.standard.string(forKey: "pp_access_pin") ?? ""
    @Published public var isConfigured: Bool = UserDefaults.standard.bool(forKey: "pp_is_configured")
    @Published public var isLoggedIn: Bool = UserDefaults.standard.bool(forKey: "pp_is_logged_in")
    @Published public var authToken: String? = UserDefaults.standard.string(forKey: "pp_auth_token")
    @Published public var currentUser: AppUserProfile? = nil

    @Published public var status: StatusResponse? = nil
    @Published public var remoteSettings: RemoteSettings? = nil
    @Published public var history: [BackupHistoryItem] = []
    @Published public var logs: [String] = []
    @Published public var isConnecting: Bool = false
    @Published public var isOnline: Bool = false
    @Published public var lastErrorMessage: String? = nil

    private var pollTimer: AnyCancellable?
    private var lastRecordedBusyService: String? = nil

    public init() {
        // Load saved user profile if exists
        if let userData = UserDefaults.standard.data(forKey: "pp_current_user"),
           let user = try? JSONDecoder().decode(AppUserProfile.self, from: userData) {
            self.currentUser = user
        }

        // If serverUrl is empty, default to localhost for simulation but leave isConfigured false
        if serverUrl.isEmpty {
            self.serverUrl = "http://localhost:8080"
        }

        if isConfigured {
            startPolling()
            Task {
                await fetchAll()
            }
        }
    }

    public func saveSettings(url: String, pin: String) {
        var cleanUrl = url.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") {
            cleanUrl.removeLast()
        }
        self.serverUrl = cleanUrl
        self.accessPin = pin.trimmingCharacters(in: .whitespacesAndNewlines)
        self.isConfigured = true

        UserDefaults.standard.set(self.serverUrl, forKey: "pp_server_url")
        UserDefaults.standard.set(self.accessPin, forKey: "pp_access_pin")
        UserDefaults.standard.set(true, forKey: "pp_is_configured")

        startPolling()
        Task {
            await fetchAll()
        }
    }

    public func pingServer(url: String) async -> (success: Bool, ping: PingResponse?, error: String?) {
        var cleanUrl = url.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") {
            cleanUrl.removeLast()
        }
        guard let pingUrl = URL(string: "\(cleanUrl)/api/ping") else {
            return (false, nil, "Invalid URL format")
        }

        var request = URLRequest(url: pingUrl)
        request.timeoutInterval = 3.5

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                return (false, nil, "Server returned HTTP \(code)")
            }
            let decoded = try JSONDecoder().decode(PingResponse.self, from: data)
            return (true, decoded, nil)
        } catch {
            return (false, nil, error.localizedDescription)
        }
    }

    public func login(username: String, password: String) async -> (success: Bool, message: String) {
        var cleanUrl = serverUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") {
            cleanUrl.removeLast()
        }
        guard let url = URL(string: "\(cleanUrl)/api/user-login") else {
            return (false, "Invalid server URL")
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 10

        let payload: [String: String] = ["username": username, "password": password]
        do {
            request.httpBody = try JSONSerialization.data(withJSONObject: payload)
            let (data, response) = try await URLSession.shared.data(for: request)
            
            if let http = response as? HTTPURLResponse {
                let decoder = JSONDecoder()
                if let loginResp = try? decoder.decode(UserLoginResponse.self, from: data) {
                    if http.statusCode == 200 && loginResp.success {
                        if let token = loginResp.token {
                            self.authToken = token
                            UserDefaults.standard.set(token, forKey: "pp_auth_token")
                        }
                        if let user = loginResp.user {
                            self.currentUser = user
                            if let userData = try? JSONEncoder().encode(user) {
                                UserDefaults.standard.set(userData, forKey: "pp_current_user")
                            }
                        }
                        self.isLoggedIn = true
                        UserDefaults.standard.set(true, forKey: "pp_is_logged_in")
                        await fetchAll()
                        return (true, loginResp.message ?? "Signed in successfully")
                    } else {
                        return (false, loginResp.message ?? "Authentication failed")
                    }
                }
            }
            return (false, "Unable to sign in. Check credentials or server connection.")
        } catch {
            return (false, error.localizedDescription)
        }
    }

    public func logout() async {
        var cleanUrl = serverUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") {
            cleanUrl.removeLast()
        }
        if let url = URL(string: "\(cleanUrl)/api/user-logout") {
            var request = URLRequest(url: url)
            request.httpMethod = "POST"
            let token = authToken ?? accessPin
            if !token.isEmpty {
                request.addValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            }
            _ = try? await URLSession.shared.data(for: request)
        }

        self.authToken = nil
        self.currentUser = nil
        self.isLoggedIn = false
        UserDefaults.standard.removeObject(forKey: "pp_auth_token")
        UserDefaults.standard.removeObject(forKey: "pp_current_user")
        UserDefaults.standard.set(false, forKey: "pp_is_logged_in")
    }

    public func disconnectServer() {
        Task {
            await logout()
        }
        pollTimer?.cancel()
        self.isConfigured = false
        UserDefaults.standard.set(false, forKey: "pp_is_configured")
    }

    public func autoDiscoverLocalPc() async -> [String] {
        var candidates: [String] = [
            "http://127.0.0.1:8080",
            "http://localhost:8080"
        ]

        let subnets = ["192.168.1", "192.168.0", "192.168.100", "10.0.0"]
        let commonHosts = [1, 2, 3, 5, 10, 20, 50, 100, 101, 102, 105, 150, 200]
        for subnet in subnets {
            for host in commonHosts {
                candidates.append("http://\(subnet).\(host):8080")
            }
        }

        var discovered: [String] = []

        await withTaskGroup(of: String?.self) { group in
            for candidate in candidates {
                group.addTask {
                    guard let url = URL(string: "\(candidate)/api/ping") else { return nil }
                    var req = URLRequest(url: url)
                    req.timeoutInterval = 1.2
                    do {
                        let (data, resp) = try await URLSession.shared.data(for: req)
                        if let http = resp as? HTTPURLResponse, http.statusCode == 200 {
                            if (try? JSONDecoder().decode(PingResponse.self, from: data)) != nil {
                                return candidate
                            }
                        }
                    } catch {}
                    return nil
                }
            }

            for await result in group {
                if let url = result {
                    if !discovered.contains(url) {
                        discovered.append(url)
                    }
                }
            }
        }

        return discovered
    }

    public func startPolling() {
        pollTimer?.cancel()
        pollTimer = Timer.publish(every: 4.0, on: .main, in: .common)
            .autoconnect()
            .sink { [weak self] _ in
                Task {
                    await self?.fetchStatus()
                }
            }
    }

    private func getAuthorizationHeader() -> String? {
        if let token = authToken, !token.isEmpty {
            return "Bearer \(token)"
        }
        if !accessPin.isEmpty {
            return "Bearer \(accessPin)"
        }
        return nil
    }

    public func fetchAll() async {
        isConnecting = true
        defer { isConnecting = false }
        await fetchStatus()
        await fetchRemoteSettings()
        await fetchHistory()
        await fetchLogs()
    }

    public func fetchStatus() async {
        guard let url = URL(string: "\(serverUrl)/api/status") else {
            isOnline = false
            return
        }

        var request = URLRequest(url: url)
        request.timeoutInterval = 6
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                isOnline = false
                return
            }

            let decoded = try JSONDecoder().decode(StatusResponse.self, from: data)

            let wasBusy = self.status?.activeBackup?.isBusy == true
            let prevService = self.status?.activeBackup?.service ?? lastRecordedBusyService

            self.status = decoded
            self.isOnline = true
            self.lastErrorMessage = nil

            let nowBusy = decoded.activeBackup?.isBusy == true
            let curService = decoded.activeBackup?.service ?? "Backup"

            if nowBusy {
                lastRecordedBusyService = curService
                if !BackupLiveActivityManager.shared.isActivityActive {
                    BackupLiveActivityManager.shared.startBackupActivity(service: curService)
                } else {
                    let prog = Double(decoded.activeBackup?.progress ?? 50) / 100.0
                    BackupLiveActivityManager.shared.updateBackupActivity(
                        progress: prog,
                        status: decoded.activeBackup?.statusText ?? "In Progress",
                        message: "Backing up \(curService.uppercased())..."
                    )
                }
            } else if wasBusy {
                let sName = (prevService ?? "Backup").uppercased()
                BackupLiveActivityManager.shared.endBackupActivity(success: true, message: "\(sName) completed successfully")
                NotificationService.shared.sendBackupNotification(
                    service: sName,
                    success: true,
                    details: "Backup routine for \(sName) completed successfully."
                )
                lastRecordedBusyService = nil
            }

            // Disk space alert check
            if let disk = decoded.health?.disk, let pct = disk.percent, pct > 88 {
                NotificationService.shared.sendLowDiskAlert(
                    diskLetter: disk.primaryDriveLetter ?? "C:",
                    freeGb: Double(disk.availableGB ?? 0),
                    percentUsed: pct
                )
            }
        } catch {
            self.isOnline = false
            self.lastErrorMessage = error.localizedDescription
        }
    }

    public func fetchHistory() async {
        guard let url = URL(string: "\(serverUrl)/api/history") else { return }
        var request = URLRequest(url: url)
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, _) = try await URLSession.shared.data(for: request)
            let items = try JSONDecoder().decode([BackupHistoryItem].self, from: data)
            self.history = items
        } catch { }
    }

    public func fetchLogs() async {
        guard let url = URL(string: "\(serverUrl)/api/logs") else { return }
        var request = URLRequest(url: url)
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, _) = try await URLSession.shared.data(for: request)
            let lines = try JSONDecoder().decode([String].self, from: data)
            self.logs = lines
        } catch { }
    }

    public func triggerBackup(service: String) async -> Bool {
        BackupLiveActivityManager.shared.startBackupActivity(service: service)

        guard let url = URL(string: "\(serverUrl)/api/backup/\(service)") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                await fetchAll()
                return true
            }
        } catch { }
        return false
    }

    public func runDiagnostics() async -> Bool {
        guard let url = URL(string: "\(serverUrl)/api/health/run") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                await fetchStatus()
                return true
            }
        } catch { }
        return false
    }

    public func fetchRemoteSettings() async {
        guard let url = URL(string: "\(serverUrl)/api/settings") else { return }
        var request = URLRequest(url: url)
        request.timeoutInterval = 6
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                let decoded = try JSONDecoder().decode(RemoteSettings.self, from: data)
                self.remoteSettings = decoded
            }
        } catch { }
    }

    public func saveRemoteSettings(_ settings: RemoteSettings) async -> Bool {
        guard let url = URL(string: "\(serverUrl)/api/settings") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 8
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let bodyData = try JSONEncoder().encode(settings)
            request.httpBody = bodyData
            let (_, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                self.remoteSettings = settings
                await fetchStatus()
                return true
            }
        } catch { }
        return false
    }

    public func triggerEmergencyStop() async -> Bool {
        guard let url = URL(string: "\(serverUrl)/api/emergency-stop") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 6
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                await fetchStatus()
                return true
            }
        } catch { }
        return false
    }
}
