import { useEffect, useState } from 'react'
import { CheckCircle, AlertTriangle, XCircle, Loader2 } from 'lucide-react'
import { getNodeDetails, runNodePreChecks } from '../services/api'
import type { NodeDetailResponse, PreCheckItem } from '../types'
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalTitle,
  ModalFooter,
} from '../components/ui/modal'

interface NodeDetailsModalProps {
  nodeId: string | null
  open: boolean
  onClose: () => void
}

export default function NodeDetailsModal({ nodeId, open, onClose }: NodeDetailsModalProps) {
  const [data, setData] = useState<NodeDetailResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [runningPreChecks, setRunningPreChecks] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open || !nodeId) {
      setData(null)
      setError(null)
      return
    }

    let cancelled = false

    async function fetchDetails() {
      setLoading(true)
      setError(null)
      try {
        const details = await getNodeDetails(nodeId!)
        if (cancelled) return

        if (!details.latestPreCheck) {
          try {
            const preCheck = await runNodePreChecks(nodeId!)
            if (cancelled) return
            details.latestPreCheck = preCheck
          } catch (preCheckErr) {
            // Don't fail the whole modal if pre-checks fail
            console.error('Failed to run pre-checks:', preCheckErr)
          }
        }

        setData(details)
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load node details')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void fetchDetails()

    return () => {
      cancelled = true
    }
  }, [open, nodeId])

  const handleRunPreChecks = async () => {
    if (!nodeId) return
    setRunningPreChecks(true)
    setError(null)
    try {
      const preCheck = await runNodePreChecks(nodeId)
      setData(prev => (prev ? { ...prev, latestPreCheck: preCheck } : null))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to run pre-checks')
    } finally {
      setRunningPreChecks(false)
    }
  }

  const statusBadgeClass = (status: string) => {
    switch (status) {
      case 'online':
        return 'bg-emerald-100 text-emerald-800'
      case 'offline':
        return 'bg-slate-100 text-slate-800'
      case 'installing':
        return 'bg-amber-100 text-amber-800'
      case 'enrolling':
        return 'bg-blue-100 text-blue-800'
      default:
        return 'bg-[var(--surface-muted)] text-[var(--text-soft)]'
    }
  }

  const preCheckIcon = (status: PreCheckItem['status']) => {
    switch (status) {
      case 'passed':
        return <CheckCircle className="h-5 w-5 text-emerald-500" />
      case 'warning':
        return <AlertTriangle className="h-5 w-5 text-amber-500" />
      case 'failed':
        return <XCircle className="h-5 w-5 text-red-500" />
    }
  }

  return (
    <Modal open={open} onOpenChange={(isOpen) => { if (!isOpen) onClose() }}>
      <ModalContent className="w-[min(92vw,56rem)]">
        {loading ? (
          <div className="flex items-center justify-center py-12">
            <Loader2 className="h-8 w-8 animate-spin text-[var(--accent)]" />
          </div>
        ) : error ? (
          <div className="px-4 py-8 text-center text-sm text-[var(--status-danger-text)]">
            {error}
          </div>
        ) : data ? (
          <>
            <ModalHeader>
              <ModalTitle>{data.displayName || data.hostname}</ModalTitle>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-[var(--text-soft)]">
                <span className="font-mono">{data.hostname}</span>
                <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${statusBadgeClass(data.status)}`}>
                  {data.status}
                </span>
                <span>IP: {data.ipAddress}</span>
                <span>OS: {data.osVersion || '—'}</span>
                <span>Agent: {data.agentVersion || '—'}</span>
              </div>
            </ModalHeader>

            <div className="space-y-6 overflow-y-auto px-4 pb-4">
              {/* Workloads */}
              <section>
                <h4 className="mb-2 text-sm font-semibold text-[var(--text-strong)]">Workloads</h4>
                {data.workloads.length === 0 ? (
                  <p className="text-sm text-[var(--text-soft)]">No workloads assigned.</p>
                ) : (
                  <div className="overflow-x-auto rounded-lg border border-[var(--surface-border)]">
                    <table className="min-w-full divide-y divide-[var(--surface-border)]">
                      <thead className="bg-[var(--surface-subtle)]">
                        <tr>
                          <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Name</th>
                          <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Revision</th>
                          <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Status</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--surface-border)]">
                        {data.workloads.map(w => (
                          <tr key={w.workloadId}>
                            <td className="px-4 py-2 text-sm text-[var(--text-strong)]">{w.name}</td>
                            <td className="px-4 py-2 text-sm font-mono text-[var(--text-soft)]">{w.currentVersion}</td>
                            <td className="px-4 py-2 text-sm text-[var(--text-soft)]">{w.status}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>

              {/* Pre-checks */}
              <section>
                <h4 className="mb-2 text-sm font-semibold text-[var(--text-strong)]">Pre-checks</h4>
                {!data.latestPreCheck ? (
                  <p className="text-sm text-[var(--text-soft)]">No pre-check data available.</p>
                ) : (
                  <div className="space-y-2">
                    <div className="flex items-center gap-2 text-xs text-[var(--text-soft)]">
                      <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        data.latestPreCheck.overallStatus === 'passed'
                          ? 'bg-emerald-100 text-emerald-800'
                          : data.latestPreCheck.overallStatus === 'warning'
                            ? 'bg-amber-100 text-amber-800'
                            : 'bg-red-100 text-red-800'
                      }`}>
                        {data.latestPreCheck.overallStatus}
                      </span>
                      <span>Run at: {new Date(data.latestPreCheck.checkedAt).toLocaleString()}</span>
                    </div>
                    <div className="overflow-x-auto rounded-lg border border-[var(--surface-border)]">
                      <table className="min-w-full divide-y divide-[var(--surface-border)]">
                        <thead className="bg-[var(--surface-subtle)]">
                          <tr>
                            <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Status</th>
                            <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Category</th>
                            <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Name</th>
                            <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Detail</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--surface-border)]">
                          {data.latestPreCheck.items.map((item, idx) => (
                            <tr key={idx}>
                              <td className="px-4 py-2">{preCheckIcon(item.status)}</td>
                              <td className="px-4 py-2 text-sm text-[var(--text-soft)]">{item.category}</td>
                              <td className="px-4 py-2 text-sm text-[var(--text-strong)]">{item.name}</td>
                              <td className="px-4 py-2 text-xs text-[var(--text-soft)]">
                                {item.detail && <div>{item.detail}</div>}
                                {item.actualVersion && (
                                  <div className="mt-0.5 font-mono text-[10px]">
                                    Version: {item.actualVersion}
                                  </div>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}
              </section>
            </div>

            <ModalFooter className="flex-row items-center justify-between gap-3">
              <div className="text-xs text-[var(--text-soft)]">
                {data.lastSeenAt ? `Last seen: ${new Date(data.lastSeenAt).toLocaleString()}` : ''}
              </div>
              <button
                onClick={handleRunPreChecks}
                disabled={runningPreChecks}
                className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-60"
              >
                {runningPreChecks ? (
                  <span className="flex items-center gap-2">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Running...
                  </span>
                ) : (
                  'Run Pre-check'
                )}
              </button>
            </ModalFooter>
          </>
        ) : null}
      </ModalContent>
    </Modal>
  )
}
