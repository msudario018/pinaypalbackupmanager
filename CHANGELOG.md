# Changelog

All notable changes to this project will be documented in this file.

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
