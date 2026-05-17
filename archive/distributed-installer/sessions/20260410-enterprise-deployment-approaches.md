# Enterprise Deployment Approaches for Agent Installation

This document provides concise step-by-step guidance for deploying agents to 1000+ machines using enterprise mechanisms (GPO and SCCM) as alternatives to the PoC's WinRM bootstrap.

## Table of Contents
- [Quick Reference Summaries](#quick-reference-summaries)
  - [GPO (Group Policy Object) Approach](#gpo-group-policy-object-approach)
  - [SCCM/MECM Approach](#sccmmecm-approach)
- [Approach 1: Group Policy Objects (GPO)](#approach-1-group-policy-objects-gpo)
  - [Prerequisites](#prerequisites)
  - [Step-by-Step Flow](#step-by-step-flow)
  - [Scaling Characteristics](#scaling-characteristics)
- [Approach 2: System Center Configuration Manager (SCCM/MECM)](#approach-2-system-center-configuration-manager-sccmmecm)
  - [Prerequisites](#prerequisites-1)
  - [Step-by-Step Flow](#step-by-step-flow-1)
  - [Monitoring and Reporting](#monitoring-and-reporting)
  - [Scaling Characteristics](#scaling-characteristics-1)
- [Comparison Summary](#comparison-summary)

## QUICK REFERENCE SUMMARIES

### GPO (Group Policy Object) Approach

**How fresh computers connect:**
- Machines must be domain-joined to Active Directory
- During boot/login, domain computers automatically retrieve and apply relevant GPOs from domain controllers
- No manual connection needed - it's automatic via domain authentication

**UI-based management:**
- Yes, entirely UI-based through Group Policy Management Console (GPMC)
- Administrators create/edit GPOs using graphical interface
- No command-line required for basic setup (though PowerShell available for automation)

**How startup scripts work:**
1. Administrator creates a startup script (PowerShell, batch, or executable)
2. Script is stored in SYSVOL share on domain controllers (`\\domain\SYSVOL\domain\Policies\{GPO-GUID}\Machine\Scripts\Startup\`)
3. GPO is configured to run this script during computer startup (Computer Configuration → Windows Settings → Scripts → Startup)
4. When domain-joined computers boot, they:
   - Contact domain controller for GPOs
   - Download and execute the startup script with SYSTEM privileges
   - Script performs agent installation (download binary, configure, register service)

**Scaling to 1000 machines:**
- No additional work per machine - same GPO applies to all targeted computers
- Leverages existing AD infrastructure (domain controllers handle distribution)
- SYSVOL replication ensures scripts are available globally
- Deployment is passive - happens automatically during each machine's boot cycle
- Can target specific OUs, security groups, or WMI filters for granular control

### SCCM/MECM Approach
**How it works:**
- Uses existing System Center Configuration Manager infrastructure
- Agent deployment packaged as an application or package in SCCM
- Distribution points store content (agent binaries, scripts) for local delivery
- Clients (target machines) periodically check in with management point for policies/actions

**Leveraging existing endpoint management:**
- Uses same infrastructure already managing OS patches, software inventory, etc.
- No new infrastructure needed if SCCM is already deployed
- Integrates with existing collections, boundaries, and maintenance windows

**Scaling to 1000 machines:**
- Administrator creates deployment once in SCCM console
- Content distributed to distribution points (can use peer-to-peer or branch cache for bandwidth optimization)
- Targeted to device collection (can include all 1000 machines)
- Clients automatically receive and execute deployment according to schedule
- Provides detailed reporting: success/failure per machine, retry capabilities
- Supports maintenance windows to control deployment timing

## Approach 1: Group Policy Objects (GPO)

### Prerequisites
- Active Directory domain environment
- Domain-joined target machines
- Group Policy Management Console (GPMC) access

### Step-by-Step Flow

1. **Prepare Installation Script**
   - Create PowerShell script that:
     * Downloads agent binary from central location
     * Writes configuration (orchestrator URL, registration token)
     * Registers agent as Windows Service (`sc create`)
     * Starts and verifies service
   - Store script in accessible location (e.g., file share)

2. **Create GPO**
   - Open Group Policy Management Console
   - Right-click target OU/domain → "Create a GPO in this domain, and Link it here"
   - Name GPO (e.g., "Deploy Monitoring Agent")

3. **Configure Startup Script**
   - Navigate to: Computer Configuration → Policies → Windows Settings → Scripts (Startup/Shutdown)
   - Double-click "Startup" → "Add..." → Browse to script location
   - Configure script parameters if needed
   - Set to run with appropriate privileges (typically SYSTEM)

4. **Link and Filter (Optional)**
   - Link GPO to specific OUs containing target machines
   - Use security filtering or WMI filters for granular targeting
   - Ensure "Authenticated Users" has Read/Apply permissions

5. **Replication and Execution**
   - GPO replicates to all domain controllers via SYSVOL
   - Domain-joined machines automatically:
     * Contact nearest domain controller during boot
     * Download applicable GPOs
     * Execute startup script with SYSTEM privileges
     * Install agent without admin interaction

6. **Verification**
   - Check Group Policy Results Wizard (gpresult /h) on target machines
   - Monitor orchestrator for new agent registrations
   - Review logs in %SystemRoot%\System32\GroupPolicy\

### Scaling Characteristics
- Zero-touch: Same GPO applies to 10 or 10,000 machines
- Automatic: Deployment happens during each machine's boot cycle
- Centralized: All management via GPMC UI
- Leverages existing AD infrastructure

## Approach 2: System Center Configuration Manager (SCCM/MECM)

### Prerequisites
- SCCM hierarchy installed and configured
- Distribution points deployed
- Client agents installed on target machines
- Appropriate security roles

### Step-by-Step Flow

1. **Prepare Deployment Package**
   - Gather agent binaries and installation scripts
   - Create SCCM Application or Package:
     * Source files: Agent binary + config template + install script
     * Detection method: File/registry/service check
     * Installation command: PowerShell script or executable
     * Uninstall command (if needed)
   - Distribute content to distribution points

2. **Create Deployment**
   - In SCCM Console: Assets and Compliance → Overview
   - Right-click Application/Package → "Distribute Content"
   - Select distribution points (or distribution point groups)
   - Complete content distribution wizard

3. **Target Deployment**
   - Right-click Application → "Deploy"
   - Select target collection (e.g., "All Workstations" or custom collection)
   - Configure deployment settings:
     * Purpose: Available or Required
     * Scheduling: Immediately or maintenance window
     * User experience: Allow/prevent user interaction
     * Alert thresholds for success/failure
   - Complete deployment wizard

4. **Client Execution Cycle**
   - SCCM clients poll management point (default: every 4 hours)
   - Clients download policy and check for new deployments
   - For required deployments:
     * Download content from nearest distribution point (BITS)
     * Execute installation script with local system privileges
     * Report status back to management point
   - Retry logic per client settings

5. **Monitoring and Reporting**
   - Monitor deployment status in SCCM Console
   - View reports: Deployments → [Application] → Deployment Status
   - Detailed status per machine: Success, In Progress, Error, etc.
   - Use built-in or custom reports for compliance

### Scaling Characteristics
- Enterprise-scale: Designed for 10K+ machines
- Bandwidth-aware: Uses BITS, distribution points, peer cache
- Targeted: Deploy to collections based on AD, IP ranges, etc.
- Managed: Full lifecycle tracking, retry, expiration
- Integrated: Works with existing patch/inventory management

## Comparison Summary

| Aspect | GPO | SCCM |
|--------|-----|------|
| **Infrastructure** | Uses existing AD | Requires SCCM deployment |
| **Trigger** | Machine boot/login | Client polling cycle |
| **Management UI** | Group Policy Management Console | SCCM Console |
| **Scaling** | Good for 100-10K machines | Excellent for 10K+ machines |
| **Reporting** | Basic (gpresult/logs) | Comprehensive built-in |
| **Flexibility** | Startup/shutdown/logon/logoff | Rich deployment types |
| **Best For** | Organizations heavily invested in GPO | Organizations with SCCM already deployed |

Both approaches satisfy the "no touch" requirement for system administrators during actual agent installation. All work is done centrally from management consoles, with automatic execution on target machines.
