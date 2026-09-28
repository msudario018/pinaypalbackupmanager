import Foundation
import UserNotifications
import UIKit

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
        attachAppIcon(to: content)
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
        attachAppIcon(to: content)
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
        attachAppIcon(to: content)
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
        attachAppIcon(to: content)
        add(content, identifier: "outdated_\(service.lowercased())_\(UUID().uuidString)")
    }

    /// Fired when the app cannot reach the PC over LAN, the Cloudflare Tunnel, or Tailscale —
    /// typically when the user is off-site and the Tailscale VPN is switched off on the iPhone.
    public func sendTailscaleEnableReminder(tailscaleConfigured: Bool, tunnelConfigured: Bool) {
        guard UserDefaults.standard.object(forKey: "pp_notify_tailscale") as? Bool ?? true else { return }
        guard shouldSend(key: "tailscale_enable_reminder", cooldown: 1800) else { return }

        let content = UNMutableNotificationContent()
        if tailscaleConfigured {
            content.title = "📶 Tailscale Appears Disabled"
            content.subtitle = "Cloudflare Tunnel is down and the LAN is unreachable"
            content.body = "You're outside the local network and the Cloudflare Tunnel isn't responding. Open the Tailscale app and turn on the VPN to reconnect to your PC over its private 100.x address."
        } else if tunnelConfigured {
            content.title = "📶 No Route to Backup Server"
            content.subtitle = "Cloudflare Tunnel is unreachable from here"
            content.body = "The tunnel URL isn't answering and you're off the local network. Enable Tailscale on this iPhone and your PC for a private backup link that works anywhere."
        } else {
            content.title = "📶 Enable Tailscale to Stay Connected"
            content.subtitle = "PC unreachable from this network"
            content.body = "Your PC can't be reached right now. Install Tailscale on this iPhone and the PC, then add its 100.x URL in Connection Settings for remote access anywhere."
        }
        content.sound = .default
        if #available(iOS 15.0, *) {
            content.interruptionLevel = .timeSensitive
        }
        content.categoryIdentifier = "BACKUP_FAILURE"
        content.userInfo = ["destination": "activity"]
        attachAppIcon(to: content)
        add(content, identifier: "tailscale_reminder_\(UUID().uuidString)")
    }

    public func setBadgeCount(_ count: Int) {
        if #available(iOS 16.0, *) {
            UNUserNotificationCenter.current().setBadgeCount(count)
        }
    }

    public func sendTestNotification() {
        let content = UNMutableNotificationContent()
        content.title = "🛡️ PinayPal Backup Alerts Ready"
        content.subtitle = "Notification channel operational"
        content.body = "You will receive real-time backup alerts, website status updates, and low disk warnings."
        content.sound = .default
        if #available(iOS 15.0, *) {
            content.interruptionLevel = .active
        }
        content.userInfo = ["destination": "activity"]
        attachAppIcon(to: content)
        add(content, identifier: "notification_test", diagnostic: "Test notification scheduled. If PinayPal Backup is open, it should still show a banner.")
    }

    private func configureCategories() {
        let retry = UNNotificationAction(identifier: "RETRY_BACKUP", title: "Retry backup", options: [.foreground])
        let details = UNNotificationAction(identifier: "VIEW_LOGS", title: "View details", options: [.foreground])
        let failure = UNNotificationCategory(identifier: "BACKUP_FAILURE", actions: [retry, details], intentIdentifiers: [], options: [])
        let success = UNNotificationCategory(identifier: "BACKUP_SUCCESS", actions: [], intentIdentifiers: [], options: [])
        UNUserNotificationCenter.current().setNotificationCategories([failure, success])
    }

    private func attachAppIcon(to content: UNMutableNotificationContent) {
        let image = UIImage(named: "NotificationLogo") ?? UIImage(named: "AppLogo")
        guard let image, let data = image.pngData() else { return }
        let tempDir = FileManager.default.temporaryDirectory
        let iconFile = tempDir.appendingPathComponent("NotificationEmblem_\(UUID().uuidString).png")
        do {
            try data.write(to: iconFile)
            let attachment = try UNNotificationAttachment(identifier: "app_emblem_\(UUID().uuidString)", url: iconFile, options: nil)
            content.attachments = [attachment]
        } catch {
            print("[NotificationService] Attachment error: \(error)")
        }
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
