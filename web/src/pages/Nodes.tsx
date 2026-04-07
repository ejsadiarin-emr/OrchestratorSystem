import { useEffect, useState } from 'react'
import type { Node, CreateNodeRequest } from '../types'

export default function Nodes() {
  const [nodes, setNodes] = useState<Node[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState<CreateNodeRequest>({ hostname: '', ipAddress: '', description: '' })

  const fetchNodes = () => {
    fetch('/api/nodes')
      .then(r => r.json())
      .then(data => {
        setNodes(data)
        setLoading(false)
      })
  }

  useEffect(() => { fetchNodes() }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    await fetch('/api/nodes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(form)
    })
    setForm({ hostname: '', ipAddress: '', description: '' })
    setShowForm(false)
    fetchNodes()
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this node?')) return
    await fetch(`/api/nodes/${id}`, { method: 'DELETE' })
    fetchNodes()
  }

  if (loading) return <div className="text-center py-8">Loading...</div>

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-gray-800">Nodes</h1>
        <button
          onClick={() => setShowForm(!showForm)}
          className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700"
        >
          {showForm ? 'Cancel' : 'Add Node'}
        </button>
      </div>

      {showForm && (
        <div className="bg-white rounded-lg shadow p-6 mb-6">
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <input
                type="text"
                placeholder="Hostname"
                value={form.hostname}
                onChange={e => setForm({ ...form, hostname: e.target.value })}
                required
                className="border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="text"
                placeholder="IP Address"
                value={form.ipAddress}
                onChange={e => setForm({ ...form, ipAddress: e.target.value })}
                required
                className="border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="text"
                placeholder="Description"
                value={form.description}
                onChange={e => setForm({ ...form, description: e.target.value })}
                className="border border-gray-300 rounded-md px-3 py-2"
              />
            </div>
            <button type="submit" className="bg-blue-600 text-white px-6 py-2 rounded-md hover:bg-blue-700">
              Create
            </button>
          </form>
        </div>
      )}

      {nodes.length === 0 ? (
        <p className="text-gray-500">No nodes registered</p>
      ) : (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Hostname</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">IP Address</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Description</th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {nodes.map(node => (
                <tr key={node.id}>
                  <td className="px-6 py-4 font-medium">{node.hostname}</td>
                  <td className="px-6 py-4 text-gray-600">{node.ipAddress}</td>
                  <td className="px-6 py-4">
                    <span className={`px-2 py-1 rounded-full text-xs ${
                      node.status === 'Online' ? 'bg-green-100 text-green-800' :
                      node.status === 'Offline' ? 'bg-red-100 text-red-800' :
                      node.status === 'Installing' ? 'bg-yellow-100 text-yellow-800' :
                      'bg-gray-100 text-gray-800'
                    }`}>
                      {node.status}
                    </span>
                  </td>
                  <td className="px-6 py-4 text-gray-600">{node.description}</td>
                  <td className="px-6 py-4 text-right">
                    <button
                      onClick={() => handleDelete(node.id)}
                      className="text-red-600 hover:text-red-800"
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
