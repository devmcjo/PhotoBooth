<#
  MCPhoto single-EXE build script (KEYLESS / backend-only, gate key EMBEDDED in exe).

  Usage (from project root):
      Double-click publish.bat
      or:  powershell -ExecutionPolicy Bypass -File .\publish.ps1

  Backend-only (no serviceAccountKey.json):
      DB access goes through the backend (Cloud Functions). No Admin service key is bundled.
      The backend gate key (one of CLIENT_API_KEYS) is EMBEDDED into the exe at publish time
      via -p:BackendApiKeyDefault (AssemblyMetadata) so a bare exe works with NO ini.
      Key source priority (first found wins), read locally and never committed:
        1. $env:MCPHOTO_BACKEND_API_KEY
        2. repo root 'backend-apikey.local'  (git-ignored)
      If not found, publish still succeeds but the exe has no embedded key
      (backend auth will fail until a key is provided; add 'BackendApiKey=<key>' to MCPhoto.ini).
      NOTE: the embedded key is extractable by decompiling the exe (like any client key).
            It is a LOW-VALUE, revocable gate key; real security is server-side (JWT + roles +
            server-only service account). ini 'BackendApiKey=' overrides the embedded default.

  Output:
      publish\MCPhoto\MCPhoto.exe     (single self-contained exe, gate key embedded)

  Notes:
    - Self-contained (.NET runtime embedded) -> no .NET install needed on target PC
    - Single file (PublishSingleFile) + bundled ffmpeg (tools\ffmpeg) for timelapse
    - Release, win-x64
    - Version display: read from the exe itself (assembly version resource + exe timestamp).
      Bump <Version> in Directory.Build.props to change it. No bldinfo.ini to ship (removed).
    - App defaults: UseBackend=true, BackendBaseUrl + GoogleClientId built in.
    - ASCII only on purpose: avoids CP949/UTF-8 console mojibake on Korean Windows.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\MCPhoto.App\MCPhoto.App.csproj'
$out  = Join-Path $root 'publish\MCPhoto'

# If the app is running the output exe is locked and publish fails.
$running = Get-Process MCPhoto -ErrorAction SilentlyContinue
if ($running) {
    Write-Warning "MCPhoto.exe is running (PID: $($running.Id -join ', ')). Close the app, then run again."
    return
}

# ---- Backend gate key (embedded into exe, NOT committed) ----
$apiKey = ''
if ($env:MCPHOTO_BACKEND_API_KEY) {
    $apiKey = $env:MCPHOTO_BACKEND_API_KEY.Trim()
} else {
    $keyFile = Join-Path $root 'backend-apikey.local'
    if (Test-Path -LiteralPath $keyFile -PathType Leaf) {
        $apiKey = (Get-Content -LiteralPath $keyFile -Raw).Trim()
    }
}

$pubArgs = @(
    $proj, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    '-o', $out
)
if ($apiKey) {
    $pubArgs += "-p:BackendApiKeyDefault=$apiKey"
    Write-Host "Backend gate key: EMBEDDED into exe (source: local, not committed)." -ForegroundColor Green
} else {
    Write-Host "Backend gate key: NOT found - exe will have NO embedded key." -ForegroundColor Yellow
    Write-Host "  Set env MCPHOTO_BACKEND_API_KEY or create '$root\backend-apikey.local'," -ForegroundColor Yellow
    Write-Host "  or add 'BackendApiKey=<key>' to MCPhoto.ini on the target PC." -ForegroundColor Yellow
}

# ---- Stale license notices ----
# 'dotnet publish' never deletes files it no longer produces, and this output folder is reused
# across releases. When a notice file is renamed (it24: README.txt -> NOTICE.txt), the old file
# keeps shipping next to the new one. Two legal notices that say different things is worse than
# one: the app also lists any undeclared notice file on screen as a deployment defect.
# Wiping only 'licenses' is enough - csproj recreates all of it on every publish.
$licDir = Join-Path $out 'licenses'
if (Test-Path -LiteralPath $licDir) {
    Remove-Item -LiteralPath $licDir -Recurse -Force
    Write-Host "Cleaned stale license notices: $licDir" -ForegroundColor DarkGray
}

Write-Host "Publishing... -> $out" -ForegroundColor Cyan
dotnet publish @pubArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "publish failed (exit $LASTEXITCODE)"
    return
}

# ---- Version report ----
# No file to copy: the app reads its version from its own assembly version resource and its
# build time from the exe timestamp. Echo both so the operator can confirm what shipped.
$exe = Join-Path $out 'MCPhoto.exe'
if (Test-Path -LiteralPath $exe -PathType Leaf) {
    $fv = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    $bt = (Get-Item -LiteralPath $exe).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
    Write-Host "Version resource: $fv   Build time: $bt" -ForegroundColor Green
}

Write-Host "`nDone: $out\MCPhoto.exe" -ForegroundColor Green
Get-ChildItem $out -Recurse -File |
    ForEach-Object { '{0,8:N2} MB  {1}' -f ($_.Length/1MB), $_.FullName.Substring($out.Length+1) }
