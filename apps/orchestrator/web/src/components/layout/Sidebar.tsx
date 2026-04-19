import { NavLink } from 'react-router-dom'
import {
  LayoutDashboard,
  GitBranch,
  Server,
  FileText,
  Settings,
} from 'lucide-react'

import { cn } from '@/lib/utils'

const navItems = [
  { to: '/', icon: LayoutDashboard, label: 'Overview' },
  { to: '/workloads', icon: GitBranch, label: 'Pipelines' },
  { to: '/nodes', icon: Server, label: 'Workers' },
  { to: '/workload-runs', icon: FileText, label: 'Logs' },
  { to: '/install', icon: Settings, label: 'Settings' },
]

export default function Sidebar() {
  return (
    <aside className="w-56 h-screen border-r border-border bg-sidebar flex flex-col">
      <div className="p-4 border-b border-sidebar-border">
        <h1 className="text-lg font-semibold text-sidebar-foreground">
          Orchestrator
        </h1>
      </div>
      <nav className="flex-1 p-2">
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-2 px-3 py-2 rounded-md text-sm transition-colors',
                isActive
                  ? 'bg-sidebar-accent text-sidebar-accent-foreground'
                  : 'text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground'
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