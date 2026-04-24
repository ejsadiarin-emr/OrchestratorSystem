import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/layout/Layout'
import ArtifactStore from './pages/ArtifactStore'
import Dashboard from './pages/Dashboard'
import Nodes from './pages/Nodes'
import WorkloadRuns from './pages/WorkloadRuns'
import Workloads from './pages/Workloads'

export default function App() {
  return (
    <BrowserRouter>
      <Layout>
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/workloads" element={<Workloads />} />
          <Route path="/workload-runs" element={<WorkloadRuns />} />
          <Route path="/nodes" element={<Nodes />} />
          <Route path="/artifacts" element={<ArtifactStore />} />
          <Route path="/packages" element={<ArtifactStore />} />
          <Route path="*" element={<Dashboard />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}
