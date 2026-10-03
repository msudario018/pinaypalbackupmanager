import Foundation
import Combine
import UIKit

@MainActor
public class PinayPalAPIService: ObservableObject {
    @Published public var serverUrl: String = UserDefaults.standard.string(forKey: "pp_server_url") ?? ""
    @Published public var fallbackUrl: String = UserDefaults.standard.string(forKey: "pp_fallback_url") ?? ""
    @Published public var tailscaleUrl: String = UserDefaults.standard.string(forKey: "pp_tailscale_url") ?? ""
    @Published public var isUsingFallback: Bool = false
    @Published public var isUsingTailscale: Bool = false
    @Published public var accessPin: String = UserDefaults.standard.string(forKey: "pp_access_pin") ?? ""
    @Published public var isConfigured: Bool = UserDefaults.standard.bool(forKey: "pp_is_configured")
    @Published public var isLoggedIn: Bool = UserDefaults.standard.bool(forKey: "pp_is_logged_in")
    @Published public var authToken: String? = UserDefaults.standard.string(forKey: "pp_auth_token")
    @Published public var currentUser: AppUserProfile? = nil

    public var activeBaseUrl: String {
        if isUsingTailscale && !tailscaleUrl.isEmpty {
            return tailscaleUrl
        }
        if isUsingFallback && !fallbackUrl.isEmpty {
            return fallbackUrl
        }
        return serverUrl.isEmpty ? "http://localhost:8080" : serverUrl
    }

    public var connectionModeName: String {
        if !isOnline { return "Offline" }
        if isUsingTailscale { return "Tailscale VPN" }
        if isUsingFallback { return "Cloudflare Tunnel" }
        return "LAN Direct"
    }

    public var activeHostName: String {
        status?.system?.hostname ?? status?.hardware?.hostname ?? "PinayPal"
    }

    @Published public var status: StatusResponse? = nil

    /// <summary>
    /// Live computer fleet. Published here rather than held inside the view so the PCs tab
    /// keeps updating while it is open, and so an add/edit/delete made on the desktop shows up
    /// within one poll interval without the user having to pull-to-refresh.
    /// </summary>
    @Published public var computers: [ComputerSpec] = []
    @Published public var computersError: String? = nil

    /// Recent CPU/RAM history keyed by computer id, used to draw per-PC sparklines.
    @Published public var computerHistory: [String: [TelemetryPoint]] = [:]

    /// <summary>One telemetry sample for the sparklines.</summary>
    public struct TelemetryPoint: Identifiable {
        public let id = UUID()
        public let cpu: Double
        public let ram: Double
    }

    /// <summary>
    /// Fetches recent history for each online PC. Best-effort: a failure must never blank the
    /// fleet list, so errors are swallowed and the charts simply stay empty.
    /// </summary>
    public func refreshComputerHistory() async {
        let targets = computers.filter { $0.telemetry.isOnline }
        guard !targets.isEmpty else {
            computerHistory = [:]
            return
        }

        var collected: [String: [TelemetryPoint]] = [:]
        await withTaskGroup(of: (String, [TelemetryPoint]).self) { group in
            for pc in targets {
                group.addTask { [weak self] in
                    guard let self, let points = await self.fetchHistory(computerId: pc.id) else { return ("", []) }
                    return (pc.id, points)
                }
            }
            for await (id, points) in group where !id.isEmpty && !points.isEmpty {
                collected[id] = points
            }
        }

        if !collected.isEmpty { computerHistory = collected }
    }

    private func fetchHistory(computerId: String) async -> [TelemetryPoint]? {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") { cleanUrl.removeLast() }

        guard let encoded = computerId.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed),
              let url = URL(string: "\(cleanUrl)/api/computers/history?id=\(encoded)") else { return nil }

