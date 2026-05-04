.PHONY: all build publish clean dist run-orchestrator run-agent frontend help

# Default target
all: publish

# Build .NET solution in Debug configuration
build:
	dotnet build DeploymentPoC.sln --configuration Release

# Build React frontend
frontend:
	cd orchestrator/web && npm install && npm run build

# Publish self-contained single-file executables for production
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

# Create distribution directory with all artifacts
dist: publish
	mkdir -p dist/artifacts
	mkdir -p dist/workload-definitions
	cp orchestrator/backend/bin/Release/net8.0-windows/win-x64/publish/Orchestrator.exe dist/
	cp agent/backend/bin/Release/net8.0-windows/win-x64/publish/Agent.exe dist/
	cp orchestrator/backend/appsettings.json dist/

# Run Orchestrator in development mode
run-orchestrator:
	cd orchestrator/backend && dotnet run

# Run Agent in development mode (requires agent.json from enrollment)
run-agent:
	cd agent/backend && dotnet run

# Clean build artifacts
clean:
	dotnet clean DeploymentPoC.sln
	cd orchestrator/web && rm -rf node_modules dist
	rm -rf dist/

# Show help
help:
	@echo "Available targets:"
	@echo "  make build          - Build .NET solution (Release)"
	@echo "  make frontend       - Build React frontend"
	@echo "  make publish        - Publish self-contained executables"
	@echo "  make dist           - Create distribution directory"
	@echo "  make run-orchestrator - Run Orchestrator in dev mode"
	@echo "  make run-agent      - Run Agent in dev mode"
	@echo "  make clean          - Clean all build artifacts"
	@echo "  make help           - Show this help"
