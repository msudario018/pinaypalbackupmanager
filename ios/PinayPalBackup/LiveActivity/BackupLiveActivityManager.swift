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
            currentActivity = Activity<BackupActivityAttributes>.activities.first
            isActivityActive = currentActivity != nil
            currentService = (currentActivity as? Activity<BackupActivityAttributes>)?.attributes.serviceName ?? ""
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

            if currentActivity == nil {
                restoreActiveActivityIfNeeded()
            }
            if let currentActivity = currentActivity as? Activity<BackupActivityAttributes>, currentActivity.attributes.serviceName.caseInsensitiveCompare(service) == .orderedSame {
                lastDiagnosticMessage = "Live Activity is already active for \(service)."
                return true
            }
            if currentActivity != nil {
                endBackupActivity(success: true, message: "New backup started")
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
                serviceIcon: service.lowercased().contains("sql") ? "cylinder.split.1x2" : (service.lowercased().contains("ftp") ? "externaldrive.fill" : "envelope.badge.fill")
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

    public func updateBackupActivity(progress: Double, status: String, message: String, speedText: String? = nil, etaText: String? = nil) {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            guard let activity = currentActivity as? Activity<BackupActivityAttributes> else { return }

            let updatedState = BackupActivityAttributes.ContentState(
                service: activity.attributes.serviceName,
                status: status,
                progress: progress,
                isComplete: false,
                message: message,
                speedText: speedText,
                etaText: etaText
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
            guard let activity = currentActivity as? Activity<BackupActivityAttributes> else { return }

            let finalState = BackupActivityAttributes.ContentState(
                service: activity.attributes.serviceName,
                status: success ? "Completed" : "Failed",
                progress: 1.0,
                isComplete: true,
                message: message
            )

            // Clear the in-memory handle first. A following backup can then create its
            // own activity immediately instead of updating the previous service label.
            self.currentActivity = nil
            self.isActivityActive = false
            self.currentService = ""
            Task {
                await activity.end(.init(state: finalState, staleDate: nil), dismissalPolicy: .after(.now + 5))
                await MainActor.run {
                    self.lastDiagnosticMessage = "Live Activity finished: \(success ? "success" : "failed")."
                }
            }
        }
        #endif
    }
}
