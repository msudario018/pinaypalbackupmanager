import Foundation

public struct StatusResponse: Codable {
    public let appName: String?
    public let version: String?
    public let isOnline: Bool?
    public let system: SystemSpecs?
    public let schedules: ScheduleSpecs?
    public let services: ServicesContainer?
    public let health: HealthSpecs?
    public let website: WebsiteStatusSpec?
    public let lastBackup: LastBackupSpec?
    public let activeBackup: ActiveBackupSpec?
    public let hardware: HardwareTelemetrySpec?
}

public struct HardwareTelemetrySpec: Codable, Equatable {
    public let hostname: String?
    public let hostRole: String?
    public let osDescription: String?
    public let architecture: String?
    public let cpuName: String?
    public let cpuPhysicalCores: Int?
    public let cpuLogicalCores: Int?
    public let cpuUsagePercent: Double?
    public let cpuTempC: Double?
    public let cpuTempStatus: String?
    public let gpuName: String?
    public let gpuTempC: Double?
    public let gpuTempStatus: String?
    public let gpuUsagePercent: Double?
    public let gpuMemoryUsedMB: Int64?
    public let gpuMemoryTotalMB: Int64?
    public let gpuMemoryPercent: Double?
    public let gpuPowerWatts: Double?
    public let gpuDriverVersion: String?
    public let ramTotalGB: Double?
    public let ramUsedGB: Double?
    public let ramFreeGB: Double?
    public let ramUsagePercent: Double?
    public let appRamUsageMB: Double?
}

public struct WebsiteStatusSpec: Codable {
    public let url: String?
    public let isOnline: Bool?
    public let statusCode: Int?
    public let responseTimeMs: Int?
    public let error: String?
    public let checkedAt: String?
    public let changedAt: String?
    public let consecutiveFailures: Int?
}

public struct ActiveBackupSpec: Codable {
    public let isBusy: Bool?
    public let service: String?
    public let statusText: String?
    public let progress: Int?
    public let startedAt: String?
    public let lastUpdatedAt: String?
    public let activeServices: [ActiveBackupServiceSpec]?
}

public struct ActiveBackupServiceSpec: Codable, Identifiable {
    public let service: String?
    public let statusText: String?
    public let progress: Int?
    public let startedAt: String?
    public let lastUpdatedAt: String?

    public var id: String { service ?? UUID().uuidString }
}

public struct ConnectionQrPayload: Codable {
    public let localUrl: String
    public let allLocalUrls: [String]?
    public let fallbackUrl: String?
    public let cloudflareUrl: String?
    public let tailscaleUrl: String?
    public let pin: String?
    public let hostname: String?
    public let version: String?
}

public struct SystemSpecs: Codable {
    public let hostname: String?
    public let os: String?
    public let architecture: String?
    public let cores: Int?
    public let systemUptime: String?
    public let appUptime: String?
    public let localIp: String?
    public let tailscaleUrl: String?
    public let cloudflareUrl: String?
    public let cloudflareActive: Bool?
    public let cloudflareManaged: Bool?

    public var computerName: String? { hostname }
    public var osVersion: String? { os }
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
    public let freshness: ServiceFreshnessSpec?
}

public struct ServiceFreshnessSpec: Codable {
    public let status: String?
    public let isOutdated: Bool?
    public let badgeText: String?
    public let lastBackupTime: String?
    public let relativeTime: String?
    public let ageHours: Double?
    public let thresholdHours: Double?
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

public struct PingResponse: Codable {
    public let appName: String?
    public let version: String?
    public let status: String?
    public let hostname: String?
    public let localIp: String?
    public let port: Int?
    public let hasUsers: Bool?
    public let requireAuth: Bool?
    public let serverTime: String?
}

// MARK: - Managed computers (fleet)

public struct ComputerNodeSpec: Codable, Equatable {
    public let id: String
    public let displayName: String
    public let role: String
    public let macAddress: String
    public let broadcastAddress: String
    public let wolPort: Int
    public let apiBaseUrl: String
    public let isLocal: Bool
    public let enabled: Bool
    public let notes: String

    public init(id: String, displayName: String, role: String, macAddress: String, broadcastAddress: String, wolPort: Int, apiBaseUrl: String, isLocal: Bool, enabled: Bool, notes: String) {
        self.id = id
        self.displayName = displayName
        self.role = role
        self.macAddress = macAddress
        self.broadcastAddress = broadcastAddress
        self.wolPort = wolPort
        self.apiBaseUrl = apiBaseUrl
        self.isLocal = isLocal
        self.enabled = enabled
        self.notes = notes
    }

    public var roleLabel: String {
        switch role.lowercased() {
        case "main": return "Main PC"
        case "dev": return "Dev PC"
        default: return "Auxiliary"
        }
    }

    /// Whether remote power actions can be dispatched to this node.
    public var supportsRemotePower: Bool {
        isLocal || !apiBaseUrl.trimmingCharacters(in: .whitespaces).isEmpty
    }
}

public struct ComputerTelemetrySpec: Codable, Equatable {
    public let isOnline: Bool
    public let hostname: String?
    public let osDescription: String?
    public let localIp: String?
    public let version: String?
    public let latencyMs: Int?
    public let cpuUsagePercent: Double?
    public let cpuTempC: Double?
    public let cpuName: String?
    public let gpuName: String?
    public let gpuTempC: Double?
    public let gpuUsagePercent: Double?
    public let ramUsagePercent: Double?
    public let ramFreeGB: Double?
    public let ramTotalGB: Double?
    public let appRamUsageMB: Double?
    public let upTime: String?
    public let lastSeenUtc: String?
    public let error: String?
}

public struct ComputerSpec: Codable, Equatable, Identifiable {
    public let id: String
    public let displayName: String
    public let role: String
    public let macAddress: String
    public let broadcastAddress: String
    public let wolPort: Int
    public let apiBaseUrl: String
    public let isLocal: Bool
    public let enabled: Bool
    public let notes: String
    public let telemetry: ComputerTelemetrySpec
    public let availableActions: [String]

