import type {
  AgentLocalSummary,
  ArtifactIngestResult,
  ArtifactManifest,
  ArtifactRecord,
  ArtifactUploadRequest,
  AuditEvent,
  CreateWorkloadDefinitionRequest,
  CreateWorkloadRevisionRequest,
  CreateWorkloadRunRequest,
  DashboardNodeRow,
  DashboardKpiSummary,
  DashboardSummary,
  EnrollmentToken,
  IngestStep,
  IssueEnrollmentTokenRequest,
  MiniLogLine,
  ManifestChannel,
  Node,
  NodeRunState,
  NodeWorkloadState,
  OrchestratorHomeData,
  OriginMetadata,
  WorkloadDefinition,
  WorkloadRevision,
  WorkloadRun,
  WorkloadRunStatus,
  WorkloadRunTimelineItem,
} from '../types'

const baseTime = new Date('2026-04-16T12:00:00.000Z').getTime()

const channelValues: ManifestChannel[] = ['stable', 'canary', 'test']

let artifactSeq = 2
let nodeSeq = 2
let tokenSeq = 1
let workloadSeq = 3
let revisionSeq = 5
let runSeq = 3

const nodes: Node[] = [
  {
    id: 'node-001',
    hostname: 'wj-plant-01',
    ipAddress: '10.30.2.41',
    status: 'online',
    description: 'Plant line A host',
    osVersion: 'Windows Server 2022',
    agentVersion: '0.1.0',
    firstConnectedAt: at(5),
    lastSeenAt: at(20),
  },
]

const artifacts: ArtifactRecord[] = [
  {
    id: 'artifact-001',
    fileName: 'EJ-Installer-1.12.0.msi',
    createdAt: at(0),
    detachedSignaturePresent: true,
    manifest: {
      name: 'EJ Installer',
      version: '1.12.0',
      channel: 'test',
      installType: 'msi',
      installArgs: '/quiet /norestart',
      digestSha256: 'd6d801d2f2d7de6f8d7482d3f648e21684af8684c0f5aabf1fcf5f0109bc0a22',
      signingIdentity: 'CN=Emerson Trusted Release',
      originMetadata: {
        sourceUrl: 'https://repo.local/installers/EJ-Installer-1.12.0.msi',
        publisher: 'Emerson',
        packageFamily: 'EJ-Installer',
        collectedAt: at(0),
        sourceConfidence: 'verified',
        publisherConfidence: 'verified',
      },
    },
  },
]

const revisionsByWorkload = new Map<string, WorkloadRevision[]>([
  [
    'workload-001',
    [
      {
        id: 'wrv-001',
        workloadId: 'workload-001',
        revision: '1.0.0',
        state: 'published',
        createdAt: at(10),
        publishedAt: at(12),
        packageSteps: [
          {
            packageId: 'pkg-runtime',
            packageName: 'Runtime Core',
            packageVersion: '2.7.0',
            packageIndex: 1,
            stepId: 'install-or-upgrade',
          },
          {
            packageId: 'pkg-agent',
            packageName: 'Node Agent',
            packageVersion: '1.5.2',
            packageIndex: 2,
            stepId: 'install-or-upgrade',
          },
        ],
      },
      {
        id: 'wrv-002',
        workloadId: 'workload-001',
        revision: '1.1.0',
        state: 'published',
        createdAt: at(20),
        publishedAt: at(23),
        packageSteps: [
          {
            packageId: 'pkg-runtime',
            packageName: 'Runtime Core',
            packageVersion: '2.8.0',
            packageIndex: 1,
            stepId: 'install-or-upgrade',
          },
          {
            packageId: 'pkg-agent',
            packageName: 'Node Agent',
            packageVersion: '1.5.3',
            packageIndex: 2,
            stepId: 'install-or-upgrade',
          },
          {
            packageId: 'pkg-policy',
            packageName: 'Policy Bundle',
            packageVersion: '3.0.0',
            packageIndex: 3,
            stepId: 'post-install-verify',
          },
        ],
      },
    ],
  ],
  [
    'workload-002',
    [
      {
        id: 'wrv-003',
        workloadId: 'workload-002',
        revision: '0.9.0',
        state: 'published',
        createdAt: at(14),
        publishedAt: at(15),
        packageSteps: [
          {
            packageId: 'pkg-telemetry',
            packageName: 'Telemetry Exporter',
            packageVersion: '0.9.0',
            packageIndex: 1,
            stepId: 'install-or-upgrade',
          },
          {
            packageId: 'pkg-dashboard',
            packageName: 'Local Dashboard',
            packageVersion: '0.9.0',
            packageIndex: 2,
            stepId: 'install-or-upgrade',
          },
        ],
      },
    ],
  ],
])

