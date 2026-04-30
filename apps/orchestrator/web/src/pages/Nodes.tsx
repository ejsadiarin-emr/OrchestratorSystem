import { useEffect, useRef, useState } from 'react'
import {
  deleteNode,
  issueEnrollmentToken,
  listEnrollmentTokens,
  listNodes,
  updateNodeDisplayName,
} from '../services/api'
import type { EnrollmentToken, Node } from '../types'
import NodeDetailsModal from '../components/NodeDetailsModal'

export default function Nodes() {
  const [nodes, setNodes] = useState<Node[]>([])
  const [tokens, setTokens] = useState<EnrollmentToken[]>([])
  const [loading, setLoading] = useState(true)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [showTokenModal, setShowTokenModal] = useState(false)
  const [tokenStep, setTokenStep] = useState<'form' | 'result'>('form')
  const [orchestratorUrl, setOrchestratorUrl] = useState('https://orchestrator.local:5000')
  const [requestedBy, setRequestedBy] = useState('ops.admin')
  const [ttlMinutes, setTtlMinutes] = useState(20)
  const [createdToken, setCreatedToken] = useState<EnrollmentToken | null>(null)
  const [issuingToken, setIssuingToken] = useState(false)

  const [editingNodeId, setEditingNodeId] = useState<string | null>(null)
  const [editingValue, setEditingValue] = useState('')
  const [savingName, setSavingName] = useState(false)
  const editInputRef = useRef<HTMLInputElement>(null)

  const [deletingNode, setDeletingNode] = useState<Node | null>(null)
  const [showConsumedTokens, setShowConsumedTokens] = useState(false)
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)

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

  useEffect(() => {
    if (editingNodeId && editInputRef.current) {
      editInputRef.current.focus()
      editInputRef.current.select()
    }
  }, [editingNodeId])

  const handleIssueToken = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setMessage(null)
    setIssuingToken(true)

    try {
      const token = await issueEnrollmentToken({ requestedBy, orchestratorUrl, ttlMinutes })
      await refresh()
      setCreatedToken(token)
      setTokenStep('result')
      setMessage(`Issued ${token.token}.`)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to issue token.')
    } finally {
      setIssuingToken(false)
    }
  }

  const openTokenModal = () => {
    setShowTokenModal(true)
    setTokenStep('form')
    setCreatedToken(null)
    setError(null)
    setMessage(null)
  }

  const closeTokenModal = () => {
    setShowTokenModal(false)
    setTokenStep('form')
    setCreatedToken(null)
  }

  const startEditing = (node: Node) => {
    setEditingNodeId(node.id)
    setEditingValue(node.displayName || node.hostname)
    setError(null)
  }

  const cancelEditing = () => {
    setEditingNodeId(null)
    setEditingValue('')
  }

  const saveDisplayName = async (nodeId: string) => {
    const trimmed = editingValue.trim()
    if (!trimmed) {
      cancelEditing()
      return
    }
    setSavingName(true)
    try {
      await updateNodeDisplayName(nodeId, trimmed)
      await refresh()
      setEditingNodeId(null)
      setEditingValue('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to rename node.')
    } finally {
      setSavingName(false)
    }
  }

  const handleKeyDown = (event: React.KeyboardEvent, nodeId: string) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      void saveDisplayName(nodeId)
    } else if (event.key === 'Escape') {
      cancelEditing()
    }
  }

  const confirmDelete = async () => {
    if (!deletingNode) return
    try {
      await deleteNode(deletingNode.id)
      await refresh()
      setDeletingNode(null)
      setMessage(`Deleted node ${deletingNode.displayName || deletingNode.hostname}.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete node.')
      setDeletingNode(null)
    }
  }

  const cliCommand = createdToken
    ? `DeploymentPoC.Agent.exe --enroll ${createdToken.token} --orchestrator-url=${createdToken.orchestratorUrl} --name "Optional Name"`
    : ''

  const copyToClipboard = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text)
      setMessage('Copied to clipboard.')
    } catch {
      setMessage('Copy failed. Select and copy manually.')
    }
  }

  const activeTokens = tokens.filter(t => !t.used && new Date(t.expiresAt) > new Date())
  const visibleTokens = showConsumedTokens ? tokens : activeTokens

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

      {/* Registered Nodes — hero section */}
      <section className="overflow-hidden rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] shadow-[var(--surface-shadow)]">
        <div className="flex items-center justify-between border-b border-[var(--surface-border)] px-6 py-4">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Registered Nodes</h2>
          <button
            onClick={openTokenModal}
            className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
          >
            Generate Token
          </button>
        </div>
        {nodes.length === 0 ? (
          <div className="px-6 py-8 text-center">
            <p className="text-sm text-[var(--text-soft)]">No nodes registered yet.</p>
            <p className="mt-2 text-xs text-[var(--text-soft)]">
              Use <span className="font-mono">DeploymentPoC.Agent.exe --enroll &lt;token&gt; --orchestrator-url=&lt;url&gt;</span> to connect an agent.
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-[var(--surface-border)]">
              <thead className="bg-[var(--surface-subtle)]">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Name</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Status</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">First Connect</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Metadata</th>
                  <th className="px-6 py-3 text-right text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--surface-border)]">
                {nodes.map(node => (
                  <tr
                    key={node.id}
                    className="cursor-pointer hover:bg-[var(--surface-subtle)]"
                    onClick={(e) => {
                      const target = e.target as HTMLElement
                      if (target.closest('button') || target.closest('input')) return
                      setSelectedNodeId(node.id)
                    }}
                  >
                    <td className="px-6 py-4">
                      {editingNodeId === node.id ? (
                        <input
                          ref={editInputRef}
                          type="text"
                          value={editingValue}
                          onChange={e => setEditingValue(e.target.value)}
                          onKeyDown={e => handleKeyDown(e, node.id)}
                          onBlur={() => void saveDisplayName(node.id)}
                          disabled={savingName}
                          className="w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-1.5 text-sm text-[var(--text-strong)]"
                        />
                      ) : (
                        <div>
                          <div className="text-sm font-medium text-[var(--text-strong)]">
                            {node.displayName || node.hostname}
                          </div>
                          <div className="text-xs text-[var(--text-soft)] font-mono">{node.hostname}</div>
                        </div>
                      )}
                    </td>
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
                      <div>OS: {node.osVersion || '—'}</div>
                      <div>Agent: {node.agentVersion || '—'}</div>
                      <div>Last seen: {node.lastSeenAt ? new Date(node.lastSeenAt).toLocaleString() : '—'}</div>
                    </td>
                    <td className="px-6 py-4 text-right">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => startEditing(node)}
                          disabled={editingNodeId === node.id && savingName}
                          className="rounded-md p-1.5 text-[var(--text-soft)] hover:bg-[var(--surface-subtle)] hover:text-[var(--text-strong)] disabled:opacity-50"
                          title="Rename"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z"/></svg>
                        </button>
                        <button
                          onClick={() => setDeletingNode(node)}
                          className="rounded-md p-1.5 text-[var(--status-danger-text)] hover:bg-[var(--status-danger-bg)]"
                          title="Delete"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/></svg>
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Enrollment Tokens — compact secondary section */}
      <section className="overflow-hidden rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] shadow-[var(--surface-shadow)]">
        <div className="flex items-center justify-between border-b border-[var(--surface-border)] px-6 py-4">
          <h2 className="text-sm font-semibold text-[var(--text-strong)]">Enrollment Tokens</h2>
          <label className="flex items-center gap-2 text-xs text-[var(--text-soft)]">
            <input
              type="checkbox"
              checked={showConsumedTokens}
              onChange={e => setShowConsumedTokens(e.target.checked)}
              className="rounded border-[var(--surface-border)]"
            />
            Show consumed
          </label>
        </div>
        {visibleTokens.length === 0 ? (
          <p className="px-6 py-4 text-xs text-[var(--text-soft)]">
            {showConsumedTokens ? 'No tokens issued yet.' : 'No active tokens.'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-[var(--surface-border)]">
              <thead className="bg-[var(--surface-subtle)]">
                <tr>
                  <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Token</th>
                  <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Expires</th>
                  <th className="px-4 py-2 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">State</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--surface-border)]">
                {visibleTokens.map(token => (
                  <tr key={token.token}>
                    <td className="px-4 py-2 text-xs font-mono text-[var(--text-soft)]">{token.token}</td>
                    <td className="px-4 py-2 text-xs text-[var(--text-soft)]">{new Date(token.expiresAt).toLocaleString()}</td>
                    <td className="px-4 py-2 text-xs">
                      <span
                        className={`rounded-full px-2 py-0.5 text-xs ${
                          token.used ? 'bg-[var(--surface-muted)] text-[var(--text-soft)]' : 'bg-[var(--status-warning-bg)] text-[var(--status-warning-text)]'
                        }`}
                      >
                        {token.used ? 'Consumed' : 'Active'}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Token Modal */}
      {showTokenModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
          <div className="w-full max-w-lg rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-xl">
            {tokenStep === 'form' ? (
              <>
                <h3 className="text-lg font-semibold text-[var(--text-strong)]">Generate Enrollment Token</h3>
                <form onSubmit={handleIssueToken} className="mt-4 space-y-4">
                  <label className="block text-sm text-[var(--text-soft)]">
                    Orchestrator URL
                    <input
                      type="text"
                      value={orchestratorUrl}
                      onChange={event => setOrchestratorUrl(event.target.value)}
                      className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2 text-sm"
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
                        className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2 text-sm"
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
                        className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2 text-sm"
                        required
                      />
                    </label>
                  </div>
                  <div className="flex items-center justify-end gap-3 pt-2">
                    <button
                      type="button"
                      onClick={closeTokenModal}
                      className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm font-medium text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
                    >
                      Cancel
                    </button>
                    <button
                      type="submit"
                      disabled={issuingToken}
                      className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:bg-[var(--surface-border)]"
                    >
                      {issuingToken ? 'Generating...' : 'Generate'}
                    </button>
                  </div>
                </form>
              </>
            ) : (
              <>
                <h3 className="text-lg font-semibold text-[var(--text-strong)]">Enrollment Token Created</h3>
                <div className="mt-4 space-y-4">
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-soft)]">Token</label>
                    <div className="mt-1 flex items-center gap-2">
                      <code className="flex-1 rounded-lg bg-slate-950 px-3 py-2 font-mono text-xs text-slate-100">
                        {createdToken?.token}
                      </code>
                      <button
                        onClick={() => createdToken && copyToClipboard(createdToken.token)}
                        className="rounded-lg border border-[var(--surface-border)] px-3 py-2 text-xs font-medium text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
                      >
                        Copy
                      </button>
                    </div>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-soft)]">CLI Command</label>
                    <div className="mt-1 flex items-start gap-2">
                      <code className="flex-1 whitespace-pre-wrap rounded-lg bg-slate-950 px-3 py-2 font-mono text-xs text-slate-100">
                        {cliCommand}
                      </code>
                      <button
                        onClick={() => copyToClipboard(cliCommand)}
                        className="rounded-lg border border-[var(--surface-border)] px-3 py-2 text-xs font-medium text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
                      >
                        Copy
                      </button>
                    </div>
                  </div>
                  <div className="flex items-center justify-end gap-3 pt-2">
                    <button
                      onClick={() => { setTokenStep('form'); setCreatedToken(null) }}
                      className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm font-medium text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
                    >
                      Create Another
                    </button>
                    <button
                      onClick={closeTokenModal}
                      className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
                    >
                      Done
                    </button>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      <NodeDetailsModal
        nodeId={selectedNodeId}
        open={!!selectedNodeId}
        onClose={() => setSelectedNodeId(null)}
      />

      {/* Delete Confirmation Modal */}
      {deletingNode && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
          <div className="w-full max-w-sm rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-[var(--text-strong)]">Delete Node</h3>
            <p className="mt-2 text-sm text-[var(--text-soft)]">
              Are you sure you want to delete <strong className="text-[var(--text-strong)]">{deletingNode.displayName || deletingNode.hostname}</strong>? This action cannot be undone.
            </p>
            <div className="mt-6 flex items-center justify-end gap-3">
              <button
                onClick={() => setDeletingNode(null)}
                className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm font-medium text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
              >
                Cancel
              </button>
              <button
                onClick={() => void confirmDelete()}
                className="rounded-lg bg-[var(--status-danger-text)] px-4 py-2 text-sm font-medium text-white hover:opacity-90"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
