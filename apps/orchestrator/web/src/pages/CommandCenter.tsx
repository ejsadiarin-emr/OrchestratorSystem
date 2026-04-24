import { useState } from 'react'
import { MetricCard } from '../components/dashboard/MetricCard'
import { ExecutionsTable } from '../components/dashboard/ExecutionsTable'
import { LogInspector } from '../components/dashboard/LogInspector'
import type { MiniLogLine } from '../types'

const mockLogs: MiniLogLine[] = [
  { id: '1', at: '01:05:23', level: 'info', message: 'Starting workload deployment for node node-001' },
  { id: '2', at: '01:05:45', level: 'info', message: 'Downloading package artifacts from registry' },
  { id: '3', at: '01:06:12', level: 'info', message: 'Validating package signatures' },
  { id: '4', at: '01:06:34', level: 'warn', message: 'Retrying connection to node node-003 (attempt 2/3)' },
  { id: '5', at: '01:07:02', level: 'info', message: 'Execution started for agent research-agent' },
  { id: '6', at: '01:07:15', level: 'error', message: 'Failed to resolve package dependencies' },
  { id: '7', at: '01:07:45', level: 'info', message: 'Rollback initiated for node node-002' },
]

export default function CommandCenter() {
  const [logSheetOpen, setLogSheetOpen] = useState(false)
  const [selectedNodeId, setSelectedNodeId] = useState('node-001')

  const metrics = [
    { label: 'Nodes Online / Offline', value: '12 / 3' },
    { label: 'Active + Failed Runs (24h)', value: '8 + 2' },
    { label: 'Pending Approvals', value: '3' },
    { label: 'Control-plane Latency (p95)', value: '145 ms' },
  ]

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 xl:grid-cols-4">
        {metrics.map((metric, idx) => (
          <MetricCard key={metric.label} label={metric.label} value={metric.value} index={idx} />
        ))}
      </div>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-5 shadow-[var(--surface-shadow)]">
        <h2 className="text-base font-semibold text-[var(--text-strong)]">Recent Executions</h2>
        <div className="mt-4">
          <ExecutionsTable
            onRowClick={(exec) => {
              setSelectedNodeId(exec.agent)
              setLogSheetOpen(true)
            }}
          />
        </div>
      </section>

      <LogInspector
        open={logSheetOpen}
        onOpenChange={setLogSheetOpen}
        nodeId={selectedNodeId}
        logs={mockLogs}
      />
    </div>
  )
}
