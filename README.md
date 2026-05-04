# DeploymentPoC — Package Orchestration System

A Windows-only package orchestration system for managing software deployments across agent nodes. Built with **.NET 10**, **ASP.NET Core**, **React**, and **SQLite**.

---

## Architecture

| Component | Technology | Role |
|---|---|---|
| **Orchestrator** | ASP.NET Core 10 + React + SQLite | Central management server — artifacts, workloads, agents, dispatch |
| **Agent** | .NET 10 Worker Service | Background service on Windows nodes — polls for tasks, executes locally |

Both components are published as **single-file, self-contained Windows executables** — no runtime installation required on target machines.

---

## Prerequisites

- **Build machine:** .NET 10 SDK, Node.js 20+
- **Target machines:** Windows 10/11 or Windows Server 2019+ (64-bit)

---

## Quick Start

```bash
# Build production binaries (cross-compiles win-x64 from Linux/WSL)
make dist

# Copy dist/ to Windows, then run:
#   .\Orchestrator.exe   # on the server
#   .\Agent.exe --enroll <token> --url <orchestrator-url>  # on agent nodes
```

See [`docs/manual-testing.md`](docs/manual-testing.md) for full step-by-step testing instructions.

---

## Project Structure

```
orchestrator/
  backend/         # ASP.NET Core Web API
    Controllers/   # REST API endpoints
    Data/          # EF Core DbContext
    Models/        # Entity models and enums
    Services/      # Business logic
    Configuration/ # Strongly-typed options
    Extensions/    # DI registration
    Migrations/    # EF Core migrations
  web/             # React + Vite frontend
    src/
      components/  # UI components
      pages/       # Route pages
      lib/         # API client, utilities
      types/       # TypeScript definitions
agent/
  backend/         # .NET Worker Service
    Services/      # Enrollment, reset, polling
    Models/        # AgentConfig
scripts/           # Build scripts (PowerShell + Bash)
docs/              # Documentation
dist/              # Production distribution output
```

---

## Build

```bash
make build          # Build .NET solution
make frontend       # Build React frontend
make publish        # Publish self-contained executables
make dist           # Create distribution directory
make run-dev        # Run Orchestrator in development mode
make clean          # Clean all build artifacts
```

---

## Tech Stack

- **Backend:** .NET 10, ASP.NET Core, EF Core, SQLite, Swashbuckle.AspNetCore
- **Frontend:** React 19, TypeScript, Vite, TailwindCSS, TanStack Query, React Router, Zod
- **Agent:** .NET 10 Worker Service, System.CommandLine, Windows Services
- **Build:** Single-file self-contained publish, win-x64 target

---

## License

MIT
