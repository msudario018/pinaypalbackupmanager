import SwiftUI

public struct BackupHistoryView: View {
    @ObservedObject var api: PinayPalAPIService
    @Binding var showSettingsSheet: Bool

    @State private var selectedFilter: HistoryFilter = .all
    @State private var selectedItem: BackupHistoryItem? = nil
    @State private var isRefreshing: Bool = false
    @State private var searchText = ""
    @State private var selectedSort: HistorySort = .recent
    @State private var showClearConfirmation = false
    @State private var isDownloading = false
    @State private var shareFile: ShareableFile?

    public enum HistoryFilter: String, CaseIterable, Identifiable {
        case all = "All"
        case ftp = "FTP"
        case sql = "SQL"
        case mailchimp = "Mailchimp"
        case failed = "Failed"

        public var id: String { rawValue }
    }

    public enum HistorySort: String, CaseIterable, Identifiable {
        case recent = "Most Recent"
        case size = "Largest Size"
        case duration = "Longest Duration"
        case failures = "Failures First"
        public var id: String { rawValue }
    }

    public var filteredHistory: [BackupHistoryItem] {
        let matching = api.history.filter { item in
            switch selectedFilter {
            case .all:
                return true
            case .ftp:
                return item.service.localizedCaseInsensitiveContains("ftp")
            case .sql:
                return item.service.localizedCaseInsensitiveContains("sql")
            case .mailchimp:
                return item.service.localizedCaseInsensitiveContains("mailchimp")
            case .failed:
                return item.status.localizedCaseInsensitiveContains("fail") || item.status.localizedCaseInsensitiveContains("error")
            }
        }
        .filter { item in
            guard !searchText.isEmpty else { return true }
            let haystack = [item.service, item.status, item.type ?? "", item.filename ?? "", item.time].joined(separator: " ")
            return haystack.localizedCaseInsensitiveContains(searchText)
        }

        switch selectedSort {
        case .recent:
            return matching
        case .size:
            return matching.sorted { ($0.sizeBytes ?? 0) > ($1.sizeBytes ?? 0) }
        case .duration:
            return matching.sorted { ($0.durationSeconds ?? 0) > ($1.durationSeconds ?? 0) }
        case .failures:
            return matching.sorted { left, right in
                let leftFailed = !left.status.localizedCaseInsensitiveContains("success")
                let rightFailed = !right.status.localizedCaseInsensitiveContains("success")
                return leftFailed && !rightFailed
            }
        }
    }

    public var body: some View {
        ZStack {
            LiquidTheme.backgroundDark.ignoresSafeArea()

            RadialGradient(
                colors: [LiquidTheme.gold.opacity(0.08), Color.clear],
                center: .topLeading,
                startRadius: 20,
                endRadius: 350
            )
            .ignoresSafeArea()

            ScrollView {
                VStack(spacing: 16) {
                    // Summary Stats Strip
                    statsStrip

                    historyControls

                    // Filter Chips Bar
                    filterBar

                    // Snapshot List
                    if filteredHistory.isEmpty {
                        emptyStateView
                    } else {
                        LazyVStack(spacing: 12) {
                            ForEach(filteredHistory) { item in
                                snapshotCard(item: item)
                                    .onTapGesture {
                                        selectedItem = item
                                        let haptic = UIImpactFeedbackGenerator(style: .light)
                                        haptic.impactOccurred()
                                    }
                            }
                        }
                    }

                    Spacer().frame(height: 100)
                }
                .padding(.horizontal, 16)
                .padding(.top, 68)
            }
            .refreshable {
                await refreshData()
            }
        }
        .sheet(item: $selectedItem) { item in
            snapshotDetailSheet(item: item)
        }
        .sheet(item: $shareFile) { file in
            ShareSheet(items: [file.url])
        }
        .confirmationDialog("Clear all local backup history?", isPresented: $showClearConfirmation, titleVisibility: .visible) {
            Button("Clear History", role: .destructive) {
                Task { _ = await api.clearHistory() }
            }
        } message: {
            Text("This removes the recorded history from the PC dashboard. Backup files are not deleted.")
        }
    }

