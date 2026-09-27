import SwiftUI

public struct ProfileSheetView: View {
    @Environment(\.dismiss) private var dismiss
    @ObservedObject var api: PinayPalAPIService
    @ObservedObject var authManager: BiometricAuthManager
    @Environment(\.colorScheme) private var colorScheme

    // Username Change States
    @State private var newUsername: String = ""
    @State private var isChangingUsername: Bool = false
    @State private var usernameMessage: String? = nil
    @State private var usernameSuccess: Bool = false

    // Password Change States
    @State private var currentPassword: String = ""
    @State private var newPassword: String = ""
    @State private var confirmPassword: String = ""
    @State private var showCurrentPassword: Bool = false
    @State private var showNewPassword: Bool = false
    @State private var isChangingPassword: Bool = false
    @State private var passwordMessage: String? = nil
    @State private var passwordSuccess: Bool = false

    // Confirmation & Switching
    @State private var showLogoutConfirmation: Bool = false

    public init(api: PinayPalAPIService, authManager: BiometricAuthManager) {
        self.api = api
        self.authManager = authManager
    }

    private var usernameDisplay: String {
        api.currentUser?.username ?? (api.accessPin.isEmpty ? "PinayPal User" : "PIN Authenticated")
    }

    private var userInitials: String {
        let name = usernameDisplay.trimmingCharacters(in: .whitespacesAndNewlines)
        if name.count >= 2 {
            return String(name.prefix(2)).uppercased()
        } else if let first = name.first {
            return String(first).uppercased()
        }
        return "PP"
    }

    private var roleDisplay: String {
        api.currentUser?.role ?? "Operator"
    }

