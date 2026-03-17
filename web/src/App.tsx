import { useState } from 'react'

interface InstallRequest {
  packageName: string
  targetMachine: string
  version: string
}

interface InstallResponse {
  isSuccessful: boolean
  errorMessage?: string
  executionLog?: string[]
}

function App() {
  const [formData, setFormData] = useState<InstallRequest>({
    packageName: '',
    targetMachine: '',
    version: ''
  })
  const [response, setResponse] = useState<InstallResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true)
    setError(null)
    setResponse(null)

    try {
      const res = await fetch('/api/install', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(formData)
      })
      if (!res.ok) {
        throw new Error(`HTTP ${res.status}: ${res.statusText}`)
      }
      const data = await res.json()
      setResponse(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to connect to API')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-gray-100 flex items-center justify-center p-4">
      <div className="bg-white rounded-lg shadow-lg p-8 w-full max-w-md">
        <h1 className="text-2xl font-bold text-center mb-6 text-gray-800">EJ Installer</h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="packageName" className="block text-sm font-medium text-gray-700 mb-1">
              Package Name
            </label>
            <input
              type="text"
              id="packageName"
              value={formData.packageName}
              onChange={(e) => setFormData({ ...formData, packageName: e.target.value })}
              required
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label htmlFor="targetMachine" className="block text-sm font-medium text-gray-700 mb-1">
              Target Machine
            </label>
            <input
              type="text"
              id="targetMachine"
              value={formData.targetMachine}
              onChange={(e) => setFormData({ ...formData, targetMachine: e.target.value })}
              required
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <div>
            <label htmlFor="version" className="block text-sm font-medium text-gray-700 mb-1">
              Version
            </label>
            <input
              type="text"
              id="version"
              value={formData.version}
              onChange={(e) => setFormData({ ...formData, version: e.target.value })}
              required
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>
          <button
            type="submit"
            disabled={loading}
            className="w-full bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors"
          >
            {loading ? 'Installing...' : 'Install'}
          </button>
        </form>

        {error && (
          <div className="mt-4 p-4 bg-red-100 text-red-700 rounded-md">
            {error}
          </div>
        )}

        {response && (
          <div className={`mt-6 p-4 rounded-md ${response.isSuccessful ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}`}>
            <h3 className="font-semibold">{response.isSuccessful ? 'Success' : 'Failed'}</h3>
            {response.errorMessage && <p className="mt-1">{response.errorMessage}</p>}
            {response.executionLog && response.executionLog.length > 0 && (
              <div className="mt-3">
                <h4 className="font-medium text-sm">Execution Log:</h4>
                <ul className="mt-1 list-disc list-inside text-sm">
                  {response.executionLog.map((log, i) => (
                    <li key={i}>{log}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

export default App
