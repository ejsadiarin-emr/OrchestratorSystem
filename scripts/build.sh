#!/usr/bin/env bash
set -e

root="$(cd "$(dirname "$0")/.." && pwd)"

# Build React frontend
echo "Building React frontend..."
cd "$root/orchestrator/web"
npm install
npm run build

# Publish Orchestrator
echo "Publishing Orchestrator..."
dotnet publish "$root/orchestrator/backend/Orchestrator.csproj" \
    -c Release -r win-x64 --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:EnableCompressionInSingleFile=true

# Publish Agent
echo "Publishing Agent..."
dotnet publish "$root/agent/backend/Agent.csproj" \
    -c Release -r win-x64 --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:EnableCompressionInSingleFile=true

# Create dist directory
mkdir -p "$root/dist/artifacts"
mkdir -p "$root/dist/workload-definitions"

# Copy executables
cp "$root/orchestrator/backend/bin/Release/net10.0-windows/win-x64/publish/Orchestrator.exe" \
    "$root/dist/Orchestrator.exe"
cp "$root/agent/backend/bin/Release/net10.0-windows/win-x64/publish/Agent.exe" \
    "$root/dist/Agent.exe"

# Copy appsettings
cp "$root/orchestrator/backend/appsettings.json" "$root/dist/appsettings.json"

echo "Build complete. Artifacts in $root/dist"
