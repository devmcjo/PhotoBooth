@echo off
rem ============================================================
rem  MCPhoto beta single-EXE build (double-click)
rem  Runs publish.ps1 via Windows PowerShell.
rem  Output: publish\MCPhoto\MCPhoto.exe
rem  (ASCII only on purpose: avoids CP949/UTF-8 batch parsing issues)
rem ============================================================
cd /d "%~dp0"

echo Starting MCPhoto publish...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"

echo.
echo ------------------------------------------------------------
echo Press any key to close.
pause >nul
