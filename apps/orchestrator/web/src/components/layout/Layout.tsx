import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import Sidebar from './Sidebar'
import Topbar from './Topbar'

const pageMeta: Record<string, { title: string; description: string }> = {
  '/': {
    title: 'Node Operations Overview',
    description: 'Workload-first triage surface for node health, run actions, and node-level evidence.',
  },
  '/workloads': {
    title: 'Workload Definitions',
    description: 'Define WorkloadDefinition entities, then create immutable WorkloadRevision records with 2-3 ordered package steps.',
  },
  '/workload-runs': {
    title: 'Workload Runs',
    description:
      'Runtime flow uses AssignRun and /api/workload-runs* contracts. Timeline shows package index, step id, sequence, and status.',
  },
  '/nodes': {
    title: 'Node Enrollment',
    description:
      'Enrollment tokens are issued with POST /api/nodes/enroll. Bootstrap script needs only orchestrator URL and short-lived token.',
  },
  '/packages': {
    title: 'Artifact Packages',
    description:
      'Artifact packages are the foundational artifacts that workload revisions pin. Packages are deprecated in favor of workload-centric workflows.',
  },
  '/artifacts': {
    title: 'Artifact Store',
    description:
      'Artifact packages are the foundational artifacts that workload revisions pin. Packages are deprecated in favor of workload-centric workflows.',
  },
  '/install': {
    title: 'Artifact Store Console',
    description:
      'Stage local artifacts for workload revisions with one mocked multipart POST to /api/artifacts using required file, required manifest, and optional detachedSignature.',
  },
}

interface LayoutProps {
  children: ReactNode
}

export default function Layout({ children }: LayoutProps) {
  const location = useLocation()
  const meta = pageMeta[location.pathname] || { title: 'Orchestrator', description: 'Phase 1 workload-first operations console' }

  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] text-[var(--text-strong)] lg:flex">
      <Sidebar />
      <div className="flex min-h-screen flex-1 flex-col">
        <Topbar title={meta.title} description={meta.description} />
        <main className="flex-1 px-4 py-6 lg:px-6">{children}</main>
      </div>
    </div>
  )
}
