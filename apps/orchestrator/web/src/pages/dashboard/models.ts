export type WorkloadRow = {
  name: string
  mixedRevisions: boolean
  revisionsLabel: string
  nodesAssigned: number
  runningNodes: number
  nodesWithRevisionUpdates: number
  nodesWithPackageUpdates: number
  packageUpdateSignals: number
}
