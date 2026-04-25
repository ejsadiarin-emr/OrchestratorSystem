import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Nodes from './Nodes'
import { listNodes, listEnrollmentTokens, updateNodeDisplayName, deleteNode } from '../services/api'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    listNodes: vi.fn().mockResolvedValue([
      {
        id: 'node-001',
        hostname: 'wj-plant-01',
        displayName: 'Plant Line A',
        ipAddress: '10.30.2.41',
        status: 'online',
        description: 'Plant line A host',
        osVersion: 'Windows Server 2022',
        agentVersion: '0.1.0',
        firstConnectedAt: new Date().toISOString(),
        lastSeenAt: new Date().toISOString(),
      },
    ]),
    listEnrollmentTokens: vi.fn().mockResolvedValue([]),
    issueEnrollmentToken: vi.fn().mockResolvedValue({
      tokenId: 'token-new',
      token: 'enroll-abc123',
      issuedAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
      requestedBy: 'qa.user',
      orchestratorUrl: 'https://orch.example.local:5000',
      singleUse: true,
      used: false,
    }),
    updateNodeDisplayName: vi.fn().mockResolvedValue({
      id: 'node-001',
      hostname: 'wj-plant-01',
      displayName: 'Renamed Node',
      ipAddress: '10.30.2.41',
      status: 'online',
      description: 'Plant line A host',
      osVersion: 'Windows Server 2022',
      agentVersion: '0.1.0',
      firstConnectedAt: new Date().toISOString(),
      lastSeenAt: new Date().toISOString(),
    }),
    deleteNode: vi.fn().mockResolvedValue(undefined),
  }
})

describe('Nodes page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders registered nodes and enrollment tokens sections', async () => {
    render(<Nodes />)
    await screen.findByText('Registered Nodes')
    expect(screen.getByText('Enrollment Tokens')).toBeInTheDocument()
    expect(screen.getByText('Plant Line A')).toBeInTheDocument()
  })

  it('opens token creation modal and shows result', async () => {
    render(<Nodes />)
    await screen.findByText('Registered Nodes')

    fireEvent.click(screen.getByText('Generate Token'))
    await screen.findByText('Generate Enrollment Token')

    fireEvent.click(screen.getByText('Generate'))

    await waitFor(() => {
      expect(screen.getByText('Enrollment Token Created')).toBeInTheDocument()
    })

    expect(screen.getByText('enroll-abc123')).toBeInTheDocument()
  })

  it('allows inline rename of a node', async () => {
    render(<Nodes />)
    await screen.findByText('Plant Line A')

    const renameButton = screen.getByTitle('Rename')
    fireEvent.click(renameButton)

    const input = screen.getByDisplayValue('Plant Line A')
    fireEvent.change(input, { target: { value: 'Renamed Node' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    await waitFor(() => {
      expect(updateNodeDisplayName).toHaveBeenCalledWith('node-001', 'Renamed Node')
    })
  })

  it('allows deleting a node with confirmation', async () => {
    render(<Nodes />)
    await screen.findByText('Plant Line A')

    const deleteButton = screen.getByTitle('Delete')
    fireEvent.click(deleteButton)

    await screen.findByText('Delete Node')
    expect(screen.getByText(/Are you sure/)).toBeInTheDocument()

    fireEvent.click(screen.getByText('Delete'))

    await waitFor(() => {
      expect(deleteNode).toHaveBeenCalledWith('node-001')
    })
  })
})

describe('Nodes page polling', () => {
  it('sets up a 5-second polling interval on mount', async () => {
    const setIntervalSpy = vi.spyOn(window, 'setInterval')
    const clearIntervalSpy = vi.spyOn(window, 'clearInterval')

    const { unmount } = render(<Nodes />)
    await screen.findByText('Registered Nodes')

    const intervalCalls = setIntervalSpy.mock.calls.filter(call => call[1] === 5_000)
    expect(intervalCalls.length).toBeGreaterThanOrEqual(1)
    expect(intervalCalls[intervalCalls.length - 1][0]).toEqual(expect.any(Function))

    unmount()

    expect(clearIntervalSpy).toHaveBeenCalled()

    setIntervalSpy.mockRestore()
    clearIntervalSpy.mockRestore()
  })

  it('polls node list and tokens every 5 seconds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    render(<Nodes />)
    await screen.findByText('Registered Nodes')

    const initialCalls = vi.mocked(listNodes).mock.calls.length
    const initialTokenCalls = vi.mocked(listEnrollmentTokens).mock.calls.length

    await vi.advanceTimersByTimeAsync(5_000)

    expect(listNodes).toHaveBeenCalledTimes(initialCalls + 1)
    expect(listEnrollmentTokens).toHaveBeenCalledTimes(initialTokenCalls + 1)

    await vi.advanceTimersByTimeAsync(5_000)

    expect(listNodes).toHaveBeenCalledTimes(initialCalls + 2)
    expect(listEnrollmentTokens).toHaveBeenCalledTimes(initialTokenCalls + 2)

    vi.useRealTimers()
  })

  it('stops polling on unmount', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    const { unmount } = render(<Nodes />)
    await screen.findByText('Registered Nodes')

    const callsAfterMount = vi.mocked(listNodes).mock.calls.length

    unmount()

    await vi.advanceTimersByTimeAsync(5_000)

    expect(listNodes).toHaveBeenCalledTimes(callsAfterMount)
    expect(listEnrollmentTokens).toHaveBeenCalledTimes(callsAfterMount)

    vi.useRealTimers()
  })
})

describe('Nodes page status badges', () => {
  it('renders colored status badges for each node status', async () => {
    const mockedListNodes = vi.mocked(listNodes)
    mockedListNodes.mockResolvedValueOnce([
      {
        id: 'node-online',
        hostname: 'online-host',
        displayName: 'Online Host',
        ipAddress: '10.0.0.1',
        status: 'online',
        description: '',
        osVersion: '',
        agentVersion: '',
        firstConnectedAt: new Date().toISOString(),
        lastSeenAt: new Date().toISOString(),
      },
      {
        id: 'node-offline',
        hostname: 'offline-host',
        displayName: 'Offline Host',
        ipAddress: '10.0.0.2',
        status: 'offline',
        description: '',
        osVersion: '',
        agentVersion: '',
        firstConnectedAt: new Date().toISOString(),
        lastSeenAt: new Date().toISOString(),
      },
      {
        id: 'node-installing',
        hostname: 'installing-host',
        displayName: 'Installing Host',
        ipAddress: '10.0.0.3',
        status: 'installing',
        description: '',
        osVersion: '',
        agentVersion: '',
        firstConnectedAt: new Date().toISOString(),
        lastSeenAt: new Date().toISOString(),
      },
      {
        id: 'node-enrolling',
        hostname: 'enrolling-host',
        displayName: 'Enrolling Host',
        ipAddress: '10.0.0.4',
        status: 'enrolling',
        description: '',
        osVersion: '',
        agentVersion: '',
        firstConnectedAt: new Date().toISOString(),
        lastSeenAt: new Date().toISOString(),
      },
    ])

    render(<Nodes />)
    await screen.findByText('Registered Nodes')

    const onlineBadge = screen.getByText('online')
    expect(onlineBadge.className).toMatch(/bg-emerald-/)

    const offlineBadge = screen.getByText('offline')
    expect(offlineBadge.className).toMatch(/bg-slate-/)

    const installingBadge = screen.getByText('installing')
    expect(installingBadge.className).toMatch(/bg-amber-/)

    const enrollingBadge = screen.getByText('enrolling')
    expect(enrollingBadge.className).toMatch(/bg-blue-/)
  })
})
