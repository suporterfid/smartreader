@echo off
REM ====================================================
REM Script: set-log-level.bat
REM Purpose: Sets the logging level for the application
REM Usage: set-log-level.bat [DEBUG|INFORMATION|WARNING|ERROR]
REM Default: INFORMATION if no level is specified
REM ====================================================


REM Enable delayed expansion for variable handling
setlocal EnableDelayedExpansion

REM Set default values for server and authentication
set "SERVER=192.168.68.248:8443"
set "AUTH_HEADER=Authorization: Basic YWRtaW46YWRtaW4="

REM Get the log level from command line argument or use default
set "LOG_LEVEL=%~1"
if "%LOG_LEVEL%"=="" (
    echo No log level specified. Using default: INFORMATION
    set "LOG_LEVEL=INFORMATION"
)

REM Convert input to uppercase using a simpler method
for %%i in ("a=A" "b=B" "c=C" "d=D" "e=E" "f=F" "g=G" "h=H" "i=I" "j=J" "k=K" "l=L" "m=M" "n=N" "o=O" "p=P" "q=Q" "r=R" "s=S" "t=T" "u=U" "v=V" "w=W" "x=X" "y=Y" "z=Z") do (
    set "LOG_LEVEL=!LOG_LEVEL:%%~i!"
)

REM Validate the log level against allowed values
set "VALID_LEVEL="
for %%L in (DEBUG INFORMATION WARNING ERROR) do (
    if /i "%LOG_LEVEL%"=="%%L" (
        set "VALID_LEVEL=1"
        set "LOG_LEVEL=%%L"
    )
)

REM If invalid level, show error and exit
if not defined VALID_LEVEL (
    echo.
    echo Error: Invalid log level '%LOG_LEVEL%'
    echo Valid levels are: DEBUG, INFORMATION, WARNING, ERROR
    echo.
    exit /b 1
)

REM Create a temporary file for the JSON payload
set "TEMP_JSON=%TEMP%\log_level.json"
echo {"level": "%LOG_LEVEL%"} > "%TEMP_JSON%"

REM Display the current operation
echo.
echo Attempting to set log level to: %LOG_LEVEL%
echo Server: %SERVER%
echo.

REM Make the curl request to change the log level
curl -X POST "https://%SERVER%/api/logging/level" ^
    -H "Content-Type: application/json" ^
    -H "Accept: application/json" ^
    -H "%AUTH_HEADER%" ^
    -k ^
    -d @"%TEMP_JSON%"

REM Store the curl result
set CURL_RESULT=%ERRORLEVEL%

REM Clean up the temporary file
del "%TEMP_JSON%" 2>nul

REM Check if curl was successful
if %CURL_RESULT% NEQ 0 (
    echo.
    echo Error: Failed to set log level
    echo Curl command returned error code: %CURL_RESULT%
    echo.
    exit /b 1
)

echo.
echo Log level change operation completed.
echo.

exit /b 0