const workloads: WorkloadDefinition[] = [
  {
    id: 'workload-001',
    name: 'Factory Base Install',
    description: 'Baseline package set for production line nodes.',
    createdAt: at(8),
  },
  {
    id: 'workload-002',
    name: 'Observer Stack',
    description: 'Grafana/Loki/collector tooling for visibility.',
    createdAt: at(9),
  },
]

const runs: WorkloadRun[] = [
  {
    id: 'run-001',
    workloadId: 'workload-001',
    workloadName: 'Factory Base Install',
    workloadRevision: '1.1.0',
    mode: 'install',
    targetNodeIds: ['node-001'],
    targetNodeHostnames: ['wj-plant-01'],
    status: 'running',
    createdAt: at(30),
    startedAt: at(31),
    timeline: [
      timeline(1, 'pkg-runtime', 1, 'assign', 'completed', 'AssignRun', at(31), 'AssignRun dispatched to node-001', 'node-001'),
      timeline(2, 'pkg-runtime', 1, 'ack-claim', 'completed', 'AckClaim', at(32), 'Agent acknowledged run claim', 'node-001'),
      timeline(3, 'pkg-runtime', 1, 'lease-heartbeat', 'completed', 'LeaseHeartbeat', at(33), 'Lease heartbeat accepted (15s cadence)', 'node-001'),
      timeline(4, 'pkg-runtime', 1, 'install-or-upgrade', 'completed', 'StepStatus', at(34), 'Package install completed', 'node-001'),
      timeline(5, 'pkg-agent', 2, 'install-or-upgrade', 'running', 'StepStatus', at(35), 'Agent package in progress', 'node-001'),
    ],
  },
  {
    id: 'run-002',
    workloadId: 'workload-002',
    workloadName: 'Observer Stack',
    workloadRevision: '0.9.0',
    mode: 'update',
    targetNodeIds: ['node-001'],
    targetNodeHostnames: ['wj-plant-01'],
    status: 'failed',
    createdAt: at(24),
    startedAt: at(25),
    completedAt: at(27),
    diagnostics: {
      reasonCode: 'sequence_payload_conflict',
      lastMessage: 'StepStatus payload mismatch rejected by orchestrator',
      replacementHint: 'Replay from lastAcknowledgedSequence + 1',
    },
    timeline: [
      timeline(1, 'pkg-telemetry', 1, 'assign', 'completed', 'AssignRun', at(25), 'AssignRun delivered', 'node-001'),
      timeline(2, 'pkg-telemetry', 1, 'install-or-upgrade', 'failed', 'Fail', at(27), 'Step payload conflict', 'node-001'),
      timeline(3, 'pkg-telemetry', 1, 'lease-close', 'completed', 'LeaseClose', at(27), 'Lease closed with failure reason', 'node-001'),
    ],
  },
]

const nodeWorkloadStates: NodeWorkloadState[] = [
  {
    nodeId: 'node-001',
    workloadId: 'workload-001',
    workloadRevision: '1.0.0',
    runId: 'run-001',
    status: 'running',
    updatedAt: at(35),
  },
]

const enrollmentTokens: EnrollmentToken[] = []

const auditEvents: AuditEvent[] = [
  {
    id: 'audit-001',
    at: at(35),
    title: 'Workload run in progress',
    detail: 'run-001 is processing package index 2 on wj-plant-01.',
    type: 'workload-run',
  },
  {
    id: 'audit-002',
    at: at(27),
    title: 'Workload run failed',
    detail: 'run-002 failed with reason sequence_payload_conflict.',
    type: 'workload-run',
  },
]

const orchestratorKpis: DashboardKpiSummary = {
  nodesOnline: 24,
  nodesOffline: 2,
  artifactsStored: artifacts.length,
  workloadDefinitions: 6,
  runningWorkloads: 4,
  activeRuns24h: 14,
  failedRuns24h: 3,
  pendingApprovals: 2,
  controlPlaneLatencyP95Ms: 182,
}

