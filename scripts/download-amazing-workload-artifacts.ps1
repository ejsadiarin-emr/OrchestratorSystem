#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads "Amazing Workload" installer binaries and generates manifests.
.DESCRIPTION
    Downloads both older and newer versions of the packages defined in the
    "Amazing Workload" workload (DBeaver, Python, and SQL Server Express) and
    generates manifest files for each version.

    Features:
    - Caches installers locally to avoid redundant downloads.
    - Outputs manifests and final zip archives directly to dist/artifacts/.
    - Re-creates zip archives only when source manifests or installers change.

    Note: SQL Server Express installers are web installers that download
    additional payload during installation; internet access is required on
    the target machine during the Install step.
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
$SqlServerVersionOlder = "2019"

# --- Newer versions (from workloads-newer.json) ---
$DbeaverVersionNewer = "26.0.3"
$PythonVersionNewer = "3.14.4"
$SqlServerVersionNewer = "2025"

# Filenames
$DbeaverExeOlder = "dbeaver-ce-${DbeaverVersionOlder}-x86_64-setup.exe"
$PythonExeOlder = "python-${PythonVersionOlder}-amd64.exe"
$SqlServerExeOlder = "SQL${SqlServerVersionOlder}-SSEI-Expr.exe"

$DbeaverExeNewer = "dbeaver-ce-${DbeaverVersionNewer}-windows-x86_64.exe"
$PythonExeNewer = "python-${PythonVersionNewer}-amd64.exe"
$SqlServerExeNewer = "SQL${SqlServerVersionNewer}-SSEI-Expr.exe"

# Download URLs
$DbeaverUrlOlder = "https://github.com/dbeaver/dbeaver/releases/download/${DbeaverVersionOlder}/${DbeaverExeOlder}"
$PythonUrlOlder = "https://www.python.org/ftp/python/${PythonVersionOlder}/${PythonExeOlder}"
$SqlServerUrlOlder = "https://download.microsoft.com/download/7/f/8/7f8a9c43-8c8a-4f7c-9f92-83c18d96b681/${SqlServerExeOlder}"

$DbeaverUrlNewer = "https://github.com/dbeaver/dbeaver/releases/download/${DbeaverVersionNewer}/${DbeaverExeNewer}"
$PythonUrlNewer = "https://www.python.org/ftp/python/${PythonVersionNewer}/${PythonExeNewer}"
$SqlServerUrlNewer = "https://go.microsoft.com/fwlink/p/?linkid=2216019&clcid=0x409&culture=en-us&country=us"

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

Write-Host "=== Downloading Amazing Workload artifacts (cached to $CacheDir) ==="

$CachedDbeaverOlder = Get-CachedFilePath $DbeaverExeOlder
$CachedPythonOlder = Get-CachedFilePath $PythonExeOlder
$CachedSqlServerOlder = Get-CachedFilePath $SqlServerExeOlder

$CachedDbeaverNewer = Get-CachedFilePath $DbeaverExeNewer
$CachedPythonNewer = Get-CachedFilePath $PythonExeNewer
$CachedSqlServerNewer = Get-CachedFilePath $SqlServerExeNewer

Download-File -Url $DbeaverUrlOlder -Destination $CachedDbeaverOlder
Download-File -Url $PythonUrlOlder -Destination $CachedPythonOlder
Download-File -Url $SqlServerUrlOlder -Destination $CachedSqlServerOlder

Download-File -Url $DbeaverUrlNewer -Destination $CachedDbeaverNewer
Download-File -Url $PythonUrlNewer -Destination $CachedPythonNewer
Download-File -Url $SqlServerUrlNewer -Destination $CachedSqlServerNewer

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
        uninstallArgs = "/S"
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
        uninstallArgs = ""
        uninstallCommand = ""
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

