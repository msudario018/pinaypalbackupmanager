import SwiftUI

/// "My Computers" tab: live hardware telemetry for every managed PC, plus
/// Wake-on-LAN and remote power controls (restart, shut down, lock, sleep).
public struct ComputersView: View {
    @ObservedObject var api: PinayPalAPIService
    @Environment(\.colorScheme) private var colorScheme

    /// Read straight from the service so the tab stays live while it is open.
    private var computers: [ComputerSpec] { api.computers }
    private var isLoading: Bool { false }
    private var errorMessage: String? { api.computersError }

    @State private var busyComputerId: String? = nil
    @State private var busyAction: String? = nil

    /// Confirmation state for the destructive actions.
    @State private var pendingPower: PowerRequest?
    @State private var resultBanner: ResultBanner?

    public init(api: PinayPalAPIService) {
        self.api = api
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                header

                if let err = errorMessage {
                    errorCard(err)
                }

                if computers.isEmpty && !isLoading {
                    emptyState
                } else {
                    ForEach(computers) { pc in
                        computerCard(pc)
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 10)
            .padding(.bottom, 24)
        }
        .refreshable { await api.refreshComputers(force: true) }
        .task {
            // Poll only while this tab is actually on screen.
            api.setComputerPolling(true)
            await api.refreshComputers(force: true)
            await MainActor.run { api.setComputerPolling(false) }
        }
        .alert(item: $pendingPower) { request in
            Alert(
                title: Text(confirmTitle(for: request)),
                message: Text(confirmMessage(for: request)),
                primaryButton: .destructive(Text(confirmButtonLabel(for: request))) {
                    Task { await runAction(request.computer, request.action) }
                },
                secondaryButton: .cancel()
            )
        }
    }

    // MARK: - Header

    private var header: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text("My Computers")
                    .font(.system(size: 20, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Text("Telemetry, Wake-on-LAN and remote power.")
                    .font(.caption)
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            Spacer()
            Button {
                Task { await loadComputers(refresh: true) }
            } label: {
                Image(systemName: "arrow.clockwise")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(LiquidTheme.gold)
                    .padding(8)
                    .background(Circle().fill(LiquidTheme.gold.opacity(0.14)))
            }
            .disabled(isLoading)
        }
    }

