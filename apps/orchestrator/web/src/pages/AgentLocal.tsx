import { useEffect, useState } from 'react'
import {
  exportAgentDiagnostics,
  getAgentLocalSummary,
  listAgentLocalLogs,
  runAgentPrecheck,
  startAgentGuidedUpdate,
} from '../services/api'
import type { AgentLocalSummary, MiniLogLine } from '../types'

interface PrecheckMessage {
  kind: 'success' | 'error'
  text: string
}

interface DiagnosticsNotice {
  kind: 'success' | 'error'
  text: string
}

export default function AgentLocal() {
  const [summary, setSummary] = useState<AgentLocalSummary | null>(null)
  const [logs, setLogs] = useState<MiniLogLine[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [confirmUpdate, setConfirmUpdate] = useState(false)
  const [runningPrecheck, setRunningPrecheck] = useState(false)
  const [startingUpdate, setStartingUpdate] = useState(false)
  const [exportingDiagnostics, setExportingDiagnostics] = useState(false)
  const [precheckMessage, setPrecheckMessage] = useState<PrecheckMessage | null>(null)
  const [diagnosticsNotice, setDiagnosticsNotice] = useState<DiagnosticsNotice | null>(null)

  useEffect(() => {
    let active = true

    Promise.all([getAgentLocalSummary(), listAgentLocalLogs()])
      .then(([summaryData, logsData]) => {
        if (!active) {
          return
        }

        setSummary(summaryData)
        setLogs(logsData)
        setError(null)
      })
      .catch(() => {
        if (active) {
          setError('Failed to load agent local console state.')
        }
      })
      .finally(() => {
        if (active) {
          setLoading(false)
        }
      })

    return () => {
      active = false
    }
  }, [])

  const handleRunPrecheck = async () => {
    setRunningPrecheck(true)
    setPrecheckMessage(null)

    try {
      const result = await runAgentPrecheck()
      if (result.passed) {
        setPrecheckMessage({ kind: 'success', text: `Pre-check passed: ${result.detail}` })
      } else {
        setPrecheckMessage({ kind: 'error', text: `Pre-check failed: ${result.detail}` })
      }
    } catch {
      setPrecheckMessage({ kind: 'error', text: 'Pre-check failed: unable to validate prerequisites.' })
    } finally {
      setRunningPrecheck(false)
    }
  }

  const handleStartGuidedUpdate = async () => {
    if (!confirmUpdate) {
      return
    }

    setStartingUpdate(true)

    try {
      const result = await startAgentGuidedUpdate()
      if (result.accepted) {
        setSummary(current => (current ? { ...current, runState: result.status, pendingApproval: false } : current))
      }
    } catch {
      setError('Failed to start guided update for this workload.')
    } finally {
      setStartingUpdate(false)
    }
  }

  const handleExportDiagnostics = async () => {
    setExportingDiagnostics(true)
    setDiagnosticsNotice(null)

    try {
      const result = await exportAgentDiagnostics()
      setDiagnosticsNotice({
        kind: 'success',
        text: `Diagnostics exported: ${result.fileName} (generated ${result.generatedAt}).`,
      })
    } catch {
      setDiagnosticsNotice({
        kind: 'error',
        text: 'Diagnostics export failed. Try again from this workload context.',
      })
    } finally {
      setExportingDiagnostics(false)
    }
  }

  if (loading) {
    return <div className="py-8 text-center">Loading...</div>
  }

  if (!summary) {
    return (
      <div className="py-8 text-center text-[var(--status-danger-text)]">Failed to load agent local console data.</div>
    )
  }

  return (
    <div className="space-y-6">
      <header className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-strong)]">Agent Local Console</h1>
        <p className="mt-2 text-sm text-[var(--text-soft)]">
          Node-local workload operations surface for pre-check validation, guided update execution, and diagnostics export.
        </p>
      </header>

      {error && (
        <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-4 py-3 text-sm text-[var(--status-danger-text)]">
          {error}
        </div>
      )}

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
        <h2 className="text-base font-semibold text-[var(--text-strong)]">Node and Run Status</h2>
        <dl className="mt-4 grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
          <StatusRow label="Node" value={`${summary.nodeId} (${summary.hostname})`} />
          <StatusRow label="Health" value={summary.health} />
          <StatusRow label="Run State" value={summary.runState} />
          <StatusRow label="Current Workload" value={summary.currentWorkload} />
          <StatusRow label="Installed Revision" value={summary.installedRevision} />
          <StatusRow label="Target Revision" value={summary.targetRevision} />
          <StatusRow label="Risk Level" value={summary.riskLevel} />
          <StatusRow label="Pending Approval" value={summary.pendingApproval ? 'yes' : 'no'} />
        </dl>
      </section>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
        <h2 className="text-base font-semibold text-[var(--text-strong)]">Guided Update Flow</h2>
        <p className="mt-2 text-sm text-[var(--text-soft)]">
          Validate node prerequisites before applying the next workload revision.
        </p>

        <div className="mt-4 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={() => handleRunPrecheck()}
            disabled={runningPrecheck}
            className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:bg-[var(--surface-border)]"
          >
            {runningPrecheck ? 'Running Pre-check...' : 'Run Pre-check'}
          </button>

          <button
            type="button"
            onClick={() => handleStartGuidedUpdate()}
            disabled={!confirmUpdate || startingUpdate}
            className="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:bg-[var(--surface-border)]"
          >
            {startingUpdate ? 'Starting Guided Update...' : 'Start Guided Update'}
          </button>
        </div>

        <label className="mt-4 flex items-start gap-3 text-sm text-[var(--text-strong)]">
          <input
            type="checkbox"
            checked={confirmUpdate}
            onChange={event => setConfirmUpdate(event.target.checked)}
            className="mt-0.5 h-4 w-4"
          />
          I confirm this workload update is approved for this node.
        </label>

        {precheckMessage && (
          <p
            role={precheckMessage.kind === 'success' ? 'status' : 'alert'}
            aria-live="polite"
            className={`mt-3 rounded-lg border px-3 py-2 text-sm ${
              precheckMessage.kind === 'success'
                ? 'border-[var(--status-success-border)] bg-[var(--status-success-bg)] text-[var(--status-success-text)]'
                : 'border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] text-[var(--status-danger-text)]'
            }`}
          >
            {precheckMessage.text}
          </p>
        )}
      </section>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
        <h2 className="text-base font-semibold text-[var(--text-strong)]">Mini Log Viewer</h2>
        {logs.length === 0 ? (
          <p className="mt-3 text-sm text-[var(--text-soft)]">No workload log lines are available for this node.</p>
        ) : (
          <ul className="mt-3 space-y-2 text-sm">
            {logs.map(line => (
              <li key={line.id} className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2">
                <p className="font-mono text-xs text-[var(--text-soft)]">{line.at}</p>
                <p className="mt-1 text-[var(--text-strong)]">{line.message}</p>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
        <h2 className="text-base font-semibold text-[var(--text-strong)]">Export Diagnostics</h2>
        <p className="mt-2 text-sm text-[var(--text-soft)]">
          Generate a diagnostics package for workload incident escalation and operator handoff.
        </p>
        <button
          type="button"
          onClick={() => handleExportDiagnostics()}
          disabled={exportingDiagnostics}
          className="mt-4 rounded-lg bg-[var(--surface-muted)] px-4 py-2 text-sm font-medium text-[var(--text-strong)] hover:bg-[var(--surface-border)] disabled:bg-[var(--surface-border)]"
        >
          {exportingDiagnostics ? 'Exporting Diagnostics...' : 'Export Diagnostics'}
        </button>

        {diagnosticsNotice && (
          <p
            role={diagnosticsNotice.kind === 'success' ? 'status' : 'alert'}
            aria-live="polite"
            className={`mt-3 rounded-lg border px-3 py-2 text-sm ${
              diagnosticsNotice.kind === 'success'
                ? 'border-[var(--status-success-border)] bg-[var(--status-success-bg)] text-[var(--status-success-text)]'
                : 'border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] text-[var(--status-danger-text)]'
            }`}
          >
            {diagnosticsNotice.text}
          </p>
        )}
      </section>
    </div>
  )
}

function StatusRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2">
      <dt className="text-xs uppercase tracking-wide text-[var(--text-soft)]">{label}</dt>
      <dd className="mt-1 text-[var(--text-strong)]">{value}</dd>
    </div>
  )
}