# SQL Server older manifest
$SqlServerManifestOlder = @{
    packageId = "sqlserver"
    version = $SqlServerVersionOlder
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $SqlServerExeOlder
        arguments = '/ACTION=Install /Q /IACCEPTSQLSERVERLICENSETERMS'
        uninstallArgs = "/ACTION=Uninstall /FEATURES=SQLEngine /INSTANCENAME=SQLEXPRESS /Q /IACCEPTSQLSERVERLICENSETERMS"
        uninstallCommand = "%ProgramFiles%\Microsoft SQL Server\150\Setup Bootstrap\SQL2019\setup.exe"
        upgradeBehavior = "UninstallFirst"
        expectedExitCodes = @(0, 3010)
        timeoutSeconds = 1800
    }
    detection = @{
        type = "file"
        path = "C:\Program Files\Microsoft SQL Server\MSSQL15.SQLEXPRESS\MSSQL\Binn\sqlservr.exe"
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "high"
        approvalRequired = $true
    }
}
$SqlServerBaseOlder = [System.IO.Path]::GetFileNameWithoutExtension($SqlServerExeOlder)
$SqlServerManifestPathOlder = Join-Path $OutputDir "${SqlServerBaseOlder}.manifest.json"
$SqlServerManifestJsonOlder = $SqlServerManifestOlder | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $SqlServerManifestPathOlder -Value $SqlServerManifestJsonOlder

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
        uninstallArgs = "/S"
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
        uninstallArgs = ""
        uninstallCommand = ""
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

# SQL Server newer manifest
$SqlServerManifestNewer = @{
    packageId = "sqlserver"
    version = $SqlServerVersionNewer
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $SqlServerExeNewer
        arguments = '/ACTION=Install /Q /IACCEPTSQLSERVERLICENSETERMS /SUPPRESSPRIVACYSTATEMENTNOTICE'
        uninstallArgs = "/ACTION=Uninstall /FEATURES=SQLEngine /INSTANCENAME=SQLEXPRESS /Q /IACCEPTSQLSERVERLICENSETERMS"
        uninstallCommand = "%ProgramFiles%\Microsoft SQL Server\170\Setup Bootstrap\SQL2025\setup.exe"
        upgradeBehavior = "UninstallFirst"
        expectedExitCodes = @(0, 3010)
        timeoutSeconds = 1800
    }
    detection = @{
        type = "file"
        path = "C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Binn\sqlservr.exe"
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "high"
        approvalRequired = $true
    }
}
$SqlServerBaseNewer = [System.IO.Path]::GetFileNameWithoutExtension($SqlServerExeNewer)
$SqlServerManifestPathNewer = Join-Path $OutputDir "${SqlServerBaseNewer}.manifest.json"
$SqlServerManifestJsonNewer = $SqlServerManifestNewer | ConvertTo-Json -Depth 10
Set-ContentIfChanged -Path $SqlServerManifestPathNewer -Value $SqlServerManifestJsonNewer

Write-Host "Manifests generated."
Write-Host ""
Write-Host "=== Creating zip archives (skipping if up-to-date) ==="

$V1ZipName = "amazing-workload-artifacts-v1.zip"
$V2ZipName = "amazing-workload-artifacts-v2.zip"
$V1ZipPath = Join-Path $OutputDir $V1ZipName
$V2ZipPath = Join-Path $OutputDir $V2ZipName

# Build v1 zip (older versions + installers)
$V1Sources = @(
    $DbeaverManifestPathOlder,
    $PythonManifestPathOlder,
    $SqlServerManifestPathOlder,
    $CachedDbeaverOlder,
    $CachedPythonOlder,
    $CachedSqlServerOlder
)

if (Test-ZipNeedsRebuild -ZipPath $V1ZipPath -SourcePaths $V1Sources) {
    Compress-Archive -Path $V1Sources -DestinationPath $V1ZipPath -Force
    Write-Host "Created $V1ZipName"
} else {
    Write-Host "Skipped $V1ZipName (up-to-date)"
}

# Build v2 zip (newer versions + installers)
$V2Sources = @(
    $DbeaverManifestPathNewer,
    $PythonManifestPathNewer,
    $SqlServerManifestPathNewer,
    $CachedDbeaverNewer,
    $CachedPythonNewer,
    $CachedSqlServerNewer
)

if (Test-ZipNeedsRebuild -ZipPath $V2ZipPath -SourcePaths $V2Sources) {
    Compress-Archive -Path $V2Sources -DestinationPath $V2ZipPath -Force
    Write-Host "Created $V2ZipName"
} else {
    Write-Host "Skipped $V2ZipName (up-to-date)"
}

Write-Host ""
Write-Host "Done. Zip archives in: $OutputDir"
Get-ChildItem -Path $OutputDir -Filter "*amazing*" -ErrorAction SilentlyContinue

# Copy workload definitions to dist/workloads for runtime import
$TestWorkloadsDir = Join-Path $ProjectDir "test-workloads"
if (Test-Path $TestWorkloadsDir) {
    Copy-Item -Path (Join-Path $TestWorkloadsDir "*.json") -Destination $WorkloadsDir -Force
    Write-Host ""
    Write-Host "Copied workload definitions to: $WorkloadsDir"
}
