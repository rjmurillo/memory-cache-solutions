@echo off
REM k6 Test Runner Script for ASP.NET Core MeteredMemoryCache Example (Windows)
REM This script runs all k6 performance tests in sequence

setlocal enabledelayedexpansion

REM Configuration
if "%BASE_URL%"=="" set BASE_URL=https://localhost:64494
if "%HTTP_HOST_URL%"=="" set HTTP_HOST_URL=http://localhost:64495
set TEST_RESULTS_DIR=.\k6-results
for /f "tokens=2 delims==" %%a in ('wmic OS Get localdatetime /value') do set "dt=%%a"
set TIMESTAMP=%dt:~0,8%_%dt:~8,6%

REM Function to print colored output (simplified for Windows)
:print_status
echo [INFO] %~1
goto :eof

:print_success
echo [SUCCESS] %~1
goto :eof

:print_warning
echo [WARNING] %~1
goto :eof

:print_error
echo [ERROR] %~1
goto :eof

REM Function to check if k6 is installed
:check_k6
k6 version >nul 2>&1
if %errorlevel% neq 0 (
    call :print_error "k6 is not installed. Please install k6 first."
    echo Installation instructions:
    echo   Windows: winget install k6
    echo   Or download from: https://k6.io/docs/getting-started/installation/
    exit /b 1
)

for /f "tokens=2" %%i in ('k6 version ^| findstr "k6"') do set K6_VERSION=%%i
call :print_success "k6 version %K6_VERSION% is installed"
goto :eof

REM Function to check if application is running
:check_application
call :print_status "Checking if application is running at %BASE_URL%..."

curl -s -f "%BASE_URL%/health" >nul 2>&1
if %errorlevel% neq 0 (
    call :print_error "Application is not running or not accessible at %BASE_URL%"
    echo Please start the application first:
    echo   cd examples\AspNetCore
    echo   dotnet run
    exit /b 1
)

call :print_success "Application is running and healthy"
goto :eof

REM Function to create results directory
:create_results_dir
if not exist "%TEST_RESULTS_DIR%" (
    mkdir "%TEST_RESULTS_DIR%"
    call :print_status "Created results directory: %TEST_RESULTS_DIR%"
)
goto :eof

REM Function to run a test
:run_test
set test_name=%~1
set test_file=%~2
set description=%~3

call :print_status "Running %test_name%: %description%"

set output_file=%TEST_RESULTS_DIR%\%test_name%_%TIMESTAMP%.json
set summary_file=%TEST_RESULTS_DIR%\%test_name%_%TIMESTAMP%_summary.txt

k6 run --out json="%output_file%" "%test_file%"
if %errorlevel% neq 0 (
    call :print_error "%test_name% failed"
    exit /b 1
)

call :print_success "%test_name% completed successfully"

REM Generate summary
echo Test: %test_name% > "%summary_file%"
echo Description: %description% >> "%summary_file%"
echo Timestamp: %TIMESTAMP% >> "%summary_file%"
echo Base URL: %BASE_URL% >> "%summary_file%"
echo Results: %output_file% >> "%summary_file%"
echo. >> "%summary_file%"
echo Key Metrics: >> "%summary_file%"
echo =========== >> "%summary_file%"
echo Review the JSON output file for detailed metrics >> "%summary_file%"

goto :eof

REM Function to run all tests
:run_all_tests
call :print_status "Starting k6 performance test suite..."
call :print_status "Base URL: %BASE_URL%"
call :print_status "Results will be saved to: %TEST_RESULTS_DIR%"
echo.

REM Test 1: Smoke Tests
call :run_test "smoke" "k6-smoke-tests.js" "Basic functionality verification (1 minute, 1 VU)"
if %errorlevel% neq 0 (
    set failed_tests=smoke
)

REM Test 2: Average Load Tests
call :run_test "average-load" "k6-average-load-tests.js" "Normal usage patterns (5 minutes, 10 VUs)"
if %errorlevel% neq 0 (
    set failed_tests=%failed_tests% average-load
)

REM Test 3: Stress Tests
call :run_test "stress" "k6-stress-tests.js" "Breaking point identification (9 minutes, 1-20 VUs)"
if %errorlevel% neq 0 (
    set failed_tests=%failed_tests% stress
)

REM Test 4: Spike Tests
call :run_test "spike" "k6-spike-tests.js" "Traffic spike simulation (4 minutes, 10-50-10 VUs)"
if %errorlevel% neq 0 (
    set failed_tests=%failed_tests% spike
)

