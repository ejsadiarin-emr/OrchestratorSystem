import type {
  AgentLocalSummary,
  ArtifactIngestResult,
  ArtifactManifest,
  ArtifactRecord,
  ArtifactUploadRequest,
  AuditEvent,
  BulkIngestResult,
  BulkWorkloadImportResult,
  CreateWorkloadDefinitionRequest,
  CreateWorkloadRevisionRequest,
  CreateWorkloadRunRequest,
  DashboardNodeRow,
  DashboardKpiSummary,
  DashboardSummary,
  EnrollmentToken,
  ImportantEvent,
  IngestStep,
  IssueEnrollmentTokenRequest,
  MiniLogLine,
  ManifestChannel,
  Node,
  NodeRunState,
  NodeWorkloadState,
  OrchestratorHomeData,
  UploadProgressCallback,
  UploadSession,
  ValidationFieldError,
  WorkloadDefinition,
  WorkloadRevision,
  WorkloadRun,
  WorkloadRunStatus,
  WorkloadRunTimelineItem,
} from '../types'

const baseTime = new Date('2026-04-16T12:00:00.000Z').getTime()

const channelValues: ManifestChannel[] = ['stable', 'canary', 'test']

// Mock data sequences kept for functions still using local mock state

const nodes: Node[] = [
  {
    id: 'node-001',
    hostname: 'wj-plant-01',
    displayName: 'Plant Line A',
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
      packageId: 'EJ-Installer',
      version: '1.12.0',
      channel: 'test',
      artifactType: 'msi',
      verificationResult: 'verified',
      installAdapter: {
        type: 'msi',
        command: 'msiexec',
        arguments: '/quiet /norestart',
        expectedExitCodes: [0],
        timeoutSeconds: 300,
      },
      detection: {
        type: 'registry',
        path: 'HKLM\\Software\\EJ',
        expectedVersion: '1.12.0',
      },
      policyTags: {
        retryabilityClass: 'retryable',
        idempotencyMode: 'enforced',
        riskLevel: 'low',
        approvalRequired: false,
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

// Mock data kept for functions still using local state (dashboard, agent-local, etc.)

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

export function suggestManifestFromFile(fileName: string, _fileSizeBytes: number): ArtifactManifest {
  const ext = fileName.toLowerCase().split('.').pop()
  const artifactType = ext === 'exe' ? 'exe' : ext === 'zip' ? 'zip' : 'msi'
  const versionMatch = fileName.match(/(\d+\.\d+\.\d+)/)
  const version = versionMatch?.[1] ?? '1.0.0'
  const packageId = fileName.replace(/\.[^.]+$/, '').replace(/[-_]?\d+\.\d+\.\d+$/, '') || 'Installer Package'

  return {
    packageId,
    version,
    channel: 'stable',
    artifactType,
    verificationResult: 'derived',
    installAdapter: {
      type: artifactType,
      command: artifactType === 'msi' ? 'msiexec' : artifactType === 'exe' ? fileName : 'powershell',
      arguments: artifactType === 'msi' ? '/quiet /norestart' : artifactType === 'exe' ? '/silent' : '',
      expectedExitCodes: [0],
      timeoutSeconds: 300,
    },
    detection: {
      type: 'registry',
      path: `HKLM\\Software\\${packageId}`,
      expectedVersion: version,
    },
    policyTags: {
      retryabilityClass: 'retryable',
      idempotencyMode: 'enforced',
      riskLevel: 'low',
      approvalRequired: false,
    },
  }
}

export async function uploadArtifact(request: ArtifactUploadRequest): Promise<ArtifactIngestResult> {
  if (!request.file) {
    throw new Error('file is required for multipart upload')
  }

  if (!request.manifest || !request.manifest.packageId || !request.manifest.version) {
    throw new Error('manifest JSON part is required for multipart upload')
  }

  if (request.manifest.channel && !validateManifestChannel(request.manifest.channel)) {
    throw new Error('manifest.channel must be one of stable, canary, test')
  }

  const formData = new FormData()
  formData.append('file', request.file)
  formData.append('manifest', JSON.stringify(request.manifest))
  if (request.detachedSignature) {
    formData.append('detachedSignature', request.detachedSignature)
  }

  const response = await fetch('/api/artifacts', {
    method: 'POST',
    body: formData,
  })

  if (!response.ok) {
    let message = `Upload failed with status ${response.status}`
    try {
      const body = await response.json() as { message?: string; errors?: ValidationFieldError[] }
      if (body.errors && body.errors.length > 0) {
        message = body.errors.map((e: ValidationFieldError) => `${e.field}: ${e.error}`).join('; ')
      } else if (body.message) {
        message = body.message
      }
    } catch {
      // use default message
    }
    throw new Error(message)
  }

  const data = await response.json() as { packageEntityId?: string; resolvedManifest: { packageId: string; version: string; channel: string; artifactType: string; installAdapter: Record<string, unknown>; detection: Record<string, unknown>; policyTags: Record<string, unknown>; originMetadata: Record<string, unknown> } }

  const artifact: ArtifactRecord = {
    id: `${data.resolvedManifest.packageId}-${data.resolvedManifest.version}`,
    packageEntityId: data.packageEntityId,
    fileName: request.file.name,
    createdAt: new Date().toISOString(),
    detachedSignaturePresent: Boolean(request.detachedSignature),
    manifest: {
      packageId: data.resolvedManifest.packageId,
      version: data.resolvedManifest.version,
      channel: data.resolvedManifest.channel,
      artifactType: data.resolvedManifest.artifactType,
      verificationResult: data.resolvedManifest.originMetadata?.verificationResult as string | undefined,
      installAdapter: data.resolvedManifest.installAdapter as ArtifactManifest['installAdapter'],
      detection: data.resolvedManifest.detection as ArtifactManifest['detection'],
      policyTags: data.resolvedManifest.policyTags as ArtifactManifest['policyTags'],
    },
  }

  const steps: IngestStep[] = [
    { id: 'upload', label: 'Receive multipart request (file + manifest + optional detachedSignature)', status: 'completed' },
    { id: 'analyze', label: 'Analyze installer media and prefill metadata', status: 'completed' },
    { id: 'verify', label: 'Verify digest, signatures, and origin metadata', status: 'completed' },
    { id: 'store', label: 'Store immutable artifact and write audit record', status: 'completed' },
  ]

  artifacts.unshift(artifact)
  writeWorkloadAudit('Artifact ingested', `${artifact.id} accepted and available for new WorkloadRevision drafts.`)
  return { artifact, steps }
}

export async function createUploadSession(manifest?: ArtifactManifest): Promise<UploadSession> {
  const response = await fetch('/api/artifacts/upload-session', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: manifest ? JSON.stringify({ manifest }) : undefined,
  })

  if (!response.ok) {
    throw new Error(`Failed to create upload session: ${response.status}`)
  }

  return response.json() as Promise<UploadSession>
}

export async function uploadChunk(sessionId: string, index: number, totalChunks: number, chunk: Blob): Promise<void> {
  const formData = new FormData()
  formData.append('sessionId', sessionId)
  formData.append('chunkIndex', String(index))
  formData.append('totalChunks', String(totalChunks))
  formData.append('chunk', chunk)

  const response = await fetch('/api/artifacts/upload-chunk', {
    method: 'POST',
    body: formData,
  })

  if (!response.ok) {
    throw new Error(`Failed to upload chunk ${index}: ${response.status}`)
  }
}

export async function completeUploadSession(sessionId: string): Promise<ArtifactIngestResult> {
  const response = await fetch('/api/artifacts/upload-complete', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId }),
  })

  if (!response.ok) {
    throw new Error(`Failed to complete upload session: ${response.status}`)
  }

  return response.json() as Promise<ArtifactIngestResult>
}

