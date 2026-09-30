@echo off
chcp 65001 >nul
setlocal DisableDelayedExpansion
set "ROOT=%~dp0"
set "LAUNCHER=%ROOT%payload\UninstallLauncher.ps1"
set "LOGDIR=%LOCALAPPDATA%\Eizo\Logs"
set "ENTRYLOG=%LOGDIR%\installer-entry.log"

if not exist "%LOGDIR%" mkdir "%LOGDIR%" >nul 2>&1
call :log "Uninstall CMD started."

if not exist "%LAUNCHER%" (
  call :log "ERROR: UninstallLauncher.ps1 is missing."
  echo Uninstall failed: payload\UninstallLauncher.ps1 was not found.
  echo Log: %ENTRYLOG%
  call :wait
  exit /b 2
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%LAUNCHER%" -EntryLogPath "%ENTRYLOG%" -EntryCommandPath "%~f0" -EntryWorkingDirectory "%CD%"
set "EXITCODE=%ERRORLEVEL%"
call :log "Uninstall launcher exit code=%EXITCODE%"

if not "%EXITCODE%"=="0" (
  echo Uninstall failed. Exit code: %EXITCODE%
  echo Log: %ENTRYLOG%
  call :wait
  exit /b %EXITCODE%
)

echo Uninstall completed successfully.
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
