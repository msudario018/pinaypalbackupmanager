@echo off
echo Resetting PinayPal database, user sessions, and setup state...

taskkill /f /im PinayPalBackupManager.exe 2>nul
timeout /t 1 /nobreak >nul

set "dataDir=%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\Data"
set "appDir=%LOCALAPPDATA%\PinayPal.PinayPalBackupManager"
set "legacyDir=%LOCALAPPDATA%\PinayPalBackupManager"

if exist "%dataDir%\users.db*" (
    del /f /q "%dataDir%\users.db*"
    echo [OK] Removed %dataDir%\users.db
)
if exist "%appDir%\users.db*" (
    del /f /q "%appDir%\users.db*"
    echo [OK] Removed %appDir%\users.db
)
if exist "%legacyDir%\users.db*" (
    del /f /q "%legacyDir%\users.db*"
    echo [OK] Removed %legacyDir%\users.db
)

if exist "%dataDir%\session.dat" (
    del /f /q "%dataDir%\session.dat"
    echo [OK] Cleared active sessions
)
if exist "%appDir%\session.dat" (
    del /f /q "%appDir%\session.dat"
)

if exist "%dataDir%\appsettings.local.json" (
    del /f /q "%dataDir%\appsettings.local.json"
    echo [OK] Reset configuration to trigger Initial Setup Wizard
)
if exist "%appDir%\appsettings.local.json" (
    del /f /q "%appDir%\appsettings.local.json"
    echo [OK] Reset configuration
)
if exist "%legacyDir%\appsettings.local.json" (
    del /f /q "%legacyDir%\appsettings.local.json"
)

echo.
echo Database and setup reset complete! Launching PinayPal will now show the Initial Setup Wizard.
pause

