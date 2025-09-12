#!/bin/bash
# Cross-platform wrapper script for k6 test runner
# This script detects the platform and runs the appropriate command

# Check if PowerShell Core is available
if command -v pwsh &> /dev/null; then
    echo "Running with PowerShell Core..."
    pwsh run-k6-tests.ps1 "$@"
elif command -v powershell &> /dev/null; then
    echo "Running with Windows PowerShell..."
    powershell -ExecutionPolicy Bypass -File run-k6-tests.ps1 "$@"
else
    echo "PowerShell Core (pwsh) not found. Please install PowerShell Core:"
    echo "  Windows: winget install Microsoft.PowerShell"
    echo "  macOS: brew install powershell"
    echo "  Linux: https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell-core-on-linux"
    exit 1
fi