export async function uploadArtifactWithProgress(
  request: ArtifactUploadRequest,
  onProgress?: UploadProgressCallback,
): Promise<ArtifactIngestResult> {
  if (!request.file) {
    throw new Error('file is required for multipart upload')
  }

  if (!request.manifest || !request.manifest.packageId || !request.manifest.version) {
    throw new Error('manifest JSON part is required for multipart upload')
  }

  if (request.manifest.channel && !validateManifestChannel(request.manifest.channel)) {
    throw new Error('manifest.channel must be one of stable, canary, test')
  }

  const formData = new FormData()
  formData.append('file', request.file)
  formData.append('manifest', JSON.stringify(request.manifest))
  if (request.detachedSignature) {
    formData.append('detachedSignature', request.detachedSignature)
  }

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()

    xhr.upload.addEventListener('progress', event => {
      if (event.lengthComputable && onProgress) {
        onProgress(event.loaded, event.total)
      }
    })

    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          const data = JSON.parse(xhr.responseText) as { packageEntityId?: string; resolvedManifest: { packageId: string; version: string; channel: string; artifactType: string; installAdapter: Record<string, unknown>; detection: Record<string, unknown>; policyTags: Record<string, unknown>; originMetadata: Record<string, unknown> } }

          const artifact: ArtifactRecord = {
            id: `${data.resolvedManifest.packageId}-${data.resolvedManifest.version}`,
            packageEntityId: data.packageEntityId,
            fileName: request.file.name,
            createdAt: new Date().toISOString(),
            detachedSignaturePresent: Boolean(request.detachedSignature),
            manifest: {
              packageId: data.resolvedManifest.packageId,
              version: data.resolvedManifest.version,
              channel: data.resolvedManifest.channel,
              artifactType: data.resolvedManifest.artifactType,
              verificationResult: data.resolvedManifest.originMetadata?.verificationResult as string | undefined,
              installAdapter: data.resolvedManifest.installAdapter as ArtifactManifest['installAdapter'],
              detection: data.resolvedManifest.detection as ArtifactManifest['detection'],
              policyTags: data.resolvedManifest.policyTags as ArtifactManifest['policyTags'],
            },
          }

          const steps: IngestStep[] = [
            { id: 'upload', label: 'Receive multipart request (file + manifest + optional detachedSignature)', status: 'completed' },
            { id: 'analyze', label: 'Analyze installer media and prefill metadata', status: 'completed' },
            { id: 'verify', label: 'Verify digest, signatures, and origin metadata', status: 'completed' },
            { id: 'store', label: 'Store immutable artifact and write audit record', status: 'completed' },
          ]

          artifacts.unshift(artifact)
          writeWorkloadAudit('Artifact ingested', `${artifact.id} accepted and available for new WorkloadRevision drafts.`)
          resolve({ artifact, steps })
        } catch (err) {
          reject(new Error('Failed to parse upload response'))
        }
      } else {
        let message = `Upload failed with status ${xhr.status}`
        try {
          const body = JSON.parse(xhr.responseText) as { message?: string; errors?: ValidationFieldError[] }
          if (body.errors && body.errors.length > 0) {
            message = body.errors.map((e: ValidationFieldError) => `${e.field}: ${e.error}`).join('; ')
          } else if (body.message) {
            message = body.message
          }
        } catch {
          // use default message
        }
        reject(new Error(message))
      }
    })

    xhr.addEventListener('error', () => {
      reject(new Error('Network error during upload'))
    })

    xhr.open('POST', '/api/artifacts')
    xhr.send(formData)
  })
}

