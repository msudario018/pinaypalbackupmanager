# Changelog

All notable changes to this project are documented in this file.

## Unreleased -

### Added
- (none)

### Changed
- (none)

### Fixed
- (none)

### UI / Animations
- (none)

### Performance
- (none)

## 2026-04-02 - v2.3 Unified

### Added
- MVVM ViewModel: `FtpViewModel` using `CommunityToolkit.Mvvm`.
- Basic DI setup in `Program.Main` and service locator placeholder.
- `InverseBooleanConverter` for simple XAML bindings.
- `CHANGELOG.md` file.
- Startup overlay that blocks navigation and disables UI until initial health scan completes.

### Changed
- App startup now initializes `BackupManager` via a simple DI container and assigns `FtpViewModel` to `MainWindow.DataContext`.
- `FtpControl` updated to bind `Start`/`Cancel` buttons to view model commands (fallback legacy handlers remain).
- `App.axaml` wiring updated to set DataContext for DI demonstration.
- Health badge now shows detailed outdated services (e.g., `(Outdated: Website, SQL)`).
- Health badge outdated services are rendered with per-service accent colors for improved readability.
- UI modernization (Fluent dark) with unified button classes and accent-primary per service.
- Sidebar selected tab state.

### Fixed
- SQL health check false `OUTDATED` by aligning health scan logic with SQL Sync Check (remote-vs-local comparison, UTC handling + small buffer).
- Health badge rendering for outdated details (mixed-style inline text + per-service accent colors).

### UI / Animations
- Documented and delivered application-wide animations and transitions (buttons, toasts, status badges, content crossfade, health indicator color transitions).
- Modernized Health badge text rendering using inline runs for mixed styling.

### Performance
- Animations designed to be hardware-accelerated and low overhead (< 2% CPU impact expected).

## 2024-04-02 - v2.2 (previous)