const orchestratorNodes: DashboardNodeRow[] = [
  {
    nodeId: 'node-001',
    hostname: 'wj-plant-01',
    health: 'online',
    assignedWorkload: 'Factory Base Install',
    workloadRevision: '1.1.0',
    runState: 'update',
    lastCheckInAge: '18s',
    riskLevel: 'low',
    revisionUpdateAvailable: true,
    packageUpdatesAvailable: true,
    packageUpdateCount: 2,
  },
  {
    nodeId: 'node-002',
    hostname: 'wj-plant-02',
    health: 'warning',
    assignedWorkload: 'Observer Stack',
    workloadRevision: '0.9.0',
    runState: 'pending-approval',
    lastCheckInAge: '42s',
    riskLevel: 'med',
    revisionUpdateAvailable: true,
    packageUpdatesAvailable: false,
    reasonCode: 'approval_window_required',
  },
  {
    nodeId: 'node-003',
    hostname: 'wj-plant-03',
    health: 'offline',
    assignedWorkload: 'Factory Base Install',
    workloadRevision: '1.0.0',
    runState: 'failed',
    lastCheckInAge: '6m',
    riskLevel: 'high',
    revisionUpdateAvailable: false,
    packageUpdatesAvailable: true,
    packageUpdateCount: 1,
    reasonCode: 'heartbeat_timeout',
  },
]

const orchestratorLogsByNodeId: Record<string, MiniLogLine[]> = {
  'node-001': [
    { id: 'log-001', at: at(34), message: 'Workload run run-001 entered package index 2.', level: 'info' },
    { id: 'log-002', at: at(35), message: 'StepStatus accepted for install-or-upgrade.', level: 'info' },
  ],
  'node-002': [
    { id: 'log-003', at: at(33), message: 'Run paused pending explicit approval window.', level: 'warn' },
    { id: 'log-004', at: at(34), message: 'Awaiting operator confirmation on node-local console.', level: 'info' },
  ],
  'node-003': [
    { id: 'log-005', at: at(29), message: 'Lease heartbeat missed beyond threshold.', level: 'error' },
    { id: 'log-006', at: at(30), message: 'Run marked failed with heartbeat_timeout.', level: 'error' },
  ],
}

const orchestratorHome: OrchestratorHomeData = {
  kpis: orchestratorKpis,
  nodes: orchestratorNodes,
  events: [
    {
      id: 'evt-001',
      severity: 'high',
      title: 'Pending approval requires operator action',
      detail: 'node-002 workload update is gated by approval_window_required.',
      ageLabel: '1m ago',
      nodeId: 'node-002',
      runId: 'run-004',
    },
    {
      id: 'evt-002',
      severity: 'critical',
      title: 'Node heartbeat timeout',
      detail: 'node-003 missed lease heartbeat and transitioned to failed.',
      ageLabel: '3m ago',
      nodeId: 'node-003',
      runId: 'run-005',
    },
    {
      id: 'evt-003',
      severity: 'info',
      title: 'Workload package sequencing healthy',
      detail: 'node-001 continues update progression with valid step ordering.',
      ageLabel: '10s ago',
      nodeId: 'node-001',
      runId: 'run-001',
    },
  ],
  selectedNodeId: 'node-001',
  logsByNodeId: orchestratorLogsByNodeId,
}

const agentLocalLogs: MiniLogLine[] = [
  { id: 'agent-log-001', at: at(34), message: 'Pre-check cache hydrated for target revision 1.1.0.', level: 'info' },
  { id: 'agent-log-002', at: at(35), message: 'Awaiting guided update confirmation from local operator.', level: 'warn' },
]

const agentLocalGeneratedAt = at(36)

let agentLocalSummary: AgentLocalSummary = {
  nodeId: 'node-001',
  hostname: 'wj-plant-01',
  health: 'online',
  runState: 'pending-approval',
  currentWorkload: 'Factory Base Install',
  targetRevision: '1.1.0',
  installedRevision: '1.0.0',
  pendingApproval: true,
  riskLevel: 'low',
}

