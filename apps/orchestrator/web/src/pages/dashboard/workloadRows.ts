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
    if (!node.assignedWorkload) {
      return
    }

    const existing = rows.get(node.assignedWorkload) ?? {
      name: node.assignedWorkload,
      revisions: new Set<string>(),
      nodesAssigned: 0,
      runningNodes: 0,
      nodesWithRevisionUpdates: 0,
      nodesWithPackageUpdates: 0,
      packageUpdateSignals: 0,
    }

    existing.revisions.add(node.workloadRevision)
    existing.nodesAssigned += 1

    if (node.runState !== 'idle' && node.runState !== 'success' && node.runState !== 'failed') {
      existing.runningNodes += 1
    }

    if (node.revisionUpdateAvailable) {
      existing.nodesWithRevisionUpdates += 1
    }

    if (node.packageUpdatesAvailable) {
      existing.nodesWithPackageUpdates += 1
      existing.packageUpdateSignals += node.packageUpdateCount ?? 1
    }

    rows.set(node.assignedWorkload, existing)
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
