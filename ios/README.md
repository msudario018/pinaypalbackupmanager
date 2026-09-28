# PinayPal Backup Manager — Native iOS App (SwiftUI + Liquid Glass iOS 27 Design)

A native iOS companion application for **PinayPal Backup Manager**, featuring a futuristic **Liquid Glass (iOS 27)** aesthetic, biometric **Face ID / Touch ID** protection, dual HUD & Web views, and native real-time control.

---

## Key Features

1. **Futuristic Liquid Glass (iOS 27) Design System:**
   - Ultra-thin frosted acrylic material (`.ultraThinMaterial`) with translucent glass back-glow.
   - Chromatic specular reflection borders and dynamic ambient gradients.
   - Floating glass capsule navigation with smooth Spring animations.
   - Native iOS haptic touch feedback on backup triggers and tab switches.

2. **Biometric Face ID / Touch ID Security Shield:**
   - Optional Face ID lock on app launch to protect your dashboard from unauthorized access.
   - Easily toggled in the in-app connection settings sheet.

3. **Dual-Mode Experience:**
   - **Mode 1: Native Liquid HUD:** 100% native SwiftUI cards, live progress meters, hardware RAM breakdown (`X GB / Y GB`), multi-partition drive graphs, and live terminal logs.
   - **Mode 2: Live Web Dashboard:** Embedded `WKWebView` with native pull-to-refresh, automatic PIN injection, and fluid scrolling.

4. **Interactive Remote Control:**
   - Single-tap triggers for Website FTP sync, SQL Database sync, and Mailchimp sync with instant toast feedback.
   - Diagnostics trigger and server health monitoring.

---

## How to Run & Build

### Option A: Open Locally in Xcode (Mac)
1. Open the project in Xcode:
   ```bash
   open ios/PinayPalBackup.xcodeproj
   ```
2. Select your iPhone or iOS Simulator.
3. Press **Cmd + R** to run!

### Option B: Free Cloud Build on GitHub Actions (No Mac Needed!)
A GitHub Actions workflow is included at [`.github/workflows/ios-build.yml`](file:///e:/Project/pinaypalbackupmanager/.github/workflows/ios-build.yml):
1. Push your repository to GitHub.
2. Go to **Actions** &rarr; **Build iOS Native App** &rarr; **Run workflow**.
3. Once completed (~2 minutes), download the compiled **`PinayPalBackup.ipa`** artifact directly from the workflow run!
4. Sideload the `.ipa` onto your iPhone using **AltStore**, **SideStore**, **Sideloadly**, or **TrollStore**.

---

## Connecting to Your PC

In the app, tap the **Gear Icon (⚙️)** in the top right:
- **Tunnel / Host URL:** Enter your Cloudflare Quick Tunnel (e.g. `https://your-name.trycloudflare.com`) or your PC's local LAN IP (e.g. `http://192.168.0.138:8080`).
- **Web Access PIN:** Enter your secret PIN if configured in PinayPal Settings.
- **Tailscale Failover URL (optional):** Your PC's private Tailscale address (e.g. `http://100.64.0.2:8080`). It is auto-filled after QR pairing when Tailscale runs on your PC, and auto-adopted from the server status on first connection.
- **Tip for Cloudflare Quick Tunnel:** Always launch your tunnel on Windows with:
  ```cmd
  cloudflared tunnel --url http://localhost:8080 --http-host-header localhost
  ```

---

## Connection Failover & Tailscale Reminder

The app routes traffic automatically through three tiers, always preferring the fastest reachable one:

1. **LAN** (`http://192.168.x.x:8080`) — used when you are on the same Wi-Fi network.
2. **Cloudflare Tunnel** — used when the LAN is unreachable (outside your home/office).
3. **Tailscale** (`http://100.x.y.z:8080`) — used when the Cloudflare Tunnel is down, as long as the Tailscale VPN is enabled on your iPhone.

Additional behaviour:

- **Fail-back:** the app continuously probes LAN and Cloudflare and switches back the moment they respond (the header chip shows 🟢 LAN, 🟣 Tunnel, or 🩵 Tailscale).
- **Tunnel recreation:** once connected, if the PC reports that its managed Cloudflare Quick Tunnel should be up but is down, the app asks it to rerun `cloudflared` over the active route and adopts the fresh URL automatically.
- **Enable Tailscale notification:** when Cloudflare *and* the LAN are both unreachable (e.g. you are outside and the tunnel died), a local notification prompts you to switch on the Tailscale VPN. This reminder fires at most once every 30 minutes and can be disabled under **Settings → iOS Background Monitoring & Alerts → Remind Me to Enable Tailscale**.
