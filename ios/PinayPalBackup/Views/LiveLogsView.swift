import SwiftUI

public struct LiveLogsView: View {
    @ObservedObject var api: PinayPalAPIService
    @Binding var showSettingsSheet: Bool

    @State private var searchText: String = ""
    @State private var selectedLevel: LogLevel = .all
    @State private var autoScroll: Bool = true
    @State private var isRefreshing: Bool = false
    @State private var showShareSheet: Bool = false
    @State private var logFileUrl: URL? = nil

    public enum LogLevel: String, CaseIterable, Identifiable {
        case all = "All"
        case error = "Errors"
        case warning = "Warnings"
        case info = "Info"

        public var id: String { rawValue }
    }

    public var filteredLogs: [String] {
        api.logs.filter { line in
            let matchesSearch = searchText.isEmpty || line.localizedCaseInsensitiveContains(searchText)
            if !matchesSearch { return false }

            switch selectedLevel {
            case .all:
                return true
            case .error:
                return line.localizedCaseInsensitiveContains("error") || line.localizedCaseInsensitiveContains("fail")
            case .warning:
                return line.localizedCaseInsensitiveContains("warn")
            case .info:
                return line.localizedCaseInsensitiveContains("info")
            }
        }
    }

    public var body: some View {
        ZStack {
            LiquidTheme.backgroundDark.ignoresSafeArea()

            VStack(spacing: 12) {
                // Top Header Bar
                logsHeader
                    .padding(.horizontal, 16)
                    .padding(.top, 10)

                // Search Bar
                searchBar
                    .padding(.horizontal, 16)

                // Level Filter Pills
                filterPills
                    .padding(.horizontal, 16)

                // Terminal Console Window
                terminalWindow
                    .padding(.horizontal, 16)

                Spacer().frame(height: 70)
            }
        }
        .sheet(isPresented: $showShareSheet) {
            if let url = logFileUrl {
                ShareSheet(items: [url])
            }
        }
    }

    // MARK: - Header Bar
    private var logsHeader: some View {
        HStack {
            HStack(spacing: 8) {
                Image(systemName: "terminal.fill")
                    .foregroundColor(LiquidTheme.gold)
                    .font(.system(size: 20))

                Text("Live Console")
                    .font(.system(size: 22, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.gold)

                Text("\(filteredLogs.count)")
                    .font(.system(size: 11, weight: .bold))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Color.white.opacity(0.1))
                    .cornerRadius(10)
                    .foregroundColor(LiquidTheme.textSecondary)
            }

            Spacer()

            HStack(spacing: 10) {
                // Auto-scroll toggle
                Button {
                    autoScroll.toggle()
                    let haptic = UIImpactFeedbackGenerator(style: .light)
                    haptic.impactOccurred()
                } label: {
                    Image(systemName: autoScroll ? "arrow.down.to.line.circle.fill" : "arrow.down.to.line.circle")
                        .font(.system(size: 16))
                        .foregroundColor(autoScroll ? LiquidTheme.gold : LiquidTheme.textSecondary)
                }

                // Share / Export Button
                Button {
                    prepareLogFileAndShare()
                } label: {
                    Image(systemName: "square.and.arrow.up")
                        .font(.system(size: 15, weight: .bold))
                        .foregroundColor(LiquidTheme.gold)
                        .padding(7)
                        .background(Circle().fill(Color.white.opacity(0.08)))
                }

                // Refresh Button
                Button {
                    Task { await refreshLogs() }
                } label: {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(LiquidTheme.gold)
                        .padding(7)
                        .background(Circle().fill(Color.white.opacity(0.08)))
                        .rotationEffect(.degrees(isRefreshing ? 360 : 0))
                        .animation(isRefreshing ? Animation.linear(duration: 1).repeatForever(autoreverses: false) : .default, value: isRefreshing)
                }
            }
        }
    }

    // MARK: - Search Bar
    private var searchBar: some View {
        HStack(spacing: 8) {
            Image(systemName: "magnifyingglass")
                .foregroundColor(LiquidTheme.textSecondary)
                .font(.system(size: 14))

            TextField("Filter log entries (e.g. ftp, error, auth)...", text: $searchText)
                .font(.system(size: 13))
                .foregroundColor(.white)
                .autocapitalization(.none)
                .disableAutocorrection(true)

            if !searchText.isEmpty {
                Button {
                    searchText = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundColor(LiquidTheme.textSecondary)
                        .font(.system(size: 14))
                }
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(Color.white.opacity(0.05))
        .cornerRadius(10)
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.white.opacity(0.1), lineWidth: 1))
    }

