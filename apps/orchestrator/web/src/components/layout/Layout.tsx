import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import Sidebar from './Sidebar'
import Topbar from './Topbar'

const pageTitles: Record<string, string> = {
  '/': 'Node Operations Overview',
  '/workloads': 'Workload Definitions',
  '/workload-runs': 'Workload Runs',
  '/nodes': 'Node Enrollment',
  '/packages': 'Artifact Packages',
  '/install': 'Artifact Store Console',
}

interface LayoutProps {
  children: ReactNode
}

export default function Layout({ children }: LayoutProps) {
  const location = useLocation()
  const title = pageTitles[location.pathname] || 'Orchestrator'

  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] text-[var(--text-strong)] lg:flex">
      <Sidebar />
      <div className="flex min-h-screen flex-1 flex-col">
        <Topbar title={title} />
        <main className="flex-1 px-4 py-6 lg:px-6">{children}</main>
      </div>
    </div>
  )
}
