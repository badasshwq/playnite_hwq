@echo off
REM Double-click to build (Debug/x86) and launch Playnite desktop app.
REM Edit code and use Git in IDEA; run with this script.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
if errorlevel 1 (
    echo.
    echo *** Build/run failed. Send the red messages above to Claude. ***
    pause
)
