@echo off
chcp 65001 >nul
setlocal DisableDelayedExpansion
set "ROOT=%~dp0"
set "LAUNCHER=%ROOT%payload\InstallLauncher.ps1"
set "LOGDIR=%LOCALAPPDATA%\Eizo\Logs"
set "ENTRYLOG=%LOGDIR%\installer-entry.log"

if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1
call :log "Install CMD started."

if not exist "%LAUNCHER%" (
  call :log "ERROR: InstallLauncher.ps1 is missing."
  echo Installation failed: payload\InstallLauncher.ps1 was not found.
  echo Log: %ENTRYLOG%
  call :wait
  exit /b 2
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%LAUNCHER%" -EntryLogPath "%ENTRYLOG%" -EntryCommandPath "%~f0" -EntryWorkingDirectory "%CD%"
set "EXITCODE=%ERRORLEVEL%"
call :log "Install launcher exit code=%EXITCODE%"

if not "%EXITCODE%"=="0" (
  echo Installation failed. Exit code: %EXITCODE%
  echo Log: %ENTRYLOG%
  call :wait
  exit /b %EXITCODE%
)

echo Installation completed. See PowerShell output for package verification.
echo Log: %ENTRYLOG%
call :wait
exit /b 0

:wait
if defined EIZO_NO_PAUSE exit /b 0
pause
exit /b 0

:log
>> "%ENTRYLOG%" echo [%TIME%] %~1
exit /b 0
