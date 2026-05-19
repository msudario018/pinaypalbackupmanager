# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0/).

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
