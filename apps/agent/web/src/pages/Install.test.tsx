import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import Install from './Install'

describe('Install page flow', () => {
  beforeEach(() => {
    render(<Install />)
  })

  it('shows channel validation error when channel is invalid', async () => {
    await screen.findByText('Installer Artifact Ingestion')

    fireEvent.change(screen.getByPlaceholderText('EJ-Installer-1.13.0.msi'), {
      target: { value: 'EJ-Installer-9.9.9.msi' },
    })
    fireEvent.change(screen.getByLabelText('File size bytes'), {
      target: { value: '43210' },
    })
    fireEvent.click(screen.getByText('Analyze and Prefill Metadata'))

    const channelSelect = await screen.findByLabelText('Channel')
    fireEvent.change(channelSelect, { target: { value: 'bad-channel' } })

    expect(await screen.findByText('manifest.channel must be one of stable, canary, or test.')).toBeInTheDocument()
  })

  it('stores artifact through mocked multipart flow', async () => {
    await screen.findByText('Installer Artifact Ingestion')

    fireEvent.change(screen.getByPlaceholderText('EJ-Installer-1.13.0.msi'), {
      target: { value: 'EJ-Installer-2.0.0.msi' },
    })
    fireEvent.change(screen.getByLabelText('File size bytes'), {
      target: { value: '87654' },
    })
    fireEvent.click(screen.getByText('Analyze and Prefill Metadata'))

    fireEvent.click(await screen.findByText('Verify and Store Artifact'))

    await waitFor(() => {
      expect(screen.getByText(/Stored EJ-Installer-2.0.0.msi/)).toBeInTheDocument()
    })
  })
})
