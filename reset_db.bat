@echo off
set "dbPath=%LOCALAPPDATA%\PinayPalBackupManager\users.db"
if exist "%dbPath%" (
    del "%dbPath%"
    echo Database reset successfully
) else (
    echo Database file not found
)
pause
