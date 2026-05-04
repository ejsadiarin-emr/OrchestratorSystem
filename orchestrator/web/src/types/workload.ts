export interface WorkloadPackage {
  packageId: string
  version: string
  preInitSteps?: string[]
  postInitSteps?: string[]
}

export interface Workload {
  id: number
  workloadId: string
  workloadName: string
  version: string
  uploadedAt: string
  packages: WorkloadPackage[]
}
