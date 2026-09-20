import Foundation
import Combine

@MainActor
public class PinayPalAPIService: ObservableObject {
    @Published public var serverUrl: String = UserDefaults.standard.string(forKey: "pp_server_url") ?? "http://localhost:8080"
    @Published public var accessPin: String = UserDefaults.standard.string(forKey: "pp_access_pin") ?? ""
    
    @Published public var status: StatusResponse? = nil
    @Published public var history: [BackupHistoryItem] = []
    @Published public var logs: [String] = []
    @Published public var isConnecting: Bool = false
    @Published public var isOnline: Bool = false
    @Published public var lastErrorMessage: String? = nil

    private var pollTimer: AnyCancellable?

    public init() {
        startPolling()
    }

    public func saveSettings(url: String, pin: String) {
        var cleanUrl = url.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.hasSuffix("/") {
            cleanUrl.removeLast()
        }
        self.serverUrl = cleanUrl
        self.accessPin = pin.trimmingCharacters(in: .whitespacesAndNewlines)

        UserDefaults.standard.set(self.serverUrl, forKey: "pp_server_url")
        UserDefaults.standard.set(self.accessPin, forKey: "pp_access_pin")

        Task {
            await fetchAll()
        }
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

    public func fetchAll() async {
        isConnecting = true
        defer { isConnecting = false }
        await fetchStatus()
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
        if !accessPin.isEmpty {
            request.addValue("Bearer \(accessPin)", forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                isOnline = false
                return
            }

            let decoder = JSONDecoder()
            let decoded = try decoder.decode(StatusResponse.self, from: data)
            self.status = decoded
            self.isOnline = true
            self.lastErrorMessage = nil
        } catch {
            self.isOnline = false
            self.lastErrorMessage = error.localizedDescription
        }
    }

    public func fetchHistory() async {
        guard let url = URL(string: "\(serverUrl)/api/history") else { return }
        var request = URLRequest(url: url)
        if !accessPin.isEmpty {
            request.addValue("Bearer \(accessPin)", forHTTPHeaderField: "Authorization")
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
        if !accessPin.isEmpty {
            request.addValue("Bearer \(accessPin)", forHTTPHeaderField: "Authorization")
        }

        do {
            let (data, _) = try await URLSession.shared.data(for: request)
            let lines = try JSONDecoder().decode([String].self, from: data)
            self.logs = lines
        } catch { }
    }

    public func triggerBackup(service: String) async -> Bool {
        guard let url = URL(string: "\(serverUrl)/api/backup/\(service)") else { return false }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        if !accessPin.isEmpty {
            request.addValue("Bearer \(accessPin)", forHTTPHeaderField: "Authorization")
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
        if !accessPin.isEmpty {
            request.addValue("Bearer \(accessPin)", forHTTPHeaderField: "Authorization")
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
