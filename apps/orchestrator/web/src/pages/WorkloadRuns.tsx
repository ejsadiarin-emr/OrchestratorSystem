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
import { Modal, ModalContent, ModalDescription, ModalFooter, ModalHeader, ModalTitle } from '../components/ui/modal'

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
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false)
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
      setIsCreateModalOpen(false)
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
    try {
      const steps = await getWorkloadRunSteps(run.id)
      setSelectedRun({ ...run, timeline: steps })
    } catch {
      setError('Failed to load run diagnostics.')
    }
  }

  const statusClasses: Record<WorkloadRunStatus, string> = {
    pending: 'border-amber-200 bg-amber-50 text-amber-700',
    assigned: 'border-sky-200 bg-sky-50 text-sky-700',
    running: 'border-cyan-200 bg-cyan-50 text-cyan-700',
    completed: 'border-emerald-200 bg-emerald-50 text-emerald-700',
    failed: 'border-rose-200 bg-rose-50 text-rose-700',
    cancelled: 'border-slate-200 bg-slate-100 text-slate-700',
  }

  const formatTimestamp = (value: string) => new Date(value).toLocaleString()

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
        <p className="mt-1 text-sm text-[var(--text-soft)]">
          Start a run from a centered popup with workload, revision, mode, and target selection.
        </p>
        <button
          type="button"
          onClick={() => setIsCreateModalOpen(true)}
          className="mt-4 rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
        >
          Open Run Creator
        </button>
      </section>

      <Modal open={isCreateModalOpen} onOpenChange={setIsCreateModalOpen}>
        <ModalContent className="w-[min(92vw,42rem)]">
          <ModalHeader>
            <ModalTitle>Create Workload Run</ModalTitle>
            <ModalDescription>Pick a workload target and launch a run with existing run APIs.</ModalDescription>
          </ModalHeader>
          <form onSubmit={handleCreateRun} className="grid grid-cols-1 gap-4 px-4 pb-4 md:grid-cols-2">
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
                className="mt-1 h-28 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
              >
                {nodes.map(node => (
                  <option key={node.id} value={node.id}>
                    {node.hostname}
                  </option>
                ))}
              </select>
            </label>
            {selectedWorkload?.latestRevision?.packageSteps && (
              <p className="text-xs text-[var(--text-soft)] md:col-span-2">
                Latest revision template has {selectedWorkload.latestRevision.packageSteps.length} package steps.
              </p>
            )}
            <ModalFooter className="px-0 pb-0 pt-2 md:col-span-2 sm:flex-row sm:justify-end">
              <button
                type="button"
                onClick={() => setIsCreateModalOpen(false)}
                className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={submitting}
                className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
              >
                {submitting ? 'Creating...' : 'Create Run'}
              </button>
            </ModalFooter>
          </form>
        </ModalContent>
      </Modal>

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
            <table className="min-w-full border-separate border-spacing-y-2">
              <thead>
                <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                  <th className="px-4 py-2">Workload</th>
                  <th className="px-4 py-2">Mode</th>
                  <th className="px-4 py-2">Revision</th>
                  <th className="px-4 py-2">Nodes</th>
                  <th className="px-4 py-2">Status</th>
                  <th className="px-4 py-2 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="text-sm">
                {runs.map(run => (
                  <tr
                    key={run.id}
                    className="cursor-pointer rounded-xl bg-[var(--surface-subtle)]/40 shadow-[inset_0_0_0_1px_var(--surface-border)] transition hover:bg-[var(--surface-subtle)]"
                    onClick={() => openRunDetails(run)}
                  >
                    <td className="rounded-l-xl px-4 py-3 font-medium text-[var(--text-strong)]">
                      <span className="block">{run.workloadName}</span>
                      <span className="mt-1 block font-mono text-xs text-[var(--text-soft)]">{run.id}</span>
                    </td>
                    <td className="px-4 py-3 text-[var(--text-soft)]">
                      <span className="rounded-full border border-[var(--surface-border)] bg-[var(--surface)] px-2 py-1 text-xs uppercase tracking-wide">
                        {run.mode}
                      </span>
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-[var(--text-soft)]">{run.workloadRevision}</td>
                    <td className="px-4 py-3 text-[var(--text-soft)]">{run.targetNodeHostnames.join(', ')}</td>
                    <td className="px-4 py-3 text-[var(--text-soft)]">
                      <span className={`rounded-full border px-2 py-1 text-xs font-medium uppercase tracking-wide ${statusClasses[run.status]}`}>
                        {run.status}
                      </span>
                    </td>
                    <td className="rounded-r-xl px-4 py-3 text-right">
                      {(run.status === 'running' || run.status === 'assigned' || run.status === 'pending') && (
                        <button
                          onClick={event => {
                            event.stopPropagation()
                            handleCancel(run.id)
                          }}
                          className="rounded-md px-2 py-1 text-red-700 hover:bg-red-50 hover:text-red-800"
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

      <Modal
        open={Boolean(selectedRun)}
        onOpenChange={open => {
          if (!open) {
            setSelectedRun(null)
          }
        }}
      >
        {selectedRun && (
          <ModalContent className="max-h-[90vh] w-[min(94vw,64rem)] overflow-y-auto">
            <ModalHeader>
              <ModalTitle>Run diagnostics</ModalTitle>
              <ModalDescription>
                {selectedRun.workloadName} revision {selectedRun.workloadRevision} on {selectedRun.targetNodeHostnames.join(', ')}
              </ModalDescription>
            </ModalHeader>

            <div className="grid grid-cols-1 gap-4 px-4 md:grid-cols-3">
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                <p className="text-xs uppercase tracking-wide text-[var(--text-soft)]">Run ID</p>
                <p className="mt-1 font-mono text-xs text-[var(--text-strong)]">{selectedRun.id}</p>
              </div>
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                <p className="text-xs uppercase tracking-wide text-[var(--text-soft)]">Mode</p>
                <p className="mt-1 text-sm text-[var(--text-strong)]">{selectedRun.mode}</p>
              </div>
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                <p className="text-xs uppercase tracking-wide text-[var(--text-soft)]">Status</p>
                <p className="mt-1 text-sm text-[var(--text-strong)]">{selectedRun.status}</p>
              </div>
            </div>

            <div className="mt-4 px-4 text-sm">
              <p className="text-[var(--text-soft)]">
                <span className="font-medium text-[var(--text-strong)]">Reason:</span> {selectedRun.diagnostics?.reasonCode ?? 'n/a'}
              </p>
              {selectedRun.diagnostics?.lastMessage && (
                <p className="mt-2 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-[var(--text-soft)]">
                  {selectedRun.diagnostics.lastMessage}
                </p>
              )}
            </div>

            <div className="mt-5 px-4 pb-2">
              <h4 className="text-sm font-semibold uppercase tracking-wide text-[var(--text-soft)]">Timeline stream</h4>
              <div className="mt-3 rounded-xl border border-slate-800 bg-slate-950 p-3 font-mono text-xs text-slate-200">
                <div className="mb-3 flex items-center gap-2 text-[11px] text-slate-400">
                  <span className="inline-block h-2 w-2 rounded-full bg-emerald-400" />
                  workload run timeline
                </div>
                <div className="max-h-80 space-y-2 overflow-y-auto pr-1">
                  {selectedRun.timeline.map(item => (
                    <div key={`${selectedRun.id}-${item.sequence}`} className="rounded-md border border-slate-800 bg-slate-900/80 px-2 py-1.5">
                      <p className="text-slate-300">
                        [{String(item.sequence).padStart(2, '0')}] {item.messageType} #{item.packageIndex} {item.stepId}
                      </p>
                      <p className="mt-1 text-[11px] text-slate-400">
                        {item.status} • {formatTimestamp(item.at)} • {item.detail}
                      </p>
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <ModalFooter className="px-4 pb-4 pt-2 sm:flex-row sm:justify-end">
              <button
                onClick={() => setSelectedRun(null)}
                className="rounded-lg bg-[var(--surface-muted)] px-4 py-2 text-sm font-medium text-[var(--text-soft)] hover:bg-[var(--surface-border)]"
              >
                Close
              </button>
            </ModalFooter>
          </ModalContent>
        )}
      </Modal>
    </div>
  )
}
