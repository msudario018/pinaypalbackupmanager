import Foundation
import UserNotifications

public class NotificationService: NSObject, ObservableObject {
    public static let shared = NotificationService()

    @Published public var isAuthorized: Bool = false
    @Published public var notifyOnSuccess: Bool = UserDefaults.standard.object(forKey: "pp_notify_success") as? Bool ?? true
    @Published public var notifyOnFailure: Bool = UserDefaults.standard.object(forKey: "pp_notify_failure") as? Bool ?? true
    @Published public var notifyOnLowDisk: Bool = UserDefaults.standard.object(forKey: "pp_notify_low_disk") as? Bool ?? true

    private override init() {
        super.init()
        checkAuthorization()
    }

    public func requestAuthorization() async -> Bool {
        do {
            let granted = try await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .badge, .sound])
            await MainActor.run {
                self.isAuthorized = granted
            }
            return granted
        } catch {
            return false
        }
    }

    public func checkAuthorization() {
        UNUserNotificationCenter.current().getNotificationSettings { settings in
            DispatchQueue.main.async {
                self.isAuthorized = settings.authorizationStatus == .authorized
            }
        }
    }

    public func updateSettings(success: Bool, failure: Bool, lowDisk: Bool) {
        self.notifyOnSuccess = success
        self.notifyOnFailure = failure
        self.notifyOnLowDisk = lowDisk
        UserDefaults.standard.set(success, forKey: "pp_notify_success")
        UserDefaults.standard.set(failure, forKey: "pp_notify_failure")
        UserDefaults.standard.set(lowDisk, forKey: "pp_notify_low_disk")
    }

    public func sendBackupNotification(service: String, success: Bool, details: String) {
        if success && !notifyOnSuccess { return }
        if !success && !notifyOnFailure { return }

        let content = UNMutableNotificationContent()
        content.title = success ? "🛡️ Backup Completed: \(service)" : "⚠️ Backup Failed: \(service)"
        content.body = details
        content.sound = success ? .default : .defaultCritical

        let request = UNNotificationRequest(
            identifier: UUID().uuidString,
            content: content,
            trigger: nil // deliver immediately
        )

        UNUserNotificationCenter.current().add(request) { _ in }
    }

    public func sendLowDiskAlert(diskLetter: String, freeGb: Double, percentUsed: Double) {
        guard notifyOnLowDisk else { return }

        let content = UNMutableNotificationContent()
        content.title = "💾 Low Disk Space Warning"
        content.body = "Drive \(diskLetter) is at \(Int(percentUsed))% capacity. Only \(String(format: "%.1f", freeGb)) GB remaining!"
        content.sound = .defaultCritical

        let request = UNNotificationRequest(
            identifier: "low_disk_\(diskLetter)",
            content: content,
            trigger: nil
        )

        UNUserNotificationCenter.current().add(request) { _ in }
    }
}
