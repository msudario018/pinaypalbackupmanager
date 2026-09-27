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
    @Published public private(set) var authorizationDescription = "Checking notification permission…"
    @Published public private(set) var lastDiagnosticMessage = "Not tested yet"
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
            authorizationDescription = granted ? "Notifications are enabled" : "Notifications were not granted"
            return granted
        } catch {
            isAuthorized = false
            authorizationDescription = "Notification request failed: \(error.localizedDescription)"
            return false
        }
    }

    public func checkAuthorization() {
        UNUserNotificationCenter.current().getNotificationSettings { [weak self] settings in
            Task { @MainActor in
                guard let self else { return }
                self.isAuthorized = settings.authorizationStatus == .authorized
                switch settings.authorizationStatus {
                case .authorized: self.authorizationDescription = "Notifications are enabled"
                case .denied: self.authorizationDescription = "Disabled in iOS Settings"
                case .notDetermined: self.authorizationDescription = "Permission has not been requested"
                case .provisional: self.authorizationDescription = "Provisional notification permission"
                case .ephemeral: self.authorizationDescription = "Temporary notification permission"
                @unknown default: self.authorizationDescription = "Unknown notification permission"
                }
            }
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
        let s = service.lowercased()
        let iconBadge: String
        if s.contains("sql") || s.contains("database") {
            iconBadge = "🗄️"
        } else if s.contains("mailchimp") || s.contains("email") {
            iconBadge = "📬"
        } else if s.contains("ftp") || s.contains("website") {
            iconBadge = "🌐"
        } else {
            iconBadge = "💾"
        }

        let statusSymbol = success ? "✅" : "⚠️"
        content.title = "\(statusSymbol) \(iconBadge) \(service.uppercased()) Backup \(success ? "Completed" : "Failed")"
        content.subtitle = success ? "All archives verified & secured" : "Attention required — check backup logs"
        content.body = details
        content.sound = .default
        if #available(iOS 15.0, *) {
            content.interruptionLevel = success ? .active : .timeSensitive
        }
        content.categoryIdentifier = success ? "BACKUP_SUCCESS" : "BACKUP_FAILURE"
        content.userInfo = ["service": service.lowercased(), "destination": success ? "activity" : "logs"]
        add(content, identifier: "backup_\(service)_\(UUID().uuidString)")
    }

    public func sendLowDiskAlert(diskLetter: String, freeGb: Double, percentUsed: Double) {
        guard notifyOnLowDisk, shouldSend(key: "low_disk_\(diskLetter)", cooldown: 86_400) else { return }
        let content = UNMutableNotificationContent()
        content.title = "🚨 Low Disk Space Warning"
        content.subtitle = "Drive \(diskLetter): \(Int(percentUsed))% Full"
        content.body = "Only \(String(format: "%.1f", freeGb)) GB remaining on Drive \(diskLetter). Older backups may need cleanup."
        content.sound = .default
        if #available(iOS 15.0, *) {
            content.interruptionLevel = .timeSensitive
        }
        content.userInfo = ["destination": "activity"]
        add(content, identifier: "low_disk_\(diskLetter)")
    }

    /// Delivers a local alert after the app observes the protected public site
    /// change state. Remote/background delivery remains the server/APNs path.
    public func sendWebsiteStatusNotification(isOnline: Bool, details: String) {
        guard isOnline ? notifyOnSuccess : notifyOnFailure else { return }
        let stateKey = isOnline ? "website_recovered" : "website_offline"
        guard shouldSend(key: stateKey, cooldown: 300) else { return }

        let content = UNMutableNotificationContent()
        content.title = isOnline ? "🟢 Website Online: pinaypal.net" : "🔴 Website Alert: pinaypal.net Offline"
        content.subtitle = isOnline ? "Health check passed successfully" : "HTTP health probe did not respond"
        content.body = details
        content.sound = .default
        if #available(iOS 15.0, *) {
            content.interruptionLevel = isOnline ? .active : .timeSensitive
        }
        content.categoryIdentifier = isOnline ? "BACKUP_SUCCESS" : "BACKUP_FAILURE"
        content.userInfo = ["destination": "activity"]
        add(content, identifier: "website_\(stateKey)_\(UUID().uuidString)")
    }

    public func sendOutdatedBackupAlert(service: String, details: String) {
        guard notifyOnFailure, shouldSend(key: "outdated_\(service.lowercased())", cooldown: 21_600) else { return } // 6 hour cooldown
        let content = UNMutableNotificationContent()
        let s = service.lowercased()
        let iconBadge = s.contains("sql") ? "🗄️" : (s.contains("mailchimp") ? "📬" : "🌐")
        content.title = "⚠️ Outdated Backup Alert: \(iconBadge) \(service.uppercased())"
        content.subtitle = "Local backup is stale (> 24 hours) or remote is newer"
        content.body = details
        content.sound = .default
        if #available(iOS 15.0, *) {
            content.interruptionLevel = .timeSensitive
        }
        content.categoryIdentifier = "BACKUP_FAILURE"
        content.userInfo = ["service": service.lowercased(), "destination": "activity"]
        add(content, identifier: "outdated_\(service.lowercased())_\(UUID().uuidString)")
    }

    public func setBadgeCount(_ count: Int) {
        if #available(iOS 16.0, *) {
            UNUserNotificationCenter.current().setBadgeCount(count)
        }
    }

    public func sendTestNotification() {
        let content = UNMutableNotificationContent()
        content.title = "🛡️ PinayPal Alerts Ready"
        content.subtitle = "Notification channel operational"
        content.body = "You will receive real-time backup alerts, website status updates, and low disk warnings."
        content.sound = .default
        if #available(iOS 15.0, *) {
            content.interruptionLevel = .active
        }
        content.userInfo = ["destination": "activity"]
        add(content, identifier: "notification_test", diagnostic: "Test notification scheduled. If PinayPal is open, it should still show a banner.")
    }

    private func configureCategories() {
        let retry = UNNotificationAction(identifier: "RETRY_BACKUP", title: "Retry backup", options: [.foreground])
        let details = UNNotificationAction(identifier: "VIEW_LOGS", title: "View details", options: [.foreground])
        let failure = UNNotificationCategory(identifier: "BACKUP_FAILURE", actions: [retry, details], intentIdentifiers: [], options: [])
        let success = UNNotificationCategory(identifier: "BACKUP_SUCCESS", actions: [], intentIdentifiers: [], options: [])
        UNUserNotificationCenter.current().setNotificationCategories([failure, success])
    }

    private func add(_ content: UNMutableNotificationContent, identifier: String, diagnostic: String? = nil) {
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: identifier, content: content, trigger: nil)) { [weak self] error in
            Task { @MainActor in
                self?.lastDiagnosticMessage = error == nil ? (diagnostic ?? "Notification scheduled.") : "Notification scheduling failed: \(error!.localizedDescription)"
            }
        }
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
