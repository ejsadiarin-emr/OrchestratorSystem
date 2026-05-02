# DeploymentPoC Makefile (PowerShell)
# Requires: GNU Make + PowerShell

SHELL := powershell.exe
.SHELLFLAGS := -NoProfile -Command

.PHONY: publish build publish-orchestrator publish-agent build-frontend copy-workloads download-artifacts run-orchestrator run-agent run-frontend clean stop-processes

# Default target
publish: clean stop-processes build-frontend publish-orchestrator publish-agent copy-workloads
	Write-Host "=== Publish complete. Artifacts in .\dist ===" -ForegroundColor Green

# Debug build (faster, for local dev)
build: build-frontend
	dotnet build apps\orchestrator\backend\DeploymentPoC.Orchestrator.csproj
	dotnet build apps\agent\backend\DeploymentPoC.Agent.csproj

# Publish self-contained orchestrator
publish-orchestrator: build-frontend stop-processes
	Write-Host "Publishing orchestrator..." -ForegroundColor Cyan
	dotnet publish apps\orchestrator\backend\DeploymentPoC.Orchestrator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist

# Publish self-contained agent
publish-agent: stop-processes
	Write-Host "Publishing agent..." -ForegroundColor Cyan
	dotnet publish apps\agent\backend\DeploymentPoC.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist

# Kill running orchestrator/agent processes so publish can overwrite
stop-processes:
	Write-Host "Stopping any running orchestrator/agent processes..." -ForegroundColor Cyan
	taskkill /F /IM DeploymentPoC.Orchestrator.exe *>$$null; taskkill /F /IM DeploymentPoC.Agent.exe *>$$null; Start-Sleep -Seconds 2

# Build React frontend into orchestrator wwwroot
build-frontend:
	Write-Host "Building frontend..." -ForegroundColor Cyan
	Set-Location apps\orchestrator\web; pnpm install; pnpm run build

# Copy workload JSONs into dist
copy-workloads:
	Write-Host "Copying workloads..." -ForegroundColor Cyan
	if (!(Test-Path .\dist\workloads)) { New-Item -ItemType Directory -Path .\dist\workloads | Out-Null }
	Copy-Item -Path test-workloads\*.json -Destination .\dist\workloads\ -Force

# Download Amazing Workload artifacts (PowerShell)
download-artifacts:
	Write-Host "Downloading artifacts..." -ForegroundColor Cyan
	.\scripts\download-amazing-workload-artifacts.ps1

# Dev mode: run orchestrator backend (from repo root, using default paths)
run-orchestrator:
	Write-Host "Starting orchestrator (dev mode)..." -ForegroundColor Cyan
	dotnet run --project apps\orchestrator\backend\DeploymentPoC.Orchestrator.csproj

# Dev mode: run agent backend
run-agent:
	Write-Host "Starting agent (dev mode)..." -ForegroundColor Cyan
	dotnet run --project apps\agent\backend\DeploymentPoC.Agent.csproj

# Dev mode: run frontend dev server
run-frontend:
	Set-Location apps\orchestrator\web; pnpm run dev

# Clean dist folder (keep .gitignore)
clean:
	Write-Host "Cleaning dist folder..." -ForegroundColor Cyan
	if (Test-Path .\dist) { Get-ChildItem -Path .\dist -Exclude .gitignore | Remove-Item -Recurse -Force }
