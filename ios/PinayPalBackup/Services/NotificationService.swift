import Foundation
import UserNotifications

public enum NotificationNavigationRequest: Equatable {
    case activity
    case logs
}

@MainActor
public final class NotificationService: NSObject, ObservableObject {
    public static let shared = NotificationService()

    @Published public private(set) var isAuthorized = false
    @Published public var navigationRequest: NotificationNavigationRequest?
    @Published public var retryService: String?
    @Published public var notifyOnSuccess = UserDefaults.standard.object(forKey: "pp_notify_success") as? Bool ?? true
    @Published public var notifyOnFailure = UserDefaults.standard.object(forKey: "pp_notify_failure") as? Bool ?? true
    @Published public var notifyOnLowDisk = UserDefaults.standard.object(forKey: "pp_notify_low_disk") as? Bool ?? true
    @Published public var notifyOnReminder = UserDefaults.standard.object(forKey: "pp_notify_reminder") as? Bool ?? true
    @Published public var notifyOnDailyDigest = UserDefaults.standard.object(forKey: "pp_notify_daily_digest") as? Bool ?? true

    private override init() {
        super.init()
        UNUserNotificationCenter.current().delegate = self
        configureCategories()
        checkAuthorization()
    }

    public func requestAuthorization() async -> Bool {
        do {
            let granted = try await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .badge, .sound])
            isAuthorized = granted
            return granted
        } catch {
            isAuthorized = false
            return false
        }
    }

    public func checkAuthorization() {
        UNUserNotificationCenter.current().getNotificationSettings { [weak self] settings in
            Task { @MainActor in self?.isAuthorized = settings.authorizationStatus == .authorized }
        }
    }

    public func clearNavigationRequest() { navigationRequest = nil }
    public func clearRetryRequest() { retryService = nil }

    public func updateSettings(success: Bool, failure: Bool, lowDisk: Bool, reminder: Bool = true, digest: Bool = true) {
        notifyOnSuccess = success
        notifyOnFailure = failure
        notifyOnLowDisk = lowDisk
        notifyOnReminder = reminder
        notifyOnDailyDigest = digest
        UserDefaults.standard.set(success, forKey: "pp_notify_success")
        UserDefaults.standard.set(failure, forKey: "pp_notify_failure")
        UserDefaults.standard.set(lowDisk, forKey: "pp_notify_low_disk")
        UserDefaults.standard.set(reminder, forKey: "pp_notify_reminder")
        UserDefaults.standard.set(digest, forKey: "pp_notify_daily_digest")
    }

    public func sendBackupNotification(service: String, success: Bool, details: String) {
        guard !success ? notifyOnFailure : notifyOnSuccess else { return }
        let content = UNMutableNotificationContent()
        content.title = success ? "Backup completed: \(service)" : "Backup failed: \(service)"
        content.body = details
        content.sound = .default
        content.categoryIdentifier = success ? "BACKUP_SUCCESS" : "BACKUP_FAILURE"
        content.userInfo = ["service": service.lowercased(), "destination": success ? "activity" : "logs"]
        add(content, identifier: "backup_\(service)_\(UUID().uuidString)")
    }

    public func sendLowDiskAlert(diskLetter: String, freeGb: Double, percentUsed: Double) {
        guard notifyOnLowDisk, shouldSend(key: "low_disk_\(diskLetter)", cooldown: 86_400) else { return }
        let content = UNMutableNotificationContent()
        content.title = "Low disk space"
        content.body = "Drive \(diskLetter) is \(Int(percentUsed))% full; \(String(format: "%.1f", freeGb)) GB remains."
        content.sound = .default
        content.userInfo = ["destination": "activity"]
        add(content, identifier: "low_disk_\(diskLetter)")
    }

    public func sendTestNotification() {
        let content = UNMutableNotificationContent()
        content.title = "PinayPal notifications are ready"
        content.body = "You will receive backup and disk-space alerts on this device."
        content.sound = .default
        content.userInfo = ["destination": "activity"]
        add(content, identifier: "notification_test")
    }

    private func configureCategories() {
        let retry = UNNotificationAction(identifier: "RETRY_BACKUP", title: "Retry backup", options: [.foreground])
        let details = UNNotificationAction(identifier: "VIEW_LOGS", title: "View details", options: [.foreground])
        let failure = UNNotificationCategory(identifier: "BACKUP_FAILURE", actions: [retry, details], intentIdentifiers: [], options: [])
        let success = UNNotificationCategory(identifier: "BACKUP_SUCCESS", actions: [], intentIdentifiers: [], options: [])
        UNUserNotificationCenter.current().setNotificationCategories([failure, success])
    }

    private func add(_ content: UNMutableNotificationContent, identifier: String) {
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: identifier, content: content, trigger: nil)) { _ in }
    }

    private func shouldSend(key: String, cooldown: TimeInterval) -> Bool {
        let stampKey = "pp_notification_\(key)_last_sent"
        let now = Date().timeIntervalSince1970
        let previous = UserDefaults.standard.double(forKey: stampKey)
        guard now - previous >= cooldown else { return false }
        UserDefaults.standard.set(now, forKey: stampKey)
        return true
    }
}

extension NotificationService: UNUserNotificationCenterDelegate {
    nonisolated public func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .sound, .badge]
    }

    nonisolated public func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
        let service = response.notification.request.content.userInfo["service"] as? String
        let destination = response.notification.request.content.userInfo["destination"] as? String
        await MainActor.run {
            switch response.actionIdentifier {
            case "RETRY_BACKUP":
                self.retryService = service
                self.navigationRequest = .activity
            case "VIEW_LOGS":
                self.navigationRequest = .logs
            default:
                self.navigationRequest = destination == "logs" ? .logs : .activity
            }
        }
    }
}
