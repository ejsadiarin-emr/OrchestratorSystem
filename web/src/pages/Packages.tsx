import { useEffect, useState } from 'react'
import type { Package, CreatePackageRequest } from '../types'

export default function Packages() {
  const [packages, setPackages] = useState<Package[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState<CreatePackageRequest>({
    name: '', version: '', sourcePath: '', installType: 'msi', installArgs: ''
  })

  const fetchPackages = () => {
    fetch('/api/packages')
      .then(r => r.json())
      .then(data => {
        setPackages(data)
        setLoading(false)
      })
  }

  useEffect(() => { fetchPackages() }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    await fetch('/api/packages', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(form)
    })
    setForm({ name: '', version: '', sourcePath: '', installType: 'msi', installArgs: '' })
    setShowForm(false)
    fetchPackages()
  }

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this package?')) return
    await fetch(`/api/packages/${id}`, { method: 'DELETE' })
    fetchPackages()
  }

  if (loading) return <div className="text-center py-8">Loading...</div>

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-gray-800">Packages</h1>
        <button
          onClick={() => setShowForm(!showForm)}
          className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700"
        >
          {showForm ? 'Cancel' : 'Add Package'}
        </button>
      </div>

      {showForm && (
        <div className="bg-white rounded-lg shadow p-6 mb-6">
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <input
                type="text"
                placeholder="Package Name"
                value={form.name}
                onChange={e => setForm({ ...form, name: e.target.value })}
                required
                className="border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="text"
                placeholder="Version"
                value={form.version}
                onChange={e => setForm({ ...form, version: e.target.value })}
                required
                className="border border-gray-300 rounded-md px-3 py-2"
              />
              <input
                type="text"
                placeholder="Source Path (UNC)"
                value={form.sourcePath}
                onChange={e => setForm({ ...form, sourcePath: e.target.value })}
                required
                className="border border-gray-300 rounded-md px-3 py-2 md:col-span-2"
              />
              <select
                value={form.installType}
                onChange={e => setForm({ ...form, installType: e.target.value })}
                className="border border-gray-300 rounded-md px-3 py-2"
              >
                <option value="msi">MSI</option>
                <option value="exe">EXE</option>
                <option value="zip">ZIP</option>
              </select>
              <input
                type="text"
                placeholder="Install Args (e.g., /quiet /norestart)"
                value={form.installArgs}
                onChange={e => setForm({ ...form, installArgs: e.target.value })}
                className="border border-gray-300 rounded-md px-3 py-2"
              />
            </div>
            <button type="submit" className="bg-blue-600 text-white px-6 py-2 rounded-md hover:bg-blue-700">
              Create
            </button>
          </form>
        </div>
      )}

      {packages.length === 0 ? (
        <p className="text-gray-500">No packages registered</p>
      ) : (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Version</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Type</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Source Path</th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {packages.map(pkg => (
                <tr key={pkg.id}>
                  <td className="px-6 py-4 font-medium">{pkg.name}</td>
                  <td className="px-6 py-4 text-gray-600">{pkg.version}</td>
                  <td className="px-6 py-4">
                    <span className="px-2 py-1 bg-blue-100 text-blue-800 rounded text-xs uppercase">
                      {pkg.installType}
                    </span>
                  </td>
                  <td className="px-6 py-4 text-gray-600 text-sm">{pkg.sourcePath}</td>
                  <td className="px-6 py-4 text-right">
                    <button
                      onClick={() => handleDelete(pkg.id)}
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
