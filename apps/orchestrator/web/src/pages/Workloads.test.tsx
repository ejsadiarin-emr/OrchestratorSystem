import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Workloads from './Workloads'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  const mockArtifacts = [
    {
      id: 'EJ-Installer-2.0.0',
      fileName: 'EJ-Installer-2.0.0.msi',
      createdAt: new Date().toISOString(),
      detachedSignaturePresent: false,
      manifest: {
        packageId: 'EJ-Installer',
        version: '2.0.0',
        channel: 'test',
        artifactType: 'msi',
      },
    },
    {
      id: 'EJ-Installer-2.1.0',
      fileName: 'EJ-Installer-2.1.0.msi',
      createdAt: new Date().toISOString(),
      detachedSignaturePresent: false,
      manifest: {
        packageId: 'EJ-Installer',
        version: '2.1.0',
        channel: 'test',
        artifactType: 'msi',
      },
    },
  ]
  const mockWorkloads = [
    {
      id: 'workload-001',
      name: 'Factory Base Install',
      description: 'Baseline package set for production line nodes.',
      createdAt: new Date().toISOString(),
    },
  ]
  const mockRevisions = [
    {
      id: 'wrv-new',
      workloadId: 'workload-001',
      revision: '2.0.0',
      state: 'draft',
      createdAt: new Date().toISOString(),
      packageSteps: [{ packageId: 'pkg-runtime', packageName: '', packageVersion: '', packageIndex: 1, stepId: 'step-1' }],
    },
  ]
  return {
    ...actual,
    listArtifacts: vi.fn().mockResolvedValue(mockArtifacts),
    listWorkloads: vi.fn().mockResolvedValue(mockWorkloads),
    getWorkload: vi.fn().mockResolvedValue({
      ...mockWorkloads[0],
      revisions: mockRevisions,
    }),
    createWorkloadRevision: vi.fn().mockResolvedValue({
      id: 'wrv-new',
      workloadId: 'workload-001',
      revision: '2.0.0',
      state: 'draft',
      createdAt: new Date().toISOString(),
      packageSteps: [{ packageId: 'pkg-runtime', packageName: '', packageVersion: '', packageIndex: 1, stepId: 'step-1' }],
    }),
    publishWorkloadRevision: vi.fn().mockResolvedValue({
      id: 'wrv-new',
      workloadId: 'workload-001',
      revision: '2.0.0',
      state: 'published',
      createdAt: new Date().toISOString(),
      publishedAt: new Date().toISOString(),
      packageSteps: [{ packageId: 'pkg-runtime', packageName: '', packageVersion: '', packageIndex: 1, stepId: 'step-1' }],
    }),
    deleteWorkload: vi.fn().mockResolvedValue(undefined),
  }
})

describe('Workloads page', () => {
  beforeEach(async () => {
    render(<Workloads />)
    await screen.findByText('Workload Definitions')
  })

  it('renders workload cards with latest revision info', async () => {
    expect(screen.getByText('Definitions and Latest Revision')).toBeInTheDocument()
    expect(screen.getByText('Factory Base Install')).toBeInTheDocument()
    expect(screen.getByText('Baseline package set for production line nodes.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'View Details' })).toBeInTheDocument()
  })

  it('opens workload detail modal showing revisions', async () => {
    fireEvent.click(screen.getByRole('button', { name: 'View Details' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Workload details')).toBeInTheDocument()
    expect(within(dialog).getByText('Factory Base Install')).toBeInTheDocument()
    expect(within(dialog).getByText('Revisions')).toBeInTheDocument()
    expect(within(dialog).getByText('2.0.0')).toBeInTheDocument()
    expect(within(dialog).getByText('draft')).toBeInTheDocument()
  })

})