function hydrateWorkload(definition: WorkloadDefinition): WorkloadDefinition {
  const revisions = revisionsByWorkload.get(definition.id) ?? []
  const published = revisions.filter(item => item.state === 'published')
  const latestRevision = published.length > 0 ? published[published.length - 1] : revisions[revisions.length - 1]
  return {
    ...definition,
    latestRevision,
  }
}

function writeWorkloadAudit(title: string, detail: string): void {
  auditEvents.unshift({
    id: `audit-${String(auditEvents.length + 1).padStart(3, '0')}`,
    at: new Date().toISOString(),
    title,
    detail,
    type: 'workload-run',
  })
}

export function validateManifestChannel(channel: string): channel is ManifestChannel {
  return channelValues.includes(channel as ManifestChannel)
}

export function getSupportedChannels(): ManifestChannel[] {
  return [...channelValues]
}

export function suggestManifestFromFile(fileName: string, fileSizeBytes: number): ArtifactManifest {
  const ext = fileName.toLowerCase().split('.').pop()
  const installType = ext === 'exe' ? 'exe' : ext === 'zip' ? 'zip' : 'msi'
  const versionMatch = fileName.match(/(\d+\.\d+\.\d+)/)
  const version = versionMatch?.[1] ?? '1.0.0'
  const name = fileName.replace(/\.[^.]+$/, '').replace(/[-_]?\d+\.\d+\.\d+$/, '') || 'Installer Package'
  const source = `https://repo.local/installers/${fileName}`
  const digest = `${fileName}:${fileSizeBytes}`

  return {
    name,
    version,
    channel: 'stable',
    installType,
    installArgs: installType === 'msi' ? '/quiet /norestart' : '/silent',
    digestSha256: normalizeHash(digest),
    signingIdentity: 'CN=Vendor Signature',
    originMetadata: buildOriginMetadata(source, 'Unknown Vendor', name, 'derived'),
  }
}

export async function uploadArtifact(request: ArtifactUploadRequest): Promise<ArtifactIngestResult> {
  if (!request.fileName) {
    throw new Error('file part is required for multipart upload')
  }

  if (!request.manifest || !request.manifest.name || !request.manifest.version) {
    throw new Error('manifest JSON part is required for multipart upload')
  }

  if (!validateManifestChannel(request.manifest.channel)) {
    throw new Error('manifest.channel must be one of stable, canary, test')
  }

  const steps: IngestStep[] = [
    { id: 'upload', label: 'Receive multipart request (file + manifest + optional detachedSignature)', status: 'completed' },
    { id: 'analyze', label: 'Analyze installer media and prefill metadata', status: 'completed' },
    { id: 'verify', label: 'Verify digest, signatures, and origin metadata', status: 'completed' },
    { id: 'store', label: 'Store immutable artifact and write audit record', status: 'completed' },
  ]

  const artifact: ArtifactRecord = {
    id: `artifact-${String(artifactSeq++).padStart(3, '0')}`,
    fileName: request.fileName,
    createdAt: new Date().toISOString(),
    detachedSignaturePresent: Boolean(request.detachedSignature),
    manifest: {
      ...request.manifest,
      digestSha256: request.manifest.digestSha256 || normalizeHash(`${request.fileName}:${request.fileSizeBytes}`),
    },
  }

  artifacts.unshift(artifact)
  writeWorkloadAudit('Artifact ingested', `${artifact.id} accepted and available for new WorkloadRevision drafts.`)
  return { artifact, steps }
}

export async function listArtifacts(): Promise<ArtifactRecord[]> {
  return [...artifacts]
}

export async function issueEnrollmentToken(request: IssueEnrollmentTokenRequest): Promise<EnrollmentToken> {
  const token: EnrollmentToken = {
    token: `enroll-${String(tokenSeq++).padStart(4, '0')}`,
    issuedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + request.ttlMinutes * 60_000).toISOString(),
    requestedBy: request.requestedBy,
    orchestratorUrl: request.orchestratorUrl,
    singleUse: true,
    used: false,
  }

  enrollmentTokens.unshift(token)
  writeWorkloadAudit('Enrollment token issued', `POST /api/nodes/enroll issued ${token.token}.`)
  return token
}

export async function listEnrollmentTokens(): Promise<EnrollmentToken[]> {
  return [...enrollmentTokens]
}

