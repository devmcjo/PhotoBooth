<#
  MCPhoto beta single-EXE build script.

  Usage (from project root):
      Double-click publish.bat            (recommended)
      or:  powershell -ExecutionPolicy Bypass -File .\publish.ps1

  Output:
      publish\MCPhoto\MCPhoto.exe         (always this single path)

  Notes:
    - Self-contained (.NET runtime embedded) -> no .NET install needed on target PC
    - Single file (PublishSingleFile) + bundled ffmpeg (tools\ffmpeg) for timelapse
    - Release, win-x64
    - Normal build output is unchanged:
        dotnet build             -> src\MCPhoto.App\bin\Debug\net8.0-windows\
        dotnet build -c Release  -> src\MCPhoto.App\bin\Release\net8.0-windows\
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

Write-Host "Publishing... -> $out" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -o $out

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nDone: $out\MCPhoto.exe" -ForegroundColor Green
    Get-ChildItem $out -Recurse -File |
        ForEach-Object { '{0,8:N2} MB  {1}' -f ($_.Length/1MB), $_.FullName.Substring($out.Length+1) }
} else {
    Write-Error "publish failed (exit $LASTEXITCODE)"
}
