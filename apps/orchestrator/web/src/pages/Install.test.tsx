import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import Install from './Install'

describe('Install page flow', () => {
  beforeEach(() => {
    render(<Install />)
  })

  it('prefills manifest via file picker and shows channel validation error when channel is invalid', async () => {
    await screen.findByText('Artifact Store Console')

    const fileInput = screen.getByLabelText('Select local artifact file')
    const pickedFile = new File(['installer-binary'], 'EJ-Installer-9.9.9.msi', {
      type: 'application/x-msi',
    })
    fireEvent.change(fileInput, { target: { files: [pickedFile] } })

    await screen.findByText(/Selected/)

    const channelSelect = await screen.findByLabelText('Channel')
    fireEvent.change(channelSelect, { target: { value: 'bad-channel' } })

    expect(await screen.findByText('manifest.channel must be one of stable, canary, or test.')).toBeInTheDocument()
  })

  it('prefills manifest from drag-drop and stores artifact through mocked multipart flow', async () => {
    await screen.findByText('Artifact Store Console')

    const droppedFile = new File(['installer-binary-2'], 'EJ-Installer-2.0.0.msi', {
      type: 'application/x-msi',
    })
    fireEvent.drop(screen.getByTestId('artifact-dropzone'), {
      dataTransfer: {
        files: [droppedFile],
      },
    })

    fireEvent.click(await screen.findByText('Validate and Store Artifact'))

    await waitFor(() => {
      expect(screen.getByText(/Stored EJ-Installer-2.0.0.msi/)).toBeInTheDocument()
    })
  })
})
