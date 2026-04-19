export interface NavItem {
  path: string
  label: string
}

export const layoutNavItems: NavItem[] = [
  { path: '/', label: 'Dashboard' },
  { path: '/agent-local', label: 'Agent Local' },
  { path: '/workloads', label: 'Workloads' },
  { path: '/workload-runs', label: 'Workload Runs' },
  { path: '/nodes', label: 'Nodes' },
  { path: '/packages', label: 'Artifacts (Legacy)' },
  { path: '/install', label: 'New Install' },
]

export const layoutAppTitle = 'EJ Workload Orchestrator'
