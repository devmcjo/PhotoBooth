@echo off
rem ============================================================
rem  MCPhoto 베타 단일 EXE 빌드 (더블클릭 실행)
rem  - publish.ps1 을 Windows PowerShell로 실행합니다.
rem  - 결과: publish\MCPhoto\MCPhoto.exe (항상 이 경로)
rem ============================================================
chcp 65001 >nul
cd /d "%~dp0"

echo MCPhoto publish 를 시작합니다...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"

echo.
echo ------------------------------------------------------------
echo 창을 닫으려면 아무 키나 누르세요.
pause >nul
