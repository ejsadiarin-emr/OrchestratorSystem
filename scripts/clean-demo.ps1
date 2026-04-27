#Requires -Version 5.1
<#
.SYNOPSIS
    Scaffolds a clean demo environment with published orchestrator and agent binaries.
.DESCRIPTION
    Creates a git worktree (or standalone directory), publishes orchestrator and agent
    in Release mode, sets up a clean SQLite database, and prepares an artifact store.
    Prints all relevant paths at the end for easy copy-paste.
.EXAMPLE
    .\scripts\clean-demo.ps1
    .\scripts\clean-demo.ps1 -WorktreeName "demo-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    .\scripts\clean-demo.ps1 -SkipWorktree
#>

[CmdletBinding()]
param(
    [string]$WorktreeName = "demo-$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [switch]$SkipWorktree
)

$ErrorActionPreference = "Stop"

# Resolve paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Split-Path -Parent $ScriptDir

if ($SkipWorktree) {
    # Use a standalone directory instead of a git worktree
    $DemoDir = Join-Path $ProjectDir ".demos" $WorktreeName
    New-Item -ItemType Directory -Path $DemoDir -Force | Out-Null
    Write-Host "=== Created standalone demo directory: $DemoDir ===" -ForegroundColor Cyan
} else {
    # Create a git worktree from the current branch
    $WorktreePath = Join-Path $ProjectDir ".worktrees" $WorktreeName
    Write-Host "=== Creating git worktree '$WorktreeName' ===" -ForegroundColor Cyan
    Push-Location $ProjectDir
    try {
        git worktree add "$WorktreePath" 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to create git worktree. It may already exist."
        }
    } finally {
        Pop-Location
    }
    $DemoDir = $WorktreePath
    Write-Host "Worktree created at: $DemoDir" -ForegroundColor Green
}

# Build configuration
$Configuration = "Release"
$Runtime = "win-x64"
$SelfContained = "true"

# Publish directories
$OrchestratorPublishDir = Join-Path $DemoDir "orchestrator-publish"
$AgentPublishDir = Join-Path $DemoDir "agent-publish"
$DataDir = Join-Path $OrchestratorPublishDir "data"
$ArtifactsDir = Join-Path $OrchestratorPublishDir "artifacts"

New-Item -ItemType Directory -Path $OrchestratorPublishDir, $AgentPublishDir, $DataDir, $ArtifactsDir -Force | Out-Null

# Project files
$OrchestratorProject = Join-Path $ProjectDir "apps\orchestrator\backend\DeploymentPoC.Orchestrator.csproj"
$AgentProject = Join-Path $ProjectDir "apps\agent\backend\DeploymentPoC.Agent.csproj"
$OrchestratorAppSettings = Join-Path $ProjectDir "apps\orchestrator\backend\appsettings.json"
$AgentAppSettings = Join-Path $ProjectDir "apps\agent\backend\appsettings.json"

function Publish-Backend {
    param(
        [string]$ProjectPath,
        [string]$OutputDir,
        [string]$Label
    )
    Write-Host ""
    Write-Host "=== Publishing $Label ===" -ForegroundColor Cyan
    Write-Host "Project: $ProjectPath"
    Write-Host "Output:  $OutputDir"

    dotnet publish "$ProjectPath" `
        -c $Configuration `
        -r $Runtime `
        --self-contained $SelfContained `
        -o "$OutputDir" `
        | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to publish $Label"
    }

    Write-Host "$Label published successfully." -ForegroundColor Green
}

# Publish orchestrator
Publish-Backend -ProjectPath $OrchestratorProject -OutputDir $OrchestratorPublishDir -Label "Orchestrator"

# Publish agent
Publish-Backend -ProjectPath $AgentProject -OutputDir $AgentPublishDir -Label "Agent"

# Copy appsettings files
Write-Host ""
Write-Host "=== Copying configuration files ===" -ForegroundColor Cyan

Copy-Item -Path $OrchestratorAppSettings -Destination (Join-Path $OrchestratorPublishDir "appsettings.json") -Force
Write-Host "Copied orchestrator appsettings.json"

Copy-Item -Path $AgentAppSettings -Destination (Join-Path $AgentPublishDir "appsettings.json") -Force
Write-Host "Copied agent appsettings.json"

# Initialize clean database
Write-Host ""
Write-Host "=== Initializing clean database ===" -ForegroundColor Cyan

$DbPath = Join-Path $DataDir "deployment-poc.db"

# Create an empty SQLite database with schema
# We do this by temporarily running the orchestrator with EF migrations
$OriginalLocation = Get-Location
Set-Location $OrchestratorPublishDir
try {
    # Run dotnet ef database update if ef tool is available
    $efExists = $null -ne (Get-Command "dotnet-ef" -ErrorAction SilentlyContinue)
    if ($efExists) {
        dotnet ef database update --project "$OrchestratorProject" 2>&1 | Out-Null
        Write-Host "Database initialized via EF migrations."
    } else {
        # Create an empty SQLite file; EF will create tables on first run
        New-Item -ItemType File -Path $DbPath -Force | Out-Null
        Write-Host "Created empty SQLite database at: $DbPath"
        Write-Host "(Tables will be created automatically on first orchestrator startup)" -ForegroundColor Yellow
    }
} finally {
    Set-Location $OriginalLocation
}

# Copy existing artifacts if available
$ExistingArtifacts = Join-Path $ProjectDir "apps\orchestrator\backend\bin\Release\net10.0\win-x64\publish\artifacts"
if (Test-Path $ExistingArtifacts) {
    Write-Host ""
    Write-Host "=== Copying existing artifacts ===" -ForegroundColor Cyan
    Copy-Item -Path "$ExistingArtifacts\*" -Destination $ArtifactsDir -Recurse -Force
    Write-Host "Artifacts copied from: $ExistingArtifacts"
} else {
    Write-Host ""
    Write-Host "=== No existing artifacts found ===" -ForegroundColor Yellow
    Write-Host "Artifact store is empty. Run scripts/download-test-artifacts.ps1 to populate."
}

# Final summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  DEMO ENVIRONMENT READY" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Demo Directory:     $DemoDir"
Write-Host ""
Write-Host "--- ORCHESTRATOR ---"
Write-Host "Binary:             $OrchestratorPublishDir\DeploymentPoC.Orchestrator.exe"
Write-Host "AppSettings:        $OrchestratorPublishDir\appsettings.json"
Write-Host "Database:           $DbPath"
Write-Host "Artifact Store:     $ArtifactsDir"
Write-Host "Data Directory:     $DataDir"
Write-Host ""
Write-Host "--- AGENT ---"
Write-Host "Binary:             $AgentPublishDir\DeploymentPoC.Agent.exe"
Write-Host "AppSettings:        $AgentPublishDir\appsettings.json"
Write-Host ""
Write-Host "--- QUICK START ---"
Write-Host ""
Write-Host "# Start Orchestrator:"
Write-Host "  cd `"$OrchestratorPublishDir`""
Write-Host "  .\DeploymentPoC.Orchestrator.exe"
Write-Host ""
Write-Host "# Start Agent (in another terminal):"
Write-Host "  cd `"$AgentPublishDir`""
Write-Host "  .\DeploymentPoC.Agent.exe"
Write-Host ""
Write-Host "# Open UI:"
Write-Host "  http://localhost:5000"
Write-Host ""
Write-Host "========================================"
