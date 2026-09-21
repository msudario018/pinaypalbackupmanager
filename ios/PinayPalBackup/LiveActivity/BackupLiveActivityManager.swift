import Foundation
#if canImport(ActivityKit)
import ActivityKit
#endif

@MainActor
public class BackupLiveActivityManager: ObservableObject {
    public static let shared = BackupLiveActivityManager()

    @Published public var isActivityActive: Bool = false
    @Published public var currentService: String = ""

    #if canImport(ActivityKit)
    private var currentActivity: Activity<BackupActivityAttributes>?
    #endif

    private init() {}

    public func restoreActiveActivityIfNeeded() {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            currentActivity = Activity<BackupActivityAttributes>.activities.first
            isActivityActive = currentActivity != nil
            currentService = currentActivity?.attributes.serviceName ?? ""
        }
        #endif
    }

    public func startBackupActivity(service: String) {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            let isEnabled = UserDefaults.standard.object(forKey: "pp_live_activities_enabled") as? Bool ?? true
            guard isEnabled, ActivityAuthorizationInfo().areActivitiesEnabled else { return }

            if currentActivity == nil {
                restoreActiveActivityIfNeeded()
            }
            if let currentActivity, currentActivity.attributes.serviceName.caseInsensitiveCompare(service) == .orderedSame {
                return
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
            } catch {
                print("Failed to start Live Activity: \(error)")
            }
        }
        #endif
    }

    public func updateBackupActivity(progress: Double, status: String, message: String, speedText: String? = nil, etaText: String? = nil) {
        #if canImport(ActivityKit)
        if #available(iOS 16.2, *) {
            guard let activity = currentActivity else { return }

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
            guard let activity = currentActivity else { return }

            let finalState = BackupActivityAttributes.ContentState(
                service: activity.attributes.serviceName,
                status: success ? "Completed" : "Failed",
                progress: 1.0,
                isComplete: true,
                message: message
            )

            Task {
                await activity.end(.init(state: finalState, staleDate: nil), dismissalPolicy: .after(.now + 5))
                await MainActor.run {
                    self.currentActivity = nil
                    self.isActivityActive = false
                    self.currentService = ""
                }
            }
        }
        #endif
    }
}
