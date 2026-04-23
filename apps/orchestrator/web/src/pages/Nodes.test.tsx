import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Nodes from './Nodes'
import { listNodes, listEnrollmentTokens } from '../services/api'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  return {
    ...actual,
    listNodes: vi.fn().mockResolvedValue([
      {
        id: 'node-001',
        hostname: 'wj-plant-01',
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
    consumeEnrollmentToken: vi.fn().mockResolvedValue({
      id: 'node-001',
      hostname: 'wj-plant-01',
      ipAddress: '10.30.2.41',
      status: 'online',
      description: 'Plant line A host',
      osVersion: 'Windows Server 2022',
      agentVersion: '0.1.0',
      firstConnectedAt: new Date().toISOString(),
      lastSeenAt: new Date().toISOString(),
    }),
  }
})

describe('Nodes page bootstrap flow', () => {
  beforeEach(async () => {
    render(<Nodes />)
    await screen.findByText('Agent Bootstrap & Enrollment')
  })

  it('issues token and then simulates first connect with auto metadata', async () => {
    fireEvent.click(screen.getByText('Issue Token (POST)'))

    await waitFor(() => {
      expect(screen.getByText(/Issued enroll-/)).toBeInTheDocument()
    })

    fireEvent.click(screen.getByText('Simulate First Connect'))

    await waitFor(() => {
      expect(screen.getByText(/auto-collected on first connect/)).toBeInTheDocument()
    })

    expect(screen.getByText('Registered Nodes')).toBeInTheDocument()
  })
})

describe('Nodes page polling', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('sets up a 5-second polling interval on mount', async () => {
    const setIntervalSpy = vi.spyOn(window, 'setInterval')
    const clearIntervalSpy = vi.spyOn(window, 'clearInterval')

    const { unmount } = render(<Nodes />)
    await screen.findByText('Agent Bootstrap & Enrollment')

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
    await screen.findByText('Agent Bootstrap & Enrollment')

    expect(listNodes).toHaveBeenCalledTimes(1)
    expect(listEnrollmentTokens).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(5_000)

    expect(listNodes).toHaveBeenCalledTimes(2)
    expect(listEnrollmentTokens).toHaveBeenCalledTimes(2)

    await vi.advanceTimersByTimeAsync(5_000)

    expect(listNodes).toHaveBeenCalledTimes(3)
    expect(listEnrollmentTokens).toHaveBeenCalledTimes(3)

    vi.useRealTimers()
  })

  it('stops polling on unmount', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })

    const { unmount } = render(<Nodes />)
    await screen.findByText('Agent Bootstrap & Enrollment')

    expect(listNodes).toHaveBeenCalledTimes(1)

    unmount()

    await vi.advanceTimersByTimeAsync(5_000)

    expect(listNodes).toHaveBeenCalledTimes(1)
    expect(listEnrollmentTokens).toHaveBeenCalledTimes(1)

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
