import { useCallback, useEffect, useMemo, useState } from 'react'
import { getOrchestratorHomeData } from '../services/api'
import type { DashboardNodeRow, OrchestratorHomeData } from '../types'

const EVENT_FILTERS = ['all', 'critical', 'high', 'medium', 'info'] as const
type EventFilter = (typeof EVENT_FILTERS)[number]
const AUTO_REFRESH_MS = 15_000

export default function Dashboard() {
  const [data, setData] = useState<OrchestratorHomeData | null>(null)
  const [selectedNodeId, setSelectedNodeId] = useState('')
  const [eventFilter, setEventFilter] = useState<EventFilter>('all')
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setRefreshing(true)
    try {
      const result = await getOrchestratorHomeData()
      setData(result)
      setLastUpdatedAt(new Date().toISOString())
      setSelectedNodeId(current => {
        if (current && result.nodes.some(node => node.nodeId === current)) {
          return current
        }
        return result.selectedNodeId || result.nodes[0]?.nodeId || ''
      })
      setError(null)
    } catch {
      setError('Failed to load orchestrator home data.')
    } finally {
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    let active = true

    getOrchestratorHomeData()
      .then(result => {
        if (!active) {
          return
        }

        setData(result)
        setLastUpdatedAt(new Date().toISOString())
        setSelectedNodeId(result.selectedNodeId || result.nodes[0]?.nodeId || '')
        setLoading(false)
      })
      .catch(() => {
        if (active) {
          setError('Failed to load orchestrator home data.')
          setLoading(false)
        }
      })

    return () => {
      active = false
    }
  }, [])

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

  const nodeLogs = useMemo(
    () => (selectedNodeId && data?.logsByNodeId[selectedNodeId]) || [],
    [data?.logsByNodeId, selectedNodeId],
  )

  const filteredEvents = useMemo(() => {
    if (!data) {
      return []
    }
    if (eventFilter === 'all') {
      return data.events
    }
    return data.events.filter(event => event.severity === eventFilter)
  }, [data, eventFilter])

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
          <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-strong)]">Orchestrator Home</h1>
          <p className="mt-2 text-sm text-[var(--text-soft)]">
            Workload-first triage surface for fleet health, run actions, and node-level evidence.
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

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Fleet Online / Offline" value={`${data.kpis.fleetOnline} / ${data.kpis.fleetOffline}`} />
        <KpiCard label="Workload Definitions" value={`${data.kpis.workloadDefinitions}`} />
        <KpiCard label="Running Workloads" value={`${data.kpis.runningWorkloads}`} />
        <KpiCard
          label="Active + Failed Runs (24h)"
          value={`${data.kpis.activeRuns24h} + ${data.kpis.failedRuns24h}`}
        />
        <KpiCard label="Pending Approvals" value={`${data.kpis.pendingApprovals}`} />
        <KpiCard label="Control-plane Latency (p95)" value={`${data.kpis.controlPlaneLatencyP95Ms} ms`} />
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-[260px_1fr_320px]">
        <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
          <h2 className="text-base font-semibold text-[var(--text-strong)]">Filter Rail</h2>
          <div className="mt-4 space-y-4 text-sm text-[var(--text-soft)]">
            <FilterStub label="Site" value="All Sites" />
            <FilterStub label="Workload" value="All Workloads" />
            <FilterStub label="Node Status" value="online | warning | offline" />
            <FilterStub label="Run Status" value="active + pending-approval + failed" />
            <FilterStub label="Time Range" value="Last 24h" />
          </div>
        </section>

        <div className="space-y-6">
          <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
            <h2 className="text-base font-semibold text-[var(--text-strong)]">Nodes Live Table</h2>
            <div className="mt-4 overflow-x-auto">
              <table className="min-w-full divide-y divide-[var(--surface-border)] text-sm">
                <thead>
                  <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                    <th className="px-3 py-2">Node</th>
                    <th className="px-3 py-2">Health</th>
                    <th className="px-3 py-2">Assigned Workload</th>
                    <th className="px-3 py-2">Revision</th>
                    <th className="px-3 py-2">Run State</th>
                    <th className="px-3 py-2">Check-in</th>
                    <th className="px-3 py-2">Risk</th>
                    <th className="px-3 py-2">Updates</th>
                    <th className="px-3 py-2">Reason</th>
                    <th className="px-3 py-2 text-right">Select</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--surface-border)]">
                  {data.nodes.map(node => (
                    <tr
                      key={node.nodeId}
                      aria-selected={selectedNodeId === node.nodeId}
                      className={selectedNodeId === node.nodeId ? 'bg-[var(--surface-subtle)]' : ''}
                    >
                      <td className="px-3 py-2 font-medium text-[var(--text-strong)]">{node.nodeId}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.health}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.assignedWorkload}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.workloadRevision}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.runState}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.lastCheckInAge}</td>
                      <td className="px-3 py-2 text-[var(--text-soft)]">{node.riskLevel}</td>
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
                      <td className="px-3 py-2 font-mono text-xs text-[var(--text-soft)]">{node.reasonCode || '-'}</td>
                      <td className="px-3 py-2 text-right">
                        <button
                          type="button"
                          onClick={() => setSelectedNodeId(node.nodeId)}
                          className="text-sm text-[var(--accent)] hover:text-[var(--accent-strong)]"
                          aria-label={`Select ${node.nodeId}`}
                        >
                          Select
                        </button>
                      </td>
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
                <p className="text-[var(--text-soft)]">Run State: {selectedNode.runState}</p>
                <div className="flex flex-wrap gap-2">
                  <button className="rounded-lg bg-[var(--accent)] px-3 py-2 text-xs font-medium text-white">Start Update</button>
                  <button className="rounded-lg bg-amber-500 px-3 py-2 text-xs font-medium text-white">Approve Risky Update</button>
                  <button className="rounded-lg bg-red-600 px-3 py-2 text-xs font-medium text-white">Cancel Run</button>
                  <button className="rounded-lg bg-slate-600 px-3 py-2 text-xs font-medium text-white">Open Run Timeline</button>
                </div>
              </div>
            )}
          </section>

          <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
            <h2 className="text-base font-semibold text-[var(--text-strong)]">Mini Log Viewer</h2>
            {nodeLogs.length === 0 ? (
              <p className="mt-3 text-sm text-[var(--text-soft)]">No actionable lines for this node context.</p>
            ) : (
              <ul className="mt-3 space-y-2 text-sm">
                {nodeLogs.map(line => (
                  <li key={line.id} className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2">
                    <p className="font-mono text-xs text-[var(--text-soft)]">{line.at}</p>
                    <p className="mt-1 text-[var(--text-strong)]">{line.message}</p>
                  </li>
                ))}
              </ul>
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
    </div>
  )
}

function KpiCard({ label, value }: { label: string; value: string }) {
  return (
    <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
      <p className="text-xs uppercase tracking-wide text-[var(--text-soft)]">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-[var(--text-strong)]">{value}</p>
    </section>
  )
}

function FilterStub({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2">
      <p className="text-xs uppercase tracking-wide">{label}</p>
      <p className="mt-1 text-[var(--text-strong)]">{value}</p>
    </div>
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