export async function uploadBulkArtifacts(
  file: File,
  onProgress?: UploadProgressCallback,
): Promise<BulkIngestResult> {
  const formData = new FormData()
  formData.append('file', file)

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()

    xhr.upload.addEventListener('progress', event => {
      if (event.lengthComputable && onProgress) {
        onProgress(event.loaded, event.total)
      }
    })

    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          const data = JSON.parse(xhr.responseText) as BulkIngestResult
          resolve(data)
        } catch {
          reject(new Error('Failed to parse bulk upload response'))
        }
      } else {
        let message = `Bulk upload failed with status ${xhr.status}`
        try {
          const body = JSON.parse(xhr.responseText) as { message?: string }
          if (body.message) message = body.message
        } catch {
          // use default message
        }
        reject(new Error(message))
      }
    })

    xhr.addEventListener('error', () => {
      reject(new Error('Network error during bulk upload'))
    })

    xhr.open('POST', '/api/artifacts/bulk')
    xhr.send(formData)
  })
}

export async function listArtifacts(): Promise<ArtifactRecord[]> {
  const response = await fetch('/api/artifacts')
  if (!response.ok) {
    throw new Error(`Failed to load artifacts: ${response.status}`)
  }
  const data = await response.json() as Array<{
    id: string
    packageEntityId?: string
    packageId: string
    version: string
    fileName: string
    channel?: string
    artifactType?: string
    verificationResult?: string
    sizeBytes?: number
    digest?: string
    createdAt: string
    installAdapterCommand?: string
    detectionType?: string
    detectionPath?: string
    riskLevel?: string
  }>

  return data.map(item => ({
    id: item.id,
    packageEntityId: item.packageEntityId,
    fileName: item.fileName,
    createdAt: item.createdAt,
    sizeBytes: item.sizeBytes,
    digest: item.digest,
    detachedSignaturePresent: false,
    manifest: {
      packageId: item.packageId,
      version: item.version,
      channel: item.channel,
      artifactType: item.artifactType,
      verificationResult: item.verificationResult,
      installAdapter: item.installAdapterCommand
        ? {
            type: item.artifactType ?? 'unknown',
            command: item.installAdapterCommand,
            arguments: '',
            expectedExitCodes: [0],
            timeoutSeconds: 300,
          }
        : undefined,
      detection: item.detectionType
        ? {
            type: item.detectionType,
            path: item.detectionPath ?? '',
            expectedVersion: item.version,
          }
        : undefined,
      policyTags: item.riskLevel
        ? {
            retryabilityClass: 'retryable',
            idempotencyMode: 'enforced',
            riskLevel: item.riskLevel,
            approvalRequired: false,
          }
        : undefined,
    },
  }))
}

