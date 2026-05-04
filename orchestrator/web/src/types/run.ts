export type WorkloadRunMode = 'PRE_CHECK' | 'INSTALL' | 'UPDATE' | 'UNINSTALL'
export type WorkloadRunStatus = 'PENDING' | 'RUNNING' | 'SUCCESS' | 'FAILED' | 'SKIPPED' | 'AWAITING_CONFIRMATION'

export interface WorkloadRun {
  id: number
  agentId: string
  workloadId: string
  workloadVersion: string
  mode: WorkloadRunMode
  status: WorkloadRunStatus
  createdAt: string
  startedAt?: string
  completedAt?: string
}

export interface WorkloadRunStep {
  id: number
  runId: number
  packageId: string
  packageVersion: string
  stepOrder: number
  action: string
  status: string
  message?: string
  exitCode?: number
}
