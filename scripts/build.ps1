#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

# Build React frontend
Write-Host "Building React frontend..."
Push-Location (Join-Path $root "orchestrator" "web")
try {
    npm install
    npm run build
} finally {
    Pop-Location
}

# Publish Orchestrator
Write-Host "Publishing Orchestrator..."
dotnet publish (Join-Path $root "orchestrator" "backend" "Orchestrator.csproj") `
    -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true

# Publish Agent
Write-Host "Publishing Agent..."
dotnet publish (Join-Path $root "agent" "backend" "Agent.csproj") `
    -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true

# Create dist directory
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dist "artifacts") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $dist "workload-definitions") | Out-Null

# Copy executables
Copy-Item (Join-Path $root "orchestrator" "backend" "bin" "Release" "net8.0-windows" "win-x64" "publish" "Orchestrator.exe") `
    (Join-Path $dist "Orchestrator.exe") -Force
Copy-Item (Join-Path $root "agent" "backend" "bin" "Release" "net8.0-windows" "win-x64" "publish" "Agent.exe") `
    (Join-Path $dist "Agent.exe") -Force

# Copy appsettings
Copy-Item (Join-Path $root "orchestrator" "backend" "appsettings.json") `
    (Join-Path $dist "appsettings.json") -Force

Write-Host "Build complete. Artifacts in $dist"
