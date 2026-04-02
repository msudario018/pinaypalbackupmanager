@echo off
echo Generating random invite code...
set chars=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789
set code=
for /l %%i in (1,1,8) do (
    call :randChar
)
echo New invite code: %code%
echo.
echo You can:
echo 1. Use this in Firebase: https://pinaypal-backup-manager-default-rtdb.firebaseio.com/
echo 2. Or enter it in the app settings
pause
goto :eof

:randChar
set /a rand=%random% %% 36
for %%i in (0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35) do (
    if !rand!==%%i set char=!chars:~%%i,1! && set code=!code!!char! && goto :eof
)
goto :eof
