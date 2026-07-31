@echo off
chcp 65001 >nul
setlocal EnableExtensions
REM ============================================================
REM  MCPhoto web client (kiosk site) build + deploy
REM  Location: webclient\
REM
REM  Usage:
REM    deploy.bat            build + deploy to hosting:kiosk
REM    deploy.bat build      build only (output: ..\web\kiosk\)
REM
REM  This NEVER touches the P1 download page (hosting:default).
REM  See docs/web-client/01-tech-stack-and-structure.md section 5.2
REM
REM  Requires: firebase login + .env.production.local with VITE_BACKEND_API_KEY
REM
REM  ASCII ONLY - cmd tracks its read position by byte offset and chcp
REM  switches the codepage mid-run.
REM ============================================================

set "PROJECT=mcphoto-955fb"
set "MODE=%~1"
if "%MODE%"=="" set "MODE=deploy"

pushd "%~dp0"

echo [1/3] npm ci
if exist node_modules (
  echo     node_modules present - skipping install
) else (
  call npm ci --no-fund --no-audit
  if errorlevel 1 goto :fail
)

echo [2/3] typecheck + build
call npx tsc --noEmit
if errorlevel 1 goto :fail
call npm run build
if errorlevel 1 goto :fail

if /I "%MODE%"=="build" goto :done

echo [3/3] firebase deploy --only hosting:kiosk
pushd ..\web
call firebase deploy --only hosting:kiosk --project %PROJECT%
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" goto :fail

echo.
echo === Deploy finished ===
echo     Kiosk URL: https://mcphoto-955fb-kiosk.web.app
echo.
goto :done

:fail
echo.
echo *** FAILED ***
popd
exit /b 1

:done
popd
if defined DEPLOY_NOPAUSE exit /b 0
pause
exit /b 0
