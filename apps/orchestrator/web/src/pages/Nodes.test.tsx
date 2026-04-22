import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Nodes from './Nodes'

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
