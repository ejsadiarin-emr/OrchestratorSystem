import { NavLink } from 'react-router-dom'
import {
  LayoutDashboard,
  GitBranch,
  Server,
  FileText,
  Laptop,
  Package,
} from 'lucide-react'

import { cn } from '@/lib/utils'

const navItems = [
  { to: '/', icon: LayoutDashboard, label: 'Home' },
  { to: '/workloads', icon: GitBranch, label: 'Workloads' },
  { to: '/workload-runs', icon: FileText, label: 'Workload Runs' },
  { to: '/nodes', icon: Server, label: 'Nodes' },
  { to: '/artifacts', icon: Package, label: 'Artifacts' },
  { to: '/agent-local', icon: Laptop, label: 'Agent Local' },
]

export default function Sidebar() {
  return (
    <aside className="w-full border-b border-[var(--surface-border)] bg-[var(--surface-glass)] backdrop-blur lg:h-screen lg:w-64 lg:border-b-0 lg:border-r">
      <div className="border-b border-[var(--surface-border)] px-4 py-4 lg:px-5">
        <h1 className="text-lg font-semibold text-[var(--text-strong)]">
          Orchestrator
        </h1>
        <p className="mt-1 text-xs text-[var(--text-soft)]">Workload-first operations console</p>
      </div>
      <nav className="flex gap-2 overflow-x-auto p-3 lg:flex-col lg:overflow-visible lg:p-4">
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              cn(
                'inline-flex shrink-0 items-center gap-2 rounded-lg px-3 py-2 text-sm transition-colors lg:flex',
                isActive
                  ? 'bg-[var(--accent)] text-white shadow-sm'
                  : 'text-[var(--text-soft)] hover:bg-[var(--surface-subtle)] hover:text-[var(--text-strong)]'
              )
            }
          >
            <item.icon className="size-4" />
            {item.label}
          </NavLink>
        ))}
      </nav>
    </aside>
  )
}
