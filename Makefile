.PHONY: all build publish clean dist run-dev run-frontend help

# Default target - production publish
all: publish

# Build .NET solution (fast, no self-contained)
build:
	dotnet build DeploymentPoC.sln --configuration Release

# Build React frontend
frontend:
	cd orchestrator/web && npm install && npm run build

# Publish self-contained single-file executables for production
# Target: win-x64. Can be built on Linux/WSL and copied to Windows.
publish: frontend
	dotnet publish orchestrator/backend/Orchestrator.csproj \
		-c Release -r win-x64 --self-contained true \
		/p:PublishSingleFile=true \
		/p:IncludeNativeLibrariesForSelfExtract=true \
		/p:EnableCompressionInSingleFile=true
	dotnet publish agent/backend/Agent.csproj \
		-c Release -r win-x64 --self-contained true \
		/p:PublishSingleFile=true \
		/p:IncludeNativeLibrariesForSelfExtract=true \
		/p:EnableCompressionInSingleFile=true

# Create distribution directory with everything needed to run on Windows
# Usage: make dist -> copy dist/ folder to Windows -> run binaries in PowerShell
dist: publish
	mkdir -p dist/artifacts
	mkdir -p dist/workload-definitions
	mkdir -p dist/scripts
	cp orchestrator/backend/bin/Release/net10.0-windows/win-x64/publish/Orchestrator.exe dist/
	cp agent/backend/bin/Release/net10.0-windows/win-x64/publish/Agent.exe dist/
	cp orchestrator/backend/appsettings.json dist/
	cp scripts/download-sample-artifact.ps1 dist/scripts/
	@echo "Distribution ready in dist/"
	@echo "Copy dist/ to Windows and run Orchestrator.exe and Agent.exe from PowerShell"

# Copy dist/ to Windows C:\temp\deployment-poc (WSL only)
cp-win: dist
	cp -r dist/* /mnt/c/temp/deployment-poc/
	@echo "Copied dist/ to C:\temp\deployment-poc"

# Run Orchestrator in development mode (for local API testing)
run-dev:
	cd orchestrator/backend && dotnet run

# Run React frontend dev server (proxies to localhost:5000)
run-frontend:
	cd orchestrator/web && npm run dev

# Clean all build artifacts
clean:
	dotnet clean DeploymentPoC.sln
	cd orchestrator/web && rm -rf node_modules dist
	rm -rf dist/

# Show help
help:
	@echo "Production workflow (build on WSL/Linux, run on Windows):"
	@echo "  make publish        - Build self-contained win-x64 executables"
	@echo "  make dist           - Package into dist/ for copying to Windows"
	@echo ""
	@echo "Development workflow (dotnet run):"
	@echo "  make build          - Build .NET solution (Release, not self-contained)"
	@echo "  make frontend       - Build React frontend"
	@echo "  make run-dev        - Run Orchestrator in dev mode"
	@echo "  make run-frontend   - Run React dev server"
	@echo ""
	@echo "Maintenance:"
	@echo "  make clean          - Clean all build artifacts"
	@echo "  make help           - Show this help"
