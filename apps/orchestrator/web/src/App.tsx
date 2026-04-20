import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/layout/Layout'
import AgentLocal from './pages/AgentLocal'
import Dashboard from './pages/Dashboard'
import Install from './pages/Install'
import Nodes from './pages/Nodes'
import Packages from './pages/Packages'
import WorkloadRuns from './pages/WorkloadRuns'
import Workloads from './pages/Workloads'

export default function App() {
  return (
    <BrowserRouter>
      <Layout>
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/agent-local" element={<AgentLocal />} />
          <Route path="/workloads" element={<Workloads />} />
          <Route path="/workload-runs" element={<WorkloadRuns />} />
          <Route path="/nodes" element={<Nodes />} />
          <Route path="/packages" element={<Packages />} />
          <Route path="/install" element={<Install />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}
