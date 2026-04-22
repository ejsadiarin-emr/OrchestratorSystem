import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../services/api'
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
  return {
    ...actual,
    uploadArtifact: vi.fn().mockResolvedValue({
      artifact: mockArtifacts[0],
      steps: [
        { id: 'upload', label: 'Receive multipart request', status: 'completed' },
        { id: 'analyze', label: 'Analyze installer media', status: 'completed' },
        { id: 'verify', label: 'Verify digest', status: 'completed' },
        { id: 'store', label: 'Store artifact', status: 'completed' },
      ],
    }),
    listArtifacts: vi.fn().mockResolvedValue(mockArtifacts),
    listWorkloads: vi.fn().mockResolvedValue(mockWorkloads),
    listWorkloadRevisions: vi.fn().mockResolvedValue([
      {
        id: 'wrv-new',
        workloadId: 'workload-001',
        revision: '2.0.0',
        state: 'draft',
        createdAt: new Date().toISOString(),
        packageSteps: [{ packageId: 'pkg-runtime', packageName: '', packageVersion: '', packageIndex: 1, stepId: 'step-1' }],
      },
    ]),
    createWorkloadDefinitionDraft: vi.fn().mockResolvedValue({
      id: 'workload-new',
      name: 'Line-B Baseline',
      description: 'Secondary line draft',
      createdAt: new Date().toISOString(),
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
  }
})

describe('Workloads page', () => {
  beforeEach(async () => {
    await api.uploadArtifact({
      file: new File(['fake'], 'EJ-Installer-2.0.0.msi', { type: 'application/octet-stream' }),
      manifest: {
        packageId: 'EJ-Installer',
        version: '2.0.0',
        channel: 'test',
        artifactType: 'msi',
      },
    })
    await api.uploadArtifact({
      file: new File(['fake'], 'EJ-Installer-2.1.0.msi', { type: 'application/octet-stream' }),
      manifest: {
        packageId: 'EJ-Installer',
        version: '2.1.0',
        channel: 'test',
        artifactType: 'msi',
      },
    })

    render(<Workloads />)
    await screen.findByText('Workload Definitions')
  })

  it('renders version-oriented workload labels', async () => {
    expect(screen.getByText('Latest Version')).toBeInTheDocument()
    expect(screen.getByText('Version Status')).toBeInTheDocument()
    expect(screen.getByText('Version List')).toBeInTheDocument()
    expect(screen.getAllByText('Factory Base Install').length).toBeGreaterThan(0)
  })

  it('creates workload definition draft via centered popup', async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Create Workload Definition Draft' }))

    const draftDialog = screen.getByRole('dialog')
    expect(within(draftDialog).getByText('Create Draft WorkloadDefinition')).toBeInTheDocument()

    fireEvent.change(within(draftDialog).getByLabelText('Name'), {
      target: { value: 'Line-B Baseline' },
    })
    fireEvent.change(within(draftDialog).getByLabelText('Description'), {
      target: { value: 'Secondary line draft' },
    })

    fireEvent.click(within(draftDialog).getByRole('button', { name: 'Create Draft' }))

    await waitFor(() => {
      expect(screen.getByText('Created WorkloadDefinition draft: Line-B Baseline')).toBeInTheDocument()
    })
  })

  it('creates and publishes revision draft via popup flow', async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Create Workload Version Draft' }))

    const revisionDialog = screen.getByRole('dialog')
    expect(within(revisionDialog).getByText('Create Workload Version Draft')).toBeInTheDocument()

    fireEvent.change(within(revisionDialog).getByLabelText('Revision'), {
      target: { value: '2.0.0' },
    })

    const packageSelect = within(revisionDialog).getByLabelText('Package steps (2-3)') as HTMLSelectElement
    const packageOptions = within(packageSelect).getAllByRole('option') as HTMLOptionElement[]
    packageOptions[0].selected = true
    packageOptions[1].selected = true
    fireEvent.change(packageSelect)

    const createButton = within(revisionDialog).getByRole('button', { name: 'Create Revision Draft' })
    expect(createButton).toBeEnabled()
    fireEvent.click(createButton)

    await waitFor(() => {
      expect(screen.getByText('Created WorkloadRevision draft 2.0.0')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: 'Publish Revision' }))

    await waitFor(() => {
      expect(screen.getByText('Published revision 2.0.0. Revision is now immutable.')).toBeInTheDocument()
    })
  })
})