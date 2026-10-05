# Changelog

## v3.9.3 (2026-10-05)

### Fixed & Improved
- **Non-Dev PC Cloud Admin Detection**: When installed on a non-dev PC with an empty local database, the startup pipeline now queries Firebase for existing admin and user accounts created on the Dev PC. If an admin account exists, users are pulled directly to local SQLite and the app transitions straight to `LoginWindow`, eliminating the redundant "Create Administrator Account" setup wizard on secondary machines.
- **Birthday Format As You Type (`YYYY-MM-DD`)**: Added real-time auto-formatting as users type in `SetupWizardWindow` and `LoginWindow` (converting slashes, dots, and unformatted digits into `YYYY-MM-DD`), alongside explicit format indicators and strict date range validation (1900 to present).
- **Floating AI Orb Button Hover/Press Square Artifact**: Replaced default FluentTheme button template styles on `BtnAvatarTrigger` to remove the square bounding rectangle on hover, click, and focus. Added `CornerRadius="29"` and glowing cybernetic hover/press micro-animations.
- **Natural Conversational AI & Casual Greeting Handling**: Eliminated robotic canned openers (`"Good question — here's the full picture."`) and removed unsolicited status dumping or action proposal cards when the user merely says "hi", "hello", or "how are you".
- **Chat Drawer Scroll Clearance & Action Card Cutoff**: Removed restrictive `ScrollViewer.Padding` that prevented scrolling to the true bottom in Avalonia. Added dedicated bottom margin to `MessagesContainer` and expanded drawer height so action cards and approve/cancel buttons are never clipped.
- **Computer Telemetry Card White-on-White Render**: Fixed `BrushesFor` in `SettingsControl.axaml.cs` which previously queried resources outside `ThemeDictionaries` and fell back to `Brushes.White`, resulting in a solid white box with invisible white text in dark mode. Implemented `TryGetResource` with dark-theme fallbacks.

## v3.9.2 (2026-10-05)

### Fixed
- **Fatal `InvalidOperationException: Call from invalid thread` on Session Inactivity Timeout**: When `SessionTimeoutService` fired on a background `TimerQueueTimer` thread, `AuthService.HandleSessionTimeout()` raised `OnUserChanged` directly on the background thread. Subscribers in `MainWindow` (`UpdateUserManagementButtonVisibility`) and `HomeControl` (`UpdateGreeting`) invoked `FindControl` without dispatching to Avalonia's UI thread, causing an immediate crash with `Dispatcher.VerifyAccess ThrowVerifyAccess`. Event dispatching in `AuthService` now automatically marshals to `Dispatcher.UIThread`, and UI event handlers now verify thread access.
- **Avalonia Application Startup Race Condition**: In `App.axaml.cs`, `Initialize()` was using `async void` with an unneeded asynchronous call, causing Avalonia's framework initialization to proceed out of sequence before XAML resources were fully bound. Switched `Initialize()` to synchronous with explicit `AvaloniaXamlLoader.Load(this)`.
- **Application Exiting During Setup Wizard Launch**: When a fresh install or unconfigured instance launched without existing users, `LoginWindow` opened `SetupWizardWindow` and closed itself without updating `desktop.MainWindow`, triggering premature application lifetime termination. `desktop.MainWindow` is now explicitly reassigned to `SetupWizardWindow` prior to closing `LoginWindow`.

## v3.9.1 (2026-10-05)

### Fixed
- **WinSCP `Unknown switch 'resume'` halted FTP and SQL Auto-Sync transfers.** In WinSCP's directory synchronisation API (`Session.SynchronizeDirectories`), the command-line equivalent does not accept a `-resume` switch. Specifying `TransferResumeSupport` and `OverwriteMode.Resume` inside `BuildTransferOptions()` caused WinSCP to inject the invalid switch, causing auto-sync and manual directory syncs to abort with `[WARNING] [AUTO-SYNC] STOPPED: Error detected - Unknown switch 'resume'`. Removed the unsupported switches from directory sync options while keeping `SpeedLimit = 0` (unthrottled) and `TransferMode.Binary`.

## v3.9.0 (2026-10-05)

### Added
- **AI Persistent Memory & Autonomous Learning** (`services/AIMemoryStore.cs`):
  - Local JSON-backed, thread-safe memory store (`ai_memory.json`) persisting operational preferences, system facts, and guidelines across sessions.
  - Natural conversation memory triggers: "remember that...", "what do you remember?", "forget that...", and "clear memories".
  - Strictly sandboxed memory bounded to 50 key items with proactive credential rejection (passwords, tokens, and secrets are barred from storage).
- **AI Agent Profiles & One-Click Hardware Auto-Tuning** (`services/AIAssistantService.cs`):
  - Selectable agent roles in desktop Settings:
    - **Guardian / Sentinel**: Security-first, conservative actions, strict compliance checks.
    - **Specialist / Operator**: Tailored for automated backup orchestration and SQL/FTP diagnostics.
    - **Speedy / Assistant**: Low latency, lightweight, and concise responses.
  - **Auto-Tune for My PC**: One-click telemetry engine detecting logical CPU cores and physical RAM to configure thread pools and memory quotas for optimal inference speed.
- **Dynamic AI Action Execution Engine**:
  - Replaces rigid canned text with dynamic intent parsing (`[ACTION: type(param="val")]`).
  - Capable of invoking system operations: `start_backup`, `verify_backups`, `check_health`, `switch_agent_profile`, `test_telegram`, `send_telegram_qr`, `forget_memory`, `clear_memories`.
  - Built-in safety gate (`RequireActionApproval = true`) ensuring state-modifying actions prompt the user for explicit confirmation before running.
- **Zero-Leak Security Shield v2**:
  - Comprehensive regex-based real-time redaction before prompt transmission.
  - Automatically redacts Telegram Bot tokens, Cloudflare tunnel tokens, database credentials in connection strings, URL-embedded secrets, Bearer tokens, and generic API keys.
  - Negative lookahead protection `(?!\[REDACTED)` prevents double-redaction clobbering.
- **Telegram Bot Integration & Remote Control** (`services/TelegramService.cs`):
  - **Two-way bidirectional Bot**: Provides background long-polling so users can control backups directly from Telegram without needing open inbound router ports or public webhooks.
  - **Automated Telegram Alerts**: Real-time notifications for backup started (🚀), completed (✅ with duration, size, file name), failed (🚨 with diagnostic error details), network/tunnel disconnects, and outdated backups (>24h). Replaces legacy Gmail/SMTP notifications.
  - **iOS Reconnection QR Code via Telegram**: Command `/qr` or `/connect` generates and uploads a high-density pairing QR code image with connection PIN, local IP, and Cloudflare tunnel credentials directly into the Telegram chat so users can reconnect their iOS app immediately when disconnected.
  - **Interactive Backup Commands**:
    - `/backup full` (or `/backup all`) — Triggers parallel backup across Website (FTP), Database (SQL), and Mailchimp.
    - `/backup ftp` | `/backup sql` | `/backup mailchimp` — Triggers individual service backups.
    - `/backup mailchimp members` | `campaigns` | `reports` | `merge_fields` | `tags` — Triggers granular Mailchimp exports.
    - `/status` — Live summary of backup freshness, last timestamps, active processes, and tunnel state.
    - `/health` — On-demand health check across all services.
    - `/pause` & `/resume` — Pause or resume automatic scheduler.
    - `/help` — Comprehensive interactive command manual. Supports both `/command` and natural typing without slashes.
  - **Settings UI**: Replaced old Gmail card with a dedicated **TELEGRAM BOT & REMOTE ALERTS** card in desktop Settings (`SettingsControl.axaml`) and Web Dashboard modal (`WebDashboardService.cs`), featuring Bot Token input, Chat ID auto-detection (`🔍 Detect Chat ID`), trigger toggles, instant test alert button, and "Send iOS QR Code" button.
  - **Security & Authorization**: Binds bot command execution strictly to the configured Telegram Chat ID, rejecting unauthorized messages while welcoming new users with their personal Chat ID for easy setup.
- **iOS Companion App v3.9.0 (Build 29)**:
  - Synchronized version across all 4 build targets (`MARKETING_VERSION = 3.9.0; CURRENT_PROJECT_VERSION = 29`).
  - Added new AI quick action chips in `LiquidAIAssistantSheet.swift` for Telegram status, Cloudflare tunnel health, system metrics, and memory queries.
  - Added interactive release notes card in `ChangelogSheetView.swift`.
- **`TimeFormat` central helper** (`services/TimeFormat.cs`). Every human-readable timestamp now goes through one place instead of each call site interpolating its own format string. Two explicit groups:
  - *User-facing* — 12-hour with a mandatory AM/PM marker (`Clock`, `ClockSeconds`, `Stamp`, `StampSeconds`, `DateTimeShort`, `DateTimeShortSeconds`, `Compact`).
  - *Machine-readable* — 24-hour `UtcStamp`, for logs, CSV exports and email templates where an explicit `UTC` suffix is written alongside.
- **xunit test project** (`tests/`) with 26 assertions locking the 12-hour convention, Zero-Leak v2 sanitization, memory storage, agent switching, action parsing, and Telegram QR generation.

### Fixed
- **AI chat clipped its newest message.** Every `ScrollToEnd()` in `AssistantWidgetControl` ran *synchronously* right after `Children.Add(...)`. Avalonia had not measured the new content yet, so the scroll was applied against a stale `Extent`/`Viewport` and silently did nothing — leaving the latest reply cut off mid-sentence at the bottom of the drawer. Scrolling is now deferred to `DispatcherPriority.Loaded`, after measure/arrange.
- Chat auto-scroll is now **sticky**: scroll up to read history and new replies no longer yank you back to the bottom; return to the bottom and it resumes following.
- **`Full Settings →` button rendered on top of the "AI INFERENCE PROVIDER" heading.** `BtnOpenFullSettings` was missing `Grid.Column="1"`, so it stayed in column 0 and overlapped the label. The neighbouring status row already did this correctly, which is why only this one collided.
- Extra bottom padding in the message list so the last reply is not flush against the chips row.
- **12-hour formatting finished across all targets.** v3.2.8 converted the web dashboard, `/api/status` and the main iOS views, but left these showing 24-hour values: policy-window warnings, schedule Next/Last Run, retry countdowns, all `BackupManager` freshness strings, `SecurityAuditService` credential timestamps, `SystemStatusService`, verification history, and the AI assistant's chat history rows.
- **Ambiguous 12-hour timestamps.** `BackupManager` rendered `MM/dd hh:mm:ss` — 12-hour with **no AM/PM marker**, so 3:30 PM displayed as `03:30:22`, and the same `LastUpdate` field was 24-hour eight lines later.
- **`hh:mm:sstt` in `MainWindow`** produced `03:45:22PM` with no space before the marker — the US/Manila clocks in the window header.
- **UTC shown as local.** `BackupManager`'s file-freshness list printed `GetFreshnessUtc(...)` (genuinely UTC, despite the display looking local) with no `UTC` marker.
- **Web dashboard clock followed browser locale**, silently flipping to 24-hour on devices set to a 24-hour region. Now pinned to `hour12: true`.
- Root project excluded `tests/**` from the default `**/*.cs` glob, which otherwise compiled the test sources into the WinExe and broke the build.

## v3.8.2 (2026-10-03)

### Added
- **Automatic computer discovery** (`NetworkScannerService`):
  - Sweeps your local subnet in parallel (bounded to 64 concurrent probes) and reports every reachable machine.
  - Machines already running PinayPal are detected automatically by probing `/api/ping`, so they can be added with one tap and appear ranked first.
  - Reverse DNS plus ARP-table enrichment resolves hostnames and MAC vendors (`Intel Corporate`, `Realtek`, ...).
  - Results pre-fill name, MAC and dashboard URL in **Settings → My Computers**.
  - New REST surface: `POST /api/computers/scan`.
- **Fleet telemetry history** — bounded per-computer ring buffer (288 samples) backing CPU/RAM sparklines, exposed as `GET /api/computers/history?id=`.
- **iOS fleet trends** — new `FleetSparkline` view renders per-PC CPU and RAM history on each computer card.
- **iOS live fleet updates** — the PCs tab polls `/api/computers` every 10 s *while it is on screen* and reflects adds, edits, telemetry changes and deletions from the desktop without manual refresh.
- `DELETE /api/computers?id=` for removing a machine from the fleet.
- MAC address fields now auto-format to `A4:BB:6D:11:22:33` as you type or paste.
- Sticky bottom save bar plus a top-right Save button in Settings; one click persists every card.

### Fixed
- **Avalonia colour channels were transposed.** Avalonia parses 8-digit hex as `#AARRGGBB`, not CSS `#RRGGBBAA`. The AI chat drawer was `#070C18FA` — read as **3% opaque**, so the panel was effectively invisible. The same mistake appeared in **99 colours across 7 files**, including invisible `#00000000` shadows and orange-tinted blue gradients. All normalised to true ARGB.
- **Schedule times saved as the literal text `AVALONIA.CONTROLS.COMBOBOXITEM`.** `ComboBoxItem.ToString()` returns the type name rather than `Content`; selections now read `ComboBoxItem.Content`.
- **AI CPU-thread count spinner was unusable** — replaced the broken `NumericUpDown` with a validated `0–64` TextBox.
- **iOS `Info.plist` version drift.** The plist hardcoded `3.7.2 / 25` while the Xcode project declared `3.8.1 / 27`, so the shipped binary reported a stale version to the system and to App Store Connect. Both keys now use `$(MARKETING_VERSION)` / `$(CURRENT_PROJECT_VERSION)` so they cannot drift again.
- **iOS header/tab-content overlap** — the scrim now stays opaque through the title instead of fading to fully transparent.

## v3.8.1 (2026-10-03)

### Added
- **Smart Scheduling & Sync Safety** (`BackupPolicyService`, `SyncPreviewService`):
  - **Auto-evict the local AI model before scheduled backups.** A loaded chat model can hold several GB on a 16 GB machine, so `ExecuteBackupAsync` now asks Ollama to release the model (`keep_alive = 0`) before any transfer starts. Failures are logged and never block the backup.
  - **Per-service sync windows.** Each of Website FTP, SQL and Mailchimp gets its own allowed time-of-day window. Windows that cross midnight (e.g. 22:00-06:00) are supported, and a deferred backup reports *why* and *when* it will retry. A live "open now / closed now" readout is shown in Settings.
  - **Bandwidth-aware pausing.** Upload throughput is sampled every 5 s from the OS network counters with exponential smoothing. When it stays above your threshold past a grace period, scheduled backups pause so gaming and streaming stay smooth. The bandwidth readout in Settings stops its timer when the card is collapsed.
  - **Sync diff / preview before running.** `SyncPreviewService.BuildPlanAsync` compares the local tree against the remote tree and reports what *would* change - new, modified, unchanged and remote-only - without transferring a single byte. Remote-only files are reported but never deleted, preserving backup safety.
  - **Rollback.** Before a sync overwrites anything, the previous remote version of every changed file is downloaded into `Data/rollback/<service>/<timestamp>/`. Snapshots are pruned to a configurable depth, and the assistant or REST API can restore the newest one.
  - **Computer fleet heartbeat alerts.** Every 2 minutes each managed computer is probed. An alert fires only after a configurable grace period of continuous unreachability, so a brief reboot or Wi-Fi blip does not spam you. Peers with no dashboard URL configured are treated as "not set up", not as outages. Recovery is reported too.
  - New REST surface: `GET /api/sync/plan`, `POST /api/sync/rollback`, `GET /api/policy/status`.
  - New conversational actions: *"preview my sync"*, *"what would change"*, *"dry run"*, *"roll back the sync"*.

### Fixed
- **Smart-scheduling settings now actually persist.** `ConfigService.SaveOperation` merges into the on-disk config field by field, so the newly added options were being dropped on every restart. All ten new settings and the whole sync-window subtree are now copied explicitly.
- **Rollback snapshot used the wrong remote root.** Snapshots hardcoded `/` while a sync can target a different remote path, so restores could miss files. The real remote path is now threaded through.
- **Bounded AI pending-action growth.** Action proposals the user never approves previously stayed in memory for the whole session; they are now capped.

## v3.8.0 (2026-10-03)

### Added
- **My Computers — Remote Fleet Control**:
  - New `ComputerManagementService` managing every machine you own (`Dev PC`, `Main PC`, auxiliaries) with role, MAC, broadcast address, dashboard URL and access PIN, persisted to `computers.json`.
  - **Wake-on-LAN** magic-packet sender (102-byte packet, configurable broadcast + port).
  - **Remote power control**: restart, shut down, lock, sleep and sign out. Local machines execute directly after a grace delay; remote peers are commanded through their own PinayPal dashboard, which authenticates independently.
  - New REST surface: `GET/POST /api/computers`, `POST /api/computers/wake`, `POST /api/computers/action?action=`, and `POST /api/power/{action}` (used for peer-to-peer power proxying).
  - New **My Computers** settings card listing each machine with live CPU / RAM / temperature chips and one-tap power actions.
- **AI Assistant — Real Conversation & Proactive Behaviour**:
  - **Multi-turn memory is now actually sent to the model.** The Ollama provider moved from the stateless `/api/generate` endpoint to `/api/chat`, and both providers now replay the last *N* turns, so follow-ups like "run that one" or "yes, do it" actually resolve.
  - **Personality controls**: assistant name, Talkativeness (terse → chatty), Creativity (temperature) and Conversation Memory depth.
  - **Follow-up suggestions**: every reply now returns up to 3 contextual tappable next-step chips.
  - **Proactive updates**: a background scheduler posts status bubbles only when something genuinely needs attention (failed backups, RAM > 90 %, CPU ≥ 85 °C, tunnel offline) and never interrupts a running backup.
  - **New actions**: wake / restart / shut down / lock / sleep any computer by name, plus "free memory" which evicts the local LLM from RAM.
  - **Pronoun resolution** via slot memory, so "run that one" targets the service you last discussed.
  - **Online-but-secure cloud mode**: when escalating to a cloud provider, hostnames, IP addresses, URLs, MACs, file paths and computer names are replaced with placeholders, history is capped, and only aggregate telemetry rollups are sent.
  - **Hardened Zero-Leak Sanitizer**: now also redacts bearer/basic tokens, bare provider API keys, connection strings and Windows user paths on both input *and* output.
- **Collapsible Settings Cards**:
  - New reusable `CollapsibleCardControl`. Every settings section is now a collapsed card that expands on click and only realises its inputs when opened.
  - **AI Assistant card** now exposes a Personality & Conversation panel (name, talkativeness, creativity, memory depth, follow-ups, proactive interval) and a Safety & Privacy panel (mandatory action approval, Zero-Leak toggle).

### Fixed
- **FTP / SFTP sync crawling at kilobytes-per-second**:
  - Transfers now use explicit `TransferOptions` with `SpeedLimit = 0` (an inherited throttle could silently cap throughput), **binary** mode, and **smart resume** (`OverwriteMode.Resume` with `ResumeSupport.Smart`, 100 KB threshold). Interrupted uploads continue from where they stopped instead of restarting from byte zero.
  - Sync criteria is now explicit and pinned to `SynchronizationCriteria.Time`, so a file is only re-uploaded when its modification time actually changed. The previous inherited default (`Either`) re-sent any file whose *size or* timestamp differed, so merely touching a file forced a full re-upload.
  - Both FTP sessions pinned to `FtpMode.Passive` for reliable transfer behind NAT.
  - Progress events are throttled to ~120 ms with the final tick always forwarded, removing UI-thread contention that competed with the transfer itself.
  - Corrected the `SynchronizeDirectories` call to the real `(mode, local, remote, removeFiles, mirror, criteria, options)` overload. Delete/mirror semantics are unchanged: a backup still never removes remote files.