REM Test 5: Breakpoint Tests
call :run_test "breakpoint" "k6-breakpoint-tests.js" "Capacity planning (10 minutes, 10-50 VUs)"
if %errorlevel% neq 0 (
    set failed_tests=%failed_tests% breakpoint
)

REM Summary
echo.
call :print_status "Test suite completed!"

if "%failed_tests%"=="" (
    call :print_success "All tests passed successfully!"
) else (
    call :print_warning "Some tests failed: %failed_tests%"
)

call :print_status "Results saved to: %TEST_RESULTS_DIR%"
call :print_status "Review the summary files for detailed results"
goto :eof

REM Function to run specific test
:run_specific_test
set test_name=%~1

if "%test_name%"=="smoke" (
    call :run_test "smoke" "k6-smoke-tests.js" "Basic functionality verification"
) else if "%test_name%"=="load" (
    call :run_test "average-load" "k6-average-load-tests.js" "Normal usage patterns"
) else if "%test_name%"=="average-load" (
    call :run_test "average-load" "k6-average-load-tests.js" "Normal usage patterns"
) else if "%test_name%"=="stress" (
    call :run_test "stress" "k6-stress-tests.js" "Breaking point identification"
) else if "%test_name%"=="soak" (
    call :run_test "soak" "k6-soak-tests.js" "Memory leaks and stability (30 minutes)"
) else if "%test_name%"=="spike" (
    call :run_test "spike" "k6-spike-tests.js" "Traffic spike simulation"
) else if "%test_name%"=="breakpoint" (
    call :run_test "breakpoint" "k6-breakpoint-tests.js" "Capacity planning"
) else (
    call :print_error "Unknown test: %test_name%"
    echo Available tests: smoke, load, stress, soak, spike, breakpoint
    exit /b 1
)
goto :eof

REM Function to show help
:show_help
echo k6 Test Runner for ASP.NET Core MeteredMemoryCache Example
echo.
echo Usage: %~nx0 [OPTIONS] [TEST_NAME]
echo.
echo Options:
echo   -h, --help          Show this help message
echo   -u, --url URL       Set base URL (default: https://localhost:64494)
echo   -r, --results DIR   Set results directory (default: .\k6-results)
echo   --check-only        Only check prerequisites, don't run tests
echo.
echo Test Names:
echo   smoke               Basic functionality verification (1 minute)
echo   load                Normal usage patterns (5 minutes)
echo   stress              Breaking point identification (9 minutes)
echo   soak                Memory leaks and stability (30 minutes)
echo   spike               Traffic spike simulation (4 minutes)
echo   breakpoint          Capacity planning (10 minutes)
echo.
echo Examples:
echo   %~nx0                  Run all tests
echo   %~nx0 smoke            Run only smoke tests
echo   %~nx0 -u https://api.example.com load  Run load tests against custom URL
echo   %~nx0 --check-only     Check prerequisites only
echo.
echo Environment Variables:
echo   BASE_URL            Application base URL
echo   HTTP_HOST_URL       HTTP host URL
goto :eof

REM Main function
:main
set test_name=
set check_only=false

REM Parse command line arguments
:parse_args
if "%~1"=="" goto :args_done
if "%~1"=="-h" goto :show_help
if "%~1"=="--help" goto :show_help
if "%~1"=="-u" (
    set BASE_URL=%~2
    shift
    shift
    goto :parse_args
)
if "%~1"=="--url" (
    set BASE_URL=%~2
    shift
    shift
    goto :parse_args
)
if "%~1"=="-r" (
    set TEST_RESULTS_DIR=%~2
    shift
    shift
    goto :parse_args
)
if "%~1"=="--results" (
    set TEST_RESULTS_DIR=%~2
    shift
    shift
    goto :parse_args
)
if "%~1"=="--check-only" (
    set check_only=true
    shift
    goto :parse_args
)
set test_name=%~1
shift
goto :parse_args

:args_done

REM Check prerequisites
call :check_k6
if %errorlevel% neq 0 exit /b 1

call :check_application
if %errorlevel% neq 0 exit /b 1

if "%check_only%"=="true" (
    call :print_success "All prerequisites check passed!"
    exit /b 0
)

REM Create results directory
call :create_results_dir

REM Run tests
if "%test_name%"=="" (
    call :run_all_tests
) else (
    call :run_specific_test "%test_name%"
)

goto :eof

REM Run main function
call :main %*
