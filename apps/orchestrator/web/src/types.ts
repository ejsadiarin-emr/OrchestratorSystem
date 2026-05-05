export type ManifestChannel = 'stable' | 'canary' | 'test'

export interface InstallAdapterInput {
  type?: string
  command?: string
  arguments?: string
  expectedExitCodes?: number[]
  timeoutSeconds?: number
}

export interface DetectionInput {
  type?: string
  path?: string
}

export interface PolicyTagsInput {
  retryabilityClass?: string
  idempotencyMode?: string
  riskLevel?: string
  approvalRequired?: boolean
}

export interface ArtifactManifest {
  packageId?: string
  version?: string
  channel?: string
  artifactType?: string
  verificationResult?: string
  installAdapter?: InstallAdapterInput
  detection?: DetectionInput
  policyTags?: PolicyTagsInput
}

export interface ArtifactUploadRequest {
  file: File
  manifest: ArtifactManifest
  detachedSignature?: string
}

export type IngestStepStatus = 'pending' | 'running' | 'completed'

export interface IngestStep {
  id: string
  label: string
  status: IngestStepStatus
}

export interface ValidationFieldError {
  field: string
  error: string
}

export interface ArtifactRecord {
  id: string
  packageEntityId?: string
  fileName: string
  createdAt: string
  sizeBytes?: number
  digest?: string
  detachedSignaturePresent?: boolean
  manifest: ArtifactManifest
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
  displayName: string
  ipAddress: string
  status: NodeStatus
  description: string
  osVersion: string
  agentVersion: string
  firstConnectedAt?: string
  lastSeenAt: string
}

export type WorkloadRevisionState = 'draft' | 'published'

export type WorkloadRunMode = 'install' | 'update' | 'uninstall'

export type WorkloadRunStatus = 'queued' | 'running' | 'completed' | 'failed' | 'cancelled'

export interface WorkloadPackageStep {
  packageId: string
  packageName: string
  packageVersion: string
  packageIndex: number
  stepId: string
  preInitSteps?: string[]
  postInitSteps?: string[]
}

export interface WorkloadRevision {
  id: string
  workloadId: string
  revision: string
  state: WorkloadRevisionState
  createdAt?: string
  publishedAt?: string
  packageSteps?: WorkloadPackageStep[]
  preWorkloadSteps?: string[]
  postWorkloadSteps?: string[]
  defaultShell?: string
}

export interface WorkloadDefinition {
  id: string
  name: string
  description: string
  createdAt: string
  latestRevision?: WorkloadRevision
  revisionCount?: number
}

export interface WorkloadRunTimelineItem {
  sequence: number
  packageId: string
  packageIndex: number
  stepId: string
  status: 'queued' | 'running' | 'completed' | 'failed' | 'cancelled'
  messageType: 'AssignRun' | 'AckClaim' | 'LeaseHeartbeat' | 'StepStatus' | 'Complete' | 'Fail' | 'LeaseClose'
  at: string
  detail: string
  nodeId: string
}

export interface WorkloadRunDiagnostics {
  reasonCode?: string
  lastMessage?: string
  replacementHint?: string
}

export interface WorkloadRun {
  id: string
  workloadId: string
  workloadName: string
  workloadRevision: string
  mode: WorkloadRunMode
  targetNodeIds: string[]
  targetNodeHostnames: string[]
  status: WorkloadRunStatus
  createdAt: string
  startedAt?: string
  completedAt?: string
  diagnostics?: WorkloadRunDiagnostics
  timeline: WorkloadRunTimelineItem[]
}

export interface NodeWorkloadState {
  nodeId: string
  workloadId: string
  workloadRevision: string
  currentRevisionId?: string | null
  runId: string
  status: WorkloadRunStatus
  updatedAt: string
  packageStatesJson?: string | null
}

export interface DashboardSummary {
  totalNodes: number
  connectedNodes: number
  activeWorkloadRuns: number
  failedWorkloadRuns: number
  workloadDefinitions: number
}

export interface AuditEvent {
  id: string
  at: string
  title: string
  detail: string
  type: 'ingest' | 'enrollment' | 'workload-run' | 'system'
}

export type NodeHealth = 'online' | 'warning' | 'offline'

export type NodeRunState =
  | 'idle'
  | 'install'
  | 'update'
  | 'uninstall'
  | 'cancel'
  | 'pending-approval'
  | 'failed'
  | 'success'

export type RiskLevel = 'low' | 'med' | 'high'

