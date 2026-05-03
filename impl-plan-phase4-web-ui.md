# Implementation Plan — Phase 4: Web UI (React)

> **MVP Plan Ref:** Section 15, Phase 4 (Items 18–26)  
> **Depends on:** Phase 1–3 complete (all backend APIs functional)

## Dependency Graph

```
P1-004 (React shell) ── P4-001 (App layout)
                       ── P4-002 (Artifacts single upload)
                       ── P4-003 (Artifacts bulk import)
                       ── P4-004 (Workloads upload)
                       ── P4-005 (Enrollment tokens)
                       ── P4-006 (Agent nodes list)
                       ── P4-011 (Artifacts list view)
                       ── P4-012 (Workloads list view)
                       ── P4-013 (Agent detail view)
                       ── P4-014 (Shared UI components)

P4-001 ──────────────── P4-007 (Run wizard — step 1)
P4-007 ──────────────── P4-008 (Run wizard — step 2)
P4-008 ──────────────── P4-009 (Run wizard — step 3)
P4-001 ──────────────── P4-010 (Run log view)

P4-014 (shared components) should be completed early, before P4-002 through P4-013
P4-011 and P4-012 should be completed before or alongside P4-002/P4-004 (upload pages redirect to list views)
```

All P4 tickets depend on the React shell (P1-004) and their corresponding backend APIs from Phases 1–3. P4-007 through P4-009 form a sequential wizard flow. P4-014 (shared components) should be built early so subsequent pages can use them.

---

## TICKET P4-001: App Shell — Layout, Sidebar Navigation, Routing

**MVP Plan Ref:** Section 15, Items 18–26 (UI container)  
**Depends on:** P1-004

### Description

Build the main application shell with sidebar navigation, responsive layout, and routing. This is the frame that all other pages render inside.

### Tasks

- [ ] Create responsive sidebar layout using shadcn/ui `SidebarProvider`, `Sidebar`, `SidebarContent`, `SidebarGroup`, `SidebarMenu`, `SidebarMenuItem`
- [ ] Sidebar navigation links:
  - **Dashboard** (`/`) — system overview
  - **Artifacts** (`/artifacts`) — list view (P4-011), with upload as sub-action
  - **Import** (`/artifacts/import`) — bulk ZIP import
  - **Workloads** (`/workloads`) — list view (P4-012), with upload as sub-action
  - **Agents** (`/agents`) — node list
  - **Enrollment** (`/enrollment`) — token generation
  - **Runs** (`/runs`) — run history
- [ ] Create `AppSidebar` component with icon + label for each nav item
- [ ] Configure `react-router-dom` routes for all pages including:
  - `/` — Dashboard
  - `/artifacts` — Artifacts list (P4-011)
  - `/artifacts/upload` — Single artifact upload (P4-002)
  - `/artifacts/import` — Bulk import (P4-003)
  - `/artifacts/:id` — Artifact detail (P4-011)
  - `/workloads` — Workloads list (P4-012)
  - `/workloads/upload` — Workload upload (P4-004)
  - `/workloads/:id/versions` — Workload versions detail (P4-012)
  - `/agents` — Agents list (P4-006)
  - `/agents/:agentId` — Agent detail (P4-013)
  - `/enrollment` — Enrollment tokens (P4-005)
  - `/runs` — Runs list (P4-010)
  - `/runs/new` — Run wizard (P4-007–009)
  - `/runs/:runId` — Run detail/log (P4-010)
- [ ] Add page header with breadcrumb-style title
- [ ] Add toast notifications using shadcn/ui `Toaster` (for success/error feedback)
- [ ] Add `.env` or `vite.config.ts` proxy for `/api` requests during development
- [ ] Create a Dashboard page that shows:
  - Total agents by status (UNREGISTERED, REGISTERED, WORKLOAD_ASSIGNED, NEEDS_UPDATE, LOST)
  - Active runs count (RUNNING, AWAITING_CONFIRMATION)
  - Recent run history (last 10 runs with status)
  - Workload summary (top 5 workloads by assigned agent count)
  - Quick-action buttons (Generate Token, Upload Artifact, Import ZIP)
- [ ] Add `GET /api/dashboard` endpoint returning all dashboard data in one call:
  - Agent counts by status
  - Active runs count
  - Recent 10 runs with summary info
  - Top 5 workloads by agent count
  - This endpoint MUST NOT require N+1 queries; all data should be aggregated server-side

### Polling Intervals (U3)

- [ ] Dashboard: refresh every 30 seconds
- [ ] Agent list: refresh every 15 seconds
- [ ] Run detail: refresh every 10 seconds while run is RUNNING, 30 seconds otherwise
- [ ] Use exponential backoff on polling errors: 2s → 4s → 8s → max 30s

### Code Example — AppSidebar

```tsx
// src/components/AppSidebar.tsx
import { Sidebar, SidebarContent, SidebarGroup, SidebarMenu, SidebarMenuItem, SidebarMenuButton } from '@/components/ui/sidebar'
import { LayoutDashboard, Package, Upload, Layers, Monitor, Key, Play } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'

const navItems = [
  { title: 'Dashboard', href: '/', icon: LayoutDashboard },
  { title: 'Artifacts', href: '/artifacts', icon: Package },
  { title: 'Import', href: '/artifacts/import', icon: Upload },
  { title: 'Workloads', href: '/workloads', icon: Layers },
  { title: 'Agents', href: '/agents', icon: Monitor },
  { title: 'Enrollment', href: '/enrollment', icon: Key },
  { title: 'Runs', href: '/runs', icon: Play },
]

export function AppSidebar() {
  const location = useLocation()

  return (
    <Sidebar>
      <SidebarContent>
        <SidebarGroup>
          <SidebarMenu>
            {navItems.map(item => (
              <SidebarMenuItem key={item.href}>
                <SidebarMenuButton asChild isActive={location.pathname === item.href}>
                  <Link to={item.href}>
                    <item.icon />
                    <span>{item.title}</span>
                  </Link>
                </SidebarMenuButton>
              </SidebarMenuItem>
            ))}
          </SidebarMenu>
        </SidebarGroup>
      </SidebarContent>
    </Sidebar>
  )
}
```

### Code Example — Dashboard Data Hook

```tsx
// src/hooks/useDashboard.ts
import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api'

export interface DashboardData {
  agentsByStatus: {
    UNREGISTERED: number
    REGISTERED: number
    WORKLOAD_ASSIGNED: number
    NEEDS_UPDATE: number
    LOST: number
  }
  activeRuns: number
  recentRuns: Array<{
    id: number
    agentHostname: string
    workloadName: string
    mode: string
    status: string
    createdAt: string
  }>
  workloadSummary: Array<{
    workloadId: string
    workloadName: string
    version: string
    agentCount: number
  }>
}

export function useDashboard() {
  return useQuery({
    queryKey: ['dashboard'],
    queryFn: () => api.get<DashboardData>('/dashboard'),
    refetchInterval: 30_000,
  })
}
```

### Acceptance Criteria

- [ ] App loads with sidebar navigation on left side
- [ ] All 7 nav items are visible with icons and labels
- [ ] Clicking each nav item navigates to the correct route without page reload
- [ ] Active page is highlighted in sidebar
- [ ] Toast notification system is functional
- [ ] Dashboard page loads with:
  - Agent counts by all 5 statuses (UNREGISTERED, REGISTERED, WORKLOAD_ASSIGNED, NEEDS_UPDATE, LOST)
  - Active runs count
  - Last 10 recent runs with status
  - Top 5 workloads by agent count
  - Quick-action buttons
- [ ] Dashboard auto-refreshes every 30 seconds
- [ ] Responsive: sidebar collapses on small screens

### Verification Steps

1. Start Orchestrator backend + Vite dev server
2. Open `http://localhost:5173` — app renders with sidebar
3. Click each sidebar item — route changes, active item highlighted
4. Dashboard shows agent counts, active runs, recent runs, workload summary
5. Verify no console errors
6. Trigger a toast notification — appears and auto-dismisses
7. Verify dashboard auto-refreshes (check network tab for polling requests every 30s)