    // MARK: - Header
    private var historyHeader: some View {
        HStack {
            HStack(spacing: 8) {
                Image(systemName: "clock.arrow.circlepath")
                    .foregroundColor(LiquidTheme.gold)
                    .font(.system(size: 20))

                Text("Snapshots")
                    .font(.system(size: 22, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.gold)

                Text("\(api.history.count)")
                    .font(.system(size: 11, weight: .bold))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color.white.opacity(0.1))
                    .cornerRadius(10)
                    .foregroundColor(LiquidTheme.textSecondary)
            }

            Spacer()

            Button {
                Task { await refreshData() }
            } label: {
                Image(systemName: "arrow.clockwise")
                    .font(.system(size: 14, weight: .bold))
                    .foregroundColor(LiquidTheme.gold)
                    .padding(8)
                    .background(Circle().fill(Color.white.opacity(0.08)))
                    .rotationEffect(.degrees(isRefreshing ? 360 : 0))
                    .animation(isRefreshing ? Animation.linear(duration: 1).repeatForever(autoreverses: false) : .default, value: isRefreshing)
            }
        }
    }

    // MARK: - Stats Strip
    private var statsStrip: some View {
        HStack(spacing: 10) {
            let total = api.history.count
            let successCount = api.history.filter { $0.status.localizedCaseInsensitiveContains("success") }.count
            let rate = total > 0 ? Int(Double(successCount) / Double(total) * 100) : 100

            statBox(title: "TOTAL RUNS", value: "\(total)", icon: "archivebox.fill", color: LiquidTheme.cyan)
            statBox(title: "SUCCESS RATE", value: "\(rate)%", icon: "checkmark.shield.fill", color: LiquidTheme.emerald)
            statBox(title: "FAILED", value: "\(total - successCount)", icon: "exclamationmark.triangle.fill", color: (total - successCount) > 0 ? LiquidTheme.coral : LiquidTheme.textSecondary)
        }
    }

    private func statBox(title: String, value: String, icon: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 5) {
                Image(systemName: icon)
                    .font(.system(size: 10))
                    .foregroundColor(color)
                Text(title)
                    .font(.system(size: 9, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary)
            }
            Text(value)
                .font(.system(size: 18, weight: .black, design: .rounded))
                .foregroundColor(.white)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .liquidGlassCard(cornerRadius: 12)
    }

