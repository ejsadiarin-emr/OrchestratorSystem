import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import Install from './Install'

function emptyArtifactListResponse() {
  return new Response(JSON.stringify([]), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('Install page flow', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('loads artifact inventory from API on mount', async () => {
    const artifactResponse = new Response(
      JSON.stringify([
        {
          id: 'EJ-Installer-1.12.0',
          packageId: 'EJ-Installer',
          version: '1.12.0',
          fileName: 'EJ-Installer-1.12.0.msi',
          channel: 'test',
          artifactType: 'msi',
          verificationResult: 'verified',
          sizeBytes: 12_345_678,
          digest: 'sha256:abc123def456',
          createdAt: '2026-04-16T12:00:00.000Z',
          installAdapterCommand: 'msiexec',
          detectionType: 'registry',
          detectionPath: 'HKLM\\Software\\EJ',
          riskLevel: 'low',
        },
      ]),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    )
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(artifactResponse)

    render(<Install />)

    await waitFor(() => {
      expect(screen.getByText('EJ-Installer')).toBeInTheDocument()
    })
    expect(screen.getByText('1.12.0')).toBeInTheDocument()
    expect(screen.getByText('test')).toBeInTheDocument()
    expect(screen.getByText('msi')).toBeInTheDocument()
    expect(screen.getByText('12,345,678 bytes')).toBeInTheDocument()
    expect(screen.getByText('sha256:abc123def456')).toBeInTheDocument()
  })

  it('prefills manifest via file picker and shows channel validation error when channel is invalid', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(emptyArtifactListResponse())
    render(<Install />)

    await screen.findByText('Artifact Store Console')

    const fileInput = screen.getByLabelText('Select local artifact file')
    const pickedFile = new File(['installer-binary'], 'EJ-Installer-9.9.9.msi', {
      type: 'application/x-msi',
    })
    fireEvent.change(fileInput, { target: { files: [pickedFile] } })

    await screen.findByText(/Selected/)

    const channelSelect = (await screen.findByLabelText('Channel')) as HTMLSelectElement
    const option = document.createElement('option')
    option.value = 'bad-channel'
    channelSelect.appendChild(option)
    channelSelect.value = 'bad-channel'
    fireEvent.change(channelSelect, { target: { value: 'bad-channel' } })
    channelSelect.removeChild(option)

    expect(await screen.findByText('manifest.channel must be one of stable, canary, or test.')).toBeInTheDocument()
  })

  it('prefills manifest from drag-drop and stores artifact through multipart flow', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(emptyArtifactListResponse())
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            resolvedManifest: {
              packageId: 'EJ-Installer',
              version: '2.0.0',
              channel: 'stable',
              artifactType: 'msi',
              installAdapter: { type: 'msi', command: 'msiexec', arguments: '/quiet /norestart', expectedExitCodes: [0], timeoutSeconds: 300 },
              detection: { type: 'registry', path: 'HKLM\\Software\\EJ-Installer', expectedVersion: '2.0.0' },
              policyTags: { retryabilityClass: 'retryable', idempotencyMode: 'enforced', riskLevel: 'low', approvalRequired: false },
              originMetadata: { source: 'test', publisher: 'test', ingestedBy: 'anonymous', ingestedAtUtc: '2026-01-01T00:00:00Z', verificationResult: 'derived' },
            },
          }),
          { status: 201, headers: { 'Content-Type': 'application/json' } },
        ),
      )

    render(<Install />)

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
