import type { WorkloadRun } from '../types'
import { advanceWorkloadRun } from './api'

type RunListener = (run: WorkloadRun) => void

const activeIntervals = new Map<string, number>()

export function subscribeToRunProgress(runId: string, onUpdate: RunListener): () => void {
  if (activeIntervals.has(runId)) {
    window.clearInterval(activeIntervals.get(runId))
  }

  const interval = window.setInterval(async () => {
    try {
      const updated = await advanceWorkloadRun(runId)
      onUpdate(updated)

      if (updated.status === 'completed' || updated.status === 'failed' || updated.status === 'cancelled') {
        const active = activeIntervals.get(runId)
        if (active !== undefined) {
          window.clearInterval(active)
          activeIntervals.delete(runId)
        }
      }
    } catch {
      const active = activeIntervals.get(runId)
      if (active !== undefined) {
        window.clearInterval(active)
        activeIntervals.delete(runId)
      }
    }
  }, 1200)

  activeIntervals.set(runId, interval)

  return () => {
    const active = activeIntervals.get(runId)
    if (active !== undefined) {
      window.clearInterval(active)
      activeIntervals.delete(runId)
    }
  }
}
