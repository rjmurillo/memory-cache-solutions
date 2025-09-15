@echo off
REM Cross-platform wrapper script for k6 test runner
REM This script detects PowerShell availability and runs the appropriate command

REM Check if PowerShell Core is available
where pwsh >nul 2>&1
if %errorlevel% equ 0 (
    echo Running with PowerShell Core...
    pwsh -NoProfile -File "%~dp0run-k6-tests.ps1" %*
    goto :eof
)

REM Check if Windows PowerShell is available
where PowerShell >nul 2>&1
if %errorlevel% equ 0 (
    echo Running with Windows PowerShell...
    PowerShell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-k6-tests.ps1" %*
    goto :eof
)

REM PowerShell not found
echo PowerShell Core (pwsh) not found. Please install PowerShell Core:
echo   Windows: winget install Microsoft.PowerShell
echo   macOS: brew install powershell
echo   Linux: https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell-core-on-linux
exit /b 1