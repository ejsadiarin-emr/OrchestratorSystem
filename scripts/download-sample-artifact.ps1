# Download real artifacts for testing artifact upload
# All downloads are cached in .artifact-cache/ (gitignored)

$RepoRoot = Split-Path -Parent $PSScriptRoot
$CacheDir = Join-Path $RepoRoot ".artifact-cache"

if (-not (Test-Path $CacheDir)) {
    New-Item -ItemType Directory -Path $CacheDir | Out-Null
}

# --- Go (golang) ---
# Go 1.25.9
$Go1259 = Join-Path $CacheDir "Go_1.25.9.msi"
if (-not (Test-Path $Go1259)) {
    Write-Host "Downloading Go 1.25.9 ..."
    Invoke-WebRequest -Uri "https://go.dev/dl/go1.25.9.windows-amd64.msi" -OutFile $Go1259 -UseBasicParsing
    Write-Host "Saved: $Go1259"
} else {
    Write-Host "Already exists: $Go1259"
}

# Go 1.26.2
$Go1262 = Join-Path $CacheDir "Go_1.26.2.msi"
if (-not (Test-Path $Go1262)) {
    Write-Host "Downloading Go 1.26.2 ..."
    Invoke-WebRequest -Uri "https://go.dev/dl/go1.26.2.windows-amd64.msi" -OutFile $Go1262 -UseBasicParsing
    Write-Host "Saved: $Go1262"
} else {
    Write-Host "Already exists: $Go1262"
}

# --- SSMS (SQL Server Management Studio) ---
# NOTE: SSMS 22+ uses a Visual Studio Installer bootstrapper (vs_SSMS.exe), not a standalone MSI.
# For silent installation: vs_SSMS.exe --quiet --norestart --wait
# Reference: https://learn.microsoft.com/en-us/sql/ssms/install/install

# SSMS 22 (latest)
$SSMS22 = Join-Path $CacheDir "SSMS_22.0.exe"
if (-not (Test-Path $SSMS22)) {
    Write-Host "Downloading SSMS 22 (Visual Studio Installer bootstrapper)..."
    Invoke-WebRequest -Uri "https://aka.ms/ssms/22/release/vs_SSMS.exe" -OutFile $SSMS22 -UseBasicParsing
    Write-Host "Saved: $SSMS22 (bootstrapper, not MSI)"
} else {
    Write-Host "Already exists: $SSMS22"
}

# NOTE: Microsoft does not provide stable direct-download URLs for older SSMS versions (19.x/20.x).
# SSMS 19/20 used SSMS-Setup-ENU.exe with /Install /Quiet /NoRestart switches.
# To obtain an older SSMS installer, download manually from the SSMS Release History:
# https://learn.microsoft.com/en-us/sql/ssms/release-notes-ssms
#
# For testing with 2 SSMS versions, you can:
# 1. Download SSMS 19.x from the release history page
# 2. Rename it to match the PackageId_Version.ext format, e.g., SSMS_19.3.exe
# 3. Place it in .artifact-cache/ alongside the other files

Write-Host ""
Write-Host "=== Download Summary ==="
Write-Host "Go 1.25.9:     $Go1259"
Write-Host "Go 1.26.2:     $Go1262"
Write-Host "SSMS 22:       $SSMS22 (bootstrapper)"
Write-Host ""
Write-Host "Upload examples:"
Write-Host "  curl.exe -X POST http://localhost:5000/api/artifacts/upload -F `"packageId=Go`" -F `"version=1.25.9`" -F `"packageName=Go`" -F `"file=@$Go1259`""
Write-Host "  curl.exe -X POST http://localhost:5000/api/artifacts/upload -F `"packageId=Go`" -F `"version=1.26.2`" -F `"packageName=Go`" -F `"file=@$Go1262`""
Write-Host "  curl.exe -X POST http://localhost:5000/api/artifacts/import -F `"files=@$Go1259`" -F `"files=@$Go1262`""
