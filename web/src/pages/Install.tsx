import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import type { Package, Node, CreateJobRequest } from '../types'

export default function Install() {
  const navigate = useNavigate()
  const [packages, setPackages] = useState<Package[]>([])
  const [nodes, setNodes] = useState<Node[]>([])
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [form, setForm] = useState<CreateJobRequest>({ packageId: '', targetNodeId: '' })
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([
      fetch('/api/packages').then(r => r.json()),
      fetch('/api/nodes').then(r => r.json())
    ]).then(([pkgs, nd]) => {
      setPackages(pkgs)
      setNodes(nd)
      setLoading(false)
    })
  }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    setError(null)

    try {
      const res = await fetch('/api/jobs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form)
      })

      if (!res.ok) {
        const data = await res.json()
        throw new Error(data.message || 'Failed to create job')
      }

      navigate('/jobs')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create job')
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) return <div className="text-center py-8">Loading...</div>

  return (
    <div className="max-w-2xl mx-auto">
      <h1 className="text-2xl font-bold text-gray-800 mb-6">New Installation</h1>

      {packages.length === 0 || nodes.length === 0 ? (
        <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
          <p className="text-yellow-800">
            Please add packages and nodes before creating an install job.
          </p>
          <div className="mt-4 flex gap-4">
            {packages.length === 0 && (
              <a href="/packages" className="text-blue-600 hover:underline">Add Packages →</a>
            )}
            {nodes.length === 0 && (
              <a href="/nodes" className="text-blue-600 hover:underline">Add Nodes →</a>
            )}
          </div>
        </div>
      ) : (
        <div className="bg-white rounded-lg shadow p-6">
          <form onSubmit={handleSubmit} className="space-y-6">
            {error && (
              <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
                {error}
              </div>
            )}

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Select Package
              </label>
              <select
                value={form.packageId}
                onChange={e => setForm({ ...form, packageId: e.target.value })}
                required
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-blue-500"
              >
                <option value="">Choose a package...</option>
                {packages.map(pkg => (
                  <option key={pkg.id} value={pkg.id}>
                    {pkg.name} v{pkg.version} ({pkg.installType})
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Select Target Node
              </label>
              <select
                value={form.targetNodeId}
                onChange={e => setForm({ ...form, targetNodeId: e.target.value })}
                required
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-blue-500"
              >
                <option value="">Choose a node...</option>
                {nodes.map(node => (
                  <option key={node.id} value={node.id}>
                    {node.hostname} ({node.ipAddress})
                  </option>
                ))}
              </select>
            </div>

            <button
              type="submit"
              disabled={submitting}
              className="w-full bg-blue-600 text-white py-3 rounded-md hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed"
            >
              {submitting ? 'Creating Job...' : 'Start Installation'}
            </button>
          </form>
        </div>
      )}
    </div>
  )
}
