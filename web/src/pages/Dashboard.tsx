import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { Node, InstallJob } from '../types'

interface Stats {
  totalNodes: number
  onlineNodes: number
  activeJobs: number
  failedJobs: number
}

export default function Dashboard() {
  const [stats, setStats] = useState<Stats>({ totalNodes: 0, onlineNodes: 0, activeJobs: 0, failedJobs: 0 })
  const [recentJobs, setRecentJobs] = useState<InstallJob[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      fetch('/api/nodes').then(r => r.json()),
      fetch('/api/jobs').then(r => r.json())
    ]).then(([nodes, jobs]) => {
      const nodeList = nodes as Node[]
      const jobList = jobs as InstallJob[]
      
      setStats({
        totalNodes: nodeList.length,
        onlineNodes: nodeList.filter(n => n.status === 'Online').length,
        activeJobs: jobList.filter(j => j.status === 'Running').length,
        failedJobs: jobList.filter(j => j.status === 'Failed').length
      })
      setRecentJobs(jobList.slice(0, 5))
      setLoading(false)
    })
  }, [])

  if (loading) return <div className="text-center py-8">Loading...</div>

  const statCards = [
    { label: 'Total Nodes', value: stats.totalNodes, color: 'blue' },
    { label: 'Online', value: stats.onlineNodes, color: 'green' },
    { label: 'Active Jobs', value: stats.activeJobs, color: 'yellow' },
    { label: 'Failed Jobs', value: stats.failedJobs, color: 'red' },
  ]

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-800 mb-6">Dashboard</h1>
      
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-8">
        {statCards.map((stat) => (
          <div key={stat.label} className="bg-white rounded-lg shadow p-6">
            <p className="text-sm text-gray-500">{stat.label}</p>
            <p className={`text-3xl font-bold text-${stat.color}-600`}>{stat.value}</p>
          </div>
        ))}
      </div>

      <div className="bg-white rounded-lg shadow">
        <div className="p-4 border-b border-gray-200 flex justify-between items-center">
          <h2 className="text-lg font-semibold">Recent Jobs</h2>
          <Link to="/jobs" className="text-blue-600 hover:text-blue-800 text-sm">View All</Link>
        </div>
        {recentJobs.length === 0 ? (
          <p className="p-4 text-gray-500">No jobs yet</p>
        ) : (
          <div className="divide-y divide-gray-200">
            {recentJobs.map((job) => (
              <div key={job.id} className="p-4 flex items-center justify-between">
                <div>
                  <p className="font-medium">{job.packageName}</p>
                  <p className="text-sm text-gray-500">→ {job.targetNodeHostname}</p>
                </div>
                <span className={`px-3 py-1 rounded-full text-xs font-medium ${
                  job.status === 'Completed' ? 'bg-green-100 text-green-800' :
                  job.status === 'Running' ? 'bg-yellow-100 text-yellow-800' :
                  job.status === 'Failed' ? 'bg-red-100 text-red-800' :
                  'bg-gray-100 text-gray-800'
                }`}>
                  {job.status}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
