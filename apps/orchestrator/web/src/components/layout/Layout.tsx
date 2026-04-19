import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import Sidebar from './Sidebar'
import Topbar from './Topbar'

const pageTitles: Record<string, string> = {
  '/': 'Overview',
  '/workloads': 'Pipelines',
  '/workload-runs': 'Logs',
  '/nodes': 'Workers',
  '/packages': 'Packages',
  '/agent-local': 'Agent',
  '/install': 'Settings',
}

interface LayoutProps {
  children: ReactNode
}

export default function Layout({ children }: LayoutProps) {
  const location = useLocation()
  const title = pageTitles[location.pathname] || 'Orchestrator'

  return (
    <div className="flex min-h-screen bg-background">
      <Sidebar />
      <div className="flex-1 flex flex-col">
        <Topbar title={title} />
        <main className="flex-1 p-6">{children}</main>
      </div>
    </div>
  )
}