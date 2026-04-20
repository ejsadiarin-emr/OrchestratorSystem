# Home Drilldowns and Info Hints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add row-click drilldown drawers for `Nodes Live Table` and `Workloads Overview`, move mini logs into node detail, and standardize ambiguous copy using reusable info hints on Home.

**Architecture:** Keep implementation centered in `Dashboard.tsx` and reuse existing Home mock payloads (`nodes`, `logsByNodeId`) for deterministic drilldown content. Use the existing sheet primitive for right-side drawers and derive workload detail state from node rows without backend/API expansion. Keep node actions UI-only while preserving existing dashboard cards/events behavior.

**Tech Stack:** React 19, TypeScript, Base UI sheet wrapper, Tailwind utility classes, Vitest + Testing Library.

---

## File Structure

- Modify: `apps/orchestrator/web/src/pages/Dashboard.tsx` - info hint glossary/components, row keyboard/click drilldown behavior, node/workload drawer content, remove standalone mini-log panel and row log button.
- Modify: `apps/orchestrator/web/src/pages/Dashboard.test.tsx` - lock drawer interactions, hint rendering, and mini-log relocation contract.

---

### Task 1: Add failing tests for Home drilldown + hint contract

**Files:**
- Modify: `apps/orchestrator/web/src/pages/Dashboard.test.tsx`

- [ ] **Step 1: Add node-row drilldown test (RED)**

```tsx
it('opens node detail drawer from node row and renders contextual logs', async () => {
  render(
    <MemoryRouter>
      <Dashboard />
    </MemoryRouter>,
  )

  await screen.findByText('Nodes Live Table')
  fireEvent.click(screen.getByText('node-002'))

  expect(await screen.findByText('Node Details')).toBeInTheDocument()
  expect(screen.getByText('Run paused pending explicit approval window.')).toBeInTheDocument()
})
```

- [ ] **Step 2: Add workload-row drilldown test (RED)**

```tsx
it('opens workload detail drawer from workload row with derived signal copy', async () => {
  render(
    <MemoryRouter>
      <Dashboard />
    </MemoryRouter>,
  )

  await screen.findByText('Workloads Overview')
  fireEvent.click(screen.getAllByText('Observer Stack')[0])

  expect(await screen.findByText('Workload Details')).toBeInTheDocument()
  expect(screen.getByText(/derived from node telemetry/i)).toBeInTheDocument()
})
```

- [ ] **Step 3: Add reusable info-hint assertions + mini-log relocation test (RED)**

```tsx
it('renders info hints and removes standalone mini log section from Home body', async () => {
  render(
    <MemoryRouter>
      <Dashboard />
    </MemoryRouter>,
  )

  await screen.findByText('Nodes Live Table')
  expect(screen.getByLabelText('Info: Risk (Node)')).toBeInTheDocument()
  expect(screen.getByLabelText('Info: Pending Approvals')).toBeInTheDocument()
  expect(screen.queryByText('Mini Log Viewer')).not.toBeInTheDocument()
})
```

- [ ] **Step 4: Run targeted dashboard tests to verify RED**

Run: `npm run test -- src/pages/Dashboard.test.tsx` (from `apps/orchestrator/web`)
Expected: FAIL on new node/workload drawer + info-hint assertions before implementation.

- [ ] **Step 5: Commit failing tests**

```bash
git add apps/orchestrator/web/src/pages/Dashboard.test.tsx
git commit -m "test(orchestrator/web): add failing home drilldown and hint contract"
```

### Task 2: Implement Home drawers and centralized info hints

**Files:**
- Modify: `apps/orchestrator/web/src/pages/Dashboard.tsx`

- [ ] **Step 1: Add shared glossary + hint component and wire target fields**

```tsx
const INFO_HINTS = {
  riskNode: 'Derived from node health, heartbeat age, and update drift.',
  reason: 'Machine-readable reason code for current highest-priority condition.',
  revisionUpdates: 'Count of nodes where a newer workload revision is available.',
  packageSignals: 'Aggregate package update indicators derived from node telemetry.',
  nodesRunning: 'Nodes currently not in idle/success/failed terminal states.',
  pendingApprovals: 'Workload actions blocked until explicit operator approval.',
} as const

function InfoHint({ label, hint }: { label: string; hint: string }) {
  return <button aria-label={`Info: ${label}`} title={hint}>i</button>
}
```

