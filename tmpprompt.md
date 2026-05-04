/grill-me We are continuing from a previous planning session, here are what were done so far:

- Created a comprehensive MVP Plan for Orchestrator System - see @MVP_Plan_PackageOrchestration.md 
- Currently creating an comprehensive implementation plan (can be multiple files if one file is too large)

## User:
Perfect. Now I want you to create another markdown file for the implementation plan (can be multiple files if everything in one file is too large).
 
Make this like a kanban/ticket-styled issues so break down everything into smaller steps with context (and reference to the MVP Plan sections as needed), add code examples to these tickets if necessary. Start from setup tickets (ex. Setup react UI with tailwind/zod/etc. for orchestrator and show it works on the browser). Each ticket MUST have Acceptance Criteria and Verification Steps for greater feedback loop for agents and humans alike.
 
Better if the tickets are "vertical", meaning for a small feature, cover the whole bases from database to model to services to controllers to UI (IMPORTANT: Cover bases as NEEDED only for the specific task/feature to be implemented and tested - so if the task/feature/ticket doesn't involve databases and/or UI, etc. then don't force it to cover these bases or all bases. Only cover necessary ones). This is done for better testing and Dev can actually have a feedback loop (visual feedback if with UI) early on.
- For example of a ticket: workload bulk-import via JSON feature: Create/Reuse existing models, create specific endpoint/controller, create UI drag & drop frontend for users to upload the zip. Better if have unit tests and e2e with playwright to test UI/UX Flow.
 
Remember that this is an MVP so don't overengineer things and stick to than MVP Plan (always refer to the MVP Plan).
 
You may interview me for parts that need implementation details and clarifications (ex. UI design, etc.) that may not be covered in the MVP Plan but are essential for the implementation.
 
---
## Planning Agent

```md
Before I write a single ticket, I need to interview you on several implementation details that the MVP Plan doesn't cover but will directly affect how tickets are written. These aren't design preferences — they affect project structure, tooling choices, and how acceptance criteria are written.
 
One more batch — these affect the Agent project specifically:
 
While you answer those — a few final short questions I can ask in text since they're open-ended:
 
1. **Windows Service name** — what should the Agent register itself as in the SCM? Something like `OrchestratorAgent`? Or does your org have a naming convention?
 
2. **Artifact store path** — the MVP Plan shows `C:\OrchestratorData\Artifacts` as the default in `appsettings.json`. Is that acceptable or do you have a preferred default?
 
3. **API base path** — is `/api/...` the right prefix, or do you want versioning like `/api/v1/...` from the start?
 
4. **Port** — what port should the Orchestrator Kestrel server run on by default? (e.g., `5000` HTTP only for MVP, or `5001` HTTPS?)
 
5. **Ticket format preference** — should tickets be in a single large markdown file (one file per phase), or one file per epic/feature group? Given the volume, I'm leaning toward one file per phase (Phase 1 Foundation, Phase 2 Pipeline, etc.) — confirm or redirect.
```
---
## User
Q: Logging approach for both Orchestrator and Agent?
A: Built-in .NET ILogger only — simpler, good enough for MVP
 
Q: React component library preference?
A: shadcn/ui (Radix primitives + Tailwind — recommended for this type of admin tool)
 
Q: React data-fetching approach?
A: what do you recommend here? note that this is plain Vite React (TypeScript) so no server components or nextjs stuff. React assets will be exported as static assets embedded in the Orchestrator.exe .NET project.
 
---
## Planning Agent 
```md
## Recommendation: TanStack Query (React Query)
 
For this setup it's the clear winner over SWR or plain fetch:
 
- **Polling/auto-refetch built-in** — agent list and run status need to refresh automatically; TanStack Query handles this with `refetchInterval` with zero boilerplate
- **Mutation handling** — uploads, dispatch, enrollment all need loading/error/success states; TanStack's `useMutation` + cache invalidation is significantly cleaner than managing this with `useState`
- **Works perfectly with plain Vite + TypeScript** — no SSR, no framework magic, purely client-side
- SWR is solid but its mutation story is weaker; plain fetch gets messy fast once you need cache invalidation across pages after a run completes
 
---
 
## Assumptions for Remaining Unanswered Questions
 
Rather than block, I'll state these as defaults — correct any before I finalize the tickets:
 
| Question | Assumed default |
|---|---|
| Project structure | React in `/ClientApp` inside Orchestrator repo, Vite builds to `wwwroot` |
| Testing scope | Manual verification only for MVP — but tickets will have thorough verification steps |
| Orchestrator UI auth | No auth — trusted internal network (known gap, flagged) |
| DB setup | EF Core Migrations — better traceability than `EnsureCreated` even for MVP |
| UI layout | Sidebar navigation (standard for admin tools with 6+ sections) |
| UI feedback | Toasts for success/errors, modal dialogs for destructive confirmations (Update delta confirm, uninstall) |
| Windows Service name | `OrchestratorAgent` |
| Artifact store default path | `C:\OrchestratorData\Artifacts` |
| API base path | `/api` — no versioning for MVP |
| Orchestrator port | `5000` HTTP only — no TLS for MVP (internal network) |
| Ticket file format | One markdown file per phase (4 files) | 
```

-------

Given the context above (Assumptions and your assumed defaults), I will answer and question the assumptions for our shared understanding:
- Q: Project structure -> Yes, vite builds to `wwwroot`. For react, just in `orchestrator/frontend/` directory.
    - NOTE: since this is Orchestrator and Agent are separate binaries (self-contained .exe files), have it like this: `orchestrator/backend/`, `orchestrator/web/`, and `agent/backend/`.
- Q: Testing scope - yes
- Q: Orchestrator UI auth - yes
- Q: DB setup - yes
- Q: UI layout - yes
- Q: UI feedback - yes
- Q: Windows Service name - just simple `Agent` (if that is not available then `OrchAgent`)
- Q: Artifact store default path - better if we have something like `dist/` where everything is there (SQLite DB, local in-disk artifact store default path, Orchestrator.exe & Agent.exe self-contained binaries, workload definition JSON files, package manifest JSON files, artifact zip files for bulk import, etc.)
    - Furthermore, create an `.artifact-cache/` directory for storing the downloaded installer media binaries (.exe files) from the internet/from a script
    - We can create a download script that downloads artifacts/actual installer medias .exe/.msi (2 versions of packages: DBeaver (v24 and v26), python (v3.13 and v3.14), SSMS (2019 and latest)), generated package-level manifest JSON (manifest filename must match the installer media name), and 2 zip files (flat files for each, no nested dirs) at the end (ex. `artifacts-older.zip` and `artifacts-newer.zip`). Refer to MVP Plan for details if available. Interview me for questions if needed.
- Q: API base path, Orchestrator port, Ticket file format - yes

