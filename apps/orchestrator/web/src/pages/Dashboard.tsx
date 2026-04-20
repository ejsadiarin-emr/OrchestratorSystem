import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Modal, ModalContent, ModalDescription, ModalHeader, ModalTitle } from '../components/ui/modal'
import { getOrchestratorHomeData } from '../services/api'
import type { DashboardNodeRow, OrchestratorHomeData } from '../types'
import { InfoHint } from './dashboard/InfoHint'
import type { InfoHintKey } from './dashboard/infoHints'
import { RowTrigger } from './dashboard/RowTrigger'
import type { WorkloadRow } from './dashboard/models'
import { buildWorkloadRows } from './dashboard/workloadRows'

const EVENT_FILTERS = ['all', 'critical', 'high', 'medium', 'info'] as const
type EventFilter = (typeof EVENT_FILTERS)[number]
const AUTO_REFRESH_MS = 15_000

type DrawerState = { kind: 'node'; nodeId: string } | { kind: 'workload'; workloadName: string } | null

export default function Dashboard() {
  const [data, setData] = useState<OrchestratorHomeData | null>(null)
  const [selectedNodeId, setSelectedNodeId] = useState('')
  const [drawerState, setDrawerState] = useState<DrawerState>(null)
  const [eventFilter, setEventFilter] = useState<EventFilter>('all')
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const loadRequestIdRef = useRef(0)
  const refreshRequestIdRef = useRef(0)

  const applyHomeData = useCallback(
    (result: OrchestratorHomeData, preserveSelection: boolean) => {
      setData(result)
      setLastUpdatedAt(new Date().toISOString())
      setSelectedNodeId(current => {
        if (preserveSelection && current && result.nodes.some(node => node.nodeId === current)) {
          return current
        }
        return result.selectedNodeId || result.nodes[0]?.nodeId || ''
      })
      setError(null)
    },
    [],
  )

  const loadHomeData = useCallback(async () => getOrchestratorHomeData(), [])

  const refresh = useCallback(async () => {
    const requestId = ++refreshRequestIdRef.current
    setRefreshing(true)
    try {
      const result = await loadHomeData()
      if (requestId !== refreshRequestIdRef.current) {
        return
      }
      applyHomeData(result, true)
    } catch {
      if (requestId !== refreshRequestIdRef.current) {
        return
      }
      setError('Failed to load orchestrator home data.')
    } finally {
      if (requestId === refreshRequestIdRef.current) {
        setRefreshing(false)
      }
    }
  }, [applyHomeData, loadHomeData])

  useEffect(() => {
    let active = true

    const requestId = ++loadRequestIdRef.current

    loadHomeData()
      .then(result => {
        if (!active || requestId !== loadRequestIdRef.current) {
          return
        }
        applyHomeData(result, false)
        setLoading(false)
      })
      .catch(() => {
        if (active && requestId === loadRequestIdRef.current) {
          setError('Failed to load orchestrator home data.')
          setLoading(false)
        }
      })

    return () => {
      active = false
    }
  }, [applyHomeData, loadHomeData])

  useEffect(() => {
    const timer = window.setInterval(() => {
      void refresh()
    }, AUTO_REFRESH_MS)

    return () => {
      window.clearInterval(timer)
    }
  }, [refresh])

  const selectedNode = useMemo<DashboardNodeRow | null>(
    () => data?.nodes.find(node => node.nodeId === selectedNodeId) ?? null,
    [data?.nodes, selectedNodeId],
  )

  const selectedNodePrimaryWorkload = useMemo(() => {
    if (!selectedNode) {
      return null
    }
    return {
      name: selectedNode.assignedWorkload,
      revision: selectedNode.workloadRevision,
      runState: selectedNode.runState,
    }
  }, [selectedNode])

  const workloadRows = useMemo<WorkloadRow[]>(() => buildWorkloadRows(data), [data])

  const filteredEvents = useMemo(() => {
    if (!data) {
      return []
    }
    if (eventFilter === 'all') {
      return data.events
    }
    return data.events.filter(event => event.severity === eventFilter)
  }, [data, eventFilter])

  const drawerNode = useMemo<DashboardNodeRow | null>(() => {
    if (!data || !drawerState || drawerState.kind !== 'node') {
      return null
    }
    return data.nodes.find(node => node.nodeId === drawerState.nodeId) ?? null
  }, [data, drawerState])

  const nodeDrawerLogs = useMemo(() => {
    if (!data || !drawerState || drawerState.kind !== 'node') {
      return []
    }
    return data.logsByNodeId[drawerState.nodeId] || []
  }, [data, drawerState])

  const drawerWorkload = useMemo<WorkloadRow | null>(() => {
    if (!drawerState || drawerState.kind !== 'workload') {
      return null
    }
    return workloadRows.find(workload => workload.name === drawerState.workloadName) ?? null
  }, [drawerState, workloadRows])

  const workloadDrawerNodes = useMemo(() => {
    if (!data || !drawerState || drawerState.kind !== 'workload') {
      return []
    }

    return data.nodes
      .map(node => {
        if (node.assignedWorkload !== drawerState.workloadName) {
          return null
        }

        return {
          nodeId: node.nodeId,
          health: node.health,
          revision: node.workloadRevision,
          runState: node.runState,
          revisionUpdateAvailable: node.revisionUpdateAvailable,
          packageUpdatesAvailable: node.packageUpdatesAvailable,
          packageUpdateCount: node.packageUpdateCount ?? 0,
        }
      })
      .filter((item): item is NonNullable<typeof item> => Boolean(item))
  }, [data, drawerState])

  const workloadRevisionUpdateNodes = useMemo(
    () => workloadDrawerNodes.filter(node => node.revisionUpdateAvailable),
    [workloadDrawerNodes],
  )

  const workloadPackageSignalNodes = useMemo(
    () => workloadDrawerNodes.filter(node => node.packageUpdatesAvailable),
    [workloadDrawerNodes],
  )

  const openNodeDrawer = (nodeId: string) => {
    setSelectedNodeId(nodeId)
    setDrawerState({ kind: 'node', nodeId })
  }

  const openWorkloadDrawer = (workloadName: string) => {
    setDrawerState({ kind: 'workload', workloadName })
  }

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  if (!data) {
    return (
      <div className="text-center py-8 text-[var(--status-danger-text)]">Failed to load dashboard data.</div>
    )
  }

  return (
    <div className="space-y-6">
      <header className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-strong)]">Node Operations Overview</h1>
          <p className="mt-2 text-sm text-[var(--text-soft)]">
            Workload-first triage surface for node health, run actions, and node-level evidence.
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-4 text-xs text-[var(--text-soft)]">
            <span>Auto-refresh: every 15s</span>
            <span>Last updated: {lastUpdatedAt ? new Date(lastUpdatedAt).toLocaleTimeString() : 'not yet'}</span>
          </div>
        </div>
        <button
          onClick={() => refresh()}
          disabled={refreshing}
          className="mt-4 rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
        >
          {refreshing ? 'Refreshing...' : 'Refresh Home'}
        </button>
      </header>

      {error && (
        <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-4 py-3 text-sm text-[var(--status-danger-text)]">
          {error}
        </div>
      )}

      <div className="grid grid-cols-2 gap-2 md:grid-cols-3 xl:grid-cols-6">
        <KpiCard
          label="Nodes Online"
          value={`${data.kpis.nodesOnline}`}
          detail={`${data.kpis.nodesOnline + data.kpis.nodesOffline} enrolled nodes`}
        />
        <KpiCard label="Nodes Offline" value={`${data.kpis.nodesOffline}`} detail="Node availability incidents" />
        <KpiCard
          label="Workload Definitions"
          value={`${data.kpis.workloadDefinitions}`}
          detail={`${workloadRows.length} active on nodes`}
        />
        <KpiCard label="Running Workloads" value={`${data.kpis.runningWorkloads}`} detail={`${data.kpis.activeRuns24h} runs / 24h`} />
        <KpiCard
          label="Pending Approvals"
          value={`${data.kpis.pendingApprovals}`}
          detail="Operator action required"
          hintKey="pendingApprovals"
        />
        <KpiCard label="Artifacts Stored" value={`${data.kpis.artifactsStored}`} detail="Backing workload packages" />
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1fr_320px]">
        <div className="space-y-6">
          <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="text-base font-semibold text-[var(--text-strong)]">Nodes Live Table</h2>
              <p className="text-xs text-[var(--text-soft)]">Filters: all nodes, all workloads</p>
            </div>
            <div className="mt-4 overflow-x-auto">
              <table aria-label="Nodes Live Table" className="min-w-full divide-y divide-[var(--surface-border)] text-sm">
                <thead>
                  <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                    <th scope="col" className="px-3 py-2">Node</th>
                    <th scope="col" className="px-3 py-2">Health</th>
                    <th scope="col" className="px-3 py-2">Workload Count</th>
                    <th scope="col" className="px-3 py-2">Workload Set</th>
                    <th scope="col" className="px-3 py-2">Workload Updates</th>
                    <th scope="col" className="px-3 py-2">Check-in</th>
                    <th scope="col" className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        <span>Risk (Node)</span>
                        <InfoHint label="Risk (Node)" hintKey="riskNode" />
                      </div>
                    </th>
                    <th scope="col" className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        <span>Reason</span>
                        <InfoHint label="Reason" hintKey="reason" />
                      </div>
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--surface-border)]">
                  {data.nodes.map(node => (
                    <tr
                      key={node.nodeId}
                      onClick={() => openNodeDrawer(node.nodeId)}
                      className={`cursor-pointer ${selectedNodeId === node.nodeId ? 'bg-[var(--surface-subtle)]' : 'hover:bg-[var(--surface-subtle)]'}`}
                    >
                      <td className="px-3 py-2 font-medium text-[var(--text-strong)]">
                        <RowTrigger
                          label={`Open node details ${node.nodeId}`}
                          onActivate={() => openNodeDrawer(node.nodeId)}
                          className="w-full text-left focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent)]"
                        >
                          {node.nodeId}
                        </RowTrigger>
                      </td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.health}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.assignedWorkload ? 1 : 0}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">
                        <p className="truncate">
                          {node.assignedWorkload ? `${node.assignedWorkload} (${node.workloadRevision})` : '-'}
                        </p>
                      </td>
                      <td className="px-3 py-2">
                        <div className="flex flex-col items-start gap-1">
                          <IndicatorBadge
                            active={node.revisionUpdateAvailable}
                            label="Revision update"
                            inactiveLabel="Revision current"
                          />
                          <IndicatorBadge
                            active={node.packageUpdatesAvailable}
                            label={node.packageUpdateCount ? `Package updates (${node.packageUpdateCount})` : 'Package updates'}
                            inactiveLabel="Packages current"
                          />
                        </div>
                      </td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.lastCheckInAge}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.riskLevel}</td>
                      <td className="px-3 py-2 font-mono text-xs text-[var(--text-soft)]">{node.reasonCode || '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="text-base font-semibold text-[var(--text-strong)]">Workloads Overview</h2>
              <p className="text-xs text-[var(--text-soft)]">
                Workloads are first-class: package and revision update pressure is tracked at workload scope.
              </p>
            </div>
            <div className="mt-4 overflow-x-auto">
              <table aria-label="Workloads Overview" className="min-w-full divide-y divide-[var(--surface-border)] text-sm">
                <thead>
                  <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                    <th scope="col" className="px-3 py-2">Workload</th>
                    <th scope="col" className="px-3 py-2">Version</th>
                    <th scope="col" className="px-3 py-2">Nodes Assigned</th>
                    <th scope="col" className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        <span>Nodes Running</span>
                        <InfoHint label="Nodes Running" hintKey="nodesRunning" />
                      </div>
                    </th>
                    <th scope="col" className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        <span>Revision Updates</span>
                        <InfoHint label="Revision Updates" hintKey="revisionUpdates" />
                      </div>
                    </th>
                    <th scope="col" className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        <span>Package Update Signals</span>
                        <InfoHint label="Package Update Signals" hintKey="packageUpdateSignals" />
                      </div>
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--surface-border)]">
                  {workloadRows.map(workload => (
                    <tr
                      key={workload.name}
                      onClick={() => openWorkloadDrawer(workload.name)}
                      className="cursor-pointer hover:bg-[var(--surface-subtle)]"
                    >
                      <td className="px-3 py-2 font-medium text-[var(--text-strong)]">
                        <RowTrigger
                          label={`Open workload details ${workload.name}`}
                          onActivate={() => openWorkloadDrawer(workload.name)}
                          className="w-full text-left focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent)]"
                        >
                          {workload.name}
                        </RowTrigger>
                      </td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{workload.revisionsLabel}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{workload.nodesAssigned}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{workload.runningNodes}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{workload.nodesWithRevisionUpdates}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{workload.packageUpdateSignals}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
            <h2 className="text-base font-semibold text-[var(--text-strong)]">Action Panel</h2>
            {!selectedNode ? (
              <p className="mt-3 text-sm text-[var(--text-soft)]">Select a node to enable run actions.</p>
            ) : (
              <div className="mt-3 space-y-3 text-sm">
                <p className="font-medium text-[var(--text-strong)]">Selected Node: {selectedNode.nodeId}</p>
                <p className="text-[var(--text-soft)]">
                  Workload: {selectedNode.assignedWorkload} ({selectedNode.workloadRevision})
                </p>
                <p className="text-[var(--text-soft)]">
                  Primary Run State: {selectedNodePrimaryWorkload ? selectedNodePrimaryWorkload.runState : 'n/a'}
                </p>
                <div className="flex flex-wrap gap-2">
                  <button className="rounded-lg bg-[var(--accent)] px-3 py-2 text-xs font-medium text-white">Start Update</button>
                  <button className="rounded-lg bg-amber-500 px-3 py-2 text-xs font-medium text-white">Approve Risky Update</button>
                  <button className="rounded-lg bg-red-600 px-3 py-2 text-xs font-medium text-white">Cancel Run</button>
                  <button className="rounded-lg bg-slate-600 px-3 py-2 text-xs font-medium text-white">Open Run Timeline</button>
                </div>
              </div>
            )}
          </section>
        </div>

        <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
          <h2 className="text-base font-semibold text-[var(--text-strong)]">Important Events</h2>
          <div className="mt-3 flex flex-wrap gap-2">
            {EVENT_FILTERS.map(filter => (
              <button
                key={filter}
                type="button"
                onClick={() => setEventFilter(filter)}
                className={`rounded-full px-3 py-1 text-xs font-medium ${
                  eventFilter === filter
                    ? 'bg-[var(--accent)] text-white'
                    : 'bg-[var(--surface-subtle)] text-[var(--text-soft)] hover:bg-[var(--surface-muted)]'
                }`}
              >
                {filter}
              </button>
            ))}
          </div>
          {filteredEvents.length === 0 ? (
            <p className="mt-3 text-sm text-[var(--text-soft)]">No critical events in selected time range.</p>
          ) : (
            <ul className="mt-4 space-y-3">
              {filteredEvents.map(event => (
                <li key={event.id} className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                  <div className="flex items-center justify-between gap-3">
                    <p className="text-sm font-medium text-[var(--text-strong)]">{event.title}</p>
                    <span className="text-xs uppercase tracking-wide text-[var(--text-soft)]">{event.severity}</span>
                  </div>
                  <p className="mt-1 text-sm text-[var(--text-soft)]">{event.detail}</p>
                  <p className="mt-2 text-xs text-[var(--text-soft)]">{event.ageLabel}</p>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <Modal
        open={drawerState !== null}
        onOpenChange={open => {
          if (!open) {
            setDrawerState(null)
          }
        }}
      >
        <ModalContent className="w-[min(96vw,64rem)] max-h-[90vh] overflow-y-auto bg-[var(--surface)] p-0">
          {drawerState?.kind === 'node' && (
            <>
              <ModalHeader>
                <ModalTitle>Node details</ModalTitle>
                {!drawerNode ? (
                  <ModalDescription>Selected node is no longer available. Close popup and retry.</ModalDescription>
                ) : (
                  <ModalDescription>
                    {drawerNode.nodeId} ({drawerNode.hostname})
                  </ModalDescription>
                )}
              </ModalHeader>
              {drawerNode && (
                <div className="flex flex-wrap gap-2 px-4 pb-2 pt-1">
                  <StatusChip label={`Health: ${drawerNode.health}`} tone={drawerNode.health === 'offline' ? 'danger' : 'neutral'} />
                  <StatusChip label={`Risk: ${drawerNode.riskLevel}`} tone={drawerNode.riskLevel === 'high' ? 'danger' : 'neutral'} />
                  <StatusChip label={`Revision update: ${drawerNode.revisionUpdateAvailable ? 'Yes' : 'No'}`} tone={drawerNode.revisionUpdateAvailable ? 'warning' : 'neutral'} />
                  <StatusChip
                    label={`Package signals: ${drawerNode.packageUpdatesAvailable ? 'Yes' : 'No'}`}
                    tone={drawerNode.packageUpdatesAvailable ? 'warning' : 'neutral'}
                  />
                </div>
              )}
              {!drawerNode ? (
                <div className="px-4 pb-4 text-sm text-[var(--text-soft)]">Node context unavailable after refresh.</div>
              ) : (
                <div className="space-y-4 px-4 pb-4">
                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm">
                    <p className="font-medium text-[var(--text-strong)]">Workload signals</p>
                    <p className="mt-2 text-[var(--text-soft)]">
                      {drawerNode.assignedWorkload} | rev {drawerNode.workloadRevision} | run-state {drawerNode.runState}
                    </p>
                  </section>

                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--text-soft)]">
                    <p className="flex items-center gap-1">
                      <span>
                        Risk (Node): <span className="font-medium text-[var(--text-strong)]">{drawerNode.riskLevel}</span>
                      </span>
                      <InfoHint label="Risk (Node)" hintKey="riskNode" />
                    </p>
                    <p className="mt-1 flex items-center gap-1">
                      <span>
                        Reason: <span className="font-mono text-xs">{drawerNode.reasonCode || '-'}</span>
                      </span>
                      <InfoHint label="Reason" hintKey="reason" />
                    </p>
                    <div className="mt-2 flex flex-wrap gap-2">
                      <IndicatorBadge
                        active={drawerNode.revisionUpdateAvailable}
                        label="Revision update"
                        inactiveLabel="Revision current"
                      />
                      <IndicatorBadge
                        active={drawerNode.packageUpdatesAvailable}
                        label={
                          drawerNode.packageUpdateCount
                            ? `Package updates (${drawerNode.packageUpdateCount})`
                            : 'Package updates'
                        }
                        inactiveLabel="Packages current"
                      />
                    </div>
                  </section>

                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm">
                    <p className="font-medium text-[var(--text-strong)]">Mini logs</p>
                    {nodeDrawerLogs.length === 0 ? (
                      <p className="mt-2 text-[var(--text-soft)]">No actionable lines for this node context.</p>
                    ) : (
                      <ul className="mt-2 space-y-2 font-mono text-xs">
                        {nodeDrawerLogs.map(line => (
                          <li
                            key={line.id}
                            className={`rounded-lg border px-3 py-2 ${
                              line.level === 'error'
                                ? 'border-red-400/40 bg-[#1f0f12] text-red-100'
                                : line.level === 'warn'
                                ? 'border-amber-400/40 bg-[#1f1a0f] text-amber-100'
                                : 'border-slate-500/30 bg-[#111827] text-slate-100'
                            }`}
                          >
                            <p className="opacity-80">[{line.at}] {line.level.toUpperCase()}</p>
                            <p className="mt-1">{line.message}</p>
                          </li>
                        ))}
                      </ul>
                    )}
                  </section>

                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm">
                    <p className="font-medium text-[var(--text-strong)]">Action controls</p>
                    <div className="mt-3 flex flex-wrap gap-2">
                      <button className="rounded-lg bg-[var(--accent)] px-3 py-2 text-xs font-medium text-white">Start Update</button>
                      <button className="rounded-lg bg-amber-500 px-3 py-2 text-xs font-medium text-white">Approve Risky Update</button>
                      <button className="rounded-lg bg-red-600 px-3 py-2 text-xs font-medium text-white">Cancel Run</button>
                      <button className="rounded-lg bg-slate-600 px-3 py-2 text-xs font-medium text-white">Open Run Timeline</button>
                    </div>
                  </section>
                </div>
              )}
            </>
          )}

          {drawerState?.kind === 'workload' && (
            <>
              <ModalHeader>
                <ModalTitle>Workload details</ModalTitle>
                {!drawerWorkload ? (
                  <ModalDescription>Selected workload is no longer available. Close popup and retry.</ModalDescription>
                ) : (
                  <ModalDescription>{drawerWorkload.name}</ModalDescription>
                )}
              </ModalHeader>
              {drawerWorkload && (
                <div className="flex flex-wrap gap-2 px-4 pb-2 pt-1">
                  <StatusChip
                    label={`Mixed revisions: ${drawerWorkload.mixedRevisions ? 'Yes' : 'No'}`}
                    tone={drawerWorkload.mixedRevisions ? 'warning' : 'neutral'}
                  />
                  <StatusChip
                    label={`Revision updates: ${workloadRevisionUpdateNodes.length} nodes`}
                    tone={workloadRevisionUpdateNodes.length > 0 ? 'warning' : 'neutral'}
                  />
                  <StatusChip
                    label={`Package signals: ${workloadPackageSignalNodes.length} nodes`}
                    tone={workloadPackageSignalNodes.length > 0 ? 'warning' : 'neutral'}
                  />
                </div>
              )}
              {!drawerWorkload ? (
                <div className="px-4 pb-4 text-sm text-[var(--text-soft)]">Workload context unavailable after refresh.</div>
              ) : (
                <div className="space-y-4 px-4 pb-4">
                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--text-soft)]">
                    <p>
                      Version:{' '}
                      <span className="font-medium text-[var(--text-strong)]">{drawerWorkload.revisionsLabel}</span>
                    </p>
                    <p className="mt-1">
                      Nodes assigned: <span className="font-medium text-[var(--text-strong)]">{drawerWorkload.nodesAssigned}</span>
                    </p>
                    <p>
                      Nodes running: <span className="font-medium text-[var(--text-strong)]">{drawerWorkload.runningNodes}</span>
                    </p>
                    <p className="mt-1">
                      Revision updates: <span className="font-medium text-[var(--text-strong)]">{drawerWorkload.nodesWithRevisionUpdates}</span>
                    </p>
                    <p>
                      Mixed revisions detected:{' '}
                      <span className="font-medium text-[var(--text-strong)]">{drawerWorkload.mixedRevisions ? 'Yes' : 'No'}</span>
                    </p>
                    <p>
                      Package update signals:{' '}
                      <span className="font-medium text-[var(--text-strong)]">{drawerWorkload.packageUpdateSignals}</span>
                    </p>
                  </section>

                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm">
                    <p className="font-medium text-[var(--text-strong)]">Impacted-node snapshots</p>
                    <div className="mt-3 space-y-3 text-[var(--text-soft)]">
                      <div>
                        <p className="text-xs uppercase tracking-wide">Revision update contributors</p>
                        {workloadRevisionUpdateNodes.length === 0 ? (
                          <p className="mt-1">No nodes currently reporting revision update availability.</p>
                        ) : (
                          <ul className="mt-2 space-y-1">
                            {workloadRevisionUpdateNodes.map(node => (
                              <li key={`${drawerWorkload.name}-${node.nodeId}-revision-update`}>{node.nodeId}</li>
                            ))}
                          </ul>
                        )}
                      </div>
                      <div>
                        <p className="text-xs uppercase tracking-wide">Package update signal nodes</p>
                        {workloadPackageSignalNodes.length === 0 ? (
                          <p className="mt-1">No nodes currently reporting package update signals.</p>
                        ) : (
                          <ul className="mt-2 space-y-1">
                            {workloadPackageSignalNodes.map(node => (
                              <li key={`${drawerWorkload.name}-${node.nodeId}-package-signal`}>
                                {node.nodeId} | package signals {node.packageUpdateCount}
                              </li>
                            ))}
                          </ul>
                        )}
                      </div>
                    </div>
                  </section>

                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm">
                    <p className="font-medium text-[var(--text-strong)]">Nodes assigned and run state</p>
                    {workloadDrawerNodes.length === 0 ? (
                      <p className="mt-2 text-[var(--text-soft)]">No nodes currently assigned.</p>
                    ) : (
                      <ul className="mt-2 space-y-2 text-[var(--text-soft)]">
                        {workloadDrawerNodes.map(node => (
                          <li
                            key={`${drawerWorkload.name}-${node.nodeId}`}
                            className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                          >
                            <p className="font-medium text-[var(--text-strong)]">{node.nodeId}</p>
                            <p className="mt-1 text-xs">
                              revision {node.revision} | run-state {node.runState} | health {node.health}
                            </p>
                          </li>
                        ))}
                      </ul>
                    )}
                  </section>

                  <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--text-soft)]">
                    Package update signals are derived from node telemetry and may lag artifact-store truth.
                  </section>
                </div>
              )}
            </>
          )}
        </ModalContent>
      </Modal>
    </div>
  )
}

