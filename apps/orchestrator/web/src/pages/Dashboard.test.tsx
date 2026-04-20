import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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

    expect(await screen.findByText('Fleet Online')).toBeInTheDocument()
    expect(screen.getByText('Fleet Offline')).toBeInTheDocument()
    expect(screen.getByText('Workload Definitions')).toBeInTheDocument()
    expect(screen.getByText('Running Workloads')).toBeInTheDocument()
    expect(screen.getByText('Pending Approvals')).toBeInTheDocument()
    expect(screen.getByText('Artifacts Stored')).toBeInTheDocument()
    expect(screen.getByText('Nodes Live Table')).toBeInTheDocument()
    expect(screen.getByText('Workloads Overview')).toBeInTheDocument()
    expect(screen.getByText('Action Panel')).toBeInTheDocument()
    expect(screen.getByText('Important Events')).toBeInTheDocument()
    expect(screen.queryByText('Mini Log Viewer')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Logs node-/ })).not.toBeInTheDocument()
    expect(screen.queryByText('Filter Rail')).not.toBeInTheDocument()
  })

  it('renders multi-workload per-node view and selection details', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    expect(screen.getByText('Workload Count')).toBeInTheDocument()
    expect(screen.getByText('Workload Updates')).toBeInTheDocument()
    expect(screen.getAllByText('Factory Base Install (1.1.0)').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Observer Stack (0.9.0)').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Factory Base Install (1.0.0)').length).toBeGreaterThan(0)

    expect(screen.getByText(/Workloads:/)).toBeInTheDocument()
    expect(screen.getAllByText(/Factory Base Install \(1\.1\.0\), Observer Stack \(0\.9\.0\)/).length).toBeGreaterThan(0)
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

  it('updates action panel when selecting a node', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    expect(screen.getByText('Selected Node: node-001')).toBeInTheDocument()
    fireEvent.click(screen.getByText('node-001'))

    fireEvent.click(screen.getByText('node-002'))

    await waitFor(() => {
      expect(screen.getByText('Selected Node: node-002')).toBeInTheDocument()
    })
  })

  it('opens node drawer from row click and shows node log content', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    fireEvent.click(screen.getByText('node-002'))

    expect(await screen.findByRole('heading', { name: 'Node details' })).toBeInTheDocument()
    expect(screen.getByText('Health: warning')).toBeInTheDocument()
    expect(screen.getByText('Risk: med')).toBeInTheDocument()
    expect(screen.getByText('Revision update: Yes')).toBeInTheDocument()
    expect(screen.getByText('Package signals: No')).toBeInTheDocument()
    expect(screen.getByText('Run paused pending explicit approval window.')).toBeInTheDocument()
  })

  it('supports keyboard row activation for node drilldown', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    const rowTrigger = screen.getByRole('button', { name: 'Open node details node-002' })
    fireEvent.keyDown(rowTrigger, { key: 'Enter' })

    expect(await screen.findByRole('heading', { name: 'Node details' })).toBeInTheDocument()
    expect(screen.getByText(/node-002 \(.+\)/)).toBeInTheDocument()
  })

  it('opens workload drawer from row click and shows derived signal copy', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Workloads Overview')

    fireEvent.click(screen.getByLabelText('Open workload details Observer Stack'))

    expect(await screen.findByRole('heading', { name: 'Workload details' })).toBeInTheDocument()
    expect(screen.getByText('Mixed revisions: No')).toBeInTheDocument()
    expect(screen.getByText('Revision updates: 2 nodes')).toBeInTheDocument()
    expect(screen.getByText('Package signals: 1 nodes')).toBeInTheDocument()
    expect(screen.getByText(/Mixed revisions detected:/)).toBeInTheDocument()
    expect(screen.getByText('Impacted-node snapshots')).toBeInTheDocument()
    expect(screen.getByText('Revision update contributors')).toBeInTheDocument()
    expect(screen.getByText('Package update signal nodes')).toBeInTheDocument()
    expect(screen.getByText('node-001 | package signals 2')).toBeInTheDocument()
    expect(screen.getAllByText('node-002').length).toBeGreaterThan(0)
    expect(
      screen.getByText(/Package update signals are derived from node telemetry and may lag artifact-store truth/i),
    ).toBeInTheDocument()
  })

  it('supports keyboard row activation for workload drilldown', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Workloads Overview')

    const workloadTrigger = screen.getByRole('button', { name: 'Open workload details Observer Stack' })
    fireEvent.keyDown(workloadTrigger, { key: ' ' })

    expect(await screen.findByRole('heading', { name: 'Workload details' })).toBeInTheDocument()
    expect(screen.getByText('Mixed revisions: No')).toBeInTheDocument()
  })

  it('shows mixed revision indicator and impacted-node snapshots for mixed workload', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Workloads Overview')

    fireEvent.click(screen.getByLabelText('Open workload details Factory Base Install'))

    expect(await screen.findByRole('heading', { name: 'Workload details' })).toBeInTheDocument()
    expect(screen.getByText('Mixed revisions: Yes')).toBeInTheDocument()
    expect(screen.getByText('Revision updates: 1 nodes')).toBeInTheDocument()
    expect(screen.getByText('Package signals: 2 nodes')).toBeInTheDocument()
    expect(screen.getByText(/Mixed revisions detected:/)).toBeInTheDocument()
    expect(screen.getByText('node-001 | package signals 2')).toBeInTheDocument()
    expect(screen.getByText('node-003 | package signals 1')).toBeInTheDocument()
  })

  it('renders info hint indicators for target labels', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    expect(screen.getByRole('button', { name: 'Info: Risk (Node)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Info: Reason' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Info: Revision Updates' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Info: Package Update Signals' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Info: Nodes Running' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Info: Pending Approvals' })).toBeInTheDocument()
  })

  it('shows info hint tooltip with focus/blur and aria-describedby link', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    const nodesTable = screen.getByRole('table', { name: 'Nodes Live Table' })
    const riskHint = within(nodesTable).getByRole('button', { name: 'Info: Risk (Node)' })

    fireEvent.focus(riskHint)

    const tooltip = await screen.findByRole('tooltip')
    expect(tooltip).toHaveAttribute('id')
    expect(riskHint).toHaveAttribute('aria-describedby', tooltip.getAttribute('id'))

    fireEvent.blur(riskHint)

    await waitFor(() => {
      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
    })
  })

  it('keeps standalone mini-log section hidden by default on home body', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')
    expect(screen.queryByText('Mini Log Viewer')).not.toBeInTheDocument()
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
