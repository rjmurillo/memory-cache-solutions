# k6 Test Runner Script for ASP.NET Core MeteredMemoryCache Example
# Cross-platform PowerShell Core script for running k6 performance tests
# Run with: pwsh run-k6-tests.ps1

param(
    [string]$TestName = "",
    [string]$BaseUrl = $env:BASE_URL ?? "https://localhost:64494",
    [string]$HttpHostUrl = $env:HTTP_HOST_URL ?? "http://localhost:64495",
    [string]$ResultsDir = "./k6-results",
    [switch]$CheckOnly = $false,
    [switch]$Help = $false
)

# Colors for output
$Colors = @{
    Red = "Red"
    Green = "Green"
    Yellow = "Yellow"
    Blue = "Cyan"
    White = "White"
}

# Function to print colored output
function Write-Status {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor $Colors.Blue
}

function Write-Success {
    param([string]$Message)
    Write-Host "[SUCCESS] $Message" -ForegroundColor $Colors.Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] $Message" -ForegroundColor $Colors.Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor $Colors.Red
}

# Function to show help
function Show-Help {
    Write-Host "k6 Test Runner for ASP.NET Core MeteredMemoryCache Example" -ForegroundColor $Colors.White
    Write-Host ""
    Write-Host "Usage: pwsh run-k6-tests.ps1 [OPTIONS] [TEST_NAME]" -ForegroundColor $Colors.White
    Write-Host ""
    Write-Host "Options:" -ForegroundColor $Colors.White
    Write-Host "  -TestName <name>     Run specific test (smoke, load, stress, soak, spike, breakpoint)" -ForegroundColor $Colors.White
    Write-Host "  -BaseUrl <url>       Set base URL (default: https://localhost:64494)" -ForegroundColor $Colors.White
    Write-Host "  -HttpHostUrl <url>   Set HTTP host URL (default: http://localhost:64495)" -ForegroundColor $Colors.White
    Write-Host "  -ResultsDir <dir>    Set results directory (default: ./k6-results)" -ForegroundColor $Colors.White
    Write-Host "  -CheckOnly           Only check prerequisites, don't run tests" -ForegroundColor $Colors.White
    Write-Host "  -Help                Show this help message" -ForegroundColor $Colors.White
    Write-Host ""
    Write-Host "Test Names:" -ForegroundColor $Colors.White
    Write-Host "  smoke               Basic functionality verification (1 minute)" -ForegroundColor $Colors.White
    Write-Host "  load                Normal usage patterns (5 minutes)" -ForegroundColor $Colors.White
    Write-Host "  stress              Breaking point identification (9 minutes)" -ForegroundColor $Colors.White
    Write-Host "  soak                Memory leaks and stability (30 minutes)" -ForegroundColor $Colors.White
    Write-Host "  spike               Traffic spike simulation (4 minutes)" -ForegroundColor $Colors.White
    Write-Host "  breakpoint          Capacity planning (10 minutes)" -ForegroundColor $Colors.White
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor $Colors.White
    Write-Host "  pwsh run-k6-tests.ps1                    # Run all tests" -ForegroundColor $Colors.White
    Write-Host "  pwsh run-k6-tests.ps1 -TestName smoke    # Run only smoke tests" -ForegroundColor $Colors.White
    Write-Host "  pwsh run-k6-tests.ps1 -BaseUrl https://api.example.com -TestName load" -ForegroundColor $Colors.White
    Write-Host "  pwsh run-k6-tests.ps1 -CheckOnly         # Check prerequisites only" -ForegroundColor $Colors.White
    Write-Host ""
    Write-Host "Environment Variables:" -ForegroundColor $Colors.White
    Write-Host "  BASE_URL            Application base URL" -ForegroundColor $Colors.White
    Write-Host "  HTTP_HOST_URL       HTTP host URL" -ForegroundColor $Colors.White
}

# Function to check if k6 is installed
function Test-K6Installed {
    try {
        $k6Version = & k6 version 2>$null | Select-String "k6" | ForEach-Object { $_.Line.Split()[1] }
        if ($LASTEXITCODE -eq 0 -and $k6Version) {
            Write-Success "k6 version $k6Version is installed"
            return $true
        }
    }
    catch {
        # k6 not found
    }
    
    Write-Error "k6 is not installed. Please install k6 first."
    Write-Host "Installation instructions:" -ForegroundColor $Colors.White
    Write-Host "  Windows: winget install k6" -ForegroundColor $Colors.White
    Write-Host "  macOS: brew install k6" -ForegroundColor $Colors.White
    Write-Host "  Linux: https://k6.io/docs/getting-started/installation/" -ForegroundColor $Colors.White
    return $false
}

# Function to check if application is running
function Test-ApplicationRunning {
    param([string]$Url)
    
    Write-Status "Checking if application is running at $Url..."
    
    try {
        $response = Invoke-WebRequest -Uri "$Url/health" -Method Get -TimeoutSec 10 -UseBasicParsing
        if ($response.StatusCode -eq 200) {
            Write-Success "Application is running and healthy"
            return $true
        }
    }
    catch {
        # Application not accessible
    }
    
    Write-Error "Application is not running or not accessible at $Url"
    Write-Host "Please start the application first:" -ForegroundColor $Colors.White
    Write-Host "  cd src" -ForegroundColor $Colors.White
    Write-Host "  dotnet run" -ForegroundColor $Colors.White
    return $false
}

# Function to create results directory
function New-ResultsDirectory {
    param([string]$Path)
    
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
        Write-Status "Created results directory: $Path"
    }
}

