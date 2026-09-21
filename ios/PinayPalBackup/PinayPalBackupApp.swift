import SwiftUI

@main
struct PinayPalBackupApp: App {
    var body: some Scene {
        WindowGroup {
            MainView()
                .task {
                    _ = await NotificationService.shared.requestAuthorization()
                    BackupLiveActivityManager.shared.restoreActiveActivityIfNeeded()
                }
        }
    }
}
