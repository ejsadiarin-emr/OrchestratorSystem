import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  cancelWorkloadRun,
  createWorkloadRun,
  downloadWorkloadRunReport,
  getInstalledRevisions,
  getWorkload,
  getWorkloadRunSteps,
  listNodes,
  listNodeWorkloadStates,
  listWorkloadRuns,
  listWorkloads,
  runNodesPreCheckSummary,
} from '../services/api'
import { subscribeToRunProgress } from '../services/realtime'
import type { EligibilityStatus, Node, NodeWorkloadState, PreCheckAction, PreCheckSummaryNode, WorkloadDefinition, WorkloadRevision, WorkloadRun, WorkloadRunStatus } from '../types'
import { isDowngrade, isSequentialUpgrade } from '../utils/versionComparison'
import { Modal, ModalContent, ModalDescription, ModalFooter, ModalHeader, ModalTitle } from '../components/ui/modal'
import { Stepper } from '../components/ui/stepper'

const filterValues: Array<WorkloadRunStatus | 'all'> = [
  'all',
  'queued',
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
  const [success, setSuccess] = useState<string | null>(null)

  const [form, setForm] = useState({
    workloadId: '',
    revisionId: '',
    mode: 'install' as WorkloadRun['mode'],
    targetNodeIds: [] as string[],
    reinstall: false,
  })
  const [workloadDetails, setWorkloadDetails] = useState<(WorkloadDefinition & { revisions: WorkloadRevision[] }) | null>(null)
  const [nodeFilter, setNodeFilter] = useState('')
  const [formErrors, setFormErrors] = useState<Record<string, string>>({})
  const [wizardStep, setWizardStep] = useState(0)
  const [submitting, setSubmitting] = useState(false)
  const [nodeWorkloadStates, setNodeWorkloadStates] = useState<NodeWorkloadState[]>([])
  const [uninstallConfirmed, setUninstallConfirmed] = useState(false)
  const [preCheckSummary, setPreCheckSummary] = useState<PreCheckSummaryNode[] | null>(null)
  const [preCheckSummaryLoading, setPreCheckSummaryLoading] = useState(false)
  const [installedRevisions, setInstalledRevisions] = useState<WorkloadRevision[]>([])

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

  useEffect(() => {
    setPreCheckSummary(null)
  }, [form.workloadId, form.revisionId, form.targetNodeIds])

  const openCreateModal = useCallback(() => {
    const defaultWorkload = workloads[0]
    setForm({
      workloadId: defaultWorkload?.id ?? '',
      revisionId: '',
      mode: 'install',
      targetNodeIds: [],
      reinstall: false,
    })
    setWorkloadDetails(null)
    setNodeFilter('')
    setFormErrors({})
    setWizardStep(0)
    setSubmitting(false)
    setIsCreateModalOpen(true)
    setSuccess(null)
    setNodeWorkloadStates([])
    setUninstallConfirmed(false)
    setInstalledRevisions([])
    setPreCheckSummary(null)
    setPreCheckSummaryLoading(false)

    if (defaultWorkload) {
      Promise.all([
        getWorkload(defaultWorkload.id),
        listNodeWorkloadStates(),
        getInstalledRevisions(defaultWorkload.id),
      ])
        .then(([w, states, installed]) => {
          setWorkloadDetails(w)
          setNodeWorkloadStates(states)
          setInstalledRevisions(installed)
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
    setNodeWorkloadStates([])
    setInstalledRevisions([])

    if (!workloadId) return

    try {
      const [w, states, installed] = await Promise.all([
        getWorkload(workloadId),
        listNodeWorkloadStates(),
        getInstalledRevisions(workloadId),
      ])
      setWorkloadDetails(w)
      setNodeWorkloadStates(states)
      setInstalledRevisions(installed)
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

  const onlineNodeIds = useMemo(
    () => new Set(nodes.filter(n => n.status === 'online').map(n => n.id)),
    [nodes],
  )

  const selectedOnlineNodeIds = useMemo(
    () => form.targetNodeIds.filter(id => onlineNodeIds.has(id)),
    [form.targetNodeIds, onlineNodeIds],
  )

  const nodeWorkloadStateByNodeId = useMemo(() => {
    const map = new Map<string, NodeWorkloadState>()
    for (const s of nodeWorkloadStates) {
      if (s.workloadId === form.workloadId) {
        if (form.mode === 'uninstall' && form.revisionId) {
          if (s.currentRevisionId === form.revisionId) {
            map.set(s.nodeId, s)
          }
        } else {
          map.set(s.nodeId, s)
        }
      }
    }
    return map
  }, [nodeWorkloadStates, form.workloadId, form.mode, form.revisionId])

  const publishedRevisionVersions = useMemo(
    () => publishedRevisions.map(r => r.revision),
    [publishedRevisions],
  )

  const selectedRevision = useMemo(() => {
    return publishedRevisions.find(r => r.id === form.revisionId)
  }, [publishedRevisions, form.revisionId])

  const nodeEligibility = useMemo(() => {
    const map = new Map<string, EligibilityStatus>()
    for (const node of nodes) {
      const nodeState = nodeWorkloadStateByNodeId.get(node.id)
      const installedVersion = nodeState?.workloadRevision
      const isExactRevision = nodeState?.currentRevisionId === form.revisionId

      if (form.mode === 'uninstall') {
        if (nodeWorkloadStateByNodeId.has(node.id)) {
          map.set(node.id, { kind: 'eligible', action: 'Uninstall' })
        } else {
          map.set(node.id, { kind: 'ineligible', reason: 'WrongVersion' })
        }
        continue
      }

      // Install mode
      if (!installedVersion) {
        map.set(node.id, { kind: 'eligible', action: 'FreshInstall' })
      } else if (isExactRevision) {
        if (form.reinstall) {
          map.set(node.id, { kind: 'eligible', action: 'Reinstall' })
        } else {
          map.set(node.id, { kind: 'eligible', action: 'AlreadyCurrent' })
        }
      } else if (isDowngrade(installedVersion, selectedRevision?.revision ?? '')) {
        map.set(node.id, { kind: 'ineligible', reason: 'Downgrade' })
      } else if (
        isSequentialUpgrade(
          installedVersion,
          selectedRevision?.revision ?? '',
          publishedRevisionVersions,
        )
      ) {
        map.set(node.id, { kind: 'eligible', action: 'SequentialUpdate' })
      } else {
        map.set(node.id, { kind: 'ineligible', reason: 'VersionJump' })
      }
    }
    return map
  }, [
    nodes,
    nodeWorkloadStateByNodeId,
    form.mode,
    form.revisionId,
    form.reinstall,
    selectedRevision,
    publishedRevisionVersions,
  ])

  useEffect(() => {
    // Auto-deselect ineligible nodes when eligibility changes
    setForm(current => {
      const eligibleIds = new Set(
        nodes
          .filter(n => {
            const eligibility = nodeEligibility.get(n.id)
            return eligibility?.kind === 'eligible'
          })
          .map(n => n.id),
      )
      const newTargetNodeIds = current.targetNodeIds.filter(id => eligibleIds.has(id))
      if (newTargetNodeIds.length !== current.targetNodeIds.length) {
        return { ...current, targetNodeIds: newTargetNodeIds }
      }
      return current
    })
  }, [nodeEligibility, nodes])

  const uninstallNodes = useMemo(
    () => nodes.filter(n => n.status === 'online' && nodeWorkloadStateByNodeId.has(n.id)),
    [nodes, nodeWorkloadStateByNodeId],
  )

  const filteredNodes = useMemo(() => {
    const filterText = nodeFilter.toLowerCase()
    let result = nodes

    if (form.mode === 'uninstall') {
      result = result.filter(node => nodeWorkloadStateByNodeId.has(node.id))
    }

    return result
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
  }, [nodes, nodeFilter, form.mode, nodeWorkloadStateByNodeId])

  const anySelectedNodeHasRevision = useMemo(() => {
    if (!selectedRevision) return false
    return form.targetNodeIds.some(nodeId => {
      const state = nodeWorkloadStateByNodeId.get(nodeId)
      return state && state.currentRevisionId === selectedRevision.id
    })
  }, [form.targetNodeIds, selectedRevision, nodeWorkloadStateByNodeId])

  useEffect(() => {
    if (wizardStep !== 3) return
    if (preCheckSummary !== null) return
    if (selectedOnlineNodeIds.length === 0 || !form.workloadId || !form.revisionId) return

    async function run() {
      setPreCheckSummaryLoading(true)
      setFormErrors(current => ({ ...current, submit: '' }))
      try {
        const results = await runNodesPreCheckSummary(selectedOnlineNodeIds, form.workloadId, form.revisionId)
        setPreCheckSummary(results)
      } catch (e) {
        setFormErrors(current => ({
          ...current,
          submit: e instanceof Error ? e.message : 'Failed to run pre-check summary',
        }))
      } finally {
        setPreCheckSummaryLoading(false)
      }
    }

    run()
  }, [wizardStep, preCheckSummary, selectedOnlineNodeIds, form.workloadId, form.revisionId])

  const canAdvance = useCallback((step: number): boolean => {
    switch (step) {
      case 0:
        return true
      case 1:
        return !!form.workloadId && !!form.revisionId
      case 2:
        return selectedOnlineNodeIds.length > 0
      case 3:
        if (preCheckSummaryLoading) return false
        if (!preCheckSummary) return false
        return !preCheckSummary.some(
          n => n.action === 'BlockedDowngrade' || n.action === 'BlockedVersionJump',
        )
      case 4:
        return true
      default:
        return false
    }
  }, [form.workloadId, form.revisionId, selectedOnlineNodeIds.length, preCheckSummaryLoading, preCheckSummary])

  const handleNext = () => {
    if (wizardStep < 4 && canAdvance(wizardStep)) {
      setWizardStep(wizardStep + 1)
    }
  }

  const handleBack = () => {
    if (wizardStep > 0) {
      setWizardStep(wizardStep - 1)
      if (wizardStep === 4) {
        setUninstallConfirmed(false)
      }
    }
  }

  const handleStepClick = (stepIndex: number) => {
    if (stepIndex < wizardStep) {
      setWizardStep(stepIndex)
      if (stepIndex < 4) {
        setUninstallConfirmed(false)
      }
    }
  }

  const handleCreateRun = async (event?: React.FormEvent) => {
    event?.preventDefault()
    if (submitting) return
    setFormErrors(current => ({ ...current, submit: '' }))

    if (wizardStep !== 4) return

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

  const downloadReport = async (runId: string) => {
    try {
      const text = await downloadWorkloadRunReport(runId)
      const blob = new Blob([text], { type: 'text/plain' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `deployment-report-${runId}.txt`
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch (downloadError) {
      setError(downloadError instanceof Error ? downloadError.message : 'Failed to download report.')
    }
  }

  const statusClasses: Record<WorkloadRunStatus, string> = {
    queued: 'border-amber-200 bg-amber-50 text-amber-700',
    running: 'border-cyan-200 bg-cyan-50 text-cyan-700',
    completed: 'border-emerald-200 bg-emerald-50 text-emerald-700',
    failed: 'border-rose-200 bg-rose-50 text-rose-700',
    cancelled: 'border-slate-200 bg-slate-100 text-slate-700',
  }

  const actionBadgeClasses = (action: PreCheckAction): string => {
    switch (action) {
      case 'Skip':
        return 'bg-slate-100 text-slate-600'
      case 'FreshInstall':
        return 'bg-emerald-100 text-emerald-700'
      case 'Update':
        return 'bg-blue-100 text-blue-700'
      case 'InstallMissing':
        return 'bg-amber-100 text-amber-700'
      case 'BlockedDowngrade':
        return 'bg-red-100 text-red-700'
      case 'BlockedVersionJump':
        return 'bg-orange-100 text-orange-700'
      case 'Reinstall':
        return 'bg-purple-100 text-purple-700'
      case 'Unknown':
        return 'bg-gray-100 text-gray-500'
      default:
        return 'bg-slate-100 text-slate-600'
    }
  }

  const formatAction = (action: PreCheckAction): string => {
    return action.replace(/([A-Z])/g, ' $1').trim()
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
        <ModalContent className="w-[min(92vw,56rem)] max-h-[90vh] overflow-y-auto">
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

            <Stepper
              steps={[
                { id: 'mode', label: 'Mode' },
                { id: 'workload', label: 'Workload & Version' },
                { id: 'nodes', label: 'Nodes' },
                { id: 'prechecks', label: 'Pre-Checks' },
                { id: 'confirm', label: 'Confirm' },
              ]}
              activeStep={wizardStep}
              onStepClick={handleStepClick}
              className="mb-4"
            />

            {wizardStep === 0 && (
              <div className="flex gap-3">
                <button
                  type="button"
                  onClick={() => {
                    setUninstallConfirmed(false)
                    setForm(current => ({ ...current, mode: 'install' }))
                  }}
                  className={`rounded-lg px-4 py-2 text-sm font-medium border-2 transition ${
                    form.mode === 'install'
                      ? 'border-[var(--accent)] bg-[var(--accent)] text-white'
                      : 'border-[var(--surface-border)] bg-[var(--surface)] text-[var(--text-soft)] hover:border-[var(--accent)] hover:text-[var(--accent)]'
                  }`}
                >
                  Install
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setUninstallConfirmed(false)
                    setForm(current => {
                      const qualifyingIds = new Set(uninstallNodes.map(n => n.id))
                      return {
                        ...current,
                        mode: 'uninstall',
                        revisionId: installedRevisions[0]?.id ?? '',
                        targetNodeIds: current.targetNodeIds.filter(id => qualifyingIds.has(id)),
                      }
                    })
                  }}
                  className={`rounded-lg px-4 py-2 text-sm font-medium border-2 transition ${
                    form.mode === 'uninstall'
                      ? 'border-[var(--status-danger-border)] bg-[var(--status-danger-border)] text-white'
                      : 'border-[var(--surface-border)] bg-[var(--surface)] text-[var(--text-soft)] hover:border-[var(--status-danger-border)] hover:text-[var(--status-danger-text)]'
                  }`}
                >
                  Uninstall
                </button>
              </div>
            )}

            {wizardStep === 1 && (
              <div className="grid grid-cols-1 gap-4">
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
                  {form.mode === 'uninstall' ? 'Installed Revision' : 'Revision'}
                  <select
                    value={form.revisionId}
                    onChange={event => {
                      setForm(current => ({ ...current, revisionId: event.target.value }))
                      setFormErrors(current => ({ ...current, revisionId: '' }))
                    }}
                    disabled={form.mode === 'uninstall' ? installedRevisions.length === 0 : publishedRevisions.length === 0}
                    className={`mt-1 w-full rounded-lg border px-3 py-2 ${
                      formErrors.revisionId ? 'border-rose-300' : 'border-[var(--surface-border)]'
                    } disabled:opacity-50`}
                  >
                    {form.mode === 'uninstall' ? (
                      installedRevisions.length === 0 ? (
                        <option value="">No installed revisions</option>
                      ) : (
                        installedRevisions.map(revision => (
                          <option key={revision.id} value={revision.id}>
                            {revision.revision}
                          </option>
                        ))
                      )
                    ) : (
                      publishedRevisions.length === 0 ? (
                        <option value="">No published revisions</option>
                      ) : (
                        publishedRevisions.map(revision => (
                          <option key={revision.id} value={revision.id}>
                            {revision.revision}
                          </option>
                        ))
                      )
                    )}
                  </select>
                  {formErrors.revisionId && (
                    <span className="mt-1 block text-xs text-rose-600">{formErrors.revisionId}</span>
                  )}
                </label>
              </div>
            )}

            {wizardStep === 2 && (
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <label className="text-sm text-[var(--text-soft)]">Target nodes</label>
                  <div className="flex gap-2 text-xs">
                    <button
                      type="button"
                      onClick={() => {
                        const eligibleOnlineIds = filteredNodes
                          .filter(n => {
                            const eligibility = nodeEligibility.get(n.id)
                            return n.status === 'online' && eligibility?.kind === 'eligible'
                          })
                          .map(n => n.id)
                        setForm(current => ({ ...current, targetNodeIds: eligibleOnlineIds }))
                        setFormErrors(current => ({ ...current, targetNodeIds: '' }))
                      }}
                      className="text-[var(--accent)] hover:underline"
                    >
                      Select all eligible online
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
                  className={`max-h-64 overflow-y-auto rounded-lg border p-2 space-y-1 ${
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
                      const nodeState = nodeWorkloadStateByNodeId.get(node.id)
                      const installedVersion = nodeState?.workloadRevision
                      const hasDrift = nodeState && nodeState.packageStatesJson ? true : false
                      const isExactRevision = nodeState?.currentRevisionId === form.revisionId
                      const eligibility = nodeEligibility.get(node.id)
                      const isEligible = eligibility?.kind === 'eligible'

                      const eligibilityBadge = () => {
                        if (!form.workloadId || !form.revisionId) return null
                        if (!eligibility) return null
                        if (eligibility.kind === 'eligible') {
                          switch (eligibility.action) {
                            case 'FreshInstall':
                              return (
                                <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700">
                                  Fresh Install
                                </span>
                              )
                            case 'SequentialUpdate':
                              return (
                                <span className="rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-700">
                                  Update {installedVersion ? `v${installedVersion}→v${selectedRevision?.revision}` : ''}
                                </span>
                              )
                            case 'Reinstall':
                              return (
                                <span className="rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-700">
                                  Reinstall
                                </span>
                              )
                            case 'AlreadyCurrent':
                              return (
                                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                                  Already Current
                                </span>
                              )
                            case 'Uninstall':
                              return (
                                <span className="rounded-full bg-rose-100 px-2 py-0.5 text-xs font-medium text-rose-700">
                                  Uninstall
                                </span>
                              )
                          }
                        } else {
                          switch (eligibility.reason) {
                            case 'Downgrade':
                              return (
                                <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700">
                                  Downgrade Blocked
                                </span>
                              )
                            case 'VersionJump':
                              return (
                                <span className="rounded-full bg-orange-100 px-2 py-0.5 text-xs font-medium text-orange-700">
                                  Skip v{selectedRevision?.revision}
                                </span>
                              )
                            case 'WrongVersion':
                              return (
                                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                                  Wrong Version
                                </span>
                              )
                          }
                        }
                      }

                      const canSelect = isOnline && isEligible

                      return (
                        <label
                          key={node.id}
                          className={`flex items-center gap-3 rounded-md px-2 py-2 text-sm ${
                            canSelect
                              ? 'cursor-pointer hover:bg-[var(--surface-subtle)]'
                              : 'opacity-50 cursor-not-allowed'
                          }`}
                        >
                          <input
                            type="checkbox"
                            checked={isSelected}
                            disabled={!canSelect}
                            aria-label={node.displayName || node.hostname}
                            onChange={event => {
                              if (!canSelect) return
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
                            <span className="rounded-full border border-[var(--surface-border)] bg-[var(--surface)] px-2 py-0.5 text-xs uppercase tracking-wide text-[var(--text-soft)]">
                              {node.osVersion.split(' ')[0]}
                            </span>
                          )}
                          {form.workloadId && installedVersion && isExactRevision && !hasDrift && (
                            <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700">
                              v{installedVersion}
                            </span>
                          )}
                          {form.workloadId && installedVersion && isExactRevision && hasDrift && (
                            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">
                              v{installedVersion}
                            </span>
                          )}
                          {form.workloadId && installedVersion && !isExactRevision && (
                            <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                              v{installedVersion}
                            </span>
                          )}
                          {form.workloadId && !installedVersion && (
                            <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                              not installed
                            </span>
                          )}
                          {eligibilityBadge()}
                        </label>
                      )
                    })
                  )}
                </div>
                {formErrors.targetNodeIds && (
                  <span className="block text-xs text-rose-600">{formErrors.targetNodeIds}</span>
                )}
              </div>
            )}

            {wizardStep === 3 && (
              <div className="space-y-3">
                {preCheckSummaryLoading ? (
                  <div className="flex items-center justify-center py-8">
                    <div className="h-8 w-8 animate-spin rounded-full border-4 border-[var(--surface-border)] border-t-[var(--accent)]" />
                    <span className="ml-3 text-sm text-[var(--text-soft)]">Running pre-checks...</span>
                  </div>
                ) : preCheckSummary ? (
                  <>
                    {(preCheckSummary.some(n => n.action === 'BlockedDowngrade') || preCheckSummary.some(n => n.action === 'BlockedVersionJump')) && (
                      <div className="space-y-1">
                        {preCheckSummary.some(n => n.action === 'BlockedDowngrade') && (
                          <div className="rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
                            Cannot proceed: downgrade detected on {preCheckSummary.filter(n => n.action === 'BlockedDowngrade').length} node(s).
                          </div>
                        )}
                        {preCheckSummary.some(n => n.action === 'BlockedVersionJump') && (
                          <div className="rounded-lg border border-orange-300 bg-orange-50 px-3 py-2 text-sm text-orange-700">
                            Cannot proceed: version jump detected on {preCheckSummary.filter(n => n.action === 'BlockedVersionJump').length} node(s).
                          </div>
                        )}
                      </div>
                    )}
                    <div className="max-h-64 overflow-y-auto rounded-lg border border-[var(--surface-border)] p-2 space-y-1">
                      {preCheckSummary.map(node => (
                        <div key={node.nodeId} className="flex items-center gap-3 rounded-md px-2 py-2 text-sm">
                          <span className="flex-1 font-medium text-[var(--text-strong)]">{node.hostname}</span>
                          <span title={node.actionDetail || ''} className={`rounded-full px-2 py-0.5 text-xs font-medium ${actionBadgeClasses(node.action)}`}>
                            {formatAction(node.action)}
                          </span>
                        </div>
                      ))}
                    </div>
                    <button
                      type="button"
                      disabled={preCheckSummaryLoading}
                      onClick={async () => {
                        setPreCheckSummaryLoading(true)
                        setFormErrors(current => ({ ...current, submit: '' }))
                        try {
                          const results = await runNodesPreCheckSummary(selectedOnlineNodeIds, form.workloadId, form.revisionId)
                          setPreCheckSummary(results)
                        } catch (e) {
                          setFormErrors(current => ({
                            ...current,
                            submit: e instanceof Error ? e.message : 'Failed to run pre-check summary',
                          }))
                        } finally {
                          setPreCheckSummaryLoading(false)
                        }
                      }}
                      className="w-full rounded-lg border border-[var(--surface-border)] px-3 py-2.5 text-xs font-medium text-[var(--accent)] hover:bg-[var(--surface-subtle)] hover:border-[var(--accent)] disabled:opacity-50"
                    >
                      Re-run Pre-Checks
                    </button>
                  </>
                ) : (
                  <div className="space-y-3">
                    <p className="text-center text-sm text-[var(--text-soft)] py-4">
                      Click the button below to run pre-checks on the selected nodes.
                    </p>
                    <button
                      type="button"
                      disabled={preCheckSummaryLoading || selectedOnlineNodeIds.length === 0}
                      onClick={async () => {
                        setPreCheckSummaryLoading(true)
                        setFormErrors(current => ({ ...current, submit: '' }))
                        try {
                          const results = await runNodesPreCheckSummary(selectedOnlineNodeIds, form.workloadId, form.revisionId)
                          setPreCheckSummary(results)
                        } catch (e) {
                          setFormErrors(current => ({
                            ...current,
                            submit: e instanceof Error ? e.message : 'Failed to run pre-check summary',
                          }))
                        } finally {
                          setPreCheckSummaryLoading(false)
                        }
                      }}
                      className="w-full rounded-lg border border-[var(--surface-border)] px-3 py-2.5 text-xs font-medium text-[var(--accent)] hover:bg-[var(--surface-subtle)] hover:border-[var(--accent)] disabled:opacity-50"
                    >
                      Run Pre-Checks
                    </button>
                  </div>
                )}
              </div>
            )}

            {wizardStep === 4 && (
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
                      {form.mode === 'uninstall'
                        ? installedRevisions.find(r => r.id === form.revisionId)?.revision ?? form.revisionId
                        : publishedRevisions.find(r => r.id === form.revisionId)?.revision ?? form.revisionId}
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
                  Target nodes:{' '}
                  {nodes
                    .filter(n => form.targetNodeIds.includes(n.id))
                    .map(n => n.hostname)
                    .join(', ')}
                </div>

                <div className="space-y-1">
                  {nodes
                    .filter(n => form.targetNodeIds.includes(n.id))
                    .map(n => {
                      const summary = preCheckSummary?.find(s => s.nodeId === n.id)
                      return (
                        <div key={n.id} className="flex items-center gap-2 text-xs">
                          <span className="font-medium text-[var(--text-strong)]">{n.hostname}</span>
                          {summary && (
                            <span title={summary.actionDetail || ''} className={`rounded-full px-2 py-0.5 text-xs font-medium ${actionBadgeClasses(summary.action)}`}>
                              {formatAction(summary.action)}
                            </span>
                          )}
                        </div>
                      )
                    })}
                </div>

                {form.mode === 'uninstall' && selectedRevision ? (
                  <>
                    <div className="rounded-lg border border-red-300 bg-red-50 p-4 space-y-3">
                      <h4 className="text-sm font-bold text-red-800">
                        Warning: This will permanently remove packages from nodes.
                      </h4>
                      <p className="text-sm text-red-700">
                        The following packages will be uninstalled from {selectedOnlineNodeIds.length} node(s):
                      </p>
                      <ul className="list-disc list-inside text-sm text-red-700 space-y-0.5">
                        {(selectedRevision.packageSteps ?? []).map(pkg => (
                          <li key={pkg.packageId}>{pkg.packageName} {pkg.packageVersion}</li>
                        ))}
                      </ul>
                      <div className="space-y-1 text-xs text-red-600">
                        {nodes
                          .filter(n => form.targetNodeIds.includes(n.id))
                          .map(n => {
                            const state = nodeWorkloadStateByNodeId.get(n.id)
                            const installedVersion = state?.workloadRevision
                            const pkgs = (selectedRevision.packageSteps ?? [])
                              .map(p => `${p.packageName} ${p.packageVersion}`)
                              .join(', ')
                            return (
                              <div key={n.id}>
                                {n.hostname} ({installedVersion ? `v${installedVersion}` : 'unknown'}): {pkgs}
                              </div>
                            )
                          })}
                      </div>
                    </div>
                    <label className="flex items-center gap-2 text-sm text-red-800 font-medium">
                      <input
                        type="checkbox"
                        checked={uninstallConfirmed}
                        onChange={event => setUninstallConfirmed(event.target.checked)}
                        className="h-4 w-4 rounded border-red-300"
                      />
                      I understand that this action cannot be undone.
                    </label>
                  </>
                ) : (
                  <>
                    {form.mode === 'install' && anySelectedNodeHasRevision && (
                      <label className="flex items-center gap-2 text-sm text-[var(--text-soft)]">
                        <input
                          type="checkbox"
                          checked={form.reinstall}
                          onChange={event =>
                            setForm(current => ({ ...current, reinstall: event.target.checked }))
                          }
                          className="h-4 w-4 rounded border-[var(--surface-border)]"
                        />
                        <span>Reinstall — force re-install even if already present</span>
                      </label>
                    )}
                  </>
                )}
              </div>
            )}

            <ModalFooter className="px-0 pb-0 pt-2 sm:flex-row sm:justify-end gap-2">
              {wizardStep > 0 && (
                <button
                  type="button"
                  onClick={handleBack}
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
              {wizardStep < 4 ? (
                <button
                  type="button"
                  onClick={handleNext}
                  disabled={!canAdvance(wizardStep)}
                  className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
                >
                  Next
                </button>
              ) : (
                <button
                  type="button"
                  onClick={() => handleCreateRun()}
                  disabled={submitting || (form.mode === 'uninstall' && !uninstallConfirmed)}
                  className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {submitting ? 'Creating...' : 'Confirm Create Run'}
                </button>
              )}
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
                      className={`rounded-md border px-2 py-1.5 ${
                        item.stepId.startsWith('PreInit_') || item.stepId.startsWith('PostInit_') || item.stepId.startsWith('PreWorkload_') || item.stepId.startsWith('PostWorkload_')
                          ? item.status === 'failed'
                            ? 'border-red-800 bg-red-950/60'
                            : item.status === 'running'
                              ? 'border-blue-800 bg-blue-950/60'
                              : 'border-slate-800 bg-slate-900/80'
                          : 'border-slate-800 bg-slate-900/80'
                      }`}
                    >
                      <p className="text-slate-300">
                        [{String(item.sequence).padStart(2, '0')}] {item.messageType} #{item.packageIndex}{' '}
                        <span className={item.stepId.startsWith('PreInit_') || item.stepId.startsWith('PostInit_') || item.stepId.startsWith('PreWorkload_') || item.stepId.startsWith('PostWorkload_')
                            ? 'text-cyan-300'
                            : ''
                        }>{item.stepId}</span>
                      </p>
                      <p className="mt-1 text-[11px] text-slate-400">
                        {item.status} &bull; {formatTimestamp(item.at)} &bull; {item.detail}
                      </p>
                      {item.status === 'failed' && item.detail && (item.stepId.startsWith('PreInit_') || item.stepId.startsWith('PostInit_') || item.stepId.startsWith('PreWorkload_') || item.stepId.startsWith('PostWorkload_')) && (
                        <div className="mt-1.5 rounded bg-slate-950 px-2 py-1 font-mono text-xs text-slate-400 whitespace-pre-wrap max-h-24 overflow-y-auto">
                          {item.detail}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            </div>

            <ModalFooter className="px-4 pb-4 pt-2 sm:flex-row sm:justify-end sm:gap-2">
              {(selectedRun.status === 'completed' || selectedRun.status === 'failed') && (
                <button
                  onClick={() => downloadReport(selectedRun.id)}
                  className="rounded-lg bg-[var(--surface-muted)] px-4 py-2 text-sm font-medium text-[var(--text-soft)] hover:bg-[var(--surface-border)]"
                >
                  Download Report
                </button>
              )}
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
