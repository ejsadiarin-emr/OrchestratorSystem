import { useEffect, useState } from 'react'
import type { Package } from '../types'
import { listArtifacts } from '../services/api'

export default function Packages() {
  const [packages, setPackages] = useState<Package[]>([])
  const [loading, setLoading] = useState(true)

  const fetchPackages = () => {
    listArtifacts()
      .then(data => {
        const mapped = data.map<Package>(artifact => ({
          id: artifact.id,
          name: artifact.manifest.name,
          version: artifact.manifest.version,
          sourcePath: artifact.manifest.originMetadata.sourceUrl,
          installType: artifact.manifest.installType,
          installArgs: artifact.manifest.installArgs,
          createdAt: artifact.createdAt,
        }))
        setPackages(mapped)
        setLoading(false)
      })
  }

  useEffect(() => { fetchPackages() }, [])

  if (loading) return <div className="text-center py-8">Loading...</div>

  return (
    <div>
      <div className="mb-6 space-y-3">
        <h1 className="text-2xl font-bold text-gray-800">Artifacts (Legacy Read-Only)</h1>
        <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
          Package-centric authoring is deprecated for runtime operations. Create WorkloadDefinition and WorkloadRevision entities in the
          Workloads screen.
        </div>
      </div>

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
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
