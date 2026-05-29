@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0flash_release.ps1" %*
if errorlevel 1 (
  echo.
  echo Flash failed.
  pause
  exit /b 1
)
echo.
echo Flash complete.
pause
