import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import WorkloadRuns from './WorkloadRuns'
import { createWorkloadRun } from '../services/api'

vi.mock('../services/realtime', () => ({
  subscribeToRunProgress: vi.fn().mockReturnValue(() => {}),
}))

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    listWorkloadRuns: vi.fn().mockResolvedValue([
      {
        id: 'run-001',
        workloadId: 'workload-001',
        workloadName: 'Factory Base Install',
        workloadRevision: '1.1.0',
        mode: 'install',
        targetNodeIds: ['node-001'],
        targetNodeHostnames: ['wj-plant-01'],
        status: 'running',
        createdAt: new Date().toISOString(),
        timeline: [],
      },
      {
        id: 'run-003',
        workloadId: 'workload-001',
        workloadName: 'Factory Base Install',
        workloadRevision: '1.1.0',
        mode: 'install',
        targetNodeIds: ['node-001'],
        targetNodeHostnames: ['wj-plant-01'],
        status: 'pending',
        createdAt: new Date().toISOString(),
        timeline: [],
      },
    ]),
    createWorkloadRun: vi.fn().mockResolvedValue({
      id: 'run-003',
      workloadId: 'workload-001',
      workloadName: 'Factory Base Install',
      workloadRevision: '1.1.0',
      mode: 'install',
      targetNodeIds: ['node-001'],
      targetNodeHostnames: ['wj-plant-01'],
      status: 'pending',
      createdAt: new Date().toISOString(),
      timeline: [],
    }),
    listNodes: vi.fn().mockResolvedValue([
      { id: 'node-001', hostname: 'wj-plant-01', displayName: 'Plant Line A', ipAddress: '10.30.2.41', status: 'online', description: '', osVersion: 'Windows Server 2022', agentVersion: '', firstConnectedAt: '', lastSeenAt: '' },
      { id: 'node-002', hostname: 'wj-plant-02', displayName: 'Plant Line B', ipAddress: '10.30.2.42', status: 'online', description: '', osVersion: 'Ubuntu 24.04', agentVersion: '', firstConnectedAt: '', lastSeenAt: '' },
      { id: 'node-003', hostname: 'wj-plant-03', displayName: 'Plant Line C', ipAddress: '10.30.2.43', status: 'offline', description: '', osVersion: 'Windows Server 2022', agentVersion: '', firstConnectedAt: '', lastSeenAt: '' },
    ]),
    listWorkloads: vi.fn().mockResolvedValue([
      { id: 'workload-001', name: 'Factory Base Install', description: '', createdAt: new Date().toISOString() },
    ]),
    getWorkload: vi.fn().mockResolvedValue({
      id: 'workload-001',
      name: 'Factory Base Install',
      description: '',
      createdAt: new Date().toISOString(),
      revisions: [
        {
          id: 'wrv-001',
          workloadId: 'workload-001',
          revision: '1.0.0',
          state: 'published',
          createdAt: new Date(Date.now() - 60000).toISOString(),
          publishedAt: new Date(Date.now() - 60000).toISOString(),
          packageSteps: [],
        },
        {
          id: 'wrv-002',
          workloadId: 'workload-001',
          revision: '1.1.0',
          state: 'published',
          createdAt: new Date().toISOString(),
          publishedAt: new Date().toISOString(),
          packageSteps: [],
        },
      ],
    }),
    getWorkloadRunSteps: vi.fn().mockResolvedValue([]),
    cancelWorkloadRun: vi.fn().mockResolvedValue({
      id: 'run-001',
      workloadId: 'workload-001',
      workloadName: '',
      workloadRevision: '',
      mode: 'install',
      targetNodeIds: [],
      targetNodeHostnames: [],
      status: 'cancelled',
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
      timeline: [],
    }),
    advanceWorkloadRun: vi.fn(),
  }
})