export async function deleteArtifact(packageId: string, version: string): Promise<void> {
  const response = await fetch(`/api/artifacts/${encodeURIComponent(packageId)}/${encodeURIComponent(version)}`, {
    method: 'DELETE',
  })
  if (!response.ok && response.status !== 404) {
    throw new Error(`Failed to delete artifact: ${response.status}`)
  }
}

export async function issueEnrollmentToken(request: IssueEnrollmentTokenRequest): Promise<EnrollmentToken> {
  const response = await fetch('/api/nodes/enroll', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    let message = `Failed to issue token: ${response.status}`
    try {
      const body = await response.json() as { message?: string }
      if (body.message) message = body.message
    } catch {
      // use default message
    }
    throw new Error(message)
  }

  return response.json() as Promise<EnrollmentToken>
}

export async function listEnrollmentTokens(): Promise<EnrollmentToken[]> {
  const response = await fetch('/api/enrollment-tokens')
  if (!response.ok) {
    throw new Error(`Failed to load enrollment tokens: ${response.status}`)
  }
  return response.json() as Promise<EnrollmentToken[]>
}

export async function consumeEnrollmentToken(tokenValue: string): Promise<Node> {
  const response = await fetch(`/api/enrollment-tokens/${encodeURIComponent(tokenValue)}/consume`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({}),
  })

  if (!response.ok) {
    let message = `Failed to consume token: ${response.status}`
    try {
      const body = await response.json() as { message?: string }
      if (body.message) message = body.message
    } catch {
      // use default message
    }
    throw new Error(message)
  }

  return response.json() as Promise<Node>
}

export async function listNodes(): Promise<Node[]> {
  const response = await fetch('/api/nodes')
  if (!response.ok) {
    throw new Error(`Failed to load nodes: ${response.status}`)
  }
  return response.json() as Promise<Node[]>
}

export async function updateNodeDisplayName(nodeId: string, displayName: string): Promise<Node> {
  const response = await fetch(`/api/nodes/${nodeId}/display-name`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ displayName }),
  })
  if (!response.ok) {
    let message = `Failed to update display name: ${response.status}`
    try {
      const body = await response.json() as { message?: string }
      if (body.message) message = body.message
    } catch {
      // use default message
    }
    throw new Error(message)
  }
  return response.json() as Promise<Node>
}

export async function deleteNode(nodeId: string): Promise<void> {
  const response = await fetch(`/api/nodes/${nodeId}`, {
    method: 'DELETE',
  })
  if (!response.ok && response.status !== 404) {
    throw new Error(`Failed to delete node: ${response.status}`)
  }
}

