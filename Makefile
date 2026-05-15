# DeploymentPoC Makefile (PowerShell + Bash)
# Requires: GNU Make + PowerShell (Windows) or Bash (Linux/macOS)
#
# NOTE: USAGE examples
#
# make publish --> publishes to ./dist/ preserving DB
# make publish-orchestrator
# make publish-agent
# make publish-full --> full wipe + publish (removes DB, clean build)
# make download-artifacts --> downloads test artifacts (DBeaver, python, SSMS .exe installer medias)
#
# ---- Use when developing in WSL ----
# make wsl-cross-publish-full --> full wipe + publish to Windows Documents

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

# Windows cross-compile (WSL -> Windows)
WIN_USERNAME ?= $(shell powershell.exe -Command '[Environment]::UserName' 2>/dev/null | tr -d '\r\n')
WIN_DIST_DIR ?= /mnt/c/Users/$(WIN_USERNAME)/Documents/DeploymentPoC-dist

ifeq ($(SHELL_KIND),powershell)
	STOP_PROCESSES_CMD = taskkill /F /IM DeploymentPoC.Orchestrator.exe *>$$null; taskkill /F /IM DeploymentPoC.Agent.exe *>$$null; Start-Sleep -Seconds 2
	BUILD_FRONTEND_CMD = Set-Location apps/orchestrator/web; pnpm install; pnpm run build
	RUN_FRONTEND_CMD = Set-Location apps/orchestrator/web; pnpm run dev
	TEST_FRONTEND_CMD = Set-Location apps/orchestrator/web; pnpm test -- --run
	COPY_WORKLOADS_CMD = if (!(Test-Path ./dist/workloads)) { New-Item -ItemType Directory -Path ./dist/workloads | Out-Null }; Copy-Item -Path test-workloads/*.json -Destination ./dist/workloads/ -Force
	CLEAN_DIST_CMD = if (Test-Path ./dist) { Get-ChildItem -Path ./dist -Exclude .gitignore | Remove-Item -Recurse -Force }
	DOWNLOAD_ARTIFACTS_CMD = ./scripts/download-amazing-workload-artifacts.ps1
else
	STOP_PROCESSES_CMD = pkill -f '[D]eploymentPoC.Orchestrator' || true; pkill -f '[D]eploymentPoC.Agent' || true; sleep 2
	BUILD_FRONTEND_CMD = cd apps/orchestrator/web && pnpm install && pnpm run build
	RUN_FRONTEND_CMD = cd apps/orchestrator/web && pnpm run dev
	TEST_FRONTEND_CMD = cd apps/orchestrator/web && pnpm test -- --run
	COPY_WORKLOADS_CMD = mkdir -p dist/workloads && cp test-workloads/*.json dist/workloads/
	CLEAN_DIST_CMD = if [ -d dist ]; then find dist -mindepth 1 ! -name .gitignore -exec rm -rf {} +; fi
	DOWNLOAD_ARTIFACTS_CMD = ./scripts/download-amazing-workload-artifacts.sh
endif

.PHONY: publish build test publish-orchestrator publish-agent publish-full build-frontend copy-workloads download-artifacts run-orchestrator run-agent run-frontend clean stop-processes wsl-cross-publish wsl-cross-publish-full clean-win-dist

# Default target (preserves DB)
publish: stop-processes build-frontend publish-orchestrator publish-agent download-artifacts copy-workloads
	@echo "=== Publish complete. Artifacts in ./dist ==="

# Debug build (faster, for local dev)
build: build-frontend
	@echo "-----------------------------------------------------"
	dotnet build apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj
	dotnet build apps/agent/backend/DeploymentPoC.Agent.csproj

# Run all test suites
test:
	@echo "====================================================="
	@echo "=== Running contracts tests ==="
	dotnet test tests/contracts/
	@echo "====================================================="
	@echo "=== Running orchestrator unit tests ==="
	dotnet test tests/orchestrator/unit/
	@echo "====================================================="
	@echo "=== Running orchestrator integration tests ==="
	dotnet test tests/orchestrator/integration/
	@echo "====================================================="
	@echo "=== Running agent unit tests ==="
	dotnet test tests/agent/unit/
	@echo "====================================================="
	@echo "=== Running agent integration tests ==="
	dotnet test tests/agent/integration/
	@echo "====================================================="
	@echo "=== Running frontend tests ==="
	$(TEST_FRONTEND_CMD)
	@echo "====================================================="
	@echo "=== All test suites complete ==="

# Publish self-contained orchestrator
publish-orchestrator: build-frontend stop-processes
	@echo "-----------------------------------------------------"
	@echo "Publishing orchestrator..."
	dotnet publish apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj -c Release -r $(ORCH_RUNTIME) --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./dist

# Publish self-contained agent
publish-agent: stop-processes
	@echo "-----------------------------------------------------"
	@echo "Publishing agent..."
	dotnet publish apps/agent/backend/DeploymentPoC.Agent.csproj -c Release -r $(AGENT_RUNTIME) --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./dist

# Kill running orchestrator/agent processes so publish can overwrite
stop-processes:
	@echo "-----------------------------------------------------"
	@echo "Stopping any running orchestrator/agent processes..."
	$(STOP_PROCESSES_CMD)

# Build React frontend into orchestrator wwwroot
build-frontend:
	@echo "-----------------------------------------------------"
	@echo "Building frontend..."
	$(BUILD_FRONTEND_CMD)

# Copy workload JSONs into dist
copy-workloads:
	@echo "-----------------------------------------------------"
	@echo "Copying workloads..."
	$(COPY_WORKLOADS_CMD)

# Download Amazing Workload artifacts
download-artifacts:
	@echo "-----------------------------------------------------"
	@echo "Downloading artifacts..."
	$(DOWNLOAD_ARTIFACTS_CMD)

# Dev mode: run orchestrator backend (from repo root, using default paths)
run-orchestrator:
	@echo "-----------------------------------------------------"
	@echo "Starting orchestrator (dev mode)..."
	dotnet run --project apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj

# Dev mode: run agent backend
run-agent:
	@echo "-----------------------------------------------------"
	@echo "Starting agent (dev mode)..."
	dotnet run --project apps/agent/backend/DeploymentPoC.Agent.csproj

# Dev mode: run frontend dev server
run-frontend:
	@echo "-----------------------------------------------------"
	$(RUN_FRONTEND_CMD)

# Clean dist folder (keep .gitignore)
clean:
	@echo "-----------------------------------------------------"
	@echo "Cleaning dist folder..."
	$(CLEAN_DIST_CMD)

# FULL WIPE
publish-full: clean stop-processes build-frontend publish-orchestrator publish-agent download-artifacts copy-workloads 
	@echo "=== Publish complete. Artifacts in ./dist ==="

######## Cross-compile from WSL to Windows ##########

# Windows cross-compile publish (run from WSL, outputs .exe to Windows Documents)
# e.g. "make wsl-cross-publish WIN_DIST_DIR=/mnt/c/Users/Other/Documents/custom."
wsl-cross-publish: build-frontend download-artifacts
	@echo "-----------------------------------------------------"
	@echo "Publishing Windows binaries to $(WIN_DIST_DIR)..."
	mkdir -p "$(WIN_DIST_DIR)/workloads" "$(WIN_DIST_DIR)/artifacts"
	dotnet publish apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o "$(WIN_DIST_DIR)"
	dotnet publish apps/agent/backend/DeploymentPoC.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o "$(WIN_DIST_DIR)"
	cp test-workloads/*.json "$(WIN_DIST_DIR)/workloads/"
	cp dist/artifacts/*workload* "$(WIN_DIST_DIR)/artifacts/"
	@echo "=== Windows publish complete. Artifacts in $(WIN_DIST_DIR) ==="

wsl-cross-publish-full: clean-win-dist build-frontend download-artifacts
	@echo "-----------------------------------------------------"
	@echo "Publishing Windows binaries to $(WIN_DIST_DIR)..."
	mkdir -p "$(WIN_DIST_DIR)/workloads" "$(WIN_DIST_DIR)/artifacts"
	dotnet publish apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o "$(WIN_DIST_DIR)"
	dotnet publish apps/agent/backend/DeploymentPoC.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o "$(WIN_DIST_DIR)"
	cp test-workloads/*.json "$(WIN_DIST_DIR)/workloads/"
	cp dist/artifacts/*workload* "$(WIN_DIST_DIR)/artifacts/"
	@echo "=== Windows publish complete. Artifacts in $(WIN_DIST_DIR) ==="

clean-win-dist:
	@echo "-----------------------------------------------------"
	@echo "Cleaning Windows dist at $(WIN_DIST_DIR)..."
	rm -rf "$(WIN_DIST_DIR)"