- [ ] **Step 2: Add right-drawer state for node/workload drilldowns**

```tsx
const [activeNodeDrawerId, setActiveNodeDrawerId] = useState<string | null>(null)
const [activeWorkloadDrawerName, setActiveWorkloadDrawerName] = useState<string | null>(null)
```

- [ ] **Step 3: Convert node rows to row-click + keyboard drilldown and remove log column/button**

```tsx
<tr
  role="button"
  tabIndex={0}
  onClick={() => {
    setSelectedNodeId(node.nodeId)
    setActiveNodeDrawerId(node.nodeId)
  }}
  onKeyDown={event => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      setSelectedNodeId(node.nodeId)
      setActiveNodeDrawerId(node.nodeId)
    }
  }}
>
```

- [ ] **Step 4: Add node detail drawer with contextual mini logs and UI-only actions**

```tsx
<Sheet open={Boolean(activeNodeDrawerId)} onOpenChange={open => !open && setActiveNodeDrawerId(null)}>
  <SheetContent side="right">
    <SheetHeader>
      <SheetTitle>Node Details</SheetTitle>
    </SheetHeader>
    {/* node signals + mini logs from logsByNodeId[activeNodeDrawerId] + action buttons */}
  </SheetContent>
</Sheet>
```

- [ ] **Step 5: Add workload row drilldown + workload detail drawer from derived rows**

```tsx
<tr
  role="button"
  tabIndex={0}
  onClick={() => setActiveWorkloadDrawerName(workload.name)}
>

<Sheet open={Boolean(activeWorkloadDrawerName)} onOpenChange={open => !open && setActiveWorkloadDrawerName(null)}>
  <SheetContent side="right">
    <SheetTitle>Workload Details</SheetTitle>
    <p>Package signals are currently derived from node telemetry (artifact-store diff pending).</p>
  </SheetContent>
</Sheet>
```

- [ ] **Step 6: Remove standalone Home mini-log section below Action Panel**

```tsx
// Delete conditional block:
// {logNodeId && <section>...Mini Log Viewer...</section>}
```

- [ ] **Step 7: Run targeted dashboard tests to verify GREEN**

Run: `npm run test -- src/pages/Dashboard.test.tsx` (from `apps/orchestrator/web`)
Expected: PASS for node/workload drawer interactions and hint coverage.

- [ ] **Step 8: Commit dashboard implementation**

```bash
git add apps/orchestrator/web/src/pages/Dashboard.tsx apps/orchestrator/web/src/pages/Dashboard.test.tsx
git commit -m "feat(orchestrator/web): add home row drilldowns and reusable info hints"
```

### Task 3: Verify no regressions and finalize implementation

**Files:**
- Modify: none expected (verification task)

- [ ] **Step 1: Run full web test suite**

Run: `npm run test` (from `apps/orchestrator/web`)
Expected: PASS.

- [ ] **Step 2: Run production build**

Run: `npm run build` (from `apps/orchestrator/web`)
Expected: PASS (TypeScript + Vite bundle).

- [ ] **Step 3: Validate changed-file scope**

Run: `git diff --name-only`
Expected: Intended paths for Home drilldown/hints implementation and no accidental unrelated edits.

- [ ] **Step 4: Commit verification adjustments only if needed**

```bash
git add apps/orchestrator/web/src/pages/Dashboard.tsx apps/orchestrator/web/src/pages/Dashboard.test.tsx
git commit -m "chore(orchestrator/web): verify home drilldown implementation"
```

## Self-Review Checklist (completed)

- Spec coverage: all acceptance criteria in `docs/superpowers/specs/2026-04-20-home-drilldown-info-hints-design.md` map to concrete tasks.
- Placeholder scan: no TODO/TBD placeholders in actionable steps.
- Type consistency: task steps use existing `OrchestratorHomeData` shape and derived workload rows without backend contract expansion.