export async function listWorkloads(): Promise<WorkloadDefinition[]> {
  const response = await fetch('/api/workloads')
  if (!response.ok) {
    throw new Error(`Failed to load workloads: ${response.status}`)
  }
  const data = await response.json() as { workloads: Array<{ workloadId: string; name: string; description: string | null; publishedRevisionId: string | null; createdAtUtc: string; updatedAtUtc: string; revisionCount: number; latestRevision: { revisionId: string; version: string; isPublished: boolean } | null }> }
  return data.workloads.map(w => ({
    id: w.workloadId,
    name: w.name,
    description: w.description ?? '',
    createdAt: w.createdAtUtc,
    revisionCount: w.revisionCount,
    latestRevision: w.latestRevision ? {
      id: w.latestRevision.revisionId,
      workloadId: w.workloadId,
      revision: w.latestRevision.version,
      state: w.latestRevision.isPublished ? ('published' as const) : ('draft' as const),
    } : undefined,
  }))
}

export async function getWorkload(workloadId: string): Promise<WorkloadDefinition & { revisions: WorkloadRevision[] }> {
  const response = await fetch(`/api/workloads/${workloadId}`)
  if (!response.ok) {
    throw new Error(`Failed to load workload: ${response.status}`)
  }
  const data = await response.json() as {
    workloadId: string
    name: string
    description: string | null
    publishedRevisionId: string | null
    createdAtUtc: string
    updatedAtUtc: string
    revisions: Array<{
      revisionId: string
      version: string
      isPublished: boolean
      createdAtUtc: string
      packages: Array<{ packageId: string; packageIndex: number; packageName: string; packageVersion: string }>
    }>
  }
  const revisions = data.revisions.map(r => ({
    id: r.revisionId,
    workloadId: data.workloadId,
    revision: r.version,
    state: r.isPublished ? ('published' as const) : ('draft' as const),
    createdAt: r.createdAtUtc,
    publishedAt: r.isPublished ? r.createdAtUtc : undefined,
    packageSteps: r.packages.map(p => ({
      packageId: p.packageId,
      packageName: p.packageName,
      packageVersion: p.packageVersion,
      packageIndex: p.packageIndex,
      stepId: `step-${p.packageIndex}`,
    })),
  }))
  return {
    id: data.workloadId,
    name: data.name,
    description: data.description ?? '',
    createdAt: data.createdAtUtc,
    latestRevision: revisions.find(r => r.state === 'published') ?? revisions[0] ?? undefined,
    revisions,
  }
}

export async function listWorkloadRevisions(workloadId: string): Promise<WorkloadRevision[]> {
  const response = await fetch(`/api/workloads/${workloadId}`)
  if (!response.ok) {
    throw new Error(`Failed to load workload revisions: ${response.status}`)
  }
  const data = await response.json() as {
    workloadId: string
    revisions: Array<{
      revisionId: string
      version: string
      isPublished: boolean
      createdAtUtc: string
      packages: Array<{ packageId: string; packageIndex: number }>
    }>
  }
  return data.revisions.map(r => ({
    id: r.revisionId,
    workloadId: data.workloadId,
    revision: r.version,
    state: r.isPublished ? ('published' as const) : ('draft' as const),
    createdAt: r.createdAtUtc,
    publishedAt: r.isPublished ? r.createdAtUtc : undefined,
    packageSteps: r.packages.map(p => ({
      packageId: p.packageId,
      packageName: '',
      packageVersion: '',
      packageIndex: p.packageIndex,
      stepId: `step-${p.packageIndex}`,
    })),
  }))
}

export async function createWorkloadDefinitionDraft(
  request: CreateWorkloadDefinitionRequest,
): Promise<WorkloadDefinition> {
  const response = await fetch('/api/workloads', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: request.name, description: request.description }),
  })
  if (!response.ok) {
    let message = `Failed to create workload: ${response.status}`
    try {
      const body = await response.json() as { message?: string; errors?: ValidationFieldError[] }
      if (body.errors && body.errors.length > 0) {
        message = body.errors.map((e: ValidationFieldError) => `${e.field}: ${e.error}`).join('; ')
      } else if (body.message) {
        message = body.message
      }
    } catch {
      // use default message
    }
    throw new Error(message)
  }
  const data = await response.json() as { workloadId: string; name: string; description: string | null; createdAtUtc: string }
  return {
    id: data.workloadId,
    name: data.name,
    description: data.description ?? '',
    createdAt: data.createdAtUtc,
  }
}

