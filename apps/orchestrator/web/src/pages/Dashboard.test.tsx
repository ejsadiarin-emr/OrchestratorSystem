import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import Dashboard from './Dashboard'
import * as api from '../services/api'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    getOrchestratorHomeData: vi.fn().mockResolvedValue({
      kpis: {
        nodesOnline: 24,
        nodesOffline: 2,
        workloadDefinitions: 6,
        runningWorkloads: 4,
        artifactsStored: 1,
        activeRuns24h: 14,
        failedRuns24h: 3,
        pendingApprovals: 2,
        controlPlaneLatencyP95Ms: 182,
      },
      nodes: [
        {
          nodeId: 'node-001',
          hostname: 'wj-plant-01',
          health: 'online',
          assignedWorkload: 'Factory Base Install',
          workloadRevision: '1.1.0',
          runState: 'update',
          lastCheckInAge: '18s',
          riskLevel: 'low',
          revisionUpdateAvailable: true,
          packageUpdatesAvailable: true,
          packageUpdateCount: 2,
        },
        {
          nodeId: 'node-002',
          hostname: 'wj-plant-02',
          health: 'warning',
          assignedWorkload: 'Observer Stack',
          workloadRevision: '0.9.0',
          runState: 'pending-approval',
          lastCheckInAge: '42s',
          riskLevel: 'med',
          revisionUpdateAvailable: true,
          packageUpdatesAvailable: false,
          reasonCode: 'approval_window_required',
        },
        {
          nodeId: 'node-003',
          hostname: 'wj-plant-03',
          health: 'offline',
          assignedWorkload: 'Factory Base Install',
          workloadRevision: '1.0.0',
          runState: 'failed',
          lastCheckInAge: '6m',
          riskLevel: 'high',
          revisionUpdateAvailable: false,
          packageUpdatesAvailable: true,
          packageUpdateCount: 1,
          reasonCode: 'heartbeat_timeout',
        },
      ],
      events: [
        {
          id: 'evt-001',
          severity: 'high',
          title: 'Pending approval requires operator action',
          detail: 'node-002 workload update is gated by approval_window_required.',
          ageLabel: '1m ago',
          nodeId: 'node-002',
          runId: 'run-004',
        },
        {
          id: 'evt-002',
          severity: 'critical',
          title: 'Node heartbeat timeout',
          detail: 'node-003 missed lease heartbeat and transitioned to failed.',
          ageLabel: '3m ago',
          nodeId: 'node-003',
          runId: 'run-005',
        },
        {
          id: 'evt-003',
          severity: 'info',
          title: 'Workload package sequencing healthy',
          detail: 'node-001 continues update progression with valid step ordering.',
          ageLabel: '10s ago',
          nodeId: 'node-001',
          runId: 'run-001',
        },
      ],
      selectedNodeId: 'node-001',
      logsByNodeId: {
        'node-001': [
          { id: 'log-001', at: new Date().toISOString(), message: 'Workload run run-001 entered package index 2.', level: 'info' },
          { id: 'log-002', at: new Date().toISOString(), message: 'StepStatus accepted for install-or-upgrade.', level: 'info' },
        ],
        'node-002': [
          { id: 'log-003', at: new Date().toISOString(), message: 'Run paused pending explicit approval window.', level: 'warn' },
          { id: 'log-004', at: new Date().toISOString(), message: 'Awaiting operator confirmation on node-local console.', level: 'info' },
        ],
        'node-003': [
          { id: 'log-005', at: new Date().toISOString(), message: 'Lease heartbeat missed beyond threshold.', level: 'error' },
          { id: 'log-006', at: new Date().toISOString(), message: 'Run marked failed with heartbeat_timeout.', level: 'error' },
        ],
      },
    }),
  }
})

