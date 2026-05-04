import { Link, useLocation } from 'react-router-dom'
import { LayoutDashboard, Package, Boxes, Cpu, KeyRound, PlayCircle } from 'lucide-react'

const navItems = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/artifacts', label: 'Artifacts', icon: Package },
  { to: '/workloads', label: 'Workloads', icon: Boxes },
  { to: '/agents', label: 'Agents', icon: Cpu },
  { to: '/enrollment', label: 'Enrollment', icon: KeyRound },
  { to: '/runs', label: 'Runs', icon: PlayCircle },
]

export function AppSidebar() {
  const location = useLocation()

  return (
    <aside className="w-64 border-r bg-background flex flex-col">
      <div className="p-4 border-b">
        <h2 className="text-lg font-bold tracking-tight">Orchestrator</h2>
      </div>
      <nav className="flex-1 p-3 space-y-1">
        {navItems.map((item) => {
          const isActive = location.pathname === item.to
          return (
            <Link
              key={item.to}
              to={item.to}
              className={`flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                isActive
                  ? 'bg-primary text-primary-foreground'
                  : 'text-foreground hover:bg-muted'
              }`}
            >
              <item.icon className="h-4 w-4" />
              {item.label}
            </Link>
          )
        })}
      </nav>
    </aside>
  )
}
