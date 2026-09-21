# Live Activities, Notifications, and Dashboard Enhancements

## Current diagnosis

### Live Activities are not displayable

The app target includes `ActivityKit` model and manager code, and `NSSupportsLiveActivities` is present in the app `Info.plist`. However, the Xcode project has only one native target: `PinayPalBackup`. It has no WidgetKit extension and no `ActivityConfiguration` view. iOS therefore has no Lock Screen or Dynamic Island presentation to render.

The existing state is also derived from a four-second foreground timer. It does not provide reliable updates after the app is suspended. An iPhone with a Dynamic Island is required for the Dynamic Island presentation; any iPhone running iOS 16.2 or later can show the Lock Screen presentation when Live Activities are enabled in Settings.

### Notifications are only partially wired

`NotificationService` defines categories and schedules local notifications, but no call site invokes `requestAuthorization()`. The alert permission sheet will never be displayed. Retry and view-details categories also have no `UNUserNotificationCenterDelegate` implementation, so their actions do nothing.

Current success detection relies on foreground polling; failures are not reliably distinguished from successful completion; reminder and daily-digest methods are never scheduled; and low-disk alerts can be scheduled repeatedly on every poll without deduplication.

## Proposed implementation

### Phase 1 — Make Live Activities work (priority: required)

1. Add a `PinayPalBackupWidgets` WidgetKit extension target and embed it in the app target.
2. Move the shared `BackupActivityAttributes` definition into a shared source group included by both targets.
3. Add `BackupLiveActivityWidget.swift` with `ActivityConfiguration(for: BackupActivityAttributes.self)`:
   - Lock Screen: service icon, service name, status, progress bar, transfer speed and ETA.
   - Dynamic Island compact: service icon plus percentage.
   - Dynamic Island minimal: service icon with progress tint.
   - Dynamic Island expanded: service, progress, status, speed and ETA.
4. Replace the manager's `Any` activity storage with `Activity<BackupActivityAttributes>?`; use typed content updates and surface start/update/end errors to the app diagnostic screen.
5. On app launch, recover any active activity through `Activity<BackupActivityAttributes>.activities`; reconcile it with `/api/status` instead of starting duplicates.
6. Start an activity only after `/api/backup/{service}` confirms acceptance. End it as failed when the endpoint fails or the backend reports a failed/cancelled terminal state.
7. Add an on-device "Start Live Activity Test" button plus a capability/status row: device support, app setting, system setting and most recent ActivityKit error.
8. Keep foreground polling for rich progress. Add optional ActivityKit push-token support as a later server-backed phase if continuous updates while the app is terminated are needed.

### Phase 2 — Complete notifications (priority: required)

1. Request notification permission at a clear user moment (after successful pairing or from an explicit Enable Notifications button); never silently rely on the settings toggles.
2. Set `UNUserNotificationCenter.current().delegate` during app startup and implement actions:
   - `RETRY_BACKUP`: call the authenticated retry/trigger endpoint for the affected service.
   - `VIEW_LOGS`: deep-link `MainView` to backup history/logs and filter to the relevant service.
3. Include the backup service, result, history ID, and deep-link destination in `userInfo`.
4. Extend `/api/status` or add `/api/events` to expose terminal result (`success`, `failed`, `cancelled`), error text, and a monotonic event ID. Drive notifications from that event rather than inferring completion merely from `isBusy` changing to false.
5. Add a durable notification event cursor in `UserDefaults` so reconnecting does not duplicate alerts.
6. Implement throttle/deduplication for low-disk and freshness alerts (one per condition per 24 hours, with a recovery notification when resolved).
7. Implement real reminder and digest scheduling using `UNCalendarNotificationTrigger`, recalculating after remote schedule changes; show their next scheduled time in Settings.
8. Add "Send Test Notification" and permission-status/recovery UI that links to iOS Settings when permission is denied.

### Phase 3 — Dashboard improvements

#### Web dashboard

1. Replace the fixed-delay `Backup All` sequence with a server-side job queue that starts the next service only after the prior job reaches a terminal result.
2. Add a persistent Activity/Alerts panel: queued, running, completed, failed, and acknowledged events with filters and per-service retry.
3. Add a backup timeline chart showing duration, transferred size, and failure rate over 7/30 days.
4. Add explicit browser-notification controls and a test action; request permission only from that user gesture.
5. Add a connection-health card showing API latency, last successful poll, LAN binding/firewall state, and a copyable diagnostic bundle.

