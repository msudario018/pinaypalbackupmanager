@echo off
echo Resetting PinayPal database and user sessions...

set "dataDb=%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\Data\users.db"
set "oldDb1=%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\users.db"
set "oldDb2=%LOCALAPPDATA%\PinayPalBackupManager\users.db"

if exist "%dataDb%" (
    del /f /q "%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\Data\users.db*"
    echo [OK] Removed %dataDb%
)
if exist "%oldDb1%" (
    del /f /q "%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\users.db*"
    echo [OK] Removed %oldDb1%
)
if exist "%oldDb2%" (
    del /f /q "%LOCALAPPDATA%\PinayPalBackupManager\users.db*"
    echo [OK] Removed %oldDb2%
)

if exist "%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\Data\session.dat" (
    del /f /q "%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\Data\session.dat"
    echo [OK] Cleared active sessions
)
if exist "%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\session.dat" (
    del /f /q "%LOCALAPPDATA%\PinayPal.PinayPalBackupManager\session.dat"
)

echo.
echo Database reset complete! When you launch PinayPal, it will show the Initial Setup Wizard or allow you to register the first Admin account.
pause