---

## TICKET P4-002: Artifact Upload Page (Single)

**MVP Plan Ref:** Section 9 (Single artifact upload), Section 15, Item 18  
**Depends on:** P4-001, P1-009

### Description

Create the single artifact upload page. Admins upload a binary (.exe/.msi) and its manifest (.json) as a pair. The page validates the pairing and shows the result. After successful upload, the user is redirected to the Artifacts List View (`/artifacts`).

### Tasks

- [ ] Create `ArtifactUploadPage` component with:
  - File input for binary (.exe, .msi)
  - File input for manifest (.json)
  - Manifest preview: after selecting manifest JSON, parse and display `packageId`, `version`, `packageName`
  - Upload button with loading state
  - Success/error feedback via toast
- [ ] Use TanStack Query `useMutation` for the upload mutation
- [ ] API call: `POST /api/artifacts` with `multipart/form-data` containing `binary` and `manifest` files
- [ ] Zod schema for manifest JSON validation (client-side preview)
- [ ] Error handling: show validation errors (installerFile mismatch, duplicate packageId+version)
- [ ] After successful upload: show success toast, then redirect to `/artifacts` (Artifacts List View)
- [ ] "Upload Another" button that stays on the upload page
- [ ] Create shared `useArtifactUpload` hook for the mutation

### Artifact Management Operations (G4)

