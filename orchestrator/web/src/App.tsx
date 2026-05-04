import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SidebarProvider } from '@/components/ui/sidebar'
import { AppSidebar } from '@/components/AppSidebar'
import { DashboardPage } from '@/pages/DashboardPage'
import { ArtifactsPage } from '@/pages/ArtifactsPage'
import { WorkloadsPage } from '@/pages/WorkloadsPage'
import { AgentsPage } from '@/pages/AgentsPage'
import { EnrollmentPage } from '@/pages/EnrollmentPage'
import { RunsPage } from '@/pages/RunsPage'

const queryClient = new QueryClient()

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <SidebarProvider>
          <AppSidebar />
          <main className="flex-1 p-6 overflow-auto">
            <Routes>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/artifacts" element={<ArtifactsPage />} />
              <Route path="/workloads" element={<WorkloadsPage />} />
              <Route path="/agents" element={<AgentsPage />} />
              <Route path="/enrollment" element={<EnrollmentPage />} />
              <Route path="/runs" element={<RunsPage />} />
            </Routes>
          </main>
        </SidebarProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