        var request = URLRequest(url: url)
        request.timeoutInterval = 10
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, _) = try await URLSession.shared.data(for: request)
            guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let raw = json["samples"] as? [[String: Any]] else { return nil }

            // The first sample is taken at app start, when there is nothing to compare it
            // against, so it would render as a misleading flat spike. Drop it.
            return raw.dropFirst().compactMap { sample in
                let cpu = (sample["CpuUsagePercent"] ?? sample["cpuUsagePercent"]) as? Double
                let ram = (sample["RamUsagePercent"] ?? sample["ramUsagePercent"]) as? Double
                guard let cpu, let ram else { return nil }
                return TelemetryPoint(cpu: cpu, ram: ram)
            }
        } catch {
            return nil
        }
    }
    @Published public var remoteSettings: RemoteSettings? = nil
    @Published public var history: [BackupHistoryItem] = []
    @Published public var logs: [String] = []
    @Published public var isConnecting: Bool = false
    @Published public var isOnline: Bool = false
    @Published public var latencyMs: Int? = nil
    @Published public var lastErrorMessage: String? = nil
    @Published public var outdatedCount: Int = 0
    @Published public var outdatedServices: [String] = []
    @Published public var isBatterySaverEnabled: Bool = UserDefaults.standard.bool(forKey: "pp_battery_saver") {
        didSet {
            UserDefaults.standard.set(isBatterySaverEnabled, forKey: "pp_battery_saver")
            startPolling()
        }
    }
    @Published public var routeLatencies: [RouteLatencyInfo] = []

    private var pollTimer: AnyCancellable?
    private var lastRecordedBusyService: String? = nil

    /// <summary>Tracks foreground/background so polling can back off aggressively when hidden.</summary>
    private var isAppActive: Bool = true

    /// <summary>
    /// Guards against overlapping status requests. Without this, a slow response on a 2s
    /// timer can stack up concurrent fetches and make the app look slower over time.
    /// </summary>
    private var isStatusFetchInFlight: Bool = false
    private var backgroundTaskID: UIBackgroundTaskIdentifier = .invalid
    private var backgroundSyncTask: Task<Void, Never>? = nil
    private var lifecycleObservers: [NSObjectProtocol] = []

    public init() {
        // Load saved user profile if exists
        if let userData = UserDefaults.standard.data(forKey: "pp_current_user"),
           let user = try? JSONDecoder().decode(AppUserProfile.self, from: userData) {
            self.currentUser = user
        }

        // Migrate or load fallback URL
        if fallbackUrl.isEmpty, let legacy = UserDefaults.standard.string(forKey: "pp_failover_url"), !legacy.isEmpty {
            self.fallbackUrl = legacy
        }

        // If serverUrl is empty, default to localhost for simulation but leave isConfigured false
        if serverUrl.isEmpty {
            self.serverUrl = "http://localhost:8080"
        }

        setupLifecycleObservers()

        if isConfigured {
            startPolling()
            Task {
                await fetchAll()
            }
        }
    }

    deinit {
        for observer in lifecycleObservers {
            NotificationCenter.default.removeObserver(observer)
        }
    }

    public func saveSettings(url: String, pin: String, fallbackUrl: String? = nil, tailscaleUrl: String? = nil) {
        var cleanUrl = url.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") {
            cleanUrl.removeLast()
        }
        self.serverUrl = cleanUrl
        self.accessPin = pin.trimmingCharacters(in: .whitespacesAndNewlines)

        if let fb = fallbackUrl {
            var cleanFb = fb.trimmingCharacters(in: .whitespacesAndNewlines)
            if cleanFb.hasSuffix("/") {
                cleanFb.removeLast()
            }
            self.fallbackUrl = cleanFb
            UserDefaults.standard.set(cleanFb, forKey: "pp_fallback_url")
            UserDefaults.standard.set(cleanFb, forKey: "pp_failover_url")
        }

        if let ts = tailscaleUrl {
            var cleanTs = ts.trimmingCharacters(in: .whitespacesAndNewlines)
            if cleanTs.hasSuffix("/") {
                cleanTs.removeLast()
            }
            self.tailscaleUrl = cleanTs
            UserDefaults.standard.set(cleanTs, forKey: "pp_tailscale_url")
        }

        self.isUsingFallback = false
        self.isUsingTailscale = false
        self.isConfigured = true

        UserDefaults.standard.set(self.serverUrl, forKey: "pp_server_url")
        UserDefaults.standard.set(self.accessPin, forKey: "pp_access_pin")
        UserDefaults.standard.set(true, forKey: "pp_is_configured")

        startPolling()
        Task {
            await fetchAll()
        }
    }

    public func toggleConnectionMode() async {
        // Cycle through the configured routes: LAN → Cloudflare → Tailscale → LAN
        if isUsingFallback {
            isUsingFallback = false
            isUsingTailscale = !tailscaleUrl.isEmpty
        } else if isUsingTailscale {
            isUsingFallback = false
            isUsingTailscale = false
        } else if !fallbackUrl.isEmpty {
            isUsingFallback = true
            isUsingTailscale = false
        } else if !tailscaleUrl.isEmpty {
            isUsingTailscale = true
        }
        await fetchAll()
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
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
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
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
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

    private func setupLifecycleObservers() {
        let enterBg = NotificationCenter.default.addObserver(
            forName: UIApplication.didEnterBackgroundNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor [weak self] in
                self?.handleDidEnterBackground()
            }
        }
        let enterFg = NotificationCenter.default.addObserver(
            forName: UIApplication.willEnterForegroundNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor [weak self] in
                self?.handleWillEnterForeground()
            }
        }
        lifecycleObservers.append(contentsOf: [enterBg, enterFg])
    }

    private func handleDidEnterBackground() {
        isAppActive = false
        let isBusy = self.status?.activeBackup?.isBusy == true || BackupLiveActivityManager.shared.isActivityActive
        if isBusy {
            startBackgroundLiveActivitySync()
        }
        // Re-arm the timer so the background cadence (20s/45s) takes effect immediately.
        startPolling()
    }

    private func handleWillEnterForeground() {
        isAppActive = true
        endBackgroundLiveActivitySync()
        startPolling()
        Task {
            await fetchStatus()
        }
    }

    public func startBackgroundLiveActivitySync() {
        guard backgroundTaskID == .invalid else { return }

        backgroundTaskID = UIApplication.shared.beginBackgroundTask(withName: "PinayPalLiveActivitySync") { [weak self] in
            Task { @MainActor [weak self] in
                self?.endBackgroundLiveActivitySync()
            }
        }

        backgroundSyncTask?.cancel()
        backgroundSyncTask = Task { [weak self] in
            while !Task.isCancelled {
                guard let self = self else { break }
                await self.fetchStatus()

                let stillBusy = await MainActor.run {
                    self.status?.activeBackup?.isBusy == true || BackupLiveActivityManager.shared.isActivityActive
                }

                if !stillBusy {
                    await MainActor.run {
                        self.endBackgroundLiveActivitySync()
                    }
                    break
                }

                try? await Task.sleep(nanoseconds: 2_500_000_000)
            }
        }
    }

    public func endBackgroundLiveActivitySync() {
        backgroundSyncTask?.cancel()
        backgroundSyncTask = nil
        if backgroundTaskID != .invalid {
            UIApplication.shared.endBackgroundTask(backgroundTaskID)
            backgroundTaskID = .invalid
        }
    }

    /// <summary>
    /// Whether the PCs tab is currently on screen. Polling only runs while it is, so the fleet
    /// does not keep hitting the dashboard when the user is looking at something else.
    /// </summary>
    private var computerPollingActive: Bool = false
    private var computerPollTask: Task<Void, Never>?

    /// <summary>Starts or stops fleet polling. Safe to call repeatedly.</summary>
    public func setComputerPolling(_ active: Bool) {
        guard active != computerPollingActive else { return }
        computerPollingActive = active

        computerPollTask?.cancel()
        computerPollTask = nil

        guard active else { return }

        computerPollTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refreshComputers()
                try? await Task.sleep(nanoseconds: 10_000_000_000)
            }
        }
    }

    /// <summary>
    /// Refreshes the fleet from /api/computers. Used both by the poller and pull-to-refresh,
    /// so registry edits made on the desktop appear automatically.
    /// </summary>
    public func refreshComputers(force: Bool = false) async {
        let (ok, list, err) = await fetchComputers(refresh: force)
        guard ok else {
            // Keep the last good list rather than blanking the tab on a transient failure.
            computersError = err
            return
        }
        computers = list
        computersError = nil

        // Chart history trails the fleet list slightly, so it is fetched after rather than
        // blocking the list the user is waiting on.
        Task { await refreshComputerHistory() }
    }

    /// <summary>Removes a computer from the fleet registry on the PC.</summary>
    public func deleteComputer(id: String) async -> (success: Bool, message: String) {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") { cleanUrl.removeLast() }

        guard let encoded = id.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed),
              let url = URL(string: "\(cleanUrl)/api/computers?id=\(encoded)") else {
            return (false, "Invalid host URL")
        }

        var request = URLRequest(url: url)
        request.httpMethod = "DELETE"
        request.timeoutInterval = 10
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            let json = (try? JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
            let message = json["message"] as? String ?? "Done"
            let ok = (response as? HTTPURLResponse)?.statusCode == 200
            if ok { await refreshComputers() }
            return (ok, message)
        } catch {
            return (false, error.localizedDescription)
        }
    }

    public func startPolling() {
        pollTimer?.cancel()

        // Adaptive polling. The dashboard is a "live ops" surface, so it should be
        // responsive while you are actually looking at it and almost silent when you are not:
        //  - a backup is running            -> 2s (progress ring must feel live)
        //  - normal viewing                 -> 5s
        //  - app is backgrounded            -> 20s (keeps Dynamic Island / widgets warm)
        //  - battery saver or Low Power Mode -> 15s foreground, 45s background
        let lowPower = isBatterySaverEnabled || ProcessInfo.processInfo.isLowPowerModeEnabled
        let busy = status?.activeBackup?.isBusy == true

        let interval: Double
        if lowPower {
            interval = isAppActive ? 15.0 : 45.0
        } else if busy {
            interval = 2.0
        } else {
            interval = isAppActive ? 5.0 : 20.0
        }

        pollTimer = Timer.publish(every: interval, on: .main, in: .common)
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
        // Skip if a previous poll is still in flight; otherwise a slow response on a short
        // timer piles up concurrent requests and the UI ends up rendering stale results.
        guard !isStatusFetchInFlight else { return }
        isStatusFetchInFlight = true
        defer { isStatusFetchInFlight = false }

        // Fail-back probes: hop to a lower-latency route whenever it becomes reachable again.
        await failBackIfReachable()

        // Walk the failover chain starting from the active route: LAN → Cloudflare → Tailscale.
        for route in failoverChain() {
            if let decoded = await requestStatus(base: route.url) {
                applyRoute(route.mode)
                applyStatus(decoded)
                await handleConnectedSideEffects(mode: route.mode)

                // Re-tune the timer: crossing into (or out of) a busy state changes the cadence.
                if pollTimer != nil { startPolling() }
                return
            }
        }

        self.isOnline = false
        self.lastErrorMessage = "No route to server: LAN, Cloudflare Tunnel and Tailscale are all unreachable."
        notifyTailscaleNeeded()
    }

    private enum RouteMode { case lan, cloudflare, tailscale }

    /// Ordered failover chain that always starts with the currently active route.
    private func failoverChain() -> [(mode: RouteMode, url: String)] {
        var routes: [(mode: RouteMode, url: String)] = []
        if !serverUrl.isEmpty { routes.append((.lan, serverUrl)) }
        if !fallbackUrl.isEmpty { routes.append((.cloudflare, fallbackUrl)) }
        if !tailscaleUrl.isEmpty { routes.append((.tailscale, tailscaleUrl)) }

        let currentMode: RouteMode = isUsingTailscale ? .tailscale : (isUsingFallback ? .cloudflare : .lan)
        if let idx = routes.firstIndex(where: { $0.mode == currentMode }) {
            let current = routes.remove(at: idx)
            routes.insert(current, at: 0)
        }
        return routes
    }

    private func applyRoute(_ mode: RouteMode) {
        self.isUsingFallback = mode == .cloudflare
        self.isUsingTailscale = mode == .tailscale
    }

    /// Probes lower-latency routes while riding a remote one and hops back when they answer.
    private func failBackIfReachable() async {
        if isUsingFallback || isUsingTailscale {
            if !serverUrl.isEmpty, await pingOk(serverUrl) {
                applyRoute(.lan)
                return
            }
        }
        if isUsingTailscale, !fallbackUrl.isEmpty, await pingOk(fallbackUrl) {
            applyRoute(.cloudflare)
        }
    }

    private func pingOk(_ base: String) async -> Bool {
        let clean = sanitizeUrl(base)
        guard let url = URL(string: "\(clean)/api/ping") else { return false }
        var request = URLRequest(url: url)
        request.timeoutInterval = 1.5
        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            return (response as? HTTPURLResponse)?.statusCode == 200
        } catch {
            return false
        }
    }

    private func requestStatus(base: String) async -> StatusResponse? {
        let clean = sanitizeUrl(base)
        guard let url = URL(string: "\(clean)/api/status") else { return nil }

        var request = URLRequest(url: url)
        request.timeoutInterval = 6
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let start = CFAbsoluteTimeGetCurrent()
            let (data, response) = try await URLSession.shared.data(for: request)
            let elapsed = CFAbsoluteTimeGetCurrent() - start
            guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                return nil
            }
            let decoded = try JSONDecoder().decode(StatusResponse.self, from: data)
            self.latencyMs = Int(elapsed * 1000)
            return decoded
        } catch {
            return nil
        }
    }

    /// Post-connect housekeeping: adopt routes advertised by the PC and recreate the
    /// Cloudflare Quick Tunnel when the PC reports it should be up but is down.
    private func handleConnectedSideEffects(mode: RouteMode) async {
        adoptServerAdvertisedRoutes()

        if !fallbackUrl.isEmpty,
           status?.system?.cloudflareActive == false,
           status?.system?.cloudflareManaged == true {
            // "Connection established → rerun cloudflared": over LAN this recovers a hung
            // tunnel; over Tailscale it recovers a tunnel while the public route is dead.
            await requestCloudflareRecreate()
        }
    }

    /// The PC advertises its current Tailscale IP and (ephemeral) quick tunnel URL via /api/status.
    private func adoptServerAdvertisedRoutes() {
        guard let system = status?.system else { return }

        if tailscaleUrl.isEmpty, let advertised = system.tailscaleUrl, !advertised.isEmpty {
            let clean = sanitizeUrl(advertised)
            tailscaleUrl = clean
            UserDefaults.standard.set(clean, forKey: "pp_tailscale_url")
        }

        // Quick tunnel URLs change on every recreation: refresh ours, but never override a custom domain.
        if let advertised = system.cloudflareUrl, !advertised.isEmpty, advertised.contains("trycloudflare.com"),
           fallbackUrl.isEmpty || (fallbackUrl.contains("trycloudflare.com") && fallbackUrl != advertised) {
            let clean = sanitizeUrl(advertised)
            fallbackUrl = clean
            UserDefaults.standard.set(clean, forKey: "pp_fallback_url")
            UserDefaults.standard.set(clean, forKey: "pp_failover_url")
        }
    }

    /// Requests the PC to restart/recreate the Cloudflare Quick Tunnel (e.g. over Tailscale or LAN).
    @discardableResult
    public func forceRestartCloudflareTunnel() async -> (success: Bool, message: String) {
        let cleanBase = sanitizeUrl(activeBaseUrl)
        guard let url = URL(string: "\(cleanBase)/api/tunnel/quick/restart") else {
            return (false, "Invalid endpoint URL")
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 45 // cloudflared bootstrap can take up to ~25s
        if let token = authToken, !token.isEmpty {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                UserDefaults.standard.set(Date().timeIntervalSince1970, forKey: "pp_tunnel_recreate_last")
                try? await Task.sleep(nanoseconds: 2_500_000_000)
                await fetchAll()
                return (true, "Cloudflare Tunnel recreated successfully.")
            } else {
                let msg = String(data: data, encoding: .utf8) ?? "Failed to restart tunnel."
                return (false, msg)
            }
        } catch {
            return (false, error.localizedDescription)
        }
    }

    /// Asks the PC to recreate the Cloudflare Quick Tunnel (max once every 5 minutes in background).
    private func requestCloudflareRecreate() async {
        let now = Date().timeIntervalSince1970
        let last = UserDefaults.standard.double(forKey: "pp_tunnel_recreate_last")
        guard now - last > 300 else { return }
        _ = await forceRestartCloudflareTunnel()
    }

    /// Measures live ping latencies across all configured routes simultaneously.
    @discardableResult
    public func measureAllRouteLatencies() async -> [RouteLatencyInfo] {
        var targets: [(name: String, type: String, url: String, isCurrent: Bool)] = []

        if !serverUrl.isEmpty {
            targets.append(("Local Network (LAN)", "LAN", serverUrl, !isUsingFallback && !isUsingTailscale))
        }
        if !fallbackUrl.isEmpty {
            targets.append(("Cloudflare Tunnel", "Cloudflare", fallbackUrl, isUsingFallback))
        }
        if !tailscaleUrl.isEmpty {
            targets.append(("Tailscale VPN Mesh", "Tailscale", tailscaleUrl, isUsingTailscale))
        }

        var results: [RouteLatencyInfo] = []
        for t in targets {
            let clean = sanitizeUrl(t.url)
            guard let url = URL(string: "\(clean)/api/ping") else {
                results.append(RouteLatencyInfo(name: t.name, routeType: t.type, url: t.url, latencyMs: nil as Int?, isReachable: false, isCurrent: t.isCurrent))
                continue
            }

            var req = URLRequest(url: url)
            req.timeoutInterval = 3.5
            let start = CFAbsoluteTimeGetCurrent()
            do {
                let (_, resp) = try await URLSession.shared.data(for: req)
                if let http = resp as? HTTPURLResponse, http.statusCode == 200 {
                    let ms = max(1, Int((CFAbsoluteTimeGetCurrent() - start) * 1000))
                    results.append(RouteLatencyInfo(name: t.name, routeType: t.type, url: t.url, latencyMs: ms, isReachable: true, isCurrent: t.isCurrent))
                } else {
                    results.append(RouteLatencyInfo(name: t.name, routeType: t.type, url: t.url, latencyMs: nil as Int?, isReachable: false, isCurrent: t.isCurrent))
                }
            } catch {
                results.append(RouteLatencyInfo(name: t.name, routeType: t.type, url: t.url, latencyMs: nil as Int?, isReachable: false, isCurrent: t.isCurrent))
            }
        }

        self.routeLatencies = results
        return results
    }

    /// Clears route fallback memories and re-probes LAN first to latch onto the fastest path.
    public func resetRouteCache() async {
        isUsingFallback = false
        isUsingTailscale = false
        UserDefaults.standard.set(false, forKey: "pp_using_fallback")
        UserDefaults.standard.set(false, forKey: "pp_using_tailscale")
        await fetchAll()
        await measureAllRouteLatencies()
    }

    /// Fires the local "Enable Tailscale" reminder when every route is unreachable (e.g. off-site).
    private func notifyTailscaleNeeded() {
        guard isConfigured else { return }
        NotificationService.shared.sendTailscaleEnableReminder(
            tailscaleConfigured: !tailscaleUrl.isEmpty,
            tunnelConfigured: !fallbackUrl.isEmpty
        )
    }

    private func sanitizeUrl(_ raw: String) -> String {
        var clean = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if clean.hasSuffix("/") { clean.removeLast() }
        return clean
    }

    private func applyStatus(_ decoded: StatusResponse) {
        let wasBusy = self.status?.activeBackup?.isBusy == true
        let prevService = self.status?.activeBackup?.service ?? lastRecordedBusyService
        let previousWebsiteOnline = self.status?.website?.isOnline

        self.status = decoded
        self.isOnline = true
        self.lastErrorMessage = nil

        // Evaluate outdated status across services
        var outdatedList: [String] = []
        let checkService: (String, ServiceItem?) -> Void = { name, item in
            if item?.freshness?.isOutdated == true || item?.freshness?.status == "outdated" || item?.freshness?.status == "never" {
                outdatedList.append(name)
                let detail = item?.freshness?.badgeText ?? "\(name) backup is outdated"
                NotificationService.shared.sendOutdatedBackupAlert(service: name, details: detail)
            }
        }

        checkService("FTP", decoded.services?.ftp)
        checkService("SQL", decoded.services?.sql)
        checkService("Mailchimp", decoded.services?.mailchimp)

        self.outdatedCount = outdatedList.count
        self.outdatedServices = outdatedList
        NotificationService.shared.setBadgeCount(outdatedList.count)

        if let previousWebsiteOnline, let websiteOnline = decoded.website?.isOnline,
           previousWebsiteOnline != websiteOnline {
            NotificationService.shared.sendWebsiteStatusNotification(
                isOnline: websiteOnline,
                details: websiteOnline
                    ? "The public HTTPS check is responding again."
                    : (decoded.website?.error ?? "The public HTTPS check failed.")
            )
        }

        let nowBusy = decoded.activeBackup?.isBusy == true
        let curService = decoded.activeBackup?.service ?? "Backup"

        if nowBusy {
            let serviceChanged = lastRecordedBusyService.map {
                $0.caseInsensitiveCompare(curService) != .orderedSame
            } ?? false
            if serviceChanged && BackupLiveActivityManager.shared.isActivityActive {
                BackupLiveActivityManager.shared.endBackupActivity(
                    success: true,
                    message: "Starting \(curService.uppercased()) backup"
                )
            }
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

            if UIApplication.shared.applicationState != .active && backgroundTaskID == .invalid {
                startBackgroundLiveActivitySync()
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
            endBackgroundLiveActivitySync()
        }

        // Disk space alert check
        if let disk = decoded.health?.disk, let pct = disk.percent, pct > 88 {
            NotificationService.shared.sendLowDiskAlert(
                diskLetter: disk.primaryDriveLetter ?? "C:",
                freeGb: Double(disk.availableGB ?? 0),
                percentUsed: pct
            )
        }
    }

    public func fetchHistory() async {
        guard let url = URL(string: "\(activeBaseUrl)/api/history") else { return }
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

    public func downloadBackupFile(filename: String) async -> URL? {
        guard let safeName = filename.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed),
              let url = URL(string: "\(activeBaseUrl)/download/\(safeName)") else { return nil }
        var request = URLRequest(url: url)
        request.timeoutInterval = 60
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }
        do {
            let (temporaryURL, response) = try await URLSession.shared.download(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return nil }
            let destination = FileManager.default.temporaryDirectory.appendingPathComponent(filename)
            try? FileManager.default.removeItem(at: destination)
            try FileManager.default.moveItem(at: temporaryURL, to: destination)
            return destination
        } catch {
            lastErrorMessage = error.localizedDescription
            return nil
        }
    }

    public func clearHistory() async -> Bool {
        guard let url = URL(string: "\(activeBaseUrl)/api/history") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "DELETE"
        if let auth = getAuthorizationHeader() { request.addValue(auth, forHTTPHeaderField: "Authorization") }
        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else { return false }
            history = []
            return true
        } catch { return false }
    }

    public func makeDiagnosticsBundle() -> URL? {
        let payload: [String: Any] = [
            "appVersion": Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "unknown",
            "serverUrl": activeBaseUrl,
            "routingMode": connectionModeName,
            "isUsingFallback": isUsingFallback,
            "isUsingTailscale": isUsingTailscale,
            "connected": isOnline,
            "latencyMs": latencyMs ?? NSNull(),
            "lastError": lastErrorMessage ?? NSNull(),
            "notificationStatus": NotificationService.shared.authorizationDescription,
            "recentLogs": Array(logs.suffix(50)),
            "generatedAt": ISO8601DateFormatter().string(from: Date())
        ]
        guard JSONSerialization.isValidJSONObject(payload),
              let data = try? JSONSerialization.data(withJSONObject: payload, options: [.prettyPrinted]) else { return nil }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("PinayPal-Diagnostics.json")
        try? data.write(to: url, options: .atomic)
        return url
    }

    public func fetchLogs() async {
        guard let url = URL(string: "\(activeBaseUrl)/api/logs") else { return }
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
        guard let url = URL(string: "\(activeBaseUrl)/api/backup/\(service)") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 202 {
                BackupLiveActivityManager.shared.startBackupActivity(service: service)
                if UIApplication.shared.applicationState != .active {
                    startBackgroundLiveActivitySync()
                }
                await fetchAll()
                return true
            }
            if let http = response as? HTTPURLResponse {
                lastErrorMessage = "Could not start backup (HTTP \(http.statusCode))."
                if http.statusCode == 409,
                   let payload = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   let message = payload["message"] as? String {
                    lastErrorMessage = message
                }
            }
        } catch {
            lastErrorMessage = error.localizedDescription
        }
        return false
    }

    public func triggerMailchimpExport(task: String) async -> Bool {
        guard let encodedTask = task.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed),
              let url = URL(string: "\(activeBaseUrl)/api/backup/mailchimp-task?task=\(encodedTask)") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 202 {
                await fetchAll()
                return true
            }
            if let payload = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
               let message = payload["message"] as? String {
                lastErrorMessage = message
            }
        } catch {
            lastErrorMessage = error.localizedDescription
        }
        return false
    }

    public func runDiagnostics() async -> Bool {
        guard let url = URL(string: "\(activeBaseUrl)/api/health/run") else { return false }
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
        guard let url = URL(string: "\(activeBaseUrl)/api/settings") else { return }
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
        guard let url = URL(string: "\(activeBaseUrl)/api/settings") else { return false }
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
        guard let url = URL(string: "\(activeBaseUrl)/api/emergency-stop") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 6
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                BackupLiveActivityManager.shared.endBackupActivity(success: false, message: "Emergency Stop Triggered")
                endBackgroundLiveActivitySync()
                await fetchStatus()
                return true
            }
        } catch { }
        return false
    }

    public func changeUsername(newUsername: String) async -> (Bool, String) {
        guard let url = URL(string: "\(activeBaseUrl)/api/user/change-username") else {
            return (false, "Invalid server URL")
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 8
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }
        let body: [String: Any] = ["newUsername": newUsername]
        guard let bodyData = try? JSONSerialization.data(withJSONObject: body) else {
            return (false, "Failed to encode payload")
        }
        request.httpBody = bodyData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse {
                if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                    let msg = json["message"] as? String ?? (http.statusCode == 200 ? "Username updated successfully." : "Failed to update username.")
                    if http.statusCode == 200 {
                        if let user = self.currentUser {
                            let updated = AppUserProfile(
                                id: user.id,
                                username: newUsername,
                                email: user.email,
                                role: user.role,
                                fullName: newUsername,
                                avatarUrl: user.avatarUrl
                            )
                            self.currentUser = updated
                            if let enc = try? JSONEncoder().encode(updated) {
                                UserDefaults.standard.set(enc, forKey: "pp_current_user")
                            }
                        }
                        return (true, msg)
                    }
                    return (false, msg)
                }
            }
        } catch {
            return (false, error.localizedDescription)
        }
        return (false, "Failed to update username.")
    }

    public func changePassword(currentPassword: String, newPassword: String) async -> (Bool, String) {
        guard let url = URL(string: "\(activeBaseUrl)/api/user/change-password") else {
            return (false, "Invalid server URL")
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 8
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }
        let body: [String: Any] = [
            "currentPassword": currentPassword,
            "newPassword": newPassword
        ]
        guard let bodyData = try? JSONSerialization.data(withJSONObject: body) else {
            return (false, "Failed to encode payload")
        }
        request.httpBody = bodyData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse {
                if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                    let msg = json["message"] as? String ?? (http.statusCode == 200 ? "Password updated successfully." : "Failed to update password.")
                    return (http.statusCode == 200, msg)
                }
            }
        } catch {
            return (false, error.localizedDescription)
        }
        return (false, "Failed to update password.")
    }

    public func triggerSyncCheck() async -> (success: Bool, message: String) {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") {
            cleanUrl.removeLast()
        }
        guard let url = URL(string: "\(cleanUrl)/api/sync/check") else {
            return (false, "Invalid server URL")
        }
        var request = URLRequest(url: url)
        request.timeoutInterval = 12
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }
        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse, http.statusCode == 200 {
                await fetchStatus()
                return (true, "Remote sync verification completed.")
            }
            return (false, "Sync check failed with HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")
        } catch {
            return (false, error.localizedDescription)
        }
    }
    public func uploadAvatar(imageData: Data) async -> (Bool, String) {
        guard let url = URL(string: "\(activeBaseUrl)/api/user/avatar") else {
            return (false, "Invalid server URL")
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("image/png", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 15
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }
        request.httpBody = imageData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse {
                if http.statusCode == 200 {
                    if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                        let msg = json["message"] as? String ?? "Avatar uploaded successfully"
                        if let avatarUrl = json["avatarUrl"] as? String, let user = self.currentUser {
                            let updated = AppUserProfile(
                                id: user.id,
                                username: user.username,
                                email: user.email,
                                role: user.role,
                                fullName: user.fullName,
                                avatarUrl: avatarUrl
                            )
                            self.currentUser = updated
                            if let enc = try? JSONEncoder().encode(updated) {
                                UserDefaults.standard.set(enc, forKey: "pp_current_user")
                            }
                        }
                        return (true, msg)
                    }
                    return (true, "Avatar uploaded successfully")
                } else {
                    if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                       let msg = json["error"] as? String ?? json["message"] as? String {
                        return (false, msg)
                    }
                    return (false, "Upload failed with HTTP \(http.statusCode)")
                }
            }
        } catch {
            return (false, error.localizedDescription)
        }
        return (false, "Failed to upload avatar")
    }

    // MARK: - Managed computers (fleet)

    /// Fetches the fleet of managed computers with live hardware telemetry.
    public func fetchComputers(refresh: Bool = true) async -> (success: Bool, computers: [ComputerSpec], error: String?) {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") { cleanUrl.removeLast() }
        let suffix = refresh ? "?refresh=1" : ""
        guard let url = URL(string: "\(cleanUrl)/api/computers\(suffix)") else {
            return (false, [], "Invalid host URL")
        }

        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.timeoutInterval = 12
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                return (false, [], "Computers endpoint returned HTTP \(code)")
            }

            let decoded = try JSONDecoder().decode(ComputerFleetResponse.self, from: data)
            return (true, decoded.computers ?? [], nil)
        } catch {
            return (false, [], error.localizedDescription)
        }
    }

    /// Sends a wake / restart / shutdown / lock / sleep request for one computer.
    /// Destructive actions use a grace delay so a mis-tap cannot kill a working machine instantly.
    public func performComputerAction(computerId: String, action: String, delaySeconds: Int = 10) async -> (success: Bool, message: String) {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") { cleanUrl.removeLast() }

        // "wake" has a dedicated endpoint; everything else goes through /api/computers/action.
        let path = (action == "wake") ? "/api/computers/wake" : "/api/computers/action?action=\(action)"

        guard let url = URL(string: "\(cleanUrl)\(path)") else {
            return (false, "Invalid host URL")
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 25
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        let body: [String: Any] = ["computerId": computerId, "delaySeconds": delaySeconds]
        guard let bodyData = try? JSONSerialization.data(withJSONObject: body) else {
            return (false, "Failed to encode the request")
        }
        request.httpBody = bodyData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse,
               let decoded = try? JSONDecoder().decode(ComputerPowerResponse.self, from: data) {
                return (http.statusCode == 200 && decoded.success, decoded.message ?? "Done.")
            }
            return (false, "Request failed with HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")
        } catch {
            return (false, error.localizedDescription)
        }
    }

    // MARK: - AI Assistant Engine

    public func sendAIChat(prompt: String) async -> (success: Bool, message: AIChatMessage?, error: String?) {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") { cleanUrl.removeLast() }
        guard let url = URL(string: "\(cleanUrl)/api/ai/chat") else {
            return (false, nil, "Invalid host URL")
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 25
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        let body: [String: Any] = ["prompt": prompt]
        guard let bodyData = try? JSONSerialization.data(withJSONObject: body) else {
            return (false, nil, "Failed to encode chat request")
        }
        request.httpBody = bodyData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                return (false, nil, "AI endpoint returned error code \(code)")
            }

            guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let content = json["content"] as? String else {
                return (false, nil, "Malformed AI response")
            }

            var proposed: AIChatProposedAction? = nil
            if let pDict = json["proposedAction"] as? [String: Any],
               let id = pDict["id"] as? String,
               let title = pDict["title"] as? String,
               let desc = pDict["description"] as? String,
               let actionType = pDict["actionType"] as? String {
                let reqConf = pDict["requiresConfirmation"] as? Bool ?? true
                let status = pDict["status"] as? String ?? "pending"
                let params = pDict["parameters"] as? [String: String]
                proposed = AIChatProposedAction(id: id, title: title, description: desc, actionType: actionType, requiresConfirmation: reqConf, status: status, parameters: params)
            }

            // Tappable next-step chips, when the engine produced any.
            let suggestions = (json["followUpSuggestions"] as? [String]) ?? []

            let engine = json["engine"] as? String

            let chatMsg = AIChatMessage(role: "assistant", content: content, proposedAction: proposed,
                                         followUpSuggestions: suggestions, engine: engine)
            return (true, chatMsg, nil)
        } catch {
            return (false, nil, error.localizedDescription)
        }
    }

    public func executeAIAction(actionId: String, userApproved: Bool) async -> (success: Bool, message: String) {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") { cleanUrl.removeLast() }
        guard let url = URL(string: "\(cleanUrl)/api/ai/action/execute") else {
            return (false, "Invalid host URL")
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 20
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        let body: [String: Any] = [
            "actionId": actionId,
            "userApproved": userApproved
        ]
        guard let bodyData = try? JSONSerialization.data(withJSONObject: body) else {
            return (false, "Failed to encode action payload")
        }
        request.httpBody = bodyData

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            if let http = response as? HTTPURLResponse,
               let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                let msg = json["message"] as? String ?? "Action processed"
                return (http.statusCode == 200, msg)
            }
            return (false, "Failed with HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")
        } catch {
            return (false, error.localizedDescription)
        }
    }

    public func clearAIChatHistory() async -> Bool {
        var cleanUrl = activeBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") { cleanUrl.removeLast() }
        guard let url = URL(string: "\(cleanUrl)/api/ai/history/clear") else { return false }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 8
        if let auth = getAuthorizationHeader() {
            request.addValue(auth, forHTTPHeaderField: "Authorization")
        }

        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            return (response as? HTTPURLResponse)?.statusCode == 200
        } catch {
            return false
        }
    }

}

