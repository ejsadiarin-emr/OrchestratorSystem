import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { uploadArtifact } from '../services/api'
import Workloads from './Workloads'

describe('Workloads page', () => {
  beforeEach(async () => {
    await uploadArtifact({
      fileName: 'EJ-Installer-2.0.0.msi',
      fileSizeBytes: 12000,
      manifest: {
        name: 'EJ Installer',
        version: '2.0.0',
        channel: 'test',
        installType: 'msi',
        installArgs: '/quiet /norestart',
        digestSha256: 'a'.repeat(64),
        signingIdentity: 'CN=Emerson Trusted Release',
        originMetadata: {
          sourceUrl: 'https://repo.local/installers/EJ-Installer-2.0.0.msi',
          publisher: 'Emerson',
          packageFamily: 'EJ-Installer',
          collectedAt: new Date().toISOString(),
          sourceConfidence: 'verified',
          publisherConfidence: 'verified',
        },
      },
    })
    await uploadArtifact({
      fileName: 'EJ-Installer-2.1.0.msi',
      fileSizeBytes: 13000,
      manifest: {
        name: 'EJ Installer',
        version: '2.1.0',
        channel: 'test',
        installType: 'msi',
        installArgs: '/quiet /norestart',
        digestSha256: 'b'.repeat(64),
        signingIdentity: 'CN=Emerson Trusted Release',
        originMetadata: {
          sourceUrl: 'https://repo.local/installers/EJ-Installer-2.1.0.msi',
          publisher: 'Emerson',
          packageFamily: 'EJ-Installer',
          collectedAt: new Date().toISOString(),
          sourceConfidence: 'verified',
          publisherConfidence: 'verified',
        },
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
    const packageOptions = within(packageSelect).getAllByRole('option')
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
