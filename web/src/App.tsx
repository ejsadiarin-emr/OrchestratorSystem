import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Dashboard from './pages/Dashboard'
import Install from './pages/Install'
import Jobs from './pages/Jobs'
import Nodes from './pages/Nodes'
import Packages from './pages/Packages'

export default function App() {
  return (
    <BrowserRouter>
      <Layout>
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/nodes" element={<Nodes />} />
          <Route path="/packages" element={<Packages />} />
          <Route path="/jobs" element={<Jobs />} />
          <Route path="/install" element={<Install />} />
        </Routes>
      </Layout>
    </BrowserRouter>
  )
}
