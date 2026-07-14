@echo off
setlocal
title Claude Agent Orchestrator

where node >nul 2>nul
if errorlevel 1 (
    echo.
    echo [!] Node.js neni nainstalovany.
    echo     Aplikace ho potrebuje ke spousteni Claude agentu.
    echo     Stahni a nainstaluj LTS verzi z:  https://nodejs.org/
    echo.
    pause
    exit /b 1
)

echo.
echo Spoustim Claude Agent Orchestrator...
echo Az server nabehne, otevri v prohlizeci:  http://localhost:6001
echo (Toto okno nechej otevrene - zavrenim okna server ukoncis.)
echo.

"%~dp0ClaudeOrchestrator.exe"
