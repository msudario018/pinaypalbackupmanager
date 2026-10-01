import SwiftUI

public struct LiquidAIAssistantSheet: View {
    @Environment(\.colorScheme) var colorScheme
    @Environment(\.dismiss) var dismiss
    @ObservedObject var api: PinayPalAPIService

    @State private var messages: [AIChatMessage] = [
        AIChatMessage(
            role: "assistant",
            content: "Hello! I am your **PinayPal AI Assistant**.\n\nI monitor your PC host daemon, backup health, and storage in real time. All interactions are strictly guarded behind a **Zero-Leak Sanitizer** so your passwords and database dumps are never exposed.\n\nHow can I assist you today?"
        )
    ]
    @State private var inputText: String = ""
    @State private var isProcessing: Bool = false
    @State private var actionExecutionStatus: [String: String] = [:]

    private let quickPrompts = [
        "System Health",
        "Check Storage",
        "Run FTP Backup",
        "Tunnel Status",
        "Test Email Alert"
    ]

    public init(api: PinayPalAPIService) {
        self.api = api
    }

    public var body: some View {
        NavigationView {
            ZStack {
                LiquidTheme.background(for: colorScheme).ignoresSafeArea()

                VStack(spacing: 0) {
                    // Header Status Strip
                    headerStatusStrip

                    // Chat Messages Scroll
                    ScrollViewReader { proxy in
                        ScrollView {
                            LazyVStack(spacing: 14) {
                                ForEach(messages) { message in
                                    chatBubble(for: message)
                                        .id(message.id)
                                }

                                if isProcessing {
                                    typingIndicatorBubble
                                        .id("typing")
                                }
                            }
                            .padding(.horizontal, 16)
                            .padding(.vertical, 16)
                        }
                        .onChange(of: messages.count) { _ in
                            if let lastId = messages.last?.id {
                                withAnimation(.spring()) {
                                    proxy.scrollTo(lastId, anchor: .bottom)
                                }
                            }
                        }
                        .onChange(of: isProcessing) { processing in
                            if processing {
                                withAnimation(.spring()) {
                                    proxy.scrollTo("typing", anchor: .bottom)
                                }
                            }
                        }
                    }

                    // Quick Prompt Chips
                    quickChipsRow

                    // Input Bar
                    inputBar
                }
            }
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .principal) {
                    HStack(spacing: 8) {
                        Image(systemName: "sparkles")
                            .font(.system(size: 15, weight: .bold))
                            .foregroundColor(LiquidTheme.gold)

                        Text("PinayPal AI")
                            .font(.system(size: 16, weight: .bold))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

                        Text("HYBRID")
                            .font(.system(size: 9, weight: .black))
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .background(LiquidTheme.gold.opacity(0.18))
                            .foregroundColor(LiquidTheme.gold)
                            .clipShape(Capsule())
                    }
                }

                ToolbarItem(placement: .navigationBarLeading) {
                    Button(action: {
                        clearHistory()
                    }) {
                        Image(systemName: "trash")
                            .font(.system(size: 14, weight: .medium))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                }

                ToolbarItem(placement: .navigationBarTrailing) {
                    Button(action: {
                        dismiss()
                    }) {
                        Image(systemName: "xmark.circle.fill")
                            .font(.system(size: 20))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme).opacity(0.7))
                    }
                }
            }
        }
    }

    // MARK: - Header Status Strip

    private var headerStatusStrip: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
                .frame(width: 8, height: 8)

            Text(api.isOnline ? "Connected to Host PC (\(api.activeHostName.isEmpty ? "PinayPal" : api.activeHostName))" : "Host PC Offline")
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

            Spacer()

            HStack(spacing: 4) {
                Image(systemName: "lock.shield.fill")
                    .font(.system(size: 10))
                Text("Zero-Leak Shield")
                    .font(.system(size: 10, weight: .bold))
            }
            .foregroundColor(LiquidTheme.emerald)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(LiquidTheme.emerald.opacity(0.12))
            .clipShape(Capsule())
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .background(LiquidTheme.surface(for: colorScheme).opacity(0.6))
    }

    // MARK: - Chat Bubble Builder

    @ViewBuilder
    private func chatBubble(for message: AIChatMessage) -> some View {
        if message.role == "user" {
            // User message bubble (Right aligned)
            HStack {
                Spacer(minLength: 40)
                Text(message.content)
                    .font(.system(size: 14, weight: .medium))
                    .foregroundColor(.white)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 11)
                    .background(
                        LinearGradient(
                            colors: [LiquidTheme.gold, Color(hex: "3B82F6")],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
                    .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
                    .shadow(color: LiquidTheme.gold.opacity(0.2), radius: 6, x: 0, y: 2)
            }
        } else {
            // Assistant message bubble (Left aligned)
            HStack(alignment: .top, spacing: 10) {
                ZStack {
                    Circle()
                        .fill(LiquidTheme.gold.opacity(0.18))
                        .frame(width: 28, height: 28)

                    Image(systemName: "sparkles")
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(LiquidTheme.gold)
                }
                .padding(.top, 4)

                VStack(alignment: .leading, spacing: 10) {
                    Text(message.content)
                        .font(.system(size: 14, weight: .regular))
                        .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                        .lineSpacing(3)

                    if let action = message.proposedAction {
                        actionProposalCard(action)
                    }
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 12)
                .background(
                    RoundedRectangle(cornerRadius: 18, style: .continuous)
                        .fill(LiquidTheme.surface(for: colorScheme))
                        .overlay(
                            RoundedRectangle(cornerRadius: 18, style: .continuous)
                                .stroke(LiquidTheme.border(for: colorScheme).opacity(0.6), lineWidth: 1)
                        )
                )

                Spacer(minLength: 30)
            }
        }
    }

    // MARK: - Action Proposal Card

    private func actionProposalCard(_ action: AIChatProposedAction) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 6) {
                Image(systemName: "bolt.fill")
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(LiquidTheme.gold)

                Text(action.title)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(LiquidTheme.gold)

                Spacer()

                Text("REQUIRES APPROVAL")
                    .font(.system(size: 8, weight: .black))
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(LiquidTheme.gold.opacity(0.2))
                    .foregroundColor(LiquidTheme.gold)
                    .clipShape(Capsule())
            }

            Text(action.description)
                .font(.system(size: 12, weight: .regular))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

            if let result = actionExecutionStatus[action.id] {
                HStack(spacing: 6) {
                    Image(systemName: result.contains("✅") ? "checkmark.circle.fill" : "info.circle.fill")
                        .foregroundColor(result.contains("✅") ? LiquidTheme.emerald : LiquidTheme.coral)
                    Text(result)
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                }
                .padding(.top, 4)
            } else {
                HStack(spacing: 10) {
                    Button(action: {
                        executeAction(action: action, approved: false)
                    }) {
                        Text("Cancel")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                            .padding(.horizontal, 14)
                            .padding(.vertical, 7)
                            .background(LiquidTheme.surface(for: colorScheme))
                            .clipShape(RoundedRectangle(cornerRadius: 8))
                    }

                    Button(action: {
                        executeAction(action: action, approved: true)
                    }) {
                        HStack(spacing: 5) {
                            Image(systemName: "checkmark")
                                .font(.system(size: 11, weight: .bold))
                            Text("Approve & Run")
                                .font(.system(size: 12, weight: .bold))
                        }
                        .foregroundColor(.black)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 7)
                        .background(LiquidTheme.emerald)
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                        .shadow(color: LiquidTheme.emerald.opacity(0.3), radius: 6, x: 0, y: 2)
                    }
                }
                .padding(.top, 4)
            }
        }
        .padding(12)
        .background(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(Color(hex: "080D1A"))
                .overlay(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .stroke(LiquidTheme.gold.opacity(0.7), lineWidth: 1)
                )
        )
    }

    // MARK: - Typing Indicator

    private var typingIndicatorBubble: some View {
        HStack(spacing: 10) {
            ZStack {
                Circle()
                    .fill(LiquidTheme.gold.opacity(0.18))
                    .frame(width: 28, height: 28)

                Image(systemName: "sparkles")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(LiquidTheme.gold)
            }

            HStack(spacing: 6) {
                ProgressView()
                    .progressViewStyle(CircularProgressViewStyle(tint: LiquidTheme.gold))
                    .scaleEffect(0.8)

                Text("Thinking & analyzing host...")
                    .font(.system(size: 13, weight: .medium))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            .background(LiquidTheme.surface(for: colorScheme))
            .clipShape(RoundedRectangle(cornerRadius: 16))

            Spacer()
        }
    }

    // MARK: - Quick Chips

    private var quickChipsRow: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(quickPrompts, id: \.self) { prompt in
                    Button(action: {
                        let haptic = UIImpactFeedbackGenerator(style: .light)
                        haptic.impactOccurred()
                        sendMessage(prompt)
                    }) {
                        Text(prompt)
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                            .background(LiquidTheme.surface(for: colorScheme))
                            .overlay(
                                Capsule()
                                    .stroke(LiquidTheme.border(for: colorScheme), lineWidth: 1)
                            )
                            .clipShape(Capsule())
                    }
                    .disabled(isProcessing)
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
        }
        .background(LiquidTheme.surface(for: colorScheme).opacity(0.4))
    }

    // MARK: - Input Bar

    private var inputBar: some View {
        HStack(spacing: 10) {
            TextField("Ask anything or trigger an action...", text: $inputText)
                .font(.system(size: 14))
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .background(LiquidTheme.surface(for: colorScheme))
                .clipShape(RoundedRectangle(cornerRadius: 20))
                .overlay(
                    RoundedRectangle(cornerRadius: 20)
                        .stroke(LiquidTheme.border(for: colorScheme), lineWidth: 1)
                )
                .disabled(isProcessing)
                .onSubmit {
                    if !inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        sendMessage(inputText)
                    }
                }

            Button(action: {
                sendMessage(inputText)
            }) {
                ZStack {
                    Circle()
                        .fill(inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isProcessing ? Color.gray.opacity(0.3) : LiquidTheme.gold)
                        .frame(width: 40, height: 40)

                    Image(systemName: "arrow.up")
                        .font(.system(size: 16, weight: .bold))
                        .foregroundColor(inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isProcessing ? .white.opacity(0.5) : .black)
                }
            }
            .disabled(inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isProcessing)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(LiquidTheme.surface(for: colorScheme))
    }

    // MARK: - Actions

    private func sendMessage(_ prompt: String) {
        let trimmed = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !isProcessing else { return }

        inputText = ""
        let userMsg = AIChatMessage(role: "user", content: trimmed)
        messages.append(userMsg)
        isProcessing = true

        let haptic = UIImpactFeedbackGenerator(style: .medium)
        haptic.impactOccurred()

        Task {
            let result = await api.sendAIChat(prompt: trimmed)
            await MainActor.run {
                isProcessing = false
                if result.success, let msg = result.message {
                    messages.append(msg)
                } else {
                    let err = result.error ?? "Failed to contact host AI"
                    messages.append(AIChatMessage(role: "assistant", content: "⚠️ Could not connect to AI engine: \(err)"))
                }
            }
        }
    }

    private func executeAction(action: AIChatProposedAction, approved: Bool) {
        let haptic = UINotificationFeedbackGenerator()
        actionExecutionStatus[action.id] = approved ? "Executing action on PC host..." : "Action cancelled."

        Task {
            let res = await api.executeAIAction(actionId: action.id, userApproved: approved)
            await MainActor.run {
                if approved {
                    if res.success {
                        haptic.notificationOccurred(.success)
                        actionExecutionStatus[action.id] = "✅ " + res.message
                    } else {
                        haptic.notificationOccurred(.error)
                        actionExecutionStatus[action.id] = "❌ " + res.message
                    }
                } else {
                    actionExecutionStatus[action.id] = "Action cancelled by user."
                }
            }
        }
    }

    private func clearHistory() {
        let haptic = UIImpactFeedbackGenerator(style: .light)
        haptic.impactOccurred()
        messages = [
            AIChatMessage(
                role: "assistant",
                content: "History cleared. How can I help you today?"
            )
        ]
        actionExecutionStatus.removeAll()
        Task {
            _ = await api.clearAIChatHistory()
        }
    }
}