export async function createWorkloadRevision(
  request: CreateWorkloadRevisionRequest,
): Promise<WorkloadRevision> {
  const count = request.packageSteps.length
  if (count === 0) {
    throw new Error('Workload revision must have at least 1 package')
  }

  const response = await fetch(`/api/workloads/${request.workloadId}/revisions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      version: request.revision,
      packages: request.packageSteps.map(step => ({
        packageId: step.packageId,
        packageIndex: step.packageIndex,
      })),
    }),
  })
  if (!response.ok) {
    let message = `Failed to create revision: ${response.status}`
    try {
      const body = await response.json() as { message?: string; errors?: ValidationFieldError[] }
      if (body.errors && body.errors.length > 0) {
        message = body.errors.map((e: ValidationFieldError) => `${e.field}: ${e.error}`).join('; ')
      } else if (body.message) {
        message = body.message
      }
    } catch {
      // use default message
    }
    throw new Error(message)
  }
  const data = await response.json() as {
    revisionId: string
    version: string
    isPublished: boolean
    createdAtUtc: string
    packages: Array<{ packageId: string; packageIndex: number }>
  }
  return {
    id: data.revisionId,
    workloadId: request.workloadId,
    revision: data.version,
    state: data.isPublished ? ('published' as const) : ('draft' as const),
    createdAt: data.createdAtUtc,
    packageSteps: data.packages.map(p => ({
      packageId: p.packageId,
      packageName: '',
      packageVersion: '',
      packageIndex: p.packageIndex,
      stepId: `step-${p.packageIndex}`,
    })),
  }
}

export async function publishWorkloadRevision(workloadId: string, revisionId: string): Promise<WorkloadRevision> {
  const response = await fetch(`/api/workloads/${workloadId}/publish`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ revisionId, replacePublished: true }),
  })
  if (!response.ok) {
    let message = `Failed to publish revision: ${response.status}`
    try {
      const body = await response.json() as { message?: string; errors?: ValidationFieldError[] }
      if (body.errors && body.errors.length > 0) {
        message = body.errors.map((e: ValidationFieldError) => `${e.field}: ${e.error}`).join('; ')
      } else if (body.message) {
        message = body.message
      }
    } catch {
      // use default message
    }
    throw new Error(message)
  }
  const data = await response.json() as {
    workloadId: string
    revisions: Array<{
      revisionId: string
      version: string
      isPublished: boolean
      createdAtUtc: string
      packages: Array<{ packageId: string; packageIndex: number }>
    }>
  }
  const published = data.revisions.find(r => r.isPublished)
  if (!published) {
    throw new Error('No published revision found after publish')
  }
  return {
    id: published.revisionId,
    workloadId: data.workloadId,
    revision: published.version,
    state: 'published' as const,
    createdAt: published.createdAtUtc,
    publishedAt: published.createdAtUtc,
    packageSteps: published.packages.map(p => ({
      packageId: p.packageId,
      packageName: '',
      packageVersion: '',
      packageIndex: p.packageIndex,
      stepId: `step-${p.packageIndex}`,
    })),
  }
}

export async function deleteWorkload(workloadId: string): Promise<void> {
  const response = await fetch(`/api/workloads/${workloadId}`, {
    method: 'DELETE',
  })
  if (!response.ok && response.status !== 404) {
    throw new Error(`Failed to delete workload: ${response.status}`)
  }
}

export async function importBulkWorkloads(file: File): Promise<BulkWorkloadImportResult> {
  const formData = new FormData()
  formData.append('file', file)

  const response = await fetch('/api/workloads/bulk-import', {
    method: 'POST',
    body: formData,
  })

  if (!response.ok) {
    let message = `Bulk import failed with status ${response.status}`
    try {
      const body = await response.json() as { message?: string }
      if (body.message) message = body.message
    } catch {}
    throw new Error(message)
  }

  return response.json() as Promise<BulkWorkloadImportResult>
}

export async function listWorkloadRuns(status: WorkloadRunStatus | 'all' = 'all'): Promise<WorkloadRun[]> {
  const url = status === 'all' ? '/api/workload-runs' : `/api/workload-runs?status=${status}`
  const response = await fetch(url)
  if (!response.ok) {
    throw new Error(`Failed to load workload runs: ${response.status}`)
  }
  const data = await response.json() as Array<{
    runId: string
    workloadId: string
    revisionId: string
    workloadVersion: string
    mode: string
    state: string
    createdAtUtc: string
    updatedAtUtc: string
    completedAtUtc: string | null
    riskLevel: string | null
    nodeIds: string[]
  }>
  return data.map(r => ({
    id: r.runId,
    workloadId: r.workloadId,
    workloadName: '',
    workloadRevision: r.workloadVersion,
    mode: r.mode as WorkloadRun['mode'],
    targetNodeIds: r.nodeIds,
    targetNodeHostnames: [],
    status: r.state.toLowerCase() as WorkloadRunStatus,
    createdAt: r.createdAtUtc,
    startedAt: r.createdAtUtc,
    completedAt: r.completedAtUtc ?? undefined,
    timeline: [],
  }))
}

export async function createWorkloadRun(request: CreateWorkloadRunRequest): Promise<WorkloadRun> {
  if (request.targetNodeIds.length === 0) {
    throw new Error('At least one target node is required')
  }

  const idempotencyKey = crypto.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`

  const response = await fetch('/api/workload-runs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      workloadId: request.workloadId,
      revisionId: request.revisionId,
      mode: request.mode,
      idempotencyKey,
      nodeIds: request.targetNodeIds,
      forceInstall: request.forceInstall,
    }),
  })
  if (!response.ok) {
    let message = `Failed to create run: ${response.status}`
    try {
      const body = await response.json() as { message?: string; errors?: ValidationFieldError[] }
      if (body.errors && body.errors.length > 0) {
        message = body.errors.map((e: ValidationFieldError) => `${e.field}: ${e.error}`).join('; ')
      } else if (body.message) {
        message = body.message
      }
    } catch {
      // use default message
    }
    throw new Error(message)
  }
  const data = await response.json() as { runId: string; state: string; riskLevel: string | null }
  return {
    id: data.runId,
    workloadId: request.workloadId,
    workloadName: '',
    workloadRevision: '',
    mode: request.mode,
    targetNodeIds: request.targetNodeIds,
    targetNodeHostnames: [],
    status: data.state.toLowerCase() as WorkloadRunStatus,
    createdAt: new Date().toISOString(),
    timeline: [],
  }
}