export async function consumeEnrollmentToken(tokenValue: string): Promise<Node> {
  const token = enrollmentTokens.find(item => item.token === tokenValue)
  if (!token) {
    throw new Error('Enrollment token not found')
  }
  if (token.used) {
    throw new Error('Enrollment token already consumed')
  }

  token.used = true
  const now = new Date().toISOString()
  const currentNodeSeq = nodeSeq++
  const node: Node = {
    id: `node-${String(currentNodeSeq).padStart(3, '0')}`,
    hostname: `wj-node-${String(currentNodeSeq).padStart(2, '0')}`,
    ipAddress: `10.30.2.${40 + currentNodeSeq}`,
    status: 'online',
    description: 'Auto-registered from bootstrap token',
    osVersion: 'Windows Server 2022',
    agentVersion: '0.1.0',
    firstConnectedAt: now,
    lastSeenAt: now,
  }
  nodes.unshift(node)
  writeWorkloadAudit('Node auto-registered', `${node.hostname} connected using token bootstrap.`)
  return node
}

export async function listNodes(): Promise<Node[]> {
  return [...nodes]
}

export async function listWorkloads(): Promise<WorkloadDefinition[]> {
  return workloads.map(hydrateWorkload)
}

export async function listWorkloadRevisions(workloadId: string): Promise<WorkloadRevision[]> {
  return [...(revisionsByWorkload.get(workloadId) ?? [])]
}

export async function createWorkloadDefinitionDraft(
  request: CreateWorkloadDefinitionRequest,
): Promise<WorkloadDefinition> {
  const definition: WorkloadDefinition = {
    id: `workload-${String(workloadSeq++).padStart(3, '0')}`,
    name: request.name,
    description: request.description,
    createdAt: new Date().toISOString(),
  }
  workloads.unshift(definition)
  revisionsByWorkload.set(definition.id, [])
  writeWorkloadAudit('WorkloadDefinition draft created', `${definition.id} created in draft state.`)
  return hydrateWorkload(definition)
}

export async function createWorkloadRevision(
  request: CreateWorkloadRevisionRequest,
): Promise<WorkloadRevision> {
  const workload = workloads.find(item => item.id === request.workloadId)
  if (!workload) {
    throw new Error('WorkloadDefinition not found')
  }

  const count = request.packageSteps.length
  if (count < 2 || count > 3) {
    throw new Error('PoC Phase 1 requires 2-3 package steps per WorkloadRevision')
  }

  const revision: WorkloadRevision = {
    id: `wrv-${String(revisionSeq++).padStart(3, '0')}`,
    workloadId: request.workloadId,
    revision: request.revision,
    state: 'draft',
    createdAt: new Date().toISOString(),
    packageSteps: request.packageSteps
      .map((step, index) => ({ ...step, packageIndex: index + 1 }))
      .sort((a, b) => a.packageIndex - b.packageIndex),
  }

  const existing = revisionsByWorkload.get(request.workloadId) ?? []
  existing.push(revision)
  revisionsByWorkload.set(request.workloadId, existing)
  writeWorkloadAudit('WorkloadRevision draft created', `${revision.id} prepared for ${request.workloadId}.`)
  return revision
}

export async function publishWorkloadRevision(workloadId: string, revisionId: string): Promise<WorkloadRevision> {
  const revisions = revisionsByWorkload.get(workloadId) ?? []
  const target = revisions.find(item => item.id === revisionId)
  if (!target) {
    throw new Error('WorkloadRevision not found')
  }
  target.state = 'published'
  target.publishedAt = new Date().toISOString()
  writeWorkloadAudit('WorkloadRevision published', `${target.id} is immutable and publish-locked.`)
  return target
}

export async function listWorkloadRuns(status: WorkloadRunStatus | 'all' = 'all'): Promise<WorkloadRun[]> {
  if (status === 'all') {
    return [...runs]
  }
  return runs.filter(item => item.status === status)
}

