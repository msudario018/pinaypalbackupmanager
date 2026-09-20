import SwiftUI

public struct LoginView: View {
    @EnvironmentObject var api: PinayPalAPIService
    @EnvironmentObject var authManager: BiometricAuthManager

    @State private var usernameInput: String = ""
    @State private var passwordInput: String = ""
    @State private var isShowPassword: Bool = false
    @State private var enableBiometricsToggle: Bool = UserDefaults.standard.bool(forKey: "pp_biometrics_enabled")
    @State private var isSubmitting: Bool = false
    @State private var errorMessage: String? = nil

    public init() {}

    public var body: some View {
        ZStack {
            LiquidTheme.backgroundDark.ignoresSafeArea()

            // Ambient glows
            RadialGradient(
                colors: [LiquidTheme.gold.opacity(0.14), LiquidTheme.purple.opacity(0.06), Color.clear],
                center: .topLeading,
                startRadius: 20,
                endRadius: 400
            )
            .ignoresSafeArea()

            ScrollView(showsIndicators: false) {
                VStack(spacing: 24) {
                    // Visual Header
                    VStack(spacing: 12) {
                        ZStack {
                            Circle()
                                .fill(LiquidTheme.surfaceDark)
                                .frame(width: 84, height: 84)
                                .overlay(
                                    Circle()
                                        .stroke(LiquidTheme.liquidGoldGradient, lineWidth: 2)
                                )
                                .shadow(color: LiquidTheme.gold.opacity(0.35), radius: 18, x: 0, y: 6)

                            Image(systemName: "lock.shield.fill")
                                .font(.system(size: 38, weight: .bold))
                                .foregroundStyle(LiquidTheme.liquidGoldGradient)
                        }
                        .padding(.top, 28)

                        Text("Sign In to PinayPal")
                            .font(.system(size: 24, weight: .black, design: .rounded))
                            .foregroundColor(LiquidTheme.textPrimary)

                        Text("Use the same username and password registered on your desktop PC app.")
                            .font(.system(size: 13, weight: .medium, design: .rounded))
                            .foregroundColor(LiquidTheme.textSecondary)
                            .multilineTextAlignment(.center)
                            .padding(.horizontal, 24)

                        // Connected Server Pill
                        HStack(spacing: 6) {
                            Circle()
                                .fill(api.isOnline ? LiquidTheme.emerald : LiquidTheme.gold)
                                .frame(width: 7, height: 7)

                            Text(api.serverUrl)
                                .font(.system(size: 11, weight: .semibold, design: .monospaced))
                                .foregroundColor(LiquidTheme.textSecondary)
                                .lineLimit(1)

                            Button(action: { api.disconnectServer() }) {
                                Text("Change")
                                    .font(.system(size: 10, weight: .bold))
                                    .foregroundColor(LiquidTheme.gold)
                                    .padding(.horizontal, 6)
                                    .padding(.vertical, 2)
                                    .background(LiquidTheme.gold.opacity(0.15))
                                    .cornerRadius(6)
                            }
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .background(LiquidTheme.surfaceDark.opacity(0.7))
                        .cornerRadius(20)
                        .overlay(
                            Capsule().stroke(Color.white.opacity(0.12), lineWidth: 0.8)
                        )
                    }

                    // Login Credentials Card
                    VStack(alignment: .leading, spacing: 18) {
                        // Username Field
                        VStack(alignment: .leading, spacing: 6) {
                            Text("USERNAME OR EMAIL")
                                .font(.system(size: 11, weight: .bold, design: .rounded))
                                .foregroundColor(LiquidTheme.textSecondary)

                            HStack {
                                Image(systemName: "person.fill")
                                    .foregroundColor(LiquidTheme.gold)
                                    .frame(width: 20)

                                TextField("Enter username or email", text: $usernameInput)
                                    .autocapitalization(.none)
                                    .disableAutocorrection(true)
                                    .foregroundColor(LiquidTheme.textPrimary)
                            }
                            .padding(14)
                            .background(Color.white.opacity(0.06))
                            .cornerRadius(12)
                            .overlay(
                                RoundedRectangle(cornerRadius: 12)
                                    .stroke(Color.white.opacity(0.15), lineWidth: 1)
                            )
                        }

                        // Password Field
                        VStack(alignment: .leading, spacing: 6) {
                            Text("PASSWORD")
                                .font(.system(size: 11, weight: .bold, design: .rounded))
                                .foregroundColor(LiquidTheme.textSecondary)

                            HStack {
                                Image(systemName: "key.fill")
                                    .foregroundColor(LiquidTheme.gold)
                                    .frame(width: 20)

                                if isShowPassword {
                                    TextField("Enter password", text: $passwordInput)
                                        .autocapitalization(.none)
                                        .disableAutocorrection(true)
                                        .foregroundColor(LiquidTheme.textPrimary)
                                } else {
                                    SecureField("Enter password", text: $passwordInput)
                                        .foregroundColor(LiquidTheme.textPrimary)
                                }

                                Button(action: { isShowPassword.toggle() }) {
                                    Image(systemName: isShowPassword ? "eye.slash.fill" : "eye.fill")
                                        .foregroundColor(LiquidTheme.textSecondary)
                                }
                            }
                            .padding(14)
                            .background(Color.white.opacity(0.06))
                            .cornerRadius(12)
                            .overlay(
                                RoundedRectangle(cornerRadius: 12)
                                    .stroke(Color.white.opacity(0.15), lineWidth: 1)
                            )
                        }

                        // Face ID / Touch ID Option
                        Toggle(isOn: $enableBiometricsToggle) {
                            HStack(spacing: 8) {
                                Image(systemName: "faceid")
                                    .font(.system(size: 18))
                                    .foregroundColor(LiquidTheme.cyan)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("Enable Face ID Unlock")
                                        .font(.system(size: 13, weight: .bold, design: .rounded))
                                        .foregroundColor(LiquidTheme.textPrimary)
                                    Text("Quickly unlock the dashboard on next launch")
                                        .font(.system(size: 11))
                                        .foregroundColor(LiquidTheme.textSecondary)
                                }
                            }
                        }
                        .tint(LiquidTheme.gold)
                        .padding(.top, 4)

                        // Error message
                        if let error = errorMessage {
                            HStack(spacing: 8) {
                                Image(systemName: "exclamationmark.triangle.fill")
                                    .foregroundColor(LiquidTheme.coral)
                                Text(error)
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(LiquidTheme.coral)
                            }
                            .padding(12)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(LiquidTheme.coral.opacity(0.12))
                            .cornerRadius(10)
                        }

                        // Submit Button
                        Button(action: handleSignIn) {
                            HStack {
                                if isSubmitting {
                                    ProgressView()
                                        .progressViewStyle(CircularProgressViewStyle(tint: .black))
                                        .scaleEffect(0.9)
                                    Text("Signing in...")
                                } else {
                                    Image(systemName: "arrow.right.square.fill")
                                    Text("SIGN IN")
                                }
                            }
                            .font(.system(size: 15, weight: .bold, design: .rounded))
                            .foregroundColor(.black)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 14)
                            .background(LiquidTheme.liquidGoldGradient)
                            .cornerRadius(12)
                            .shadow(color: LiquidTheme.gold.opacity(0.4), radius: 10, x: 0, y: 4)
                        }
                        .disabled(isSubmitting || usernameInput.isEmpty || passwordInput.isEmpty)
                        .opacity((usernameInput.isEmpty || passwordInput.isEmpty) ? 0.6 : 1.0)
                    }
                    .padding(22)
                    .modifier(LiquidGlassCardModifier(cornerRadius: 22, glowColor: LiquidTheme.gold.opacity(0.18)))
                    .padding(.horizontal, 20)

                    // Footer note
                    VStack(spacing: 8) {
                        Text("Need to recover your username or password?")
                            .font(.system(size: 12))
                            .foregroundColor(LiquidTheme.textSecondary)

                        Text("Use 'Forgot user?' or 'Forgot password?' on your desktop PC app to receive an OTP.")
                            .font(.system(size: 11))
                            .foregroundColor(LiquidTheme.gold.opacity(0.9))
                            .multilineTextAlignment(.center)
                            .padding(.horizontal, 30)
                    }
                    .padding(.top, 4)
                    .padding(.bottom, 36)
                }
            }
        }
    }

    private func handleSignIn() {
        isSubmitting = true
        errorMessage = nil

        Task {
            let (success, message) = await api.login(
                username: usernameInput.trimmingCharacters(in: .whitespacesAndNewlines),
                password: passwordInput
            )
            isSubmitting = false

            if success {
                // Save biometric preference
                UserDefaults.standard.set(enableBiometricsToggle, forKey: "pp_biometrics_enabled")
                authManager.isUnlocked = true
            } else {
                errorMessage = message
            }
        }
    }
}
