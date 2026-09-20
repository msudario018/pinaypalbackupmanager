import SwiftUI
import WebKit

public struct LiquidWebView: View {
    @ObservedObject var api: PinayPalAPIService
    @Binding var showSettingsSheet: Bool

    @State private var webView: WKWebView? = nil
    @State private var isLoading: Bool = false
    @State private var canGoBack: Bool = false

    public var body: some View {
        ZStack {
            LiquidTheme.backgroundDark.ignoresSafeArea()

            VStack(spacing: 0) {
                // Top floating bar
                HStack {
                    HStack(spacing: 6) {
                        Image(systemName: "safari.fill")
                            .foregroundColor(LiquidTheme.gold)
                        Text("Live Web Dashboard")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(.white)
                    }

                    Spacer()

                    if isLoading {
                        ProgressView()
                            .tint(LiquidTheme.gold)
                            .scaleEffect(0.8)
                            .padding(.trailing, 6)
                    }

                    Button {
                        webView?.reload()
                    } label: {
                        Image(systemName: "arrow.clockwise")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }

                    Button {
                        showSettingsSheet = true
                    } label: {
                        Image(systemName: "gearshape.fill")
                            .font(.system(size: 14))
                            .foregroundColor(LiquidTheme.textSecondary)
                    }
                    .padding(.leading, 8)
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .background(.ultraThinMaterial)

                // The WKWebView
                WKWebViewRepresentable(url: api.serverUrl, pin: api.accessPin, webView: $webView, isLoading: $isLoading)
            }
        }
    }
}

public struct WKWebViewRepresentable: UIViewRepresentable {
    let url: String
    let pin: String
    @Binding var webView: WKWebView?
    @Binding var isLoading: Bool

    public func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true

        let wv = WKWebView(frame: .zero, configuration: config)
        wv.navigationDelegate = context.coordinator
        wv.isOpaque = false
        wv.backgroundColor = .clear
        wv.scrollView.backgroundColor = UIColor(red: 0.043, green: 0.055, blue: 0.078, alpha: 1.0)
        wv.scrollView.bounces = true

        // Add Pull-to-refresh on WebView
        let refreshControl = UIRefreshControl()
        refreshControl.tintColor = UIColor(red: 0.988, green: 0.639, blue: 0.067, alpha: 1.0)
        refreshControl.addTarget(context.coordinator, action: #selector(Coordinator.handleRefresh(_:)), for: .valueChanged)
        wv.scrollView.refreshControl = refreshControl

        DispatchQueue.main.async {
            self.webView = wv
        }

        loadRequest(on: wv)
        return wv
    }

    public func updateUIView(_ uiView: WKWebView, context: Context) { }

    private func loadRequest(on wv: WKWebView) {
        var cleanUrl = url.trimmingCharacters(in: .whitespacesAndNewlines)
        if cleanUrl.isEmpty { cleanUrl = "http://localhost:8080" }
        guard let target = URL(string: cleanUrl) else { return }

        // Inject PIN cookie if configured
        if !pin.isEmpty {
            let cookie = HTTPCookie(properties: [
                .domain: target.host ?? "localhost",
                .path: "/",
                .name: "pp_pin",
                .value: pin,
                .secure: target.scheme == "https",
                .expires: Date().addingTimeInterval(86400 * 30)
            ])
            if let c = cookie {
                wv.configuration.websiteDataStore.httpCookieStore.setCookie(c) {
                    var req = URLRequest(url: target)
                    req.timeoutInterval = 10
                    wv.load(req)
                }
                return
            }
        }

        var req = URLRequest(url: target)
        req.timeoutInterval = 10
        wv.load(req)
    }

    public func makeCoordinator() -> Coordinator {
        Coordinator(self)
    }

    public class Coordinator: NSObject, WKNavigationDelegate {
        var parent: WKWebViewRepresentable

        init(_ parent: WKWebViewRepresentable) {
            self.parent = parent
        }

        public func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
            parent.isLoading = true
        }

        public func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            parent.isLoading = false
            webView.scrollView.refreshControl?.endRefreshing()
        }

        public func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
            parent.isLoading = false
            webView.scrollView.refreshControl?.endRefreshing()
        }

        @objc func handleRefresh(_ sender: UIRefreshControl) {
            parent.webView?.reload()
        }
    }
}