export async function getWorkloadRunSteps(runId: string): Promise<WorkloadRunTimelineItem[]> {
  const response = await fetch(`/api/workload-runs/${runId}/steps`)
  if (!response.ok) {
    throw new Error(`Failed to load run steps: ${response.status}`)
  }
  const data = await response.json() as {
    steps: Array<{
      packageId: string
      packageIndex: number
      stepId: string
      sequence: number
      action: string
    }>
  }
  return data.steps.map(s => ({
    sequence: s.sequence,
    packageId: s.packageId,
    packageIndex: s.packageIndex,
    stepId: s.stepId,
    status: 'queued' as const,
    messageType: 'AssignRun' as const,
    at: new Date().toISOString(),
    detail: `Action: ${s.action}`,
    nodeId: '',
  }))
}

export async function cancelWorkloadRun(runId: string): Promise<WorkloadRun> {
  const response = await fetch(`/api/workload-runs/${runId}/cancel`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason: 'Operator cancelled from UI' }),
  })
  if (!response.ok) {
    let message = `Failed to cancel run: ${response.status}`
    try {
      const body = await response.json() as { message?: string }
      if (body.message) message = body.message
    } catch {
      // use default message
    }
    throw new Error(message)
  }
  const data = await response.json() as { runId: string; state: string; cancelledAtUtc: string }
  return {
    id: data.runId,
    workloadId: '',
    workloadName: '',
    workloadRevision: '',
    mode: 'install' as WorkloadRun['mode'],
    targetNodeIds: [],
    targetNodeHostnames: [],
    status: data.state.toLowerCase() as WorkloadRunStatus,
    createdAt: data.cancelledAtUtc,
    completedAt: data.cancelledAtUtc,
    timeline: [],
  }
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
  const maxPackageIndex = revision.packageSteps?.length ?? 0
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
  const nextPackageId = revision.packageSteps?.[nextPackageIndex - 1]?.packageId ?? last?.packageId ?? 'pkg-unknown'
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
  const response = await fetch('/api/nodes/workload-states')
  if (!response.ok) {
    throw new Error(`Failed to list node workload states: ${response.status}`)
  }
  const data = await response.json()
  return data.map((s: any) => ({
    nodeId: s.nodeId ?? s.node_id,
    workloadId: s.workloadId ?? s.workload_id,
    workloadRevision: s.currentRevisionId ?? s.current_revision_id,
    runId: s.runId ?? '',
    status: s.state ?? s.status ?? 'pending',
    updatedAt: s.updatedAt ?? new Date().toISOString(),
  }))
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

