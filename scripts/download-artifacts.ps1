# Download real artifacts for testing artifact upload and import
# All downloads are cached in .artifact-cache/ (gitignored)
# Generates manifest JSONs and creates ZIP files in dist/imports/

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$CacheDir = Join-Path $RepoRoot ".artifact-cache"
$ImportsDir = Join-Path $RepoRoot "dist" "imports"

if (-not (Test-Path $CacheDir)) {
    New-Item -ItemType Directory -Path $CacheDir | Out-Null
}
if (-not (Test-Path $ImportsDir)) {
    New-Item -ItemType Directory -Path $ImportsDir | Out-Null
}

$artifacts = @(
    [ordered]@{
        PackageId = "dbeaver-ce"
        PackageName = "DBeaver Community Edition"
        Version = "24.1.0"
        Url = "https://github.com/dbeaver/dbeaver/releases/download/24.1.0/dbeaver-ce-24.1.0-x86_64-setup.exe"
        Filename = "dbeaver-ce-24.1.0-x86_64-setup.exe"
        InstallArgs = "/S"
        UninstallCommand = "MsiExec.exe"
        UninstallArgs = "/X{2C2B8C8C-5C5C-4C5C-8C5C-2C2B8C8C5C5C} /qn"
        UpdateStrategy = "overinstall"
        DetectionType = "registry"
        DetectionKey = "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DBeaver Community_is1"
        DetectionValueName = "DisplayVersion"
        DetectionExpectedValue = "24.1.0"
        ZipGroup = "older"
    },
    [ordered]@{
        PackageId = "python-windows"
        PackageName = "Python for Windows"
        Version = "3.12.4"
        Url = "https://www.python.org/ftp/python/3.12.4/python-3.12.4-amd64.exe"
        Filename = "python-3.12.4-amd64.exe"
        InstallArgs = "/quiet InstallAllUsers=1 PrependPath=1"
        UninstallCommand = "MsiExec.exe"
        UninstallArgs = "/X{12345678-1234-1234-1234-123456789012} /qn"
        UpdateStrategy = "overinstall"
        DetectionType = "registry"
        DetectionKey = "HKLM\SOFTWARE\Python\PythonCore\3.12\InstallPath"
        DetectionValueName = "ExecutablePath"
        DetectionExpectedValue = "C:\Program Files\Python312\python.exe"
        ZipGroup = "older"
    },
    [ordered]@{
        PackageId = "ssms"
        PackageName = "SQL Server Management Studio"
        Version = "22.0"
        Url = "https://aka.ms/ssms/22/release/vs_SSMS.exe"
        Filename = "SSMS-22.0.exe"
        InstallArgs = "--quiet --norestart --wait"
        UninstallCommand = "MsiExec.exe"
        UninstallArgs = "/X{ABCDEF12-3456-7890-ABCD-EF1234567890} /qn"
        UpdateStrategy = "overinstall"
        DetectionType = "registry"
        DetectionKey = "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft SQL Server Management Studio - 22.0"
        DetectionValueName = "DisplayVersion"
        DetectionExpectedValue = "22.0"
        ZipGroup = "older"
    },
    [ordered]@{
        PackageId = "dbeaver-ce"
        PackageName = "DBeaver Community Edition"
        Version = "24.2.0"
        Url = "https://github.com/dbeaver/dbeaver/releases/download/24.2.0/dbeaver-ce-24.2.0-x86_64-setup.exe"
        Filename = "dbeaver-ce-24.2.0-x86_64-setup.exe"
        InstallArgs = "/S"
        UninstallCommand = "MsiExec.exe"
        UninstallArgs = "/X{3D3C9D9D-6D6D-5D6D-9D6D-3D3C9D9D6D6D} /qn"
        UpdateStrategy = "overinstall"
        DetectionType = "registry"
        DetectionKey = "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DBeaver Community_is1"
        DetectionValueName = "DisplayVersion"
        DetectionExpectedValue = "24.2.0"
        ZipGroup = "newer"
    },
    [ordered]@{
        PackageId = "python-windows"
        PackageName = "Python for Windows"
        Version = "3.13.0"
        Url = "https://www.python.org/ftp/python/3.13.0/python-3.13.0-amd64.exe"
        Filename = "python-3.13.0-amd64.exe"
        InstallArgs = "/quiet InstallAllUsers=1 PrependPath=1"
        UninstallCommand = "MsiExec.exe"
        UninstallArgs = "/X{23456789-2345-2345-2345-234567890123} /qn"
        UpdateStrategy = "overinstall"
        DetectionType = "registry"
        DetectionKey = "HKLM\SOFTWARE\Python\PythonCore\3.13\InstallPath"
        DetectionValueName = "ExecutablePath"
        DetectionExpectedValue = "C:\Program Files\Python313\python.exe"
        ZipGroup = "newer"
    },
    [ordered]@{
        PackageId = "vscode"
        PackageName = "Visual Studio Code"
        Version = "1.91.1"
        Url = "https://update.code.visualstudio.com/1.91.1/win32-x64-user/stable"
        Filename = "VSCodeUserSetup-x64-1.91.1.exe"
        InstallArgs = "/VERYSILENT /NORESTART"
        UninstallCommand = "MsiExec.exe"
        UninstallArgs = "/X{34567890-3456-3456-3456-345678901234} /qn"
        UpdateStrategy = "overinstall"
        DetectionType = "registry"
        DetectionKey = "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{GUID}_is1"
        DetectionValueName = "DisplayVersion"
        DetectionExpectedValue = "1.91.1"
        ZipGroup = "newer"
    }
)