describe('Dashboard orchestrator home', () => {
  it('renders locked KPI strip and nodes-first operational regions', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Nodes Online')).toBeInTheDocument()
    expect(screen.getByText('Nodes Offline')).toBeInTheDocument()
    expect(screen.getByText('Workload Definitions')).toBeInTheDocument()
    expect(screen.getByText('Running Workloads')).toBeInTheDocument()
    expect(screen.getByText('Pending Approvals')).toBeInTheDocument()
    expect(screen.getByText('Artifacts Stored')).toBeInTheDocument()
    expect(screen.getByText('Nodes Live Table')).toBeInTheDocument()
    expect(screen.getByText('Workloads Overview')).toBeInTheDocument()
    
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
    expect(screen.getByText('Factory Base Install (1.1.0)')).toBeInTheDocument()
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
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Important Events')
    expect(screen.getByText('Workload package sequencing healthy')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'critical' }))

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

  it('opens node popup from row click and shows node log content', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    await user.click(screen.getByRole('button', { name: 'Open node details node-002' }))

    expect(await screen.findByRole('heading', { name: 'Node details' })).toBeInTheDocument()
    expect(screen.getByText('Health: warning')).toBeInTheDocument()
    expect(screen.getByText('Risk: med')).toBeInTheDocument()
    expect(screen.getByText('Revision update: Yes')).toBeInTheDocument()
    expect(screen.getByText('Package signals: No')).toBeInTheDocument()
    expect(screen.getByText('Run paused pending explicit approval window.')).toBeInTheDocument()
  })

  it('supports keyboard row activation for node drilldown', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')

    const rowTrigger = screen.getByRole('button', { name: 'Open node details node-002' })
    rowTrigger.focus()
    await user.keyboard('{Enter}')

    expect(await screen.findByRole('heading', { name: 'Node details' })).toBeInTheDocument()
    expect(screen.getByText(/wj-plant-02 \(node-002\)/)).toBeInTheDocument()
  })

  it('opens workload popup from row click and shows derived signal copy', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Workloads Overview')

    await user.click(screen.getByLabelText('Open workload details Observer Stack'))

    expect(await screen.findByRole('heading', { name: 'Workload details' })).toBeInTheDocument()
    expect(screen.getByText('Mixed revisions: No')).toBeInTheDocument()
    expect(screen.getByText('Revision updates: 1 nodes')).toBeInTheDocument()
    expect(screen.getByText('Package signals: 0 nodes')).toBeInTheDocument()
    expect(screen.getByText(/Mixed revisions detected:/)).toBeInTheDocument()
    expect(screen.getByText('Impacted-node snapshots')).toBeInTheDocument()
    expect(screen.getByText('Revision update contributors')).toBeInTheDocument()
    expect(screen.getByText('Package update signal nodes')).toBeInTheDocument()
    expect(screen.getAllByText('node-002').length).toBeGreaterThan(0)
    expect(
      screen.getByText(/Package update signals are derived from node telemetry and may lag artifact-store truth/i),
    ).toBeInTheDocument()
  })

  it('supports keyboard row activation for workload drilldown', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Workloads Overview')

    const workloadTrigger = screen.getByRole('button', { name: 'Open workload details Observer Stack' })
    workloadTrigger.focus()
    await user.keyboard(' ')

    expect(await screen.findByRole('heading', { name: 'Workload details' })).toBeInTheDocument()
    expect(screen.getByText('Mixed revisions: No')).toBeInTheDocument()
  })

  it('shows mixed revision indicator and impacted-node snapshots for mixed workload', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Workloads Overview')

    await user.click(screen.getByLabelText('Open workload details Factory Base Install'))

    expect(await screen.findByRole('heading', { name: 'Workload details' })).toBeInTheDocument()
    expect(screen.getByText('Mixed revisions: Yes')).toBeInTheDocument()
    expect(screen.getByText('Revision updates: 1 nodes')).toBeInTheDocument()
    expect(screen.getByText('Package signals: 2 nodes')).toBeInTheDocument()
    expect(screen.getByText(/Mixed revisions detected:/)).toBeInTheDocument()
    expect(screen.getByText('node-001 | package signals 2')).toBeInTheDocument()
    expect(screen.getByText('node-003 | package signals 1')).toBeInTheDocument()
  })

  it('shows workload overview version column label', async () => {
    render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Workloads Overview')
    expect(screen.getByText('Revision')).toBeInTheDocument()
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

    fireEvent.mouseEnter(riskHint)

    const tooltip = await screen.findByRole('tooltip')
    expect(tooltip).toHaveAttribute('id')
    expect(riskHint).toHaveAttribute('aria-describedby', tooltip.getAttribute('id'))

    fireEvent.mouseLeave(riskHint)

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

  it('has no accessibility violations', async () => {
    const { container } = render(
      <MemoryRouter>
        <Dashboard />
      </MemoryRouter>,
    )

    await screen.findByText('Nodes Live Table')
    const results = await axe(container)
    expect(results.violations).toEqual([])
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
