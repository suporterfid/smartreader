
@echo off
setlocal

REM Check if Docker is running
docker --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Error: Docker is not running or not installed.
    pause
    exit /b 1
)

REM Clearing Docker system
docker system prune -a -f
if %errorlevel% neq 0 (
    echo Error: Failed to prune Docker system.
    pause
    exit /b 1
)

REM Building Docker image
docker build --progress=plain --platform=linux/arm -t smartreader-upgx -f ./Dockerfile ../
if %errorlevel% neq 0 (
    echo Error: Failed to build Docker image.
    pause
    exit /b 1
)

REM Running Docker container
docker run -d --name smartreader-container smartreader-upgx 
if %errorlevel% neq 0 (
    echo Error: Failed to run Docker container.
    pause
    exit /b 1
)

REM Copying file from Docker container
docker container cp smartreader-container:/etk/smartreader_cap.upgx cap_deploy/
if %errorlevel% neq 0 (
    echo Error: Failed to copy file from Docker container.
    pause
    exit /b 1
)

REM Removing Docker container
docker container rm -f smartreader-container
if %errorlevel% neq 0 (
    echo Error: Failed to remove Docker container.
    pause
    exit /b 1
)

pause