foreach ($art in $artifacts) {
    $filePath = Join-Path $CacheDir $art.Filename
    if (-not (Test-Path $filePath)) {
        Write-Host "Downloading $($art.PackageName) $($art.Version) ..."
        Invoke-WebRequest -Uri $art.Url -OutFile $filePath -UseBasicParsing
        Write-Host "Saved: $filePath"
    } else {
        Write-Host "Already exists: $filePath"
    }

    $manifest = [ordered]@{
        packageId = $art.PackageId
        packageName = $art.PackageName
        version = $art.Version
        installerFile = $art.Filename
        installCommand = $art.Filename
        installArgs = $art.InstallArgs
        uninstallCommand = $art.UninstallCommand
        uninstallArgs = $art.UninstallArgs
        updateStrategy = $art.UpdateStrategy
        detection = [ordered]@{
            type = $art.DetectionType
            key = $art.DetectionKey
            valueName = $art.DetectionValueName
            expectedValue = $art.DetectionExpectedValue
        }
    }
    $manifestPath = Join-Path $CacheDir ([System.IO.Path]::ChangeExtension($art.Filename, ".json"))
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding UTF8
    Write-Host "Manifest: $manifestPath"
}

$olderFiles = $artifacts | Where-Object { $_.ZipGroup -eq "older" } | ForEach-Object {
    Join-Path $CacheDir $_.Filename
    Join-Path $CacheDir ([System.IO.Path]::ChangeExtension($_.Filename, ".json"))
}

$newerFiles = $artifacts | Where-Object { $_.ZipGroup -eq "newer" } | ForEach-Object {
    Join-Path $CacheDir $_.Filename
    Join-Path $CacheDir ([System.IO.Path]::ChangeExtension($_.Filename, ".json"))
}

Compress-Archive -Path $olderFiles -DestinationPath (Join-Path $ImportsDir "artifacts-older.zip") -Force
Compress-Archive -Path $newerFiles -DestinationPath (Join-Path $ImportsDir "artifacts-newer.zip") -Force

Write-Host ""
Write-Host "=== Download & Packaging Summary ==="
Write-Host "Cache directory: $CacheDir"
Write-Host "Imports directory: $ImportsDir"
Write-Host ""
Write-Host "ZIP files:"
Get-ChildItem -Path $ImportsDir -Filter "*.zip" | ForEach-Object { Write-Host $_.FullName }
Write-Host ""
Write-Host "Upload examples:"
Write-Host "  curl.exe -X POST http://localhost:5000/api/artifacts/upload -F `"packageId=dbeaver-ce`" -F `"version=24.1.0`" -F `"packageName=DBeaver Community Edition`" -F `"file=@$CacheDir\dbeaver-ce-24.1.0-x86_64-setup.exe`""
Write-Host "  curl.exe -X POST http://localhost:5000/api/artifacts/import -F `"files=@$ImportsDir\artifacts-older.zip`""
Write-Host "  curl.exe -X POST http://localhost:5000/api/artifacts/import -F `"files=@$ImportsDir\artifacts-newer.zip`""
