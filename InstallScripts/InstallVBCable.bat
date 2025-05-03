@echo off
setlocal enabledelayedexpansion
set LOGFILE="%TEMP%\vb_cable_install.log"

echo [%date% %time%] Starting installation >> %LOGFILE%

:: Download VB-Cable
set "DOWNLOAD_URL=https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip"
set "ZIP_FILE=%TEMP%\VBCABLE.zip"

echo Downloading VB-Cable... >> %LOGFILE%
powershell -Command "Invoke-WebRequest -Uri '%DOWNLOAD_URL%' -OutFile '%ZIP_FILE%'"
if errorlevel 1 (
    echo Download failed >> %LOGFILE%
    exit /b 1
)

:: Extract files
echo Extracting files... >> %LOGFILE%
powershell -Command "Expand-Archive -Path '%ZIP_FILE%' -DestinationPath '%~dp0' -Force"
if errorlevel 1 (
    echo Extraction failed >> %LOGFILE%
    exit /b 1
)

:: Install based on architecture
echo Checking architecture... >> %LOGFILE%
if "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
    echo Installing 64-bit version >> %LOGFILE%
    start "" /wait "%~dp0VBCABLE_Setup_x64.exe" /S
) else (
    echo Installing 32-bit version >> %LOGFILE%
    start "" /wait "%~dp0VBCABLE_Setup.exe" /S
)

:: Verify installation
echo Verifying installation... >> %LOGFILE%
reg query "HKLM\SYSTEM\CurrentControlSet\Services\VBAudioVACMM" >> %LOGFILE% 2>&1
if %errorlevel% neq 0 (
    echo Installation verification failed >> %LOGFILE%
    exit /b 1
)

echo Installation successful >> %LOGFILE%
exit /b 0