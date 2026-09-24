@echo off
setlocal EnableExtensions
title Uninstall Roblox Chat Launcher
cd /d "%~dp0"

echo.
echo  Roblox Chat Launcher - Version Removal
echo  ======================================
echo.
echo This removes the installed launcher so a different version can be installed.
echo The source code, Radmin server, server .env, and PostgreSQL database are kept.
echo.

set "RCL_UNINSTALLER="
set "RCL_UNINSTALL_KEY=HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{B0BACAFE-D326-4A7B-B6BA-1437C0DEBABE}_is1"

for /f "tokens=2,*" %%A in ('reg query "%RCL_UNINSTALL_KEY%" /v UninstallString 2^>nul ^| find /i "REG_SZ"') do set "RCL_UNINSTALLER=%%B"

if not defined RCL_UNINSTALLER if exist "%ProgramFiles%\RobloxChatLauncher\unins000.exe" set "RCL_UNINSTALLER=%ProgramFiles%\RobloxChatLauncher\unins000.exe"
if not defined RCL_UNINSTALLER if exist "%SystemDrive%\Program Files (x86)\RobloxChatLauncher\unins000.exe" set "RCL_UNINSTALLER=%SystemDrive%\Program Files (x86)\RobloxChatLauncher\unins000.exe"

if not defined RCL_UNINSTALLER (
    echo No installer-managed version was found. Checking portable builds...
    echo.
    if exist "%~dp0publish-latest\RobloxChatLauncher.exe" "%~dp0publish-latest\RobloxChatLauncher.exe" --uninstall
    if exist "%~dp0publish-coregui\RobloxChatLauncher.exe" "%~dp0publish-coregui\RobloxChatLauncher.exe" --uninstall
    if exist "%~dp0publish\RobloxChatLauncher.exe" "%~dp0publish\RobloxChatLauncher.exe" --uninstall
    taskkill /IM RobloxChatLauncher.exe /F >nul 2>nul
    if exist "%~dp0publish-latest" rmdir /s /q "%~dp0publish-latest"
    if exist "%~dp0publish-coregui" rmdir /s /q "%~dp0publish-coregui"
    if exist "%~dp0publish" rmdir /s /q "%~dp0publish"
    echo Portable launcher builds were unregistered and removed.
    echo The source code and server were kept.
    echo.
    pause
    exit /b 0
)

set "RCL_UNINSTALLER=%RCL_UNINSTALLER:"=%"

if not exist "%RCL_UNINSTALLER%" (
    echo [ERROR] The registered uninstaller does not exist:
    echo %RCL_UNINSTALLER%
    echo.
    pause
    exit /b 1
)

echo Found installed version.
echo Windows may ask for administrator permission.
echo.

start "" /wait "%RCL_UNINSTALLER%" /SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART
set "RCL_RESULT=%ERRORLEVEL%"

echo.
if not "%RCL_RESULT%"=="0" (
    echo [ERROR] Uninstall failed with exit code %RCL_RESULT%.
    pause
    exit /b %RCL_RESULT%
)

echo The installed launcher was removed successfully.
echo You can now install or run the newer version.
echo.
pause
exit /b 0
