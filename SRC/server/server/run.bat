@echo off
setlocal
title Roblox Chat Launcher Server
cd /d "%~dp0"

echo.
echo  Roblox Chat Launcher - Radmin Server
echo  ====================================
echo.

where node >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Node.js is not installed or is not in PATH.
    echo Download the current Node.js LTS release from https://nodejs.org/
    echo.
    pause
    exit /b 1
)

where npm >nul 2>nul
if errorlevel 1 (
    echo [ERROR] npm was not found. Repair or reinstall Node.js.
    echo.
    pause
    exit /b 1
)

if not exist "node_modules\ws\package.json" (
    echo [SETUP] Installing server packages...
    call npm ci
    if errorlevel 1 (
        echo.
        echo [ERROR] Package installation failed. Check your internet connection.
        pause
        exit /b 1
    )
    echo.
)

if not exist ".env" (
    echo [SETUP] No server configuration was found.
    node setup.js
    if errorlevel 1 (
        echo.
        echo [ERROR] Setup was cancelled or failed.
        pause
        exit /b 1
    )
    echo.
)

echo [DATABASE] Checking the local PostgreSQL database...
node --env-file=.env ensureDatabase.js
if errorlevel 1 (
    echo.
    echo [ERROR] PostgreSQL setup failed.
    echo Expected local login: postgres / postgres on 127.0.0.1 port 5432.
    echo This script will never modify the existing Syntax database.
    pause
    exit /b 1
)
echo.

echo [START] Starting the chat server...
echo Players should enter this computer's Radmin VPN IP in the launcher.
echo Default port: 10000
echo Press Ctrl+C to stop the server.
echo.

node --env-file=.env server.js
set "RCL_EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%RCL_EXIT_CODE%"=="0" echo [ERROR] The server stopped with exit code %RCL_EXIT_CODE%.
pause
exit /b %RCL_EXIT_CODE%