// MARK: - AI Models

public struct AIChatProposedAction: Codable, Identifiable, Equatable {
    public let id: String
    public let title: String
    public let description: String
    public let actionType: String
    public let requiresConfirmation: Bool
    public var status: String
    public let parameters: [String: String]?

    public init(id: String, title: String, description: String, actionType: String, requiresConfirmation: Bool, status: String, parameters: [String: String]? = nil) {
        self.id = id
        self.title = title
        self.description = description
        self.actionType = actionType
        self.requiresConfirmation = requiresConfirmation
        self.status = status
        self.parameters = parameters
    }
}

public struct AIChatMessage: Identifiable, Equatable {
    public let id: String
    public let role: String // "user", "assistant"
    public let content: String
    public let timestamp: Date
    public var proposedAction: AIChatProposedAction?

    /// Tappable next-step chips rendered under the reply.
    public var followUpSuggestions: [String]

    /// Which engine answered: "ollama", "cloud", or "heuristics".
    public var engine: String?

    public init(id: String = UUID().uuidString, role: String, content: String, timestamp: Date = Date(), proposedAction: AIChatProposedAction? = nil, followUpSuggestions: [String] = [], engine: String? = nil) {
        self.id = id
        self.role = role
        self.content = content
        self.timestamp = timestamp
        self.proposedAction = proposedAction
        self.followUpSuggestions = followUpSuggestions
        self.engine = engine
    }
}

