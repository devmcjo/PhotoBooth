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
param(
    # Also build the Inno Setup installer after a successful publish.
    # Off by default: publish is the fast inner loop, packaging is a release step.
    [switch]$Installer
)

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

# ---- Installer (-Installer) ----
# The .iss reads AppVersion from the exe's version resource, so it can never disagree with the
# binary it packages - no version is passed from here on purpose.
# It also ships ONLY three things (exe / licenses / tools). Everything else that accumulates in
# this reused output folder (MCPhoto.ini, result\, branding.ini.sample) is left out by the
# whitelist in [Files], so a stale test setting cannot leak into a shipped installer.
if (-not $Installer) {
    Write-Host "`nInstaller: skipped (pass -Installer to build it)." -ForegroundColor DarkGray
    return
}

$iss = Join-Path $root 'installer\MCPhoto.iss'
if (-not (Test-Path -LiteralPath $iss -PathType Leaf)) {
    Write-Warning "Installer script not found: $iss"
    return
}

# ISCC.exe is not on PATH by default. Look in both Program Files trees, then PATH as a fallback.
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }
if (-not $isccCandidates) {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $isccCandidates = @($onPath.Source) }
}

if (-not $isccCandidates) {
    # Not an error: the publish above succeeded and is usable on its own.
    Write-Warning "Inno Setup 6 not found - installer skipped. Install from https://jrsoftware.org/isdl.php"
    return
}

$iscc = $isccCandidates[0]
Write-Host "`nBuilding installer with: $iscc" -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) {
    Write-Error "installer build failed (exit $LASTEXITCODE)"
    return
}

# OutputBaseFilename is MCPhoto-Setup-<version>; Inno writes to installer\Output by default.
$setup = Get-ChildItem (Join-Path $root 'installer\Output') -Filter 'MCPhoto-Setup-*.exe' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($setup) {
    Write-Host ('Installer: {0}  ({1:N1} MB)' -f $setup.FullName, ($setup.Length/1MB)) -ForegroundColor Green
} else {
    Write-Warning "Installer built but the output file was not found under installer\Output."
}