    // MARK: - Filter Bar
    private var filterBar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(HistoryFilter.allCases) { filter in
                    let isSelected = selectedFilter == filter
                    Button {
                        withAnimation(.spring(response: 0.3, dampingFraction: 0.7)) {
                            selectedFilter = filter
                        }
                        let haptic = UIImpactFeedbackGenerator(style: .light)
                        haptic.impactOccurred()
                    } label: {
                        Text(filter.rawValue)
                            .font(.system(size: 12, weight: isSelected ? .bold : .medium))
                            .foregroundColor(isSelected ? .black : LiquidTheme.textSecondary)
                            .padding(.horizontal, 14)
                            .padding(.vertical, 7)
                            .background {
                                if isSelected {
                                    Capsule(style: .continuous)
                                        .fill(LiquidTheme.gold)
                                        .shadow(color: LiquidTheme.gold.opacity(0.3), radius: 6, y: 2)
                                } else {
                                    Capsule(style: .continuous)
                                        .fill(Color.white.opacity(0.06))
                                }
                            }
                    }
                }
            }
            .padding(.vertical, 4)
        }
    }

    private var historyControls: some View {
        VStack(spacing: 10) {
            HStack(spacing: 8) {
                Image(systemName: "magnifyingglass").foregroundColor(LiquidTheme.textSecondary)
                TextField("Search service, file, or result", text: $searchText)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .font(.system(size: 13))
                if !searchText.isEmpty {
                    Button { searchText = "" } label: { Image(systemName: "xmark.circle.fill").foregroundColor(LiquidTheme.textSecondary) }
                }
            }
            .padding(.horizontal, 12).padding(.vertical, 10)
            .background(Color.white.opacity(0.07), in: RoundedRectangle(cornerRadius: 12, style: .continuous))

            HStack(spacing: 10) {
                Menu {
                    Picker("Sort", selection: $selectedSort) {
                        ForEach(HistorySort.allCases) { option in Text(option.rawValue).tag(option) }
                    }
                } label: {
                    Label(selectedSort.rawValue, systemImage: "arrow.up.arrow.down.circle.fill")
                        .font(.caption.weight(.semibold)).frame(maxWidth: .infinity, minHeight: 38)
                }
                .buttonStyle(.bordered).tint(LiquidTheme.blue)

                Button {
                    let rows = filteredHistory.map { "\($0.time),\($0.service),\($0.status),\($0.sizeBytes ?? 0),\($0.durationSeconds ?? 0)" }
                    let url = FileManager.default.temporaryDirectory.appendingPathComponent("PinayPal-History.csv")
                    try? (["Time,Service,Status,SizeBytes,DurationSeconds"] + rows).joined(separator: "\n").write(to: url, atomically: true, encoding: .utf8)
                    shareFile = ShareableFile(url: url)
                } label: {
                    Label("Export", systemImage: "square.and.arrow.up")
                        .font(.caption.weight(.semibold)).frame(maxWidth: .infinity, minHeight: 38)
                }
                .buttonStyle(.bordered).tint(LiquidTheme.emerald)

                Button(role: .destructive) { showClearConfirmation = true } label: {
                    Image(systemName: "trash").frame(minWidth: 38, minHeight: 38)
                }
                .buttonStyle(.bordered)
            }
        }
    }

    // MARK: - Snapshot Card
    private func snapshotCard(item: BackupHistoryItem) -> some View {
        let isSuccess = item.status.localizedCaseInsensitiveContains("success")
        let serviceColor = getServiceColor(item.service)

        return HStack(spacing: 12) {
            // Service Icon with glow
            ZStack {
                Circle()
                    .fill(serviceColor.opacity(0.15))
                    .frame(width: 44, height: 44)

                Image(systemName: getServiceIcon(item.service))
                    .foregroundColor(serviceColor)
                    .font(.system(size: 18))
            }

            // Info
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 6) {
                    Text(item.service.uppercased())
                        .font(.system(size: 13, weight: .heavy, design: .rounded))
                        .foregroundColor(.white)

                    if let type = item.type, !type.isEmpty {
                        Text(type)
                            .font(.system(size: 9, weight: .bold))
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(Color.white.opacity(0.08))
                            .cornerRadius(4)
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                }

                HStack(spacing: 8) {
                    Text(formatTimestamp(item.time))
                        .font(.system(size: 11))
                        .foregroundColor(LiquidTheme.textSecondary)

                    if let bytes = item.sizeBytes, bytes > 0 {
                        Text("•")
                            .font(.system(size: 9))
                            .foregroundColor(LiquidTheme.textSecondary)
                        Text(formatBytes(bytes))
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(LiquidTheme.gold)
                    }

                    if let sec = item.durationSeconds, sec > 0 {
                        Text("•")
                            .font(.system(size: 9))
                            .foregroundColor(LiquidTheme.textSecondary)
                        Text(String(format: "%.1fs", sec))
                            .font(.system(size: 11))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                }
            }

            Spacer()

            // Status Badge
            HStack(spacing: 4) {
                Image(systemName: isSuccess ? "checkmark.circle.fill" : "xmark.circle.fill")
                    .font(.system(size: 11))
                Text(isSuccess ? "Success" : "Failed")
                    .font(.system(size: 11, weight: .bold))
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 5)
            .background((isSuccess ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.15))
            .foregroundColor(isSuccess ? LiquidTheme.emerald : LiquidTheme.coral)
            .clipShape(Capsule())
        }
        .padding(14)
        .liquidGlassCard(cornerRadius: 14)
    }

    // MARK: - Empty State
    private var emptyStateView: some View {
        VStack(spacing: 12) {
            Image(systemName: "tray")
                .font(.system(size: 36))
                .foregroundColor(LiquidTheme.textSecondary)
                .padding(.top, 40)

            Text("No Snapshots Found")
                .font(.system(size: 16, weight: .bold))
                .foregroundColor(.white)

            Text("No backup history matches the selected filter.")
                .font(.system(size: 13))
                .foregroundColor(LiquidTheme.textSecondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(24)
    }

    // MARK: - Detail Sheet
    private func snapshotDetailSheet(item: BackupHistoryItem) -> some View {
        let isSuccess = item.status.localizedCaseInsensitiveContains("success")

        return NavigationStack {
            ZStack {
                LiquidTheme.backgroundDark.ignoresSafeArea()

                ScrollView {
                    VStack(spacing: 20) {
                        // Top Header Card
                        VStack(spacing: 12) {
                            ZStack {
                                Circle()
                                    .fill((isSuccess ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.15))
                                    .frame(width: 64, height: 64)

                                Image(systemName: isSuccess ? "checkmark.shield.fill" : "exclamationmark.shield.fill")
                                    .font(.system(size: 28))
                                    .foregroundColor(isSuccess ? LiquidTheme.emerald : LiquidTheme.coral)
                            }

                            Text(item.service.uppercased() + " BACKUP")
                                .font(.system(size: 18, weight: .black, design: .rounded))
                                .foregroundColor(.white)

                            Text(isSuccess ? "Completed Successfully" : "Backup Failed")
                                .font(.system(size: 14, weight: .bold))
                                .foregroundColor(isSuccess ? LiquidTheme.emerald : LiquidTheme.coral)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(24)
                        .liquidGlassCard(cornerRadius: 16)

                        // Meta details list
                        VStack(spacing: 12) {
                            detailRow(label: "Snapshot ID", value: item.id)
                            detailRow(label: "Timestamp", value: item.time)
                            if let type = item.type {
                                detailRow(label: "Trigger Type", value: type)
                            }
                            if let bytes = item.sizeBytes {
                                detailRow(label: "Archive Size", value: formatBytes(bytes))
                            }
                            if let sec = item.durationSeconds {
                                detailRow(label: "Duration", value: String(format: "%.2f seconds", sec))
                            }
                            if let fn = item.filename, !fn.isEmpty {
                                detailRow(label: "Destination File", value: fn)
                            }
                        }
                        .padding(16)
                        .liquidGlassCard(cornerRadius: 16)

                        // Quick trigger button
                        Button {
                            Task {
                                _ = await api.triggerBackup(service: item.service.lowercased())
                                selectedItem = nil
                            }
                        } label: {
                            HStack(spacing: 8) {
                                Image(systemName: "arrow.triangle.2.circlepath")
                                Text("Rerun \(item.service.uppercased()) Backup Now")
                                    .font(.system(size: 14, weight: .bold))
                            }
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 14)
                            .background(LiquidTheme.gold)
                            .foregroundColor(.black)
                            .cornerRadius(12)
                        }
                        if item.hasFile == true, let filename = item.filename, !filename.isEmpty {
                            Button {
                                isDownloading = true
                                Task {
                                    if let url = await api.downloadBackupFile(filename: filename) {
                                        shareFile = ShareableFile(url: url)
                                    }
                                    isDownloading = false
                                }
                            } label: {
                                Label(isDownloading ? "Preparing..." : "Download backup", systemImage: "arrow.down.circle.fill")
                                    .frame(maxWidth: .infinity).padding(.vertical, 12)
                            }
                            .buttonStyle(.bordered)
                            .tint(LiquidTheme.cyan)
                            .disabled(isDownloading)
                        }
                    }
                    .padding(16)
                }
            }
            .navigationTitle("Snapshot Details")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { selectedItem = nil }
                        .foregroundColor(LiquidTheme.gold)
                }
            }
        }
    }

    private func detailRow(label: String, value: String) -> some View {
        HStack {
            Text(label)
                .font(.system(size: 13))
                .foregroundColor(LiquidTheme.textSecondary)
            Spacer()
            Text(value)
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(.white)
                .multilineTextAlignment(.trailing)
        }
    }

    // MARK: - Helpers
    private func refreshData() async {
        isRefreshing = true
        let haptic = UIImpactFeedbackGenerator(style: .medium)
        haptic.impactOccurred()
        await api.fetchHistory()
        isRefreshing = false
    }

    private func getServiceColor(_ service: String) -> Color {
        let s = service.lowercased()
        if s.contains("ftp") { return LiquidTheme.emerald }
        if s.contains("sql") { return LiquidTheme.gold }
        if s.contains("mailchimp") { return LiquidTheme.cyan }
        return LiquidTheme.blue
    }

    private func getServiceIcon(_ service: String) -> String {
        let s = service.lowercased()
        if s.contains("ftp") { return "globe" }
        if s.contains("sql") { return "cylinder.split.1x2.fill" }
        if s.contains("mailchimp") { return "envelope.fill" }
        return "externaldrive.fill"
    }

    private func formatTimestamp(_ iso: String) -> String {
        let clean = iso.replacingOccurrences(of: "T", with: " ")
        if clean.count >= 19 {
            return String(clean.prefix(19))
        }
        return clean
    }

    private func formatBytes(_ bytes: Int64) -> String {
        let b = Double(bytes)
        if b >= 1024 * 1024 * 1024 {
            return String(format: "%.2f GB", b / (1024 * 1024 * 1024))
        } else if b >= 1024 * 1024 {
            return String(format: "%.1f MB", b / (1024 * 1024))
        } else if b >= 1024 {
            return String(format: "%.0f KB", b / 1024)
        }
        return "\(bytes) B"
    }
}

private struct ShareableFile: Identifiable {
    let url: URL
    var id: URL { url }
}
