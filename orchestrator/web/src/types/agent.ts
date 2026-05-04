export type AgentNodeStatus = 'UNREGISTERED' | 'REGISTERED' | 'LOST' | 'WORKLOAD_ASSIGNED' | 'NEEDS_UPDATE'
export type AgentPackageStatus = 'INSTALLED' | 'MISSING' | 'UNKNOWN'

export interface AgentNode {
  id: number
  agentId: string
  hostname: string
  ipAddress: string
  status: AgentNodeStatus
  assignedWorkloadId?: string
  assignedWorkloadVersion?: string
  lastSeenAt: string
  registeredAt: string
  pollingIntervalSeconds: number
}

export interface AgentPackage {
  agentId: string
  packageId: string
  installedVersion: string
  detectedAt: string
  status: AgentPackageStatus
}
