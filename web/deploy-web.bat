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
REM    deploy-web.bat                    functions + hosting (default)
REM    deploy-web.bat all                same as the default, spelled out
REM    deploy-web.bat functions          functions only - download page NOT updated
REM    deploy-web.bat hosting            hosting only  - backend API  NOT updated
REM    deploy-web.bat all nopause        do not wait for a key at the end (CI)
REM                                      env DEPLOY_WEB_NOPAUSE=1 does the same
REM
REM  Why the default is "all" (changed 2026-07-30):
REM  it used to be "functions", so double-clicking this file deployed the
REM  backend and silently left the download page on an old release. The page
REM  sat 8 days behind (old product name, no share button, no one-click save)
REM  while the deploy log said "Deploy complete". "web deploy" now means the
REM  whole web surface; pass an explicit target to narrow it.
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
set "HOSTING_URL=https://%PROJECT%.web.app"
set "RC=0"
set "VERIFY_FAILED="

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=all"

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
REM Spell out what this run touches. A partial deploy must never look total.
if /I "%TARGET%"=="all"       echo     Cloud Functions (backend API) + Hosting (download page)
if /I "%TARGET%"=="functions" echo     Cloud Functions ONLY - the download page will NOT be updated.
if /I "%TARGET%"=="hosting"   echo     Hosting ONLY - the backend API will NOT be updated.
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
echo.

REM ---- Post-deploy check (hosting): is the live page really what we built? ----
REM "Deploy complete" only means the CLI finished. This compares the served
REM bytes against public\, which is the check that would have caught the page
REM being 8 days stale. Byte compare is valid: hosting serves these verbatim.
if /I "%TARGET%"=="functions" goto :hostingCheckDone
echo [check] Comparing the live download page against public\
call :verifyHostingFile index.html
call :verifyHostingFile app.js
call :verifyHostingFile styles.css
if defined VERIFY_FAILED goto :verifyWarn
echo     Hosting URL: %HOSTING_URL%
echo.
goto :hostingCheckDone
:verifyWarn
echo.
echo *** WARNING: the live files do not match public\ ***
echo     The CLI reported success but the served page is not what you built.
echo     - Re-run:  deploy-web.bat hosting
echo     - Still failing: delete the .firebase folder, then deploy again.
echo     - Confirm the release: firebase hosting:channel:list --project %PROJECT%
echo.
:hostingCheckDone

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

REM ---- Compare one live hosting file against its local copy ----
REM  %1 = file name directly under public\ . Sets VERIFY_FAILED on mismatch.
REM  A cache-buster query plus no-cache keeps a CDN/proxy copy of the previous
REM  release from passing as the new one - that copy is exactly what fooled us.
REM  Fetch failure is reported but does not fail the deploy: no network answer
REM  is not evidence of a bad release.
:verifyHostingFile
set "VF_NAME=%~1"
set "VF_TMP=%TEMP%\mcphoto-deploycheck-%VF_NAME%"
if exist "%VF_TMP%" del "%VF_TMP%" >nul 2>&1
curl.exe -s --max-time 25 -H "Cache-Control: no-cache" -o "%VF_TMP%" "%HOSTING_URL%/%VF_NAME%?deploycheck=%RANDOM%%RANDOM%" >nul 2>&1
if not exist "%VF_TMP%" goto :vfNoFetch
fc /b "%VF_TMP%" "public\%VF_NAME%" >nul 2>&1
if errorlevel 1 goto :vfMismatch
echo     - [ OK ] %VF_NAME%
goto :vfCleanup
:vfMismatch
echo     - [FAIL] %VF_NAME% - served bytes differ from public\%VF_NAME%
set "VERIFY_FAILED=1"
goto :vfCleanup
:vfNoFetch
echo     - [ ?? ] %VF_NAME% - could not fetch it, check the page in a browser
goto :eof
:vfCleanup
del "%VF_TMP%" >nul 2>&1
goto :eof
