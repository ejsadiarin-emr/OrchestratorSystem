export interface Package {
  id: string
  name: string
  version: string
  sourcePath: string
  installType: string
  installArgs: string
  createdAt: string
}

export interface Node {
  id: string
  hostname: string
  ipAddress: string
  status: 'Online' | 'Offline' | 'Installing' | 'Unknown'
  lastSeenAt: string
  description: string
}

export interface JobStep {
  name: string
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  duration?: string
}

export interface InstallJob {
  id: string
  packageId: string
  packageName: string
  targetNodeId: string
  targetNodeHostname: string
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  currentStep: number
  totalSteps: number
  startedAt: string
  completedAt?: string
  steps: JobStep[]
  errorMessage?: string
}

export interface CreatePackageRequest {
  name: string
  version: string
  sourcePath: string
  installType: string
  installArgs: string
}

export interface CreateNodeRequest {
  hostname: string
  ipAddress: string
  description: string
}

export interface CreateJobRequest {
  packageId: string
  targetNodeId: string
}
