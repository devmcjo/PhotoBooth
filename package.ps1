<#
  MCPhoto release packaging script - produces the Windows installer.

  Usage (from project root):
      Double-click package.bat
      or:  powershell -ExecutionPolicy Bypass -File .\package.ps1
      or:  powershell -ExecutionPolicy Bypass -File .\package.ps1 -SkipPublish

  What it does:
      1. Runs publish.ps1 (fresh self-contained single exe)   <- skip with -SkipPublish
      2. Compiles installer\MCPhoto.iss with Inno Setup (ISCC.exe)
      3. Reports the resulting setup file

  Why this is separate from publish.ps1:
      publish.ps1 is the fast inner loop used for testing. Packaging is a release step.
      Keeping them apart means a test publish can never accidentally look like a shippable
      installer, and this script can guarantee freshness on its own terms.

  Why it re-publishes by default:
      The publish output folder is REUSED. If packaging just wrapped whatever is sitting there,
      a release could ship a stale exe while the installer name/version (read from that exe)
      looked plausible. Republishing removes that whole class of drift.
      -SkipPublish exists for iterating on the .iss alone, right after a publish.

  Version:
      Not passed from here. MCPhoto.iss reads it from the exe's own version resource, so the
      installer cannot disagree with the binary it packages. Bump <Version> in
      Directory.Build.props to change it.

  ASCII only on purpose: avoids CP949/UTF-8 console mojibake on Korean Windows.
#>
param(
    # Package whatever is already in publish\MCPhoto instead of building it again.
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $root 'publish\MCPhoto'
$exe  = Join-Path $out 'MCPhoto.exe'
$iss  = Join-Path $root 'installer\MCPhoto.iss'

# ---- 1. Publish ----
if ($SkipPublish) {
    Write-Host "Publish: skipped (-SkipPublish) - packaging the existing output." -ForegroundColor Yellow
} else {
    $publish = Join-Path $root 'publish.ps1'
    if (-not (Test-Path -LiteralPath $publish -PathType Leaf)) {
        Write-Error "publish.ps1 not found: $publish"
        return
    }
    Write-Host "=== 1/2  Publishing ===" -ForegroundColor Cyan
    # Dot-source so a 'return' inside publish.ps1 (app running, publish failed) lands here and
    # we can stop before packaging a half-built folder.
    & $publish
    if ($LASTEXITCODE -ne 0) {
        Write-Error "publish failed (exit $LASTEXITCODE) - not packaging."
        return
    }
}

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    Write-Error "Published exe not found: $exe`nRun publish first (or drop -SkipPublish)."
    return
}
if (-not (Test-Path -LiteralPath $iss -PathType Leaf)) {
    Write-Error "Installer script not found: $iss"
    return
}

# ---- 2. Locate ISCC.exe ----
# Do NOT hardcode a version folder. Inno Setup 7 (current) installs alongside 6, ships 32/64-bit
# editions, and a future 8 will land in yet another folder - a pinned 'Inno Setup 6' path silently
# stops finding the compiler. Enumerate every 'Inno Setup *' folder in both Program Files trees
# and take the highest version, so new majors are picked up with no edit here.
$isccCandidates = @()
foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if (-not $pf) { continue }
    $isccCandidates += Get-ChildItem -LiteralPath $pf -Directory -Filter 'Inno Setup*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
}
# PATH fallback (custom install location, or a portable copy).
if (-not $isccCandidates) {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $isccCandidates = @($onPath.Source) }
}

if (-not $isccCandidates) {
    Write-Error @"
Inno Setup not found - cannot build the installer.
Install it from https://jrsoftware.org/isdl.php (version 7 is current; 6 also works),
then run this script again. The published exe in '$out' is unaffected and still usable.
"@
    return
}

# Highest file version wins (7.x over 6.x). Ties fall back to path order.
# ⚠️ Build the version from FileMajorPart/FileMinorPart, NOT VersionInfo.FileVersionRaw:
#    FileVersionRaw is a PowerShell 7+ extended property and package.bat runs Windows
#    PowerShell 5.1, where it is absent (the sort would silently compare nulls).
$iscc = $isccCandidates |
    Sort-Object -Property @{ Expression = {
        $vi = (Get-Item -LiteralPath $_).VersionInfo
        New-Object System.Version ($vi.FileMajorPart, $vi.FileMinorPart, $vi.FileBuildPart, $vi.FilePrivatePart)
    } } -Descending |
    Select-Object -First 1
$isccVer = (Get-Item -LiteralPath $iscc).VersionInfo.FileVersion
if ($isccCandidates.Count -gt 1) {
    Write-Host "Inno Setup: found $($isccCandidates.Count) installs, using the newest." -ForegroundColor DarkGray
}

# ---- 3. Compile ----
Write-Host "`n=== 2/2  Building installer ===" -ForegroundColor Cyan
Write-Host "ISCC: $iscc  (v$isccVer)" -ForegroundColor DarkGray
& $iscc $iss
if ($LASTEXITCODE -ne 0) {
    Write-Error "installer build failed (exit $LASTEXITCODE)"
    return
}

# ---- 4. Report ----
# OutputBaseFilename is MCPhoto-Setup-<version>; Inno writes to installer\Output by default.
$setup = Get-ChildItem (Join-Path $root 'installer\Output') -Filter 'MCPhoto-Setup-*.exe' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) {
    Write-Warning "Installer reported success but no MCPhoto-Setup-*.exe was found under installer\Output."
    return
}

$appVer = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
Write-Host "`nDone." -ForegroundColor Green
Write-Host ('  Installer : {0}' -f $setup.FullName) -ForegroundColor Green
Write-Host ('  Size      : {0:N1} MB' -f ($setup.Length/1MB)) -ForegroundColor Green
Write-Host ('  App exe   : {0} (version resource {1})' -f $exe, $appVer) -ForegroundColor Green
Write-Host "`nShipped contents: MCPhoto.exe + licenses\ + tools\  (see installer\MCPhoto.iss [Files])" -ForegroundColor DarkGray
