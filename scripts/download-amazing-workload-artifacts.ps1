#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads "Amazing Workload" installer binaries and generates manifests.
.DESCRIPTION
    Downloads both older and newer versions of the packages defined in the
    "Amazing Workload" workload (DBeaver and Python) and generates manifest
    files for each version.
#>

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Split-Path -Parent $ScriptDir
$OutputDir = Join-Path $ProjectDir "test-artifacts"
$TempDir = Join-Path $env:TEMP ("amazing-workload-artifacts-" + [Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

# --- Older versions (from workloads-older.json) ---
$DbeaverVersionOlder = "24.3.0"
$PythonVersionOlder = "3.13.3"

# --- Newer versions (from workloads-newer.json) ---
$DbeaverVersionNewer = "26.0.3"
$PythonVersionNewer = "3.14.4"

# Filenames
$DbeaverExeOlder = "dbeaver-ce-${DbeaverVersionOlder}-x86_64-setup.exe"
$PythonExeOlder = "python-${PythonVersionOlder}-amd64.exe"
$DbeaverExeNewer = "dbeaver-ce-${DbeaverVersionNewer}-windows-x86_64.exe"
$PythonExeNewer = "python-${PythonVersionNewer}-amd64.exe"

# Download URLs
$DbeaverUrlOlder = "https://github.com/dbeaver/dbeaver/releases/download/${DbeaverVersionOlder}/${DbeaverExeOlder}"
$PythonUrlOlder = "https://www.python.org/ftp/python/${PythonVersionOlder}/${PythonExeOlder}"
$DbeaverUrlNewer = "https://github.com/dbeaver/dbeaver/releases/download/${DbeaverVersionNewer}/${DbeaverExeNewer}"
$PythonUrlNewer = "https://www.python.org/ftp/python/${PythonVersionNewer}/${PythonExeNewer}"

function Download-File {
    param(
        [string]$Url,
        [string]$Destination
    )
    Write-Host "Downloading $(Split-Path -Leaf $Destination) ..."
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
    $size = (Get-Item $Destination).Length
    Write-Host "  -> $([math]::Round($size / 1MB, 2)) MB"
}

Write-Host "=== Downloading Amazing Workload artifacts to $TempDir ==="

Download-File -Url $DbeaverUrlOlder -Destination (Join-Path $TempDir $DbeaverExeOlder)
Download-File -Url $PythonUrlOlder -Destination (Join-Path $TempDir $PythonExeOlder)
Download-File -Url $DbeaverUrlNewer -Destination (Join-Path $TempDir $DbeaverExeNewer)
Download-File -Url $PythonUrlNewer -Destination (Join-Path $TempDir $PythonExeNewer)

Write-Host ""
Write-Host "=== Generating manifest files ==="

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
        upgradeBehavior = "InPlace"
        expectedExitCodes = @(0)
        timeoutSeconds = 300
    }
    detection = @{
        type = "version_manifest"
        path = "dbeaver"
        expectedVersion = $DbeaverVersionOlder
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$DbeaverBaseOlder = [System.IO.Path]::GetFileNameWithoutExtension($DbeaverExeOlder)
$DbeaverManifestOlder | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${DbeaverBaseOlder}.manifest.json") -Encoding UTF8

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
        uninstallArgs = "/uninstall"
        upgradeBehavior = "SideBySide"
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
$PythonManifestOlder | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${PythonBaseOlder}.manifest.json") -Encoding UTF8

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
        upgradeBehavior = "InPlace"
        expectedExitCodes = @(0)
        timeoutSeconds = 300
    }
    detection = @{
        type = "version_manifest"
        path = "dbeaver"
        expectedVersion = $DbeaverVersionNewer
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$DbeaverBaseNewer = [System.IO.Path]::GetFileNameWithoutExtension($DbeaverExeNewer)
$DbeaverManifestNewer | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${DbeaverBaseNewer}.manifest.json") -Encoding UTF8

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
        uninstallArgs = "/uninstall"
        upgradeBehavior = "SideBySide"
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
$PythonManifestNewer | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${PythonBaseNewer}.manifest.json") -Encoding UTF8

Write-Host "Manifests generated."
Write-Host ""
Write-Host "=== Creating zip archives ==="

$V1ZipName = "amazing-workload-artifacts-v1.zip"
$V2ZipName = "amazing-workload-artifacts-v2.zip"
$V1ZipPath = Join-Path $OutputDir $V1ZipName
$V2ZipPath = Join-Path $OutputDir $V2ZipName

# Create v1 zip (older versions + installers)
Compress-Archive -Path (
    Join-Path $TempDir "${DbeaverBaseOlder}.manifest.json"),
                    (Join-Path $TempDir "${PythonBaseOlder}.manifest.json"),
                    (Join-Path $TempDir $DbeaverExeOlder),
                    (Join-Path $TempDir $PythonExeOlder) `
    -DestinationPath $V1ZipPath -Force
Write-Host "Created $V1ZipName"

# Create v2 zip (newer versions + installers)
Compress-Archive -Path (
    Join-Path $TempDir "${DbeaverBaseNewer}.manifest.json"),
                    (Join-Path $TempDir "${PythonBaseNewer}.manifest.json"),
                    (Join-Path $TempDir $DbeaverExeNewer),
                    (Join-Path $TempDir $PythonExeNewer) `
    -DestinationPath $V2ZipPath -Force
Write-Host "Created $V2ZipName"

Write-Host ""
Write-Host "=== Cleaning up temporary files ==="
Remove-Item -Recurse -Force $TempDir

Write-Host ""
Write-Host "Done. Zip archives created in: $OutputDir"
Get-ChildItem -Path $OutputDir -Filter "*amazing*" -ErrorAction SilentlyContinue
