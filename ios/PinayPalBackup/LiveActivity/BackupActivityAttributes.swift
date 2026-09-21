import Foundation
#if canImport(ActivityKit)
import ActivityKit

public struct BackupActivityAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        public var service: String
        public var status: String
        public var progress: Double
        public var isComplete: Bool
        public var message: String
        public var speedText: String?
        public var etaText: String?
        public var serviceIcon: String?

        public init(
            service: String,
            status: String,
            progress: Double,
            isComplete: Bool,
            message: String,
            speedText: String? = nil,
            etaText: String? = nil,
            serviceIcon: String? = nil
        ) {
            self.service = service
            self.status = status
            self.progress = progress
            self.isComplete = isComplete
            self.message = message
            self.speedText = speedText
            self.etaText = etaText
            self.serviceIcon = serviceIcon
        }
    }

    public var serviceName: String
    public var startedAt: Date

    public init(serviceName: String, startedAt: Date = Date()) {
        self.serviceName = serviceName
        self.startedAt = startedAt
    }
}
#endif
