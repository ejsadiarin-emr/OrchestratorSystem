import type {
  ArtifactIngestResult,
  ArtifactManifest,
  ArtifactRecord,
  ArtifactUploadRequest,
  AuditEvent,
  CreateInstallJobRequest,
  DashboardSummary,
  EnrollmentToken,
  IngestStep,
  InstallJob,
  IssueEnrollmentTokenRequest,
  ManifestChannel,
  Node,
  OriginMetadata,
} from '../types'

const baseTime = new Date('2026-04-16T12:00:00.000Z').getTime()

const channelValues: ManifestChannel[] = ['stable', 'canary', 'test']

let artifactSeq = 2
let jobSeq = 2
let nodeSeq = 2
let tokenSeq = 1

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

const jobs: InstallJob[] = [
  {
    id: 'job-001',
    artifactId: 'artifact-001',
    artifactName: 'EJ Installer 1.12.0',
    targetNodeId: 'node-001',
    targetNodeHostname: 'wj-plant-01',
    status: 'running',
    deliveryStage: 'range-download',
    chunksDownloaded: 3,
    totalChunks: 6,
    startedAt: at(30),
    events: [
      { at: at(31), message: 'AssignJob delivered with artifact reference artifact-001' },
      { at: at(32), message: 'HEAD /api/artifacts/artifact-001 -> 200 OK' },
      { at: at(33), message: 'Range GET loop started (chunk 1/6)' },
      { at: at(34), message: 'Range GET loop progressed (chunk 3/6)' },
    ],
  },
]

const enrollmentTokens: EnrollmentToken[] = []
const auditEvents: AuditEvent[] = [
  {
    id: 'audit-001',
    at: at(34),
    title: 'Artifact delivery in progress',
    detail: 'job-001 is downloading artifact chunks via ranged GET.',
    type: 'delivery',
  },
  {
    id: 'audit-002',
    at: at(0),
    title: 'Artifact ingested',
    detail: 'artifact-001 was verified and locked in local artifact store.',
    type: 'ingest',
  },
]

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
  pushAudit('ingest', 'Artifact ingested', `${artifact.id} accepted via multipart upload and locked in artifact store.`)

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
  pushAudit('enrollment', 'Enrollment token issued', `POST /api/nodes/enroll issued ${token.token} for ${request.orchestratorUrl}.`)
  return token
}

export async function listEnrollmentTokens(): Promise<EnrollmentToken[]> {
  return [...enrollmentTokens]
}

export async function consumeEnrollmentToken(tokenValue: string): Promise<Node> {
  const token = enrollmentTokens.find(t => t.token === tokenValue)
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
  pushAudit('enrollment', 'Node auto-registered', `${node.hostname} connected with URL+token bootstrap and uploaded first-connect metadata.`)
  return node
}

export async function listNodes(): Promise<Node[]> {
  return [...nodes]
}

export async function createInstallJob(request: CreateInstallJobRequest): Promise<InstallJob> {
  const artifact = artifacts.find(item => item.id === request.artifactId)
  if (!artifact) {
    throw new Error('Artifact not found')
  }

  const node = nodes.find(item => item.id === request.targetNodeId)
  if (!node) {
    throw new Error('Target node not found')
  }

  const job: InstallJob = {
    id: `job-${String(jobSeq++).padStart(3, '0')}`,
    artifactId: artifact.id,
    artifactName: `${artifact.manifest.name} ${artifact.manifest.version}`,
    targetNodeId: node.id,
    targetNodeHostname: node.hostname,
    status: 'pending',
    deliveryStage: 'assigned',
    chunksDownloaded: 0,
    totalChunks: 6,
    startedAt: new Date().toISOString(),
    events: [
      {
        at: new Date().toISOString(),
        message: `AssignJob created for ${node.hostname} with artifact ${artifact.id}`,
      },
    ],
  }

  jobs.unshift(job)
  pushAudit('delivery', 'Install job created', `${job.id} queued for ${job.targetNodeHostname}.`)
  return job
}

