export type ManifestChannel = 'stable' | 'canary' | 'test'

export type ConfidenceTag = 'declared' | 'derived' | 'verified'

export interface OriginMetadata {
  sourceUrl: string
  publisher: string
  packageFamily: string
  collectedAt: string
  sourceConfidence: ConfidenceTag
  publisherConfidence: ConfidenceTag
}

export interface ArtifactManifest {
  name: string
  version: string
  channel: ManifestChannel
  installType: 'msi' | 'exe' | 'zip'
  installArgs: string
  digestSha256: string
  signingIdentity: string
  originMetadata: OriginMetadata
}

export interface ArtifactUploadRequest {
  fileName: string
  fileSizeBytes: number
  manifest: ArtifactManifest
  detachedSignature?: string
}

export type IngestStepStatus = 'pending' | 'running' | 'completed'

export interface IngestStep {
  id: string
  label: string
  status: IngestStepStatus
}

export interface ArtifactRecord {
  id: string
  fileName: string
  createdAt: string
  manifest: ArtifactManifest
  detachedSignaturePresent: boolean
}

export interface ArtifactIngestResult {
  artifact: ArtifactRecord
  steps: IngestStep[]
}

export interface EnrollmentToken {
  token: string
  issuedAt: string
  expiresAt: string
  requestedBy: string
  orchestratorUrl: string
  singleUse: true
  used: boolean
}

export interface IssueEnrollmentTokenRequest {
  requestedBy: string
  orchestratorUrl: string
  ttlMinutes: number
}

export type NodeStatus = 'online' | 'offline' | 'installing' | 'enrolling' | 'unknown'

export interface Node {
  id: string
  hostname: string
  ipAddress: string
  status: NodeStatus
  description: string
  osVersion: string
  agentVersion: string
  firstConnectedAt?: string
  lastSeenAt: string
}

export type DeliveryStage =
  | 'assigned'
  | 'head-check'
  | 'range-download'
  | 'verify-digest-signature'
  | 'completed'
  | 'failed'

export interface JobEvent {
  at: string
  message: string
}

export interface InstallJob {
  id: string
  artifactId: string
  artifactName: string
  targetNodeId: string
  targetNodeHostname: string
  status: 'pending' | 'running' | 'completed' | 'failed' | 'cancelled'
  deliveryStage: DeliveryStage
  chunksDownloaded: number
  totalChunks: number
  startedAt: string
  completedAt?: string
  errorMessage?: string
  events: JobEvent[]
}

export interface DashboardSummary {
  totalNodes: number
  connectedNodes: number
  activeJobs: number
  failedJobs: number
  artifactsInStore: number
}

export interface AuditEvent {
  id: string
  at: string
  title: string
  detail: string
  type: 'ingest' | 'enrollment' | 'delivery' | 'system'
}

export interface CreateInstallJobRequest {
  artifactId: string
  targetNodeId: string
}

export interface CreateNodeRequest {
  hostname: string
  ipAddress: string
  description: string
}

export interface CreatePackageRequest {
  name: string
  version: string
  sourcePath: string
  installType: string
  installArgs: string
}

export interface Package {
  id: string
  name: string
  version: string
  sourcePath: string
  installType: string
  installArgs: string
  createdAt: string
}