describe('Workload Runs page', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  beforeEach(async () => {
    render(<WorkloadRuns />)
    await screen.findByText('Create Workload Run')
  })

  it('opens run creator popup and creates a run', async () => {
    expect(screen.getByRole('button', { name: 'Open Run Creator' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Open Run Creator' }))
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByText('Create Workload Run')).toBeInTheDocument()
    expect(within(dialog).getAllByText('install').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('update').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('rollback').length).toBeGreaterThan(0)
    expect(within(dialog).queryByText('cancel')).not.toBeInTheDocument()

    // wait for workload details to load and revision dropdown to populate
    await waitFor(() => {
      expect(within(dialog).getByLabelText('Revision')).toBeInTheDocument()
    })

    // verify revision dropdown shows published revisions
    const revisionSelect = within(dialog).getByLabelText('Revision')
    expect(within(revisionSelect).getByText('1.0.0')).toBeInTheDocument()
    expect(within(revisionSelect).getByText('1.1.0')).toBeInTheDocument()

    // select all online nodes via helper link
    fireEvent.click(within(dialog).getByRole('button', { name: 'Select all online' }))

    // verify online nodes checked, offline node not
    const nodeCheckbox1 = within(dialog).getByLabelText('Plant Line A')
    const nodeCheckbox2 = within(dialog).getByLabelText('Plant Line B')
    const nodeCheckbox3 = within(dialog).getByLabelText('Plant Line C')
    expect(nodeCheckbox1).toBeChecked()
    expect(nodeCheckbox2).toBeChecked()
    expect(nodeCheckbox3).not.toBeChecked()
    expect(nodeCheckbox3).toBeDisabled()

    // click Create Run to reveal summary
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create Run' }))

    // summary should appear with confirm button and correct details
    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: 'Confirm Create Run' })).toBeInTheDocument()
    })
    expect(within(dialog).getByText('Factory Base Install')).toBeInTheDocument()
    expect(within(dialog).getByText('1.1.0')).toBeInTheDocument()
    expect(within(dialog).getByText(/2 selected/)).toBeInTheDocument()

    // click confirm
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm Create Run' }))

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    expect(screen.getByText('run-003')).toBeInTheDocument()
  })

  it('opens diagnostics popup from run row', async () => {
    const row = screen.getByText('run-001').closest('tr')
    expect(row).not.toBeNull()
    fireEvent.click(row as HTMLTableRowElement)
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByText('Run diagnostics')).toBeInTheDocument()
    expect(within(dialog).getByText('Timeline stream')).toBeInTheDocument()
    expect(within(dialog).getByText('workload run timeline')).toBeInTheDocument()
  })

  it('describes workload-runs runtime contracts', () => {
    expect(screen.getByText('Create Workload Run')).toBeInTheDocument()
  })

  it('renders offline nodes disabled with visual cue and prevents selection', async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Open Run Creator' }))
    const dialog = await screen.findByRole('dialog')

    const offlineCheckbox = within(dialog).getByLabelText('Plant Line C')
    expect(offlineCheckbox).toBeDisabled()
    expect(offlineCheckbox).not.toBeChecked()

    // clicking disabled checkbox does nothing
    fireEvent.click(offlineCheckbox)
    expect(offlineCheckbox).not.toBeChecked()
  })

  it('shows error and keeps modal open on creation failure', async () => {
    vi.mocked(createWorkloadRun).mockRejectedValueOnce(new Error('Backend unavailable'))

    fireEvent.click(screen.getByRole('button', { name: 'Open Run Creator' }))
    const dialog = await screen.findByRole('dialog')

    await waitFor(() => {
      expect(within(dialog).getByLabelText('Revision')).toBeInTheDocument()
    })

    fireEvent.click(within(dialog).getByRole('button', { name: 'Select all online' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create Run' }))

    await waitFor(() => {
      expect(within(dialog).getByRole('button', { name: 'Confirm Create Run' })).toBeInTheDocument()
    })

    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm Create Run' }))

    await waitFor(() => {
      expect(within(dialog).getByText('Backend unavailable')).toBeInTheDocument()
    })

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Confirm Create Run' })).not.toBeDisabled()
  })
})