export interface DashboardKpiSummary {
  nodesOnline: number
  nodesOffline: number
  workloadDefinitions: number
  runningWorkloads: number
  artifactsStored: number
  activeRuns24h: number
  failedRuns24h: number
  pendingApprovals: number
  controlPlaneLatencyP95Ms: number
}

export interface DashboardNodeRow {
  nodeId: string
  hostname: string
  displayName: string
  health: NodeHealth
  assignedWorkload: string
  workloadRevision: string
  runState: NodeRunState
  lastCheckInAge: string
  riskLevel: RiskLevel
  revisionUpdateAvailable: boolean
  packageUpdatesAvailable: boolean
  packageUpdateCount?: number
  reasonCode?: string
}

export interface ImportantEvent {
  id: string
  severity: 'critical' | 'high' | 'medium' | 'info'
  title: string
  detail: string
  ageLabel: string
  nodeId?: string
  runId?: string
}

export interface MiniLogLine {
  id: string
  at: string
  message: string
  level: 'info' | 'warn' | 'error'
}

export interface OrchestratorHomeData {
  kpis: DashboardKpiSummary
  nodes: DashboardNodeRow[]
  events: ImportantEvent[]
  selectedNodeId: string
  logsByNodeId: Record<string, MiniLogLine[]>
  workloads: WorkloadDefinition[]
}

export interface AgentLocalSummary {
  nodeId: string
  hostname: string
  health: NodeHealth
  runState: NodeRunState
  currentWorkload: string
  targetRevision: string
  installedRevision: string
  pendingApproval: boolean
  riskLevel: RiskLevel
}

export interface CreateWorkloadDefinitionRequest {
  name: string
  description: string
}

export interface CreateWorkloadRevisionRequest {
  workloadId: string
  revision: string
  packageSteps: WorkloadPackageStep[]
  preWorkloadSteps?: string[]
  postWorkloadSteps?: string[]
  defaultShell?: string
}

export interface CreateWorkloadRunRequest {
  workloadId: string
  revisionId: string
  mode: WorkloadRunMode
  targetNodeIds: string[]
  reinstall?: boolean
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


export interface BulkIngestResultItem {
  fileName: string
  status: 'success' | 'failed' | 'skipped'
  reason?: string
  artifact?: {
    packageId: string
    version: string
  }
}

export interface BulkIngestResult {
  results: BulkIngestResultItem[]
}

export interface UploadSession {
  sessionId: string
}

export type UploadProgressCallback = (loaded: number, total: number) => void

export interface BulkWorkloadImportResultItem {
  name: string
  slug: string
  status: 'success' | 'failed'
  reason?: string
}

export interface BulkWorkloadImportResult {
  results: BulkWorkloadImportResultItem[]
}

export interface WorkloadJsonEntry {
  name: string
  version: string
  slug: string
  packages: string[]
  preUpgradeActions?: unknown[]
  preUninstallSteps?: string[]
  postUninstallSteps?: string[]
}

export interface NodeWorkloadAssignment {
  workloadId: string
  name: string
  currentVersion: string
  status: string
}

export interface PreCheckItem {
  category: string
  name: string
  packageId?: string
  status: 'passed' | 'failed' | 'warning' | 'info'
  detail?: string
  actualVersion?: string
}

export interface NodePreCheckSummary {
  checkedAt: string
  overallStatus: 'passed' | 'failed' | 'warning' | 'info'
  items: PreCheckItem[]
}

export interface NodeDetailResponse extends Node {
  workloads: NodeWorkloadAssignment[]
  latestPreCheck?: NodePreCheckSummary
}

export type PreCheckAction = 'Skip' | 'FreshInstall' | 'Update' | 'InstallMissing' | 'BlockedDowngrade' | 'Reinstall' | 'Unknown'

export type WorkloadAssignmentStatus = 'Current' | 'Drifted' | 'Unknown'

export interface PreCheckPackageResult {
  packageId: string
  name: string
  status: 'passed' | 'failed' | 'warning' | 'info'
  detail?: string
  expectedVersion?: string
  actualVersion?: string
  comparison?: string
}

export interface PreCheckSummaryNode {
  nodeId: string
  hostname: string
  overallStatus?: 'passed' | 'failed' | 'warning' | 'info'
  workloadStatus: WorkloadAssignmentStatus | 'Absent'
  action: PreCheckAction
  actionDetail?: string
  packages: PreCheckPackageResult[]
}