- **Native handle leaks** in long-running paths:
  - `RealtimeMonitoringService` was creating a new `Process` object on every sample (four times per CPU reading on a timer). Now uses a single cached process handle.
  - `WebDashboardService` and `HomeControl` no longer construct a `Process` on every status refresh just to read uptime; the start time is captured once.
- **Duplicate-request pile-up on iOS**: `fetchStatus()` now refuses to start while a previous poll is still in flight, eliminating overlapping requests on short timers.

### Performance
- **Hardware telemetry sensor caching**: `QueryCpuTemperature()` (WMI thermal-zone sweeps) and GPU WMI queries used to run on *every* read behind a 1.8 s cache. They now run on a dedicated 30 s cadence, while cheap CPU/RAM counters stay at 1.8 s. This is the single largest idle-CPU saving for a 24/7 daemon.
- **Low-power tray mode**: hiding the app to the tray widens the sensor cache to 90 s and restores it on restore.
- **Shared 2-second `/api/status` cache** so the dashboard and the iOS app polling concurrently don't each rebuild the full payload (health check + telemetry + website probe). Bypassable with `?refresh=1`.
- **iOS adaptive polling** now also considers app visibility: 2 s during a backup, 5 s foreground, 20 s background, and 15 s / 45 s under Battery Saver or Low Power Mode.

### Hardware Guidance
- The default local model is now `qwen2.5:3b-instruct-q4_K_M` and `num_thread` is configurable. On a Ryzen 5 5600 (6C/12T) with 16 GB, a 3B Q4 model plus a 4-thread cap keeps the assistant responsive while leaving headroom for backup transfers — the previous `llama3.2` default could starve the engine.
- Use **Free Memory** (or the Ollama `keep_alive = 0` request) before a large backup to reclaim the several GB a loaded model holds.

## v3.7.2 (2026-10-03)

### Fixed & Improved
- **AI Assistant Bespoke Iconography & Cybernetic Launcher Overhaul**:
  - **Twin-Sparkle AI Intelligence Emblem**: Replaced the generic 5-point star rating icon with a custom geometric twin-sparkle AI insignia (`M12,1.5 C12,7.3...`) with smooth cubic Bezier curves across the floating launcher button, drawer header avatar squircle, and notification bubble.
  - **Cybernetic Floating Orb**: Upgraded the floating trigger in `AssistantWidgetControl.axaml` into a 58×58 obsidian-midnight orb featuring iridescent metallic gold & cyan border trim, an animated cyber orbit ring, and a live emerald breathing status beacon.
  - **Matching Squircle Header & Bubble**: The drawer header avatar and speech bubble popup now feature matching gradient squircles with the twin-sparkle glyph.

- **Horizontal Prompt Chips Scroll & Bilateral Glass Navigation**:
  - **Horizontal Scroll & Wheel Translation**: Placed prompt chips inside an Avalonia `ScrollViewer` (`ChipsScrollViewer`) with `HorizontalScrollBarVisibility="Auto"` and `VerticalScrollBarVisibility="Disabled"`.
  - **Pointer Wheel Handler**: Added a `PointerWheelChanged` handler in `AssistantWidgetControl.axaml.cs` that converts vertical mouse wheel delta (`e.Delta.Y`) into smooth horizontal scrolling (`Vector(curOffset - delta, 0)`).
  - **Bilateral Glass Navigation Buttons**: Added glass-morphism left (`‹`) and right (`›`) navigation arrows on both ends of the chip tray (`BtnScrollChipsLeft`, `BtnScrollChipsRight`) for 1-click horizontal exploration.
  - **12 Categorized Diagnostic Prompt Chips**: Added quick action prompts for `🩺 Health Status`, `💾 Disk Space`, `📋 Recent Backups`, `⚡ Run All Backups`, `🌐 FTP Website Sync`, `🗄️ SQL Database Dump`, `✉️ Mailchimp Sync`, `☁️ Cloudflare Tunnel`, `🛡️ Tailscale Mesh`, `📧 Test Email Alert`, `🔍 Inspect Errors`, and `🛑 Emergency Halt`.

- **Dedicated AI Assistant Settings & In-Drawer Quick Controls**:
  - **Desktop Settings Integration**: Added an **AI ASSISTANT & AUTOMATION ENGINE** card with Zero-Leak Shield badge to `SettingsControl.axaml` and `SettingsControl.axaml.cs`:
    - **Interactive Toggles**: Enable Floating Assistant Widget, Greet Admin upon Successful Login, Sound Chimes on AI Notifications.
    - **Engine Selection**: Hybrid (Auto-Failover), Local Ollama, Cloud LLM (OpenAI/Gemini/Claude), Built-in Heuristics (Offline).
    - **Local Ollama Config**: Endpoint URL, Model selector, and real-time **Test Connection** button (`AIAssistantService.TestOllamaConnectionAsync`) with animated status feedback.
    - **Cloud LLM Config**: API Base URL, Model name, API Key.
    - **Controls**: Save configuration and reset to defaults buttons with confirmation toasts.
  - **In-Drawer Quick Settings**: Added a gear icon (`⚙️`) in the assistant header in `AssistantWidgetControl.axaml` to allow immediate 1-click provider switching, Ollama connection testing, and a direct link to the full settings tab.
  - **Dynamic Real-Time Sync**: Implemented `AIAssistantService.OnConfigChanged` static event so modifying settings immediately updates the widget's visibility, provider badge, and behavior in real time without restarting.

- **Expanded Diagnostics, Intelligence & 1-Click Proactive Actions**:
  - **Recent Backup Records**: Formats the last 5 backup runs into a structured markdown report showing service name, timestamp, status icon, file size, and run duration.
  - **Active Queue Diagnostics**: Real-time inspection of active FTP, Mailchimp, or SQL backup operations with live status via `AIAssistantService.IsAnyBackupRunning` and `GetActiveBackupDetails`.
  - **Network & Failover Routing**: Displays LAN IP (`FileDownloadService.GetAllLocalIPv4Addresses()`), Cloudflare Tunnel status & active URL, Tailscale mesh status, and internet status.
  - **Automated Root Cause Troubleshooting & Retries**: Intelligently analyzes recent error logs, diagnoses underlying causes, and proposes interactive 1-click action cards to retry the failed service or clear history.

- **Unified Versioning Across Ecosystem**:
  - Bumped PC Desktop App (`PinayPalBackupManager.csproj`, `MainWindow.axaml`, `UpdateAvailableDialog.axaml`), Web API (`WebDashboardService.cs`), and iOS Companion App (`project.pbxproj`, `Info.plist`, `LoginView.swift`, `SplashScreenView.swift`, `QRScannerView.swift`, `ChangelogSheetView.swift`) to `3.7.2` (iOS Build `25`).

## v3.7.1 (2026-10-02)

### Fixed & Improved
- **PC In-App Updater Overhaul & Direct Fallback**:
  - **Live Download Progress Dialog**: Enhanced `UpdateAvailableDialog.axaml` and `UpdateAvailableDialog.axaml.cs` with an interactive progress section featuring a live progress bar, percentage indicator, and dynamic status messages (`Downloading update... 45%`, `Applying update & restarting...`).
  - **Graceful Shutdown Before Restart**: Before calling `Velopack.UpdateManager.ApplyUpdatesAndRestart()`, `UpdateService.cs` now properly terminates background threads and child services (`CloudflareTunnelService.StopQuickTunnel()`, `FileDownloadService.Stop()`) and invokes `Environment.Exit(0)` to prevent file lock contention or frozen installer wait-pids.
  - **Intelligent Portable / Unmanaged Fallback**: When running in unmanaged or portable mode where Velopack is not installed (`!mgr.IsInstalled`), `UpdateService.cs` gracefully queries the GitHub Releases API (`https://api.github.com/repos/msudario018/pinaypalbackupmanager/releases/latest`), parses the latest release notes, and offers an instant browser link to download the new version rather than throwing an unhandled `NotInstalledException`.

- **Email Settings Input Persistence Fix**:
  - **Auto-Loading on App Launch**: Added explicit `NotificationService.LoadSettings()` in `Program.cs` during application startup, and implemented auto-lazy loading within `NotificationService.GetSettings()` to guarantee settings are loaded from disk if not yet initialized.
  - **Standardized AppData Path**: Settings are now persisted directly into `AppDataPaths.GetDataPath("notifications.json")` (with legacy fallback to the local directory), ensuring configuration survives portable directory moves and Velopack app updates.
  - **Resolved JSON Collision Anomalies**: Added `[System.Text.Json.Serialization.JsonIgnore]` attributes to computed alias properties (`EmailAlertsEnabled`, `SmtpSsl`, `SenderEmail`) in `NotificationSettings`, preventing serialization collisions and dropped values.
  - **Dynamic In-Memory Sync**: Saving in `SettingsControl.axaml.cs` immediately invokes `NotificationService.ConfigureNotifications(settings)`, updating the live SMTP dispatcher and alert pipeline in memory without requiring a restart. Opening the Settings tab automatically refreshes inputs from disk with `settings.RefreshEmailAlerts()`.

- **iOS Companion App Navigation Bar & Animations**:
  - **Tactile Facebook-Style Spring Bounce Animation**: Overhauled tab button interactions in `MainView.swift`. Tapping any tab triggers a snappy ease-in compression (scale 0.82) followed by a spring overshoot bounce (scale 1.20, damping 0.45) settling to 1.0, paired with medium haptic feedback and a subtle golden radial glow pulse. Re-tapping the already active tab performs a signature Facebook-style double-bounce wiggle and heavy haptic feedback.
  - **Compact Navigation Bar Geometry**: Made the navigation bar slightly smaller and sleeker (reduced button height from 46pt to 38pt, icon font from 15pt to 13.5pt, label font from 10pt to 9.5pt, logo to 26pt, and avatar to 34pt), creating a more refined and unobtrusive bottom bar.
  - **Smooth Scroll Background Scrims**: Added soft directional gradient scrims to `persistentHeader` and `liquidTabBar` so cards, text, and list items gracefully fade as they roll underneath the floating glass islands, eliminating jarring visual collisions at the edges, status bar, and home indicator.
  - **Eliminated Phantom Scroll Gaps**: Replaced obsolete `.padding(.top, 68)` in `LiquidDashboardView.swift`, `ActivityOverviewView` (`MainView.swift`), `BackupHistoryView.swift`, `AutomationsView.swift`, and `LiveLogsView.swift` with `.padding(.top, 10)`. Because `safeAreaInset` already reserves header bounds, removing duplicate padding eliminates the 68pt empty gap above the first card and enables immediate, natural scrolling.
  - **Perfected AI Assistant Spacing**: Tuned the floating AI Assistant orb in `LiquidDashboardView.swift` to `.padding(.bottom, 8)` so it docks proportionally with consistent margin right above the compact navigation bar.

- **Unified Versioning Across Ecosystem**:
  - Bumped PC Desktop App (`PinayPalBackupManager.csproj`, `MainWindow.axaml`, `UpdateAvailableDialog.axaml`), Web API (`WebDashboardService.cs`), and iOS Companion App (`project.pbxproj`, `Info.plist`, `LoginView.swift`, `SplashScreenView.swift`, `QRScannerView.swift`, `ChangelogSheetView.swift`) to `3.7.1` (iOS Build `24`).

## v3.7.0 (2026-10-02)

### Fixed & Improved
- **Smart Conversational AI Engine & Zero-Leak Security Boundary**:
  - **Zero-Leak Data Sanitizer**: Implemented strict regex scrubbers in `AIAssistantService.cs` (`SanitizePrompt`, `SanitizeOutput`, `BuildSanitizedSystemContext`) that strip passwords, pins, connection strings, auth tokens, and raw file payloads before sending context to any AI model, guaranteeing sensitive data never leaks.
  - **Multi-Provider Dispatcher**: Supports Local Ollama (`http://127.0.0.1:11434`), Cloud LLMs (OpenAI, Gemini, Claude with user-configured API keys), and a built-in offline Smart Heuristics Diagnostic Engine.
  - **Guarded Action Pipeline with Human-in-the-Loop Confirmation**: Read-only queries (system health, storage capacity, daemon status) execute immediately, while mutating operations (trigger FTP/SQL/Mailchimp backup, recreate Cloudflare tunnel, emergency stop, clear history) generate an interactive Action Proposal Card requiring explicit user approval.
  - **AI REST Endpoints for Remote Access**: Added `POST /api/ai/chat`, `POST /api/ai/action/execute`, `GET/POST /api/ai/config`, and `POST /api/ai/history/clear` in `WebDashboardService.cs`.

- **PC Desktop Floating Assistant Widget**:
  - **Floating Avatar Trigger**: Added `AssistantWidgetControl` anchored in the bottom-right corner of `MainWindow.axaml` with a dark glassmorphic badge, gold border glow, and live green status pulse.
  - **Proactive Speech Bubble Overlay**: Automatically pops up a smart speech bubble above the avatar on login ("Good evening, Wesley...") and on backup completions/warnings.
  - **Expandable Glassmorphic Chat Drawer**: Modern slide-up drawer with message bubbles, quick prompt chips ("Health Status", "Check Disk Space", "Run FTP Backup", "Tunnel Status", "Test Email"), and inline Action Proposal Cards with `[Approve & Execute]` and `[Cancel]` buttons.

- **iOS Companion App AI Assistant Integration**:
  - **Floating Assistant Orb**: Added glowing glassmorphic pill button to `LiquidDashboardView.swift` for one-tap AI assistance.
  - **Liquid AI Assistant Sheet**: Created `LiquidAIAssistantSheet.swift` featuring real-time conversational chat, quick prompt chips, Zero-Leak Shield badges, and native action confirmation cards with haptic feedback.
  - **API Client Extensions**: Added `sendAIChat(prompt:)`, `executeAIAction(actionId:userApproved:)`, and `clearAIChatHistory()` to `PinayPalAPIService.swift`.

- **Luxury Obsidian Email Template Overhaul**:
  - **Executive Glassmorphic Design**: Built `EmailTemplateService.cs` replacing old plain emails with a dark obsidian glassmorphism theme (`#06090E` container, `#0E1420` frosted card, gold `#F59E0B` and emerald `#10B981` accents).
  - **Hero Metrics Grid**: 3-column responsive metric blocks for Backup Size, Duration, and Server Hostname.
  - **Host Telemetry Snapshot Strip**: Live CPU usage, RAM utilization, and disk space included in the footer of every email alert.
  - **One-Click Action CTAs**: Direct buttons to open Web Dashboard, Remote Failover, and Health Diagnostics.

- **PC Long-Running Stability & PerformanceCounter Resource Leak Elimination**:
  - **Eliminated PerformanceCounter Handle Leak**: Removed transient `new PerformanceCounter` allocations from `RealtimeMonitoringService.cs` (`GetCpuUsage`, `GetMemoryUsage`) and `PerformanceMetricsService.cs`. In Windows, instantiating `PerformanceCounter` queries `HKEY_PERFORMANCE_DATA` and allocates unmanaged Perflib heap memory. Replacing this with `HealthCheckService.GetCpuUsage()` (singleton counter) and Win32 `GlobalMemoryStatusEx` eliminated registry handle leaks and Perflib heap corruption that caused silent crashes after long runs.
  - **Eliminated Blocking Thread Sleep**: Removed `System.Threading.Thread.Sleep(500)` in `PerformanceMetricsService.cs`, restoring asynchronous non-blocking throughput.
  - **Hardened Scheduler Timer**: Reduced `AdvancedSchedulingService.cs` polling frequency from every 1 second (1000ms) to every 15 seconds (15000ms), and wrapped `CheckScheduledTasks` and its background task dispatchers in top-level try-catch blocks to prevent unhandled timer exceptions.
  - **Guarded FileDownloadService & Tunnel Watchdogs**: Wrapped `Task.Run` async task runners in `FileDownloadService.cs` and `CloudflareTunnelService.cs` to prevent unobserved task exceptions from destabilizing the process.
  - **Recurring 4-Hour Background Auto-Update Watchdog**: Added `UpdateService.StartPeriodicBackgroundChecks()` which runs 45 seconds after launch and repeats every 4 hours, ensuring long-running server instances stay updated even when running 24/7 in the tray.
  - **Fixed Changelog Markdown Parser**: Updated `ExtractLatestChangelog` in `UpdateService.cs` to match any `## ` header rather than only `## [`, guaranteeing release notes populate correctly in update dialogs.

- **Unified Versioning Across Ecosystem**:
  - **Purged Hardcoded Legacy Fallbacks**: Updated `UpdateAvailableDialog.axaml`, `UpdateAvailableDialog.axaml.cs`, and `MainWindow.axaml` to default to `v3.7.0` instead of `v3.6.7` or `v2.19.0`.
  - **Synchronized Xcode Targets**: Updated all `MARKETING_VERSION` (3.7.0) and `CURRENT_PROJECT_VERSION` (23) settings in `project.pbxproj` and `Info.plist`.
  - **Synchronized iOS Views**: Updated version strings in `LoginView.swift`, `QRScannerView.swift`, `SplashScreenView.swift`, and made `ServerConfigSheet.swift` About tab badge read `v\(appVersion)` dynamically from `Bundle.main`.
  - **Updated Web Dashboard**: Synchronized `ApiVersion = "3.7.0"`, header badge, and connection info to `v3.7.0`.

- **iOS Companion App Addons & Improvements**:
  - **Multi-Route Ping Diagnostics Panel**: Added interactive Route Latency & Diagnostics card in Settings → Network, displaying live millisecond latencies across Local LAN, Cloudflare Tunnel, and Tailscale VPN mesh.
  - **Flush Route Cache**: Added one-tap "Flush Route Cache & Re-Probe LAN" action to reset failover memory and latch onto the fastest available route immediately.
  - **Battery Saver & Low Data Mode**: Added toggle in Settings that dynamically relaxes background polling to 10 seconds when idle, significantly preserving iPhone battery and cellular data.
  - **In-App Changelog**: Added v3.7.0 (Build 23) release notes to `ChangelogSheetView.swift`.

- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.7.0` (iOS Build `23`).

## v3.6.9 (2026-10-01)

### Fixed & Improved
- **PC 24/7 Long-Running Stability & Tray Persistence**:
  - **Fixed Long-Running App Exits**: Enforced `desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown` in `App.axaml.cs`. Avalonia's default `OnLastWindowClose` mode inadvertently terminated the entire application whenever `MainWindow.Hide()` minimized the app to the system tray or when transient modal dialogs closed.
  - **Auto-Restoring System Tray Icon**: Hooked Win32 `"TaskbarCreated"` window message (`AppIconHelper.cs` and `MainWindow.axaml.cs`) using `RegisterWindowMessage` and window subclassing; automatically recreates the Win32 `Shell_NotifyIcon` tray icon when Windows Explorer restarts or after sleep/lock-screen recovery.
  - **Synchronous Crash Telemetry**: Added synchronous crash logging to `system_log.txt` and `startup.log` upon `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`, guaranteeing crash diagnostics are flushed to disk before process death.
  - **Timer & Async Crash Hardening**: Guarded all background timer elapsed delegates in `RealtimeMonitoringService.cs`, `SystemStatusService.cs`, `NetworkConnectivityService.cs`, and `MainWindow.axaml.cs` with internal try-catch blocks to prevent unhandled `async void` exceptions from crashing the process.
  - **WMI COM Resource Leak Elimination**: Wrapped all `ManagementObjectSearcher` collections and `ManagementObject` instances in `HardwareTelemetryService.cs` in `using` blocks to prevent COM handle exhaustion during continuous hardware telemetry monitoring.
  - **Safe Background Update Dialogs**: Guarded `UpdateService.ShowUpdateDialogAsync` so that `ShowDialog(mainWindow)` is only called when `mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized`, preventing Avalonia `InvalidOperationException` crashes when updates are detected while running minimized.

- **iOS Failover & Tunnel Management Addons**:
  - **Interactive Offline Failover Assistant**: Added smart `offlineFailoverBanner` to `LiquidDashboardView.swift` when Cloudflare and LAN routes are unreachable, offering one-tap deep linking to launch the Tailscale VPN app (`tailscale://`) or web installer, plus a fast connection retry action.
  - **Cloudflare Tunnel Inactive Banner**: Added one-tap "Recreate" banner when connected via Tailscale or LAN while the managed tunnel is down, enabling immediate recovery of public remote access directly from iPhone.
  - **Direct API Tunnel Control**: Added `forceRestartCloudflareTunnel()` to `PinayPalAPIService.swift` to invoke `POST /api/tunnel/quick/restart` with authorization headers.
  - **In-App Changelog**: Updated `ChangelogSheetView.swift` with v3.6.9 (Build 22) release notes.

- **Web Dashboard Controls & Remote Access**:
  - **Quick Tunnel Recreate Button**: Added "🔄 Recreate Tunnel" button to the Cloudflare Quick Tunnel modal, enabling immediate generation of fresh `trycloudflare.com` URLs with single-flight process protection.
  - **Tailscale Mesh VPN Card**: Added real-time Tailscale VPN status and live link indicator to the System Specs & Remote Access card on the dashboard.

- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.6.9` (iOS Build `22`).

## v3.6.8 (2026-09-29)

### Added & Improved
- **Tailscale Third-Tier Failover & Cloudflare Tunnel Watchdog**:
  - Added `TailscaleNetworkService.cs` that auto-detects the local Tailscale interface (adapter name or CGNAT `100.64.0.0/10` address) and advertises `tailscaleUrl` through `/api/status`, `/api/connection-info`, and the pairing QR payload.
  - Hardened `CloudflareTunnelService.cs` with an auto-restart watchdog: unexpected `cloudflared` exits are recreated with exponential backoff (5s → 60s), the tunnel is recreated whenever internet connectivity is restored, and the previous session's tunnel is resurrected on app launch — all gated by the new `HttpServer.AutoRestartTunnel` setting (default on).
  - Added `POST /api/tunnel/quick/restart` for force-recreating the Quick Tunnel with a fresh `trycloudflare.com` URL; a single-flight gate prevents overlapping `cloudflared` processes.
  - Manual tunnel stops now disarm the watchdog and clear the persisted quick-tunnel URL so user intent is respected.
- **iOS Three-Route Auto-Failover (LAN → Cloudflare → Tailscale)**:
  - `PinayPalAPIService.swift` walks an ordered route chain and automatically fails back to LAN, then Cloudflare, whenever they become reachable again.
  - When a connection is established while the PC reports a managed-but-down tunnel, the app calls `/api/tunnel/quick/restart` to rerun `cloudflared`, then adopts the fresh ephemeral URL from `/api/status` (custom domains are never overridden; the PC's Tailscale URL is auto-adopted after pairing).
  - Added a local **"Enable Tailscale"** notification for when Cloudflare and LAN are both unreachable while off-site (30-minute cooldown, toggleable under Settings → iOS alerts).
  - New Tailscale failover URL field in Connection Setup and Settings, saved connection profiles now persist it, and the routing chip shows LAN / Tunnel / Tailscale states.
- **Web Dashboard Pairing QR Modal**:
  - Displays the detected Tailscale URL alongside the local Wi-Fi and fallback tunnel URLs.
- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.6.8` (iOS Build `21`).

## v3.6.7 (2026-09-28)

### Fixed & Improved
- **PC Setup Wizard & Startup Flow Resolution**:
  - Fixed startup routing in `App.axaml.cs` so that systems with existing accounts in `users.db` bypass the initial account creation wizard and proceed directly to login/auto-login.
  - Automatically marks `ConfigService.MarkSetupComplete()` on update when valid accounts exist, eliminating the setup loop.
  - Enhanced `SetupWizardWindow.axaml.cs` so that if an existing username is entered, it verifies the credentials and logs in rather than throwing a blocking error.
  - Defaulted FTP, SQL, and Mailchimp checkboxes to unchecked in `SetupWizardWindow.axaml`, and auto-disables empty integrations during setup validation so users can complete onboarding without friction.
  - Configured `desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose` to prevent premature process termination during window transitions (`SetupWizardWindow` -> `MainWindow`/`LoginWindow`).
- **Velopack 1.2 Lifecycle & Packaging Alignment**:
  - Upgraded `Velopack` NuGet package to `1.2.158` across `PinayPalBackupManager.csproj`, matching the `vpk` tool version.
  - Converted `Program.Main` from `async Task` to synchronous `void Main`, ensuring `VelopackApp.Build().Run()` is the immediate entry point instruction and avoiding compiler state machine (`MoveNext`) warnings.
  - Positioned `VelopackApp.Build().Run()` ahead of `AppIconHelper.CheckSingleInstanceAndSignalExisting()`, ensuring installer and update hooks (`--veloapp-install`, `--veloapp-updated`) execute without single-instance interference.
  - Added `--runtime win-x64` to `vpk pack` in `.github/workflows/velopack-release.yml`, resolving x86 fallback warnings.
  - Added `--merge` and release cleanup fallback to `vpk upload github` in `.github/workflows/velopack-release.yml` to prevent existing tag collisions.
- **Desktop UI Thread Safety**:
  - Made `UpdateHealthStatus` in `SettingsControl.axaml.cs` thread-safe by wrapping control access in `Dispatcher.UIThread.Post(...)`.
  - Moved `BackupCalendar` control lookup in `HomeControl.axaml.cs` onto the UI thread dispatcher, preventing `Call from invalid thread` exceptions.
- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.6.7` (iOS Build `20`).

## v3.6.6 (2026-09-28)

### Added & Improved
- **Intel Arc & Non-NVIDIA GPU Telemetry Fix**:
  - Enhanced `HardwareTelemetryService.cs` with direct 64-bit VRAM querying from Windows Registry (`HardwareInformation.qwMemorySize`), bypassing the 4GB cap of legacy `Win32_VideoController.AdapterRAM`.
  - Added dedicated GPU performance metrics query via `Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory` to track actual dedicated VRAM usage (MB) across non-NVIDIA GPUs (Intel Arc A380, AMD Radeon, integrated).
  - Added 3D GPU engine utilization tracking via `Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine` and dynamic thermal modeling so GPU temperature and load report live values instead of "N/A" on Web, Desktop, and iOS companion apps.
- **iOS Backup Completion Banner Auto-Dismiss**:
  - In `LiquidDashboardView.swift`, resolved the issue where the backup completion card permanently occupied screen space.
  - Added "COMPLETED" status indicator on successful backup, an immediate dismiss ('X') button, and a 10-second automatic fade/hide timer.
- **iOS Dynamic Island & Live Activities Lifecycle Fix**:
  - Overhauled `BackupLiveActivityManager.swift` state recovery: now filters strictly for truly `.active` activities, pruning stale or ended sessions that previously blocked new Dynamic Island instances from being requested.
  - Added `NSSupportsLiveActivitiesFrequentUpdates` to `PinayPalBackup/Info.plist` and `PinayPalBackupWidgets/Info.plist`.
  - Accelerated active backup synchronization so Dynamic Island and Lock Screen widgets reflect transfer progress in real time.
- **Real-Time Host Hardware & Backup Progress Updates**:
  - **Desktop PC App**: Added dedicated 3-second realtime hardware telemetry timer in `HomeControl.axaml.cs` so CPU, GPU, RAM, load, and temperatures update continuously on the desktop GUI.
  - **Web Dashboard**: Set default polling interval to 3 seconds, with automatic 1.5-second fast polling during active backups.
  - **iOS Companion App**: Increased polling frequency to 2.5 seconds with rapid refresh during active sync routines.
- **Web Dashboard Active Transfer Progress Bar**:
  - Enhanced the active backup banner in `WebDashboardService.cs` with a modern animated gradient progress bar and dynamic percentage indicator.
- **Authentic App Branding & Logo Integration**:
  - Replaced generic emoji shield with the authentic PinayPal app icon from `Assets/logo.ico` across Web Dashboard header and login pages.
  - Updated `ServeLogoAsync` to prioritize `Assets/logo.ico`.
- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.6.6` (iOS Build `19`).


## v3.6.5 (2026-09-28)

### Added & Improved
- **Direct Host PC Hardware & Thermal Telemetry**:
  - Added `HardwareTelemetryService.cs`: Implemented direct, low-overhead hardware telemetry engine to monitor the PC host running the PinayPal Backup engine.
  - **Live CPU Temperature (°C)**: Direct ACPI hardware thermal zone sensor querying (`Win32_PerfFormattedData_Counters_ThermalZoneInformation` / `MSAcpi_ThermalZoneTemperature`) with dynamic thermal load calculation fallback.
  - **Live GPU Temperature (°C) & Diagnostics**: Integrated direct NVIDIA SMI monitor (`nvidia-smi`) querying GPU model name, active GPU temperature (°C), GPU utilization (%), dedicated VRAM allocated/total (MB), and GPU active power draw (Watts), with graceful fallback to `Win32_VideoController`.
  - **Host Memory & Identity**: Added physical host RAM capacity (total/used/free GB), backup manager process working set (MB), host machine name, and OS architecture metrics.
  - Built-in reader-writer cache with 1.8-second TTL ensuring zero redundant process spawns even with multiple concurrent pollers (Web Dashboard, Desktop, iOS Companion).
- **Dedicated Host PC Hardware Cards**:
  - **Web Dashboard**: Added dedicated `🖥️ HOST PC HARDWARE & THERMAL TELEMETRY` card with `SERVER HOST RESOURCE (PC RUNNING BACKUP)` badge, real-time gauges, color-coded temperature badges (`COOL`, `OPTIMAL`, `WARM`, `HOT`), and processor/graphics detail bars.
  - **Desktop PC Home Dashboard**: Added prominent `🖥️ HOST PC HARDWARE & TELEMETRY` card in `HomeControl.axaml` clearly distinguishing PC host resources from backup service statuses.
  - **Desktop PC Health Check Tab**: Extended `HealthCheckControl.axaml.cs` System Resources list with live CPU Temperature and GPU Temperature/VRAM/Power telemetry rows.
  - **iOS Companion App**: Added dedicated `HOST PC HARDWARE & TELEMETRY` Liquid Glass card in `LiquidDashboardView.swift` displaying host PC CPU and GPU temperatures with dual-gauge styling and host badge.
- **REST Hardware Telemetry Endpoints**:
  - Added `GET /api/hardware/telemetry` and `GET /api/hardware` endpoints.
  - Extended `/api/status` JSON payload with a comprehensive `hardware` object for remote monitoring.
- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.6.5` (iOS Build `18`).


## v3.6.4 (2026-09-28)

### Added & Improved
- **Cloudflare Quick Tunnel (On-Demand Temporary Websites)**:
  - Added `CloudflareTunnelService.cs`: Integrated automatic zero-account temporary public website creation (`trycloudflare.com`) via `cloudflared`.
  - Automatic binary discovery across standard install paths (`AppData\Local\Programs\cloudflared`, WinGet, PATH) and automated binary download from official Cloudflare GitHub releases.
  - Spawns and manages `tunnel --url http://localhost:8080 --http-host-header localhost` process with real-time URL capture, stdout/stderr streaming, and graceful shutdown.
  - Added Web Dashboard modal and REST endpoints (`GET /api/tunnel/quick/status`, `POST /api/tunnel/quick/start`, `POST /api/tunnel/quick/stop`) allowing one-click quick tunnel provisioning and termination from any browser or device.
- **Enhanced Dual-Tier QR Pairing & Connection Payload**:
  - Overhauled pairing QR generation in `WebDashboardService.cs` (`ServePairingQrAsync` and `/api/connection-info`).
  - QR payload embeds primary LAN address, all detected local network interfaces (`allLocalUrls`), and active Cloudflare Quick Tunnel URL (`fallbackUrl` / `cloudflareUrl`).
  - Web Dashboard QR modal displays the active Cloudflare fallback URL with instant copy-to-clipboard functionality.
- **Dual-Tier iOS Routing & Auto-Failover**:
  - `PinayPalAPIService.swift` seamlessly handles network transitions: if local Wi-Fi becomes unavailable, network requests automatically fail over to the Cloudflare Tunnel fallback.
  - Periodic background probing gently checks if local LAN has been restored and seamlessly switches back to low-latency local network communication.
  - Added live routing chips to the persistent navigation header (`🟢 LAN` vs `🟣 Tunnel`) with manual switch toggle.
  - Updated `ConnectionSetupView.swift` to automatically capture and configure the Cloudflare fallback URL upon QR scan.
- **iOS Profile Avatar Upload via PhotosPicker**:
  - Added PhotosPicker in `ProfileSheetView.swift` allowing users to select any image from their iOS Photo Library.
  - Directly uploads image data to `/api/user/avatar` with optimistic loading spinner, visual feedback toast, and real-time avatar cache busting.
  - Displayed live profile avatars in `persistentHeader` and `ProfileSheetView` with modern SVG / initials fallback.
- **Multi-Channel Email & Disconnect Alerts**:
  - Upgraded `NotificationService.cs` with full SMTP email alert infrastructure with support for STARTTLS and SSL (ports 587, 465, 25).
  - Added notification triggers for:
    - Connection disconnects (local network drops or Cloudflare Quick Tunnel terminations).
    - Backup routine completion (FTP, SQL, Mailchimp success).
    - Backup failures with detailed error descriptions.
    - Outdated backup routines (>24h since last successful sync).
  - Added "EMAIL & DISCONNECT ALERTS" card to Desktop PC Settings with one-click SMTP presets (Gmail, Outlook/Office365), trigger checkboxes, recipient inputs, and an instant "Send Test Email" button.
  - Added Web Dashboard Email Settings modal with live testing and configuration saving (`GET/POST /api/settings/notifications`, `POST /api/settings/notifications/test-email`).
- **Desktop PC Profile Avatar Persistence Fix**:
  - Fixed issue where desktop profile avatars were not persisted upon exiting or relaunching the application.
  - `AppDataPaths.cs` now properly queries the `Data/` directory for `avatar_{userId}.png` and `avatar.png`.
  - Re-engineered bitmap image loading in `MainWindow.axaml.cs` and `ProfileControl.axaml.cs` using non-locking memory streams (`FileShare.ReadWrite`), eliminating file lock crashes during avatar updates.
  - `AuthService.UpdateAvatar` now updates current in-memory user avatar path and triggers UI update notifications.
- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.6.4` (iOS Build `17`).


## v3.6.3 (2026-09-27)

### Added & Improved
- **120Hz ProMotion Display Performance**:
  - Enabled `CADisableMinimumFrameDurationOnPhone` in `Info.plist`, removing iOS's 60Hz frame rate clamp and unlocking native 120fps ultra-fluid rendering on iPhone ProMotion devices (iPhone 13 Pro through iPhone 17).
  - Optimized SwiftUI animation curves and spring damping for seamless high-refresh-rate interactions.
- **Background Live Activity & Dynamic Island Engine**:
  - Configured `UIBackgroundModes` (`fetch`, `processing`) and `BGTaskSchedulerPermittedIdentifiers` in `Info.plist`.
  - Implemented background execution lifecycle observer and background polling task in `PinayPalAPIService.swift` via `UIApplication.shared.beginBackgroundTask`.
  - Live Activity and Dynamic Island now reliably continue updating in real-time (every 2.5s) while a backup is executing, even when the app is minimized or the iPhone is locked.
  - Automatically terminates background tasks and releases system resources as soon as backup routines complete or emergency stop is invoked.
- **Home Dashboard Real-Time Backup Progress HUD**:
  - Overhauled active backup banner in `LiquidDashboardView.swift` into a prominent, high-precision Liquid Glass Progress HUD.
  - Features real-time percentage indicators (`XX%`), service-specific dynamic icons and accent color themes (SQL Gold, FTP Blue, Mailchimp Purple), and a glowing animated liquid progress bar.
  - Added real-time status text and tactile emergency STOP button with instant feedback.
- **WWDC 2025 Liquid Glass Navigation Island Overhaul**:
  - Re-engineered the iOS navigation bar and persistent header into floating **Liquid Glass Islands** elevated above content.
  - Implemented high-transmittance optical transparency, 135-degree physical specular rim highlights, and multi-tier ambient elevation shadows.
  - Replaced isolated static tab capsules with a continuous fluid sliding active lens using `matchedGeometryEffect`.
  - Added viscous spring physics (`response: 0.35, dampingFraction: 0.72`), tactile haptic detent feedback (`UIImpactFeedbackGenerator(style: .rigid)`), and micro-bounce icon scaling.
  - Added full support for `accessibilityReduceTransparency` fallback rendering.
- **Reimagined Liquid Glass iOS App Icon & App Name**:
  - Changed iOS application display name from **PinayPal** to **PinayPal Backup** across `Info.plist`, `project.pbxproj`, and header views.
  - Redesigned app icon to Apple's WWDC 2025 Liquid Glass specifications: solid porcelain-white 3D volumetric linked "pp" infinity emblem, layered concentric refractive liquid glass rings, 3D mechanical gear, and full-bleed royal purple to indigo gradient backdrop.
  - Generated complete multi-resolution icon suite with explicit iPhone notification (`20x20@2x`, `@3x`), Settings (`29x29`), Spotlight (`40x40`), and App (`60x60`, `1024x1024`) scales.
  - Added dedicated `NotificationLogo.imageset` and updated `NotificationService.swift` with dynamic UUID temp file caching to ensure iOS notification banners always display the latest emblem without file lock collisions.
- **Windows PC & Web Icons Multi-Resolution**:
  - Re-rendered multi-resolution `Assets/logo.ico` (16x16, 24x24, 32x32, 48x48, 64x64, 128x128, 256x256) and `Assets/logo.png` to match the WWDC 2025 Liquid Glass design.
- **Remote Sync Verification & Outdated Detection Ecosystem**:
  - Created `SyncStatusService.cs`: Centralized remote vs local sync verification for FTP, SQL, and Mailchimp. Directly inspects actual disk archive files rather than relying solely on execution history timestamps.
  - Upgraded `ComputeServiceFreshness` in `WebDashboardService.cs`: Accurately marks backups as "Outdated" if local archives are missing, stale (>24h), or older than remote server archives.
  - Added `/api/sync/check` endpoint for on-demand sync verification from Web Dashboard and iOS companion app.
  - Integrated `SyncStatusService.UpdateStatus(...)` into `FtpControl`, `SqlControl`, and `MailchimpControl` on every sync check.
- **iOS App - Outdated Detection, Alerts & Badges**:
  - Implemented automatic outdated backup detection on app launch and periodic status poll.
  - Added high-priority `.timeSensitive` outdated backup alerts with custom badge icons (`NotificationService.sendOutdatedBackupAlert`).
  - Added home screen app icon badge counts (`NotificationService.setBadgeCount`) reflecting the number of outdated backups.
  - Added prominent `outdatedWarningBanner` on the dashboard with a one-tap "Sync Check" trigger.
  - Added dynamic `OUTDATED` / `FRESH` badge pills across carousel cards and service rows.
- **iOS App - Navigation & Automations Overhaul**:
  - Created `AutomationsView.swift`, replacing the main bar's "Logs" tab with a high-value "Automations & Schedules" tab.
  - Features real-time countdown clocks for daily FTP, SQL, Mailchimp, and Health schedules (Manila Time UTC+8).
  - Integrated live remote sync verification card, instant automation triggers, and maintenance policy overview.
- **iOS App - Settings Sheet & Console Redesign**:
  - Moved the Live Logs developer console directly into `ServerConfigSheet.swift` as an embedded diagnostic console.
  - Overhauled Settings category selector: replaced cramped pill scroller with a modern card-based selector featuring rich gradients, shadows, and live badges.
- **iOS App - Light Theme Optimization**:
  - Converted `LiquidTheme.textPrimary` and `LiquidTheme.textSecondary` to dynamic `UIColor`-backed tokens that automatically adapt with crisp contrast in light mode.
  - Eliminated hardcoded `.foregroundColor(.white)` and low-contrast white-on-white backgrounds across Dashboard, History, Settings, and Profile views.
- **Version Bumps**:
  - Bumped PC Desktop App, Web Dashboard API, and iOS Companion App to `3.6.3` (iOS Build `16`).

## v3.6.0 (2026-09-27)

### Added & Improved
- **iOS Live Activity & Dynamic Island Widgets**:
  - Overhauled Live Activity icon with high-end 3D beveled squircle badge plates featuring specular gloss highlights and ambient drop shadows.
  - Implemented dynamic service-specific gradients: Mailchimp electric cyan/blue (`#38BDF8` → `#1D4ED8`) with envelope shield icon, SQL amber gold (`#FBBF24` → `#D97706`) with database stack, and FTP emerald jade (`#34D399` → `#059669`) with globe network symbol.
  - Added live pulsing status indicator during active backups and checkmark seal badge upon completion.
  - Redesigned Lock Screen Live Activity banner with brand header (`PINAYPAL • SERVICE`), glowing multi-stop gradient progress bar, and pill percentage badge.
  - Upgraded Dynamic Island compact leading/trailing, minimal, and expanded views with high-fidelity badges and progress chips.
  - Bumped marketing version to `3.6.0` (Build `14`).
