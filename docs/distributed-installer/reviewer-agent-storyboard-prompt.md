# Reviewer Agent Prompt: Storyboard Comparison

You are reviewing storyboard quality for a distributed installer PoC.

Review these three documents together:

1. Raw requirements notes:
- `docs/distributed-installer/sessions/20260413-raw-meeting-notes.md`

2. Storyboard A:
- `docs/distributed-installer/15-installation-and-operational-storyboards.md`

3. Storyboard B (independent):
- `docs/distributed-installer/16-installation-and-operational-storyboards-independent.md`

Goal:
- Compare Storyboard A vs Storyboard B against the raw meeting notes and industry-standard architecture/security/reliability expectations for a Windows-first orchestrator-agent installer PoC.

Review criteria (score each 1-5, explain why):
1. Coverage completeness
   - packaging media
   - fresh orchestrator install
   - sub-node/agent bootstrap
   - update flow
   - modify workload flow
   - orchestrator self-update handling
2. Requirement traceability to raw notes
3. Clarity/readability (simple wording, low ambiguity)
4. Technical correctness
   - SignalR usage boundaries
   - mTLS integration
   - package pull model with no external source
   - retry/idempotency semantics
5. Security depth
   - package trust/signing/hash
   - child process hardening
   - trust boundaries
6. Verification quality
   - explicit step-by-step verification gates
   - observable pass/fail evidence
7. PoC feasibility
   - realistic for stated scope and constraints
8. Risk handling
   - downgrade safety
   - brittle/non-idempotent job handling
   - rollback/self-healing realism

Required output format:

## A) Executive verdict
- Which storyboard is stronger overall and why (3-6 bullets)

## B) Score table
- One row per criterion, with:
  - Storyboard A score
  - Storyboard B score
  - Winner
  - Short rationale

## C) Gap list by storyboard
- `A missing/weak`
- `B missing/weak`
- Reference exact file paths and section headings where possible

## D) Critical contradictions or risky recommendations
- Call out any technical advice that may be unsafe, unclear, or not PoC-fit

## E) Merge recommendation
- Produce a concrete best-of-both merge plan:
  - keep from A
  - keep from B
  - rewrite
  - remove
- Maximum 12 actionable bullets

## F) Final recommendation
- Choose one:
  - `Adopt A`
  - `Adopt B`
  - `Merge A+B`
- Include confidence level (`High` / `Medium` / `Low`)

Important:
- Be strict about alignment to raw meeting notes.
- Prefer clear and implementable language over verbose architecture prose.
- If something is unclear, mark it explicitly as unclear instead of assuming.
