.PHONY: test test-web test-orchestrator test-agent test-contracts test-integration build build-web build-backend build-agent publish publish-win publish-backend publish-backend-win publish-agent publish-agent-win clean

# --- tests ---

test-web:
	cd apps/orchestrator/web && pnpm test

test-orchestrator:
	cd tests/orchestrator/unit && dotnet test

test-agent:
	cd tests/agent/unit && dotnet test

test-contracts:
	cd tests/contracts && dotnet test

test-integration:
	cd tests/orchestrator/integration && dotnet test
	cd tests/agent/integration && dotnet test

test: test-web test-orchestrator test-agent test-contracts

# --- builds ---

build-web:
	cd apps/orchestrator/web && pnpm build

build-backend:
	cd apps/orchestrator/backend && dotnet build

build-agent:
	cd apps/agent/backend && dotnet build

build: build-web build-backend build-agent

# --- publish (linux-x64) ---

publish-backend: build-web
	cd apps/orchestrator/backend && dotnet publish -c Release -r linux-x64 --self-contained true

publish-agent:
	cd apps/agent/backend && dotnet publish -c Release -r linux-x64 --self-contained true

publish: publish-backend publish-agent

# --- publish (win-x64) ---

publish-backend-win: build-web
	cd apps/orchestrator/backend && dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

publish-agent-win:
	cd apps/agent/backend && dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

publish-win: publish-backend-win publish-agent-win

# --- clean ---

clean:
	cd apps/orchestrator/web && rm -rf dist node_modules/.vite
	cd apps/orchestrator/backend && dotnet clean
	cd apps/agent/backend && dotnet clean
	cd tests/orchestrator/unit && dotnet clean
	cd tests/agent/unit && dotnet clean
	cd tests/contracts && dotnet clean
