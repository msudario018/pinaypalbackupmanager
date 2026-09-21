import Foundation
import UserNotifications

public class NotificationService: NSObject, ObservableObject {
    public static let shared = NotificationService()

    @Published public var isAuthorized: Bool = false
    @Published public var notifyOnSuccess: Bool = UserDefaults.standard.object(forKey: "pp_notify_success") as? Bool ?? true
    @Published public var notifyOnFailure: Bool = UserDefaults.standard.object(forKey: "pp_notify_failure") as? Bool ?? true
    @Published public var notifyOnLowDisk: Bool = UserDefaults.standard.object(forKey: "pp_notify_low_disk") as? Bool ?? true
    @Published public var notifyOnReminder: Bool = UserDefaults.standard.object(forKey: "pp_notify_reminder") as? Bool ?? true
    @Published public var notifyOnDailyDigest: Bool = UserDefaults.standard.object(forKey: "pp_notify_daily_digest") as? Bool ?? true

    private override init() {
        super.init()
        checkAuthorization()
        setupCategories()
    }

    private func setupCategories() {
        let retryAction = UNNotificationAction(identifier: "RETRY_BACKUP", title: "🔄 Retry Backup Now", options: [.foreground])
        let viewDetailsAction = UNNotificationAction(identifier: "VIEW_LOGS", title: "📋 View Error Logs", options: [.foreground])
        let failureCategory = UNNotificationCategory(
            identifier: "BACKUP_FAILURE",
            actions: [retryAction, viewDetailsAction],
            intentIdentifiers: [],
            options: .customDismissAction
        )

        let dismissAction = UNNotificationAction(identifier: "DISMISS_SUCCESS", title: "✓ Dismiss", options: [])
        let successCategory = UNNotificationCategory(
            identifier: "BACKUP_SUCCESS",
            actions: [dismissAction],
            intentIdentifiers: [],
            options: []
        )

        UNUserNotificationCenter.current().setNotificationCategories([failureCategory, successCategory])
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

    public func updateSettings(success: Bool, failure: Bool, lowDisk: Bool, reminder: Bool = true, digest: Bool = true) {
        self.notifyOnSuccess = success
        self.notifyOnFailure = failure
        self.notifyOnLowDisk = lowDisk
        self.notifyOnReminder = reminder
        self.notifyOnDailyDigest = digest
        UserDefaults.standard.set(success, forKey: "pp_notify_success")
        UserDefaults.standard.set(failure, forKey: "pp_notify_failure")
        UserDefaults.standard.set(lowDisk, forKey: "pp_notify_low_disk")
        UserDefaults.standard.set(reminder, forKey: "pp_notify_reminder")
        UserDefaults.standard.set(digest, forKey: "pp_notify_daily_digest")
    }

    public func sendBackupNotification(service: String, success: Bool, details: String) {
        if success && !notifyOnSuccess { return }
        if !success && !notifyOnFailure { return }

        let content = UNMutableNotificationContent()
        content.title = success ? "🛡️ Backup Completed: \(service)" : "⚠️ Backup Failed: \(service)"
        content.body = details
        content.sound = .default
        content.categoryIdentifier = success ? "BACKUP_SUCCESS" : "BACKUP_FAILURE"

        let request = UNNotificationRequest(
            identifier: UUID().uuidString,
            content: content,
            trigger: nil // deliver immediately
        )

        UNUserNotificationCenter.current().add(request) { _ in }
    }

    public func sendReminderNotification(service: String, scheduledTime: String) {
        guard notifyOnReminder else { return }

        let content = UNMutableNotificationContent()
        content.title = "⏰ Scheduled Backup Reminder"
        content.body = "\(service) backup is scheduled to run at \(scheduledTime) (in ~15 mins)."
        content.sound = .default

        let request = UNNotificationRequest(
            identifier: "reminder_\(service)",
            content: content,
            trigger: nil
        )

        UNUserNotificationCenter.current().add(request) { _ in }
    }

    public func sendDailyDigest(completedCount: Int, failedCount: Int, totalSizeFormatted: String) {
        guard notifyOnDailyDigest else { return }

        let content = UNMutableNotificationContent()
        content.title = "📊 Daily Backup Digest"
        content.body = "\(completedCount) completed, \(failedCount) failed. Total \(totalSizeFormatted) backed up today."
        content.sound = .default

        let request = UNNotificationRequest(
            identifier: "daily_digest",
            content: content,
            trigger: nil
        )

        UNUserNotificationCenter.current().add(request) { _ in }
    }

    public func sendLowDiskAlert(diskLetter: String, freeGb: Double, percentUsed: Double) {
        guard notifyOnLowDisk else { return }

        let content = UNMutableNotificationContent()
        content.title = "💾 Low Disk Space Warning"
        content.body = "Drive \(diskLetter) is at \(Int(percentUsed))% capacity. Only \(String(format: "%.1f", freeGb)) GB remaining!"
        content.sound = .default

        let request = UNNotificationRequest(
            identifier: "low_disk_\(diskLetter)",
            content: content,
            trigger: nil
        )

        UNUserNotificationCenter.current().add(request) { _ in }
    }
}
