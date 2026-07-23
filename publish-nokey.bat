@echo off
rem ============================================================
rem  MCPhoto beta single-EXE build (double-click) - NO SERVICE KEY
rem  Runs publish.ps1 -NoServiceKey via Windows PowerShell.
rem  Output: publish\MCPhoto\MCPhoto.exe (offline build, no key bundled)
rem  Use this for builds that must NOT contain the Admin service key.
rem  (ASCII only on purpose: avoids CP949/UTF-8 batch parsing issues)
rem ============================================================
cd /d "%~dp0"

echo Starting MCPhoto publish (NO service key)...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" -NoServiceKey

echo.
echo ------------------------------------------------------------
echo Press any key to close.
pause >nul
