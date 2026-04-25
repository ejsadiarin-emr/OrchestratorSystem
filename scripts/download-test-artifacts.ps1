#Requires -Version 5.1
<#
.SYNOPSIS
    Downloads installer binaries and generates manifests for test artifacts.
.DESCRIPTION
    Creates a zip bundle similar to artifact-bulk.zip but with different versions
    of Git, Node.js, Python, and 7-Zip.
#>

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Split-Path -Parent $ScriptDir
$OutputDir = Join-Path $ProjectDir "test-artifacts"
$TempDir = Join-Path $env:TEMP ("test-artifacts-" + [Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

# Versions for the alternative test artifacts
$GitVersion = "2.47.1"
$NodeVersion = "22.14.0"
$PythonVersion = "3.13.3"
$ZipVersion = "24.09"

# Filenames
$GitExe = "Git-${GitVersion}-64-bit.exe"
$NodeMsi = "node-v${NodeVersion}-x64.msi"
$PythonExe = "python-${PythonVersion}-amd64.exe"
$ZipExe = "7z$($ZipVersion.Replace('.',''))-x64.exe"

# Download URLs
$GitUrl = "https://github.com/git-for-windows/git/releases/download/v${GitVersion}.windows.1/${GitExe}"
$NodeUrl = "https://nodejs.org/dist/v${NodeVersion}/${NodeMsi}"
$PythonUrl = "https://www.python.org/ftp/python/${PythonVersion}/${PythonExe}"
$ZipUrl = "https://www.7-zip.org/a/${ZipExe}"

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

Write-Host "=== Downloading test artifacts to $TempDir ==="

Download-File -Url $GitUrl -Destination (Join-Path $TempDir $GitExe)
Download-File -Url $NodeUrl -Destination (Join-Path $TempDir $NodeMsi)
Download-File -Url $PythonUrl -Destination (Join-Path $TempDir $PythonExe)
Download-File -Url $ZipUrl -Destination (Join-Path $TempDir $ZipExe)

Write-Host ""
Write-Host "=== Generating manifest files ==="

# Git manifest
$GitManifest = @{
    packageId = "git"
    version = $GitVersion
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $GitExe
        arguments = '/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /COMPONENTS="icons,extreg,shellassoc"'
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
$GitBase = [System.IO.Path]::GetFileNameWithoutExtension($GitExe)
$GitManifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${GitBase}.manifest.json") -Encoding UTF8

# Node.js manifest
$NodeManifest = @{
    packageId = "nodejs"
    version = $NodeVersion
    channel = "stable"
    artifactType = "msi"
    verificationResult = "pass"
    installAdapter = @{
        type = "msi"
        command = $NodeMsi
        arguments = "/quiet /norestart"
        expectedExitCodes = @(0, 3010)
        timeoutSeconds = 300
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$NodeBase = [System.IO.Path]::GetFileNameWithoutExtension($NodeMsi)
$NodeManifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${NodeBase}.manifest.json") -Encoding UTF8

# Python manifest
$PythonManifest = @{
    packageId = "python"
    version = $PythonVersion
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $PythonExe
        arguments = "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0"
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
$PythonBase = [System.IO.Path]::GetFileNameWithoutExtension($PythonExe)
$PythonManifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${PythonBase}.manifest.json") -Encoding UTF8

# 7-Zip manifest
$ZipManifest = @{
    packageId = "7zip"
    version = $ZipVersion
    channel = "stable"
    artifactType = "exe"
    verificationResult = "pass"
    installAdapter = @{
        type = "exe"
        command = $ZipExe
        arguments = '/S /D=C:\\Program Files\\7-Zip'
        expectedExitCodes = @(0)
        timeoutSeconds = 120
    }
    policyTags = @{
        retryabilityClass = "non-idempotent"
        idempotencyMode = "none"
        riskLevel = "low"
        approvalRequired = $false
    }
}
$ZipBase = [System.IO.Path]::GetFileNameWithoutExtension($ZipExe)
$ZipManifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $TempDir "${ZipBase}.manifest.json") -Encoding UTF8

Write-Host "Manifests generated."
Write-Host ""
Write-Host "=== Creating artifact-bulk-older.zip ==="

$ZipPath = Join-Path $OutputDir "artifact-bulk-older.zip"
Compress-Archive -Path "$TempDir\*" -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "=== Cleaning up temporary files ==="
Remove-Item -Recurse -Force $TempDir

Write-Host ""
Write-Host "Done. Created: $ZipPath"
$final = Get-Item $ZipPath
Write-Host ("Size: {0:N2} MB" -f ($final.Length / 1MB))
