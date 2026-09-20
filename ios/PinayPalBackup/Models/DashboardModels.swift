import Foundation

public struct StatusResponse: Codable {
    public let appName: String?
    public let version: String?
    public let isOnline: Bool?
    public let system: SystemSpecs?
    public let schedules: ScheduleSpecs?
    public let services: ServicesContainer?
    public let health: HealthSpecs?
    public let lastBackup: LastBackupSpec?
}

public struct SystemSpecs: Codable {
    public let hostname: String?
    public let os: String?
    public let architecture: String?
    public let cores: Int?
    public let systemUptime: String?
    public let appUptime: String?
    public let localIp: String?
}

public struct ScheduleSpecs: Codable {
    public let ftpDaily: String?
    public let sqlDaily: String?
    public let mailchimpDaily: String?
    public let healthDaily: String?
    public let ftpInterval: String?
    public let sqlInterval: String?
    public let mailchimpInterval: String?
}

public struct ServicesContainer: Codable {
    public let ftp: ServiceItem?
    public let sql: ServiceItem?
    public let mailchimp: ServiceItem?
}

public struct ServiceItem: Codable {
    public let name: String?
    public let host: String?
    public let port: Int?
    public let user: String?
    public let folder: String?
    public let configured: Bool?
    public let fileCount: Int?
    public let sizeBytes: Int64?
    public let audienceId: String?
}

public struct HealthSpecs: Codable {
    public let status: String?
    public let isHealthy: Bool?
    public let lastCheck: String?
    public let cpu: Double?
    public let memory: MemorySpec?
    public let disk: DiskSpec?
    public let drives: [DriveSpec]?
    public let backupFolders: [BackupFolderSpec]?
}

public struct MemorySpec: Codable {
    public let percent: Double?
    public let totalBytes: Int64?
    public let usedBytes: Int64?
    public let availableBytes: Int64?
    public let appBytes: Int64?
}

public struct DiskSpec: Codable {
    public let percent: Double?
    public let primaryDriveLetter: String?
    public let primaryDriveLabel: String?
    public let totalGB: Int64?
    public let availableGB: Int64?
    public let usedGB: Int64?
    public let backupPath: String?
}

public struct DriveSpec: Codable, Identifiable {
    public var id: String { name ?? UUID().uuidString }
    public let name: String?
    public let volumeLabel: String?
    public let totalBytes: Int64?
    public let freeBytes: Int64?
    public let usedBytes: Int64?
    public let usedPercent: Double?
    public let isBackupDrive: Bool?
    public let isSystemDrive: Bool?
}

public struct BackupFolderSpec: Codable, Identifiable {
    public var id: String { (service ?? "") + (path ?? "") }
    public let service: String?
    public let path: String?
    public let totalSizeBytes: Int64?
    public let fileCount: Int?
    public let exists: Bool?
}

public struct LastBackupSpec: Codable {
    public let service: String?
    public let time: String?
    public let duration: Double?
    public let sizeBytes: Int64?
}

public struct BackupHistoryItem: Codable, Identifiable {
    public let id: String
    public let service: String
    public let type: String?
    public let status: String
    public let time: String
    public let durationSeconds: Double?
    public let sizeBytes: Int64?
    public let filename: String?
    public let hasFile: Bool?
}

public struct TriggerResponse: Codable {
    public let success: Bool
    public let message: String?
}

public struct RemoteSettings: Codable, Equatable {
    public var ftpDailySyncHourMnl: Int
    public var ftpDailySyncMinuteMnl: Int
    public var sqlDailySyncHourMnl: Int
    public var sqlDailySyncMinuteMnl: Int
    public var mailchimpDailySyncHourMnl: Int
    public var mailchimpDailySyncMinuteMnl: Int
    public var retentionDays: Int
    public var dailyHealthCheckEnabled: Bool
    public var dailyHealthCheckHour: Int
    public var autoStartWindows: Bool
    public var notificationSound: Bool

    public init(
        ftpDailySyncHourMnl: Int = 22,
        ftpDailySyncMinuteMnl: Int = 0,
        sqlDailySyncHourMnl: Int = 17,
        sqlDailySyncMinuteMnl: Int = 0,
        mailchimpDailySyncHourMnl: Int = 18,
        mailchimpDailySyncMinuteMnl: Int = 0,
        retentionDays: Int = 7,
        dailyHealthCheckEnabled: Bool = true,
        dailyHealthCheckHour: Int = 8,
        autoStartWindows: Bool = false,
        notificationSound: Bool = true
    ) {
        self.ftpDailySyncHourMnl = ftpDailySyncHourMnl
        self.ftpDailySyncMinuteMnl = ftpDailySyncMinuteMnl
        self.sqlDailySyncHourMnl = sqlDailySyncHourMnl
        self.sqlDailySyncMinuteMnl = sqlDailySyncMinuteMnl
        self.mailchimpDailySyncHourMnl = mailchimpDailySyncHourMnl
        self.mailchimpDailySyncMinuteMnl = mailchimpDailySyncMinuteMnl
        self.retentionDays = retentionDays
        self.dailyHealthCheckEnabled = dailyHealthCheckEnabled
        self.dailyHealthCheckHour = dailyHealthCheckHour
        self.autoStartWindows = autoStartWindows
        self.notificationSound = notificationSound
    }
}

