import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import Dashboard from './Dashboard'
import * as api from '../services/api'

describe('Dashboard orchestrator home', () => {
  it('renders locked KPI strip and nodes-first operational regions', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Fleet Online / Offline')).toBeInTheDocument()
    expect(screen.getByText('Active + Failed Runs (24h)')).toBeInTheDocument()
    expect(screen.getByText('Pending Approvals')).toBeInTheDocument()
    expect(screen.getByText('Control-plane Latency (p95)')).toBeInTheDocument()
    expect(screen.getByText('Nodes Live Table')).toBeInTheDocument()
    expect(screen.getByText('Action Panel')).toBeInTheDocument()
    expect(screen.getByText('Important Events')).toBeInTheDocument()
    expect(screen.getByText('Mini Log Viewer')).toBeInTheDocument()
  })

  it('updates action panel and mini log viewer when selecting a node', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    expect(screen.getByText('Selected Node: node-001')).toBeInTheDocument()
    expect(screen.getByText('Workload run run-001 entered package index 2.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Select node-002' }))

    await waitFor(() => {
      expect(screen.getByText('Selected Node: node-002')).toBeInTheDocument()
    })

    expect(screen.getByText('Run paused pending explicit approval window.')).toBeInTheDocument()
  })

  it('renders fallback error message when orchestrator home data fetch fails', async () => {
    const mock = vi.spyOn(api, 'getOrchestratorHomeData').mockRejectedValueOnce(new Error('network down'))

    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Failed to load dashboard data.')).toBeInTheDocument()

    mock.mockRestore()
  })
})
