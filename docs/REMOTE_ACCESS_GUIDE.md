# Remote Access Guide: Accessing PinayPal Backup Manager from Anywhere

This guide explains how to access the **PinayPal Backup Manager Web Dashboard** outside your home or office network.

---

## Overview
PinayPal Backup Manager includes a built-in lightweight Web Dashboard running by default on `http://localhost:8080/` (or your configured port). 

To access this dashboard securely from the internet (e.g. from your mobile phone, laptop, or work computer) **without port forwarding**, the recommended method is **Cloudflare Tunnel (Zero-Trust)**.

---

## Method 1: Cloudflare Quick Tunnel (60 Seconds, No Account Needed)

This is the fastest method to test remote access immediately:

1. **Download `cloudflared` for Windows**:
   - Download the official executable: [cloudflared-windows-amd64.exe](https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe)
   - Save it as `cloudflared.exe` in any folder (e.g., `C:\cloudflared\cloudflared.exe`).

2. **Run the Quick Tunnel**:
   - Open PowerShell or Command Prompt in that folder and run:
     ```powershell
     .\cloudflared.exe tunnel --url http://localhost:8080
     ```
   - Cloudflare will output an instant secure HTTPS link:
     ```text
     https://random-name-1234.trycloudflare.com
     ```

3. **Connect**:
   - Open that URL in any browser on your phone or remote computer. You will see the PinayPal Web Dashboard.

---

## Method 2: Permanent Tunnel with Custom Domain (Recommended for 24/7 Access)

If you own a domain (e.g. `yourdomain.com`) or want a fixed URL like `https://backup.yourdomain.com`:

### Step 1: Create a Free Cloudflare Account
1. Sign up at [cloudflare.com](https://cloudflare.com) (free plan).
2. Go to the **Cloudflare Zero Trust** dashboard: [one.dash.cloudflare.com](https://one.dash.cloudflare.com).
3. On the left sidebar, navigate to **Networks** &rarr; **Tunnels**.
4. Click **Create a Tunnel**, select **Cloudflared**, and name it `PinayPal-Backup`.

### Step 2: Install the Service on Windows
1. In the tunnel setup wizard, select **Windows** under Choose your environment.
2. Cloudflare provides a single PowerShell installation command with your secret tunnel token:
   ```powershell
   winget install --id Cloudflare.cloudflared
   cloudflared.exe service install <YOUR_TUNNEL_TOKEN>
   ```
3. Run this command in PowerShell as Administrator. It installs `cloudflared` as a background Windows Service that automatically boots up with your PC.

### Step 3: Route Public Hostname to PinayPal
1. In the Cloudflare tunnel setup wizard, click **Next** to go to **Public Hostnames**.
2. Configure:
   - **Subdomain**: `backup`
   - **Domain**: `yourdomain.com`
   - **Type**: `HTTP`
   - **URL**: `localhost:8080` (or your configured port)
3. Click **Save Hostname**.

Your dashboard is now live 24/7 at `https://backup.yourdomain.com`!

---

## Method 3: Cloudflare Access Protection (Email PIN / Two-Factor)

To protect your remote dashboard with an extra security layer:
1. In Cloudflare Zero Trust, go to **Access** &rarr; **Applications**.
2. Click **Add an Application** &rarr; **Self-hosted**.
3. Set the domain to `backup.yourdomain.com`.
4. Under **Policies**, create an "Allow" policy with rule: **Include** &rarr; **Emails** (enter your email address).
5. Now, whenever anyone visits `https://backup.yourdomain.com`, Cloudflare will first ask for your email and email you a 6-digit one-time passcode before allowing access to the dashboard.

---

## Method 4: Tailscale Mesh VPN (Private Network Alternative)

If you don't want a public domain and only want private device-to-device access:
1. Install [Tailscale](https://tailscale.com) on your Windows PC and your mobile phone.
2. Sign in with the same account.
3. Tailscale assigns your PC an IP address (e.g., `100.x.y.z`).
4. On your phone browser, navigate directly to `http://100.x.y.z:8080/`.

---

## Web Dashboard Security Tips
- In PinayPal Backup Manager under **Settings** &rarr; **Web Dashboard & Remote Access**:
  - Enable **Require Access PIN for Web Dashboard**.
  - Enter a secret access PIN.
  - This ensures that anyone accessing the web page must enter the PIN before viewing backups or triggering actions.
