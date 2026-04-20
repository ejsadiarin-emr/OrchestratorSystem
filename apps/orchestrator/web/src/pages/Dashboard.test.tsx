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
    expect(screen.getByText('Workload Definitions')).toBeInTheDocument()
    expect(screen.getByText('Running Workloads')).toBeInTheDocument()
    expect(screen.getByText('Active + Failed Runs (24h)')).toBeInTheDocument()
    expect(screen.getByText('Pending Approvals')).toBeInTheDocument()
    expect(screen.getByText('Control-plane Latency (p95)')).toBeInTheDocument()
    expect(screen.getByText('Nodes Live Table')).toBeInTheDocument()
    expect(screen.getByText('Action Panel')).toBeInTheDocument()
    expect(screen.getByText('Important Events')).toBeInTheDocument()
    expect(screen.getByText('Mini Log Viewer')).toBeInTheDocument()
  })

  it('renders explicit update indicators and events severity filters', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    expect(screen.getAllByText('Revision update').length).toBeGreaterThan(0)
    expect(screen.getAllByText(/Package updates/).length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'all' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'critical' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'high' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'medium' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'info' })).toBeInTheDocument()
  })

  it('filters right-rail events by severity', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Important Events')
    expect(screen.getByText('Workload package sequencing healthy')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'critical' }))

    await waitFor(() => {
      expect(screen.queryByText('Workload package sequencing healthy')).not.toBeInTheDocument()
    })
    expect(screen.getByText('Node heartbeat timeout')).toBeInTheDocument()
  })

  it('shows auto-refresh metadata in dashboard header', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    expect(await screen.findByText(/Auto-refresh:/)).toBeInTheDocument()
    expect(screen.getByText(/Last updated:/)).toBeInTheDocument()
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
