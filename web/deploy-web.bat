@echo off
chcp 65001 >nul
setlocal EnableExtensions
REM ============================================================
REM  MC포토 웹 배포 (Cloud Functions / Hosting)
REM  위치: web\ (firebase.json / .firebaserc 와 같은 폴더)
REM
REM  사용법:
REM    deploy-web.bat                    functions 만 배포 (기본)
REM    deploy-web.bat all                functions + hosting(다운로드 페이지)
REM    deploy-web.bat hosting            hosting 만
REM    deploy-web.bat functions nopause  끝나도 대기하지 않음 (자동화/CI용)
REM                                      DEPLOY_WEB_NOPAUSE=1 환경변수도 동일
REM
REM  전제: firebase login 완료 + functions 시크릿 등록됨
REM        (최초 1회는 docs\DEPLOY-WALKTHROUGH.md 참조)
REM  프로젝트: mcphoto-955fb (.firebaserc 기본값)
REM ============================================================

cd /d "%~dp0"

set "PROJECT=mcphoto-955fb"
set "FN_URL=https://asia-northeast3-%PROJECT%.cloudfunctions.net/api"
set "RC=0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=functions"

REM 끝나고 키 입력을 기다린다(더블클릭 실행 시 창이 닫혀 결과를 놓치지 않도록).
set "HOLD=1"
if /I "%~2"=="nopause" set "HOLD="
if /I "%DEPLOY_WEB_NOPAUSE%"=="1" set "HOLD="

REM 대상 오타로 엉뚱한 것이 배포되지 않도록 검증한다.
if /I "%TARGET%"=="functions" goto :targetOk
if /I "%TARGET%"=="all"       goto :targetOk
if /I "%TARGET%"=="hosting"   goto :targetOk
echo *** 알 수 없는 배포 대상: "%TARGET%"
echo     사용 가능: functions ^| all ^| hosting
set "RC=2"
goto :fail
:targetOk

echo === MC포토 웹 배포  project=%PROJECT%  target=%TARGET% ===
echo.

REM hosting 만 배포하면 functions 빌드는 건너뛴다.
if /I "%TARGET%"=="hosting" goto :deploy

REM ---- [1/3] functions 의존성 (tsc 등) ----
echo [1/3] functions 의존성 확인...
if exist "functions\node_modules\.bin\tsc.cmd" goto :haveDeps
echo     - node_modules 없음, npm install 실행
call npm --prefix functions install
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail
goto :depsDone
:haveDeps
echo     - 이미 설치됨 (갱신하려면 functions 에서 npm install)
:depsDone
echo.

REM ---- [2/3] functions 빌드 (tsc) : 배포 전 조기 실패 확인 ----
echo [2/3] functions 빌드 (tsc)...
call npm --prefix functions run build
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :fail
echo.

:deploy
echo [3/3] firebase 배포 (predeploy 훅이 tsc 재빌드)...
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
echo === 배포 완료 ===
if /I "%TARGET%"=="hosting" goto :done

echo 함수 URL: %FN_URL%
echo.

REM ---- 배포 확인: 배포된 함수가 실제로 응답하는지 확인한다 ----
echo [확인] GET %FN_URL%/health
set "HTTP="
for /f %%S in ('curl.exe -s -o nul -w "%%{http_code}" --max-time 20 "%FN_URL%/health" 2^>nul') do set "HTTP=%%S"
if "%HTTP%"=="200"  goto :healthOk
if "%HTTP%"==""     goto :healthUnknown
echo     - 경고: HTTP %HTTP% — 함수가 정상 응답하지 않습니다.
echo       firebase functions:log --project %PROJECT% --only api
goto :healthDone
:healthUnknown
echo     - 확인 불가 (curl 실행 실패). 위 URL 을 브라우저에서 직접 확인하세요.
goto :healthDone
:healthOk
echo     - OK (HTTP 200) — 배포된 함수가 정상 응답합니다.
:healthDone
echo.

echo 스모크 검증(선택) — API 키(CLIENT_API_KEYS 값)를 넣고:
echo     set BASE_URL=%FN_URL%
echo     set API_KEY=여기에_CLIENT_API_KEYS_값
echo     node functions\scripts\post-deploy-smoke.mjs

:done
echo.
call :hold
endlocal
exit /b 0

:fail
echo.
echo *** 배포 중단 (종료 코드 %RC%): 위 오류를 확인하세요. ***
echo     - "tsc 없음" 이면 functions 폴더에 npm install 이 안 된 것입니다.
echo     - "not logged in" 이면 firebase login 먼저.
call :hold
REM endlocal 이 RC 를 지우기 전에 한 줄에서 확장시킨다.
endlocal & exit /b %RC%

REM ---- 결과를 확인할 수 있도록 키 입력까지 대기 ----
:hold
if not defined HOLD goto :eof
echo.
echo 계속하려면 아무 키나 누르세요...
pause >nul
goto :eof