#### Expandable service cards — FTP, SQL, and Mailchimp

Make every service summary card an accessible, clickable disclosure control. Selecting a card expands an inline detail panel; only one card is expanded by default to keep the dashboard readable. The card itself must not trigger a backup, so the expandable header and action buttons remain separate controls.

Each expanded panel contains:

1. **Live service console** — a filtered terminal-style log stream for the selected service, with timestamp, level, search, pause, copy, and download actions. It should use a server event cursor or WebSocket/SSE stream rather than repeatedly downloading all logs.
2. **Individual backup actions** — `Run backup`, `Retry failed`, and `Cancel active backup` (when applicable), with a confirmation only for destructive/costly actions. Mailchimp gets the same first-class `Run backup` action as FTP and SQL.
3. **Run state** — queued/running/completed/failed status, progress, elapsed time, current operation, error summary, and a link to the relevant history entry.
4. **Backup details** — freshness state, last successful and failed run, latest duration, transferred size/file count, next scheduled time, destination path, retention policy, and service-specific configuration summary with sensitive values redacted.
5. **History and trend** — the most recent runs, duration/size trend, and a retry count. Keep this compact in the panel; link to the full Activity tab for longer history.
6. **Service-specific information**:
   - FTP: host/port (redacted as appropriate), remote path, local target, discovered file count, transfer delta, and connection health.
   - SQL: database/server identifier, dump size, integrity/verification result, local target, and most recent restore-check result if supported.
   - Mailchimp: audience name/ID (partially redacted), member count, added/updated/removed totals, export format, local target, and API connectivity state.

Required backend/API additions:

1. Add `/api/services/{service}/detail` for the redacted configuration, freshness, current job, schedules, and recent history.
2. Add `/api/services/{service}/logs?after={eventId}` or a scoped SSE endpoint. Return structured entries (`eventId`, UTC time, level, message, jobId, service) instead of parsing display log strings.
3. Standardize `POST /api/backup/{service}`, `POST /api/backup/{service}/retry`, and `POST /api/backup/{service}/cancel`, with explicit accepted/rejected/job-state responses.
4. Add authorization checks and audit records for every remote manual action; never expose credentials or raw API keys in these responses or browser logs.

#### iOS app

1. Add a Notifications & Live Activities diagnostics screen with the test controls and system capability states from Phases 1–2.
2. Add a unified event inbox with unread count, service filters, retry action, and deep links from notifications.
3. Add per-service detail screens: last successful backup, freshness threshold, duration trend, storage trend, recent errors and retry status.
4. Add offline/cache behaviour: timestamp cached status, label it as stale, and automatically refresh on foreground/reconnect.
5. Add a queued "Backup All" progress view matching the server-side job queue rather than assuming all triggers succeed.
6. Make each iOS service row tappable to open a dedicated service-detail sheet/page with the same scoped live logs, individual backup/retry/cancel actions, service-specific metrics, and recent run history. Keep the dashboard row compact and use the detail page for the terminal-style console.

## Verification and acceptance criteria

1. On a physical iPhone with iOS 16.2+, starting a backup shows the Lock Screen Live Activity; on Dynamic Island hardware it also shows compact, minimal and expanded presentations.
2. The Test Live Activity action reports a useful error for disabled system settings, unsupported OS, or ActivityKit request failure.
3. After notification permission is granted, successful, failed, low-disk and stale-backup notifications arrive once per distinct event, including while the app is backgrounded when a local trigger/event is available.
4. Tapping Retry starts the correct authenticated service backup; tapping View Details opens the correct iOS view.
5. Browser and iOS dashboards show the same server-issued backup event state and do not claim success for failed or cancelled jobs.
6. Run `xcodebuild` in CI for both the app and widget extension, plus the existing .NET release build, before release.

## Delivery order

Implement Phases 1 and 2 together in the next release because the current UI promises features that cannot yet work. Phase 3 can follow as a separate dashboard/product enhancement release.

