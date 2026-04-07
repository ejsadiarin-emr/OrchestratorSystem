import { useEffect, useState } from 'react'
import type { InstallJob } from '../types'

export default function Jobs() {
  const [jobs, setJobs] = useState<InstallJob[]>([])
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState<string>('all')
  const [selectedJob, setSelectedJob] = useState<InstallJob | null>(null)

  const fetchJobs = () => {
    const url = filter === 'all' ? '/api/jobs' : `/api/jobs?status=${filter}`
    fetch(url)
      .then(r => r.json())
      .then(data => {
        setJobs(data)
        setLoading(false)
      })
  }

  useEffect(() => { fetchJobs() }, [filter])

  const handleCancel = async (id: string) => {
    if (!confirm('Cancel this job?')) return
    await fetch(`/api/jobs/${id}`, { method: 'DELETE' })
    fetchJobs()
  }

  const refresh = () => fetchJobs()

  if (loading) return <div className="text-center py-8">Loading...</div>

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-gray-800">Jobs</h1>
        <button onClick={refresh} className="text-blue-600 hover:text-blue-800">
          Refresh
        </button>
      </div>

      <div className="flex gap-2 mb-4">
        {['all', 'Pending', 'Running', 'Completed', 'Failed'].map(status => (
          <button
            key={status}
            onClick={() => setFilter(status)}
            className={`px-3 py-1 rounded-full text-sm ${
              filter === status
                ? 'bg-blue-600 text-white'
                : 'bg-gray-200 text-gray-700 hover:bg-gray-300'
            }`}
          >
            {status.charAt(0).toUpperCase() + status.slice(1)}
          </button>
        ))}
      </div>

      {jobs.length === 0 ? (
        <p className="text-gray-500">No jobs found</p>
      ) : (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Package</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Target</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Progress</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Started</th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {jobs.map(job => (
                <tr
                  key={job.id}
                  className="cursor-pointer hover:bg-gray-50"
                  onClick={() => setSelectedJob(job)}
                >
                  <td className="px-6 py-4 font-medium">{job.packageName}</td>
                  <td className="px-6 py-4 text-gray-600">{job.targetNodeHostname}</td>
                  <td className="px-6 py-4">
                    <span className={`px-2 py-1 rounded-full text-xs ${
                      job.status === 'Completed' ? 'bg-green-100 text-green-800' :
                      job.status === 'Running' ? 'bg-yellow-100 text-yellow-800' :
                      job.status === 'Failed' ? 'bg-red-100 text-red-800' :
                      job.status === 'Cancelled' ? 'bg-gray-100 text-gray-800' :
                      'bg-blue-100 text-blue-800'
                    }`}>
                      {job.status}
                    </span>
                  </td>
                  <td className="px-6 py-4 text-gray-600">
                    {job.currentStep}/{job.totalSteps}
                  </td>
                  <td className="px-6 py-4 text-gray-600 text-sm">
                    {new Date(job.startedAt).toLocaleString()}
                  </td>
                  <td className="px-6 py-4 text-right">
                    {job.status === 'Running' && (
                      <button
                        onClick={(e) => { e.stopPropagation(); handleCancel(job.id) }}
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
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-lg shadow-lg p-6 max-w-lg w-full">
            <h2 className="text-xl font-bold mb-4">Job Details</h2>
            <div className="space-y-3">
              <p><span className="font-medium">Package:</span> {selectedJob.packageName}</p>
              <p><span className="font-medium">Target:</span> {selectedJob.targetNodeHostname}</p>
              <p><span className="font-medium">Status:</span> {selectedJob.status}</p>
              <p><span className="font-medium">Started:</span> {new Date(selectedJob.startedAt).toLocaleString()}</p>
              {selectedJob.completedAt && (
                <p><span className="font-medium">Completed:</span> {new Date(selectedJob.completedAt).toLocaleString()}</p>
              )}
              {selectedJob.errorMessage && (
                <p className="text-red-600"><span className="font-medium">Error:</span> {selectedJob.errorMessage}</p>
              )}
            </div>
            <h3 className="font-semibold mt-4 mb-2">Steps</h3>
            <div className="space-y-2">
              {selectedJob.steps.map((step, i) => (
                <div key={i} className="flex items-center justify-between p-2 bg-gray-50 rounded">
                  <span>{step.name}</span>
                  <div className="flex items-center gap-2">
                    {step.duration && <span className="text-sm text-gray-500">{step.duration}</span>}
                    <span className={`text-xs ${
                      step.status === 'Completed' ? 'text-green-600' :
                      step.status === 'Running' ? 'text-yellow-600' :
                      step.status === 'Failed' ? 'text-red-600' :
                      'text-gray-500'
                    }`}>
                      {step.status}
                    </span>
                  </div>
                </div>
              ))}
            </div>
            <button
              onClick={() => setSelectedJob(null)}
              className="mt-4 w-full bg-gray-200 text-gray-800 py-2 rounded-md hover:bg-gray-300"
            >
              Close
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
