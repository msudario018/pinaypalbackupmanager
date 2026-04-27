# Changelog

All notable changes to this project will be documented in this file.

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