- [ ] Add `DELETE /api/artifacts/{id}` — soft delete artifact (marks as deleted, doesn't remove file from disk)
- [ ] Add `PUT /api/artifacts/{id}` — replace artifact file (upload new version, re-hashes and updates metadata)
- [ ] Delete button on artifact detail/list view with confirmation dialog
- [ ] Confirmation dialog must warn: "This artifact is used by N workload(s). Are you sure?"
- [ ] Prevent deletion of artifacts currently referenced by active workloads (or show warning with workload names)

### Code Example — ArtifactUploadPage

```tsx
// src/pages/ArtifactUploadPage.tsx
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { api } from '@/lib/api'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { useToast } from '@/hooks/use-toast'

interface ManifestPreview {
  packageId: string
  packageName: string
  version: string
  installerFile: string
}

export function ArtifactUploadPage() {
  const navigate = useNavigate()
  const [binaryFile, setBinaryFile] = useState<File | null>(null)
  const [manifestFile, setManifestFile] = useState<File | null>(null)
  const [manifestPreview, setManifestPreview] = useState<ManifestPreview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const { toast } = useToast()

  const uploadMutation = useMutation({
    mutationFn: async (data: FormData) => {
      return api.upload<{ id: number; packageId: string; version: string }>(
        '/artifacts', data)
    },
    onSuccess: () => {
      toast({ title: 'Artifact uploaded successfully' })
      setBinaryFile(null)
      setManifestFile(null)
      setManifestPreview(null)
      navigate('/artifacts')
    },
    onError: (err: Error) => setError(err.message),
  })

  const handleManifestSelect = async (file: File) => {
    setManifestFile(file)
    try {
      const text = await file.text()
      const json = JSON.parse(text)
      setManifestPreview({
        packageId: json.packageId,
        packageName: json.packageName,
        version: json.version,
        installerFile: json.installerFile,
      })
    } catch {
      setManifestPreview(null)
    }
  }

  const handleUpload = () => {
    if (!binaryFile || !manifestFile) return
    setError(null)
    const formData = new FormData()
    formData.append('binary', binaryFile)
    formData.append('manifest', manifestFile)
    uploadMutation.mutate(formData)
  }

  // ... render JSX with file inputs, preview, upload button
}
```

### Acceptance Criteria

- [ ] Page shows two file input fields: binary and manifest
- [ ] Selecting a manifest JSON file shows preview (packageId, version, packageName, installerFile)
- [ ] Upload button sends `multipart/form-data` with both files
- [ ] Success: toast notification, redirect to `/artifacts` list view
- [ ] "Upload Another" button stays on upload page and clears form
- [ ] Error: validation message shown (duplicate, installerFile mismatch, etc.)
- [ ] Loading state on upload button during mutation
- [ ] DELETE endpoint available: `DELETE /api/artifacts/{id}` soft-deletes
- [ ] PUT endpoint available: `PUT /api/artifacts/{id}` replaces artifact file
- [ ] Delete confirmation warns about workload associations
- [ ] Deletion blocked or warned when artifact is used by active workloads

### Verification Steps

1. Navigate to `/artifacts/upload`
2. Select a binary file and a matching manifest JSON → preview shows
3. Click Upload → success toast, redirected to `/artifacts` list
4. From list, click Upload Another → returns to upload form, cleared
5. Upload same `packageId + version` → error toast "already exists"
6. Upload manifest with `installerFile` not matching binary filename → error toast
7. Try deleting an artifact used by a workload → confirmation dialog warns
8. Try deleting an unused artifact → confirmation dialog, then soft-deleted

---

## TICKET P4-003: Artifact Bulk Import Page (ZIP)

**MVP Plan Ref:** Section 6.3 (Bulk Artifact Import), Section 15, Item 19  
**Depends on:** P4-001, P1-010

### Description

Create the bulk artifact import page. Admins upload a flat ZIP file. The page shows per-artifact import results (imported vs. failed).

### Tasks

- [ ] Create `ArtifactImportPage` component with:
  - Drag-and-drop zone for ZIP file upload
  - File selection input as fallback
  - Upload button with loading state
  - Import summary table showing:
    - Imported: packageId, version, installerFile
    - Failed: file, reason
  - Success/error feedback via toast
- [ ] TanStack Query `useMutation` for `POST /api/artifacts/bulk`
- [ ] Response parsing: show imported and failed lists in separate sections
- [ ] Color-coded status badges: green for imported, red for failed
- [ ] Option to download a template ZIP or view the expected format
- [ ] After successful import: link to navigate to `/artifacts` list view

### Code Example — Import Summary Component

```tsx
// src/components/ImportSummary.tsx
interface ImportSummaryProps {
  result: {
    imported: Array<{ packageId: string; version: string; installerFile: string }>
    failed: Array<{ file: string; reason: string }>
  }
}

export function ImportSummary({ result }: ImportSummaryProps) {
  return (
    <div className="space-y-4">
      {result.imported.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-green-600">
              Imported ({result.imported.length})
            </CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Package ID</TableHead>
                  <TableHead>Version</TableHead>
                  <TableHead>File</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.imported.map(item => (
                  <TableRow key={`${item.packageId}-${item.version}`}>
                    <TableCell>{item.packageId}</TableCell>
                    <TableCell>{item.version}</TableCell>
                    <TableCell>{item.installerFile}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {result.failed.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-red-600">
              Failed ({result.failed.length})
            </CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              {/* ... failed items table ... */}
            </Table>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
```

### Acceptance Criteria

- [ ] Drag-and-drop zone accepts ZIP files only
- [ ] Upload sends ZIP to `POST /api/artifacts/bulk`
- [ ] Import summary displays both imported and failed items in separate tables
- [ ] Imported items show green badge, failed items show red badge with reason
- [ ] Loading state during upload
- [ ] Can upload another ZIP after completing import
- [ ] Link to `/artifacts` list view after successful import

### Verification Steps

1. Navigate to `/artifacts/import`
2. Drag and drop a valid ZIP with 3 pairs → shows 3 imported, 0 failed
3. Drag and drop same ZIP again → shows 0 imported, 3 failed ("already exists")
4. Drag and drop ZIP with a missing binary → shows 1 failed with "No matching binary found"
5. Click link to `/artifacts` → navigates to artifacts list

---

## TICKET P4-004: Workload Upload Page

**MVP Plan Ref:** Section 6.2 (Workload Definition JSON), Section 15, Item 20  
**Depends on:** P4-001, P1-011

### Description

Create the workload upload page. Admins can upload a single workload JSON or an array of workload JSONs. After successful upload, the user is redirected to the Workloads List View (`/workloads`).

### Tasks

- [ ] Create `WorkloadUploadPage` component with:
  - Two input modes: upload JSON file or paste JSON in a text area
  - Auto-detect single object vs. array format
  - Preview: parse and display workload(s) before upload
  - Show workload summary: workloadId, workloadName, version, package count
  - Upload button with loading state
  - Result summary: imported, updated, failed arrays
- [ ] TanStack Query `useMutation` for `POST /api/workloads`
- [ ] Zod schema for validating workload JSON before upload
- [ ] Color-coded result: green for imported/updated, red for failed
- [ ] After successful upload: show success toast, then redirect to `/workloads` (Workloads List View)
- [ ] "Upload Another" button that stays on the upload page

### Acceptance Criteria

- [ ] Accept JSON file upload or direct text paste
- [ ] Validate JSON format (Zod schema) before submission
- [ ] Show preview of workload(s) before upload
- [ ] Upload sends to `POST /api/workloads`
- [ ] Display result summary with imported, updated, failed counts
- [ ] Upsert behavior: same `workloadId + version` updates existing record
- [ ] Success: toast notification, redirect to `/workloads` list view
- [ ] "Upload Another" button stays on upload page and clears form

### Verification Steps

1. Paste a valid single workload JSON → preview shows
2. Paste an array of 2 workloads → preview shows both
3. Click Upload → success, results shown, redirected to `/workloads`
4. Upload same workload again → "updated" in result
5. Paste invalid JSON → validation error shown before submission
6. Click Upload Another → stays on upload page, form cleared

---

## TICKET P4-005: Enrollment Token Generation Page

**MVP Plan Ref:** Section 4 (Enrollment Flow), Section 15, Item 21  
**Depends on:** P4-001, P1-005

### Description

Create the enrollment token generation page. Admins generate tokens and see the enrollment command for agents.

### Tasks

- [ ] Create `EnrollmentPage` component with:
  - "Generate Token" button
  - Generated token display (with copy-to-clipboard)
  - Token expiry display
  - Enrollment command builder: `Agent.exe --enroll <token> --url <orchestrator-url>`
  - Orchestrator URL auto-detected from current browser URL
  - List of recent tokens (with status: active/used/expired)
- [ ] TanStack Query `useMutation` for token generation
- [ ] TanStack Query `useQuery` for listing tokens (add `GET /api/enrollment/tokens` endpoint for listing)

### Code Example — Token Display

```tsx
// src/pages/EnrollmentPage.tsx
export function EnrollmentPage() {
  const [generatedToken, setGeneratedToken] = useState<{ token: string; expiresAt: string } | null>(null)
  const orchestratorUrl = `${window.location.protocol}//${window.location.host}`

  const generateMutation = useMutation({
    mutationFn: () => api.post<{ token: string; expiresAt: string }>('/enrollment/tokens'),
    onSuccess: (data) => setGeneratedToken(data),
  })

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>Generate Enrollment Token</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <Button onClick={() => generateMutation.mutate()} disabled={generateMutation.isPending}>
            Generate New Token
          </Button>

          {generatedToken && (
            <div className="space-y-2">
              <div className="flex items-center gap-2">
                <code className="rounded bg-muted p-2 text-sm">
                  {generatedToken.token}
                </code>
                <Button variant="outline" size="sm" onClick={() => navigator.clipboard.writeText(generatedToken.token)}>
                  Copy
                </Button>
              </div>
              <p className="text-sm text-muted-foreground">
                Expires: {new Date(generatedToken.expiresAt).toLocaleString()}
              </p>
              <div className="mt-4 rounded border bg-muted p-4">
                <p className="text-sm font-medium">Enrollment Command:</p>
                <code className="text-sm">
                  Agent.exe --enroll {generatedToken.token} --url {orchestratorUrl}
                </code>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
```

### Acceptance Criteria

- [ ] "Generate Token" button creates a new token via API
- [ ] Generated token displayed with copy-to-clipboard button
- [ ] Token expiry shown in human-readable format
- [ ] Complete enrollment command displayed with token and orchestrator URL
- [ ] URL auto-detected from browser's current location
- [ ] Recent tokens listed with status badges (active/used/expired)

### Verification Steps

1. Navigate to `/enrollment`
2. Click "Generate Token" → token appears with expiry
3. Copy token → clipboard contains the token
4. Verify enrollment command format: `Agent.exe --enroll <token> --url http://host:5000`
5. Use the token to enroll via API → success, token marked as "used"

---

## TICKET P4-006: Agent Nodes List Page

**MVP Plan Ref:** Section 15, Item 22  
**Depends on:** P4-001, P1-006

### Description

Create the agent nodes list page showing all enrolled agents with status, assigned workload, and last seen timestamp.

### Tasks

- [ ] Create `AgentsPage` component with:
  - Table of all agents with columns: Hostname, IP, Status, Assigned Workload, Last Seen, Registered At
  - Status badges with all 5 states, each with appropriate icon and color (see Status Display section at end of document):
    - UNREGISTERED — gray dot, "Unregistered" label
    - REGISTERED — blue dot, "Registered" label
    - WORKLOAD_ASSIGNED — green dot, "Active" label
    - NEEDS_UPDATE — orange/amber dot, "Update Available" label
    - LOST — red dot, "Lost" label
  - Auto-refresh using TanStack Query `refetchInterval` (every 15 seconds)
  - Clickable rows linking to `/agents/{agentId}` detail view (P4-013)
- [ ] Add `GET /api/agents` endpoint (list all agents with status and workload assignment)
- [ ] Add `GET /api/agents/{agentId}` endpoint (single agent detail — see P4-013)
- [ ] TanStack Query `useQuery` with polling for real-time status updates

### Polling Intervals (U3)

- [ ] Agent list: refresh every 15 seconds
- [ ] Use exponential backoff on polling errors: 2s → 4s → 8s → max 30s

### Acceptance Criteria

- [ ] Table shows all enrolled agents with correct data
- [ ] Status badges display all 5 states with correct colors and icons:
  - UNREGISTERED: gray dot, "Unregistered"
  - REGISTERED: blue dot, "Registered"
  - WORKLOAD_ASSIGNED: green dot, "Active"
  - NEEDS_UPDATE: orange dot, "Update Available"
  - LOST: red dot, "Lost"
- [ ] Auto-refresh every 15 seconds
- [ ] Clickable rows navigate to `/agents/{agentId}` detail view
- [ ] Empty state shows "No agents enrolled" message with CTA to generate enrollment token
- [ ] Exponential backoff on polling errors

### Verification Steps

1. Navigate to `/agents` → table shows enrolled agents
2. Stop an agent → after LOST threshold, status changes to LOST (red badge)
3. Wait for auto-refresh → status updates without manual page refresh
4. Click on an agent row → navigates to `/agents/{agentId}` detail view
5. No agents enrolled → empty state message with CTA button
6. Simulate polling error → backoff from 2s to 4s to 8s

---

## TICKET P4-007: Run Wizard — Step 1: Select Agent, Workload & Mode

**MVP Plan Ref:** Section 15, Item 23  
**Depends on:** P4-001, P2-005

### Description

Create the first step of the run wizard: select agent, select workload (and version), choose execution mode (INSTALL, UPDATE, UNINSTALL). PRE_CHECK is not a mode selection — it is automatically dispatched in Step 2.

> **Wizard State:** This step stores selections in the shared wizard state (see [Wizard State Management](#wizard-state-management-g5) at the end of this document).

### Tasks

- [ ] Create `RunWizardPage` component with multi-step form
- [ ] Implement shared wizard state using React state management (see Wizard State Management section)
- [ ] Step 1 form:
  - **Agent selection**: dropdown/table of available agents (filtered by status — must be REGISTERED, WORKLOAD_ASSIGNED, or NEEDS_UPDATE)
  - **Workload selection**: dropdown of available workloads, with version selector (loads versions after workload selection)
  - **Mode selection**: radio buttons for INSTALL, UPDATE, UNINSTALL
  - Mode validation based on agent status:
    - INSTALL: available when agent is REGISTERED (no workload assigned)
    - UPDATE: available when agent is WORKLOAD_ASSIGNED or NEEDS_UPDATE
    - UNINSTALL: available when agent is WORKLOAD_ASSIGNED
    - PRE_CHECK is NOT a mode selection — it is automatically dispatched in Step 2
  - Disable modes that are not valid for the current agent status
  - Show tooltip explaining why a mode is disabled (e.g., "UPDATE requires an agent with an assigned workload")
  - "Next" button to proceed to Step 2 (stores selections in wizard state)
- [ ] Add `GET /api/workloads` endpoint (list all workloads with versions)
- [ ] Add `GET /api/workloads/{workloadId}/versions` endpoint (list versions of a workload)

### Acceptance Criteria

- [ ] Agent dropdown shows only REGISTERED, WORKLOAD_ASSIGNED, and NEEDS_UPDATE agents
- [ ] Workload dropdown shows available workloads, version sub-selector after workload selection
- [ ] Mode radio buttons correctly disable invalid options:
  - Agent with REGISTERED status → only INSTALL available, UPDATE/UNINSTALL disabled
  - Agent with WORKLOAD_ASSIGNED status → UPDATE and UNINSTALL available
  - Agent with NEEDS_UPDATE status → UPDATE available
  - Tooltips explain why disabled modes are unavailable
- [ ] "Next" button disabled until all selections made
- [ ] Selections stored in wizard state for Steps 2 and 3
- [ ] Browser back/forward navigates between wizard steps with state preserved
- [ ] Cancel button resets wizard state

### Verification Steps

1. Navigate to `/runs/new`
2. Select an agent with REGISTERED status → only INSTALL mode available, UPDATE/UNINSTALL disabled
3. Hover over disabled UPDATE → tooltip says "UPDATE requires an agent with an assigned workload"
4. Select an agent with WORKLOAD_ASSIGNED status → UPDATE and UNINSTALL available, INSTALL disabled
5. Select an agent with NEEDS_UPDATE status → UPDATE available
6. Select workload and version → "Next" button enabled
7. Click "Next" → navigates to step 2, state preserved
8. Click browser back → returns to step 1 with previous selections intact
9. Click Cancel → wizard state reset, returns to /runs

---

## TICKET P4-008: Run Wizard — Step 2: Pre-Check Results & Delta Summary

**MVP Plan Ref:** Section 15, Item 24 (Delta summary view), Section 10 (Pre-Checks)  
**Depends on:** P4-007, P2-005, P2-008

### Description

Step 2 of the wizard: dispatch PRE_CHECK, display results and delta summary showing per-package status (MATCHES, MISSING, VERSION_DRIFT, AHEAD, ORPHANED).

> **Wizard State:** This step reads `selectedAgentId`, `selectedWorkloadId`, `selectedWorkloadVersion`, and `selectedMode` from wizard state. See [Wizard State Management](#wizard-state-management-g5).

### Tasks

- [ ] On entering Step 2, automatically dispatch PRE_CHECK run for the selected agent + workload
- [ ] Show loading spinner while pre-check is in progress (poll `GET /api/runs/{runId}` for status)
- [ ] Once pre-check completes, display delta summary table:
  - Columns: Package, Installed Version, Required Version, Status
  - Status badges: MATCHES (green), MISSING (red), VERSION_DRIFT (yellow), AHEAD (orange), ORPHANED (purple)
- [ ] For UPDATE mode: highlight ORPHANED packages with a callout explaining they will be uninstalled
- [ ] "Proceed" button to advance to Step 3 (confirmation)
- [ ] "Back" button to return to Step 1 with wizard state preserved
- [ ] If PRE_CHECK fails, show error with retry option

### Race Condition Handling (U1)

- [ ] After user clicks "Proceed", the UI MUST:
  1. Disable the "Proceed" button immediately (prevent double-submit)
  2. Show a loading state on the button
  3. Wait for the API response before transitioning
  4. On success: advance to Step 3 with state preserved
  5. On failure: re-enable the button and show error message via toast
- [ ] Add optimistic locking: the `POST /api/runs` endpoint should verify the agent is still in a valid state (WORKLOAD_ASSIGNED or NEEDS_UPDATE for UPDATE, REGISTERED for INSTALL) before creating the run
- [ ] If agent state changed between form load and submission, the API returns `409 Conflict` — the UI must handle this by:
  - Showing an error: "The agent state has changed. Please restart the wizard."
  - Redirecting back to Step 1 to re-select agent/mode
- [ ] Pre-check dispatch should also guard against stale agent state — if the agent's workload assignment changed since Step 1, show a warning and offer to restart the wizard

### Code Example — Delta Summary Table

```tsx
// src/components/DeltaSummary.tsx
const statusColors: Record<string, string> = {
  MATCHES: 'bg-green-100 text-green-800',
  MISSING: 'bg-red-100 text-red-800',
  VERSION_DRIFT: 'bg-yellow-100 text-yellow-800',
  AHEAD: 'bg-orange-100 text-orange-800',
  ORPHANED: 'bg-purple-100 text-purple-800',
}

export function DeltaSummary({ delta }: { delta: DeltaSummary }) {
  return (
    <div className="space-y-4">
      <h3 className="text-lg font-semibold">Delta Summary</h3>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Package</TableHead>
            <TableHead>Installed</TableHead>
            <TableHead>Required</TableHead>
            <TableHead>Status</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {delta.packages.map(pkg => (
            <TableRow key={pkg.packageId}>
              <TableCell>{pkg.packageId}</TableCell>
              <TableCell>{pkg.installedVersion ?? '—'}</TableCell>
              <TableCell>{pkg.requiredVersion ?? '—'}</TableCell>
              <TableCell>
                <Badge className={statusColors[pkg.status]}>
                  {pkg.status}
                </Badge>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {delta.packages.some(p => p.status === 'ORPHANED') && (
        <Alert variant="destructive">
          <AlertDescription>
            The following packages will be <strong>uninstalled</strong> as they are
            not present in the new workload version:
            {delta.packages.filter(p => p.status === 'ORPHANED').map(p => p.packageId).join(', ')}
          </AlertDescription>
        </Alert>
      )}
    </div>
  )
}
```

### Acceptance Criteria

- [ ] Entering Step 2 auto-dispatches PRE_CHECK run
- [ ] Loading state shown while pre-check is in progress (polling for updates)
- [ ] Delta summary table displays after pre-check completion
- [ ] Status badges color-coded correctly
- [ ] ORPHANED packages highlighted with warning/callout
- [ ] "Proceed" button is immediately disabled on click, shows loading state, waits for API response
- [ ] On `409 Conflict` (agent state changed): error shown, user redirected to Step 1
- [ ] On other API errors: button re-enabled, error toast shown
- [ ] "Back" button returns to Step 1 with selections preserved
- [ ] Pre-check failure shows error with retry option

### Verification Steps

1. Complete Step 1, advance to Step 2
2. Pre-check dispatched automatically → loading spinner shown
3. Pre-check completes → delta summary table appears
4. Verify status badges match expected colors
5. If UPDATE mode: orphaned packages shown with warning callout
6. Click "Proceed" → button disables immediately, loading state shown
7. On success: advances to Step 3
8. On 409 Conflict: error shown, redirected to Step 1
9. On other error: button re-enabled, error toast shown

---

## TICKET P4-009: Run Wizard — Step 3: Confirmation & Execution Gate

**MVP Plan Ref:** Section 15, Item 25 (Admin confirmation step)  
**Depends on:** P4-008

### Description

Step 3 of the wizard: admin reviews the delta summary and confirms execution. The confirmation gate behavior differs by mode:
- For UPDATE mode: shows "Awaiting Confirmation" status with a "Confirm" button
- For INSTALL/UNINSTALL mode: skips confirmation, dispatches directly, shows "Running" status

> **Wizard State:** This step reads from wizard state and dispatches the run. See [Wizard State Management](#wizard-state-management-g5).

### Tasks

- [ ] Display confirmation summary:
  - Mode (INSTALL / UPDATE / UNINSTALL)
  - Agent name and hostname
  - Workload and version
  - Packages to install/update/skip/uninstall (per delta summary)
- [ ] For UPDATE mode specifically:
  - Show orphaned packages that will be uninstalled
  - Require explicit checkbox: "I confirm the removal of orphaned packages: [list]"
  - Disable "Execute" button until confirmation checkbox is checked
  - After dispatch: run enters AWAITING_CONFIRMATION status
  - Show "Awaiting Confirmation" status with orange badge
  - "Confirm" button calls `POST /api/runs/{id}/confirm` with `{ confirmed: true }`
  - "Reject" button calls `POST /api/runs/{id}/confirm` with `{ confirmed: false, reason: "..." }`
- [ ] For INSTALL mode:
  - Show packages that will be installed and those that will be skipped
  - Simple confirmation (no extra checkbox needed)
  - Dispatches directly — no AWAITING_CONFIRMATION state
- [ ] For UNINSTALL mode:
  - Show all packages listed for removal
  - Simple confirmation (no extra checkbox needed)
  - Dispatches directly — no AWAITING_CONFIRMATION state
- [ ] "Execute" button dispatches the run and shows progress
- [ ] After dispatch: redirect to Run Log view (`/runs/{runId}`)
- [ ] "Back" button returns to Step 2 with wizard state preserved

### Confirmation Gate Backend Endpoint (U2)

- [ ] `POST /api/runs/{id}/confirm` — confirm or reject an AWAITING_CONFIRMATION run (defined in Phase 3)
  - Request body: `{ confirmed: boolean, reason?: string }`
  - On confirm (`{ confirmed: true }`): transitions run from AWAITING_CONFIRMATION to RUNNING
  - On reject (`{ confirmed: false, reason: "..." }`): transitions run from AWAITING_CONFIRMATION to FAILED
- [ ] The confirmation page handles both flows:
  - UPDATE mode: dispatches run → AWAITING_CONFIRMATION → admin confirms/rejects via button
  - INSTALL/UNINSTALL mode: dispatches run → immediately RUNNING → redirect to log view

### Acceptance Criteria

- [ ] Confirmation page shows all planned actions clearly
- [ ] UPDATE mode: orphan removal confirmation checkbox required before Execute
- [ ] UPDATE mode: after dispatch, shows AWAITING_CONFIRMATION with Confirm/Reject buttons
- [ ] INSTALL mode: packages listed with skip/install status, dispatches directly
- [ ] UNINSTALL mode: all packages listed for removal, dispatches directly
- [ ] Confirm button calls `POST /api/runs/{id}/confirm` with `{ confirmed: true }`
- [ ] Reject button calls `POST /api/runs/{id}/confirm` with `{ confirmed: false, reason: "..." }`
- [ ] "Execute" button dispatches the appropriate run mode
- [ ] After dispatch: redirect to run detail/log page
- [ ] "Back" button returns to Step 2

### Verification Steps

1. Complete Steps 1-2, reach Step 3
2. Verify all actions displayed correctly
3. For UPDATE mode: verify orphan confirmation checkbox is present and required
4. Click Execute without checking orphan box → button disabled
5. Check orphan box → Execute button enabled
6. Click Execute → run dispatched; if UPDATE mode, shows AWAITING_CONFIRMATION
7. Click Confirm → `POST /api/runs/{id}/confirm` called, run transitions to RUNNING
8. Click Reject → `POST /api/runs/{id}/confirm` called with reason, run transitions to FAILED
9. For INSTALL/UNINSTALL mode: Execute dispatches directly, redirected to run log
10. Click "Back" → returns to Step 2 with state preserved

---

## TICKET P4-010: Run Log / Step Audit View

**MVP Plan Ref:** Section 15, Item 26  
**Depends on:** P4-001, P2-003

### Description

Create the run log page that shows per-command granularity for a WorkloadRun: each step with its action, status, message, exit code, and timestamps.

### Tasks

- [ ] Create `RunLogPage` component at `/runs/{runId}`
- [ ] Add `GET /api/runs` endpoint (list all runs with basic info, optional agent/workload filter)
- [ ] Add `GET /api/runs/{runId}` endpoint returning run details with steps
- [ ] Add `GET /api/runs/{runId}/steps` endpoint returning all steps for a run
- [ ] Display run header: agent hostname, workload name, mode, status, timestamps
- [ ] Steps table with columns:
  - Package ID
  - Action (DETECT, PRE_INIT_STEP, INSTALL, POST_INIT_STEP, VERIFY, SKIP, UPDATE, UNINSTALL)
  - Status (PENDING, RUNNING, SUCCESS, FAILED, SKIPPED, PARTIAL_SUCCESS)
  - Message (stdout/stderr output or error)
  - Exit Code
  - Started At / Completed At
- [ ] Status badges color-coded (see Status Display section):
  - SUCCESS=green, FAILED=red, RUNNING=pulsing blue, PENDING=blue, PARTIAL_SUCCESS=yellow, SKIPPED=gray
- [ ] Auto-refresh using TanStack Query `refetchInterval` (every 10 seconds while run is RUNNING, 30 seconds otherwise)
- [ ] Stop auto-refresh when run status is terminal (SUCCESS, FAILED, SKIPPED)
- [ ] Add cancel endpoint: `DELETE /api/runs/{runId}` — cancel a PENDING or AWAITING_CONFIRMATION run

### Polling Intervals (U3)

- [ ] Run detail: refresh every 10 seconds while run is RUNNING, 30 seconds otherwise
- [ ] Use exponential backoff on polling errors: 2s → 4s → 8s → max 30s

### Code Example — Step Status Badge

```tsx
// src/components/StepStatusBadge.tsx
const stepStatusVariants: Record<string, { bg: string; text: string }> = {
  PENDING: { bg: 'bg-blue-100', text: 'text-blue-800' },
  RUNNING: { bg: 'bg-blue-100', text: 'text-blue-800' },
  SUCCESS: { bg: 'bg-green-100', text: 'text-green-800' },
  FAILED: { bg: 'bg-red-100', text: 'text-red-800' },
  SKIPPED: { bg: 'bg-gray-100', text: 'text-gray-600' },
  PARTIAL_SUCCESS: { bg: 'bg-yellow-100', text: 'text-yellow-800' },
}

export function StepStatusBadge({ status }: { status: string }) {
  const variant = stepStatusVariants[status] ?? stepStatusVariants.PENDING
  return (
    <Badge className={`${variant.bg} ${variant.text}`}>
      {status}
    </Badge>
  )
}
```

### Acceptance Criteria

- [ ] Run log page shows all steps for a run with full detail
- [ ] Steps table displays: package, action, status, message, exit code, timestamps
- [ ] Status badges color-coded correctly
- [ ] Auto-refresh updates steps every 10s while run is RUNNING
- [ ] Auto-refresh switches to 30s when run is not RUNNING
- [ ] Auto-refresh stops when run reaches terminal status (SUCCESS, FAILED, SKIPPED)
- [ ] Previous run steps are visible (no clearing on new steps)
- [ ] Runs list page (`/runs`) shows all runs with basic info
- [ ] Cancel button available for PENDING/AWAITING_CONFIRMATION runs via `DELETE /api/runs/{runId}`
- [ ] Exponential backoff on polling errors

### Verification Steps

1. Navigate to `/runs` → list of all runs
2. Click on a run → run log page shows steps
3. While run is RUNNING → steps update automatically every 10 seconds
4. After run completes → auto-refresh switches to 30s, then stops
5. Verify step details: action, status, message, exit code all displayed
6. Check color coding matches expected status colors
7. Cancel a PENDING run → run transitions to FAILED/SKIPPED

---

## TICKET P4-011: Artifacts List View

**MVP Plan Ref:** Section 15, Items 18–26 (Artifacts management)  
**Depends on:** P4-001, P4-014

### Description

Create the Artifacts List View page showing all uploaded artifacts in a searchable table. Clicking a row navigates to the artifact detail view. This page is the landing page after uploading an artifact (P4-002 redirects here on success).

### Tasks

- [ ] Create `ArtifactsListPage` component at `/artifacts`
- [ ] Table listing all uploaded artifacts with columns: ID, Filename, Upload Date, Hash, Size, Workload Associations
- [ ] Clickable rows navigating to `/artifacts/{id}` detail view
- [ ] Search/filter by filename (client-side filter or server-side query parameter)
- [ ] Delete artifact button (with confirmation dialog from P4-014)
  - Confirmation dialog warns: "This artifact is used by N workload(s). Are you sure?"
  - Prevent deletion of artifacts currently referenced by active workloads (or show warning with workload names)
- [ ] "Upload Artifact" action button linking to `/artifacts/upload`
- [ ] TanStack Query `useQuery` for fetching artifacts with `GET /api/artifacts`
- [ ] Add `GET /api/artifacts` endpoint (list all artifacts with pagination, search)
- [ ] Pagination support (page, pageSize parameters)
- [ ] Uses shared `ErrorState`, `LoadingState`, `EmptyState` components from P4-014

### API Endpoint

```
GET /api/artifacts?page=1&pageSize=20&search=filename
```

Response:
```json
{
  "items": [
    {
      "id": 1,
      "filename": "installer.msi",
      "packageId": "com.example.app",
      "version": "1.0.0",
      "sha256Hash": "abc123...",
      "sizeBytes": 52428800,
      "uploadedAt": "2025-01-15T10:30:00Z",
      "workloadCount": 3
    }
  ],
  "total": 42,
  "page": 1,
  "pageSize": 20
}
```

- Authentication: admin-only (not agent auth)

### Acceptance Criteria

- [ ] Table shows all uploaded artifacts with ID, filename, upload date, hash, size, workload associations
- [ ] Clicking a row navigates to `/artifacts/{id}` detail view
- [ ] Search/filter by filename works (debounced input)
- [ ] Delete button shows confirmation dialog referencing workload associations
- [ ] Delete blocked/warned when artifact is used by active workloads
- [ ] "Upload Artifact" button links to `/artifacts/upload`
- [ ] Pagination works correctly
- [ ] Empty state shows "No artifacts uploaded" with CTA to upload
- [ ] Error and loading states handled with shared components

### Verification Steps

1. Navigate to `/artifacts` → table shows all uploaded artifacts
2. Click on a row → navigates to `/artifacts/{id}`
3. Search by filename → table filters results
4. Click delete on an artifact used by a workload → confirmation warns "used by N workload(s)"
5. Click delete on an unused artifact → confirmation dialog, then soft-deleted
6. Empty state: no artifacts → "No artifacts uploaded" with upload CTA
7. Pagination: navigate between pages

---

## TICKET P4-012: Workloads List View

**MVP Plan Ref:** Section 15, Items 18–26 (Workloads management)  
**Depends on:** P4-001, P4-014

### Description

Create the Workloads List View page showing all workload definitions in a filterable table. Clicking a row navigates to the workload versions detail view. This page is the landing page after uploading a workload (P4-004 redirects here on success).

### Tasks

- [ ] Create `WorkloadsListPage` component at `/workloads`
- [ ] Table listing all workload definitions with columns: ID, Name, Version, Package Count, Last Updated
- [ ] Clickable rows navigating to `/workloads/{id}/versions` detail view
- [ ] Filter by name and version
- [ ] Status indicators for workload versions (latest version highlighted)
- [ ] "Upload Workload" action button linking to `/workloads/upload`
- [ ] TanStack Query `useQuery` for fetching workloads with `GET /api/workloads`
- [ ] Add `GET /api/workloads` endpoint (list all workloads with pagination)
- [ ] Add `GET /api/workloads/{id}/versions` endpoint (list versions of a workload)
- [ ] Pagination support (page, pageSize parameters)
- [ ] Uses shared `ErrorState`, `LoadingState`, `EmptyState` components from P4-014

### API Endpoints

```
GET /api/workloads?page=1&pageSize=20&name=filter&version=filter
```

Response:
```json
{
  "items": [
    {
      "workloadId": "wl-001",
      "workloadName": "Office Suite",
      "version": "2.0.0",
      "packageCount": 5,
      "updatedAt": "2025-01-15T10:30:00Z"
    }
  ],
  "total": 15,
  "page": 1,
  "pageSize": 20
}
```

```
GET /api/workloads/{id}/versions
```

Response:
```json
{
  "workloadId": "wl-001",
  "workloadName": "Office Suite",
  "versions": [
    {
      "version": "2.0.0",
      "packageCount": 5,
      "createdAt": "2025-01-15T10:30:00Z",
      "isLatest": true
    },
    {
      "version": "1.0.0",
      "packageCount": 4,
      "createdAt": "2024-12-01T08:00:00Z",
      "isLatest": false
    }
  ]
}
```

- Authentication: admin-only (not agent auth)

### Acceptance Criteria

- [ ] Table shows all workload definitions with ID, name, version, package count, last updated
- [ ] Clicking a row navigates to `/workloads/{id}/versions`
- [ ] Filter by name and version works
- [ ] Status indicators show latest version highlighted
- [ ] "Upload Workload" button links to `/workloads/upload`
- [ ] Pagination works correctly
- [ ] Empty state shows "No workloads defined" with CTA to upload
- [ ] Error and loading states handled with shared components

### Verification Steps

1. Navigate to `/workloads` → table shows all workload definitions
2. Click on a row → navigates to `/workloads/{id}/versions`
3. Filter by name → table filters results
4. Filter by version → table filters results
5. Latest version highlighted with indicator
6. Empty state: no workloads → "No workloads defined" with upload CTA
7. Pagination: navigate between pages

---

## TICKET P4-013: Agent Detail View

**MVP Plan Ref:** Section 15, Item 22 (Agent detail)  
**Depends on:** P4-001, P4-006, P4-014

### Description

Create the Agent Detail View page showing all information for a single agent: status, assigned workload, package states, run history, and action buttons.

### Tasks

- [ ] Create `AgentDetailPage` component at `/agents/{agentId}`
- [ ] Show agent overview section:
  - Agent name/hostname
  - Status (with full color/icon badge per Status Display section)
  - Assigned workload (name, version) — or "None" if REGISTERED
  - Last heartbeat timestamp
  - Polling interval
  - Enrollment date
- [ ] Show package states section:
  - List of installed packages with versions
  - Compare against assigned workload's expected packages
  - Show drift indicators (version mismatches)
- [ ] Show run history section:
  - Filtered list of WorkloadRuns for this agent
  - Columns: Run ID, Workload, Mode, Status, Started At, Completed At
  - Clickable rows linking to `/runs/{runId}`
- [ ] Show action buttons:
  - "Enroll New Token" — generates an enrollment token for this agent
  - "Delete Agent" — removes agent record (with confirmation dialog from P4-014)
- [ ] TanStack Query `useQuery` for fetching agent detail with `GET /api/agents/{id}`
- [ ] Add `GET /api/agents/{id}` endpoint returning:
  - Agent metadata (name, IP, status, enrolled workload, heartbeat, polling interval, enrollment date)
  - Package states (list of installed packages with versions)
  - Pagination: `?includeRuns=true&page=1&pageSize=10`
- [ ] Auto-refresh agent status using TanStack Query `refetchInterval` (every 15 seconds)
- [ ] Uses shared `ErrorState`, `LoadingState`, `StatusBadge` components from P4-014

### API Endpoint

```
GET /api/agents/{id}?includeRuns=true&page=1&pageSize=10
```

Response:
```json
{
  "agentId": "a1b2c3",
  "hostname": "WORKSTATION-01",
  "ipAddress": "192.168.1.100",
  "status": "WORKLOAD_ASSIGNED",
  "assignedWorkload": {
    "workloadId": "wl-001",
    "workloadName": "Office Suite",
    "version": "2.0.0"
  },
  "packageStates": [
    { "packageId": "com.example.app", "installedVersion": "1.0.0", "expectedVersion": "1.0.0", "status": "MATCHES" },
    { "packageId": "com.example.lib", "installedVersion": "2.1.0", "expectedVersion": "2.2.0", "status": "VERSION_DRIFT" }
  ],
  "lastHeartbeat": "2025-01-15T10:25:00Z",
  "pollingInterval": 30,
  "enrolledAt": "2025-01-01T09:00:00Z",
  "runs": {
    "items": [
      { "id": 42, "workloadName": "Office Suite", "mode": "INSTALL", "status": "SUCCESS", "startedAt": "2025-01-15T10:00:00Z", "completedAt": "2025-01-15T10:05:00Z" }
    ],
    "total": 5,
    "page": 1,
    "pageSize": 10
  }
}
```

- Authentication: admin-only (not agent auth)

### Acceptance Criteria

- [ ] Agent overview shows: name, status, assigned workload, last heartbeat, polling interval, enrollment date
- [ ] Status displayed with correct color/icon per Status Display section
- [ ] Package states show installed vs. expected versions with drift indicators
- [ ] Run history shows filtered runs for this agent with pagination
- [ ] Clickable run rows link to `/runs/{runId}`
- [ ] "Enroll New Token" button generates token for this agent
- [ ] "Delete Agent" button shows confirmation dialog, then deletes
- [ ] Auto-refreshes every 15 seconds
- [ ] Empty states for no runs, no package states

### Verification Steps

1. Navigate to `/agents/{agentId}` → agent detail shows
2. Verify all agent metadata fields displayed correctly
3. Status badge matches expected color for WORKLOAD_ASSIGNED (green, "Active")
4. Package states show drift indicators for version mismatches
5. Run history table shows runs for this agent
6. Click a run row → navigates to `/runs/{runId}`
7. Click "Enroll New Token" → token generated and displayed
8. Click "Delete Agent" → confirmation dialog, then deletes
9. Auto-refresh: wait 15 seconds → status updates without page reload

---

## TICKET P4-014: Shared UI Components

**MVP Plan Ref:** Section 15 (cross-cutting UI concerns)  
**Depends on:** P4-001

### Description

Create shared UI components that will be used across all pages. These should be built early (after P4-001) so all subsequent pages can reference them.

### Tasks

- [ ] Create `ErrorState` component:
  - Props: `message: string`, `onRetry?: () => void`
  - Displays error message with a retry button
  - Uses destructive alert style from shadcn/ui
- [ ] Create `LoadingState` component:
  - Props: `message?: string`
  - Displays skeleton/spinner with optional message
  - Uses shadcn/ui skeleton components
- [ ] Create `EmptyState` component:
  - Props: `icon?: LucideIcon`, `message: string`, `actionLabel?: string`, `onAction?: () => void`
  - Displays icon + message + optional CTA button
  - Centered layout with muted colors
- [ ] Create `ConfirmDialog` component:
  - Props: `open: boolean`, `onOpenChange: (open: boolean) => void`, `title: string`, `description: string`, `onConfirm: () => void`, `variant?: 'default' | 'destructive'`, `confirmLabel?: string`
  - Reusable confirmation modal using shadcn/ui AlertDialog
  - Used for delete confirmations, unsaved changes warnings, etc.
- [ ] Create `StatusBadge` component:
  - Props: `status: string`, `size?: 'sm' | 'md'`
  - Colored badge for agent/run/workload statuses
  - Uses the status display specification (see Status Display section at end of document)
  - Agent statuses: UNREGISTERED (gray), REGISTERED (blue), WORKLOAD_ASSIGNED (green), NEEDS_UPDATE (orange), LOST (red)
  - Run statuses: PENDING (blue), RUNNING (pulsing blue), SUCCESS (green), FAILED (red), SKIPPED (gray), AWAITING_CONFIRMATION (orange)
- [ ] Create `DataTable` component (optional, for consistent table usage):
  - Props: `data`, `columns`, `onRowClick?`, `pagination?`, `isLoading?`, `emptyMessage?`
  - Wraps shadcn/ui Table with built-in pagination, empty state, and loading state
- [ ] All components should use consistent styling from shadcn/ui theme
- [ ] Export all components from `src/components/shared/index.ts`

### Code Example — StatusBadge

```tsx
// src/components/shared/StatusBadge.tsx
import { Badge } from '@/components/ui/badge'

const statusConfig: Record<string, { bg: string; text: string; label: string }> = {
  // Agent statuses
  UNREGISTERED: { bg: 'bg-gray-100', text: 'text-gray-800', label: 'Unregistered' },
  REGISTERED: { bg: 'bg-blue-100', text: 'text-blue-800', label: 'Registered' },
  WORKLOAD_ASSIGNED: { bg: 'bg-green-100', text: 'text-green-800', label: 'Active' },
  NEEDS_UPDATE: { bg: 'bg-orange-100', text: 'text-orange-800', label: 'Update Available' },
  LOST: { bg: 'bg-red-100', text: 'text-red-800', label: 'Lost' },
  
  // Run statuses
  PENDING: { bg: 'bg-blue-100', text: 'text-blue-800', label: 'Pending' },
  RUNNING: { bg: 'bg-blue-100', text: 'text-blue-800', label: 'Running' },
  SUCCESS: { bg: 'bg-green-100', text: 'text-green-800', label: 'Success' },
  FAILED: { bg: 'bg-red-100', text: 'text-red-800', label: 'Failed' },
  SKIPPED: { bg: 'bg-gray-100', text: 'text-gray-600', label: 'Skipped' },
  AWAITING_CONFIRMATION: { bg: 'bg-orange-100', text: 'text-orange-800', label: 'Awaiting Confirmation' },
}

export function StatusBadge({ status, size = 'md' }: { status: string; size?: 'sm' | 'md' }) {
  const config = statusConfig[status] ?? { bg: 'bg-gray-100', text: 'text-gray-800', label: status }
  return (
    <Badge className={`${config.bg} ${config.text} ${size === 'sm' ? 'text-xs' : 'text-sm'}`}>
      {config.label}
    </Badge>
  )
}
```

### Acceptance Criteria

- [ ] `ErrorState` renders error message with retry button
- [ ] `LoadingState` renders spinner/skeleton with optional message
- [ ] `EmptyState` renders icon + message + optional CTA button
- [ ] `ConfirmDialog` renders confirmation modal with confirm/cancel buttons
- [ ] `ConfirmDialog` supports destructive variant for delete actions
- [ ] `StatusBadge` renders correct colors and labels for all agent statuses (5) and all run statuses (6)
- [ ] All components exported from `src/components/shared/index.ts`
- [ ] All components use shadcn/ui theme tokens for consistent styling

### Verification Steps

1. Render `ErrorState` with a test message and retry callback → shows message + retry button
2. Render `LoadingState` with a message → shows spinner + message
3. Render `EmptyState` with icon, message, and action button → shows all three elements
4. Render `ConfirmDialog` with destructive variant → shows red confirm button
5. Render `StatusBadge` for each of the 5 agent statuses → correct colors and labels
6. Render `StatusBadge` for each of the 6 run statuses → correct colors and labels
7. Verify all components are importable from `src/components/shared/index.ts`

---

## TICKET P4-015: Backend API Endpoints for Web UI

**MVP Plan Ref:** Section 15 (cross-cutting backend support)  
**Depends on:** Phase 1–3 APIs must be complete

### Description

Define the backend API endpoints that the Web UI requires. Some of these may already exist from Phases 1–3; this ticket ensures all are documented with response schemas, pagination, filtering, and authentication requirements.

### Tasks

- [ ] `GET /api/dashboard` — aggregate dashboard data in one call
  - Response: agent counts by status, active runs count, recent 10 runs with summary, top 5 workloads by agent count
  - No pagination needed (aggregate data)
  - Authentication: admin-only
- [ ] `GET /api/agents` — list all agents with status and workload assignment
  - Query parameters: `page`, `pageSize`, `status` (filter), `search` (hostname/IP)
  - Response schema: `{ items: Agent[], total: number, page: number, pageSize: number }`
  - Each `Agent` includes: `agentId`, `hostname`, `ipAddress`, `status`, `assignedWorkload` (nullable), `lastHeartbeat`, `enrolledAt`
  - Authentication: admin-only
- [ ] `GET /api/agents/{id}` — agent detail with package states and run summary
  - Query parameters: `includeRuns=true`, `page`, `pageSize`
  - Response includes: agent metadata, packageStates[], runs (paginated)
  - Authentication: admin-only
- [ ] `GET /api/enrollment/tokens` — list enrollment tokens
  - Query parameters: `page`, `pageSize`, `status` (active/used/expired)
  - Response: `{ items: EnrollmentToken[], total, page, pageSize }`
  - Authentication: admin-only
- [ ] `GET /api/workloads` — list workload definitions
  - Query parameters: `page`, `pageSize`, `name` (filter), `version` (filter)
  - Response: `{ items: WorkloadSummary[], total, page, pageSize }`
  - Authentication: admin-only
- [ ] `GET /api/workloads/{id}/versions` — list versions of a workload
  - Response: `{ workloadId, workloadName, versions: WorkloadVersion[] }`
  - Authentication: admin-only
- [ ] `GET /api/runs` — list runs with optional agent/workload filter
  - Query parameters: `page`, `pageSize`, `agentId` (filter), `workloadId` (filter), `status` (filter)
  - Response: `{ items: RunSummary[], total, page, pageSize }`
  - Authentication: admin-only
- [ ] `GET /api/runs/{id}` — run detail with steps
  - Response: run metadata + steps array
  - Authentication: admin-only
- [ ] `GET /api/runs/{id}/steps` — get run detail with steps
  - Response: steps array with full detail
  - Authentication: admin-only
- [ ] `GET /api/artifacts` — list artifacts
  - Query parameters: `page`, `pageSize`, `search` (filename filter)
  - Response: `{ items: ArtifactSummary[], total, page, pageSize }`
  - Authentication: admin-only
- [ ] `POST /api/runs/{id}/confirm` — confirm an AWAITING_CONFIRMATION run
  - Request body: `{ confirmed: boolean, reason?: string }`
  - On confirm: transitions run to RUNNING
  - On reject: transitions run to FAILED
  - Authentication: admin-only
- [ ] `DELETE /api/runs/{runId}` — cancel a PENDING or AWAITING_CONFIRMATION run
  - Response: `{ success: true }` or `409 Conflict` if run is already in progress
  - Authentication: admin-only
- [ ] `DELETE /api/artifacts/{id}` — soft delete artifact
  - Marks artifact as deleted, doesn't remove file from disk
  - Returns `409 Conflict` if artifact is referenced by an active workload
  - Authentication: admin-only
- [ ] `PUT /api/artifacts/{id}` — replace artifact file
  - Accepts `multipart/form-data` with new binary and/or manifest
  - Re-hashes and updates metadata
  - Authentication: admin-only

### Common Response Patterns

All list endpoints should follow this pattern:

```typescript
interface PaginatedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}
```

All endpoints should:
- Return `401 Unauthorized` if not authenticated (admin auth)
- Return `403 Forbidden` if agent auth is used instead of admin auth
- Return `404 Not Found` for missing resources
- Return `409 Conflict` for optimistic locking failures (agent state changed, artifact in use)
- Use standard error response: `{ error: string, code?: string }`

### Acceptance Criteria

- [ ] All endpoints documented above are implemented
- [ ] All list endpoints support pagination (`page`, `pageSize`)
- [ ] All list endpoints return `{ items, total, page, pageSize }` response shape
- [ ] Filter/search parameters work as documented
- [ ] All endpoints require admin authentication (not agent auth)
- [ ] `409 Conflict` returned for optimistic locking failures
- [ ] `409 Conflict` returned when deleting artifacts referenced by active workloads
- [ ] Dashboard endpoint returns aggregated data without requiring N+1 queries

### Verification Steps

1. Call `GET /api/dashboard` → returns all dashboard data in one response
2. Call `GET /api/agents?page=1&pageSize=10` → returns paginated agents
3. Call `GET /api/agents/{id}?includeRuns=true` → returns agent with package states and runs
4. Call `GET /api/runs?agentId=a1&status=RUNNING` → returns filtered runs
5. Call `POST /api/runs/{id}/confirm` with `{ confirmed: true }` → run transitions to RUNNING
6. Call `POST /api/runs/{id}/confirm` with `{ confirmed: false, reason: "cancelled" }` → run transitions to FAILED
7. Call `DELETE /api/artifacts/{id}` on artifact used by workload → 409 Conflict
8. Call `GET /api/dashboard` with agent auth → 403 Forbidden

---

## Wizard State Management (G5)

The run wizard (P4-007 → P4-008 → P4-009) requires state that persists across all three steps. This section specifies the state management approach.

### State Shape

```typescript
interface WizardState {
  step: 1 | 2 | 3
  selectedAgentId: string | null
  selectedWorkloadId: string | null
  selectedWorkloadVersion: string | null
  selectedMode: 'INSTALL' | 'UPDATE' | 'UNINSTALL'
  confirmed: boolean
}
```

### State Management Approach

- Use React state management (`useState` or Zustand) to store wizard state across steps
- Wizard state is initialized when the user navigates to `/runs/new`
- Each step reads from and writes to the shared wizard state
- Step transitions update the `step` field in wizard state

### Step Responsibilities

- **Step 1 (P4-007):** Select agent, workload, version → stores `selectedAgentId`, `selectedWorkloadId`, `selectedWorkloadVersion`, `selectedMode` in wizard state
- **Step 2 (P4-008):** Review delta summary, confirm mode → stores `confirmed: true` in wizard state (after reviewing pre-check results)
- **Step 3 (P4-009):** Confirmation gate (UPDATE mode) or immediate dispatch → reads all wizard state to dispatch run

### Navigation

- **Next button:** advances `step` in wizard state, preserves all other fields
- **Back button:** decrements `step` in wizard state, preserves all other fields
- **Cancel button:** resets wizard state to initial values, navigates to `/runs`
- **Browser back/forward:** should work with wizard steps using URL-based step tracking (`/runs/new`, `/runs/new/2`, `/runs/new/3`)

### Implementation Notes

- Store wizard state in a shared context or Zustand store, not in individual step components
- Reset wizard state on mount if the user navigates directly to step 2 or 3 without prior state
- On successful run dispatch (Step 3), navigate to `/runs/{runId}` and reset wizard state
- On cancel or browser navigation away from wizard, reset state

---

## Status Display Consistency

All pages must use consistent status display. Use the `StatusBadge` component from P4-014 for all status indicators.

### Agent Statuses

| Status | Color | Icon | Label |
|---|---|---|---|
| UNREGISTERED | Gray | `Circle` (outline) | Unregistered |
| REGISTERED | Blue | `Circle` (filled) | Registered |
| WORKLOAD_ASSIGNED | Green | `CheckCircle` | Active |
| NEEDS_UPDATE | Orange/Amber | `AlertCircle` | Update Available |
| LOST | Red | `XCircle` | Lost |

### Run Statuses

| Status | Color | Icon | Label |
|---|---|---|---|
| PENDING | Blue | `Clock` | Pending |
| RUNNING | Blue (pulsing) | `Loader` (spinning) | Running |
| SUCCESS | Green | `CheckCircle` | Success |
| FAILED | Red | `XCircle` | Failed |
| SKIPPED | Gray | `MinusCircle` | Skipped |
| AWAITING_CONFIRMATION | Orange | `AlertCircle` | Awaiting Confirmation |

### Step Statuses

| Status | Color | Label |
|---|---|---|
| PENDING | Blue | Pending |
| RUNNING | Blue (pulsing) | Running |
| SUCCESS | Green | Success |
| FAILED | Red | Failed |
| SKIPPED | Gray | Skipped |
| PARTIAL_SUCCESS | Yellow | Partial Success |

### Delta Statuses

| Status | Color | Label |
|---|---|---|
| MATCHES | Green | Matches |
| MISSING | Red | Missing |
| VERSION_DRIFT | Yellow | Version Drift |
| AHEAD | Orange | Ahead |
| ORPHANED | Purple | Orphaned |

### Rules

- All status displays must use the `StatusBadge` component from P4-014
- No inline status color definitions — all colors come from the centralized `statusConfig` map
- Running statuses should use a pulsing animation (`animate-pulse`) to indicate activity
- Status labels should be human-readable, not enum values (e.g., "Active" not "WORKLOAD_ASSIGNED")