## Native Liquid Glass and navigation overhaul

### Platform baseline

Apple's Liquid Glass API is the iOS 26 SwiftUI design system; it will continue to use the system appearance on later platforms, including iOS 27. The implementation must therefore compile with an Xcode SDK that includes the iOS 26 APIs, gate explicit new APIs with `#available(iOS 26.0, *)`, and retain the current iOS 16–25 presentation as a fallback. Update the iOS CI runner from Xcode 15.4 to an Xcode version that supplies the iOS 26 SDK before adding those calls.

### Current issues

`MainView` implements a custom, draggable capsule dock using layered materials, manually drawn highlights, shadows, scaling, and a drag gesture. It replaces the system tab bar rather than participating in it. This conflicts with the intended Liquid Glass hierarchy: navigation should be a small, system-managed functional layer above content; content cards should not attempt to reproduce the effect. The dock's drag gesture also competes with horizontal interactions and makes the navigation position feel unstable.

### Proposed navigation architecture

1. Replace the custom dock and integer `selectedTab` switch with one root `TabView` using typed `Tab` values:
   - **Overview** — backup status, freshness, active backup, and primary actions.
   - **Activity** — history, current queue, failures, retries, and the unified event inbox.
   - **Console** — live logs and diagnostics.
   - **Dashboard** — optional embedded web dashboard for parity/admin-only actions.
   - **Search** — a dedicated search-role tab for history, services, errors, and commands.
2. Place settings, pairing, and global actions in the system toolbar rather than as navigation destinations. Use one explicit primary backup action with a tinted, prominent system button.
3. On iPhone, enable `.tabBarMinimizeBehavior(.onScrollDown)` for content-first scrolling. On iPad, use `.tabViewStyle(.sidebarAdaptable)` or a `NavigationSplitView` so the tab bar naturally becomes a Liquid Glass sidebar.
4. Use `NavigationStack` inside each tab. This gives each destination an independent path and preserves state when changing tabs.
5. Add a persistent `TabView` bottom accessory only for global backup state: a compact active-job progress strip that opens the Activity tab when tapped. Do not place screen-specific controls there.
6. Deep links from notifications should select the appropriate tab and navigate to the exact history/event detail.

### Native Liquid Glass rules

1. Let `TabView`, `NavigationStack`, sheets, toolbars, menus, alerts, controls, and search receive the system material automatically. Remove custom tab-bar backgrounds, hard dividers, custom blur layers, manual specular rims, and fixed dark overlays from these navigation surfaces.
2. Use `glassEffect(_:in:)`, `.buttonStyle(.glass)`, `GlassEffectContainer`, and `glassEffectID(_:in:)` only for a small number of custom controls that need to morph or combine, such as the active-backup accessory and an anchored quick-action cluster. Never apply it broadly to cards or the page background.
3. Keep backup cards in the content layer with semantic `Material`, adaptive color tokens, and standard controls. This preserves visual hierarchy and keeps text legible on light/dark modes.
4. Use `ToolbarSpacer` to group toolbar actions; use tint only for the single most important action. Use `scrollEdgeEffectStyle` rather than opaque bar overlays.
5. Respect Reduce Transparency, Reduce Motion, Increased Contrast, Dynamic Type, VoiceOver, and user-selected system Liquid Glass appearance. Disable decorative morphing and use semantic system materials under accessibility settings.

### Motion and interaction changes

1. Remove draggable-navigation physics, arbitrary dock movement, rubber-band scaling, and swipe-to-change-tabs gestures.
2. Retain motion only where it conveys state: standard tab transition, matched `glassEffectID` morphing between an idle and active backup control, progress changes, and sheet presentations anchored to their initiating control.
3. Ensure all actions have accessible labels, 44pt minimum hit targets, and equivalent non-gesture navigation.

### Verification

1. Validate on iOS 16 fallback, iOS 26/27 latest system appearance, iPhone compact width, iPad sidebar, and Dynamic Type/accessibility settings.
2. Confirm navigation state survives switching tabs, authentication lock/unlock, settings presentation, and notification deep links.
3. Confirm the app no longer draws an opaque/custom tab bar over the system Liquid Glass navigation layer.


Then lastly github push, release, update changelogs, and bump version