- **iOS Rich Notifications**:
  - Added contextual service emoji badges to backup notifications (`✅ 📬 MAILCHIMP`, `✅ 🗄️ SQL`, `🚨 Low Disk Space`, `🟢 Website Online`).
  - Set `.timeSensitive` interruption levels for failed runs, low disk space warnings, and downtime alerts to break through Focus modes.
- **Desktop PC App**:
  - Upgraded toast notification icon badge in `ToastNotification.axaml` from a 24x24 flat ellipse to a modern 38x38 rounded squircle with drop shadows and white iconography.
  - Implemented dynamic 3D linear gradient backgrounds tailored to notification types (Emerald for FTP, Blue for Mailchimp, Amber for SQL, Crimson for Errors, Gold for Warnings).
  - Bumped desktop app version to `v3.6.0`.
- **Web Dashboard & API**:
  - Added `/api/logo` and `/favicon.ico` endpoints directly serving the official PinayPal emblem with caching headers.
  - Linked brand favicon and apple-touch-icon in both Dashboard and Login HTML heads.
  - Configured browser desktop push notifications with the PinayPal logo icon and badge.
  - Bumped Server API version to `v3.6.0`.

## v3.5.0 (2026-09-27)

### Added & Improved
- **Desktop PC App**:
  - Fixed auto-scan countdown timers freezing upon switching tabs by implementing a unified `OnLoaded` lifecycle re-subscription in `HomeControl`.
  - Fixed auto-scan execution in `BackupManager` by decoupling FTP, Mailchimp, and SQL auto-scan checks into independent triggers.
  - Fixed status bar `Last Health Check: Never` issue by storing in-memory timestamps and falling back to deep log scanning.
  - Enforced 24h freshness checks in FTP and SQL `SyncCheckAsync`, ensuring outdated backups are accurately flagged rather than falsely labeled "LATEST".
  - Aligned desktop Overall Health scoring with Web Dashboard freshness rules, penalizing stale (>24h) and missing backups so 3-day-old backups display "Outdated" instead of "Good".
  - Subscribed individual FTP, Mailchimp, and SQL controls to `OnTimeUpdate` with proper timezone offsets so per-tab timers tick every second.
  - Modernized the Software Update Available modal (`UpdateAvailableDialog`) with dark glassmorphism, version badges, changelog preview, and clear action buttons.
- **Web Dashboard & API**:
  - Added `/api/user/change-username` and `/api/user/change-password` endpoints with validation and session updates.
  - Bumped Server API version to `v3.5.0`.
- **iOS Companion App**:
  - Generated and installed the official 1024x1024 app icon from `Assets/logo.png`.
  - Overhauled Live Activity and Dynamic Island widgets (`BackupLiveActivityWidget`) with service badges, speed/ETA telemetry, and modern progress indicators.
  - Added Profile Avatar button beside Settings gear in navigation header with `ProfileSheetView` for changing username, display name, and password.
  - Added in-app `ChangelogSheetView` accessible from both Settings and Login screens with release notes and version badges.
  - Added animated `SplashScreenView` on launch with breathing logo effects and smooth entrance animation.
  - Fixed carousel pagination dot overlap on backup service cards in `LiquidDashboardView`.
  - Bumped marketing version to `3.5.0` (Build `13`).

## v3.4.0 (2026-09-23)

### Added & Improved
- **iOS Home Tab**:
  - Widened backup carousel cards to `min(UIScreen.main.bounds.width - 64, 340)` and increased pager height to 236pt for optimal card margins and lower pagination dots.
  - Added real-time API latency indicator pill in the navigation header bar.
  - Added global backup freshness summary strip above the service carousel.
  - Added last backup result banner with completion timestamp, status, and duration below active backup banner.
  - Added real-time app uptime metrics in System Status card.
  - Added automated schedule countdown timers in daily schedules card (e.g. "in 2h 15m").
- **iOS Activity Tab**:
  - Added live active queue progress section with instant service feedback.
  - Grouped backup history runs chronologically into Today, Yesterday, and past dates.
  - Added inline retry button for failed backup runs.
  - Added 7-day backup success rate summary card.
- **iOS History Tab**:
  - Added comprehensive history sorting by Recent, Size, Duration, and Failures.
  - Added direct file download button for backup records with downloadable archives.
  - Added toolbar CSV export share sheet and clear history action with confirmation dialog.
- **iOS Settings Tab**:
  - Added full About / App Info section with app version, server API version, latency, diagnostics bundle export, and GitHub release notes link.
  - Added connection profiles manager to save, switch between, and remove server connection configurations.
  - Added notification test panel to fire on-demand test alerts and verify permissions.
  - Added data & storage section showing app document and cache sizes with a cache-clearing action.
- **Web Dashboard**:
  - Added session user display in header (`👤 username`) and one-click session logout button.
  - Added dedicated Connection Health & Diagnostics card with real-time round-trip latency, last poll timestamp, server API version, and expandable/copyable raw diagnostics JSON.
  - Unified API error responses with standardized error codes across all endpoints.
- **Desktop PC App**:
  - Enhanced system tray icon with Quick Backup submenu to trigger FTP, SQL, or Mailchimp backups directly from the notification area.
  - Added real-time backup progress percentage and status tooltip on tray hover during active backups.
  - Added balloon and toast notifications when backups complete or fail.

## v3.3.7 (2026-09-22)

### Fixed
- Centered the iOS Home backup-service pager with one-card snap navigation and page indicators.
- Reserved consistent header clearance across Home, Activity, History, and Logs so the first content never sits beneath the fixed Liquid Glass header.

## v3.3.6 (2026-09-22)

### Added & Improved
- Moved the service launcher to the top of iOS Home as a swipeable Liquid Glass carousel; replaced the duplicate Backups tab with an at-a-glance Activity timeline.
- Fixed cramped header/tab sizing, tightened safe-area spacing, made paired action buttons equal-height, and clarified backup action labels.
- Added real individual Mailchimp export actions for Members, Campaigns, Reports, Merge Fields, and Tags across iOS, the web dashboard, and the desktop-hosted API.
- Expanded activity/recent-run detail, website failure context, and refreshed the Live Activity and alert-test visual language.

## v3.3.5 (2026-09-22)

### Added & Improved
- Added real HTTPS monitoring for `https://pinaypal.net`, including HTTP status, response latency, consecutive failures, recovery detection, desktop alerts, API exposure, and web/iOS status cards.
- Added web-browser and iOS local notifications for observed website outage and recovery transitions, respecting the existing failure/success alert preferences.
- Expanded web FTP, SQL, and Mailchimp cards now include running state, progress, last backup freshness, recent result, and scoped service logs.
- Rebuilt iOS navigation with a fixed Liquid Glass header and custom Home, Backups, History, and Logs tab bar. The Backups tab has expandable service cards with recent results and full service controls.
- Modernized the iOS and web visual palette with midnight navy glass, periwinkle, cyan, lavender, mint, and rose accents.

## v3.3.4 (2026-09-22)

### Fixed
- Hardened the unsigned iOS IPA packaging path for Feather: validate the compiled app icon catalog and embedded Live Activity extension before packaging, then use macOS `ditto` to preserve the IPA bundle metadata.
- Added the required version metadata to the WidgetKit extension so it stays aligned with the host app during sideload signing.
- Optimized the desktop web dashboard for the iOS in-app browser with a compact, touch-friendly single-column layout, safe-area spacing, responsive controls, scrollable tables, and a mobile-sized QR pairing modal.

## v3.3.3 (2026-09-21)

### Fixed
- Unified the backup lifecycle used by the desktop app, web dashboard, and iOS client. FTP, SQL, and Mailchimp now register their own running state, stream progress through the shared tracker, and reliably clear it when the backup reaches a terminal state.
- Replaced the single active-backup slot with per-service tracking, preventing one completed backup from clearing another active service during parallel runs.
- Remote backup requests now reserve the queue atomically, reject overlapping starts with a clear `409 Conflict`, validate the requested service, and return `202 Accepted` only after queuing work.
- Connected the remote iOS/web emergency-stop endpoint to the actual desktop cancellation handlers; it no longer only displays a toast.
- Added API timestamps and an active-service collection so the web dashboard and iOS service detail views display the correct in-progress service.
- Added Live Activity and notification diagnostics, including in-app test controls and clearer permission/error status.

## v3.3.2 (2026-09-21)

### Added & Improved
- Added a WidgetKit Live Activity extension with Lock Screen and Dynamic Island layouts for backup progress, status, speed, and ETA.
- Completed iOS local notification permission, foreground presentation, retry/details actions, deep navigation, and low-disk alert throttling.
- Replaced the custom draggable navigation dock with native SwiftUI tab navigation that adopts the system Liquid Glass treatment on current iOS releases.
- Added expandable FTP, SQL, and Mailchimp service cards in the web dashboard, plus native iOS service detail pages with individual backup controls, scoped logs, status, storage, freshness, and recent history.

## v3.3.1 (2026-09-21)

### Added & Fixed
- Added backup freshness reporting for FTP, SQL, and Mailchimp in the desktop web dashboard and iOS status model, including updated, outdated, and never-backed-up indicators.
- Added LAN listener diagnostics, local IPv4 adapter discovery, URL ACL and Windows Firewall configuration, and the iOS local-network permission message.
- Corrected the iOS QR scanner simulator payload to report the current app version.

## v3.3.0 (2026-09-21)

### Fixed
- **Velopack CI Release Conflict (`vpk pack`)**:
  - Resolved `[FTL] There is a release in channel win which is equal or greater to the current version 3.2.9` by bumping the application version to `3.3.0` across the solution (`PinayPalBackupManager.csproj`, `WebDashboardService.cs`, `project.pbxproj`, and `Info.plist`).
  - Added pre-pack cleanup in `.github/workflows/velopack-release.yml` to remove any conflicting version nupkg assets before packaging.
- **iOS Native App Build Pipeline & Framework Links**:
  - Explicitly linked `AVFoundation.framework` and `UserNotifications.framework` in `PinayPalBackup.xcodeproj/project.pbxproj` build phases.
  - Resolved Swift type conversions (`Int64` to `Double` for disk health) and guaranteed zero compiler warnings/errors for iOS 16.0+ targets.

### Added & Enhanced
- **Light & Dark Mode (Web Dashboard & iOS Native App)**:
  - **iOS App**: Introduced a 3-way Appearance Mode in Settings (`ServerConfigSheet.swift`): **Auto (System)**, **Dark (Obsidian Glass)**, and **Light (Pearlescent Frost Glass)**.
  - **Adaptive Liquid Glass Design System**: Updated `LiquidGlassCardModifier` and `LiquidGlassBarModifier` with dual-mode Fresnel optical underlays, dynamic specular rims, and adaptive color tokens (`LiquidTheme.background()`, `LiquidTheme.surface()`, `LiquidTheme.textPrimary()`).
  - **Dynamic App Hierarchy Scheme**: Bound `.preferredColorScheme(...)` in `MainView.swift` to user's persisted appearance setting with instant preview and tactile feedback.
  - **Web Dashboard**: Added zero-flash Light/Dark mode with dynamic CSS custom properties (`:root` obsidian dark and `[data-theme="light"]` frosted light glass).
  - Added header ☀️/🌙 toggle button with smooth 250ms CSS color transitions and `localStorage` persistence.
- **Actionable Notifications & Sound Alerts**:
  - **iOS Local Push**: Added actionable categories (`RETRY_BACKUP`, `VIEW_LOGS`) to notifications on backup completion or failure.
  - Added pre-backup schedule reminders (15 minutes prior) and daily summary digest at 9:00 PM MNL.
  - Added configurable Low Disk Warning threshold slider (70% to 95%).
  - **Web Audio Chimes**: Synthesized luxury glass multi-tone chimes using the HTML5 Web Audio API (`AudioContext` sine oscillators) for backup triggers, successes, and errors without external audio dependencies.
  - Added HTML5 browser push notifications for web dashboard operations.
- **Live Activities & Dynamic Island Telemetry**:
  - Enhanced `BackupActivityAttributes.swift` and `BackupLiveActivityManager.swift` with real-time transfer telemetry (`speedText`, `etaText`, and service icons).
  - Added a toggle in Settings to control Live Activities on Lock Screen and Dynamic Island.
- **Security & Critical Action Protection**:
  - Added biometric Face ID / Touch ID protection toggle for critical actions (requiring authentication before Emergency Stop, manual backups, or schedule edits).
  - Added Haptic Feedback profile selector: Subtle, Crisp, Heavy, and Off.
- **Dashboard Upgrades**:
  - Added server latency / ping indicator in iOS HUD and Web Dashboard.
  - Storage breakdown visualizer with interactive segment bars.



### Fixed
- **iOS Native Build Failure**:
  - Fixed Swift compiler error `cannot find 'decoded' in scope` in `PinayPalAPIService.swift` line 262 by re-inserting the missing JSON decoding invocation (`try JSONDecoder().decode(StatusResponse.self, from: data)`) in `fetchStatus()`.
  - Fixed compilation issue in `NotificationService.swift` where `UNNotificationSound.defaultCritical` was invoked instead of `.default`.

### Added & Enhanced
- **Apple Liquid Glass Navigation Dock & Interactive Dragging**:
  - Implemented Apple's official Liquid Glass design system (`https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass`) on the iOS bottom navigation dock in `MainView.swift`.
  - **Dynamic Optical Translucency**: High-transmittance `.ultraThinMaterial` foundation, dark Fresnel optical absorption layer, and directional light core.
  - **Fluid Matched Geometry Tab Indicator**: Integrated `matchedGeometryEffect(id: "active_tab_liquid_pill")` to smoothly slide and morph active tab pill highlights across tabs like viscous liquid.
  - **Interactive Dragging Ability**: Attached a fluid drag gesture with viscous rubber-band resistance (`dragOffset`), proportional horizontal/vertical squish-stretch physics (`scaleEffect`), and dynamic specular rim angle rotation that tracks drag vector illumination.
  - **Tactile Physics & Spring Rebound**: Added spring-based snap-back physics (`.spring(response: 0.44, dampingFraction: 0.64)`) and multi-level haptic feedback (`UIImpactFeedbackGenerator`).
  - **Horizontal Flick Tab Switching**: Swiping across the dock smoothly advances or returns between HUD, Snapshots, Console, and Web views.
- **Automated iOS IPA Release Workflow**:
  - Updated `.github/workflows/ios-build.yml` with `permissions: contents: write` and tag trigger (`v*`).
  - Automatically attaches the compiled `PinayPalBackup.ipa` directly to GitHub Releases alongside Windows Velopack installers.

## v3.2.8 (2026-09-21)

### Added
- **Instant QR Code Server Pairing (PC ↔ iOS)**:
  - Added `/api/connection-qr` endpoint in `WebDashboardService.cs` using `QRCoder` to serve a high-density pairing QR code containing local network URL, fallback Cloudflare URL, security PIN, and hostname.
  - Added "📱 Pair iOS App" button and modal in the Web Dashboard.
  - Created native `QRScannerView.swift` utilizing `AVCaptureSession` camera scanning with a targeting reticle, haptic feedback, and simulator fallback.
  - Added a dedicated "Scan QR" tab in `ConnectionSetupView.swift` for one-tap camera discovery and configuration.
- **Dynamic Island & Live Activities (ActivityKit)**:
  - Implemented `BackupActivityAttributes.swift` and `BackupLiveActivityManager.swift` with ActivityKit support for iOS 16.2+.
  - Displays dynamic backup progress, service badge, and status updates in the iPhone Dynamic Island and Lock Screen.
  - Added `NSSupportsLiveActivities` and `NSCameraUsageDescription` to `Info.plist`.
- **iOS Local Push Notifications**:
  - Added `NotificationService.swift` with `UNUserNotificationCenter` integration for background and lock screen notifications on backup completion, backup failure, and low disk space warnings (>88% capacity).
  - Added notification preference toggles in `ServerConfigSheet.swift`.
- **Real-time Active Backup Banners & Dynamic Status**:
  - Added thread-safe `BackupStateTracker.cs` tracking active backup operations, services, progress, and elapsed time.
  - Added pulsing glowing active backup banner in iOS `LiquidDashboardView.swift` with quick Emergency Stop action.
  - Added pulsing active backup banner in the Web Dashboard.
  - Added active backup indicator dots to bottom navigation buttons (HUD & Console) in `MainView.swift`.
- **Official App Logo in iOS Header**:
  - Added `AppLogo.imageset` in `Assets.xcassets` using `Assets/logo.png`.
  - Replaced generic shield SF Symbol in `LiquidDashboardView.swift` with the official PinayPal logo.

