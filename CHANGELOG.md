# Changelog

All notable changes to this project are documented in this file.

## Unreleased

### Added
- (none)

### Changed
- (none)

### Fixed
- Added startup error logging and exception handling to diagnose installation issues.

## 2026-04-03 - v2.4.7

### Added
- (none)

### Changed
- Reorganized Settings layout order: App Updates → Backup Health Dashboard → System Configuration → System Information → Credentials & Paths → User Management.

### Fixed
- (none)

## 2026-04-03 - v2.4.5

### Added
- Auto-rotating invite codes every 5 minutes with circular timer indicator.
- Copy to clipboard button for invite codes (replaces manual rotate button).

### Changed
- Invite codes now automatically rotate instead of requiring manual rotation.
- Reorganized Settings layout: Credentials moved to top, User Management moved to bottom.
- Velopack configuration updated for proper installation folder (PinayPal/PinayPal Backup Manager) and shortcut name.

### Fixed
- Fixed installation folder structure to create PinayPal/PinayPal Backup Manager instead of nested folders.
- Fixed shortcut name to display "PinayPal Backup Manager" instead of executable name.
- Fixed SQL credentials to use same host as FTP (removed redundant host field).
- Ensured SQL password field is properly masked with PasswordChar.

## 2026-04-03 - v2.4.1

### Added
- Multi-user login system with local SQLite user store.
- Invite code registration: admins generate/share codes for co-devs.
- Admin panel in Settings: manage users, rotate invite codes, disable/delete accounts.
- Login/Register UI with Fluent dark theme, password masking, Enter-key support.
- Secure password hashing with PBKDF2+SHA256+salt.
- Logout support: returns to login screen.
- Shared TLS fingerprint field: single input used for both FTP and SQL connections.

### Changed
- Version badge and Settings version now read dynamically from assembly (no more hardcoded version strings).
- App now starts with LoginWindow; MainWindow opens only after successful authentication.

### Fixed
- Tab locking: app now correctly forces Settings tab and disables all other content when credentials are missing.
- Version display on top bar was stuck at "v2.3 Unified"; now shows actual assembly version.

### UI / Animations
- (none)

### Performance
- (none)

## 2026-04-03 - v2.4.0

### Added
- In-app Settings form to edit credentials and paths and save them to `appsettings.local.json`.
- Startup config guard: disables non-Settings tabs until required config is provided.
- Auto-load of `appsettings.json` + `appsettings.local.json` at startup via `ConfigService`.

### Changed
- System Information popup now summarizes changelog into `Added / Changed / Fixed` (prefers `Unreleased`, otherwise latest release).
- Increased default application window height to reduce Settings scrolling.

### Fixed
- Masked sensitive Settings inputs (FTP/SQL passwords and Mailchimp API key) using password-style text fields.

### UI / Animations
- (none)

### Performance
- (none)