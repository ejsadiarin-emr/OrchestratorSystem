# ADR-010: WinRM Bootstrap for PoC

Date: 2026-04-07

## Status

Accepted for PoC.

## Context

Agents need to be installed onto target machines initially. Multiple push mechanisms are available: WinRM, GPO, SCCM/MECM, and SSH. The PoC needs a practical mechanism that works with minimal infrastructure.

## Decision

Use **WinRM (PowerShell Remoting)** for agent bootstrap in the PoC.

The bootstrap flow:
1. Operator runs a PowerShell script from their machine or the orchestrator
2. Script connects to target machine via WinRM
3. Downloads agent binary, writes configuration, registers Windows service
4. Starts the service and verifies SignalR connection to orchestrator

Production deployments may use GPO, SCCM, or other enterprise mechanisms.

## Consequences

### Positive

- No additional infrastructure required — WinRM is built into Windows
- Simple to implement and test in PoC scope
- PowerShell provides rich scripting capabilities for the bootstrap logic
- Works in air-gapped environments (script and binary on local network)

### Negative

- WinRM must be enabled and configured on target machines
- Not suitable for Linux agents (SSH for phase 2)
- Enterprise-scale deployments would prefer GPO or SCCM for automation
