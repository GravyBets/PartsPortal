@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Push-Update.ps1"
if errorlevel 1 (
  echo.
  echo Push update failed. Review the error above.
  pause
)
