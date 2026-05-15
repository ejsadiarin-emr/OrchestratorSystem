import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Workloads from './Workloads'
import { TestRouterWrapper } from '../test-utils/TestRouterWrapper'
import * as api from '../services/api'

vi.mock('../services/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../services/api')>()
  const mockArtifacts = [
    {
      id: 'EJ-Installer-2.0.0',
      packageEntityId: 'pkg-1',
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
      packageEntityId: 'pkg-2',
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
      latestRevision: {
        id: 'wrv-pub',
        workloadId: 'workload-001',
        revision: '1.0.0',
        state: 'published' as const,
        createdAt: new Date().toISOString(),
        publishedAt: new Date().toISOString(),
        packageSteps: [{ packageId: 'pkg-1', packageName: 'EJ-Installer', packageVersion: '2.0.0', packageIndex: 1, stepId: 'step-1' }],
      },
      revisionCount: 2,
    },
  ]
  const mockRevisions = [
    {
      id: 'wrv-pub',
      workloadId: 'workload-001',
      revision: '1.0.0',
      state: 'published' as const,
      createdAt: new Date().toISOString(),
      publishedAt: new Date().toISOString(),
      packageSteps: [{ packageId: 'pkg-1', packageName: 'EJ-Installer', packageVersion: '2.0.0', packageIndex: 1, stepId: 'step-1' }],
    },
    {
      id: 'wrv-new',
      workloadId: 'workload-001',
      revision: '2.0.0',
      state: 'draft' as const,
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
    updateWorkloadRevision: vi.fn().mockResolvedValue({
      changed: true,
      revision: {
        id: 'wrv-new',
        workloadId: 'workload-001',
        revision: '2.0.0',
        state: 'draft',
        createdAt: new Date().toISOString(),
        packageSteps: [{ packageId: 'pkg-runtime', packageName: '', packageVersion: '', packageIndex: 1, stepId: 'step-1' }],
      },
    }),
    importBulkWorkloads: vi.fn().mockResolvedValue({
      results: [
        { name: 'New Workload', slug: 'new-workload', status: 'success' as const },
      ],
    }),
  }
})

describe('Workloads page', () => {
  describe('when data loads successfully', () => {
    beforeEach(async () => {
      render(<Workloads />, { wrapper: TestRouterWrapper })
      await screen.findByRole('heading', { name: 'Workload Definitions', level: 1 })
    })

    it('renders workload cards with latest revision info', async () => {
      expect(screen.getByText('Factory Base Install')).toBeInTheDocument()
      expect(screen.getByText('Baseline package set for production line nodes.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'View Details' })).toBeInTheDocument()
      expect(screen.getByText('1.0.0')).toBeInTheDocument()
      expect(screen.getByText('published')).toBeInTheDocument()
    })

    it('opens workload detail modal showing revisions', async () => {
      const user = userEvent.setup()
      await user.click(screen.getByRole('button', { name: 'View Details' }))

      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByText('Workload details')).toBeInTheDocument()
      expect(within(dialog).getByText('Factory Base Install')).toBeInTheDocument()
      expect(within(dialog).getByText('Revisions')).toBeInTheDocument()
      expect(within(dialog).getByText('1.0.0')).toBeInTheDocument()
      expect(within(dialog).getByText('2.0.0')).toBeInTheDocument()
      expect(within(dialog).getByText('published')).toBeInTheDocument()
      expect(within(dialog).getByText('draft')).toBeInTheDocument()
    })

    it('opens delete confirmation modal', async () => {
      const user = userEvent.setup()
      const deleteButton = screen.getByRole('button', { name: 'Delete workload' })
      await user.click(deleteButton)

      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByText('Delete Workload')).toBeInTheDocument()
      expect(within(dialog).getByText(/Are you sure you want to delete "Factory Base Install"/)).toBeInTheDocument()
      expect(within(dialog).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
      expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
    })

    it('deletes a workload and shows success message', async () => {
      const user = userEvent.setup()
      await user.click(screen.getByRole('button', { name: 'Delete workload' }))

      const dialog = await screen.findByRole('dialog')
      await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

      await waitFor(() => {
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
      })
      expect(screen.getByText(/Deleted workload/)).toBeInTheDocument()
    })

    it('publishes a draft revision from detail modal', async () => {
      const user = userEvent.setup()
      await user.click(screen.getByRole('button', { name: 'View Details' }))

      const dialog = await screen.findByRole('dialog')
      const publishButton = within(dialog).getByRole('button', { name: 'Publish' })
      await user.click(publishButton)

      await waitFor(() => {
        expect(screen.getByText(/Published revision/)).toBeInTheDocument()
      })
      expect(api.publishWorkloadRevision).toHaveBeenCalledWith('workload-001', 'wrv-new')
    })

    it('opens import dialog', async () => {
      const user = userEvent.setup()
      const heading = screen.getByRole('heading', { name: /Workload Definitions/i, level: 2 })
      const toolbar = heading.closest('div')!.parentElement!
      const importButton = within(toolbar).getByRole('button')
      await user.click(importButton)

      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByText('Import Workload Definitions')).toBeInTheDocument()
      expect(within(dialog).getByText(/Drag & drop a workloads.json file/)).toBeInTheDocument()
    })
  })

  it('shows loading state while fetching', async () => {
    vi.mocked(api.listWorkloads).mockReturnValueOnce(new Promise(() => {}))

    render(<Workloads />, { wrapper: TestRouterWrapper })

    expect(screen.getByText('Loading...')).toBeInTheDocument()
  })

  it('shows empty state when no workloads exist', async () => {
    vi.mocked(api.listWorkloads).mockResolvedValueOnce([])
    vi.mocked(api.listArtifacts).mockResolvedValueOnce([])

    render(<Workloads />, { wrapper: TestRouterWrapper })

    await screen.findByRole('heading', { name: 'Workload Definitions', level: 1 })
    expect(screen.getByText('No workload definitions yet')).toBeInTheDocument()
    expect(screen.getByText('0')).toBeInTheDocument()
  })

  it('shows error message when API fails', async () => {
    vi.mocked(api.listWorkloads).mockRejectedValueOnce(new Error('Network error'))

    render(<Workloads />, { wrapper: TestRouterWrapper })

    expect(await screen.findByText('Failed to load workloads and revisions.')).toBeInTheDocument()
  })

  it('shows error message when delete fails', async () => {
    vi.mocked(api.deleteWorkload).mockRejectedValueOnce(new Error('Delete denied'))

    render(<Workloads />, { wrapper: TestRouterWrapper })
    await screen.findByRole('heading', { name: 'Workload Definitions', level: 1 })

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Delete workload' }))

    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await screen.findByText('Delete denied')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = render(<Workloads />, { wrapper: TestRouterWrapper })
    await screen.findByRole('heading', { name: 'Workload Definitions', level: 1 })
    const results = await axe(container)
    expect(results.violations).toEqual([])
  })
})
