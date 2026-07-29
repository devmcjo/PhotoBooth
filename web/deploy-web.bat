@echo off
REM UTF-8 console: npm / firebase / tsc all print UTF-8 (and box chars).
REM Safe here ONLY because this file is pure ASCII - see the note below.
chcp 65001 >nul
setlocal EnableExtensions
REM ============================================================
REM  MCPhoto web deploy (Cloud Functions / Hosting)
REM  Location: web\ (same folder as firebase.json / .firebaserc)
REM
REM  Usage:
REM    deploy-web.bat                    functions only (default)
REM    deploy-web.bat all                functions + hosting (download page)
REM    deploy-web.bat hosting            hosting only
REM    deploy-web.bat functions nopause  do not wait for a key at the end (CI)
REM                                      env DEPLOY_WEB_NOPAUSE=1 does the same
REM
REM  Requires: firebase login done + functions secrets registered
REM  Project: mcphoto-955fb (.firebaserc default)
REM
REM  ASCII ONLY - do not put Korean (or any non-ASCII) text in this file.
REM  Why: cmd tracks its read position in this file by BYTE offset. When
REM  chcp switches the codepage mid-run, a multi-byte character makes that
REM  offset land mid-character, so the rest of a line gets executed as a
REM  command - REM comments included. That once ran "firebase functions:
REM  secrets:set" straight out of a comment. With pure ASCII, 1 byte = 1
REM  char in every codepage, so chcp above is harmless.
REM  Same rule as publish.bat / publish.ps1.
REM ============================================================

cd /d "%~dp0"

set "PROJECT=mcphoto-955fb"
set "FN_URL=https://asia-northeast3-%PROJECT%.cloudfunctions.net/api"
set "RC=0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=functions"

REM Wait for a key at the end so a double-clicked window does not vanish.
set "HOLD=1"
if /I "%~2"=="nopause" set "HOLD="
if /I "%DEPLOY_WEB_NOPAUSE%"=="1" set "HOLD="

REM Validate the target so a typo cannot deploy the wrong thing.
if /I "%TARGET%"=="functions" goto :targetOk
if /I "%TARGET%"=="all"       goto :targetOk
if /I "%TARGET%"=="hosting"   goto :targetOk
echo *** Unknown deploy target: "%TARGET%"
echo     Valid: functions ^| all ^| hosting
set "RC=2"
goto :fail
:targetOk

echo === MCPhoto web deploy  project=%PROJECT%  target=%TARGET% ===
echo.

REM hosting-only skips the functions build.
if /I "%TARGET%"=="hosting" goto :deploy

REM ---- [1/3] functions dependencies (tsc etc.) ----
echo [1/3] Checking functions dependencies...
if exist "functions\node_modules\.bin\tsc.cmd" goto :haveDeps
echo     - node_modules missing, running npm install
call npm --prefix functions install
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail
goto :depsDone
:haveDeps
echo     - already installed (to refresh: npm install inside functions)
:depsDone
echo.

REM ---- [2/3] functions build (tsc): fail early, before deploying ----
echo [2/3] Building functions (tsc)...
call npm --prefix functions run build
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail
echo.

:deploy
echo [3/3] firebase deploy (predeploy hook rebuilds tsc)...
if /I "%TARGET%"=="all"     goto :deployAll
if /I "%TARGET%"=="hosting" goto :deployHosting
call firebase deploy --only functions --project %PROJECT%
set "RC=%ERRORLEVEL%"
goto :after
:deployAll
call firebase deploy --only functions,hosting --project %PROJECT%
set "RC=%ERRORLEVEL%"
goto :after
:deployHosting
call firebase deploy --only hosting --project %PROJECT%
set "RC=%ERRORLEVEL%"
goto :after

:after
if not "%RC%"=="0" goto :fail
echo.
echo === Deploy finished ===
if /I "%TARGET%"=="hosting" goto :done

echo Function URL: %FN_URL%
echo.

REM ---- Post-deploy check: does the deployed function actually answer? ----
echo [check] GET %FN_URL%/health
set "HTTP="
for /f %%S in ('curl.exe -s -o nul -w "%%{http_code}" --max-time 20 "%FN_URL%/health" 2^>nul') do set "HTTP=%%S"
if "%HTTP%"=="200"  goto :healthOk
if "%HTTP%"==""     goto :healthUnknown
echo     - WARNING: HTTP %HTTP% - the function is not answering normally.
echo       firebase functions:log --project %PROJECT% --only api
goto :healthDone
:healthUnknown
echo     - Could not check (curl failed). Open the URL above in a browser.
goto :healthDone
:healthOk
echo     - OK (HTTP 200) - the deployed function answers.
:healthDone
echo.

echo Optional smoke test - set your API key (a CLIENT_API_KEYS value) first:
echo     set BASE_URL=%FN_URL%
echo     set API_KEY=your_client_api_key
echo     node functions\scripts\post-deploy-smoke.mjs

:done
echo.
call :hold
endlocal
exit /b 0

:fail
echo.
echo *** Deploy aborted (exit code %RC%): check the errors above. ***
echo     - "tsc not found" means npm install was never run in functions.
echo     - "not logged in" means you need firebase login first.
call :hold
REM Expand RC on the same line, before endlocal clears it.
endlocal & exit /b %RC%

REM ---- Keep the window open so the result stays readable ----
:hold
if not defined HOLD goto :eof
echo.
echo Press any key to continue...
pause >nul
goto :eof
