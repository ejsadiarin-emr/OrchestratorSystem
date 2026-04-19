import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AgentLocal from './AgentLocal'
import * as api from '../services/api'

const summaryFixture = {
  nodeId: 'node-001',
  hostname: 'wj-plant-01',
  health: 'online' as const,
  runState: 'pending-approval' as const,
  currentWorkload: 'Factory Base Install',
  targetRevision: '1.1.0',
  installedRevision: '1.0.0',
  pendingApproval: true,
  riskLevel: 'low' as const,
}

const logFixture = [
  {
    id: 'agent-log-001',
    at: '2026-04-16T12:34:00.000Z',
    message: 'Pre-check cache hydrated for target revision 1.1.0.',
    level: 'info' as const,
  },
]

describe('AgentLocal page', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.spyOn(api, 'getAgentLocalSummary').mockResolvedValue(summaryFixture)
    vi.spyOn(api, 'listAgentLocalLogs').mockResolvedValue(logFixture)
  })

  it('renders node status, guided update, mini logs, and diagnostics action', async () => {
    render(<AgentLocal />)

    expect(await screen.findByText('Agent Local Console')).toBeInTheDocument()
    expect(screen.getByText('Node and Run Status')).toBeInTheDocument()
    expect(screen.getByText('Guided Update Flow')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Run Pre-check' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Start Guided Update' })).toBeInTheDocument()
    expect(screen.getByText('Mini Log Viewer')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export Diagnostics' })).toBeInTheDocument()
  })

  it('requires explicit confirmation before starting guided update', async () => {
    const startSpy = vi.spyOn(api, 'startAgentGuidedUpdate').mockResolvedValue({ accepted: true, status: 'update' })

    render(<AgentLocal />)

    const startButton = await screen.findByRole('button', { name: 'Start Guided Update' })
    const confirmCheckbox = screen.getByRole('checkbox', {
      name: 'I confirm this workload update is approved for this node.',
    })

    expect(startButton).toBeDisabled()
    fireEvent.click(startButton)
    expect(startSpy).not.toHaveBeenCalled()

    fireEvent.click(confirmCheckbox)
    expect(startButton).toBeEnabled()

    fireEvent.click(startButton)

    await waitFor(() => {
      expect(startSpy).toHaveBeenCalledTimes(1)
    })
  })

  it('shows pre-check result and diagnostics export notices', async () => {
    vi.spyOn(api, 'runAgentPrecheck').mockResolvedValue({
      passed: true,
      detail: 'Disk, signature chain, and rollback prerequisites validated.',
    })
    vi.spyOn(api, 'exportAgentDiagnostics').mockResolvedValue({
      fileName: 'diagnostics-node-001.zip',
      generatedAt: '2026-04-16T12:36:00.000Z',
    })

    render(<AgentLocal />)

    fireEvent.click(await screen.findByRole('button', { name: 'Run Pre-check' }))
    expect(await screen.findByText('Pre-check passed: Disk, signature chain, and rollback prerequisites validated.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Export Diagnostics' }))
    expect(await screen.findByText('Diagnostics exported: diagnostics-node-001.zip (generated 2026-04-16T12:36:00.000Z).')).toBeInTheDocument()
  })

  it('shows pre-check failure when prerequisites validation fails', async () => {
    vi.spyOn(api, 'runAgentPrecheck').mockResolvedValue({
      passed: false,
      detail: 'Rollback snapshot missing.',
    })

    render(<AgentLocal />)

    fireEvent.click(await screen.findByRole('button', { name: 'Run Pre-check' }))
    expect(await screen.findByText('Pre-check failed: Rollback snapshot missing.')).toBeInTheDocument()
  })

  it('shows diagnostics export failure notice when export fails', async () => {
    vi.spyOn(api, 'exportAgentDiagnostics').mockRejectedValue(new Error('export failed'))

    render(<AgentLocal />)

    fireEvent.click(await screen.findByRole('button', { name: 'Export Diagnostics' }))
    expect(await screen.findByText('Diagnostics export failed. Try again from this workload context.')).toBeInTheDocument()
  })
})