export async function createWorkloadRun(request: CreateWorkloadRunRequest): Promise<WorkloadRun> {
  const workload = workloads.find(item => item.id === request.workloadId)
  if (!workload) {
    throw new Error('WorkloadDefinition not found')
  }
  const revision = (revisionsByWorkload.get(request.workloadId) ?? []).find(item => item.revision === request.workloadRevision)
  if (!revision) {
    throw new Error('WorkloadRevision not found')
  }
  if (revision.state !== 'published') {
    throw new Error('WorkloadRevision must be published before run creation')
  }

  const targetNodes = nodes.filter(node => request.targetNodeIds.includes(node.id))
  if (targetNodes.length === 0) {
    throw new Error('At least one target node is required')
  }
  if (targetNodes.length !== 1) {
    throw new Error('Phase 1 supports exactly one target node per run')
  }

  const now = new Date().toISOString()
  const run: WorkloadRun = {
    id: `run-${String(runSeq++).padStart(3, '0')}`,
    workloadId: workload.id,
    workloadName: workload.name,
    workloadRevision: revision.revision,
    mode: request.mode,
    targetNodeIds: targetNodes.map(node => node.id),
    targetNodeHostnames: targetNodes.map(node => node.hostname),
    status: 'assigned',
    createdAt: now,
    startedAt: now,
    timeline: [
      timeline(1, revision.packageSteps[0].packageId, 1, 'assign', 'completed', 'AssignRun', now, 'AssignRun emitted from orchestrator runtime hub', targetNodes[0].id),
    ],
  }
  runs.unshift(run)

  nodeWorkloadStates.unshift({
    nodeId: targetNodes[0].id,
    workloadId: workload.id,
    workloadRevision: revision.revision,
    runId: run.id,
    status: 'assigned',
    updatedAt: now,
  })

  writeWorkloadAudit('WorkloadRun created', `${run.id} submitted via POST /api/workload-runs (${run.mode}).`)
  return run
}

export async function getWorkloadRunSteps(runId: string): Promise<WorkloadRunTimelineItem[]> {
  const run = runs.find(item => item.id === runId)
  if (!run) {
    throw new Error('WorkloadRun not found')
  }
  return [...run.timeline].sort((a, b) => a.sequence - b.sequence)
}

export async function cancelWorkloadRun(runId: string): Promise<WorkloadRun> {
  const run = runs.find(item => item.id === runId)
  if (!run) {
    throw new Error('WorkloadRun not found')
  }
  if (run.status === 'completed' || run.status === 'failed' || run.status === 'cancelled') {
    return run
  }

  run.status = 'cancelled'
  run.completedAt = new Date().toISOString()
  run.diagnostics = {
    reasonCode: 'operator_cancelled',
    lastMessage: 'Cancel accepted at safe interruption boundary.',
  }
  run.timeline.push(
    timeline(
      run.timeline.length + 1,
      run.timeline[run.timeline.length - 1]?.packageId ?? 'pkg-unknown',
      run.timeline[run.timeline.length - 1]?.packageIndex ?? 1,
      'cancel',
      'cancelled',
      'Fail',
      run.completedAt,
      'Cancellation completed with explicit reason code.',
      run.targetNodeIds[0],
    ),
  )
  writeWorkloadAudit('WorkloadRun cancelled', `${run.id} transitioned to cancelled.`)
  return run
}

export async function advanceWorkloadRun(runId: string): Promise<WorkloadRun> {
  const run = runs.find(item => item.id === runId)
  if (!run) {
    throw new Error('WorkloadRun not found')
  }
  if (run.status === 'completed' || run.status === 'failed' || run.status === 'cancelled') {
    return run
  }

  const revision = (revisionsByWorkload.get(run.workloadId) ?? []).find(item => item.revision === run.workloadRevision)
  if (!revision) {
    throw new Error('WorkloadRevision not found for run')
  }

  const nextSequence = run.timeline.length + 1
  const last = run.timeline[run.timeline.length - 1]
  const maxPackageIndex = revision.packageSteps.length
  const nextStepId = last?.stepId === 'assign'
    ? 'ack-claim'
    : last?.stepId === 'ack-claim'
    ? 'lease-heartbeat'
    : last?.stepId === 'lease-heartbeat'
    ? 'install-or-upgrade'
    : last?.stepId === 'install-or-upgrade' && (last?.packageIndex ?? 1) < maxPackageIndex
    ? 'install-or-upgrade'
    : 'complete'

  if (nextStepId === 'complete') {
    run.status = 'completed'
    run.completedAt = new Date().toISOString()
    run.timeline.push(
      timeline(nextSequence, last.packageId, last.packageIndex, 'complete', 'completed', 'Complete', run.completedAt, 'Run completed and lease closed.', run.targetNodeIds[0]),
    )
    run.timeline.push(
      timeline(nextSequence + 1, last.packageId, last.packageIndex, 'lease-close', 'completed', 'LeaseClose', run.completedAt, 'LeaseClose acknowledged by orchestrator.', run.targetNodeIds[0]),
    )
    writeWorkloadAudit('WorkloadRun completed', `${run.id} reached terminal completed state.`)
    return run
  }

  const nextPackageIndex = last?.stepId === 'install-or-upgrade' ? (last.packageIndex ?? 1) + 1 : last?.packageIndex ?? 1
  const nextPackageId = revision.packageSteps[nextPackageIndex - 1]?.packageId ?? last?.packageId ?? 'pkg-unknown'
  const messageType =
    nextStepId === 'ack-claim'
      ? 'AckClaim'
      : nextStepId === 'lease-heartbeat'
      ? 'LeaseHeartbeat'
      : 'StepStatus'

  run.status = 'running'
  run.timeline.push(
    timeline(
      nextSequence,
      nextPackageId,
      nextPackageIndex,
      nextStepId,
      nextStepId === 'install-or-upgrade' ? 'running' : 'completed',
      messageType,
      new Date().toISOString(),
      nextStepId === 'install-or-upgrade'
        ? `Package index ${nextPackageIndex} executing`
        : `${messageType} accepted`,
      run.targetNodeIds[0],
    ),
  )
  return run
}

