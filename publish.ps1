<#
  MCPhoto beta single-EXE build script.

  Usage (from project root):
      Double-click publish.bat            (recommended, service key INCLUDED)
      Double-click publish-nokey.bat      (service key EXCLUDED / offline build)
      or:  powershell -ExecutionPolicy Bypass -File .\publish.ps1
      or:  powershell -ExecutionPolicy Bypass -File .\publish.ps1 -NoServiceKey
      or:  powershell -ExecutionPolicy Bypass -File .\publish.ps1 -KeyPath C:\path\serviceAccountKey.json

  Parameters:
      -KeyPath <string>   Explicit service account key path (highest priority source).
      -NoServiceKey       Skip key lookup/copy (offline build, no server connectivity).

  Service account key (it10 S1):
      By default the Admin service account key is copied into the publish output so the
      beta EXE connects to Firebase on a QA PC (login + default frame download).
      Key source priority (first existing file wins):
        1. -KeyPath argument
        2. $env:MCPHOTO_SERVICE_KEY
        3. %ProgramData%\MCPhoto\serviceAccountKey.json
        4. repo root serviceAccountKey.json
      If no key is found, publish still succeeds (offline build) with a warning.
      WARNING: a key-included folder grants Admin access to Firestore/Storage.
               Internal beta only - do NOT distribute externally.

  Output:
      publish\MCPhoto\MCPhoto.exe                 (always this single path)
      publish\MCPhoto\serviceAccountKey.json      (only when key found and not -NoServiceKey)

  Notes:
    - Self-contained (.NET runtime embedded) -> no .NET install needed on target PC
    - Single file (PublishSingleFile) + bundled ffmpeg (tools\ffmpeg) for timelapse
    - Release, win-x64
    - Key is git-ignored (.gitignore covers serviceAccountKey.json and publish/).
    - App loads exe-folder key first, so no app code change is needed.
    - ASCII only on purpose: avoids CP949/UTF-8 console mojibake on Korean Windows.
    - Normal build output is unchanged:
        dotnet build             -> src\MCPhoto.App\bin\Debug\net8.0-windows\
        dotnet build -c Release  -> src\MCPhoto.App\bin\Release\net8.0-windows\
#>

param(
    [string]$KeyPath = '',
    [switch]$NoServiceKey
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

Write-Host "Publishing... -> $out" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -o $out

if ($LASTEXITCODE -ne 0) {
    Write-Error "publish failed (exit $LASTEXITCODE)"
    return
}

Write-Host "`nDone: $out\MCPhoto.exe" -ForegroundColor Green
Get-ChildItem $out -Recurse -File |
    ForEach-Object { '{0,8:N2} MB  {1}' -f ($_.Length/1MB), $_.FullName.Substring($out.Length+1) }

# ---- Service account key bundling (it10 S1) ----
$keyDest       = Join-Path $out 'serviceAccountKey.json'
$keyIncluded   = $false
$keySource     = ''

if ($NoServiceKey) {
    Write-Host "`n-NoServiceKey specified: skipping service key copy (offline build)." -ForegroundColor Yellow
} else {
    # Build source candidate list in priority order; first existing file wins.
    $candidates = @()
    if ($KeyPath)                  { $candidates += $KeyPath }
    if ($env:MCPHOTO_SERVICE_KEY)  { $candidates += $env:MCPHOTO_SERVICE_KEY }
    if ($env:ProgramData)          { $candidates += (Join-Path $env:ProgramData 'MCPhoto\serviceAccountKey.json') }
    $candidates += (Join-Path $root 'serviceAccountKey.json')

    foreach ($cand in $candidates) {
        if ($cand -and (Test-Path -LiteralPath $cand -PathType Leaf)) {
            $keySource = (Resolve-Path -LiteralPath $cand).Path
            break
        }
    }

    if ($keySource) {
        Copy-Item -LiteralPath $keySource -Destination $keyDest -Force
        $keyIncluded = $true
        Write-Host "`n============================================================" -ForegroundColor Red
        Write-Host " WARNING: Admin service key INCLUDED - internal beta only." -ForegroundColor Red
        Write-Host "          Do NOT distribute this folder externally." -ForegroundColor Red
        Write-Host "============================================================" -ForegroundColor Red
    } else {
        Write-Host "`nService key NOT found - offline build (server features disabled on target PC)." -ForegroundColor Yellow
        Write-Host "Searched: $($candidates -join '; ')" -ForegroundColor Yellow
    }
}

# ---- Summary ----
Write-Host ""
if ($keyIncluded) {
    Write-Host ("Service key: INCLUDED (source: {0})" -f $keySource) -ForegroundColor Green
} else {
    Write-Host "Service key: NOT INCLUDED" -ForegroundColor Yellow
}
