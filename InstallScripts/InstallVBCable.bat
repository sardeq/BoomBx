@echo off
setlocal enabledelayedexpansion
set LOGFILE="%TEMP%\vb_cable_install.log"

:: Get batch file location
set "BATCH_DIR=%~dp0"
set "DRIVER_DIR=%BATCH_DIR%VBCABLE_Driver_Pack"

:: Architecture detection
echo Detecting system architecture... >> %LOGFILE%
set "ARCH=x86"
if exist "%ProgramFiles%\Internet Explorer\iexplore.exe" (
    if exist "%ProgramW6432%" set "ARCH=x64"
)
echo System architecture: !ARCH! >> %LOGFILE%

:: Check for existing installation
reg query "HKLM\SYSTEM\CurrentControlSet\Services\VBAudioVACMM" >> %LOGFILE% 2>&1
if %errorlevel% equ 0 (
    echo VB-Cable already installed >> %LOGFILE%
    exit /b 0
)

:: Install based on architecture
if "!ARCH!"=="x64" (
    echo Installing 64-bit version >> %LOGFILE%
    if not exist "%DRIVER_DIR%\VBCABLE_Setup_x64.exe" (
        echo 64-bit installer missing >> %LOGFILE%
        exit /b 1
    )
    "%DRIVER_DIR%\VBCABLE_Setup_x64.exe" /S /V"/qb /l*v %TEMP%\vbcable64_install.log"
    set "ERR=!errorlevel!"
) else (
    echo Installing 32-bit version >> %LOGFILE%
    if not exist "%DRIVER_DIR%\VBCABLE_Setup.exe" (
        echo 32-bit installer missing >> %LOGFILE%
        exit /b 1
    )
    "%DRIVER_DIR%\VBCABLE_Setup.exe" /S /V"/qb /l*v %TEMP%\vbcable32_install.log"
    set "ERR=!errorlevel!"
)

if !ERR! neq 0 (
    echo Installation failed with error !ERR! >> %LOGFILE%
    exit /b !ERR!
)

:: Final verification
reg query "HKLM\SYSTEM\CurrentControlSet\Services\VBAudioVACMM" >> %LOGFILE% 2>&1
if %errorlevel% neq 0 (
    echo Installation verification failed >> %LOGFILE%
    exit /b 1
)

echo Installation succeeded >> %LOGFILE%
exit /b 0