    public init(id: String, displayName: String, role: String, macAddress: String,
                broadcastAddress: String, wolPort: Int, apiBaseUrl: String,
                isLocal: Bool, enabled: Bool, notes: String,
                telemetry: ComputerTelemetrySpec, availableActions: [String]) {
        self.id = id
        self.displayName = displayName
        self.role = role
        self.macAddress = macAddress
        self.broadcastAddress = broadcastAddress
        self.wolPort = wolPort
        self.apiBaseUrl = apiBaseUrl
        self.isLocal = isLocal
        self.enabled = enabled
        self.notes = notes
        self.telemetry = telemetry
        self.availableActions = availableActions
    }

    /// Decoding is deliberately lenient.
    ///
    /// Every property here is non-optional, so a single field missing from the
    /// server payload used to throw `keyNotFound` and wipe out the *entire* PCs
    /// list ("The data couldn't be read because it is missing"). The desktop app
    /// adds and renames fields over time, and an older desktop paired with a newer
    /// phone would drop the whole fleet on a mismatch. Missing values now fall back
    /// to a neutral default so one absent field cannot hide every computer.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)

        func value<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T {
            ((try? c.decodeIfPresent(T.self, forKey: key)) ?? nil) ?? fallback
        }

        id = value(.id, "")
        displayName = value(.displayName, "PC")
        role = value(.role, "client")
        macAddress = value(.macAddress, "")
        broadcastAddress = value(.broadcastAddress, "255.255.255.255")
        wolPort = value(.wolPort, 9)
        apiBaseUrl = value(.apiBaseUrl, "")
        isLocal = value(.isLocal, false)
        enabled = value(.enabled, true)
        notes = value(.notes, "")

        // ComputerTelemetrySpec has no zero-argument init (isOnline is non-optional),
        // so build the "no data yet" value explicitly.
        if let decoded = (try? c.decodeIfPresent(ComputerTelemetrySpec.self, forKey: .telemetry)) ?? nil {
            telemetry = decoded
        } else {
            telemetry = ComputerTelemetrySpec(
                isOnline: false, hostname: nil, osDescription: nil, localIp: nil, version: nil,
                latencyMs: nil, cpuUsagePercent: nil, cpuTempC: nil, cpuName: nil, gpuName: nil,
                gpuTempC: nil, gpuUsagePercent: nil, ramUsagePercent: nil, ramFreeGB: nil,
                ramTotalGB: nil, appRamUsageMB: nil, upTime: nil, lastSeenUtc: nil, error: nil)
        }

        availableActions = value(.availableActions, [String]())
    }

    public var node: ComputerNodeSpec {
        ComputerNodeSpec(id: id, displayName: displayName, role: role, macAddress: macAddress,
                         broadcastAddress: broadcastAddress, wolPort: wolPort, apiBaseUrl: apiBaseUrl,
                         isLocal: isLocal, enabled: enabled, notes: notes)
    }

    public var roleLabel: String { node.roleLabel }
    public var supportsRemotePower: Bool { node.supportsRemotePower }
    public var isOnline: Bool { telemetry.isOnline ?? false }

    /// "Online" / "Offline" plus a short reason when we know it.
    public var statusSummary: String {
        if isOnline { return "Online" }
        if let err = telemetry.error, !err.isEmpty { return err }
        return "Offline"
    }
}

public struct ComputerFleetResponse: Codable {
    public let success: Bool?
    public let computers: [ComputerSpec]?
}

public struct ComputerPowerResponse: Codable {
    public let success: Bool
    public let message: String?
    public let action: String?
    public let targetId: String?
}

public struct AppUserProfile: Codable, Equatable {
    public var id: Int
    public var username: String
    public var email: String?
    public var role: String
    public var fullName: String?
    public var avatarUrl: String?

    public init(id: Int, username: String, email: String? = nil, role: String = "User", fullName: String? = nil, avatarUrl: String? = nil) {
        self.id = id
        self.username = username
        self.email = email
        self.role = role
        self.fullName = fullName
        self.avatarUrl = avatarUrl
    }
}

public struct UserLoginResponse: Codable {
    public let success: Bool
    public let token: String?
    public let message: String?
    public let user: AppUserProfile?
}

public struct RouteLatencyInfo: Identifiable, Equatable {
    public let id: String
    public let name: String
    public let routeType: String
    public let url: String
    public let latencyMs: Int?
    public let isReachable: Bool
    public let isCurrent: Bool

    public init(name: String, routeType: String, url: String, latencyMs: Int?, isReachable: Bool, isCurrent: Bool) {
        self.id = "\(routeType)-\(url)"
        self.name = name
        self.routeType = routeType
        self.url = url
        self.latencyMs = latencyMs
        self.isReachable = isReachable
        self.isCurrent = isCurrent
    }
}
