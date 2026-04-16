import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getDashboardSummary, listAuditEvents, listJobs } from '../services/api'
import type { AuditEvent, DashboardSummary, InstallJob } from '../types'

const emptySummary: DashboardSummary = {
  totalNodes: 0,
  connectedNodes: 0,
  activeJobs: 0,
  failedJobs: 0,
  artifactsInStore: 0,
}

export default function Dashboard() {
  const [summary, setSummary] = useState<DashboardSummary>(emptySummary)
  const [recentJobs, setRecentJobs] = useState<InstallJob[]>([])
  const [events, setEvents] = useState<AuditEvent[]>([])
  const [loading, setLoading] = useState(true)

  const refresh = async () => {
    const [summaryData, jobData, eventData] = await Promise.all([
      getDashboardSummary(),
      listJobs('all'),
      listAuditEvents(6),
    ])

    setSummary(summaryData)
    setRecentJobs(jobData.slice(0, 5))
    setEvents(eventData)
  }

  useEffect(() => {
    let active = true

    Promise.all([getDashboardSummary(), listJobs('all'), listAuditEvents(6)])
      .then(([summaryData, jobData, eventData]) => {
        if (!active) {
          return
        }

        setSummary(summaryData)
        setRecentJobs(jobData.slice(0, 5))
        setEvents(eventData)
        setLoading(false)
      })
      .catch(() => {
        if (active) {
          setLoading(false)
        }
      })

    return () => {
      active = false
    }
  }, [])

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  const cards: Array<{ label: string; value: number; tone: string }> = [
    { label: 'Registered Nodes', value: summary.totalNodes, tone: 'text-blue-700' },
    { label: 'Connected Nodes', value: summary.connectedNodes, tone: 'text-emerald-700' },
    { label: 'Active Jobs', value: summary.activeJobs, tone: 'text-amber-700' },
    { label: 'Failed Jobs', value: summary.failedJobs, tone: 'text-rose-700' },
    { label: 'Artifacts in Store', value: summary.artifactsInStore, tone: 'text-indigo-700' },
  ]

  return (
    <div className="space-y-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-gray-800">Runtime Dashboard</h1>
          <p className="text-sm text-gray-600 mt-2">
            Snapshot of mocked Phase 1 runtime: ingestion, enrollment, and artifact delivery control-plane activity.
          </p>
        </div>
        <button onClick={() => refresh()} className="text-blue-600 hover:text-blue-800">
          Refresh
        </button>
      </header>

      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-5 gap-4">
        {cards.map(card => (
          <div key={card.label} className="bg-white rounded-lg shadow p-5 border border-gray-100">
            <p className="text-xs uppercase tracking-wide text-gray-500">{card.label}</p>
            <p className={`text-3xl font-bold mt-2 ${card.tone}`}>{card.value}</p>
          </div>
        ))}
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        <section className="bg-white rounded-lg shadow overflow-hidden">
          <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
            <h2 className="text-lg font-semibold text-gray-800">Recent Jobs</h2>
            <Link to="/jobs" className="text-blue-600 hover:text-blue-800 text-sm">
              View all
            </Link>
          </div>
          {recentJobs.length === 0 ? (
            <p className="p-6 text-sm text-gray-500">No jobs available.</p>
          ) : (
            <div className="divide-y divide-gray-200">
              {recentJobs.map(job => (
                <div key={job.id} className="p-4 flex items-center justify-between gap-3">
                  <div>
                    <p className="font-medium text-gray-800">{job.artifactName}</p>
                    <p className="text-sm text-gray-500">{job.targetNodeHostname}</p>
                    <p className="text-xs text-gray-500 mt-1">Stage: {job.deliveryStage}</p>
                  </div>
                  <span
                    className={`px-3 py-1 rounded-full text-xs font-medium ${
                      job.status === 'completed'
                        ? 'bg-emerald-100 text-emerald-800'
                        : job.status === 'running' || job.status === 'pending'
                        ? 'bg-blue-100 text-blue-800'
                        : job.status === 'failed'
                        ? 'bg-red-100 text-red-800'
                        : 'bg-gray-100 text-gray-800'
                    }`}
                  >
                    {job.status}
                  </span>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="bg-white rounded-lg shadow overflow-hidden">
          <div className="px-6 py-4 border-b border-gray-200">
            <h2 className="text-lg font-semibold text-gray-800">Recent Events</h2>
          </div>
          {events.length === 0 ? (
            <p className="p-6 text-sm text-gray-500">No audit events available.</p>
          ) : (
            <div className="divide-y divide-gray-200">
              {events.map(event => (
                <div key={event.id} className="p-4">
                  <div className="flex items-center justify-between gap-3">
                    <p className="font-medium text-gray-800">{event.title}</p>
                    <span className="text-xs uppercase tracking-wide text-gray-500">{event.type}</span>
                  </div>
                  <p className="text-sm text-gray-600 mt-1">{event.detail}</p>
                  <p className="text-xs text-gray-500 mt-2">{new Date(event.at).toLocaleString()}</p>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>
    </div>
  )
}
