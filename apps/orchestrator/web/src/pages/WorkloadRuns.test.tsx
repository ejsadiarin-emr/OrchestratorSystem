import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import WorkloadRuns from './WorkloadRuns'

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
      { id: 'node-001', hostname: 'wj-plant-01', ipAddress: '10.30.2.41', status: 'online', description: '', osVersion: '', agentVersion: '', firstConnectedAt: '', lastSeenAt: '' },
    ]),
    listWorkloads: vi.fn().mockResolvedValue([
      { id: 'workload-001', name: 'Factory Base Install', description: '', createdAt: new Date().toISOString() },
    ]),
    getWorkloadRunSteps: vi.fn().mockResolvedValue([]),
    cancelWorkloadRun: vi.fn().mockResolvedValue({
      id: 'run-001',
      workloadId: 'workload-001',
      workloadName: '',
      workloadRevision: '',
      mode: 'cancel',
      targetNodeIds: [],
      targetNodeHostnames: [],
      status: 'cancelled',
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
      timeline: [],
    }),
  }
})

describe('Workload Runs page', () => {
  beforeEach(async () => {
    render(<WorkloadRuns />)
    await screen.findByText('Workload Runs')
  })

  it('opens run creator popup and creates a run', async () => {
    expect(screen.getByRole('button', { name: 'Open Run Creator' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Open Run Creator' }))
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByText('Create Workload Run')).toBeInTheDocument()
    expect(within(dialog).getAllByText('install').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('update').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('rollback').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('cancel').length).toBeGreaterThan(0)

    fireEvent.change(within(dialog).getByLabelText('Workload'), {
      target: { value: 'workload-001' },
    })
    fireEvent.change(within(dialog).getByLabelText('Revision'), {
      target: { value: 'rev-001' },
    })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create Run' }))

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
    expect(screen.getAllByText('/api/workload-runs', { exact: false }).length).toBeGreaterThan(0)
  })
})
