import type { OrchestratorHomeData } from '../../types'
import type { WorkloadRow } from './models'

export function buildWorkloadRows(data: OrchestratorHomeData | null): WorkloadRow[] {
  if (!data) {
    return []
  }

  const rows = new Map<
    string,
    {
      name: string
      revisions: Set<string>
      nodesAssigned: number
      runningNodes: number
      nodesWithRevisionUpdates: number
      nodesWithPackageUpdates: number
      packageUpdateSignals: number
    }
  >()

  data.nodes.forEach(node => {
    node.workloads.forEach(workload => {
      const existing = rows.get(workload.name) ?? {
        name: workload.name,
        revisions: new Set<string>(),
        nodesAssigned: 0,
        runningNodes: 0,
        nodesWithRevisionUpdates: 0,
        nodesWithPackageUpdates: 0,
        packageUpdateSignals: 0,
      }

      existing.revisions.add(workload.revision)
      existing.nodesAssigned += 1

      if (workload.runState !== 'idle' && workload.runState !== 'success' && workload.runState !== 'failed') {
        existing.runningNodes += 1
      }

      if (node.revisionUpdateAvailable) {
        existing.nodesWithRevisionUpdates += 1
      }

      if (node.packageUpdatesAvailable) {
        existing.nodesWithPackageUpdates += 1
        existing.packageUpdateSignals += node.packageUpdateCount ?? 1
      }

      rows.set(workload.name, existing)
    })
  })

  return Array.from(rows.values())
    .map(row => ({
      name: row.name,
      nodesAssigned: row.nodesAssigned,
      runningNodes: row.runningNodes,
      nodesWithRevisionUpdates: row.nodesWithRevisionUpdates,
      nodesWithPackageUpdates: row.nodesWithPackageUpdates,
      packageUpdateSignals: row.packageUpdateSignals,
      mixedRevisions: row.revisions.size > 1,
      revisionsLabel: Array.from(row.revisions).join(', '),
    }))
    .sort(
      (a, b) =>
        b.packageUpdateSignals - a.packageUpdateSignals ||
        b.nodesAssigned - a.nodesAssigned ||
        a.name.localeCompare(b.name),
    )
}
