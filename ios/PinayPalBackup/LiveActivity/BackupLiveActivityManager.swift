import Foundation
#if canImport(ActivityKit)
import ActivityKit
#endif

@MainActor
public class BackupLiveActivityManager: ObservableObject {
    public static let shared = BackupLiveActivityManager()

    @Published public var isActivityActive: Bool = false
    @Published public var currentService: String = ""
    @Published public private(set) var lastDiagnosticMessage: String = "Not tested yet"

    #if canImport(ActivityKit)
    private var currentActivity: Any?
    #endif

    private init() {}

    public func restoreActiveActivityIfNeeded() {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            let active = Activity<BackupActivityAttributes>.activities.first(where: { $0.activityState == .active })
            currentActivity = active
            isActivityActive = active != nil
            currentService = active?.attributes.serviceName ?? ""

            // Clean up any stale or ended activities left in the system
            for stale in Activity<BackupActivityAttributes>.activities where stale.activityState != .active {
                Task {
                    await stale.end(nil, dismissalPolicy: .immediate)
                }
            }
        }
        #endif
    }

    public var availabilityDescription: String {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            let appEnabled = UserDefaults.standard.object(forKey: "pp_live_activities_enabled") as? Bool ?? true
            return ActivityAuthorizationInfo().areActivitiesEnabled && appEnabled
                ? "Ready on this device"
                : "Disabled in iOS Settings or PinayPal settings"
        }
        return "Requires iOS 16.2 or later"
        #else
        return "ActivityKit is unavailable in this build"
        #endif
    }

    @discardableResult
    public func startBackupActivity(service: String) -> Bool {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            let isEnabled = UserDefaults.standard.object(forKey: "pp_live_activities_enabled") as? Bool ?? true
            guard isEnabled else {
                lastDiagnosticMessage = "Enable Live Activities in PinayPal Settings first."
                return false
            }
            guard ActivityAuthorizationInfo().areActivitiesEnabled else {
                lastDiagnosticMessage = "Enable Live Activities for PinayPal in iOS Settings."
                return false
            }

            // Prune ended or cancelled activities
            for stale in Activity<BackupActivityAttributes>.activities where stale.activityState != .active {
                Task { await stale.end(nil, dismissalPolicy: .immediate) }
            }

            // Check if there is an existing truly ACTIVE activity
            if let active = Activity<BackupActivityAttributes>.activities.first(where: { $0.activityState == .active }) {
                if active.attributes.serviceName.caseInsensitiveCompare(service) == .orderedSame {
                    self.currentActivity = active
                    self.isActivityActive = true
                    self.currentService = service
                    self.lastDiagnosticMessage = "Live Activity is active for \(service)."
                    return true
                } else {
                    // Different service, end previous activity immediately
                    Task { await active.end(nil, dismissalPolicy: .immediate) }
                }
            }

            let attributes = BackupActivityAttributes(serviceName: service.uppercased(), startedAt: Date())
            let initialContentState = BackupActivityAttributes.ContentState(
                service: service.uppercased(),
                status: "In Progress",
                progress: 0.15,
                isComplete: false,
                message: "Backing up \(service.uppercased())...",
                speedText: "Connecting...",
                etaText: "Estimating...",
                serviceIcon: serviceIconFor(service)
            )

            do {
                let activity = try Activity.request(
                    attributes: attributes,
                    content: .init(state: initialContentState, staleDate: nil),
                    pushType: nil
                )
                self.currentActivity = activity
                self.isActivityActive = true
                self.currentService = service
                self.lastDiagnosticMessage = "Live Activity started for \(service)."
                return true
            } catch {
                self.lastDiagnosticMessage = "Live Activity request failed: \(error.localizedDescription)"
                return false
            }
        }
        lastDiagnosticMessage = "Requires iOS 16.2 or later."
        return false
        #endif

        #if !canImport(ActivityKit)
        lastDiagnosticMessage = "ActivityKit is unavailable in this build."
        return false
        #endif
    }

    public func startTestActivity() {
        guard !isActivityActive else {
            lastDiagnosticMessage = "Finish the current backup before running the Live Activity test."
            return
        }
        guard startBackupActivity(service: "Live Activity Test") else { return }
        Task {
            try? await Task.sleep(nanoseconds: 3_000_000_000)
            updateBackupActivity(progress: 0.60, status: "Test update received", message: "Lock your device to view the activity.", speedText: "Test signal", etaText: "5 seconds")
            try? await Task.sleep(nanoseconds: 5_000_000_000)
            endBackupActivity(success: true, message: "Live Activity test completed")
        }
    }

    public func updateBackupActivity(progress: Double, status: String, message: String, speedText: String? = nil, etaText: String? = nil, serviceIcon: String? = nil) {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            guard let activity = currentActivity as? Activity<BackupActivityAttributes> else { return }

            let icon = serviceIcon ?? activity.content.state.serviceIcon ?? serviceIconFor(activity.attributes.serviceName)

            let updatedState = BackupActivityAttributes.ContentState(
                service: activity.attributes.serviceName,
                status: status,
                progress: progress,
                isComplete: false,
                message: message,
                speedText: speedText,
                etaText: etaText,
                serviceIcon: icon
            )

            Task {
                await activity.update(.init(state: updatedState, staleDate: nil))
            }
        }
        #endif
    }

    public func endBackupActivity(success: Bool, message: String) {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            let activity = (currentActivity as? Activity<BackupActivityAttributes>)
                ?? Activity<BackupActivityAttributes>.activities.first(where: { $0.activityState == .active })
            guard let activity = activity else {
                self.currentActivity = nil
                self.isActivityActive = false
                self.currentService = ""
                return
            }

            let finalState = BackupActivityAttributes.ContentState(
                service: activity.attributes.serviceName,
                status: success ? "Completed" : "Failed",
                progress: 1.0,
                isComplete: true,
                message: message,
                speedText: success ? "All files synced" : "Halted",
                etaText: success ? "0s" : "Error",
                serviceIcon: success ? "checkmark.seal.fill" : "exclamationmark.triangle.fill"
            )

            // Clear the in-memory handle first. A following backup can then create its
            // own activity immediately instead of updating the previous service label.
            self.currentActivity = nil
            self.isActivityActive = false
            self.currentService = ""
            Task {
                await activity.end(.init(state: finalState, staleDate: nil), dismissalPolicy: .after(.now + 4))
                await MainActor.run {
                    self.lastDiagnosticMessage = "Live Activity finished: \(success ? "success" : "failed")."
                }
            }
        }
        #endif
    }

    public func serviceIconFor(_ service: String) -> String {
        let s = service.lowercased()
        if s.contains("sql") || s.contains("database") { return "cylinder.split.1x2.fill" }
        if s.contains("mailchimp") || s.contains("email") { return "envelope.badge.shield.half.filled" }
        if s.contains("ftp") || s.contains("website") { return "globe.americas.fill" }
        return "arrow.triangle.2.circlepath.circle.fill"
    }
}