### Fixed & Improved
- **PC App Terminal Log Text Visibility**:
  - Fixed an issue where terminal logs in `SqlControl.axaml`, `FtpControl.axaml`, and `MailchimpControl.axaml` were invisible due to Avalonia FluentTheme pointer-over and focus overrides.
  - Enforced high-contrast `#F8FAFC` foreground and caret brush, and added informative watermark placeholders when idle.
- **12-Hour Schedule Formatting**:
  - Converted schedule displays from 24-hour (`22:00 MNL`) to 12-hour AM/PM (`10:00 PM MNL`) across the Web Dashboard, `/api/status`, and iOS views.
  - Updated `ServerConfigSheet.swift` daily schedule pickers and health check pickers to 12-hour format with AM/PM selector.
- **Version Bump**:
  - Bumped version to `v3.2.8` across `PinayPalBackupManager.csproj`, `WebDashboardService.cs`, `project.pbxproj`, and `Info.plist`.

## v3.2.7 (2026-09-21)

### Fixed
- **Setup Wizard "Complete Setup" UI Freezing**:
  - Replaced synchronous `AuthService.Login(username, password)` in `SetupWizardWindow.CompleteSetup()` with non-blocking `await AuthService.LoginAsync(username, password)`.
  - Added `Task.Yield()` prior to user account creation and configuration persistence so the UI thread repaints the button to "Setting up..." immediately.
  - Wrapped `OnSetupComplete` and `Close()` in `Dispatcher.UIThread.Post(...)` to dispatch window transition to `MainWindow` cleanly without blocking or deadlocking the Avalonia dispatcher queue.
  - Added re-entrancy prevention guard (`_isCompletingSetup`) in `SetupWizardWindow` to ignore duplicate clicks.
  - Made `SaveServiceConfigurations()` and `UpdateSummary()` completely null-safe against uninitialized or null checkbox states (`?.IsChecked == true`).
  - Protected `AuthService.Login(...)` synchronous wrapper with `Task.Run(() => LoginAsync(...)).GetAwaiter().GetResult()` and added `.ConfigureAwait(false)` in `AuthService.cs` and `LoginHistoryService.cs` to eliminate SynchronizationContext deadlock risks.

### Maintenance
- **Purged Firebase Realtime Database Users Node**:
  - Deleted all remote user entries under `/users` (`Admin`, `Adminwes`, `User`, `Wesley`, `admin_mobile`, `mobile_admin`, `system`) on `https://pinaypal-backup-manager-default-rtdb.firebaseio.com/` for a completely fresh start.

## v3.2.6 (2026-09-21)

### Fixed
- **Dev PC Initial Setup Invite Code Bypass**:
  - Fixed an issue where the Initial Setup Wizard queried Firebase, detected existing remote admin accounts, and incorrectly converted Step 1 into a restricted "Create User Account" requiring an invite code.
  - Added comprehensive `AuthService.IsDevPC()` detection: checks debugger state, debug paths, developer machine name (`WESLEY`), OS user (`msuda` / `wesley`), source repo structure (`PinayPalBackupManager.csproj`, `.git`), dev environment variables, and persisted `dev_pc.flag`.
  - Updated `SetupWizardWindow.axaml.cs` so on the Dev PC it always presents "Create Administrator Account", hides the invite code field, and creates an active Admin account directly with auto-login.
  - Updated `LoginWindow.axaml.cs` to hide the invite code requirement for Dev PC registrations.

### Added
- **iOS 4-Tab Luxury Liquid Glass Dock**:
  - Expanded iOS bottom navigation from a 2-view switcher into a 4-tab liquid glass dock:
    1. **Liquid HUD**: Real-time system monitoring, one-tap backup triggers, and live hardware dials.
    2. **Snapshots**: Dedicated backup history explorer with search, service filtering (FTP, SQL, Mailchimp, Failed), and detailed snapshot inspector sheets.
    3. **Console**: Real-time streaming terminal log feed with syntax highlighting, search/filter, auto-scroll, and iOS ShareSheet export.
    4. **Web Dashboard**: Embedded high-performance responsive web dashboard view.
- **Master "Backup All Services" Action**:
  - Added a golden action banner on the iOS Dashboard and Web Dashboard to trigger FTP, SQL, and Mailchimp backups sequentially with live haptics and progress feedback.
- **Storage Breakdown Visualizer**:
  - Added multi-segmented liquid storage bar visualizing FTP, SQL, Mailchimp, and drive free space.
- **Wake-on-LAN (WOL) Remote PC Boot**:
  - Added `WakeOnLanHelper` utilizing native `Network.framework` to send UDP magic packets (port 9) to wake up remote Windows backup host PCs from Sleep or Hibernation.
  - Added Wake-on-LAN card with MAC address configuration in iOS Dashboard Settings (`ServerConfigSheet`).

---

## v3.2.5 (2026-09-21)

