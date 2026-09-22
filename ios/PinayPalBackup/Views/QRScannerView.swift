import SwiftUI
import AVFoundation

public struct QRScannerView: View {
    public var onScanSuccess: (ConnectionQrPayload) -> Void
    public var onDismiss: () -> Void

    @State private var hasCameraPermission: Bool = true
    @State private var scanError: String? = nil

    public init(onScanSuccess: @escaping (ConnectionQrPayload) -> Void, onDismiss: @escaping () -> Void) {
        self.onScanSuccess = onScanSuccess
        self.onDismiss = onDismiss
    }

    public var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            #if targetEnvironment(simulator)
            VStack(spacing: 20) {
                Image(systemName: "camera.viewfinder")
                    .font(.system(size: 60))
                    .foregroundColor(LiquidTheme.gold)

                Text("Camera Simulator Mode")
                    .font(.system(size: 18, weight: .bold))
                    .foregroundColor(.white)

                Text("Camera is unavailable in iOS simulator.\nTap below to simulate scanning a pairing QR.")
                    .font(.system(size: 13))
                    .foregroundColor(LiquidTheme.textSecondary)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 30)

                Button {
                    let simulated = ConnectionQrPayload(
                        localUrl: "http://192.168.1.50:8080",
                        fallbackUrl: "https://backup.pinaypal.com",
                        pin: "1234",
                        hostname: "SIMULATOR-PC",
                        version: "3.3.7"
                    )
                    let haptic = UINotificationFeedbackGenerator()
                    haptic.notificationOccurred(.success)
                    onScanSuccess(simulated)
                } label: {
                    Text("Simulate QR Scan")
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(.black)
                        .padding(.horizontal, 24)
                        .padding(.vertical, 12)
                        .background(LiquidTheme.gold)
                        .cornerRadius(12)
                }
            }
            #else
            QRCameraPreview { resultString in
                guard let data = resultString.data(using: .utf8) else { return }
                if let payload = try? JSONDecoder().decode(ConnectionQrPayload.self, from: data) {
                    let haptic = UINotificationFeedbackGenerator()
                    haptic.notificationOccurred(.success)
                    onScanSuccess(payload)
                } else if resultString.hasPrefix("http") {
                    // Fallback in case raw URL is embedded
                    let payload = ConnectionQrPayload(localUrl: resultString, fallbackUrl: nil, pin: nil, hostname: nil, version: nil)
                    let haptic = UINotificationFeedbackGenerator()
                    haptic.notificationOccurred(.success)
                    onScanSuccess(payload)
                }
            }
            .ignoresSafeArea()

            // Viewfinder reticle overlay
            VStack {
                HStack {
                    Button {
                        onDismiss()
                    } label: {
                        Image(systemName: "xmark.circle.fill")
                            .font(.system(size: 32))
                            .foregroundColor(.white.opacity(0.8))
                    }
                    .padding(.leading, 20)
                    .padding(.top, 20)

                    Spacer()
                }

                Spacer()

                // Target box
                ZStack {
                    RoundedRectangle(cornerRadius: 24)
                        .strokeBorder(LiquidTheme.gold, lineWidth: 3)
                        .frame(width: 250, height: 250)
                        .background(Color.white.opacity(0.04))

                    VStack(spacing: 8) {
                        Image(systemName: "qrcode.viewfinder")
                            .font(.system(size: 40))
                            .foregroundColor(LiquidTheme.gold)

                        Text("Align QR code within frame")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(.white)
                    }
                }

                Spacer()

                Text("Point your camera at the Pairing QR Code on the PinayPal Web Dashboard or PC App.")
                    .font(.system(size: 13))
                    .foregroundColor(LiquidTheme.textSecondary)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 40)
                    .padding(.bottom, 40)
            }
            #endif
        }
    }
}

#if !targetEnvironment(simulator)
struct QRCameraPreview: UIViewControllerRepresentable {
    var onCodeScanned: (String) -> Void

    func makeUIViewController(context: Context) -> QRScannerViewController {
        let controller = QRScannerViewController()
        controller.onCodeScanned = onCodeScanned
        return controller
    }

    func updateUIViewController(_ uiViewController: QRScannerViewController, context: Context) {}
}

class QRScannerViewController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    var onCodeScanned: ((String) -> Void)?
    private var captureSession: AVCaptureSession?
    private var previewLayer: AVCaptureVideoPreviewLayer?
    private var hasScanned: Bool = false

    override func viewDidLoad() {
        super.viewDidLoad()
        setupCamera()
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer?.frame = view.layer.bounds
    }

    private func setupCamera() {
        let session = AVCaptureSession()

        guard let device = AVCaptureDevice.default(for: .video),
              let input = try? AVCaptureDeviceInput(device: device) else {
            return
        }

        if session.canAddInput(input) {
            session.addInput(input)
        }

        let output = AVCaptureMetadataOutput()
        if session.canAddOutput(output) {
            session.addOutput(output)
            output.setMetadataObjectsDelegate(self, queue: DispatchQueue.main)
            output.metadataObjectTypes = [.qr]
        }

        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        view.layer.addSublayer(layer)
        self.previewLayer = layer
        self.captureSession = session

        DispatchQueue.global(qos: .userInitiated).async {
            session.startRunning()
        }
    }

    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        guard !hasScanned,
              let metadata = metadataObjects.first as? AVMetadataMachineReadableCodeObject,
              let stringVal = metadata.stringValue else {
            return
        }

        hasScanned = true
        captureSession?.stopRunning()
        onCodeScanned?(stringVal)
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        if captureSession?.isRunning == true {
            captureSession?.stopRunning()
        }
    }
}
#endif