export async function listJobs(status: InstallJob['status'] | 'all' = 'all'): Promise<InstallJob[]> {
  if (status === 'all') {
    return [...jobs]
  }

  return jobs.filter(job => job.status === status)
}

export async function advanceJobDelivery(jobId: string): Promise<InstallJob> {
  const job = jobs.find(item => item.id === jobId)
  if (!job) {
    throw new Error('Job not found')
  }

  if (job.status === 'completed' || job.status === 'failed' || job.status === 'cancelled') {
    return job
  }

  job.status = 'running'
  const now = new Date().toISOString()

  if (job.deliveryStage === 'assigned') {
    job.deliveryStage = 'head-check'
    job.events.push({ at: now, message: `HEAD /api/artifacts/${job.artifactId} -> 200 OK` })
    return job
  }

  if (job.deliveryStage === 'head-check') {
    job.deliveryStage = 'range-download'
    job.chunksDownloaded = 1
    job.events.push({ at: now, message: `GET /api/artifacts/${job.artifactId} with Range bytes=0-1048575` })
    return job
  }

  if (job.deliveryStage === 'range-download') {
    if (job.chunksDownloaded < job.totalChunks) {
      job.chunksDownloaded += 1
      job.events.push({
        at: now,
        message: `Chunk ${job.chunksDownloaded}/${job.totalChunks} downloaded and assembled locally`,
      })
      if (job.chunksDownloaded < job.totalChunks) {
        return job
      }
    }

    job.deliveryStage = 'verify-digest-signature'
    job.events.push({ at: now, message: 'Digest and signature verification started' })
    return job
  }

  if (job.deliveryStage === 'verify-digest-signature') {
    const artifact = artifacts.find(item => item.id === job.artifactId)
    const shouldFailValidation = artifact?.manifest.channel === 'test'

    if (shouldFailValidation) {
      job.deliveryStage = 'failed'
      job.status = 'failed'
      job.completedAt = now
      job.errorMessage = 'Digest/signature validation failed for test channel artifact'
      job.events.push({ at: now, message: 'Digest/signature validation failed; job moved to terminal failed state' })
      pushAudit('delivery', 'Install job failed validation', `${job.id} failed during verify-digest-signature stage.`)
      return job
    }

    job.deliveryStage = 'completed'
    job.status = 'completed'
    job.completedAt = now
    job.events.push({ at: now, message: 'Install step reported success and lease closed' })
    pushAudit('delivery', 'Install job completed', `${job.id} reached terminal completed state.`)
  }

  return job
}

export async function cancelJob(jobId: string): Promise<void> {
  const job = jobs.find(item => item.id === jobId)
  if (!job) {
    throw new Error('Job not found')
  }

  job.status = 'cancelled'
  job.deliveryStage = 'failed'
  job.errorMessage = 'Cancelled by operator'
  job.completedAt = new Date().toISOString()
  job.events.push({ at: new Date().toISOString(), message: 'Job cancelled by operator action' })
}

export async function getDashboardSummary(): Promise<DashboardSummary> {
  return {
    totalNodes: nodes.length,
    connectedNodes: nodes.filter(node => node.status === 'online').length,
    activeJobs: jobs.filter(job => job.status === 'running' || job.status === 'pending').length,
    failedJobs: jobs.filter(job => job.status === 'failed').length,
    artifactsInStore: artifacts.length,
  }
}

export async function listAuditEvents(limit = 8): Promise<AuditEvent[]> {
  return [...auditEvents].slice(0, limit)
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

function pushAudit(type: AuditEvent['type'], title: string, detail: string): void {
  auditEvents.unshift({
    id: `audit-${String(auditEvents.length + 1).padStart(3, '0')}`,
    at: new Date().toISOString(),
    title,
    detail,
    type,
  })
}
