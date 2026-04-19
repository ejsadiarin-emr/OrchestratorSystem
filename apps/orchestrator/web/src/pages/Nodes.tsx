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
    <div className="space-y-8">
      <header>
        <h1 className="text-2xl font-bold text-gray-800">Agent Bootstrap & Enrollment</h1>
        <p className="text-sm text-gray-600 mt-2">
          Enrollment tokens are issued with <code>POST /api/nodes/enroll</code>. Bootstrap script needs only
          orchestrator URL and short-lived token.
        </p>
      </header>

      {error && <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div>}
      {message && <div className="rounded-md border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-700">{message}</div>}

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        <section className="bg-white rounded-lg shadow p-6 space-y-4">
          <h2 className="text-lg font-semibold text-gray-800">1) Issue short-lived enrollment token</h2>
          <form onSubmit={handleIssueToken} className="space-y-4">
            <label className="text-sm text-gray-700 block">
              Orchestrator URL
              <input
                type="text"
                value={orchestratorUrl}
                onChange={event => setOrchestratorUrl(event.target.value)}
                className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                required
              />
            </label>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <label className="text-sm text-gray-700 block">
                Requested by
                <input
                  type="text"
                  value={requestedBy}
                  onChange={event => setRequestedBy(event.target.value)}
                  className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                  required
                />
              </label>
              <label className="text-sm text-gray-700 block">
                TTL (minutes)
                <input
                  type="number"
                  min={1}
                  max={120}
                  value={ttlMinutes}
                  onChange={event => setTtlMinutes(Number(event.target.value) || 1)}
                  className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                  required
                />
              </label>
            </div>
            <button
              type="submit"
              disabled={creatingToken}
              className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 disabled:bg-gray-400"
            >
              {creatingToken ? 'Issuing...' : 'Issue Token (POST)'}
            </button>
          </form>
        </section>

        <section className="bg-white rounded-lg shadow p-6 space-y-4">
          <h2 className="text-lg font-semibold text-gray-800">2) Bootstrap script and first connect</h2>
          <p className="text-sm text-gray-600">
            Required bootstrap input is URL + token only. Hostname, IP, OS version, and agent version are
            auto-collected when the node first connects.
          </p>
          <pre className="text-xs bg-gray-900 text-gray-100 rounded-md p-3 overflow-x-auto">
{`powershell -ExecutionPolicy Bypass -File .\\bootstrap-agent.ps1 \\
  -OrchestratorUrl "${orchestratorUrl}" \\
  -EnrollmentToken "${simulateToken || '<token>'}"`}
          </pre>

          <form onSubmit={handleFirstConnect} className="space-y-3">
            <label className="text-sm text-gray-700 block">
              Token to consume
              <input
                type="text"
                value={simulateToken}
                onChange={event => setSimulateToken(event.target.value)}
                className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                placeholder="enroll-0001"
                required
              />
            </label>
            <button
              type="submit"
              disabled={simulatingConnect}
              className="bg-emerald-600 text-white px-4 py-2 rounded-md hover:bg-emerald-700 disabled:bg-gray-400"
            >
              {simulatingConnect ? 'Connecting...' : 'Simulate First Connect'}
            </button>
          </form>
        </section>
      </div>

      <section className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-800">Enrollment Tokens</h2>
        </div>
        {tokens.length === 0 ? (
          <p className="px-6 py-5 text-sm text-gray-500">No tokens issued yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Token</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">URL</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Expires</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">State</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {tokens.map(token => (
                  <tr key={token.token}>
                    <td className="px-6 py-4 text-sm font-mono text-gray-700">{token.token}</td>
                    <td className="px-6 py-4 text-sm text-gray-700">{token.orchestratorUrl}</td>
                    <td className="px-6 py-4 text-sm text-gray-700">{new Date(token.expiresAt).toLocaleString()}</td>
                    <td className="px-6 py-4 text-sm">
                      <span
                        className={`px-2 py-1 rounded-full text-xs ${
                          token.used ? 'bg-gray-200 text-gray-800' : 'bg-blue-100 text-blue-800'
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

      <section className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-800">Registered Nodes</h2>
        </div>
        {nodes.length === 0 ? (
          <p className="px-6 py-5 text-sm text-gray-500">No nodes registered yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Hostname</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">IP</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">First Connect</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Metadata</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {nodes.map(node => (
                  <tr key={node.id}>
                    <td className="px-6 py-4 text-sm font-medium text-gray-800">{node.hostname}</td>
                    <td className="px-6 py-4 text-sm text-gray-700">{node.ipAddress}</td>
                    <td className="px-6 py-4 text-sm text-gray-700">{node.status}</td>
                    <td className="px-6 py-4 text-sm text-gray-700">
                      {node.firstConnectedAt ? new Date(node.firstConnectedAt).toLocaleString() : 'Pending'}
                    </td>
                    <td className="px-6 py-4 text-xs text-gray-600">
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
