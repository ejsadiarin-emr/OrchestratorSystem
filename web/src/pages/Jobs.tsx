import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { cancelJob, createInstallJob, listArtifacts, listJobs, listNodes } from '../services/api'
import { subscribeToJobProgress } from '../services/realtime'
import type { ArtifactRecord, InstallJob, Node } from '../types'

const filterValues: Array<InstallJob['status'] | 'all'> = ['all', 'pending', 'running', 'completed', 'failed', 'cancelled']

export default function Jobs() {
  const [jobs, setJobs] = useState<InstallJob[]>([])
  const [nodes, setNodes] = useState<Node[]>([])
  const [artifacts, setArtifacts] = useState<ArtifactRecord[]>([])
  const [filter, setFilter] = useState<InstallJob['status'] | 'all'>('all')
  const [selectedJob, setSelectedJob] = useState<InstallJob | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [form, setForm] = useState({ artifactId: '', targetNodeId: '' })
  const [submitting, setSubmitting] = useState(false)
  const unsubscribers = useRef<Map<string, () => void>>(new Map())

  const refresh = useCallback(async (status: InstallJob['status'] | 'all' = filter) => {
    const [jobData, nodeData, artifactData] = await Promise.all([
      listJobs(status),
      listNodes(),
      listArtifacts(),
    ])
    setJobs(jobData)
    setNodes(nodeData)
    setArtifacts(artifactData)

    if (!form.artifactId && artifactData.length > 0) {
      setForm(current => ({ ...current, artifactId: artifactData[0].id }))
    }

    if (!form.targetNodeId && nodeData.length > 0) {
      setForm(current => ({ ...current, targetNodeId: nodeData[0].id }))
    }
  }, [filter, form.artifactId, form.targetNodeId])

  useEffect(() => {
    const activeUnsubscribers = unsubscribers.current

    refresh()
      .catch(() => setError('Failed to load jobs, nodes, or artifacts.'))
      .finally(() => setLoading(false))

    return () => {
      Array.from(activeUnsubscribers.values()).forEach(unsubscribe => unsubscribe())
      activeUnsubscribers.clear()
    }
  }, [refresh])

  useEffect(() => {
    refresh(filter).catch(() => setError('Failed to refresh jobs.'))
  }, [filter, refresh])

  useEffect(() => {
    const runningJobs = jobs.filter(job => job.status === 'running' || job.status === 'pending')
    const runningIds = new Set(runningJobs.map(job => job.id))

    runningJobs.forEach(job => {
      if (!unsubscribers.current.has(job.id)) {
        const unsubscribe = subscribeToJobProgress(job.id, updatedJob => {
          setJobs(current => current.map(item => (item.id === updatedJob.id ? { ...updatedJob } : item)))
          if (selectedJob?.id === updatedJob.id) {
            setSelectedJob({ ...updatedJob })
          }
        })
        unsubscribers.current.set(job.id, unsubscribe)
      }
    })

    Array.from(unsubscribers.current.entries()).forEach(([jobId, unsubscribe]) => {
      if (!runningIds.has(jobId)) {
        unsubscribe()
        unsubscribers.current.delete(jobId)
      }
    })
  }, [jobs, selectedJob?.id])

  const stageLabel = useMemo(
    () => ({
      assigned: 'AssignJob',
      'head-check': 'HEAD request',
      'range-download': 'Ranged GET loop',
      'verify-digest-signature': 'Digest/signature verify',
      completed: 'Complete',
      failed: 'Fail',
    }),
    [],
  )

  const handleCreateJob = async (event: React.FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    setError(null)

    try {
      await createInstallJob(form)
      await refresh(filter)
    } catch (creationError) {
      setError(creationError instanceof Error ? creationError.message : 'Failed to create job.')
    } finally {
      setSubmitting(false)
    }
  }

  const handleCancelJob = async (jobId: string) => {
    try {
      await cancelJob(jobId)
      await refresh(filter)
    } catch (cancelError) {
      setError(cancelError instanceof Error ? cancelError.message : 'Failed to cancel job.')
    }
  }

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-gray-800">Jobs & Delivery Flow</h1>
          <p className="text-sm text-gray-600 mt-2">
            Mock sequence: AssignJob -&gt; HEAD -&gt; ranged GET loop -&gt; digest/signature verify -&gt; terminal status.
          </p>
        </div>
        <button
          onClick={() => refresh(filter).catch(() => setError('Failed to refresh jobs.'))}
          className="text-blue-600 hover:text-blue-800"
        >
          Refresh
        </button>
      </header>

      {error && <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div>}

      <section className="bg-white rounded-lg shadow p-6">
        <h2 className="text-lg font-semibold text-gray-800 mb-4">Create mock install job</h2>
        <form onSubmit={handleCreateJob} className="grid grid-cols-1 md:grid-cols-3 gap-4 items-end">
          <label className="text-sm text-gray-700 block">
            Artifact
            <select
              value={form.artifactId}
              onChange={event => setForm(current => ({ ...current, artifactId: event.target.value }))}
              className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
              required
            >
              <option value="">Select artifact...</option>
              {artifacts.map(artifact => (
                <option key={artifact.id} value={artifact.id}>
                  {artifact.manifest.name} {artifact.manifest.version} ({artifact.manifest.channel})
                </option>
              ))}
            </select>
          </label>
          <label className="text-sm text-gray-700 block">
            Target node
            <select
              value={form.targetNodeId}
              onChange={event => setForm(current => ({ ...current, targetNodeId: event.target.value }))}
              className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
              required
            >
              <option value="">Select node...</option>
              {nodes.map(node => (
                <option key={node.id} value={node.id}>
                  {node.hostname} ({node.ipAddress})
                </option>
              ))}
            </select>
          </label>
          <button
            type="submit"
            disabled={submitting}
            className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 disabled:bg-gray-400"
          >
            {submitting ? 'Creating...' : 'Create Job'}
          </button>
        </form>
      </section>

      <div className="flex gap-2 flex-wrap">
        {filterValues.map(status => (
          <button
            key={status}
            onClick={() => setFilter(status)}
            className={`px-3 py-1 rounded-full text-sm ${
              filter === status ? 'bg-blue-600 text-white' : 'bg-gray-200 text-gray-700 hover:bg-gray-300'
            }`}
          >
            {status}
          </button>
        ))}
      </div>

      {jobs.length === 0 ? (
        <p className="text-gray-500">No jobs found.</p>
      ) : (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Artifact</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Node</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Delivery Stage</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Progress</th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {jobs.map(job => (
                <tr key={job.id} className="hover:bg-gray-50 cursor-pointer" onClick={() => setSelectedJob(job)}>
                  <td className="px-6 py-4 text-sm font-medium text-gray-800">{job.artifactName}</td>
                  <td className="px-6 py-4 text-sm text-gray-700">{job.targetNodeHostname}</td>
                  <td className="px-6 py-4 text-sm text-gray-700">{job.status}</td>
                  <td className="px-6 py-4 text-sm text-gray-700">{stageLabel[job.deliveryStage]}</td>
                  <td className="px-6 py-4 text-sm text-gray-700">
                    {job.chunksDownloaded}/{job.totalChunks}
                  </td>
                  <td className="px-6 py-4 text-right">
                    {(job.status === 'running' || job.status === 'pending') && (
                      <button
                        onClick={event => {
                          event.stopPropagation()
                          handleCancelJob(job.id)
                        }}
                        className="text-red-600 hover:text-red-800"
                      >
                        Cancel
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {selectedJob && (
        <div className="fixed inset-0 bg-black/40 p-4 flex items-center justify-center">
          <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl p-6 max-h-[90vh] overflow-y-auto">
            <h3 className="text-xl font-bold text-gray-800">{selectedJob.id} delivery details</h3>
            <p className="text-sm text-gray-600 mt-1">
              {selectedJob.artifactName} -&gt; {selectedJob.targetNodeHostname}
            </p>
            <div className="mt-4 grid grid-cols-1 md:grid-cols-2 gap-3 text-sm">
              <div>
                <span className="font-medium text-gray-800">Status:</span> {selectedJob.status}
              </div>
              <div>
                <span className="font-medium text-gray-800">Current Stage:</span> {stageLabel[selectedJob.deliveryStage]}
              </div>
            </div>

            <h4 className="font-semibold text-gray-800 mt-6">Protocol events</h4>
            <div className="mt-2 border border-gray-200 rounded-md divide-y divide-gray-200">
              {selectedJob.events.map((event, index) => (
                <div key={`${event.at}-${index}`} className="px-3 py-2 text-sm">
                  <span className="text-gray-500 mr-2">{new Date(event.at).toLocaleTimeString()}</span>
                  <span className="text-gray-700">{event.message}</span>
                </div>
              ))}
            </div>

            {selectedJob.errorMessage && (
              <p className="mt-4 text-sm text-red-700 bg-red-50 border border-red-200 rounded-md p-3">
                {selectedJob.errorMessage}
              </p>
            )}

            <button
              onClick={() => setSelectedJob(null)}
              className="mt-6 w-full bg-gray-200 text-gray-800 py-2 rounded-md hover:bg-gray-300"
            >
              Close
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