    public var body: some View {
        NavigationStack {
            ZStack {
                LiquidTheme.background(for: colorScheme).ignoresSafeArea()

                // Ambient Radial Glow
                RadialGradient(
                    colors: [LiquidTheme.purple.opacity(0.18), LiquidTheme.gold.opacity(0.08), Color.clear],
                    center: .top,
                    startRadius: 20,
                    endRadius: 360
                )
                .ignoresSafeArea()

                ScrollView {
                    VStack(spacing: 20) {
                        // Profile Hero Header
                        profileHeaderCard

                        // Account Information Card
                        accountInfoCard

                        // Change Username Card
                        changeUsernameCard

                        // Change Password Card
                        changePasswordCard

                        // Session Actions Card (Switch User / Log Out)
                        sessionActionsCard

                        Spacer().frame(height: 30)
                    }
                    .padding(.horizontal, 16)
                    .padding(.top, 14)
                }
            }
            .navigationTitle("User Profile")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Done") { dismiss() }
                        .font(.body.weight(.semibold))
                        .foregroundColor(LiquidTheme.gold)
                }
            }
            .alert("Sign Out of PinayPal?", isPresented: $showLogoutConfirmation) {
                Button("Sign Out", role: .destructive) {
                    Task {
                        await api.logout()
                        authManager.isUnlocked = false
                        dismiss()
                    }
                }
                Button("Cancel", role: .cancel) { }
            } message: {
                Text("You will need to sign in again with your credentials or PIN to access desktop backups.")
            }
        }
    }

    // MARK: - Profile Header Card
    private var profileHeaderCard: some View {
        VStack(spacing: 14) {
            ZStack {
                // Outer glowing gradient ring
                Circle()
                    .stroke(
                        LinearGradient(
                            colors: [LiquidTheme.gold, LiquidTheme.purple, LiquidTheme.cyan],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        ),
                        lineWidth: 3
                    )
                    .frame(width: 86, height: 86)
                    .shadow(color: LiquidTheme.gold.opacity(0.45), radius: 12, x: 0, y: 4)

                // Avatar Background
                Circle()
                    .fill(
                        LinearGradient(
                            colors: [LiquidTheme.surfaceDark, Color(red: 0.15, green: 0.12, blue: 0.25)],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
                    .frame(width: 78, height: 78)

                // Initials or Icon
                Text(userInitials)
                    .font(.system(size: 28, weight: .black, design: .rounded))
                    .foregroundStyle(
                        LinearGradient(
                            colors: [.white, LiquidTheme.gold],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
            }
            .padding(.top, 8)

            VStack(spacing: 4) {
                Text(usernameDisplay)
                    .font(.system(size: 20, weight: .black, design: .rounded))
                    .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))

                if let email = api.currentUser?.email, !email.isEmpty {
                    Text(email)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                }

                HStack(spacing: 8) {
                    // Role Pill
                    HStack(spacing: 4) {
                        Image(systemName: "shield.lefthalf.filled")
                            .font(.system(size: 10))
                        Text(roleDisplay.uppercased())
                            .font(.system(size: 10, weight: .heavy, design: .rounded))
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(LiquidTheme.gold.opacity(0.18))
                    .foregroundColor(LiquidTheme.gold)
                    .clipShape(Capsule())

                    // Status Pill
                    HStack(spacing: 4) {
                        Circle()
                            .fill(api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
                            .frame(width: 6, height: 6)
                        Text(api.isOnline ? "ONLINE" : "OFFLINE")
                            .font(.system(size: 10, weight: .heavy, design: .rounded))
                            .foregroundColor(api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral)
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background((api.isOnline ? LiquidTheme.emerald : LiquidTheme.coral).opacity(0.14))
                    .clipShape(Capsule())
                }
                .padding(.top, 4)
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 20)
        .padding(.horizontal, 16)
        .liquidGlassCard(cornerRadius: 22, glow: LiquidTheme.gold.opacity(0.14), variant: .prominent)
    }

    // MARK: - Account Information Card
    private var accountInfoCard: some View {
        VStack(alignment: .leading, spacing: 14) {
            Label("PROFILE & SYSTEM DETAILS", systemImage: "info.circle.fill")
                .font(.system(size: 11, weight: .bold, design: .rounded))
                .foregroundColor(LiquidTheme.cyan)

            Divider().background(Color.white.opacity(0.08))

            infoRow(title: "Account Username", value: usernameDisplay, icon: "person.text.rectangle")
            infoRow(title: "Access Level", value: roleDisplay, icon: "lock.shield")
            infoRow(title: "Desktop Machine", value: api.status?.system?.hostname ?? "Desktop Connected", icon: "desktopcomputer")
            infoRow(title: "Server Endpoint", value: api.serverUrl, icon: "network")
            if let os = api.status?.system?.os {
                infoRow(title: "Host OS", value: os, icon: "cpu")
            }
        }
        .padding(18)
        .liquidGlassCard(cornerRadius: 18, glow: LiquidTheme.cyan.opacity(0.10))
    }

    // MARK: - Change Username Card
    private var changeUsernameCard: some View {
        VStack(alignment: .leading, spacing: 14) {
            Label("UPDATE USERNAME", systemImage: "person.crop.circle.badge.plus")
                .font(.system(size: 11, weight: .bold, design: .rounded))
                .foregroundColor(LiquidTheme.emerald)

            Text("Change the display name used across your mobile dashboard and desktop backups.")
                .font(.system(size: 12))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

            VStack(alignment: .leading, spacing: 6) {
                Text("NEW USERNAME")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

                HStack {
                    Image(systemName: "person.fill")
                        .foregroundColor(LiquidTheme.emerald)
                        .frame(width: 20)

                    TextField("Enter new username", text: $newUsername)
                        .autocapitalization(.none)
                        .disableAutocorrection(true)
                        .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                }
                .padding(12)
                .background(Color.white.opacity(0.06))
                .cornerRadius(12)
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(Color.white.opacity(0.12), lineWidth: 1)
                )
            }

            if let msg = usernameMessage {
                HStack(spacing: 6) {
                    Image(systemName: usernameSuccess ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    Text(msg)
                }
                .font(.system(size: 12, weight: .medium))
                .foregroundColor(usernameSuccess ? LiquidTheme.emerald : LiquidTheme.coral)
            }

            Button {
                executeUsernameChange()
            } label: {
                HStack {
                    if isChangingUsername {
                        ProgressView().progressViewStyle(CircularProgressViewStyle(tint: .black))
                        Text("Updating...")
                    } else {
                        Image(systemName: "arrow.triangle.2.circlepath")
                        Text("Save New Username")
                    }
                }
                .font(.system(size: 13, weight: .bold, design: .rounded))
                .foregroundColor(.black)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 12)
                .background(newUsername.trimmingCharacters(in: .whitespacesAndNewlines).count >= 3 ? LiquidTheme.emerald : LiquidTheme.emerald.opacity(0.4))
                .cornerRadius(12)
            }
            .disabled(isChangingUsername || newUsername.trimmingCharacters(in: .whitespacesAndNewlines).count < 3)
        }
        .padding(18)
        .liquidGlassCard(cornerRadius: 18, glow: LiquidTheme.emerald.opacity(0.10))
    }

    // MARK: - Change Password Card
    private var changePasswordCard: some View {
        VStack(alignment: .leading, spacing: 14) {
            Label("CHANGE PASSWORD", systemImage: "key.fill")
                .font(.system(size: 11, weight: .bold, design: .rounded))
                .foregroundColor(LiquidTheme.gold)

            Text("Ensure your new password has at least 6 characters.")
                .font(.system(size: 12))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

            // Current Password
            VStack(alignment: .leading, spacing: 6) {
                Text("CURRENT PASSWORD")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

                HStack {
                    Image(systemName: "lock")
                        .foregroundColor(LiquidTheme.gold)
                        .frame(width: 20)

                    if showCurrentPassword {
                        TextField("Enter current password", text: $currentPassword)
                            .autocapitalization(.none)
                            .disableAutocorrection(true)
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    } else {
                        SecureField("Enter current password", text: $currentPassword)
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    }

                    Button {
                        showCurrentPassword.toggle()
                    } label: {
                        Image(systemName: showCurrentPassword ? "eye.slash" : "eye")
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                }
                .padding(12)
                .background(Color.white.opacity(0.06))
                .cornerRadius(12)
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(Color.white.opacity(0.12), lineWidth: 1)
                )
            }

            // New Password
            VStack(alignment: .leading, spacing: 6) {
                Text("NEW PASSWORD")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

                HStack {
                    Image(systemName: "lock.shield")
                        .foregroundColor(LiquidTheme.gold)
                        .frame(width: 20)

                    if showNewPassword {
                        TextField("At least 6 characters", text: $newPassword)
                            .autocapitalization(.none)
                            .disableAutocorrection(true)
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    } else {
                        SecureField("At least 6 characters", text: $newPassword)
                            .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                    }

                    Button {
                        showNewPassword.toggle()
                    } label: {
                        Image(systemName: showNewPassword ? "eye.slash" : "eye")
                            .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                    }
                }
                .padding(12)
                .background(Color.white.opacity(0.06))
                .cornerRadius(12)
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(Color.white.opacity(0.12), lineWidth: 1)
                )
            }

            // Confirm Password
            VStack(alignment: .leading, spacing: 6) {
                Text("CONFIRM NEW PASSWORD")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

                HStack {
                    Image(systemName: "checkmark.shield")
                        .foregroundColor(LiquidTheme.gold)
                        .frame(width: 20)

                    SecureField("Re-enter new password", text: $confirmPassword)
                        .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                }
                .padding(12)
                .background(Color.white.opacity(0.06))
                .cornerRadius(12)
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(Color.white.opacity(0.12), lineWidth: 1)
                )
            }

            if let msg = passwordMessage {
                HStack(spacing: 6) {
                    Image(systemName: passwordSuccess ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    Text(msg)
                }
                .font(.system(size: 12, weight: .medium))
                .foregroundColor(passwordSuccess ? LiquidTheme.emerald : LiquidTheme.coral)
            }

            Button {
                executePasswordChange()
            } label: {
                HStack {
                    if isChangingPassword {
                        ProgressView().progressViewStyle(CircularProgressViewStyle(tint: .black))
                        Text("Updating Password...")
                    } else {
                        Image(systemName: "lock.rotation")
                        Text("Update Password")
                    }
                }
                .font(.system(size: 13, weight: .bold, design: .rounded))
                .foregroundColor(.black)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 12)
                .background(isPasswordFormValid ? LiquidTheme.gold : LiquidTheme.gold.opacity(0.4))
                .cornerRadius(12)
            }
            .disabled(isChangingPassword || !isPasswordFormValid)
        }
        .padding(18)
        .liquidGlassCard(cornerRadius: 18, glow: LiquidTheme.gold.opacity(0.10))
    }

    private var isPasswordFormValid: Bool {
        !currentPassword.isEmpty && newPassword.count >= 6 && newPassword == confirmPassword
    }

    // MARK: - Session Actions Card
    private var sessionActionsCard: some View {
        VStack(spacing: 12) {
            Button {
                // Switch User
                Task {
                    await api.logout()
                    authManager.isUnlocked = false
                    dismiss()
                }
            } label: {
                HStack {
                    Image(systemName: "person.2.badge.gearshape.fill")
                    Text("Switch User Account")
                }
                .font(.system(size: 13, weight: .bold))
                .foregroundColor(.white)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 13)
                .background(Color.white.opacity(0.08))
                .cornerRadius(12)
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(Color.white.opacity(0.15), lineWidth: 1)
                )
            }

            Button {
                showLogoutConfirmation = true
            } label: {
                HStack {
                    Image(systemName: "rectangle.portrait.and.arrow.right.fill")
                    Text("Sign Out")
                }
                .font(.system(size: 13, weight: .bold))
                .foregroundColor(LiquidTheme.coral)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 13)
                .background(LiquidTheme.coral.opacity(0.12))
                .cornerRadius(12)
                .overlay(
                    RoundedRectangle(cornerRadius: 12)
                        .stroke(LiquidTheme.coral.opacity(0.3), lineWidth: 1)
                )
            }
        }
    }

    // MARK: - Logic Handlers
    private func executeUsernameChange() {
        let clean = newUsername.trimmingCharacters(in: .whitespacesAndNewlines)
        guard clean.count >= 3 else { return }

        isChangingUsername = true
        usernameMessage = nil

        Task {
            let (success, message) = await api.changeUsername(newUsername: clean)
            isChangingUsername = false
            usernameSuccess = success
            usernameMessage = message

            if success {
                newUsername = ""
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.success)
            } else {
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.error)
            }
        }
    }

    private func executePasswordChange() {
        guard isPasswordFormValid else { return }

        isChangingPassword = true
        passwordMessage = nil

        Task {
            let (success, message) = await api.changePassword(
                currentPassword: currentPassword,
                newPassword: newPassword
            )
            isChangingPassword = false
            passwordSuccess = success
            passwordMessage = message

            if success {
                currentPassword = ""
                newPassword = ""
                confirmPassword = ""
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.success)
            } else {
                let haptic = UINotificationFeedbackGenerator()
                haptic.notificationOccurred(.error)
            }
        }
    }

    private func infoRow(title: String, value: String, icon: String) -> some View {
        HStack(spacing: 10) {
            Image(systemName: icon)
                .font(.system(size: 14))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))
                .frame(width: 20)

            Text(title)
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(LiquidTheme.textSecondary(for: colorScheme))

            Spacer()

            Text(value)
                .font(.system(size: 13, weight: .bold, design: .rounded))
                .foregroundColor(LiquidTheme.textPrimary(for: colorScheme))
                .lineLimit(1)
        }
        .padding(.vertical, 2)
    }
}
