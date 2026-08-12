@echo off
rem ============================================================
rem  MCPhoto release packaging (double-click)
rem  Runs package.ps1: publish -> Inno Setup -> installer\Output
rem  Output: installer\Output\MCPhoto-Setup-<version>.exe
rem
rem  This is the RELEASE path. For plain testing use publish.bat,
rem  which only builds publish\MCPhoto\MCPhoto.exe.
rem  (ASCII only on purpose: avoids CP949/UTF-8 batch parsing issues)
rem ============================================================
cd /d "%~dp0"

echo Starting MCPhoto packaging...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1" %*

echo.
echo ------------------------------------------------------------
echo Press any key to close.
pause >nul