    // MARK: - Filter Pills
    private var filterPills: some View {
        HStack(spacing: 8) {
            ForEach(LogLevel.allCases) { level in
                let isSelected = selectedLevel == level
                Button {
                    withAnimation(.spring(response: 0.3, dampingFraction: 0.7)) {
                        selectedLevel = level
                    }
                    let haptic = UIImpactFeedbackGenerator(style: .light)
                    haptic.impactOccurred()
                } label: {
                    Text(level.rawValue)
                        .font(.system(size: 11, weight: isSelected ? .bold : .medium))
                        .foregroundColor(isSelected ? .black : LiquidTheme.textSecondary)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 5)
                        .background {
                            if isSelected {
                                Capsule()
                                    .fill(LiquidTheme.gold)
                            } else {
                                Capsule()
                                    .fill(Color.white.opacity(0.06))
                            }
                        }
                }
            }
            Spacer()
        }
    }

    // MARK: - Terminal Window
    private var terminalWindow: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 4) {
                    if filteredLogs.isEmpty {
                        Text("No logs available matching filter.")
                            .font(.system(size: 12, weight: .medium, design: .monospaced))
                            .foregroundColor(LiquidTheme.textSecondary)
                            .padding(20)
                    } else {
                        ForEach(Array(filteredLogs.enumerated()), id: \.offset) { index, line in
                            logLineView(line: line)
                                .id(index)
                        }
                    }
                }
                .padding(12)
            }
            .background(Color(red: 11/255, green: 14/255, blue: 20/255))
            .cornerRadius(12)
            .overlay(
                RoundedRectangle(cornerRadius: 12)
                    .strokeBorder(LinearGradient(
                        colors: [Color.white.opacity(0.15), Color.white.opacity(0.05)],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing
                    ), lineWidth: 1)
            )
            .onChange(of: filteredLogs.count) { _ in
                if autoScroll, let last = filteredLogs.indices.last {
                    withAnimation {
                        proxy.scrollTo(last, anchor: .bottom)
                    }
                }
            }
            .refreshable {
                await refreshLogs()
            }
        }
    }

    // MARK: - Log Line View
    private func logLineView(line: String) -> some View {
        let (color, _) = parseLineColor(line)

        return HStack(alignment: .top, spacing: 6) {
            Circle()
                .fill(color)
                .frame(width: 6, height: 6)
                .padding(.top, 5)

            Text(line)
                .font(.system(size: 11, weight: .regular, design: .monospaced))
                .foregroundColor(color)
                .lineSpacing(3)
                .textSelection(.enabled)
        }
    }

    private func parseLineColor(_ line: String) -> (Color, String) {
        let lower = line.lowercased()
        if lower.contains("[error]") || lower.contains("fail") || lower.contains("exception") {
            return (LiquidTheme.coral, "ERR")
        }
        if lower.contains("[warn") || lower.contains("warning") {
            return (LiquidTheme.gold, "WRN")
        }
        if lower.contains("[success]") || lower.contains("completed") {
            return (LiquidTheme.emerald, "SUC")
        }
        return (LiquidTheme.cyan.opacity(0.9), "INF")
    }

    // MARK: - Actions
    private func refreshLogs() async {
        isRefreshing = true
        let haptic = UIImpactFeedbackGenerator(style: .medium)
        haptic.impactOccurred()
        await api.fetchLogs()
        isRefreshing = false
    }

    private func prepareLogFileAndShare() {
        let content = api.logs.joined(separator: "\n")
        let filename = "PinayPal_Logs_\(Int(Date().timeIntervalSince1970)).txt"
        let tempUrl = FileManager.default.temporaryDirectory.appendingPathComponent(filename)

        do {
            try content.write(to: tempUrl, atomically: true, encoding: .utf8)
            self.logFileUrl = tempUrl
            self.showShareSheet = true
        } catch { }
    }
}

// MARK: - ShareSheet Helper
public struct ShareSheet: UIViewControllerRepresentable {
    public let items: [Any]

    public func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: items, applicationActivities: nil)
    }

    public func updateUIViewController(_ uiViewController: UIActivityViewController, context: Context) { }
}
