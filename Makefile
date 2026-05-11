# DeploymentPoC Makefile (PowerShell + Bash)
# Requires: GNU Make + PowerShell (Windows) or Bash (Linux/macOS)

ifeq ($(OS),Windows_NT)
SHELL := powershell.exe
.SHELLFLAGS := -NoProfile -Command
SHELL_KIND := powershell
else
SHELL := /usr/bin/env bash
.SHELLFLAGS := -e -o pipefail -c
SHELL_KIND := bash
endif

DEFAULT_RUNTIME ?= $(if $(filter Windows_NT,$(OS)),win-x64,linux-x64)
ORCH_RUNTIME ?= $(DEFAULT_RUNTIME)
AGENT_RUNTIME ?= $(DEFAULT_RUNTIME)

ifeq ($(SHELL_KIND),powershell)
	STOP_PROCESSES_CMD = taskkill /F /IM DeploymentPoC.Orchestrator.exe *>$$null; taskkill /F /IM DeploymentPoC.Agent.exe *>$$null; Start-Sleep -Seconds 2
	BUILD_FRONTEND_CMD = Set-Location apps/orchestrator/web; pnpm install; pnpm run build
	RUN_FRONTEND_CMD = Set-Location apps/orchestrator/web; pnpm run dev
	COPY_WORKLOADS_CMD = if (!(Test-Path ./dist/workloads)) { New-Item -ItemType Directory -Path ./dist/workloads | Out-Null }; Copy-Item -Path test-workloads/*.json -Destination ./dist/workloads/ -Force
	CLEAN_DIST_CMD = if (Test-Path ./dist) { Get-ChildItem -Path ./dist -Exclude .gitignore | Remove-Item -Recurse -Force }
	DOWNLOAD_ARTIFACTS_CMD = ./scripts/download-amazing-workload-artifacts.ps1
else
	STOP_PROCESSES_CMD = pkill -f '[D]eploymentPoC.Orchestrator' || true; pkill -f '[D]eploymentPoC.Agent' || true; sleep 2
	BUILD_FRONTEND_CMD = cd apps/orchestrator/web && pnpm install && pnpm run build
	RUN_FRONTEND_CMD = cd apps/orchestrator/web && pnpm run dev
	COPY_WORKLOADS_CMD = mkdir -p dist/workloads && cp test-workloads/*.json dist/workloads/
	CLEAN_DIST_CMD = if [ -d dist ]; then find dist -mindepth 1 ! -name .gitignore -exec rm -rf {} +; fi
	DOWNLOAD_ARTIFACTS_CMD = ./scripts/download-amazing-workload-artifacts.sh
endif

.PHONY: publish build publish-orchestrator publish-agent build-frontend copy-workloads download-artifacts run-orchestrator run-agent run-frontend clean stop-processes

# Default target
publish: clean stop-processes build-frontend publish-orchestrator publish-agent copy-workloads
	echo "=== Publish complete. Artifacts in ./dist ==="

# Debug build (faster, for local dev)
build: build-frontend
	dotnet build apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj
	dotnet build apps/agent/backend/DeploymentPoC.Agent.csproj

# Publish self-contained orchestrator
publish-orchestrator: build-frontend stop-processes
	echo "Publishing orchestrator..."
	dotnet publish apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj -c Release -r $(ORCH_RUNTIME) --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./dist

# Publish self-contained agent
publish-agent: stop-processes
	echo "Publishing agent..."
	dotnet publish apps/agent/backend/DeploymentPoC.Agent.csproj -c Release -r $(AGENT_RUNTIME) --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./dist

# Kill running orchestrator/agent processes so publish can overwrite
stop-processes:
	echo "Stopping any running orchestrator/agent processes..."
	$(STOP_PROCESSES_CMD)

# Build React frontend into orchestrator wwwroot
build-frontend:
	echo "Building frontend..."
	$(BUILD_FRONTEND_CMD)

# Copy workload JSONs into dist
copy-workloads:
	echo "Copying workloads..."
	$(COPY_WORKLOADS_CMD)

# Download Amazing Workload artifacts
download-artifacts:
	echo "Downloading artifacts..."
	$(DOWNLOAD_ARTIFACTS_CMD)

# Dev mode: run orchestrator backend (from repo root, using default paths)
run-orchestrator:
	echo "Starting orchestrator (dev mode)..."
	dotnet run --project apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj

# Dev mode: run agent backend
run-agent:
	echo "Starting agent (dev mode)..."
	dotnet run --project apps/agent/backend/DeploymentPoC.Agent.csproj

# Dev mode: run frontend dev server
run-frontend:
	$(RUN_FRONTEND_CMD)

# Clean dist folder (keep .gitignore)
clean:
	echo "Cleaning dist folder..."
	$(CLEAN_DIST_CMD)

# FULL WIPE
full: clean stop-processes build-frontend publish-orchestrator publish-agent download-artifacts copy-workloads 
	echo "=== Publish complete. Artifacts in ./dist ==="
