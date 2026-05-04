#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads "Amazing Workload" and "SSMS Workload" installer binaries and generates manifests.
.DESCRIPTION
    Downloads both older and newer versions of the packages defined in the
    "Amazing Workload" workload (DBeaver and Python) and the separate
    "SSMS Workload" workload, and generates manifest files for each version.

    Features:
    - Caches installers locally to avoid redundant downloads.
    - Outputs manifests and final zip archives directly to dist/artifacts/.
    - Re-creates zip archives only when source manifests or installers change.
    - Creates separate zip archives for Amazing Workload and SSMS Workload.

    Note: SSMS installers are bootstrappers that download additional payload
    during installation; internet access is required on the target machine
    during the Install step.
#>

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Split-Path -Parent $ScriptDir
$OutputDir = Join-Path (Join-Path $ProjectDir "dist") "artifacts"
$WorkloadsDir = Join-Path (Join-Path $ProjectDir "dist") "workloads"
$CacheDir = Join-Path $ProjectDir ".artifact-cache"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $WorkloadsDir -Force | Out-Null
New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null

# --- Older versions (from workloads-older.json) ---
$DbeaverVersionOlder = "24.3.0"
$PythonVersionOlder = "3.13.3"
$SsmsVersionOlder = "19.3"

# --- Newer versions (from workloads-newer.json) ---
$DbeaverVersionNewer = "26.0.3"
$PythonVersionNewer = "3.14.4"
$SsmsVersionNewer = "22.5.2"

# Filenames
$DbeaverExeOlder = "dbeaver-ce-${DbeaverVersionOlder}-x86_64-setup.exe"
$PythonExeOlder = "python-${PythonVersionOlder}-amd64.exe"
$SsmsExeOlder = "SSMS-Setup-ENU-${SsmsVersionOlder}.exe"

$DbeaverExeNewer = "dbeaver-ce-${DbeaverVersionNewer}-windows-x86_64.exe"
$PythonExeNewer = "python-${PythonVersionNewer}-amd64.exe"
$SsmsExeNewer = "vs_SSMS-${SsmsVersionNewer}.exe"

# Download URLs
$DbeaverUrlOlder = "https://github.com/dbeaver/dbeaver/releases/download/${DbeaverVersionOlder}/${DbeaverExeOlder}"
$PythonUrlOlder = "https://www.python.org/ftp/python/${PythonVersionOlder}/${PythonExeOlder}"
$SsmsUrlOlder = "https://go.microsoft.com/fwlink/?linkid=2257624&clcid=0x409"

$DbeaverUrlNewer = "https://github.com/dbeaver/dbeaver/releases/download/${DbeaverVersionNewer}/${DbeaverExeNewer}"
$PythonUrlNewer = "https://www.python.org/ftp/python/${PythonVersionNewer}/${PythonExeNewer}"
$SsmsUrlNewer = "https://aka.ms/ssms/22/release/vs_SSMS.exe"

function Get-CachedFilePath {
    param([string]$FileName)
    return Join-Path $CacheDir $FileName
}

function Download-File {
    param(
        [string]$Url,
        [string]$Destination
    )
    if (Test-Path $Destination) {
        $size = (Get-Item $Destination).Length
        Write-Host "Using cached $(Split-Path -Leaf $Destination) ($([math]::Round($size / 1MB, 2)) MB)"
        return
    }

    Write-Host "Downloading $(Split-Path -Leaf $Destination) ..."
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
    $size = (Get-Item $Destination).Length
    Write-Host "  -> $([math]::Round($size / 1MB, 2)) MB"
}

function Set-ContentIfChanged {
    param(
        [string]$Path,
        [string]$Value
    )
    if (Test-Path $Path) {
        $existing = Get-Content -Path $Path -Raw -Encoding UTF8
        if ($existing -eq $Value) {
            return
        }
    }
    Set-Content -Path $Path -Value $Value -Encoding UTF8 -NoNewline
}

function Test-ZipNeedsRebuild {
    param(
        [string]$ZipPath,
        [string[]]$SourcePaths
    )
    if (-not (Test-Path $ZipPath)) {
        return $true
    }
    $zipTime = (Get-Item $ZipPath).LastWriteTimeUtc
    foreach ($src in $SourcePaths) {
        if (-not (Test-Path $src)) {
            return $true
        }
        if ((Get-Item $src).LastWriteTimeUtc -gt $zipTime) {
            return $true
        }
    }
    return $false
}

function New-ZipArchive {
    param(
        [string]$ZipPath,
        [string[]]$SourcePaths
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }
    $archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($src in $SourcePaths) {
            $entryName = Split-Path -Leaf $src
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $src, $entryName)
        }
    } finally {
        $archive.Dispose()
    }
}

Write-Host "=== Downloading Amazing Workload and SSMS Workload artifacts (cached to $CacheDir) ==="

$CachedDbeaverOlder = Get-CachedFilePath $DbeaverExeOlder
$CachedPythonOlder = Get-CachedFilePath $PythonExeOlder
$CachedSsmsOlder = Get-CachedFilePath $SsmsExeOlder

