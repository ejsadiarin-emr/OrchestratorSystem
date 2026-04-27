import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  cancelWorkloadRun,
  createWorkloadRun,
  getWorkload,
  getWorkloadRunSteps,
  listNodes,
  listWorkloadRuns,
  listWorkloads,
} from '../services/api'
import { subscribeToRunProgress } from '../services/realtime'
import type { Node, WorkloadDefinition, WorkloadRevision, WorkloadRun, WorkloadRunStatus } from '../types'
import { Modal, ModalContent, ModalDescription, ModalFooter, ModalHeader, ModalTitle } from '../components/ui/modal'

const filterValues: Array<WorkloadRunStatus | 'all'> = [
  'all',
  'queued',
  'running',
  'completed',
  'failed',
  'cancelled',
]

const runModes: Array<WorkloadRun['mode']> = ['install', 'update', 'rollback']

export default function WorkloadRuns() {
  const [runs, setRuns] = useState<WorkloadRun[]>([])
  const [nodes, setNodes] = useState<Node[]>([])
  const [workloads, setWorkloads] = useState<WorkloadDefinition[]>([])
  const [filter, setFilter] = useState<WorkloadRunStatus | 'all'>('all')
  const [selectedRun, setSelectedRun] = useState<WorkloadRun | null>(null)
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const [form, setForm] = useState({
    workloadId: '',
    revisionId: '',
    mode: 'install' as WorkloadRun['mode'],
    targetNodeIds: [] as string[],
    forceInstall: false,
  })
  const [workloadDetails, setWorkloadDetails] = useState<(WorkloadDefinition & { revisions: WorkloadRevision[] }) | null>(null)
  const [nodeFilter, setNodeFilter] = useState('')
  const [formErrors, setFormErrors] = useState<Record<string, string>>({})
  const [showSummary, setShowSummary] = useState(false)
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
  }, [filter])

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
    const active = runs.filter(run => run.status === 'queued' || run.status === 'running')
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

  const openCreateModal = useCallback(() => {
    const defaultWorkload = workloads[0]
    setForm({
      workloadId: defaultWorkload?.id ?? '',
      revisionId: '',
      mode: 'install',
      targetNodeIds: [],
      forceInstall: false,
    })
    setWorkloadDetails(null)
    setNodeFilter('')
    setFormErrors({})
    setShowSummary(false)
    setSubmitting(false)
    setIsCreateModalOpen(true)
    setSuccess(null)

    if (defaultWorkload) {
      getWorkload(defaultWorkload.id)
        .then(w => {
          setWorkloadDetails(w)
          const published = w.revisions
            .filter(r => r.state === 'published')
            .sort((a, b) => {
              const aTime = new Date(a.publishedAt ?? a.createdAt ?? 0).getTime()
              const bTime = new Date(b.publishedAt ?? b.createdAt ?? 0).getTime()
              return bTime - aTime
            })
          if (published[0]) {
            setForm(current => ({ ...current, revisionId: published[0].id }))
          }
        })
        .catch(() => {
          setFormErrors(current => ({ ...current, workloadId: 'Failed to load workload details' }))
        })
    }
  }, [workloads])

  const handleWorkloadChange = useCallback(async (workloadId: string) => {
    setForm(current => ({ ...current, workloadId, revisionId: '' }))
    setFormErrors(current => ({ ...current, workloadId: '' }))
    setWorkloadDetails(null)

    if (!workloadId) return

    try {
      const w = await getWorkload(workloadId)
      setWorkloadDetails(w)
      const published = w.revisions
        .filter(r => r.state === 'published')
        .sort((a, b) => {
          const aTime = new Date(a.publishedAt ?? a.createdAt ?? 0).getTime()
          const bTime = new Date(b.publishedAt ?? b.createdAt ?? 0).getTime()
          return bTime - aTime
        })
      if (published[0]) {
        setForm(current => ({ ...current, revisionId: published[0].id }))
      }
    } catch {
      setFormErrors(current => ({ ...current, workloadId: 'Failed to load workload details' }))
    }
  }, [])

  const publishedRevisions = useMemo(() => {
    if (!workloadDetails) return []
    return workloadDetails.revisions
      .filter(r => r.state === 'published')
      .sort((a, b) => {
        const aTime = new Date(a.publishedAt ?? a.createdAt ?? 0).getTime()
        const bTime = new Date(b.publishedAt ?? b.createdAt ?? 0).getTime()
        return bTime - aTime
      })
  }, [workloadDetails])

  const filteredNodes = useMemo(() => {
    const filterText = nodeFilter.toLowerCase()
    return nodes
      .filter(
        node =>
          node.hostname.toLowerCase().includes(filterText) ||
          node.displayName.toLowerCase().includes(filterText),
      )
      .sort((a, b) => {
        const aOnline = a.status === 'online' ? 1 : 0
        const bOnline = b.status === 'online' ? 1 : 0
        if (aOnline !== bOnline) return bOnline - aOnline
        return a.hostname.localeCompare(b.hostname)
      })
  }, [nodes, nodeFilter])

  const onlineNodeIds = useMemo(
    () => new Set(nodes.filter(n => n.status === 'online').map(n => n.id)),
    [nodes],
  )

  const selectedOnlineNodeIds = useMemo(
    () => form.targetNodeIds.filter(id => onlineNodeIds.has(id)),
    [form.targetNodeIds, onlineNodeIds],
  )

  const validateForm = useCallback((): boolean => {
    const errors: Record<string, string> = {}
    if (!form.workloadId) errors.workloadId = 'Select a workload'
    if (!form.revisionId) errors.revisionId = 'Select a published revision'
    if (selectedOnlineNodeIds.length === 0) errors.targetNodeIds = 'Select at least one online node'
    setFormErrors(errors)
    return Object.keys(errors).length === 0
  }, [form.workloadId, form.revisionId, selectedOnlineNodeIds.length])

  const handleCreateRun = async (event: React.FormEvent) => {
    event.preventDefault()
    setFormErrors(current => ({ ...current, submit: '' }))

    if (!showSummary) {
      if (validateForm()) {
        setShowSummary(true)
      }
      return
    }

    setSubmitting(true)
    try {
      await createWorkloadRun(form)
      await refresh(filter)
      setIsCreateModalOpen(false)
      setSuccess('Workload run created successfully.')
      setError(null)
    } catch (creationError) {
      const message =
        creationError instanceof Error ? creationError.message : 'Failed to create workload run.'
      setFormErrors(current => ({ ...current, submit: message }))
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
    queued: 'border-amber-200 bg-amber-50 text-amber-700',
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
      {(error || success) && (
        <div
          className={`rounded-lg border px-4 py-3 text-sm ${
            success
              ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
              : 'border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] text-[var(--status-danger-text)]'
          }`}
        >
          {success ?? error}
        </div>
      )}

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h2 className="text-lg font-semibold text-[var(--text-strong)]">Create Workload Run</h2>
        <p className="mt-1 text-sm text-[var(--text-soft)]">
          Start a run from a centered popup with workload, revision, mode, and target selection.
        </p>
        <button
          type="button"
          onClick={openCreateModal}
          className="mt-4 rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
        >
          Open Run Creator
        </button>
      </section>

      <Modal open={isCreateModalOpen} onOpenChange={setIsCreateModalOpen}>
        <ModalContent className="w-[min(92vw,48rem)] max-h-[90vh] overflow-y-auto">
          <ModalHeader>
            <ModalTitle>Create Workload Run</ModalTitle>
            <ModalDescription>Configure and launch a workload run across selected nodes.</ModalDescription>
          </ModalHeader>
          <form onSubmit={handleCreateRun} className="space-y-4 px-4 pb-4">
            {formErrors.submit && (
              <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
                {formErrors.submit}
              </div>
            )}

            {showSummary ? (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-4 space-y-3">
                <h3 className="text-sm font-semibold text-[var(--text-strong)]">Confirm Run</h3>
                <div className="grid grid-cols-2 gap-2 text-sm">
                  <div>
                    <span className="text-[var(--text-soft)]">Workload:</span>
                    <p className="font-medium text-[var(--text-strong)]">
                      {workloads.find(w => w.id === form.workloadId)?.name ?? form.workloadId}
                    </p>
                  </div>
                  <div>
                    <span className="text-[var(--text-soft)]">Revision:</span>
                    <p className="font-medium text-[var(--text-strong)]">
                      {publishedRevisions.find(r => r.id === form.revisionId)?.revision ?? form.revisionId}
                    </p>
                  </div>
                  <div>
                    <span className="text-[var(--text-soft)]">Mode:</span>
                    <p className="font-medium text-[var(--text-strong)] uppercase">{form.mode}</p>
                  </div>
                  <div>
                    <span className="text-[var(--text-soft)]">Nodes:</span>
                    <p className="font-medium text-[var(--text-strong)]">{selectedOnlineNodeIds.length} selected</p>
                  </div>
                </div>
                <div className="text-xs text-[var(--text-soft)]">
                  Target nodes:{" "}
                  {nodes
                    .filter(n => form.targetNodeIds.includes(n.id))
                    .map(n => n.hostname)
                    .join(', ')}
                </div>
              </div>
            ) : (
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <div className="space-y-4">
                  <label className="block text-sm text-[var(--text-soft)]">
                    Workload
                    <select
                      value={form.workloadId}
                      onChange={event => handleWorkloadChange(event.target.value)}
                      className={`mt-1 w-full rounded-lg border px-3 py-2 ${
                        formErrors.workloadId ? 'border-rose-300' : 'border-[var(--surface-border)]'
                      }`}
                    >
                      <option value="">Select workload...</option>
                      {workloads.map(workload => (
                        <option key={workload.id} value={workload.id}>
                          {workload.name}
                        </option>
                      ))}
                    </select>
                    {formErrors.workloadId && (
                      <span className="mt-1 block text-xs text-rose-600">{formErrors.workloadId}</span>
                    )}
                  </label>

                  <label className="block text-sm text-[var(--text-soft)]">
                    Revision
                    <select
                      value={form.revisionId}
                      onChange={event => {
                        setForm(current => ({ ...current, revisionId: event.target.value }))
                        setFormErrors(current => ({ ...current, revisionId: '' }))
                      }}
                      disabled={publishedRevisions.length === 0}
                      className={`mt-1 w-full rounded-lg border px-3 py-2 ${
                        formErrors.revisionId ? 'border-rose-300' : 'border-[var(--surface-border)]'
                      } disabled:opacity-50`}
                    >
                      {publishedRevisions.length === 0 ? (
                        <option value="">No published revisions</option>
                      ) : (
                        publishedRevisions.map(revision => (
                          <option key={revision.id} value={revision.id}>
                            {revision.revision}
                          </option>
                        ))
                      )}
                    </select>
                    {formErrors.revisionId && (
                      <span className="mt-1 block text-xs text-rose-600">{formErrors.revisionId}</span>
                    )}
                  </label>

                  <label className="block text-sm text-[var(--text-soft)]">
                    Mode
                    <select
                      value={form.mode}
                      onChange={event =>
                        setForm(current => ({ ...current, mode: event.target.value as WorkloadRun['mode'] }))
                      }
                      className="mt-1 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
                    >
                      {runModes.map(mode => (
                        <option key={mode} value={mode}>
                          {mode}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label className="flex items-center gap-2 text-sm text-[var(--text-soft)]">
                    <input
                      type="checkbox"
                      checked={form.forceInstall}
                      onChange={event =>
                        setForm(current => ({ ...current, forceInstall: event.target.checked }))
                      }
                      className="h-4 w-4 rounded border-[var(--surface-border)]"
                    />
                    <span>Force reinstall</span>
                  </label>
                </div>

                <div className="space-y-3">
                  <div className="flex items-center justify-between">
                    <label className="text-sm text-[var(--text-soft)]">Target nodes</label>
                    <div className="flex gap-2 text-xs">
                      <button
                        type="button"
                        onClick={() => {
                          const onlineIds = filteredNodes.filter(n => n.status === 'online').map(n => n.id)
                          setForm(current => ({ ...current, targetNodeIds: onlineIds }))
                          setFormErrors(current => ({ ...current, targetNodeIds: '' }))
                        }}
                        className="text-[var(--accent)] hover:underline"
                      >
                        Select all online
                      </button>
                      <span className="text-[var(--text-soft)]">|</span>
                      <button
                        type="button"
                        onClick={() => setForm(current => ({ ...current, targetNodeIds: [] }))}
                        className="text-[var(--accent)] hover:underline"
                      >
                        Clear all
                      </button>
                    </div>
                  </div>

                  <input
                    type="text"
                    placeholder="Filter nodes..."
                    value={nodeFilter}
                    onChange={event => setNodeFilter(event.target.value)}
                    className="w-full rounded-lg border border-[var(--surface-border)] px-3 py-2 text-sm"
                  />

                  <div
                    className={`max-h-48 overflow-y-auto rounded-lg border p-2 space-y-1 ${
                      formErrors.targetNodeIds ? 'border-rose-300' : 'border-[var(--surface-border)]'
                    }`}
                  >
                    {filteredNodes.length === 0 ? (
                      <p className="px-2 py-4 text-center text-sm text-[var(--text-soft)]">
                        No nodes match filter
                      </p>
                    ) : (
                      filteredNodes.map(node => {
                        const isOnline = node.status === 'online'
                        const isSelected = form.targetNodeIds.includes(node.id)
                        return (
                          <label
                            key={node.id}
                            className={`flex items-center gap-3 rounded-md px-2 py-2 text-sm ${
                              isOnline
                                ? 'cursor-pointer hover:bg-[var(--surface-subtle)]'
                                : 'opacity-50 cursor-not-allowed'
                            }`}
                          >
                            <input
                              type="checkbox"
                              checked={isSelected}
                              disabled={!isOnline}
                              aria-label={node.displayName || node.hostname}
                              onChange={event => {
                                if (!isOnline) return
                                const newIds = event.target.checked
                                  ? [...form.targetNodeIds, node.id]
                                  : form.targetNodeIds.filter(id => id !== node.id)
                                setForm(current => ({ ...current, targetNodeIds: newIds }))
                                setFormErrors(current => ({ ...current, targetNodeIds: '' }))
                              }}
                              className="h-4 w-4 rounded border-[var(--surface-border)]"
                            />
                            <span
                              className={`inline-block h-2 w-2 rounded-full ${
                                isOnline ? 'bg-emerald-500' : 'bg-slate-400'
                              }`}
                            />
                            <span className="flex-1 font-medium text-[var(--text-strong)]">
                              {node.displayName || node.hostname}
                            </span>
                            {node.osVersion && (
                              <span className="rounded-full border border-[var(--surface-border)] bg-[var(--surface)] px-2 py-0.5 text-[10px] uppercase tracking-wide text-[var(--text-soft)]">
                                {node.osVersion.split(' ')[0]}
                              </span>
                            )}
                          </label>
                        )
                      })
                    )}
                  </div>
                  {formErrors.targetNodeIds && (
                    <span className="block text-xs text-rose-600">{formErrors.targetNodeIds}</span>
                  )}
                </div>
              </div>
            )}

            <ModalFooter className="px-0 pb-0 pt-2 sm:flex-row sm:justify-end gap-2">
              {showSummary && (
                <button
                  type="button"
                  onClick={() => setShowSummary(false)}
                  className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
                >
                  Back
                </button>
              )}
              <button
                type="button"
                onClick={() => setIsCreateModalOpen(false)}
                className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={submitting || publishedRevisions.length === 0 || (!showSummary && selectedOnlineNodeIds.length === 0)}
                className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
              >
                {submitting ? 'Creating...' : showSummary ? 'Confirm Create Run' : 'Create Run'}
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
          <div className="overflow-visible">
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
                      <span
                        className={`rounded-full border px-2 py-1 text-xs font-medium uppercase tracking-wide ${statusClasses[run.status]}`}
                      >
                        {run.status}
                      </span>
                    </td>
                    <td className="rounded-r-xl px-4 py-3 text-right">
                      {(run.status === 'running' || run.status === 'queued') && (
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
                {selectedRun.workloadName} revision {selectedRun.workloadRevision} on{' '}
                {selectedRun.targetNodeHostnames.join(', ')}
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
                <span className="font-medium text-[var(--text-strong)]">Reason:</span>{' '}
                {selectedRun.diagnostics?.reasonCode ?? 'n/a'}
              </p>
              {selectedRun.diagnostics?.lastMessage && (
                <p className="mt-2 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-[var(--text-soft)]">
                  {selectedRun.diagnostics.lastMessage}
                </p>
              )}
            </div>

            <div className="mt-5 px-4 pb-2">
              <h4 className="text-sm font-semibold uppercase tracking-wide text-[var(--text-soft)]">
                Timeline stream
              </h4>
              <div className="mt-3 rounded-xl border border-slate-800 bg-slate-950 p-3 font-mono text-xs text-slate-200">
                <div className="mb-3 flex items-center gap-2 text-[11px] text-slate-400">
                  <span className="inline-block h-2 w-2 rounded-full bg-emerald-400" />
                  workload run timeline
                </div>
                <div className="max-h-80 space-y-2 overflow-y-auto pr-1">
                  {selectedRun.timeline.map(item => (
                    <div
                      key={`${selectedRun.id}-${item.sequence}`}
                      className="rounded-md border border-slate-800 bg-slate-900/80 px-2 py-1.5"
                    >
                      <p className="text-slate-300">
                        [{String(item.sequence).padStart(2, '0')}] {item.messageType} #{item.packageIndex}{' '}
                        {item.stepId}
                      </p>
                      <p className="mt-1 text-[11px] text-slate-400">
                        {item.status} &bull; {formatTimestamp(item.at)} &bull; {item.detail}
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