function KpiCard({
  label,
  value,
  detail,
  hintKey,
}: {
  label: string
  value: string
  detail: string
  hintKey?: InfoHintKey
}) {
  return (
    <section className="rounded-xl border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2 shadow-[var(--surface-shadow)]">
      <p className="flex items-center gap-1 text-[11px] uppercase tracking-wide text-[var(--text-soft)]">
        <span>{label}</span>
        {hintKey ? <InfoHint label={label} hintKey={hintKey} /> : null}
      </p>
      <p className="mt-1 text-lg font-semibold leading-tight text-[var(--text-strong)]">{value}</p>
      <p className="mt-1 text-[11px] text-[var(--text-soft)]">{detail}</p>
    </section>
  )
}

function IndicatorBadge({
  active,
  label,
  inactiveLabel,
}: {
  active: boolean
  label: string
  inactiveLabel: string
}) {
  if (active) {
    return (
      <span className="rounded-full border border-[var(--status-warning-border)] bg-[var(--status-warning-bg)] px-2 py-0.5 text-xs font-medium text-[var(--status-warning-text)]">
        {label}
      </span>
    )
  }
  return (
    <span className="rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-xs text-[var(--text-soft)]">
      {inactiveLabel}
    </span>
  )
}

function StatusChip({ label, tone }: { label: string; tone: 'neutral' | 'warning' | 'danger' }) {
  const toneClass =
    tone === 'warning'
      ? 'border-[var(--status-warning-border)] bg-[var(--status-warning-bg)] text-[var(--status-warning-text)]'
      : tone === 'danger'
      ? 'border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] text-[var(--status-danger-text)]'
      : 'border-[var(--surface-border)] bg-[var(--surface-muted)] text-[var(--text-soft)]'

  return <span className={`rounded-full border px-2 py-0.5 text-xs font-medium ${toneClass}`}>{label}</span>
}
