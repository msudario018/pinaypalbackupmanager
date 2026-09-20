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
- **Tip for Cloudflare Quick Tunnel:** Always launch your tunnel on Windows with:
  ```cmd
  cloudflared tunnel --url http://localhost:8080 --http-host-header localhost
  ```