function computeLastCheckInAge(lastSeenAt: string): string {
  const elapsed = Date.now() - new Date(lastSeenAt).getTime()
  const seconds = Math.floor(elapsed / 1_000)
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h`
  return `${Math.floor(hours / 24)}d`
}

export async function getOrchestratorHomeData(): Promise<OrchestratorHomeData> {
  const [nodes, workloadStates, runs, artifacts, workloads] = await Promise.all([
    listNodes(),
    listNodeWorkloadStates(),
    listWorkloadRuns(),
    listArtifacts(),
    listWorkloads(),
  ])

  const stateByNodeId = new Map(workloadStates.map(s => [s.nodeId, s]))
  const workloadById = new Map(workloads.map(w => [w.id, w]))

  const now = new Date()
  const dayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1_000)

  const dashboardNodes: DashboardNodeRow[] = nodes.map(node => {
    const state = stateByNodeId.get(node.id)
    const workload = state ? workloadById.get(state.workloadId) : undefined

    const health: DashboardNodeRow['health'] =
      node.status.toLowerCase() === 'online' ? 'online' : node.status.toLowerCase() === 'offline' ? 'offline' : 'warning'

    const runState: DashboardNodeRow['runState'] =
      state?.status === 'running'
        ? 'update'
        : state?.status === 'pending'
          ? 'pending-approval'
          : node.status === 'offline'
            ? 'failed'
            : 'idle'

    return {
      nodeId: node.id,
      hostname: node.hostname,
      displayName: node.displayName,
      health,
      assignedWorkload: workload?.name ?? '',
      workloadRevision: state?.workloadRevision ?? '',
      runState,
      lastCheckInAge: computeLastCheckInAge(node.lastSeenAt),
      riskLevel: 'low',
      revisionUpdateAvailable: false,
      packageUpdatesAvailable: false,
      packageUpdateCount: 0,
    }
  })

  const events: ImportantEvent[] = []

  for (const node of dashboardNodes) {
    if (node.health === 'offline') {
      events.push({
        id: `evt-offline-${node.nodeId}`,
        severity: 'critical',
        title: 'Node heartbeat timeout',
        detail: `${node.nodeId} is offline and not reporting.`,
        ageLabel: node.lastCheckInAge,
        nodeId: node.nodeId,
      })
    }
  }

  for (const run of runs) {
    if (run.status === 'failed') {
      events.push({
        id: `evt-failed-${run.id}`,
        severity: 'high',
        title: 'Workload run failed',
        detail: `${run.id} failed for workload ${run.workloadName || run.workloadId}.`,
        ageLabel: run.completedAt ? computeLastCheckInAge(run.completedAt) : 'recent',
        runId: run.id,
      })
    }
  }

  if (events.length === 0) {
    events.push({
      id: 'evt-healthy',
      severity: 'info',
      title: 'System healthy',
      detail: 'All nodes and workloads are operating normally.',
      ageLabel: 'now',
    })
  }

  const activeRuns24h = runs.filter(r => r.createdAt && new Date(r.createdAt) > dayAgo).length
  const failedRuns24h = runs.filter(r => r.status === 'failed' && r.completedAt && new Date(r.completedAt) > dayAgo).length

  const kpis: DashboardKpiSummary = {
    nodesOnline: nodes.filter(n => n.status.toLowerCase() === 'online').length,
    nodesOffline: nodes.filter(n => n.status.toLowerCase() === 'offline').length,
    workloadDefinitions: workloads.length,
    runningWorkloads: runs.filter(r => r.status === 'running').length,
    artifactsStored: artifacts.length,
    activeRuns24h,
    failedRuns24h,
    pendingApprovals: workloadStates.filter(s => s.status === 'pending').length,
    controlPlaneLatencyP95Ms: 0,
  }

  return {
    kpis,
    nodes: dashboardNodes,
    events,
    selectedNodeId: dashboardNodes[0]?.nodeId ?? '',
    logsByNodeId: {},
  }
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
