import { Link, useLocation } from 'react-router-dom'
import { LayoutDashboard, Package, Boxes, Cpu, KeyRound, PlayCircle } from 'lucide-react'
import {
  Sidebar,
  SidebarContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from '@/components/ui/sidebar'

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
    <Sidebar>
      <SidebarHeader>
        <h2 className="text-lg font-bold tracking-tight px-2 py-1">Orchestrator</h2>
      </SidebarHeader>
      <SidebarContent>
        <SidebarMenu>
          {navItems.map((item) => {
            const isActive = location.pathname === item.to
            return (
              <SidebarMenuItem key={item.to}>
                <SidebarMenuButton asChild isActive={isActive}>
                  <Link to={item.to}>
                    <item.icon className="h-4 w-4" />
                    <span>{item.label}</span>
                  </Link>
                </SidebarMenuButton>
              </SidebarMenuItem>
            )
          })}
        </SidebarMenu>
      </SidebarContent>
    </Sidebar>
  )
}