    private var emptyState: some View {
        VStack(spacing: 10) {
            Image(systemName: "desktopcomputer")
                .font(.system(size: 34, weight: .light))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme).opacity(0.6))
            Text("No computers registered yet")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
            Text("Add your Dev PC and Main PC in the desktop app under Settings → My Computers.")
                .font(.caption)
                .multilineTextAlignment(.center)
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 36)
    }

    private func errorCard(_ err: String) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundColor(LiquidTheme.coral)
            Text(err)
                .font(.caption)
                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
            Spacer()
        }
        .padding(14)
        .background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(LiquidTheme.coral.opacity(0.10))
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(LiquidTheme.coral.opacity(0.35), lineWidth: 1))
        )
    }
    // MARK: - Data

    private func loadComputers(refresh: Bool) async {
        await api.refreshComputers(force: refresh)
    }

    private func runAction(_ pc: ComputerSpec, _ action: String) async {
        busyComputerId = pc.id
        busyAction = action

        let (ok, message) = await api.performComputerAction(computerId: pc.id, action: action)
        resultBanner = ResultBanner(text: message.replacingOccurrences(of: "**", with: ""), isError: !ok)

        busyComputerId = nil
        busyAction = nil

        if ok {
            // Give the machine a moment to actually change state, then re-read telemetry.
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            await loadComputers(refresh: true)
        }
    }

    // MARK: - Confirmation copy

    private func confirmTitle(for request: PowerRequest) -> String {
        let verb: String
        switch request.action {
        case "restart": verb = "Restart"
        case "shutdown": verb = "Shut down"
        case "lock": verb = "Lock"
        case "sleep": verb = "Sleep"
        case "wake": verb = "Wake"
        default: verb = request.action.capitalized
        }
        return "\(verb) \(request.computer.displayName)?"
    }

    private func confirmMessage(for request: PowerRequest) -> String {
        switch request.action {
        case "restart":
            return "\(request.computer.displayName) will restart in 10 seconds. Any in-progress work on that PC will be interrupted."
        case "shutdown":
            return "\(request.computer.displayName) will shut down in 10 seconds. Any in-progress work on that PC will be lost."
        case "lock":
            return "The \(request.computer.displayName) screen will be locked. You'll need the password to get back in."
        case "sleep":
            return "\(request.computer.displayName) will go to sleep. Wake it again with the Wake button."
        case "wake":
            return "Send a Wake-on-LAN packet to \(request.computer.displayName)?"
        default:
            return "Run this action on \(request.computer.displayName)?"
        }
    }

    private func confirmButtonLabel(for request: PowerRequest) -> String {
        switch request.action {
        case "restart": return "Restart"
        case "shutdown": return "Shut Down"
        case "lock": return "Lock"
        case "sleep": return "Sleep"
        case "wake": return "Send Signal"
        default: return "Confirm"
        }
    }
    // MARK: - Computer card

    private func computerCard(_ pc: ComputerSpec) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            cardHeader(pc)

            if pc.isOnline {
                telemetryGrid(pc)

                // Trend charts only mean anything with real history behind them, and only
                // for machines that are actually reachable.
                if pc.telemetry.isOnline, let points = api.computerHistory[pc.id], !points.isEmpty {
                    sparklineRow(points: points)
                }
            } else {
                offlineNotice(pc)
            }

            if let banner = resultBanner, busyComputerId == nil {
                resultNotice(banner)
            }

            actionGrid(pc)
        }
        .padding(16)
        .background(
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .fill(LiquidTheme.card(for: colorScheme))
                .overlay(
                    RoundedRectangle(cornerRadius: 20, style: .continuous)
                        .strokeBorder(pc.isOnline ? LiquidTheme.gold.opacity(0.28) : LiquidTheme.textSecondary(for: colorScheme).opacity(0.18), lineWidth: 1)
                )
        )
    }

    private func cardHeader(_ pc: ComputerSpec) -> some View {
        HStack(spacing: 10) {
            ZStack {
                Circle()
                    .fill((pc.isOnline ? LiquidTheme.emerald : LiquidTheme.textSecondary(for: colorScheme)).opacity(0.16))
                    .frame(width: 38, height: 38)
                Image(systemName: pc.roleLabel == "Dev PC" ? "hammer.fill" : "desktopcomputer")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(pc.isOnline ? LiquidTheme.emerald : LiquidTheme.textSecondary(for: colorScheme))
            }

            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(pc.displayName)
                        .font(.system(size: 15, weight: .black))
                        .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    if pc.isLocal {
                        Text("THIS PC")
                            .font(.system(size: 8, weight: .black))
                            .foregroundColor(LiquidTheme.gold)
                            .padding(.horizontal, 5).padding(.vertical, 2)
                            .background(Capsule().fill(LiquidTheme.gold.opacity(0.16)))
                    }
                }
                Text(pc.isOnline
                     ? "\(pc.roleLabel) · \(pc.telemetry.hostname ?? "—") · \(pc.telemetry.localIp ?? "no IP")"
                     : "\(pc.roleLabel) · \(pc.statusSummary)")
                    .font(.caption2)
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    .lineLimit(2)
            }

            Spacer()

            Circle()
                .fill(pc.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
                .frame(width: 9, height: 9)
                .shadow(color: (pc.isOnline ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.6), radius: 4)
        }
    }
    private func telemetryGrid(_ pc: ComputerSpec) -> some View {
        let t = pc.telemetry
        return LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 10), count: 3), spacing: 10) {
            telemetryTile("CPU", t.cpuUsagePercent.map { "\(Int($0))%" } ?? "—",
                          sub: t.cpuName ?? "load", color: LiquidTheme.gold)
            telemetryTile("RAM", t.ramUsagePercent.map { "\(Int($0))%" } ?? "—",
                          sub: t.ramFreeGB.map { String(format: "%.1f GB free", $0) } ?? "usage", color: LiquidTheme.blue)
            telemetryTile("CPU Temp", t.cpuTempC.map { String(format: "%.0f°C", $0) } ?? "—",
                          sub: "sensor", color: tempColor(t.cpuTempC))
            telemetryTile("GPU", t.gpuUsagePercent.map { "\(Int($0))%" } ?? "—",
                          sub: t.gpuName ?? "load", color: LiquidTheme.purple)
            telemetryTile("GPU Temp", t.gpuTempC.map { String(format: "%.0f°C", $0) } ?? "—",
                          sub: "sensor", color: tempColor(t.gpuTempC))
            telemetryTile("Uptime", t.upTime ?? "—",
                          sub: t.latencyMs.map { "\($0) ms" } ?? "running", color: LiquidTheme.cyan)
        }
    }

    private func sparklineRow(points: [PinayPalAPIService.TelemetryPoint]) -> some View {
        HStack(alignment: .top, spacing: 12) {
            FleetSparkline(points: points, metric: .cpu)
            FleetSparkline(points: points, metric: .ram)
            Spacer(minLength: 0)
        }
    }

    private func tempColor(_ value: Double?) -> Color {
        guard let v = value else { return LiquidTheme.textSecondary(for: colorScheme) }
        if v >= 85 { return LiquidTheme.coral }
        if v >= 70 { return LiquidTheme.gold }
        return LiquidTheme.emerald
    }

    private func telemetryTile(_ title: String, _ value: String, sub: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(title.uppercased())
                .font(.system(size: 9, weight: .bold))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            Text(value)
                .font(.system(size: 15, weight: .black, design: .rounded))
                .foregroundColor(color)
                .lineLimit(1).minimumScaleFactor(0.7)
            Text(sub)
                .font(.system(size: 8.5))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                .lineLimit(1).minimumScaleFactor(0.7)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(10)
        .background(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(LiquidTheme.textPrimary(for: colorScheme).opacity(0.05))
        )
    }

    private func offlineNotice(_ pc: ComputerSpec) -> some View {
        HStack(spacing: 8) {
            Image(systemName: "bolt.horizontal.circle")
                .foregroundColor(LiquidTheme.gold)
            VStack(alignment: .leading, spacing: 2) {
                Text("This computer is offline")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                Text(pc.telemetry.error ?? "Send a Wake-on-LAN signal to bring it back.")
                    .font(.caption2)
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            Spacer()
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(LiquidTheme.gold.opacity(0.08)))
    }

    private func resultNotice(_ banner: ResultBanner) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: banner.isError ? "xmark.octagon.fill" : "checkmark.circle.fill")
                .foregroundColor(banner.isError ? LiquidTheme.coral : LiquidTheme.emerald)
            Text(banner.text)
                .font(.caption2)
                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
            Spacer()
        }
        .padding(10)
        .background(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill((banner.isError ? LiquidTheme.coral : LiquidTheme.emerald).opacity(0.10))
        )
    }
    // MARK: - Actions

    private func actionGrid(_ pc: ComputerSpec) -> some View {
        VStack(spacing: 8) {
            // Wake is the hero action, especially when the machine is asleep.
            Button {
                pendingPower = PowerRequest(computer: pc, action: "wake")
            } label: {
                actionLabel(pc.isOnline ? "Restart" : "Wake on LAN",
                            icon: pc.isOnline ? "arrow.clockwise" : "power",
                            tint: pc.isOnline ? LiquidTheme.blue : LiquidTheme.gold,
                            busy: isBusy(pc, "wake"))
            }
            .disabled(isBusy(pc, "wake"))

            if pc.supportsRemotePower {
                HStack(spacing: 8) {
                    smallAction(pc, "restart", "Restart", "arrow.clockwise", LiquidTheme.blue)
                    smallAction(pc, "lock", "Lock", "lock.fill", LiquidTheme.purple)
                    smallAction(pc, "sleep", "Sleep", "moon.zzz.fill", LiquidTheme.cyan)
                    smallAction(pc, "shutdown", "Shut Down", "power", LiquidTheme.coral)
                }
            } else {
                Text("Add this PC's dashboard URL on the desktop app to enable remote restart and shutdown.")
                    .font(.system(size: 9.5))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
        }
    }

    private func smallAction(_ pc: ComputerSpec, _ action: String, _ label: String, _ icon: String, _ tint: Color) -> some View {
        Button {
            pendingPower = PowerRequest(computer: pc, action: action)
        } label: {
            VStack(spacing: 4) {
                Image(systemName: icon)
                    .font(.system(size: 13, weight: .bold))
                Text(label)
                    .font(.system(size: 9, weight: .semibold))
                    .lineLimit(1).minimumScaleFactor(0.8)
            }
            .foregroundColor(isBusy(pc, action) ? LiquidTheme.textSecondary(for: colorScheme) : tint)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 9)
            .background(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(tint.opacity(0.12))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(tint.opacity(0.25), lineWidth: 1))
            )
        }
        .disabled(isBusy(pc, action))
    }

    private func actionLabel(_ label: String, icon: String, tint: Color, busy: Bool) -> some View {
        HStack(spacing: 8) {
            if busy {
                ProgressView().controlSize(.mini).tint(tint)
            } else {
                Image(systemName: icon).font(.system(size: 12, weight: .bold))
            }
            Text(busy ? "Working..." : label)
                .font(.system(size: 12, weight: .bold))
        }
        .foregroundColor(tint)
        .frame(maxWidth: .infinity)
        .padding(.vertical, 11)
        .background(
            RoundedRectangle(cornerRadius: 13, style: .continuous)
                .fill(tint.opacity(0.14))
                .overlay(RoundedRectangle(cornerRadius: 13, style: .continuous).strokeBorder(tint.opacity(0.32), lineWidth: 1))
        )
    }

    private func isBusy(_ pc: ComputerSpec, _ action: String) -> Bool {
        busyComputerId == pc.id && busyAction == action
    }
}

/// Result of the last power action, shown as an inline banner.
///
/// This is a struct rather than a tuple because Swift does not allow tuple types
/// to be used as generic arguments, so `@State` cannot hold a tuple.
struct ResultBanner {
    let text: String
    let isError: Bool
}

/// Identifiable wrapper so `alert(item:)` can present a power confirmation.
struct PowerRequest: Identifiable {
    let computer: ComputerSpec
    let action: String
    var id: String { "\(computer.id)-\(action)" }
}