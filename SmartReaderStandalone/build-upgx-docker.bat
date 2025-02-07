@echo off
setlocal enabledelayedexpansion

:: Set up logging and output directories
set "LOG_DIR=build_logs"
set "OUTPUT_DIR=cap_deploy"
set "TIMESTAMP=%date:~-4%%date:~3,2%%date:~0,2%_%time:~0,2%%time:~3,2%%time:~6,2%"
set "TIMESTAMP=!TIMESTAMP: =0!"
set "LOG_FILE=%LOG_DIR%\build_log_%TIMESTAMP%.txt"

:: Create necessary directories
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

:: Start logging
echo Build started at %date% %time% > "%LOG_FILE%"
echo ====================================== >> "%LOG_FILE%"

:: Function to log messages
call :log "Starting SmartReader build process..."

:: Check Docker installation and service
call :log "Checking Docker installation..."
docker --version >nul 2>&1
if %errorlevel% neq 0 (
    call :log "ERROR: Docker is not running or not installed."
    call :error_exit
)
call :log "Docker is available and running."

:: Clean up existing resources
call :log "Cleaning up Docker resources..."
call :log "Stopping any existing smartreader containers..."
docker ps -q --filter "name=smartreader-container" | findstr /r /c:"." >nul && docker stop smartreader-container
docker container rm -f smartreader-container >nul 2>&1

:: Remove existing images to ensure clean build
call :log "Removing existing smartreader images..."
docker images smartreader-upgx -q | findstr /r /c:"." >nul && docker rmi -f smartreader-upgx

:: Build the Docker image
call :log "Building Docker image..."
docker build --progress=plain --platform=linux/arm -t smartreader-upgx -f ./Dockerfile ../ >> "%LOG_FILE%" 2>&1
if %errorlevel% neq 0 (
    call :log "ERROR: Docker build failed. Check the log file for details."
    call :error_exit
)
call :log "Docker image built successfully."

:: Create and run container
call :log "Creating container and extracting UPGX package..."
docker run -d --name smartreader-container smartreader-upgx
if %errorlevel% neq 0 (
    call :log "ERROR: Failed to create Docker container."
    call :error_exit
)

:: Wait for container to be ready
timeout /t 5 /nobreak >nul

:: Copy UPGX file from container
call :log "Copying UPGX package from container..."
docker container cp smartreader-container:/etk/smartreader_cap.upgx "%OUTPUT_DIR%/"
if %errorlevel% neq 0 (
    call :log "ERROR: Failed to copy UPGX package from container."
    call :cleanup_and_exit
)

:: Verify UPGX file
if exist "%OUTPUT_DIR%\smartreader_cap.upgx" (
    call :log "UPGX package successfully created and copied to %OUTPUT_DIR%"
) else (
    call :log "ERROR: UPGX package not found in output directory."
    call :cleanup_and_exit
)

:: Add GLIBC verification after successful build
call :log "Verifying build environment..."
docker run --rm smartreader-upgx ldd --version >> "%LOG_FILE%" 2>&1

call :log "Checking GLIBC requirements of built binary..."
docker run --rm smartreader-upgx objdump -p /etk/cap/SmartReaderStandalone | findstr "GLIBC" >> "%LOG_FILE%" 2>&1

:: Add binary format verification
call :log "Verifying binary format..."
docker run --rm smartreader-upgx file /etk/cap/SmartReaderStandalone >> "%LOG_FILE%" 2>&1

:: Clean up
call :cleanup

:: Success message
call :log "Build completed successfully!"
echo.
echo Build completed successfully! Check the log file for details:
echo %LOG_FILE%
echo.
echo UPGX package location:
echo %OUTPUT_DIR%\smartreader_cap.upgx
echo.
pause
exit /b 0

:: Functions
:log
echo [%date% %time%] %~1 >> "%LOG_FILE%"
echo %~1
exit /b 0

:cleanup
call :log "Cleaning up resources..."
docker container rm -f smartreader-container >nul 2>&1
exit /b 0

:cleanup_and_exit
call :cleanup
call :error_exit

:error_exit
echo.
echo Build failed! Check the log file for details:
echo %LOG_FILE%
echo.
pause
exit /b 1