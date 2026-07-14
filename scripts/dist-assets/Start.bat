@echo off
setlocal
title Claude Agent Orchestrator

where node >nul 2>nul
if errorlevel 1 (
    echo.
    echo [!] Node.js is not installed.
    echo     It is required to launch Claude agents.
    echo     Download and install the LTS version from:  https://nodejs.org/
    echo.
    pause
    exit /b 1
)

echo.
echo Starting Claude Agent Orchestrator...
echo When the server is up, open in your browser:  http://localhost:6001
echo (Keep this window open - closing it stops the server.)
echo.

"%~dp0ClaudeOrchestrator.exe"
