import { useEffect, useState } from 'react'
import {
  consumeEnrollmentToken,
  issueEnrollmentToken,
  listEnrollmentTokens,
  listNodes,
} from '../services/api'
import type { EnrollmentToken, Node } from '../types'

export default function Nodes() {
  const [nodes, setNodes] = useState<Node[]>([])
  const [tokens, setTokens] = useState<EnrollmentToken[]>([])
  const [loading, setLoading] = useState(true)
  const [creatingToken, setCreatingToken] = useState(false)
  const [simulatingConnect, setSimulatingConnect] = useState(false)
  const [orchestratorUrl, setOrchestratorUrl] = useState('https://orchestrator.local:5000')
  const [requestedBy, setRequestedBy] = useState('ops.admin')
  const [ttlMinutes, setTtlMinutes] = useState(20)
  const [simulateToken, setSimulateToken] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refresh = async () => {
    const [nodeData, tokenData] = await Promise.all([listNodes(), listEnrollmentTokens()])
    setNodes(nodeData)
    setTokens(tokenData)
  }

  useEffect(() => {
    refresh()
      .catch(() => setError('Failed to load nodes and enrollment tokens.'))
      .finally(() => setLoading(false))

    const timer = window.setInterval(() => {
      void refresh()
    }, 5_000)

    return () => {
      window.clearInterval(timer)
    }
  }, [])

  const handleIssueToken = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setMessage(null)
    setCreatingToken(true)

    try {
      const token = await issueEnrollmentToken({ requestedBy, orchestratorUrl, ttlMinutes })
      await refresh()
      setSimulateToken(token.token)
      setMessage(`Issued ${token.token} via POST /api/nodes/enroll.`)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to issue token.')
    } finally {
      setCreatingToken(false)
    }
  }

  const handleFirstConnect = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setMessage(null)
    setSimulatingConnect(true)

    try {
      const node = await consumeEnrollmentToken(simulateToken)
      await refresh()
      setMessage(`${node.hostname} connected. Hostname/IP/OS metadata auto-collected on first connect.`)
      setSimulateToken('')
    } catch (connectError) {
      setError(connectError instanceof Error ? connectError.message : 'Failed to simulate connect.')
    } finally {
      setSimulatingConnect(false)
    }
  }

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-6">
      {error && (
        <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-4 py-3 text-sm text-[var(--status-danger-text)]">
          {error}
        </div>
      )}
      {message && (
        <div className="rounded-lg border border-[var(--status-success-border)] bg-[var(--status-success-bg)] px-4 py-3 text-sm text-[var(--status-success-text)]">
          {message}
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        <section className="space-y-4 rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">1) Issue short-lived enrollment token</h2>
          <form onSubmit={handleIssueToken} className="space-y-4">
            <label className="block text-sm text-[var(--text-soft)]">
              Orchestrator URL
              <input
                type="text"
                value={orchestratorUrl}
                onChange={event => setOrchestratorUrl(event.target.value)}
                className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                required
              />
            </label>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <label className="block text-sm text-[var(--text-soft)]">
                Requested by
                <input
                  type="text"
                  value={requestedBy}
                  onChange={event => setRequestedBy(event.target.value)}
                  className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                  required
                />
              </label>
              <label className="block text-sm text-[var(--text-soft)]">
                TTL (minutes)
                <input
                  type="number"
                  min={1}
                  max={120}
                  value={ttlMinutes}
                  onChange={event => setTtlMinutes(Number(event.target.value) || 1)}
                  className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                  required
                />
              </label>
            </div>
            <button
              type="submit"
              disabled={creatingToken}
              className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:bg-[var(--surface-border)]"
            >
              {creatingToken ? 'Issuing...' : 'Issue Token (POST)'}
            </button>
          </form>
        </section>

        <section className="space-y-4 rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">2) Bootstrap script and first connect</h2>
          <p className="text-sm text-[var(--text-soft)]">
            Required bootstrap input is URL + token only. Hostname, IP, OS version, and agent version are
            auto-collected when the node first connects.
          </p>
          <pre className="overflow-x-auto rounded-lg bg-slate-950 p-3 font-mono text-xs text-slate-100">
{`powershell -ExecutionPolicy Bypass -File .\\bootstrap-agent.ps1 \\
  -OrchestratorUrl "${orchestratorUrl}" \\
  -EnrollmentToken "${simulateToken || '<token>'}"`}
          </pre>

          <form onSubmit={handleFirstConnect} className="space-y-3">
            <label className="block text-sm text-[var(--text-soft)]">
              Token to consume
              <input
                type="text"
                value={simulateToken}
                onChange={event => setSimulateToken(event.target.value)}
                className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                placeholder="enroll-0001"
                required
              />
            </label>
            <button
              type="submit"
              disabled={simulatingConnect}
              className="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-[var(--surface-border)]"
            >
              {simulatingConnect ? 'Connecting...' : 'Simulate First Connect'}
            </button>
          </form>
        </section>
      </div>

      <section className="overflow-hidden rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] shadow-[var(--surface-shadow)]">
        <div className="border-b border-[var(--surface-border)] px-6 py-4">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Enrollment Tokens</h2>
        </div>
        {tokens.length === 0 ? (
          <p className="px-6 py-5 text-sm text-[var(--text-soft)]">No tokens issued yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-[var(--surface-border)]">
              <thead className="bg-[var(--surface-subtle)]">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Token</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">URL</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Expires</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">State</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--surface-border)]">
                {tokens.map(token => (
                  <tr key={token.token}>
                    <td className="px-6 py-4 text-sm font-mono text-[var(--text-soft)]">{token.token}</td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">{token.orchestratorUrl}</td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">{new Date(token.expiresAt).toLocaleString()}</td>
                    <td className="px-6 py-4 text-sm">
                      <span
                        className={`rounded-full px-2 py-1 text-xs ${
                          token.used ? 'bg-[var(--surface-muted)] text-[var(--text-soft)]' : 'bg-[var(--status-warning-bg)] text-[var(--status-warning-text)]'
                        }`}
                      >
                        {token.used ? 'Consumed (invalidated)' : 'Issued (single-use)'}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="overflow-hidden rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] shadow-[var(--surface-shadow)]">
        <div className="border-b border-[var(--surface-border)] px-6 py-4">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Registered Nodes</h2>
        </div>
        {nodes.length === 0 ? (
          <p className="px-6 py-5 text-sm text-[var(--text-soft)]">No nodes registered yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-[var(--surface-border)]">
              <thead className="bg-[var(--surface-subtle)]">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Hostname</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">IP</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Status</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">First Connect</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Metadata</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--surface-border)]">
                {nodes.map(node => (
                  <tr key={node.id}>
                    <td className="px-6 py-4 text-sm font-medium text-[var(--text-strong)]">{node.hostname}</td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">{node.ipAddress}</td>
                    <td className="px-6 py-4 text-sm">
                      <span
                        className={`rounded-full px-2 py-1 text-xs font-medium ${
                          node.status === 'online'
                            ? 'bg-emerald-100 text-emerald-800'
                            : node.status === 'offline'
                              ? 'bg-slate-100 text-slate-800'
                              : node.status === 'installing'
                                ? 'bg-amber-100 text-amber-800'
                                : node.status === 'enrolling'
                                  ? 'bg-blue-100 text-blue-800'
                                  : 'bg-[var(--surface-muted)] text-[var(--text-soft)]'
                        }`}
                      >
                        {node.status}
                      </span>
                    </td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">
                      {node.firstConnectedAt ? new Date(node.firstConnectedAt).toLocaleString() : 'Pending'}
                    </td>
                    <td className="px-6 py-4 text-xs text-[var(--text-soft)]">
                      <div>OS: {node.osVersion}</div>
                      <div>Agent: {node.agentVersion}</div>
                      <div>Last seen: {new Date(node.lastSeenAt).toLocaleString()}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