$CachedDbeaverNewer = Get-CachedFilePath $DbeaverExeNewer
$CachedPythonNewer = Get-CachedFilePath $PythonExeNewer
$CachedSsmsNewer = Get-CachedFilePath $SsmsExeNewer

Download-File -Url $DbeaverUrlOlder -Destination $CachedDbeaverOlder
Download-File -Url $PythonUrlOlder -Destination $CachedPythonOlder
Download-File -Url $SsmsUrlOlder -Destination $CachedSsmsOlder

Download-File -Url $DbeaverUrlNewer -Destination $CachedDbeaverNewer
Download-File -Url $PythonUrlNewer -Destination $CachedPythonNewer
Download-File -Url $SsmsUrlNewer -Destination $CachedSsmsNewer

Write-Host ""
Write-Host "=== Generating manifest files in $OutputDir ==="

# DBeaver older manifest
$DbeaverManifestOlder = @{
    packageId = "dbeaver"
    version = $DbeaverVersionOlder
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $DbeaverExeOlder
        arguments = "/S /allusers"
        uninstallArgs = "/S /allusers"
        uninstallCommand = "%ProgramFiles%\DBeaver\uninstall.exe"
        upgradeBehavior = "InPlace"
        expectedExitCodes = @(0)
        timeoutSeconds = 300
    }
    detection = @{
        type = "version_manifest"
        path = "dbeaver"
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$DbeaverBaseOlder = [System.IO.Path]::GetFileNameWithoutExtension($DbeaverExeOlder)
$DbeaverManifestPathOlder = Join-Path $OutputDir "${DbeaverBaseOlder}.manifest.json"
$DbeaverManifestJsonOlder = $DbeaverManifestOlder | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $DbeaverManifestPathOlder -Value $DbeaverManifestJsonOlder

# Python older manifest
$PythonManifestOlder = @{
    packageId = "python"
    version = $PythonVersionOlder
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $PythonExeOlder
        arguments = "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0"
        uninstallArgs = "/uninstall /quiet"
        uninstallCommand = "{artifactPath}"
        upgradeBehavior = "UninstallFirst"
        expectedExitCodes = @(0)
        timeoutSeconds = 300
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$PythonBaseOlder = [System.IO.Path]::GetFileNameWithoutExtension($PythonExeOlder)
$PythonManifestPathOlder = Join-Path $OutputDir "${PythonBaseOlder}.manifest.json"
$PythonManifestJsonOlder = $PythonManifestOlder | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $PythonManifestPathOlder -Value $PythonManifestJsonOlder

# SSMS older manifest
$SsmsManifestOlder = @{
    packageId = "ssms"
    version = $SsmsVersionOlder
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $SsmsExeOlder
        arguments = '/install /quiet /norestart'
        uninstallArgs = "/uninstall /quiet"
        uninstallCommand = "%ProgramFiles(x86)%\Microsoft SQL Server Management Studio 19\Common7\IDE\Ssms.exe"
        upgradeBehavior = "UninstallFirst"
        expectedExitCodes = @(0, 3010)
        timeoutSeconds = 600
    }
    detection = @{
        type = "file"
        path = "C:\Program Files (x86)\Microsoft SQL Server Management Studio 19\Common7\IDE\Ssms.exe"
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "medium"
        approvalRequired = $false
    }
}
$SsmsBaseOlder = [System.IO.Path]::GetFileNameWithoutExtension($SsmsExeOlder)
$SsmsManifestPathOlder = Join-Path $OutputDir "${SsmsBaseOlder}.manifest.json"
$SsmsManifestJsonOlder = $SsmsManifestOlder | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $SsmsManifestPathOlder -Value $SsmsManifestJsonOlder

# DBeaver newer manifest
$DbeaverManifestNewer = @{
    packageId = "dbeaver"
    version = $DbeaverVersionNewer
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $DbeaverExeNewer
        arguments = "/S /allusers"
        uninstallArgs = "/S /allusers"
        uninstallCommand = "%ProgramFiles%\DBeaver\uninstall.exe"
        upgradeBehavior = "InPlace"
        expectedExitCodes = @(0)
        timeoutSeconds = 300
    }
    detection = @{
        type = "version_manifest"
        path = "dbeaver"
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$DbeaverBaseNewer = [System.IO.Path]::GetFileNameWithoutExtension($DbeaverExeNewer)
$DbeaverManifestPathNewer = Join-Path $OutputDir "${DbeaverBaseNewer}.manifest.json"
$DbeaverManifestJsonNewer = $DbeaverManifestNewer | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $DbeaverManifestPathNewer -Value $DbeaverManifestJsonNewer

# Python newer manifest
$PythonManifestNewer = @{
    packageId = "python"
    version = $PythonVersionNewer
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $PythonExeNewer
        arguments = "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0"
        uninstallArgs = "/uninstall /quiet"
        uninstallCommand = "{artifactPath}"
        upgradeBehavior = "UninstallFirst"
        expectedExitCodes = @(0)
        timeoutSeconds = 300
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$PythonBaseNewer = [System.IO.Path]::GetFileNameWithoutExtension($PythonExeNewer)
$PythonManifestPathNewer = Join-Path $OutputDir "${PythonBaseNewer}.manifest.json"
$PythonManifestJsonNewer = $PythonManifestNewer | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $PythonManifestPathNewer -Value $PythonManifestJsonNewer

# SSMS newer manifest
$SsmsManifestNewer = @{
    packageId = "ssms"
    version = $SsmsVersionNewer
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $SsmsExeNewer
        arguments = '--quiet --norestart --wait'
        uninstallArgs = "uninstall --quiet --norestart"
        uninstallCommand = $SsmsExeNewer
        upgradeBehavior = "UninstallFirst"
        expectedExitCodes = @(0, 3010)
        timeoutSeconds = 600
    }
    detection = @{
        type = "file"
        path = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe"
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "medium"
        approvalRequired = $false
    }
}
$SsmsBaseNewer = [System.IO.Path]::GetFileNameWithoutExtension($SsmsExeNewer)
$SsmsManifestPathNewer = Join-Path $OutputDir "${SsmsBaseNewer}.manifest.json"
$SsmsManifestJsonNewer = $SsmsManifestNewer | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $SsmsManifestPathNewer -Value $SsmsManifestJsonNewer

Write-Host "Manifests generated."
Write-Host ""
Write-Host "=== Creating zip archives (skipping if up-to-date) ==="

$AmazingV1ZipName = "amazing-workload-artifacts-v1.zip"
$AmazingV2ZipName = "amazing-workload-artifacts-v2.zip"
$SsmsV1ZipName = "ssms-workload-artifacts-v1.zip"
$SsmsV2ZipName = "ssms-workload-artifacts-v2.zip"

$AmazingV1ZipPath = Join-Path $OutputDir $AmazingV1ZipName
$AmazingV2ZipPath = Join-Path $OutputDir $AmazingV2ZipName
$SsmsV1ZipPath = Join-Path $OutputDir $SsmsV1ZipName
$SsmsV2ZipPath = Join-Path $OutputDir $SsmsV2ZipName

# Build Amazing Workload v1 zip (older DBeaver + Python + manifests)
$AmazingV1Sources = @(
    $DbeaverManifestPathOlder,
    $PythonManifestPathOlder,
    $CachedDbeaverOlder,
    $CachedPythonOlder
)

if (Test-ZipNeedsRebuild -ZipPath $AmazingV1ZipPath -SourcePaths $AmazingV1Sources) {
    New-ZipArchive -ZipPath $AmazingV1ZipPath -SourcePaths $AmazingV1Sources
    Write-Host "Created $AmazingV1ZipName"
} else {
    Write-Host "Skipped $AmazingV1ZipName (up-to-date)"
}

# Build SSMS Workload v1 zip (older SSMS + manifest)
$SsmsV1Sources = @(
    $SsmsManifestPathOlder,
    $CachedSsmsOlder
)

if (Test-ZipNeedsRebuild -ZipPath $SsmsV1ZipPath -SourcePaths $SsmsV1Sources) {
    New-ZipArchive -ZipPath $SsmsV1ZipPath -SourcePaths $SsmsV1Sources
    Write-Host "Created $SsmsV1ZipName"
} else {
    Write-Host "Skipped $SsmsV1ZipName (up-to-date)"
}

# Build Amazing Workload v2 zip (newer DBeaver + Python + manifests)
$AmazingV2Sources = @(
    $DbeaverManifestPathNewer,
    $PythonManifestPathNewer,
    $CachedDbeaverNewer,
    $CachedPythonNewer
)

if (Test-ZipNeedsRebuild -ZipPath $AmazingV2ZipPath -SourcePaths $AmazingV2Sources) {
    New-ZipArchive -ZipPath $AmazingV2ZipPath -SourcePaths $AmazingV2Sources
    Write-Host "Created $AmazingV2ZipName"
} else {
    Write-Host "Skipped $AmazingV2ZipName (up-to-date)"
}

# Build SSMS Workload v2 zip (newer SSMS + manifest)
$SsmsV2Sources = @(
    $SsmsManifestPathNewer,
    $CachedSsmsNewer
)

if (Test-ZipNeedsRebuild -ZipPath $SsmsV2ZipPath -SourcePaths $SsmsV2Sources) {
    New-ZipArchive -ZipPath $SsmsV2ZipPath -SourcePaths $SsmsV2Sources
    Write-Host "Created $SsmsV2ZipName"
} else {
    Write-Host "Skipped $SsmsV2ZipName (up-to-date)"
}

Write-Host ""
Write-Host "Done. Zip archives in: $OutputDir"
Get-ChildItem -Path $OutputDir -Filter "*workload*" -ErrorAction SilentlyContinue

# Copy workload definitions to dist/workloads for runtime import
$TestWorkloadsDir = Join-Path $ProjectDir "test-workloads"
if (Test-Path $TestWorkloadsDir) {
    Copy-Item -Path (Join-Path $TestWorkloadsDir "*.json") -Destination $WorkloadsDir -Force
    Write-Host ""
    Write-Host "Copied workload definitions to: $WorkloadsDir"
}
