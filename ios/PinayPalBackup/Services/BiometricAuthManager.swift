import Foundation
import LocalAuthentication

@MainActor
public class BiometricAuthManager: ObservableObject {
    @Published public var isUnlocked: Bool = false
    @Published public var biometricType: LABiometryType = .none
    @Published public var errorMessage: String? = nil

    public init() {
        checkBiometricType()
        // If user disabled biometric lock, start unlocked
        let isEnabled = UserDefaults.standard.bool(forKey: "pp_biometrics_enabled")
        if !isEnabled {
            isUnlocked = true
        }
    }

    public func checkBiometricType() {
        let context = LAContext()
        var error: NSError?
        if context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) {
            biometricType = context.biometryType
        } else {
            biometricType = .none
        }
    }

    public func authenticate() async {
        let context = LAContext()
        context.localizedCancelTitle = "Use PIN"

        var error: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) else {
            // Fallback or biometrics not enrolled
            isUnlocked = true
            return
        }

        do {
            let success = try await context.evaluatePolicy(
                .deviceOwnerAuthenticationWithBiometrics,
                localizedReason: "Unlock PinayPal Backup Manager Dashboard"
            )
            if success {
                self.isUnlocked = true
                self.errorMessage = nil
            }
        } catch {
            self.errorMessage = error.localizedDescription
            self.isUnlocked = false
        }
    }

    public func lock() {
        let isEnabled = UserDefaults.standard.bool(forKey: "pp_biometrics_enabled")
        if isEnabled {
            isUnlocked = false
        }
    }

    public func testBiometrics() async -> (success: Bool, message: String) {
        let context = LAContext()
        var error: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) else {
            let desc = error?.localizedDescription ?? "Face ID / Touch ID not configured or available."
            return (false, desc)
        }

        do {
            let success = try await context.evaluatePolicy(
                .deviceOwnerAuthenticationWithBiometrics,
                localizedReason: "Verify Face ID Biometric Recognition"
            )
            if success {
                return (true, "Biometric authentication confirmed successfully!")
            } else {
                return (false, "Biometric verification failed.")
            }
        } catch {
            return (false, error.localizedDescription)
        }
    }
}
