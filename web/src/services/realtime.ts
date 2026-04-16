import type { InstallJob } from '../types'
import { advanceJobDelivery } from './api'

type JobListener = (job: InstallJob) => void

const activeIntervals = new Map<string, number>()

export function subscribeToJobProgress(jobId: string, onUpdate: JobListener): () => void {
  if (activeIntervals.has(jobId)) {
    window.clearInterval(activeIntervals.get(jobId))
  }

  const interval = window.setInterval(async () => {
    try {
      const updated = await advanceJobDelivery(jobId)
      onUpdate(updated)

      if (updated.status === 'completed' || updated.status === 'failed' || updated.status === 'cancelled') {
        const active = activeIntervals.get(jobId)
        if (active !== undefined) {
          window.clearInterval(active)
          activeIntervals.delete(jobId)
        }
      }
    } catch {
      const active = activeIntervals.get(jobId)
      if (active !== undefined) {
        window.clearInterval(active)
        activeIntervals.delete(jobId)
      }
    }
  }, 1200)

  activeIntervals.set(jobId, interval)

  return () => {
    const active = activeIntervals.get(jobId)
    if (active !== undefined) {
      window.clearInterval(active)
      activeIntervals.delete(jobId)
    }
  }
}
