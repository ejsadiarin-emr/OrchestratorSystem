import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  cancelWorkloadRun,
  createWorkloadRun,
  getWorkloadRunSteps,
  listNodes,
  listWorkloadRuns,
  listWorkloads,
} from '../services/api'
import { subscribeToRunProgress } from '../services/realtime'
import type { Node, WorkloadDefinition, WorkloadRun, WorkloadRunStatus } from '../types'

const filterValues: Array<WorkloadRunStatus | 'all'> = [
  'all',
  'pending',
  'assigned',
  'running',
  'completed',
  'failed',
  'cancelled',
]

export default function WorkloadRuns() {
  const [runs, setRuns] = useState<WorkloadRun[]>([])
  const [nodes, setNodes] = useState<Node[]>([])
  const [workloads, setWorkloads] = useState<WorkloadDefinition[]>([])
  const [filter, setFilter] = useState<WorkloadRunStatus | 'all'>('all')
  const [selectedRun, setSelectedRun] = useState<WorkloadRun | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [form, setForm] = useState({
    workloadId: '',
    workloadRevision: '',
    mode: 'install' as WorkloadRun['mode'],
    targetNodeIds: [] as string[],
  })
  const [submitting, setSubmitting] = useState(false)
  const unsubscribers = useRef<Map<string, () => void>>(new Map())

  const refresh = useCallback(async (status: WorkloadRunStatus | 'all' = filter) => {
    const [runData, nodeData, workloadData] = await Promise.all([
      listWorkloadRuns(status),
      listNodes(),
      listWorkloads(),
    ])
    setRuns(runData)
    setNodes(nodeData)
    setWorkloads(workloadData)

    const defaultWorkload = workloadData[0]
    if (!form.workloadId && defaultWorkload) {
      setForm(current => ({
        ...current,
        workloadId: defaultWorkload.id,
        workloadRevision: defaultWorkload.latestRevision?.revision ?? '',
      }))
    }

    if (form.targetNodeIds.length === 0 && nodeData[0]) {
      setForm(current => ({ ...current, targetNodeIds: [nodeData[0].id] }))
    }
  }, [filter, form.targetNodeIds.length, form.workloadId])

  useEffect(() => {
    refresh()
      .catch(() => setError('Failed to load workload runs, workloads, or nodes.'))
      .finally(() => setLoading(false))

    const subscriptions = unsubscribers.current

    return () => {
      const activeUnsubscribers = Array.from(subscriptions.values())
      activeUnsubscribers.forEach(unsubscribe => unsubscribe())
      subscriptions.clear()
    }
  }, [refresh])

  useEffect(() => {
    refresh(filter).catch(() => setError('Failed to refresh workload runs.'))
  }, [filter, refresh])

  useEffect(() => {
    const active = runs.filter(run => run.status === 'assigned' || run.status === 'running' || run.status === 'pending')
    const activeIds = new Set(active.map(run => run.id))

    active.forEach(run => {
      if (!unsubscribers.current.has(run.id)) {
        const unsubscribe = subscribeToRunProgress(run.id, updatedRun => {
          setRuns(current => current.map(item => (item.id === updatedRun.id ? { ...updatedRun } : item)))
          if (selectedRun?.id === updatedRun.id) {
            setSelectedRun({ ...updatedRun })
          }
        })
        unsubscribers.current.set(run.id, unsubscribe)
      }
    })

    Array.from(unsubscribers.current.entries()).forEach(([runId, unsubscribe]) => {
      if (!activeIds.has(runId)) {
        unsubscribe()
        unsubscribers.current.delete(runId)
      }
    })
  }, [runs, selectedRun?.id])

  const selectedWorkload = useMemo(
    () => workloads.find(item => item.id === form.workloadId),
    [workloads, form.workloadId],
  )

  const handleCreateRun = async (event: React.FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    setError(null)

    try {
      await createWorkloadRun(form)
      await refresh(filter)
    } catch (creationError) {
      setError(creationError instanceof Error ? creationError.message : 'Failed to create workload run.')
    } finally {
      setSubmitting(false)
    }
  }

  const handleCancel = async (runId: string) => {
    try {
      await cancelWorkloadRun(runId)
      await refresh(filter)
    } catch (cancelError) {
      setError(cancelError instanceof Error ? cancelError.message : 'Failed to cancel run.')
    }
  }

  const openRunDetails = async (run: WorkloadRun) => {
    const steps = await getWorkloadRunSteps(run.id)
    setSelectedRun({ ...run, timeline: steps })
  }

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-6">
      <header className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-strong)]">Workload Runs</h1>
        <p className="mt-2 text-sm text-[var(--text-soft)]">
          Runtime flow uses <code>AssignRun</code> and <code>/api/workload-runs*</code> contracts. Timeline shows package index,
          step id, sequence, and status.
        </p>
      </header>

      {error && (
        <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-4 py-3 text-sm text-[var(--status-danger-text)]">
          {error}
        </div>
      )}

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h2 className="text-lg font-semibold text-[var(--text-strong)]">Create Workload Run</h2>
        <form onSubmit={handleCreateRun} className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4 xl:items-end">
          <label className="block text-sm text-[var(--text-soft)]">
            Workload
            <select
              value={form.workloadId}
              onChange={event => {
                const workloadId = event.target.value
                const workload = workloads.find(item => item.id === workloadId)
                setForm(current => ({
                  ...current,
                  workloadId,
                  workloadRevision: workload?.latestRevision?.revision ?? '',
                }))
              }}
              className="mt-1 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
              required
            >
              <option value="">Select workload...</option>
              {workloads.map(workload => (
                <option key={workload.id} value={workload.id}>
                  {workload.name}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-sm text-[var(--text-soft)]">
            Revision
            <input
              value={form.workloadRevision}
              onChange={event => setForm(current => ({ ...current, workloadRevision: event.target.value }))}
              className="mt-1 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
              required
            />
          </label>
          <label className="block text-sm text-[var(--text-soft)]">
            Mode
            <select
              value={form.mode}
              onChange={event => setForm(current => ({ ...current, mode: event.target.value as WorkloadRun['mode'] }))}
              className="mt-1 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
            >
              <option value="install">install</option>
              <option value="update">update</option>
              <option value="rollback">rollback</option>
              <option value="cancel">cancel</option>
            </select>
          </label>
          <label className="block text-sm text-[var(--text-soft)]">
            Target nodes
            <select
              multiple
              value={form.targetNodeIds}
              onChange={event => {
                const values = Array.from(event.target.selectedOptions).map(option => option.value)
                setForm(current => ({ ...current, targetNodeIds: values }))
              }}
              className="mt-1 h-24 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
            >
              {nodes.map(node => (
                <option key={node.id} value={node.id}>
                  {node.hostname}
                </option>
              ))}
            </select>
          </label>
          <div className="md:col-span-2 xl:col-span-4">
            <button
              type="submit"
              disabled={submitting}
              className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
            >
              {submitting ? 'Creating...' : 'Create Run'}
            </button>
          </div>
        </form>
        {selectedWorkload?.latestRevision?.packageSteps && (
          <p className="mt-3 text-xs text-[var(--text-soft)]">
            Latest revision template has {selectedWorkload.latestRevision.packageSteps.length} package steps.
          </p>
        )}
      </section>

      <div className="flex flex-wrap gap-2">
        {filterValues.map(value => (
          <button
            key={value}
            onClick={() => setFilter(value)}
            className={`rounded-full px-3 py-1 text-sm ${
              filter === value
                ? 'bg-[var(--accent)] text-white'
                : 'bg-[var(--surface)] text-[var(--text-soft)] ring-1 ring-[var(--surface-border)] hover:bg-[var(--surface-subtle)]'
            }`}
          >
            {value}
          </button>
        ))}
      </div>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        {runs.length === 0 ? (
          <p className="text-sm text-[var(--text-soft)]">No runs found.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-[var(--surface-border)]">
              <thead>
                <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                  <th className="px-4 py-3">Workload</th>
                  <th className="px-4 py-3">Mode</th>
                  <th className="px-4 py-3">Revision</th>
                  <th className="px-4 py-3">Nodes</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--surface-border)] text-sm">
                {runs.map(run => (
                  <tr key={run.id} className="cursor-pointer hover:bg-[var(--surface-subtle)]" onClick={() => openRunDetails(run)}>
                    <td className="px-4 py-3 font-medium text-[var(--text-strong)]">{run.workloadName}</td>
                    <td className="px-4 py-3 text-[var(--text-soft)]">{run.mode}</td>
                    <td className="px-4 py-3 text-[var(--text-soft)]">{run.workloadRevision}</td>
                    <td className="px-4 py-3 text-[var(--text-soft)]">{run.targetNodeHostnames.join(', ')}</td>
                    <td className="px-4 py-3 text-[var(--text-soft)]">{run.status}</td>
                    <td className="px-4 py-3 text-right">
                      {(run.status === 'running' || run.status === 'assigned' || run.status === 'pending') && (
                        <button
                          onClick={event => {
                            event.stopPropagation()
                            handleCancel(run.id)
                          }}
                          className="text-red-600 hover:text-red-800"
                        >
                          Cancel
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {selectedRun && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/35 p-4 backdrop-blur-sm">
          <div className="max-h-[90vh] w-full max-w-4xl overflow-y-auto rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
            <h3 className="text-xl font-semibold text-[var(--text-strong)]">{selectedRun.id} diagnostics</h3>
            <p className="mt-1 text-sm text-[var(--text-soft)]">
              {selectedRun.workloadName} revision {selectedRun.workloadRevision} on {selectedRun.targetNodeHostnames.join(', ')}
            </p>

            <div className="mt-4 grid grid-cols-2 gap-3 text-sm">
              <div>
                <span className="font-medium text-[var(--text-strong)]">Mode:</span> {selectedRun.mode}
              </div>
              <div>
                <span className="font-medium text-[var(--text-strong)]">Status:</span> {selectedRun.status}
              </div>
              <div className="col-span-2">
                <span className="font-medium text-[var(--text-strong)]">Reason:</span>{' '}
                {selectedRun.diagnostics?.reasonCode ?? 'n/a'}
              </div>
              {selectedRun.diagnostics?.lastMessage && (
                <div className="col-span-2 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-[var(--text-soft)]">
                  {selectedRun.diagnostics.lastMessage}
                </div>
              )}
            </div>

            <h4 className="mt-6 text-sm font-semibold uppercase tracking-wide text-[var(--text-soft)]">Timeline</h4>
            <div className="mt-3 overflow-x-auto rounded-xl border border-[var(--surface-border)]">
              <table className="min-w-full divide-y divide-[var(--surface-border)] text-sm">
                <thead>
                  <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                    <th className="px-3 py-2">Sequence</th>
                    <th className="px-3 py-2">Message</th>
                    <th className="px-3 py-2">Package Index</th>
                    <th className="px-3 py-2">Step ID</th>
                    <th className="px-3 py-2">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--surface-border)]">
                  {selectedRun.timeline.map(item => (
                    <tr key={`${selectedRun.id}-${item.sequence}`}>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{item.sequence}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{item.messageType}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{item.packageIndex}</td>
                      <td className="px-3 py-2 font-mono text-xs text-[var(--text-soft)]">{item.stepId}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{item.status}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <button
              onClick={() => setSelectedRun(null)}
              className="mt-6 w-full rounded-lg bg-[var(--surface-muted)] px-4 py-2 text-sm font-medium text-[var(--text-soft)] hover:bg-[var(--surface-border)]"
            >
              Close
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
