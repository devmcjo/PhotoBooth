@echo off
chcp 65001 >nul
setlocal
REM ============================================================
REM  MC포토 웹 배포 (Cloud Functions / Hosting)
REM  위치: web\ (firebase.json / .firebaserc 와 같은 폴더)
REM
REM  사용법:
REM    deploy-web.bat            functions 만 배포 (기본)
REM    deploy-web.bat all        functions + hosting(다운로드 페이지)
REM    deploy-web.bat hosting    hosting 만
REM
REM  전제: firebase login 완료 + functions 시크릿 등록됨
REM        (최초 1회는 docs\DEPLOY-WALKTHROUGH.md 참조)
REM  프로젝트: mcphoto-955fb (.firebaserc 기본값)
REM ============================================================

cd /d "%~dp0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=functions"

echo === MC포토 웹 배포  project=mcphoto-955fb  target=%TARGET% ===
echo.

REM hosting 만 배포하면 functions 빌드는 건너뛴다.
if /I "%TARGET%"=="hosting" goto :deploy

REM ---- [1/3] functions 의존성 (tsc 등) ----
echo [1/3] functions 의존성 확인...
if exist "functions\node_modules\.bin\tsc.cmd" goto :haveDeps
echo     - node_modules 없음, npm install 실행
call npm --prefix functions install
if errorlevel 1 goto :fail
goto :depsDone
:haveDeps
echo     - 이미 설치됨 (갱신하려면 functions 에서 npm install)
:depsDone
echo.

REM ---- [2/3] functions 빌드 (tsc) : 배포 전 조기 실패 확인 ----
echo [2/3] functions 빌드 (tsc)...
call npm --prefix functions run build
if errorlevel 1 goto :fail
echo.

:deploy
echo [3/3] firebase 배포 (predeploy 훅이 tsc 재빌드)...
if /I "%TARGET%"=="all"     goto :deployAll
if /I "%TARGET%"=="hosting" goto :deployHosting
call firebase deploy --only functions --project mcphoto-955fb
goto :after
:deployAll
call firebase deploy --only functions,hosting --project mcphoto-955fb
goto :after
:deployHosting
call firebase deploy --only hosting --project mcphoto-955fb
goto :after

:after
if errorlevel 1 goto :fail
echo.
echo === 배포 완료 ===
if /I "%TARGET%"=="hosting" goto :done
echo 함수 URL: https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api
echo.
echo 스모크 검증(선택) — API 키(CLIENT_API_KEYS 값)를 넣고:
echo     set BASE_URL=https://asia-northeast3-mcphoto-955fb.cloudfunctions.net/api
echo     set API_KEY=여기에_CLIENT_API_KEYS_값
echo     node functions\scripts\post-deploy-smoke.mjs
:done
endlocal
exit /b 0

:fail
echo.
echo *** 배포 중단: 위 오류를 확인하세요. ***
echo     - "tsc 없음" 이면 functions 폴더에 npm install 이 안 된 것입니다.
echo     - "not logged in" 이면 firebase login 먼저.
endlocal
exit /b 1
