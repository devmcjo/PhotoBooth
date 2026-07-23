<#
  MCPhoto 베타 배포용 단일 EXE 빌드 스크립트

  사용법 (프로젝트 루트에서):
      pwsh ./publish.ps1              # 또는  powershell -File .\publish.ps1

  결과:
      publish\MCPhoto\MCPhoto.exe    (항상 이 한 경로로만 생성 — 폴더 뒤죽박죽 없음)

  특징:
    - 자체 포함(.NET 런타임 내장) → 대상 PC에 .NET 설치 불필요
    - 단일 파일(PublishSingleFile) → exe 1개 + Frame\, branding.ini.sample 정도만
    - Release 구성, win-x64

  일반 빌드(디버그/실행)는 이 스크립트와 무관하게 기존 경로에 그대로 나옵니다:
      dotnet build   →  src\MCPhoto.App\bin\Debug\net8.0-windows\
      dotnet build -c Release  →  src\MCPhoto.App\bin\Release\net8.0-windows\
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\MCPhoto.App\MCPhoto.App.csproj'
$out  = Join-Path $root 'publish\MCPhoto'

# 실행 중이면 출력 파일이 잠겨 실패하므로 먼저 안내
$running = Get-Process MCPhoto -ErrorAction SilentlyContinue
if ($running) {
    Write-Warning "MCPhoto.exe가 실행 중입니다(PID: $($running.Id -join ', ')). 앱을 종료한 뒤 다시 실행하세요."
    return
}

Write-Host "publish 중... → $out" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -o $out

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n완료: $out\MCPhoto.exe" -ForegroundColor Green
    Get-ChildItem $out -Recurse -File |
        ForEach-Object { '{0,8:N2} MB  {1}' -f ($_.Length/1MB), $_.FullName.Substring($out.Length+1) }
} else {
    Write-Error "publish 실패 (exit $LASTEXITCODE)"
}
