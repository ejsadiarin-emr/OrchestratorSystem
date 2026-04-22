# Agent is headless with no local web UI

The Agent is a pure background Windows service with no embedded web UI, no tray icon redirect, and no local web server. All operator visibility and control flows through the Orchestrator's embedded UI. This removes attack surface (no local HTTP endpoint), simplifies the Agent packaging (single .exe, no frontend bundle), and keeps the Operator mental model unified (one UI for everything).

**Status**: accepted

**Considered Options**: (1) Agent with embedded React UI for local status, (2) Agent with tray icon that redirects to Orchestrator, (3) Headless Agent with no UI.

**Consequences**: AC-106 (agent embedded UI) was removed from the PRD. FR-009 was rewritten to focus on Orchestrator UI only. The Agent cannot be operated without network connectivity to the Orchestrator.