# Function to run a test
function Invoke-K6Test {
    param(
        [string]$TestName,
        [string]$TestFile,
        [string]$Description,
        [string]$ResultsDir,
        [string]$Timestamp
    )
    
    Write-Status "Running $TestName : $Description"
    
    $outputFile = Join-Path $ResultsDir "${TestName}_${Timestamp}.json"
    $summaryFile = Join-Path $ResultsDir "${TestName}_${Timestamp}_summary.txt"
    
    try {
        & k6 run --out json=$outputFile $TestFile
        if ($LASTEXITCODE -eq 0) {
            Write-Success "$TestName completed successfully"
            
            # Generate summary
            $summaryContent = @"
Test: $TestName
Description: $Description
Timestamp: $Timestamp
Base URL: $BaseUrl
Results: $outputFile

Key Metrics:
===========
Review the JSON output file for detailed metrics
"@
            $summaryContent | Out-File -FilePath $summaryFile -Encoding UTF8
            
            return $true
        }
        else {
            Write-Error "$TestName failed"
            return $false
        }
    }
    catch {
        Write-Error "$TestName failed with exception: $($_.Exception.Message)"
        return $false
    }
}

# Function to run all tests
function Invoke-AllTests {
    param(
        [string]$ResultsDir,
        [string]$Timestamp
    )
    
    Write-Status "Starting k6 performance test suite..."
    Write-Status "Base URL: $BaseUrl"
    Write-Status "Results will be saved to: $ResultsDir"
    Write-Host ""
    
    $failedTests = @()
    
    # Test 1: Smoke Tests
    if (-not (Invoke-K6Test "smoke" "k6-smoke-tests.js" "Basic functionality verification (1 minute, 1 VU)" $ResultsDir $Timestamp)) {
        $failedTests += "smoke"
    }
    
    # Test 2: Average Load Tests
    if (-not (Invoke-K6Test "average-load" "k6-average-load-tests.js" "Normal usage patterns (5 minutes, 10 VUs)" $ResultsDir $Timestamp)) {
        $failedTests += "average-load"
    }
    
    # Test 3: Stress Tests
    if (-not (Invoke-K6Test "stress" "k6-stress-tests.js" "Breaking point identification (9 minutes, 1-20 VUs)" $ResultsDir $Timestamp)) {
        $failedTests += "stress"
    }
    
    # Test 4: Spike Tests
    if (-not (Invoke-K6Test "spike" "k6-spike-tests.js" "Traffic spike simulation (4 minutes, 10-50-10 VUs)" $ResultsDir $Timestamp)) {
        $failedTests += "spike"
    }
    
    # Test 5: Breakpoint Tests
    if (-not (Invoke-K6Test "breakpoint" "k6-breakpoint-tests.js" "Capacity planning (10 minutes, 10-50 VUs)" $ResultsDir $Timestamp)) {
        $failedTests += "breakpoint"
    }
    
    # Summary
    Write-Host ""
    Write-Status "Test suite completed!"
    
    if ($failedTests.Count -eq 0) {
        Write-Success "All tests passed successfully!"
    }
    else {
        Write-Warning "Some tests failed: $($failedTests -join ', ')"
    }
    
    Write-Status "Results saved to: $ResultsDir"
    Write-Status "Review the summary files for detailed results"
}

# Function to run specific test
function Invoke-SpecificTest {
    param(
        [string]$TestName,
        [string]$ResultsDir,
        [string]$Timestamp
    )
    
    switch ($TestName.ToLower()) {
        "smoke" {
            Invoke-K6Test "smoke" "k6-smoke-tests.js" "Basic functionality verification" $ResultsDir $Timestamp
        }
        "load" {
            Invoke-K6Test "average-load" "k6-average-load-tests.js" "Normal usage patterns" $ResultsDir $Timestamp
        }
        "average-load" {
            Invoke-K6Test "average-load" "k6-average-load-tests.js" "Normal usage patterns" $ResultsDir $Timestamp
        }
        "stress" {
            Invoke-K6Test "stress" "k6-stress-tests.js" "Breaking point identification" $ResultsDir $Timestamp
        }
        "soak" {
            Invoke-K6Test "soak" "k6-soak-tests.js" "Memory leaks and stability (30 minutes)" $ResultsDir $Timestamp
        }
        "spike" {
            Invoke-K6Test "spike" "k6-spike-tests.js" "Traffic spike simulation" $ResultsDir $Timestamp
        }
        "breakpoint" {
            Invoke-K6Test "breakpoint" "k6-breakpoint-tests.js" "Capacity planning" $ResultsDir $Timestamp
        }
        default {
            Write-Error "Unknown test: $TestName"
            Write-Host "Available tests: smoke, load, stress, soak, spike, breakpoint" -ForegroundColor $Colors.White
            exit 1
        }
    }
}

# Main execution
if ($Help) {
    Show-Help
    exit 0
}

# Check prerequisites
if (-not (Test-K6Installed)) {
    exit 1
}

if (-not (Test-ApplicationRunning $BaseUrl)) {
    exit 1
}

if ($CheckOnly) {
    Write-Success "All prerequisites check passed!"
    exit 0
}

# Create results directory
New-ResultsDirectory $ResultsDir

# Generate timestamp
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

# Run tests
if ([string]::IsNullOrEmpty($TestName)) {
    Invoke-AllTests $ResultsDir $timestamp
}
else {
    Invoke-SpecificTest $TestName $ResultsDir $timestamp
}