export async function listNodeWorkloadStates(): Promise<NodeWorkloadState[]> {
  return [...nodeWorkloadStates]
}

export async function getDashboardSummary(): Promise<DashboardSummary> {
  return {
    totalNodes: nodes.length,
    connectedNodes: nodes.filter(node => node.status === 'online').length,
    activeWorkloadRuns: runs.filter(run => run.status === 'running' || run.status === 'assigned' || run.status === 'pending').length,
    failedWorkloadRuns: runs.filter(run => run.status === 'failed').length,
    workloadDefinitions: workloads.length,
  }
}

export async function listAuditEvents(limit = 8): Promise<AuditEvent[]> {
  return [...auditEvents].slice(0, limit)
}

export async function getOrchestratorHomeData(): Promise<OrchestratorHomeData> {
  return structuredClone(orchestratorHome)
}

export async function getAgentLocalSummary(): Promise<AgentLocalSummary> {
  return {
    ...agentLocalSummary,
  }
}

export async function runAgentPrecheck(): Promise<{ passed: boolean; detail: string }> {
  return {
    passed: true,
    detail: 'Disk, signature chain, and rollback prerequisites validated.',
  }
}

export async function startAgentGuidedUpdate(): Promise<{ accepted: boolean; status: NodeRunState }> {
  agentLocalSummary = {
    ...agentLocalSummary,
    runState: 'update',
  }

  return { accepted: true, status: 'update' }
}

export async function exportAgentDiagnostics(): Promise<{ fileName: string; generatedAt: string }> {
  return {
    fileName: `diagnostics-${agentLocalSummary.nodeId}.zip`,
    generatedAt: agentLocalGeneratedAt,
  }
}

export async function listAgentLocalLogs(): Promise<MiniLogLine[]> {
  return [...agentLocalLogs]
}

function at(minuteOffset: number): string {
  return new Date(baseTime + minuteOffset * 60_000).toISOString()
}

function normalizeHash(input: string): string {
  const source = input.replace(/[^a-zA-Z0-9]/g, '').toLowerCase().padEnd(64, 'a')
  return source.slice(0, 64)
}

function buildOriginMetadata(
  sourceUrl: string,
  publisher: string,
  packageFamily: string,
  confidence: OriginMetadata['sourceConfidence'],
): OriginMetadata {
  return {
    sourceUrl,
    publisher,
    packageFamily,
    collectedAt: new Date().toISOString(),
    sourceConfidence: confidence,
    publisherConfidence: confidence,
  }
}

function timeline(
  sequence: number,
  packageId: string,
  packageIndex: number,
  stepId: string,
  status: WorkloadRunTimelineItem['status'],
  messageType: WorkloadRunTimelineItem['messageType'],
  atValue: string,
  detail: string,
  nodeId: string,
): WorkloadRunTimelineItem {
  return {
    sequence,
    packageId,
    packageIndex,
    stepId,
    status,
    messageType,
    at: atValue,
    detail,
    nodeId,
  }
}
