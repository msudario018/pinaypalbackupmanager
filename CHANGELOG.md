# Changelog

All notable changes to this project will be documented in this file.

## [2.6.6] - 2025-04-03

### Added
- New user approval workflow - users must be approved by admin before accessing the app
- Admin can approve pending users from User Management panel
- Shared IP/Host field for FTP and SQL credentials (simplified configuration)

### Fixed
- Profile tab overscroll - buttons now fully visible
- Release notes now included in Velopack packages

## [2.6.5] - 2025-04-03

### Fixed
- Invite code now properly syncs across all PCs via Firebase
- Fixed invite code validation to check Firebase first before local database
- Improved cross-PC user synchronization

## [2.6.4] - 2025-04-03

### Fixed
- Firebase blocking issue resolved with non-blocking initialization
- Invite code sync between local and Firebase

## [2.6.3] - 2025-04-03

### Added
- Firebase Realtime Database integration for multi-PC sync
- Cross-PC user synchronization (register, disable, delete)
- Invite code sharing across multiple PCs
- Auto-update functionality via Velopack

### Changed
- Improved profile display in sidebar
- Updated UI components

## [2.6.0] - 2025-04-02

### Added
- Initial Firebase integration
- User management features (disable/enable/delete)
- Admin panel improvements

### Changed
- Version bump to stable release (removed -alpha)