### Fixed
- **Guaranteed Initial Setup Wizard Launch on Database Reset & Fresh Install**:
  - Disabled automatic silent Firebase user restore in `AuthService.InitializeAsync()` on empty or reset databases, preventing stale remote accounts from bypassing onboarding.
  - Updated `App.axaml.cs` startup pipeline to unconditionally launch `SetupWizardWindow` whenever the local database contains zero users or `IsFirstRun` is true.
  - Added auto-forwarding in `LoginWindow.axaml.cs` so any accidental navigation to Login with an empty database smoothly opens `SetupWizardWindow`.
  - Overhauled [reset_db.bat](file:///e:/Project/pinaypalbackupmanager/reset_db.bat) to terminate any running process, remove database files across all directories, clear session tokens, and reset local configuration flags.
- **Resolved Login & Startup UI Freezing**:
  - Replaced blocking `.GetAwaiter().GetResult()` synchronous login calls in [LoginWindow.axaml.cs](file:///e:/Project/pinaypalbackupmanager/UI/LoginWindow.axaml.cs) with non-blocking `await AuthService.LoginAsync(...)`.
  - Replaced blocking reverse DNS queries (`Dns.GetHostEntry`) in [LoginHistoryService.cs](file:///e:/Project/pinaypalbackupmanager/services/LoginHistoryService.cs) with instant local network adapter inspection.
  - Removed UI disabling lock in [MainWindow.axaml.cs](file:///e:/Project/pinaypalbackupmanager/UI/MainWindow.axaml.cs): sidebar navigation and tabs remain completely interactive and responsive immediately upon login while health checks execute asynchronously.
  - Fixed background system status monitoring in [SystemStatusService.cs](file:///e:/Project/pinaypalbackupmanager/services/SystemStatusService.cs): replaced string-parsed `systeminfo` and child PowerShell processes with instant native .NET APIs (`Environment.TickCount64` and `Process.GetProcesses()`).

---

## v3.2.4 (2026-09-21)

### Added
- **Official Apple Liquid Glass Material Architecture (iOS)**:
  - Upgraded iOS design system to official Apple Liquid Glass specification (`developer.apple.com/documentation/technologyoverviews/liquid-glass`).
  - Implemented dynamic optical high-transmittance material base (`.ultraThinMaterial` / `.thinMaterial`) with continuous concentric corner geometry (`style: .continuous`).
  - Added directional 135° specular rim highlight gradient (`LiquidTheme.specularRimGradient`) that models real micro-bevel glass refraction.
  - Implemented interactive fluid morphing button modifier (`.liquidMorph()`) with spring dynamics and depth deflection.
  - Added floating liquid glass capsule bars, status pills, and backdrop chromatic dispersion layers.
- **Layered Liquid Glass iOS App Icon**:
  - Redesigned app icon adhering to Apple's layered icon specification: deep obsidian ambient base, translucent liquid glass middle shield with 135° specular rim, and crisp solid gold backup emblem.
  - Rendered in 1024x1024 single-size universal appiconset with zero asset compilation warnings.
- **Structured Data Store & Logs Separation**:
  - Restructured storage architecture into distinct folders under the application directory:
    - `AppDir/Data` (`%LOCALAPPDATA%/PinayPal.PinayPalBackupManager/Data`): Houses persistent data stores including `users.db`, `settings.json`, `session.dat`, `config_salt.bin`, `firebase_config.txt`, `update_prefs.txt`, and user avatars.
    - `AppDir/Data/logs` (`%LOCALAPPDATA%/PinayPal.PinayPalBackupManager/Data/logs`): Dedicated logging directory for `system_log.txt`, `startup.log`, and live activity logs.
  - Implemented automatic seamless file migration in `AppDataPaths.MigrateKnownFiles()` ensuring backwards compatibility with existing installations.
- **Multi-Factor Account Recovery (Email + Birthday)**:
  - Enhanced account security by requiring both registered **Email Address** and **Birthday** (`YYYY-MM-DD`) for forgotten username retrieval.
  - Added Email and Birthday input fields to Initial Setup Wizard (`SetupWizardWindow`), User Registration (`LoginWindow`), and Forgot Username dialog.
  - Added Email and Birthday displays in User Profile (`ProfileControl`) with interactive "Edit Email & Birthday" update dialog.
  - Enriched User Management (`UserManagementControl`) list with registered email and birthday columns.
- **User Database Reset & Clean Setup Workflow**:
  - Added "Reset User Database (Initial Setup)" action in Profile Administrator Options.
  - Backs up existing `users.db` with a timestamp, cleans active sessions and cached tokens, re-initializes SQLite schema, resets setup flags, and re-launches the Initial Setup Wizard for fresh onboarding.

---

## v3.2.3 (2026-09-20)

### Fixed
- **iOS IPA Cloud Build Pipeline & Sideloading Compatibility**:
  - **Fixed App Bundle Discovery Failure**: Resolved build failure in GitHub Actions where `find build` returned `No such file or directory` by configuring absolute `SYMROOT` and `BUILD_DIR` paths in `ios-build.yml` and using recursive bundle search (`find . -name "PinayPalBackup.app"`).
  - **Standardized App Icon Catalog**: Transitioned `AppIcon.appiconset` to Apple's modern Single-Size Universal 1024x1024 specification (`AppIcon-1024.png` generated from `Assets\logo.ico`), eliminating Xcode `actool` asset compilation warnings and idiom conflicts.
  - **SwiftUI Biometrics Binding**: Replaced `.onChange(of: enableBiometrics)` with direct `Binding(get:set:)` on the Face ID settings toggle in `ServerConfigSheet.swift`, ensuring 100% compatibility across Swift 5.10, Xcode 15/16, and iOS 17+.
  - **Feather Sideload Ready**: Verified multi-megabyte `.ipa` archive generation with standard `Payload/PinayPalBackup.app` packaging structure for direct import into Feather, AltStore, and TrollStore.

- **Desktop Taskbar Icon & Window Restore Reliability**:
  - **Fixed Windows Taskbar Icon**: Registered explicit `AppUserModelID` (`PinayPal.PinayPalBackupManager`) and injected Win32 `WM_SETICON` (32x32 taskbar & 16x16 titlebar) from `Assets/logo.ico`.
  - **Fixed Window Hidden Permanently on Minimize**: Prevented silent crash in `SetupSystemTray()`, kept taskbar button active on standard minimize (`_`), improved `RestoreFromTray()` foreground window activation, and implemented `EventWaitHandle` single-instance auto-restore listener.

- **Real-Time Remote Settings & Biometrics**:
  - Full biometric Face ID & Touch ID security protection on app launch with in-app test button and immediate lock switch.
  - Real-time synchronization between iOS and Desktop PC for daily backup schedules (FTP, SQL, Mailchimp Manila times), retention periods, and health check automation.

---

## v3.2.2 (2026-09-20)

### Added
- **100% Accurate Hardware Memory & Disk Diagnostics**:
  - Implemented Win32 `GlobalMemoryStatusEx` P/Invoke in `HealthCheckService.cs` to query true installed physical RAM (`ullTotalPhys`, `ullAvailPhys`, `dwMemoryLoad`), replacing the inaccurate GC budget calculation that inflated usage.
  - Added detailed memory breakdown on the Web Dashboard: shows used vs total RAM (e.g. `13.8 GB / 31.8 GB`), free RAM badge (`🟢 18.0 GB Free`), and PinayPal app process memory (`⚡ App: 145 MB`).
  - Added full physical drive scanner (`DriveInfo.GetDrives()`) detecting all mounted partitions (`C:`, `D:`, `E:`, `F:`) with volume labels, free space, and usage bars.
  - Identifies active Backup Drive vs System Drive and displays the exact drive letter, label, and free space (`Drive E: 146 GB Free of 932 GB`).
- **Desktop Taskbar Icon & Window Restore Fixes**:
  - **Fixed Windows Taskbar Icon**:
    - Extracted 256x256 high-resolution PNG (`Assets/logo.png`) from `Assets/logo.ico`.
    - Added `<Content Include="Assets\**"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>` in `PinayPalBackupManager.csproj` so icons are always present in the build and publish output directories.
    - Implemented `AppIconHelper.cs`: registered explicit Windows `AppUserModelID` (`PinayPal.PinayPalBackupManager`), resolving generic/missing taskbar icons.
    - Injected Win32 `WM_SETICON` messages directly to the window handle for both large (32x32 taskbar/Alt-Tab) and small (16x16 titlebar) icons.
  - **Fixed Window Becoming Permanently Hidden on Minimize**:
    - Identified and fixed crash in `SetupSystemTray()` caused by attempting to load `Assets/logo.ico` from disk before it was copied to the output directory, which caused the tray icon to fail silently.
    - Ensured `ShowInTaskbar = true` on standard minimize (`_`), so users can always click the Windows taskbar button to restore the window immediately.
    - Re-engineered `RestoreFromTray()` to properly unhide, restore window state to normal, bring to front (`SetForegroundWindow`), and focus.
    - Added single-instance restore listener (`EventWaitHandle`): launching the application again from the desktop or Start menu automatically signals and pops up the running instance instead of creating duplicates.
  - Fixed issue where backups triggered from the Web Dashboard did not reflect in the desktop application GUI.
  - Dispatched `BackupSchedulingService.BackupExecutor` through `Avalonia.Threading.Dispatcher.UIThread.InvokeAsync` so remote web actions run directly on the UI thread, immediately activating desktop progress bars, status labels, real-time log streaming, and desktop toast notifications.
- **Enriched Web Dashboard UI/UX Overhaul**:
  - **All System Drives & Partitions Card**: visual space consumption bars and statistics for every storage partition on the host machine.
  - **PinayPal Backup Storage Allocation Card**: breakdown of disk space consumed by Website FTP, SQL Database, and Mailchimp backups with file counts and total disk footprint badge.
  - **Automated Backup Schedules Card**: table of daily Manila sync times (FTP 22:00, SQL 17:00, Mailchimp 18:00, Health 08:00) and auto-scan frequency timers.
  - **System Specs & Remote Tunnel Card**: displays machine name, OS description, 64-bit architecture, CPU cores, system uptime, app uptime, and local network IP.
  - **Live Activity Logs Terminal**: dark monospace console stream with real-time log polling, colored level badges (`[INFO]`, `[SUCCESS]`, `[WARN]`, `[ERROR]`), and pause/resume controls.
  - **Enriched Service Cards**: displays host/user credentials, destination folder paths, file counts, folder sizes, and next scheduled sync times.
- **Native iOS App (SwiftUI + Liquid Glass iOS 27 Design System)**:
  - Created complete native iOS application under `ios/PinayPalBackup` built with modern SwiftUI.
  - Implemented **Liquid Glass (iOS 27)** design language: ultra-thin frosted acrylic materials (`.ultraThinMaterial`), iridescent ambient back-glow, chromatic edge specular borders, floating capsule navigation, and interactive haptics.
  - **Biometric Security & Face ID Settings**:
    - Complete Face ID and Touch ID biometric authentication on app launch with dedicated in-app Security settings (`ServerConfigSheet`).
    - Added interactive "Test Face ID Recognition Now" button to verify biometric authentication on demand with instant haptic response.
    - Added "Lock App Now" instant lock trigger and on/off biometric enforcement toggle (`pp_biometrics_enabled`).
  - **Remote PC Management & Real-Time Reflection**:
    - Added `GET /api/settings` and `POST /api/settings` in `WebDashboardService.cs` enabling iOS to remotely configure daily sync schedules (FTP, SQL, Mailchimp Manila times), retention days, and daily health check automation.
    - Changes saved on iPhone dispatch through `Avalonia.Threading.Dispatcher.UIThread` and `ConfigService.TriggerScheduleChanged()`, immediately updating the desktop application GUI, recalculating next runs, and showing desktop notification toasts in real time.
    - Added remote `POST /api/emergency-stop` to halt backups from iPhone with emergency toast notifications.
  - **iOS App Icon & Build Pipeline Fixes**:
    - Standardized `AppIcon.appiconset` to the single-size universal 1024x1024 specification (`AppIcon-1024.png` derived from `Assets\logo.ico`), eliminating Apple `actool` asset compilation warnings and idiom conflicts.
    - Updated `ServerConfigSheet.swift` biometric settings toggle to use explicit `Binding(get:set:)`, resolving Swift 5.10 / Xcode 15+ closure signature mismatches with `.onChange`.
    - Fixed GitHub Actions iOS workflow (`.github/workflows/ios-build.yml`): explicitly set `SYMROOT` and `BUILD_DIR` to absolute paths and modernized bundle discovery (`find . -name "PinayPalBackup.app"`), resolving the build failure where the compiled `.app` bundle was located under `ios/build` instead of root `build`.
  - **Dual Navigation Mode**: Seamless switching between **Native Liquid HUD** (interactive cards, hardware gauges, partition graphs, live terminal logs) and **Live Web Dashboard** (`WKWebView` with native pull-to-refresh).
  - **Automated Cloud CI/CD for Feather Sideloading**: GitHub Actions workflow (`.github/workflows/ios-build.yml`) packages a complete multi-megabyte unsigned `.ipa` with `Payload/PinayPalBackup.app` hierarchy ready for direct import into the Feather app.

### Files Modified
- `PinayPalBackupManager.csproj`
- `CHANGELOG.md`
- `docs/REMOTE_ACCESS_GUIDE.md`
- `services/HealthCheckService.cs`
- `services/WebDashboardService.cs`
- `UI/MainWindow.axaml.cs`

---

## v3.2.1 (2026-09-20)

### Fixed
- **Web Dashboard Dynamic Runtime Activation**:
  - Fixed issue where Web Dashboard port `8080` refused connections if enabled after application startup.
  - Clicking **"Save Web Settings"** or **"Open Web Dashboard"** now immediately boots or restarts the HTTP server dynamically at runtime without requiring an application restart.
  - Added `FileDownloadService.RestartAsync(port)` for dynamic port reconfiguration.
  - Added safe fallback defaults for username and backup paths in `FileDownloadService.StartAsync()` so runtime startup never fails on uninitialized properties.

### Files Modified
- `PinayPalBackupManager.csproj`
- `CHANGELOG.md`
- `services/FileDownloadService.cs`
- `UI/UserControls/SettingsControl.axaml.cs`

---

## v3.2.0 (2026-09-20)

### Added
- **Web-Based Dashboard & Remote Access**:
  - Built-in embedded Cyberpunk dark-slate Web Dashboard accessible from any web browser (`services/WebDashboardService.cs`).
  - Optional PIN authentication (`RequireAuth` and `WebPin`).
  - Full REST API endpoints: `/api/status`, `/api/health`, `/api/health/run`, `/api/history`, `/api/backup/{service}`.
  - Direct backup download links via `/download/{service}/{filename}`.
  - Added Web Dashboard configuration card and "Open Web Dashboard" button in Settings Control.
  - Added comprehensive `docs/REMOTE_ACCESS_GUIDE.md` documenting Cloudflare Tunnel (Quick and Named Tunnels) and Tailscale setup.
- **TLS Certificate Auto-Recovery & Probing**:
  - Added `ScanTlsFingerprint(host, port)` in `FtpService.cs` and `SqlService.cs` using WinSCP's `Session.ScanFingerprint`.
  - Automatic detection and recovery when FTPS server certificate rotates: probes new fingerprint, updates config, saves credentials, notifies via toast, and auto-retries connection without manual WinSCP GUI intervention.
  - Added "Auto-Fetch" button in Credentials Dialog to automatically retrieve TLS certificate fingerprint directly from the server.
  - Added `AcceptAnyTlsCert` toggle option in Settings for environments with self-signed certificates.
- **Persistent & Scheduled Health Checks**:
  - Persisted health check results to `health_check_result.json` in LocalApplicationData so historical diagnostics show immediately on app launch.
  - Added automated daily scheduled health check (`DailyHealthCheckEnabled`, `DailyHealthCheckHour`).
  - Fixed "Last check: Never" display issue and fixed UI text clipping for "Operational".
- **Structured Backup History & Checksum Automation**:
  - Connected `FtpControl`, `SqlControl`, and `MailchimpControl` to automatically record backup starts, durations, file counts, and sizes in `BackupHistoryService`.
  - Automated checksum calculation via `ChecksumService.SaveChecksumsForFolderAsync()` upon backup completion.

### Fixed
- **System Tray & Window Minimize**:
  - Fixed window minimize crash by safely hiding from taskbar (`ShowInTaskbar = false; Hide()`) when `MinimizeToTray` is enabled.
  - Added `CloseToTray` option to minimize on window close button.
  - Added safe `RestoreFromTray()` to restore window state smoothly without layout crashes.
- **Backup Schedule Engine**:
  - Replaced mock `Task.Delay(2000)` simulation in `BackupSchedulingService` with real backup execution routines.
  - Removed duplicate event handler subscription on schedule type combo box in `BackupScheduleControl`.
  - Removed dummy sample schedules and fake history data generators that contaminated real data.
- **Verification & Statistics Tabs**:
  - Fixed missing column headers in Verification DataGrid.
  - Removed dummy `test_file.zip` from Verification Control.
  - Fixed status filter to properly include all error, corrupted, and mismatched states.
  - Updated Statistics tab to prioritize structured backup history.
- **Compiler Warnings**:
  - Resolved all 24 CS1998 compiler warnings across services and controls for a clean 0-warning build.

### Files Modified
- `PinayPalBackupManager.csproj`
- `CHANGELOG.md`
- `services/AppSettings.cs`
- `services/ConfigService.cs`
- `services/FtpService.cs`
- `services/SqlService.cs`
- `services/HealthCheckService.cs`
- `services/BackupManager.cs`
- `services/BackupSchedulingService.cs`
- `services/BackupHistoryService.cs`
- `services/FileDownloadService.cs`
- `services/WebDashboardService.cs` [NEW]
- `docs/REMOTE_ACCESS_GUIDE.md` [NEW]
- `UI/MainWindow.axaml.cs`
- `UI/LoginWindow.axaml.cs`
- `UI/SetupWizardWindow.axaml.cs`
- `UI/UserControls/CredentialsDialog.axaml`
- `UI/UserControls/CredentialsDialog.axaml.cs`
- `UI/UserControls/SettingsControl.axaml`
- `UI/UserControls/SettingsControl.axaml.cs`
- `UI/UserControls/HealthCheckControl.axaml.cs`
- `UI/UserControls/BackupScheduleControl.axaml.cs`
- `UI/UserControls/VerificationControl.axaml`
- `UI/UserControls/VerificationControl.axaml.cs`
- `UI/UserControls/StatisticsControl.axaml.cs`
- `UI/UserControls/FtpControl.axaml.cs`
- `UI/UserControls/SqlControl.axaml.cs`
- `UI/UserControls/MailchimpControl.axaml.cs`
- `UI/UserControls/ErrorReportViewerControl.axaml.cs`
- `UI/UserControls/HomeControl.axaml.cs`
- `UI/UserControls/PerformanceMetricsControl.axaml.cs`
- `UI/UserControls/UserManagementControl.axaml.cs`
- `UI/UserControls/BackupHistoryControl.axaml.cs`
- `services/AuthService.cs`
- `services/ErrorReportingService.cs`
- `services/FirebaseUserService.cs`
- `services/RealtimeMonitoringService.cs`
- `services/SystemMonitorService.cs`
- `services/SmartOperationsService.cs`
- `services/SystemStatusService.cs`

---

## v3.1.3 (2026-06-06)

### Fixed
- **Minimize Crash**: Fixed application crash when minimizing the main window
  - Simplified `OnWindowStateChanged` to only handle maximized/normal states
  - Removed `SetMaximizedLayout` call on `_homeControl` which was causing the crash
  - Disabled `MinimizeOwnedDialogs` to prevent dialog-related crashes

### Files Modified
- UI/MainWindow.axaml.cs

---

## v3.1.2 (2026-06-03)

### Fixed
- **App Crash on Minimize**: Fixed application crash when minimizing the main window
  - Added try-catch block in `OnWindowStateChanged` to prevent unhandled exceptions
  - Added try-catch block in `MinimizeOwnedDialogs` to handle errors when minimizing dialog windows
  - Added check for `window.PlatformImpl == null` to skip already disposed windows
  - Added error logging for debugging minimize-related issues

### Files Modified
- UI/MainWindow.axaml.cs

---

## v3.1.1 (2026-05-29)

### Fixed
- **Home Dashboard Display Issues**: Fixed system status indicators showing default placeholder values ("-")
  - Changed `Name` to `x:Name` for SystemUptime, LastHealthCheck, ActiveProcesses, StorageUsage TextBlock controls
  - Updated HomeControl.axaml.cs to use generated x:Name fields instead of FindControl
  - Changed `AvaloniaXamlLoader.Load(this)` to `InitializeComponent()` to ensure x:Name fields are properly populated by generated code
  - Fixed `OnLoaded` to restart all timers (dashboard refresh, health auto refresh, stats auto refresh, error refresh) not just active process timer
  - Rewrote `UpdateSystemStatusAsync` to be more defensive with individual try-catch blocks and `Dispatcher.UIThread.Post` for each field
  - Added diagnostic logging to verify x:Name field population

### Files Modified
- UI/UserControls/HomeControl.axaml
- UI/UserControls/HomeControl.axaml.cs
- PinayPalBackupManager.csproj

---

## v3.1.0 (2026-05-27)

### UI Improvements
- **Responsive Dashboard Cards**: Backup service cards now adapt to window size
  - Changed from fixed `Grid` to `WrapPanel` for automatic wrapping
  - Cards now have `MinWidth="240"` and `MaxWidth="300"` for flexible sizing
  - Cards are centered when wrapping to new rows
- **Wider Default Window**: Increased default window width to accommodate 4 cards
  - Default width: `1060` → `1320` pixels
  - Minimum width: `780` → `1080` pixels
  - Fits all 4 backup cards (FTP, Mailchimp, SQL, Network Drive) in one row

### Files Modified
- UI/MainWindow.axaml
- UI/UserControls/HomeControl.axaml

---

## v3.0.0 (2026-05-27)

### Major Release
- **Dialog System Overhaul**: Complete rewrite of dialog behavior across the entire application
  - All dialogs now properly minimize with main window
  - Dialogs no longer stay on top of other applications
  - Modern title-bar-less design for all dialogs
  - Proper z-order management with parent window activation

### All Changes from v2.3.0
- **Verification Freezing**: Fixed "Verify All" button freezing the PC app
  - Added `Task.Yield()` to prevent UI thread blocking
  - Wrapped verifications in `Task.Run()` for thread pool execution
  - Limited issue alerts to 50 max to prevent UI freeze with thousands of corrupted files
  - Added "... and X more" indicator for exceeded limits

### Dialog System Improvements
- **Fixed Dialog Z-Order**: All popup dialogs now properly appear on top of parent window
  - Removed `Topmost = true` from all dialogs (prevents dialogs staying on top of other apps)
  - Added `parentWindow.Activate()` before `ShowDialog()` calls
  - Dialogs minimize with main window and restore properly
- **Modern Dialog Styling**: Removed title bars from all dialogs for cleaner look
  - Added `SystemDecorations = SystemDecorations.None`
  - Added `ExtendClientAreaToDecorationsHint = true`
- **Fixed Missing Dialogs**: 
  - System Info dialog
  - Change Password dialog
  - Change Username dialog
  - Login History dialog
  - Two Factor Auth dialog
  - Logout Confirmation dialog

### Performance & Stability
- **Fixed Synchronous I/O**: Changed `Write()` to `WriteAsync()` in `FileDownloadService`
- **Fixed Async Context**: Changed `CheckIntegrity` to async method with `ReadAsync()`
- **Fixed Timer Memory Leaks**: 
  - Added proper event unsubscription before disposing timers
  - Fixed `BackupRetentionService` timer cleanup
  - Fixed `BackupRetryService` timer cleanup
  - Fixed `HomeControl` timer cleanup (5 timers)
- **Fixed Null Reference Issues**: Added null checks for UI controls in `VerificationControl`

### Code Quality
- **Fixed Async Patterns**: 
  - Removed `async void` from `RealtimeMonitoringService.CheckAlerts`
  - Removed `async void` from `NotificationService.ProcessNotificationQueue`
- **Fixed Database Connections**: Removed duplicate `connection.OpenAsync()` calls in `PasswordResetService`
- **Fixed JSON Serialization**: Added missing `JsonIgnore` using directive in `VerificationHistoryService`

### Files Modified
- Services/RealtimeMonitoringService.cs
- Services/VerificationHistoryService.cs
- Services/PasswordResetService.cs
- Services/FileDownloadService.cs
- Services/BackupRetentionService.cs
- Services/BackupRetryService.cs
- UI/MainWindow.axaml.cs
- UI/UserControls/VerificationControl.axaml.cs
- UI/UserControls/ConfirmDialog.axaml.cs
- UI/UserControls/ProfileControl.axaml.cs
- UI/UserControls/SettingsControl.axaml.cs
- UI/UserControls/UserManagementDialog.axaml.cs

---

## v2.3.0 (2026-05-27)

### Major Fixes
- **Verification Freezing**: Fixed "Verify All" button freezing the PC app
  - Added `Task.Yield()` to prevent UI thread blocking
  - Wrapped verifications in `Task.Run()` for thread pool execution
  - Limited issue alerts to 50 max to prevent UI freeze with thousands of corrupted files
  - Added "... and X more" indicator for exceeded limits

### Dialog System Improvements
- **Fixed Dialog Z-Order**: All popup dialogs now properly appear on top of parent window
  - Removed `Topmost = true` from all dialogs (prevents dialogs staying on top of other apps)
  - Added `parentWindow.Activate()` before `ShowDialog()` calls
  - Dialogs minimize with main window and restore properly
- **Modern Dialog Styling**: Removed title bars from all dialogs for cleaner look
  - Added `SystemDecorations = SystemDecorations.None`
  - Added `ExtendClientAreaToDecorationsHint = true`
- **Fixed Missing Dialogs**: 
  - System Info dialog
  - Change Password dialog
  - Change Username dialog
  - Login History dialog
  - Two Factor Auth dialog
  - Logout Confirmation dialog

### Performance & Stability
- **Fixed Synchronous I/O**: Changed `Write()` to `WriteAsync()` in `FileDownloadService`
- **Fixed Async Context**: Changed `CheckIntegrity` to async method with `ReadAsync()`
- **Fixed Timer Memory Leaks**: 
  - Added proper event unsubscription before disposing timers
  - Fixed `BackupRetentionService` timer cleanup
  - Fixed `BackupRetryService` timer cleanup
  - Fixed `HomeControl` timer cleanup (5 timers)
- **Fixed Null Reference Issues**: Added null checks for UI controls in `VerificationControl`

### Code Quality
- **Fixed Async Patterns**: 
  - Removed `async void` from `RealtimeMonitoringService.CheckAlerts`
  - Removed `async void` from `NotificationService.ProcessNotificationQueue`
- **Fixed Database Connections**: Removed duplicate `connection.OpenAsync()` calls in `PasswordResetService`
- **Fixed JSON Serialization**: Added missing `JsonIgnore` using directive in `VerificationHistoryService`

### Files Modified
- Services/RealtimeMonitoringService.cs
- Services/VerificationHistoryService.cs
- Services/PasswordResetService.cs
- Services/FileDownloadService.cs
- Services/BackupRetentionService.cs
- Services/BackupRetryService.cs
- UI/MainWindow.axaml.cs
- UI/UserControls/VerificationControl.axaml.cs
- UI/UserControls/ConfirmDialog.axaml.cs
- UI/UserControls/ProfileControl.axaml.cs
- UI/UserControls/SettingsControl.axaml.cs
- UI/UserControls/UserManagementDialog.axaml.cs

---

# Previous Versions

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0/).

## [2.20.0] - 2026-05-25

### Added
- **Custom Close Buttons**: Modern close button (X) added to all management tool dialogs
  - Health Check, Error Reports, Performance Metrics, Backup History, Backup Schedules
  - Each dialog now uses `SystemDecorations=None` with a custom styled close button in the header
  - Close buttons use theme-aware colors with hover effects

### Changed
- **UI Animations**: Smoother transitions across the entire application
  - Increased animation durations for buttons, borders, text, and cards
  - Added `CubicEaseOut` and `SineEaseOut` easing to all transitions for more fluid motion
  - Sidebar buttons: stronger hover scale (`1.05`), gentler press (`0.96`)
  - Dashboard cards: lift on hover with `translateY(-3px)` and deeper shadows
- **Toast Notifications**: Moved from top-right to bottom-right corner for less intrusive positioning

### Fixed
- **Network Drive Progress Bar**: Mirror progress bar section now stays visible when idle (matching global backup progress behavior)
- **Sidebar Branding**: Updated sidebar logo text from "PinayPal" to "PinayPal Backup Manager"

## [2.19.0] - 2026-05-24

### Added
- **Modern Color Palette**: Complete overhaul of dark and light theme colors
  - Dark theme: Deep Space palette (`#02040A` bg) with vibrant accents (Emerald `#34D399`, Sky Blue `#60A5FA`, Amber `#FBBF24`, Violet `#A78BFA`, Rose `#FB7185`)
  - Light theme: Clean Slate palette (`#F8FAFC` bg) with matching accent tones
  - New glow colors (`Accent*Glow`) for subtle elevation effects on cards and badges
- **Card Hover Effects**: Service cards now lift on hover with `translateY(-2px)` and deeper shadows
- **Button Animations**: `service-action` buttons scale and lift on hover/press
- **Accent Icon Badges**: Each service card has a colored background behind its icon (e.g., emerald bg for FTP)
- **Sidebar Arrow Fix**: Toggle arrow direction reversed — compact points right (expand), expanded points left (collapse)

### Changed
- **Dashboard UI Modernization**:
  - All cards upgraded to `CornerRadius="14-16"` with soft `BoxShadow` elevation
  - Service cards: larger icons (`20x20`), accent-colored icon backgrounds, more spacing
  - Quick action buttons use `service-action` style with hover animations
  - Mirror buttons styled with violet accent theme
  - "Open Tab" buttons use service-specific accent backgrounds (`AccentFtpBg`, `AccentMailchimpBg`, etc.)
  - Header: larger title (`26px`), improved badge shadows
- **Top Bar Modernization**: All badges upgraded to `CornerRadius="12"`, subtle elevation shadows
- **Sidebar Modernization**: Softer shadow (`8px offset, 40px blur`), larger logo area
- **ProgressBar**: Slimmer (`8px`), smoother transitions (`0.25s`)
- **Theme Toggle Relocation**: Moved from sidebar to top bar beside notification bell

### Fixed
- **Sidebar Compact Alignment**: Sidebar now sticks to left edge in compact mode by synchronizing Border width and parent Grid column width
- **Sidebar Startup Size**: Sidebar now correctly matches expanded width on app startup
- **Stuck Progress Safeguard**: Added 120-second stuck detection for backup progress bars that hang on non-zero values
- **Mirror Progress Reset Race Condition**: Fixed `ResetGlobalBackupProgressIfIdle` race causing mirror progress bar reset failure

## [2.18.0] - 2026-05-21

### Fixed
- **Memory Leaks**: Fixed 11 event subscription leaks across the app
  - `HomeControl` — added missing unsubscriptions for `_manager` and `AuthService.OnUserChanged` in `OnUnloaded`
  - `MainWindow` — stored FirebaseRemoteService delegates and unsubscribed on window close
  - `FtpControl`, `MailchimpControl`, `SqlControl` — stored `LogService.OnNewLogEntry` delegates and unsubscribed in `OnUnloaded`
  - `ProfileControl` — stored `AuthService.OnUserChanged` delegate and unsubscribed in `OnUnloaded`
- **Crash Protection**: Added try-catch to 6 `async void` methods to prevent unhandled exceptions crashing the app
  - `LoginWindow.OnVerify2FAClick`, `OnRegisterClick`
  - `SetupWizardWindow.OnNextClick`, `BrowseImportFile`, `BrowseFolder`
  - `NotificationItem.OnPointerReleased`
- **Retry Timer**: Fixed `BackupRetryService.RegisterFailure` not rescheduling `NextRetryTime` when a service fails again while already in queue
  - Added `Reschedule()` for when retry is skipped due to busy controls
  - Fixed `CheckRetriesAsync` race condition by locking queue during entry modification
- **Progress Bar Reset**: Completed backups/mirrors now reset to idle after 5 seconds instead of 60

### Changed
- **Dead Code Removal**: Removed unused timer fields (`_autoPingTimer`, `_statsTimer`, `_scheduleTimer`, `_storageTimer`) from `HomeControl`
- **Dead Code Removal**: Removed unused `_toastTimer` field and `#pragma warning disable CS0649` blocks from `MainWindow`
- **Race Condition**: Fixed `HttpClientFactory.EnforceRateLimitAsync` reading `_lastRequest` outside the lock after setting it inside
- **Debug Cleanup**: Removed all `Console.WriteLine` debug output from `LoginWindow`, `App.axaml.cs`, and `HttpClientFactory`

## [2.17.5] - 2026-05-20

### Added
- **Setup Wizard Post-Login Mode**: After a standard user registers and logs in for the first time on a new PC, the setup wizard now opens to help configure backup services
  - New `Step1AppInfo` panel shows a welcome overview of app features (FTP, SQL, Mailchimp, encryption, Firebase sync)
  - New `_isPostLoginMode` flag and `SetPostLoginMode()` method on `SetupWizardWindow`
  - Step 1 shows app info instead of admin account creation when in post-login mode
  - User creation logic in `CompleteSetup()` is skipped — only service configurations are saved

### Changed
- **App.axaml.cs First-Run Flow**: When users exist from Firebase on a fresh install, the app now shows login first instead of marking setup complete immediately
  - `ConfigService.MarkSetupComplete()` is no longer called prematurely when users are pulled from Firebase
  - After successful first login, `ShowSetupWizardPostLogin()` is called if `IsFirstRun()` is still true
  - Post-login wizard routes to `MainWindow` on completion (no logout/login cycle needed)
- **Import Skip in Post-Login Mode**: When settings are imported during post-login wizard, backup tabs are hidden and step indicators collapse exactly the same as in admin mode

## [2.17.0] - 2026-05-20

### Added
- **Setup Wizard Import Configuration Step (Step 2)**: New screen between admin creation and backup tabs to bulk-import all service settings from a single file
  - Supports `.ppenc` encrypted files (exported from Credentials Dialog) and plain `.json` files
  - File picker filters for `.ppenc` and `.json` with "Browse" and "Import All Settings" actions
  - Uses the same hardcoded AES key as `CredentialsDialog` for `.ppenc` decryption (cross-PC compatible)
  - Imports FTP, SQL, and Mailchimp fields in one operation

### Changed
- **Imported Settings Navigation Optimization**: When settings are successfully imported, backup tab steps (FTP/SQL/Mailchimp) are hidden entirely
  - Step indicators collapse from 6 dots to 3 dots (Admin → Import → Summary)
  - Step title renumbers to "Step X of 3"
  - Next/Back/Skip buttons bypass hidden steps automatically
  - User goes straight from Import to Security & Summary without seeing empty backup tabs
- **Removed Per-Tab Import Buttons**: The individual "Import from appsettings.json" buttons previously on FTP, SQL, and Mailchimp wizard tabs have been removed in favor of the centralized Step 2 import

### Fixed
- **`.ppenc` Decryption in Setup Wizard**: Previously used `ConfigEncryptionService.TryDecrypt()` which relies on a machine-specific key, causing decryption failures on different PCs. Now uses the correct hardcoded AES key (`PinayPalBackupManagerKey2024!`) matching the Credentials Dialog export format

## [2.16.8] - 2026-05-19

### Added
- **Setup Wizard Firebase Admin Detection**: The setup wizard now queries Firebase on startup to determine if an admin already exists
  - If Firebase already has an admin → wizard switches to "Create User Account" mode with invite code requirement
  - If Firebase is empty → wizard stays in "Create Administrator Account" mode (no invite code needed)
  - Prevents accidental creation of multiple admin accounts on secondary PCs

### Changed
- **Setup Wizard Account Creation Logic**:
  - Admin PC: creates account as `Role: Admin, Status: Active` via `AuthService.CreateUser()` and auto-logs in
  - Non-admin PC: validates invite code via Firebase, creates account as `Role: User, Status: Pending`, marks invite as used, and redirects to login screen
- **Password Validation in Setup Wizard**: Now enforces full complexity rules (uppercase, lowercase, digit, special character) matching `AuthService.CreateUser`
- **App.OnSetupComplete**: Routes to `MainWindow` if a user is logged in, otherwise routes to `Login` screen (for pending accounts)

## [2.16.5] - 2026-05-19

### Added
- **Cross-PC User Sync via Firebase**: Installing the app on a new PC now automatically pulls existing users from Firebase
  - New `FirebaseUserService.PullUsersFromFirebaseToLocalAsync()` fetches all users and writes them to the local SQLite DB
  - Users pulled from Firebase retain their passwords, roles, and statuses — login works immediately without re-creating accounts
  - If Firebase already contains users on a fresh install, the setup wizard is skipped and the login screen is shown directly
- **FirebaseUserData Password Fields**: Added `PasswordHash` and `Salt` properties to the internal `FirebaseUserData` DTO so password hashes survive round-trips between local DB and Firebase

### Fixed
- **Setup Wizard First Run**: Fixed `App.ShowSetupWizard` which was previously bypassing the wizard entirely and marking setup as complete before the user could create an admin account
- **Registration Deadlock**: Fixed `LoginWindow` and `SetupWizardWindow` calling the synchronous `AuthService.Register()` wrapper on the UI thread, causing a deadlock while awaiting Firebase invite-code validation

## [2.16.0] - 2026-05-19

### Added
- **Admin Direct User Creation**: Admins can now create users directly without requiring invite codes
  - New "Create User" button in User Management dialog
  - Inline popup dialog with username, password, confirm password, role selector (User/Admin), and status selector (Active/Pending/Disabled)
  - Strong password validation enforced (8+ chars, uppercase, lowercase, digit, special character)
  - New `AuthService.CreateUser()` method with full validation and Firebase sync

### Changed
- **Invite Code Security**: Uses CSPRNG (`RandomNumberGenerator`) instead of `Random()` for cryptographically secure code generation
- **Invite Code Auto-Cleanup**: Generating a new invite code now automatically deletes all old codes, keeping only the latest one
  - New `FirebaseInviteService.CleanupAllCodesExceptAsync()` method
- **Logging Consistency**: All `Console.WriteLine` statements in `FirebaseInviteService` replaced with `LogService.WriteLiveLog` for proper audit trail
- **Registration Async**: `AuthService.Register` renamed to `RegisterAsync` with proper async signature

## [2.15.0] - 2026-05-19

### Added
- **Internet Connectivity Monitoring**: Detects when the PC has no internet connection and shows a visible offline banner
  - New `NetworkConnectivityService` polls every 15 seconds using `NetworkInterface.GetIsNetworkAvailable()` + ping to Cloudflare/Google DNS
  - Pink offline banner appears below the top bar when connection is lost
  - Top-bar connection indicator dot switches to red and shows "Offline"
- **Offline Tab Protection**: Automatically disables internet-required sidebar tabs when offline
  - Affected tabs: FTP, Mailchimp, SQL, Health Check, Performance Metrics
  - Disabled buttons show reduced opacity (0.4) and cannot be clicked
  - Attempting to click an offline-required tab shows a toast warning instead of switching
  - If the user is on an internet-required tab when connection drops, the app auto-redirects to the Dashboard

### Changed
- **Home Dashboard Optimizations**: Cached FindControl references to avoid repeated visual-tree lookups on every dashboard tick
- **Refresh Intervals**: Split dashboard refresh timers by cost — uptime every 30s, logs every 60s, storage every 5 minutes
- **Fire-and-Forget Safety**: Added `FireAndForget` helper to wrap `_ = Task.Run(...)` calls with exception logging
- **Mailchimp Column Width**: Widened Mailchimp label column from 65 to 75 in dashboard time-since-last-backup grid

### Fixed
- **Empty Catch Blocks**: Audited 7 empty catch blocks in `AuthService.cs` and added debug logging for Firebase sync, config read, and audit logging failures

## [2.13.8] - 2026-05-17

### Fixed
- **Theme Toggle Crash**: Fixed `InvalidCastException` when switching between light/dark themes by explicitly casting `int` theme settings (`FontSize`, `UIScale`, `BorderRadius`) to `double` before storing in Avalonia resources
- **Theme Customizer Dialog**: Removed double border by setting `SystemDecorations=None` and removing conflicting `ExtendClientArea` properties; dialog is now non-resizable
- **Horizontal Scrollbar Overflow**: Reverted `ScrollViewer` horizontal scrollbar from `Auto` to `Disabled` across all 10+ tab controls to prevent star-column sizing issues and layout breakage
- **Verification Tab Overlap**: Fixed overlapping progress bars in `VerificationControl.axaml` by adding `ClipToBounds=True` and `HorizontalAlignment=Stretch`
- **Statistics Chart Clipping**: Added `ClipToBounds=True` to chart container `Border` and inner `Canvas` to prevent line overflow
- **MainWindow Responsiveness**: Lowered `MinWidth`/`MinHeight` to allow smaller window sizes

### Changed
- **Home Dashboard Layout** (`HomeControl.axaml`):
  - "Time Since Last Backup" left column now uses `Auto` sizing with `VerticalAlignment=Bottom`
  - Time values are now right-aligned at a consistent horizontal position across all 3 services
  - Increased font sizes for title, service names, and time values for better readability
  - Added spacing between service names and time values
  - Added new **"Backup Summary"** section filling empty space with:
    - Services Monitored count
    - Next Backup In countdown
    - Overall Health status (Good/Fair/Poor with color coding)
  - "Global Backup Progress" right panel is now vertically centered with increased spacing

## [2.13.2] - 2026-05-17

### Fixed
- **White Theme**: Fixed hardcoded dark colors across all 13 UserControls to use theme-aware `DynamicResource` bindings
- **Card Borders**: Added visible `BorderBrush`/`BorderThickness` to all cards in Home and Settings tabs so they no longer blend into the background
- **Theme Toggle Refresh**: Added explicit background refresh for Window, Sidebar, TopBar, and StatusBar on theme change

## [2.12.0] - 2026-05-17

### Added
- **Dashboard Greeting**: Dynamic time-based greeting in Home Dashboard header ("Good Morning/Afternoon/Evening, Username") using Manila time

### UI
- **Consistent Tab Headers**: Added uniform title + subtitle headers with dark rounded borders across all tabs:
  - FTP, Mailchimp, SQL backup tabs
  - Verification, Statistics, Settings, Profile tabs
  - Home Dashboard tab with "DASHBOARD" title and personalized greeting
- **Visual Polish**: Fixed content margins so cards no longer touch tab edges; removed duplicate old dashboard header

### Fixed
- **Version Badge**: Updated stale version display from v2.9.8 to match current release

## [2.11.5] - 2026-05-17

### Security
- **AES-256 Encryption**: Replaced hardcoded salt with random per-install salt; added key caching for performance
- **Password Hashing**: Removed insecure SHA256 and PBKDF2 fallbacks — BCrypt only (legacy users will need password reset)
- **Path Traversal**: Hardened filename validation in `FileDownloadService` with `Path.GetFileName`, whitelist regex, and URL-encoded traversal blocking
- **Secure RNG**: Replaced `new Random()` with `RandomNumberGenerator` for invite code generation

### Fixed
- **SQL Authentication**: Fixed encrypted Base64 password string being passed to WinSCP when decryption failed
  - Added `TryDecrypt` to `ConfigEncryptionService` with proper error handling
  - `SecurityService` now returns empty string and logs error instead of passing encrypted text as password
  - Added diagnostic logging to show which salt path succeeds (random vs legacy)
- **WinSCP Session Disposal**: Fixed `Session is already opened` crash when disposing `SqlService`/`FtpService`
  - Wrapped `FileTransferProgress` event unsubscription and `Dispose()` in try/catch blocks
- **Dialog Clipping**: Fixed Performance Metrics and Backup Schedules popup content being cut off
  - Removed `ExtendClientAreaToDecorationsHint` and `ExtendClientAreaChromeHints` that were eating usable space
  - Changed control roots from `StackPanel` to `Grid RowDefinitions="Auto, *"` so `ScrollViewer` fills remaining space
  - Set `SystemDecorations = BorderOnly` and solid background on popup windows
- **MessageBox Resizing**: Task Complete dialogs are now non-resizable (`CanResize = false`)

### UI
- **Recent Activity**: Removed Refresh button — now auto-refreshes when new system log entries arrive (throttled to 2 seconds)
- **Daily Schedule**: Countdown replaced with next scheduled backup time in 12-hour AM/PM format (e.g., `2:30 PM`)
- **Backup Health**: Removed critical alerts error list from dashboard; kept alert count badge only
- **Recent Errors**: Fixed layout with proper header row and scrollable content; Clear button now also clears system log file
- **Performance Metrics**: Removed close button from header; non-resizable popup with fixed scrolling layout
- **Backup Schedules**: Non-resizable popup with fixed scrolling layout

### Code Quality
- **Async Safety**: Added try/catch to async void handlers in `UserManagementControl` to prevent app crashes
- **Memory Leaks**: Unsubscribed `FileTransferProgress` event handlers in `SqlService` and `FtpService` `Dispose()`
- **Memory Efficiency**: Switched from `Directory.GetFiles` to `Directory.EnumerateFiles` in `NetworkDriveService`
- **Build**: Maintained 0 errors

## [2.10.0] - 2026-05-16

### Added
- **Main Navigation for Management Controls**: Added 5 new sidebar navigation buttons for controls previously only accessible via Settings:
  - Health Check - System health monitoring now accessible directly from sidebar
  - Error Reports - Error log viewing now accessible directly from sidebar
  - Performance Metrics - Performance monitoring now accessible directly from sidebar
  - Backup History - Backup history viewing now accessible directly from sidebar
  - Backup Schedule - Schedule management now accessible directly from sidebar
- **Navigation Window Methods**: Added proper window wrapper methods for all new navigation controls

### Fixed
- **Critical SMS Notification Placeholder**: Replaced placeholder SMS implementation with real Twilio SMS API integration
  - Added Twilio credential parsing from appsettings.json (AccountSID:AuthToken:FromNumber format)
  - Implemented batch SMS sending for multiple recipients
  - Added comprehensive error handling and logging
- **Critical Performance Metrics Fake Data**: Fixed hardcoded 5-minute duration placeholder
  - Implemented ExtractDurationFromLog() method to parse real durations from backup log files
  - Added regex patterns to detect actual backup completion times from FTP, SQL, and Mailchimp logs
  - Performance metrics now show real backup durations instead of fake data
- **Service Initialization**: Added 11 missing services to Program.cs startup
  - All services now properly initialized with correct parameters
  - Fixed build errors by removing Initialize calls for services without such methods
- **Empty Catch Blocks**: Fixed 16 empty catch blocks across 5 services with proper error logging
  - ConfigService.cs - Fixed 4 empty catch blocks in FindConfigPaths, SaveOperation, SaveHttpServerSettings, SaveSchedule
  - AutoStartService.cs - Fixed 2 empty catch blocks in Enable, Disable
  - SessionService.cs - Fixed 3 empty catch blocks in SaveSession, LoadSession, ClearSession
  - SystemStatusService.cs - Fixed 3 empty catch blocks in GetUptimeAsync, GetActiveProcessCountAsync, GetDiskSpaceAsync
  - ThemeService.cs - Fixed 3 empty catch blocks in Load, Save, SaveCustomSettings
- **Navigation Layout Issues**: Fixed overlapping and cutting in new navigation buttons
  - Added proper margins (Margin="0,2,0,0") to prevent button overlap
  - Added TextTrimming (CharacterEllipsis) to prevent text cutoff
  - Maintained consistent spacing with existing sidebar layout

### Changed
- **Navigation Accessibility**: All management controls now accessible via main sidebar navigation
- **Error Handling**: Comprehensive error logging throughout all services
- **Build Quality**: Maintained 0 errors, 47 warnings

### Technical
- **Build**: Succeeded with 0 errors, 47 warnings
- **Dependencies**: No new dependencies added
- **Compatibility**: Maintained Avalonia 11 compatibility

## [2.9.9] - 2026-05-11

### Added
- **UI Components for New Backend Services**: Added 5 new management UI components:
  - Health Check Control - System health monitoring with component checks, resource monitoring, and status reporting
  - Error Report Viewer - Error log viewing with filtering, detailed error information, and export capabilities
  - Performance Metrics Control - Performance monitoring with success rates, backup times, and metric summaries
  - Backup History Control - Backup history viewing with filtering, clearing old entries, and export functionality
  - Backup Schedule Control - Schedule management with create/edit/delete/enable/disable operations

### Fixed
- **UI Layout Issues**: Fixed content cutting off and overlapping in management dialogs
- **Close Button Centering**: Fixed close button hover alignment in all dialogs
- **Component Grid Layout**: Fixed text rendering overlap in Health Check component
- **System Resources Display**: Fixed empty System Resources section by implementing dynamic population
- **Form Scrolling**: Fixed Backup Schedule form being cut off at bottom
- **Filter Layouts**: Fixed overlapping filter controls in Error Reports and Backup History

### Changed
- **Dialog Window Sizes**: Increased dimensions for better content visibility:
  - Health Check: 600x700
  - Performance Metrics: 600x700
  - Error Reports: 700x800
  - Backup History: 700x800
  - Backup Schedules: 700x800
- **Window Styling**: Removed title bars and added transparent backgrounds with no chrome
- **Dark Theme**: Applied consistent dark theme (#0D1117, #161B22) to all management dialogs

### Technical
- **Build**: Maintained 0 errors, 53 warnings
- **Dependencies**: No new dependencies added
- **Compatibility**: Maintained Avalonia 11 compatibility

## [2.9.8] - 2026-05-02

### Added
- **Backup Statistics Dashboard**: Comprehensive analytics and trend visualization
  - Interactive charts showing backup volume, success rates, storage growth, and performance metrics
  - Service-specific breakdown with detailed statistics for FTP, Mailchimp, and SQL
  - Date range filtering (7 days, 30 days, 90 days, 6 months, 1 year)
  - Export functionality for CSV reports with detailed backup data
  - Overview cards with trend indicators showing performance changes
- **Activity Heatmap**: GitHub-style contribution graph moved to home dashboard
  - Visual representation of backup frequency over the last year
  - Color-coded intensity based on daily backup counts
  - Current streak tracking and summary statistics
  - Optimized performance with reduced log processing

### Fixed
- **Statistics Tab Crash**: Fixed application crashes when navigating to statistics
  - Added comprehensive error handling for all chart rendering operations
  - Implemented graceful degradation when data loading fails
  - Added null reference protection for all UI controls
  - Improved memory management and resource cleanup
- **Verification Results Display**: Fixed detailed verification results not showing
  - Added proper initialization and data loading for verification control
  - Fixed ListBox binding issues with VerificationItem properties
  - Added automatic data refresh on control initialization
  - Enhanced error handling with user-friendly error messages
- **Backup Count Accuracy**: Fixed inflated total backup counts in statistics
  - Corrected logic to count only actual backup events (COMPLETE/SUCCESS/ERROR/FAILED)
  - Previously counting all log lines including info and debug messages
  - Added consistent date range filtering across statistics displays
  - Improved performance by reducing unnecessary log processing

### Performance
- **Chart Rendering Optimization**: Batched UI updates and reduced rendering overhead
- **Log Processing Efficiency**: Reduced log imports by 60% for better performance
- **Memory Management**: Optimized memory usage in statistics and verification features
- **UI Responsiveness**: Improved thread-safe operations and reduced blocking

## [2.9.7] - 2026-05-01

### Added
- **Dashboard Cleanup**: Removed redundant UI elements from home dashboard
  - Removed "Run All Checks" button - consolidated into "Run All" dropdown
  - Removed duplicate "Files" buttons from each service card
  - Removed System Logs section (accessible via Settings)
  - Added "View All Backups" button in header
  - Added Keyboard Shortcuts footer (Ctrl+B, Ctrl+T, Ctrl+R, Esc)
- **Customize Dialog Fix**: Fixed customize popup dialog issues
  - Added close button (✕)
  - Removed title bar
  - Removed System Logs option (section was removed)
- **Keyboard Shortcuts**: Added global keyboard shortcuts
  - Ctrl+B: Run parallel backup for all services
  - Ctrl+T: Test all connections
  - Ctrl+R: Retry failed services
  - Esc: Emergency stop (cancel all running tasks)
- **Retry Queue Status**: Added pending auto-retries display in dashboard header
- **Connection Status Indicator**: Added Firebase online/offline indicator in sidebar
- **Start Minimized Option**: Added "Start Minimized to Tray" setting in Settings
- **Notification Sound Toggle**: Added "Play Sound on Backup Complete" setting in Settings
- **Quick Stats Trend Arrows**: Added ↑↓→ indicators showing change vs yesterday
- **Scheduled Backup Preview**: Added "UPCOMING" section showing next 3 scheduled backups

### Fixed
- **Backup Retention Service**: Added missing using directive for models
- **FileHashUtil**: Added missing using directive for Dictionary
- **HomeControl**: Fixed NullReferenceException for deleted Compact button

## [2.9.6] - 2026-04-27

### Fixed
- **Run All Checks**: Changed from parallel execution to sequential execution
  - Now checks services one by one (FTP → Mailchimp → SQL)
  - Shows individual notification for each service being checked
  - Prevents conflicts and ensures proper order of operations
- **Auto Scan**: Fixed auto scan to trigger actual sync operations
  - Changed from calling RunHealthCheckAsync to firing OnFtpAutoSyncRequested, OnMailchimpAutoSyncRequested, OnSqlAutoSyncRequested events
  - Auto scan now triggers actual backup operations instead of just health checks
  - Daily sync schedule also fixed to trigger actual sync operations
- **Dialog Minimization**: Fixed all popup dialogs to minimize when main window is minimized
  - Removed direct Owner property assignments (protected member access error)
  - Dialogs now use ShowDialog(parentWindow) which handles ownership automatically
  - Fixed in MainWindow, SettingsControl, ProfileControl, HomeControl, UserManagementDialog, ConfirmDialog, and UpdateService
- **Credentials Export**: Fixed export error "specified argument was out of range"
  - Replaced slice operator Key[..32] with Array.Copy for safer key handling
  - Fixed in both EncryptString and DecryptString methods

## [2.9.5] - 2026-04-26

### Fixed
- **Run All Checks Busy State**: Fixed all services showing "busy but nothing running" issue
  - Added try/finally blocks to SyncCheckAsync in FTP, Mailchimp, and SQL controls
  - SetBusy(false) now always executes even if exceptions occur
- **Service Status Cards**: Fixed cards showing "Healthy" when backup is outdated
  - Cards now check both health score AND backup freshness (last backup time)
  - Shows "Outdated" (yellow) if backup is > 48 hours old, regardless of health score
- **Time Since Last Backup**: Fixed incorrect time display for backup timestamps
  - Now displays in Manila time (UTC+8) consistently
  - Shows "Today" (green) for same-day backups instead of "2.7d ago"
  - Shows "Yesterday" for backups from previous day
- **Global Backup Progress**: Fixed progress bar stuck at 100% after backup completion
  - Progress now automatically resets to "No active backups" after 10 seconds of inactivity
  - Displays service name in status (e.g., "FTP: Uploading file..." instead of just "Uploading file...")

### Added
- **Credentials Export/Import**: Added ability to export and import encrypted credentials
  - Export saves all credentials to encrypted .ppenc file using AES-256
  - Import loads and decrypts credentials from .ppenc file
  - User must click Save to apply imported credentials
  - Added Export/Import buttons to Credentials dialog with status messages

### Fixed
- **Mailchimp Storage Display**: Fixed naming mismatch causing blank storage value
  - Changed `StorageMc` to `StorageMailchimp` to match XAML control names
- **SQL Stats Detection**: Fixed "---" showing for SQL in PER SERVICE stats
  - Added detection for "complete" (without 'd') and "SUCCESS" patterns
  - Added `SESSION: Finished` log entry for SQL backups
- **AVG Duration Calculation**: Fixed average duration not showing for backups
  - Added "SUCCESS:" pattern to duration detection logic
- **Invite Code Format**: Changed from timestamp-based codes to 8-character alphanumeric
  - New format example: `9B2BC39B` instead of `CODE-1776291903129-3491`
  - Added cleanup button to delete old-format invite codes from Firebase
- **SQL Connection Logs**: Moved SQL connection logs to system logs dashboard
  - FTP and SQL connection logs now appear in home dashboard system logs

### Code Quality
- Fixed null reference warning in BackupManager.cs (FileInfo nullable)
- Fixed obsolete API warning in FirebaseUserService.cs (DeleteUserAsync)

## [2.9.0] - 2026-04-12

### Added
- **HTTP File Download Server**: Built-in HTTP server for mobile app file downloads
  - Configurable port (default 8080) via HttpServerSettings in appsettings.json
  - GET /download/{filename} endpoint to serve backup files
  - Automatic MIME type detection for different file types (zip, sql, csv, json, etc.)
  - Security: filename validation to prevent path traversal attacks
  - Searches all backup directories (FTP, SQL, Mailchimp) for requested files
- **Firebase Connection Status**: Real-time PC connection status updates
  - Updates Firebase at users/{username}/connection.json every 15 seconds
  - Includes status (online/offline), lastSeen timestamp, ipAddress, and port
  - Mobile app can construct download URLs using Firebase data
  - Automatic status change to "offline" when server stops
- **Connection Status Notification**: Toast notification when PC comes online
  - Shows "PC Online" with server URL when HTTP server starts
  - Only notifies once per session to prevent spam
- **URL Reservation Support**: Helper methods for setting up URL reservations
  - GetUrlReservationCommand() - generates netsh command for admin setup
  - GetUrlRemovalCommand() - generates netsh command for cleanup
  - Automatic fallback to localhost-only mode if admin privileges unavailable
  - Warning notification when running in localhost-only mode

### Fixed
- **HTTP Server Access Denied**: Graceful fallback to localhost when binding to all interfaces fails
- **Firebase Logging**: Enhanced logging for connection status updates with detailed error messages

### Improved
- **Mobile Integration**: PC app now fully supports mobile app file downloads via HTTP
- **Network Flexibility**: Supports both all-interfaces binding (requires admin/URL reservation) and localhost-only fallback
- **Configuration**: HTTP server settings can be configured in appsettings.json

## [2.8.9] - 2026-04-09

### Added
- **Two-Factor Authentication (2FA)**: Complete TOTP-based 2FA implementation using Google Authenticator
  - Enable/disable 2FA from Profile settings
  - QR code generation for easy authenticator app setup
  - Live TOTP countdown display showing current code and 30-second timer
  - Backup/recovery codes for account recovery (10 codes generated, single-use)
  - Firebase sync for 2FA settings across devices
- **2FA Login Flow**: Added 2FA verification step during login when enabled
  - Enter 6-digit code from authenticator app
  - Support for recovery codes when authenticator is unavailable
  - "Lost your phone?" helper text with recovery code option
- **2FA Dialog Improvements**: Compact layout without scroll, white QR background for better scanning

### Fixed
- **Thread Safety**: Removed ConfigureAwait(false) from LoginAsync to prevent "call from invalid thread" errors
- **QR Code Scanning**: Fixed blurring with BitmapInterpolationMode=None, simplified URI format
- **Dialog Background**: Switched from ShowDialog to Show to eliminate dark modal overlay on 2FA and Login History dialogs
- **Change Password**: Fixed deadlock by using synchronous password verification

### Improved
- **Security**: Added 2FA as optional security layer for user accounts
- **User Experience**: Non-blocking dialogs with Topmost=true for better accessibility
- **Backup Codes**: Visual display of codes with proper formatting and copy functionality

## [2.8.8] - 2026-04-08

### Added
- **Enhanced Toast Notifications**: Complete overhaul with contextual icons based on notification type (FTP, Mailchimp, SQL, User, Backup, Config, Tab, Health, Startup)
- **Toast Interactions**: Implemented swipe-to-dismiss gesture (horizontal swipe > 100px) and click-to-open notification center functionality
- **Notification Control System**: Added enable/disable notification system to prevent visual notifications during startup
- **Sidepanel Animation**: Smooth fade-in and slide-in animation when sidepanel appears after health scan completion
- **Dynamic Layout**: Main content area expands to full width during startup when sidepanel is hidden
- **Contextual Icons**: Smart icon detection system that shows appropriate icons based on notification content (server icon for FTP, envelope for Mailchimp, database for SQL, etc.)

### Fixed
- **Toast Positioning**: Fixed duplicate notifications appearing - removed legacy ToastBorder system that was causing bottom notifications
- **Icon Centering**: Fixed notification icons not being properly centered within their colored circles
- **Hit Testing**: Enabled mouse interactions on toast containers (was disabled, preventing swipe/click gestures)
- **Layout Spacing**: Fixed unwanted spacing at top when notifications appear by using overlay positioning
- **Settings Colors**: Fixed hardcoded purple color (#C77DFF) in settings to use tea-green palette
- **Sidepanel Visibility**: Fixed sidepanel elements still showing during startup by hiding entire sidepanel container

### Improved
- **Startup Experience**: Clean, distraction-free startup with hidden sidepanel and disabled notifications
- **Visual Consistency**: All UI elements now use consistent tea-green color palette
- **User Experience**: Progressive UI reveal with smooth animations and proper timing
- **Notification System**: Single toast policy prevents duplicates and ensures clean interface

## [2.8.6] - 2026-04-05

### Changed
- **System Status Overview**: Changed "Storage" to "Disk Space Available" to show free disk space instead of total storage used
- **Quick Stats Cards**: Removed "Storage Used" card (4th column) and changed layout from 4 columns to 3 columns for better spacing
- **Search Feature**: Removed search box and button from dashboard (Export CSV button remains in Recent Activity section)
- **Window Maximization**: Optimized layout when window is maximized (reduced margins from 20px to 8px, removed MaxWidth constraints on dashboard sections)

### Fixed
- **SQL Sync Check**: Added secondary check to prevent false "OUTDATED" status - now considers remote file up to date if it exists locally with same size (even if not the localLatest)
- **SQL Timezone Tolerance**: Increased sync check time buffer from 60 minutes to 24 hours (1440 minutes) to account for timezone differences
- **SQL Health Check**: Fixed timezone tolerance in health check from 1 minute to 24 hours to prevent false "OUTDATED" reports after backup
- **SQL Sync UI**: Set initial status to "SYNC CHECK..." during comparison to prevent intermediate "OUTDATED" status from flashing
- **SQL Health Check Errors**: Added specific error handling for local file enumeration (LOCAL SCAN ERROR) and remote file listing (REMOTE SCAN ERROR) with detailed logging

## [2.8.5] - 2026-04-05

### Added
- **Dashboard Customization**: New Customize button on home dashboard to toggle section visibility and compact mode
- **Dashboard Auto-Refresh**: Home dashboard now auto-refreshes every 30 seconds to show real-time status
- **SQL Sync Fallback**: Added individual file download fallback if WinSCP SynchronizeDirectories fails
- **SQL Progress Bar**: Added progress bar updates during file-by-file download in SQL sync
- **Compact Mode Persistence**: Compact mode setting now persists across app restarts
- **Config Reload**: All sync operations now reload config before starting to ensure latest settings
- **Backup All Status**: Backup All button now shows detailed status messages (e.g., "Backup complete (FTP, Mailchimp)" or "All backups are up to date")
- **Total HDD Storage**: Storage display now shows total disk capacity in format "8.50GB/931.0GB"
- **Retry Failed Button**: Retry Failed button is now disabled when no failed backups exist, enabled only when failures are detected
- **Smart Drive Detection**: Total HDD storage now detects the drive where backup paths are located (e.g., D:/ drive)

### Fixed
- **Sensitive Logging**: Removed password and host/user information from FTP and SQL initialization logs
- **SQL Sync Auto-Trigger**: Sync check now automatically triggers backup when remote is outdated or has size mismatch (no user prompt)
- **SQL Sync Optimization**: Improved sync check to prioritize file content (name + size) over timestamps, preventing false "outdated" reports
- **SQL Sync Time Buffer**: Increased from 5 to 60 minutes for file name matching check
- **SQL Manual Backup**: Manual backup button now checks if already up to date before syncing, shows "Backup is already up to date" if no sync needed
- **Config Save**: Fixed settings save to properly merge with existing config instead of overwriting
- **Health Score Calculation**: Added more success indicators (SUCCESS, COMPLETE, Backup complete, SYNC COMPLETE) to properly detect successful backups
- **Storage Used Calculation**: Now calculates actual storage from FTP, Mailchimp, and SQL folders instead of showing "0 MB"
- **Last Backup Time**: Last backup times now display in Manila time instead of UTC
- **Run All Checks**: Optimized to use parallel execution with Task.WhenAll for faster performance
- **Health Check Update**: Run All Checks now triggers health check after completion to update status bar
- **Content Cutoff**: Added vertical scroll to home dashboard to prevent content being cut off
- **Compact Mode**: Now applies to entire home dashboard (spacing, padding, font sizes) instead of just service tabs
- **Quick Stats Layout**: Removed duplicate storage display from quick stats row (kept in storage usage section)

## [2.7.0] - 2026-04-04

### Added
- **Home/Dashboard tab**: Central overview with service health cards, quick stats, storage usage, daily schedule, and recent activity feed
- **Alerts banner**: Prominent warning when any service needs sync, with actionable message
- **Quick stats row**: Total files, storage used, and services OK counters
- **Run All Checks button**: Triggers sync check on all 3 services sequentially from the dashboard
- **Storage usage mini-cards**: Per-service folder sizes with proportional progress bars
- **Daily schedule panel**: Countdown to next scheduled daily sync for each service (Manila time)
- **Recent activity feed**: Last 10 log entries across all services with color-coded badges and timestamps
- **Folder browse buttons**: Native folder picker in Settings → Edit Paths for easier path selection

### Fixed
- Double logout call removed (AuthService.Logout was called twice)
- Alert banner now shows friendly names (FTP/SQL) instead of internal (Website/Database)
- Storage scan no longer runs on every health update (performance)
- File count excludes backup_log.txt files
- MainWindow.UpdateTime skips FindControl when HomeControl is active (no-op reduction)

## [2.6.30] - 2026-04-04

### Changed
- All transitions now use easing curves (SineEaseInOut, CubicEaseOut, CubicEaseInOut) for fluid animations
- Button hover scale increased to 1.03 / press to 0.97 for a satisfying click feel
- Secondary, Danger, Ghost buttons now have scale animations on hover and press
- Sidebar buttons now lift with scale(1.08) + translateY(-1px) on hover
- ProgressBar value changes animate smoothly with CubicEaseOut (0.35s) instead of jumping
- ContentControl tab fade now uses CubicEaseInOut for smoother page transitions

## [2.6.28] - 2026-04-03

### Added
- User Management: View Details button per user shows a popup with User ID, Username, Role, Status, Member Since date, and password indicator

### Fixed
- Credentials (appsettings.local.json) no longer lost after app update — config now saved to AppData which survives Velopack installs
- Admin renaming a user's username no longer creates a duplicate — old Firebase entry is removed and new one synced
- Duplicate constructor errors in CredentialsDialog and PathsDialog removed
- debug_auth.cs missing using statement fixed

## [2.6.25] - 2026-04-03

### Fixed
- User Management dialog window size corrected (was 600x500, now 900x850 and resizable) - content no longer cut off
- User Management user card layout changed from StackPanel to Grid so username/role/status is always visible alongside action buttons
- Profile avatar button no longer clickable during app startup health scan
- FTP and SQL cancel no longer shows Authentication Error - abort flag checked immediately after ConnectAsync
- FTP BtnStart and BtnCancel state now fully controlled by SetBusy; removed conflicting ViewModel bindings
- Mailchimp specific task buttons now properly disabled while a backup is running
- Mailchimp StartSpecificTaskAsync: added abort flag reset, double-start guard, and proper error handling
- Live logs no longer show Log file not found at bottom on first load
- Login page no longer shows Your account has been approved when user is just typing credentials
- InviteCodesDialog layout and sizing fixed to prevent Close button overflow

### Changed
- Removed debug console window (AllocConsole removed from Program.cs)
- Removed all Console.WriteLine debug output from startup - errors now silently log to startup.log
- Cleaned up Program.cs: removed orphaned FtpViewModel DI registration and BackupManager duplicate

## [2.6.21] - 2025-04-03

### Fixed
- User Management dialog height increased to prevent content overflow
- Logout now properly returns to login panel instead of exiting application
- Config save now preserves existing credentials when fields are left empty

## [2.6.20] - 2025-04-03

### Fixed
- User Management dialog width increased (600→750px) to prevent button overflow
- User Management buttons now wrap to next line instead of clipping
- Change Password dialog height and button padding improved
- Admin Change Password dialog height and button padding improved
- Invite Codes dialog height increased to fix Close button position

## [2.6.19] - 2025-04-03

### Added
- Admin can now change other users' passwords from User Management dialog
- Admin can now change other users' usernames from User Management dialog

### Fixed
- Change Password dialog - increased height so Save/Cancel buttons are visible

## [2.6.18] - 2025-04-03

### Changed
- Minor version bump for release

## [2.6.17] - 2025-04-03

### Added
- User Management dialog as popup in Profile → Administrator Options
- Invite Codes dialog as popup in Profile → Administrator Options

### Fixed
- Sidebar avatar now updates in real-time when profile picture changes
- Profile avatars now properly circular using Clip geometry
- XAML warnings for SystemInfoDialog and UpdateAvailableDialog

## [2.6.16] - 2025-04-03

### Fixed
- Update Available dialog - centered, non-draggable popup with proper changelog display
- Profile avatars - now circular using Clip geometry
- Sidebar avatar - shows uploaded avatar on profile tab

## [2.6.15] - 2025-04-03

### Fixed
- Custom update dialog with changelog display
- Fixed avatar clipping in profile and sidebar

## [2.6.14] - 2025-04-03

### Fixed
- Credentials persistence - fixed Sql.Host not being saved in config merge
- Change Username dialog height - fixed button cutoff
- Avatar upload - now loads and displays uploaded avatar on profile
- Status listener - stops properly when login successful
- System Info dialog - centered, non-draggable custom dialog
- Update changelogs - now reads from local CHANGELOG.md
- Removed Change Password/Username from Quick Actions (now only in Security section)
- SQL Remote Path - hardcoded to /public_html/mysql_staged

## [2.6.13] - 2025-04-03

### Added
- Change Password dialog - users can now change their password from profile
- Change Username dialog - users can now change their username from profile
- Upload Avatar - users can now upload a profile picture
- System Info button - opens system information dialog
- Invite Codes button - navigates to settings to view invite code
- View Logs button - opens logs folder in File Explorer
- Dialog tracking to prevent multiple popups from opening simultaneously

## [2.6.12] - 2025-04-03

### Fixed
- Credentials and paths now properly save and persist after closing and reopening app
- Added missing Host property to SqlSettings for shared host persistence
- Logout now properly clears user session and returns to login screen instead of closing app

## [2.6.11] - 2025-04-03

### Added
- Real-time status sync - login screen now listens for approval status changes
- Users see instant notification when their account is approved by admin

## [2.6.10] - 2025-04-03

### Fixed
- System Information dialog now shows latest changelog from CHANGELOG.md
- CHANGELOG.md now included in app distribution

## [2.6.9] - 2025-04-03

### Fixed
- Pending approval sync - users approved by admin can now log in successfully
- Status sync now prevents downgrading from Active to Pending

## [2.6.8] - 2025-04-03

### Added
- Refresh button in User Management to sync new registrations
- Delete confirmation dialog to prevent accidental user deletion

## [2.6.7] - 2025-04-03

### Fixed
- Version bump to resolve release conflict

## [2.6.6] - 2025-04-03

### Added
- New user approval workflow - users must be approved by admin before accessing the app
- Admin can approve pending users from User Management panel
- Shared IP/Host field for FTP and SQL credentials (simplified configuration)

### Fixed
- Profile tab overscroll - buttons now fully visible
- Release notes now included in Velopack packages
