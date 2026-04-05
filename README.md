# PinayPal Backup Manager

A comprehensive backup management application for PinayPal.net, designed to automate and manage backups for FTP, Mailchimp, and SQL databases with real-time monitoring and health tracking.

## Features

### Dashboard
- **Real-time Health Monitoring**: View status of all backup services at a glance
- **Quick Stats**: Track backups today, success rate, failed backups, and total files
- **Storage Usage**: Monitor backup folder sizes with total HDD capacity display
- **Daily Schedule**: Countdown to next scheduled daily sync for each service (Manila time)
- **Recent Activity**: Live feed of recent log entries across all services
- **Customization**: Toggle section visibility and compact mode for personalized dashboard view
- **Auto-Refresh**: Dashboard updates every 30 seconds for real-time status

### Service Management

#### FTP Backup
- Automatic sync with pinaypal.net FTP server
- Configurable auto-scan intervals
- Daily scheduled sync (default: 10 PM Manila time)
- Progress tracking and detailed logging
- Connection testing and integrity checks

#### Mailchimp Backup
- Full backup of audiences, campaigns, reports, merge fields, and tags
- Configurable auto-scan intervals
- Daily scheduled backup (default: 6 PM Manila time)
- Freshness tracking for all backup components
- Integrity verification

#### SQL Database Backup
- Automatic sync with remote MySQL staged folder
- Configurable auto-scan intervals
- Daily scheduled backup (default: 5 PM Manila time)
- Smart sync check (prioritizes file content over timestamps)
- Fallback to individual file download if directory sync fails
- Progress bar updates during file transfers

### Automation
- **Auto-Sync**: Automatically triggers sync when backups are detected as outdated
- **Daily Schedules**: Configurable daily sync times for each service
- **Health Checks**: Periodic health monitoring with automatic sync triggers
- **Retry Failed**: Smart retry button that only enables when failed backups are detected

### Security
- User authentication with role-based access (Admin/User)
- Encrypted credential storage
- Secure password management
- Firebase integration for user management
- Admin approval workflow for new registrations

### Configuration
- Customizable backup paths
- Configurable scan intervals and daily schedules
- TLS fingerprint verification for secure connections
- Settings persistence across app updates
- Auto-start on Windows startup option

## Installation

### Prerequisites
- Windows 10 or later
- .NET 8.0 Runtime
- Valid credentials for FTP, Mailchimp, and SQL services

### Setup
1. Download the latest release from GitHub Releases
2. Run the installer (Velopack will handle the installation)
3. Launch the application
4. Register a new account (requires admin approval)
5. Configure backup paths and credentials in Settings

## Configuration

### Backup Paths
- **FTP Local Folder**: Path where FTP backups are stored
- **Mailchimp Folder**: Path where Mailchimp exports are saved
- **SQL Local Folder**: Path where SQL database backups are stored

### Credentials
- **FTP**: Host, username, password, port, TLS fingerprint
- **SQL**: Host (shared with FTP), username, password, TLS fingerprint
- **Mailchimp**: API key (configured via Mailchimp service)

### Schedule Settings
- **Auto-Scan Intervals**: Hours and minutes between automatic health checks
- **Daily Sync Times**: Configurable Manila time for daily scheduled backups
- **Auto-Start**: Option to start application with Windows

## Usage

### Quick Start
1. Log in with your credentials
2. Navigate to Settings to configure backup paths and credentials
3. Click "Run All Checks" on the dashboard to verify all services
4. Use individual service tabs to trigger manual backups or sync checks

### Dashboard
- View real-time status of all backup services
- Click "Run All Checks" to trigger sync checks on all services
- Click "Backup All" to run backups on all services
- Use "Customize" to toggle dashboard sections and enable compact mode
- View recent activity and system logs

### Service Tabs
- **FTP Tab**: View FTP status, trigger sync, view terminal logs
- **Mailchimp Tab**: View Mailchimp status, run full backup, view terminal logs
- **SQL Tab**: View SQL status, trigger sync check, run manual backup, view terminal logs

### Settings
- **Paths**: Configure backup folder locations
- **Credentials**: Manage FTP, SQL, and Mailchimp credentials
- **Schedule**: Set auto-scan intervals and daily sync times
- **General**: Configure auto-start and view app version

### User Management (Admin Only)
- View and manage registered users
- Approve pending registrations
- Change user passwords and usernames
- Manage invite codes
- View user details

## Troubleshooting

### Common Issues

**Sync Check Shows "OUTDATED" Despite Recent Backup**
- The sync check prioritizes file content (name + size) over timestamps
- If files match in name and size, it should show "LATEST"
- Check the terminal logs for detailed comparison information

**Storage Shows "0 MB"**
- Verify backup paths are correctly configured in Settings
- Ensure backup folders exist and contain files
- Check system logs for storage calculation errors

**Connection Failures**
- Verify credentials are correct
- Check TLS fingerprints match server certificates
- Ensure network connectivity to backup servers
- Test connection using the "Test Connection" button on each service tab

**Auto-Sync Not Triggering**
- Verify auto-scan intervals are configured in Schedule settings
- Check if maintenance mode is enabled
- Review system logs for sync trigger errors

## Development

### Project Structure
- `UI/`: Avalonia UI controls and views
- `services/`: Business logic for backup operations
- `Models/`: Data models and configurations
- `services/ConfigService.cs`: Configuration management
- `services/BackupManager.cs`: Health check and automation
- `services/LogService.cs`: Logging system

### Building
```bash
dotnet build
```

### Running
```bash
dotnet run
```

### Publishing
```bash
dotnet publish -c Release
```

## Version History

See [CHANGELOG.md](CHANGELOG.md) for detailed version history.

## Support

For issues, questions, or feature requests, please open an issue on GitHub.

## License

This project is proprietary software for PinayPal.net internal use.

## Credits

Developed for PinayPal.net backup management and monitoring.
