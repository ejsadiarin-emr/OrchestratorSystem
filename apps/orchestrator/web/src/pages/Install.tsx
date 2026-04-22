import { useEffect, useMemo, useRef, useState } from 'react'
import {
  getSupportedChannels,
  listArtifacts,
  suggestManifestFromFile,
  uploadArtifact,
  validateManifestChannel,
} from '../services/api'
import { Modal, ModalContent, ModalDescription, ModalHeader, ModalTitle } from '../components/ui/modal'
import type { ArtifactManifest, ArtifactRecord, IngestStep, ManifestChannel } from '../types'

type UploadStage = 'idle' | 'prefilled' | 'uploading' | 'stored'

interface FileDraft {
  file: File | null
  detachedSignature?: string
}

const channelOptions = getSupportedChannels()

const emptyIngestSteps: IngestStep[] = [
  { id: 'upload', label: 'Receive multipart request (file + manifest + optional detachedSignature)', status: 'pending' },
  { id: 'analyze', label: 'Analyze installer media and prefill metadata', status: 'pending' },
  { id: 'verify', label: 'Verify digest, signatures, and origin metadata', status: 'pending' },
  { id: 'store', label: 'Store immutable artifact and write audit record', status: 'pending' },
]

export default function Install() {
  const [artifacts, setArtifacts] = useState<ArtifactRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [stage, setStage] = useState<UploadStage>('idle')
  const [fileDraft, setFileDraft] = useState<FileDraft>({ file: null, detachedSignature: '' })
  const [manifest, setManifest] = useState<ArtifactManifest | null>(null)
  const [steps, setSteps] = useState<IngestStep[]>(emptyIngestSteps)
  const [error, setError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const [isDragActive, setIsDragActive] = useState(false)
  const [selectedArtifact, setSelectedArtifact] = useState<ArtifactRecord | null>(null)
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => {
    listArtifacts()
      .then(data => setArtifacts(data))
      .finally(() => setLoading(false))
  }, [])

  const channelError = useMemo(() => {
    if (!manifest) {
      return null
    }

    if (!manifest.channel) {
      return null
    }

    return validateManifestChannel(manifest.channel)
      ? null
      : 'manifest.channel must be one of stable, canary, or test.'
  }, [manifest])

  const prefillFromFile = (fileName: string) => {
    if (!fileName) {
      setError('Select a local installer file before prefilling metadata.')
      return
    }

    setError(null)
    setSuccessMessage(null)

    const prefetched = suggestManifestFromFile(fileName, 0)
    setManifest(prefetched)
    setSteps([
      { ...emptyIngestSteps[0], status: 'completed' },
      { ...emptyIngestSteps[1], status: 'completed' },
      { ...emptyIngestSteps[2] },
      { ...emptyIngestSteps[3] },
    ])
    setStage('prefilled')
  }

  const handleFileSelection = (file: File | null) => {
    if (!file) {
      return
    }

    setFileDraft(current => ({
      ...current,
      file,
    }))
    prefillFromFile(file.name)
  }

  const handlePickerChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] ?? null
    handleFileSelection(file)
  }

  const handleDrop = (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault()
    setIsDragActive(false)
    handleFileSelection(event.dataTransfer.files?.[0] ?? null)
  }

  const handleStore = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setSuccessMessage(null)

    if (!manifest) {
      setError('Run Analyze first to prefill metadata.')
      return
    }

    if (manifest.channel && !validateManifestChannel(manifest.channel)) {
      setError('manifest.channel must be one of stable, canary, or test.')
      return
    }

    if (!fileDraft.file) {
      setError('Select a local installer file first.')
      return
    }

    setStage('uploading')

    try {
      const result = await uploadArtifact({
        file: fileDraft.file,
        manifest,
        detachedSignature: fileDraft.detachedSignature?.trim() || undefined,
      })
      setArtifacts(current => [result.artifact, ...current])
      setSteps(result.steps)
      setSuccessMessage(`Stored ${result.artifact.fileName} as ${result.artifact.id}.`)
      setStage('stored')
    } catch (uploadError) {
      setStage('prefilled')
      setError(uploadError instanceof Error ? uploadError.message : 'Failed to store artifact.')
    }
  }

  const resetForm = () => {
    setFileDraft({ file: null, detachedSignature: '' })
    setManifest(null)
    setSteps(emptyIngestSteps)
    setStage('idle')
    setError(null)
    setSuccessMessage(null)
    setIsDragActive(false)
    if (fileInputRef.current) {
      fileInputRef.current.value = ''
    }
  }

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-6">
      <header className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-strong)]">Artifact Store Console</h1>
        <p className="mt-2 text-sm text-[var(--text-soft)]">
          Stage local artifacts for workload revisions with one mocked multipart POST to
          <code> /api/artifacts</code> using required <code>file</code>, required <code>manifest</code>, and optional
          <code> detachedSignature</code>.
        </p>
      </header>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div className="space-y-5 rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)] xl:col-span-2">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Ingest Artifact</h2>

          {error && (
            <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] p-3 text-sm text-[var(--status-danger-text)]">
              {error}
            </div>
          )}

          {successMessage && (
            <div className="rounded-lg border border-[var(--status-success-border)] bg-[var(--status-success-bg)] p-3 text-sm text-[var(--status-success-text)]">
              {successMessage}
            </div>
          )}

          <div className="space-y-4">
            <div
              data-testid="artifact-dropzone"
              onDragOver={event => {
                event.preventDefault()
                setIsDragActive(true)
              }}
              onDragLeave={() => setIsDragActive(false)}
              onDrop={handleDrop}
              className={`rounded-xl border border-dashed p-5 text-sm transition-colors ${
                isDragActive
                  ? 'border-[var(--accent)] bg-[var(--surface-subtle)]'
                  : 'border-[var(--surface-border)] bg-[var(--surface-subtle)]'
              }`}
            >
              <p className="font-medium text-[var(--text-strong)]">Drop installer media here</p>
              <p className="mt-1 text-[var(--text-soft)]">or pick from local disk. Metadata prefill runs immediately.</p>
              <div className="mt-4">
                <input
                  ref={fileInputRef}
                  id="artifact-file-picker"
                  type="file"
                  aria-label="Select local artifact file"
                  onChange={handlePickerChange}
                  className="sr-only"
                />
                <button
                  type="button"
                  onClick={() => fileInputRef.current?.click()}
                  className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
                >
                  Choose Local Artifact
                </button>
              </div>
            </div>

            {fileDraft.file ? (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--text-soft)]">
                Selected <span className="font-medium text-[var(--text-strong)]">{fileDraft.file.name}</span> ({fileDraft.file.size}{' '}
                bytes)
              </div>
            ) : (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--text-soft)]">
                No artifact selected yet.
              </div>
            )}

            <label className="block text-sm text-[var(--text-soft)]">
              Optional company detached signature
              <input
                type="text"
                value={fileDraft.detachedSignature}
                onChange={event => setFileDraft(current => ({ ...current, detachedSignature: event.target.value }))}
                placeholder="base64-signature"
                className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
              />
            </label>
          </div>

          {manifest && (
            <form onSubmit={handleStore} className="space-y-4 border-t border-[var(--surface-border)] pt-5">
              <h3 className="font-semibold text-[var(--text-strong)]">Manifest Draft</h3>
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <Field label="Package ID" value={manifest.packageId ?? ''} onChange={value => setManifest({ ...manifest, packageId: value })} />
                <Field
                  label="Version"
                  value={manifest.version ?? ''}
                  onChange={value => setManifest({ ...manifest, version: value })}
                />
                <label className="text-sm text-[var(--text-soft)]">
                  Channel
                  <select
                    value={manifest.channel ?? 'stable'}
                    onChange={event => setManifest({ ...manifest, channel: event.target.value as ManifestChannel })}
                    className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                  >
                    {channelOptions.map(option => (
                      <option value={option} key={option}>
                        {option}
                      </option>
                    ))}
                  </select>
                </label>
                <Field
                  label="Artifact type"
                  value={manifest.artifactType ?? ''}
                  onChange={value => setManifest({ ...manifest, artifactType: value })}
                />
              </div>

              {channelError && (
                <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-3 py-2 text-sm text-[var(--status-danger-text)]">
                  {channelError}
                </div>
              )}

              <div className="space-y-2 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-4">
                <h4 className="font-medium text-[var(--text-strong)]">Install adapter</h4>
                <Field
                  label="Command"
                  value={manifest.installAdapter?.command ?? ''}
                  onChange={value => setManifest({ ...manifest, installAdapter: { ...manifest.installAdapter, command: value } })}
                />
                <label className="block text-sm text-[var(--text-soft)]">
                  Arguments
                  <input
                    type="text"
                    value={manifest.installAdapter?.arguments ?? ''}
                    onChange={event => setManifest({ ...manifest, installAdapter: { ...manifest.installAdapter, arguments: event.target.value } })}
                    className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                  />
                </label>
              </div>

              <div className="space-y-2 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-4">
                <h4 className="font-medium text-[var(--text-strong)]">Detection</h4>
                <Field
                  label="Detection type"
                  value={manifest.detection?.type ?? ''}
                  onChange={value => setManifest({ ...manifest, detection: { ...manifest.detection, type: value } })}
                />
                <Field
                  label="Detection path"
                  value={manifest.detection?.path ?? ''}
                  onChange={value => setManifest({ ...manifest, detection: { ...manifest.detection, path: value } })}
                />
              </div>

              <div className="flex gap-3">
                <button
                  type="submit"
                  disabled={Boolean(channelError) || stage === 'uploading'}
                  className="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-[var(--surface-border)]"
                >
                  {stage === 'uploading' ? 'Storing...' : 'Validate and Store Artifact'}
                </button>
                <button
                  type="button"
                  onClick={resetForm}
                  className="rounded-lg bg-[var(--surface-muted)] px-4 py-2 text-sm font-medium text-[var(--text-soft)] hover:bg-[var(--surface-border)]"
                >
                  Reset Draft
                </button>
              </div>
            </form>
          )}
        </div>

        <aside className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
          <h2 className="mb-4 text-lg font-semibold text-[var(--text-strong)]">Ingest Timeline</h2>
          <ol className="space-y-3">
            {steps.map(step => (
              <li key={step.id} className="flex items-start gap-3">
                <span
                  className={`mt-1 h-2.5 w-2.5 rounded-full ${
                    step.status === 'completed'
                      ? 'bg-emerald-500'
                      : step.status === 'running'
                      ? 'bg-blue-500'
                      : 'bg-[var(--surface-border)]'
                  }`}
                />
                <div>
                  <p className="text-sm font-medium text-[var(--text-strong)]">{step.label}</p>
                  <p className="text-xs uppercase text-[var(--text-soft)]">{step.status}</p>
                </div>
              </li>
            ))}
          </ol>
        </aside>
      </div>

      <section className="overflow-hidden rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] shadow-[var(--surface-shadow)]">
        <div className="border-b border-[var(--surface-border)] px-6 py-4">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Artifact Inventory</h2>
          <p className="mt-1 text-xs text-[var(--text-soft)]">Immutable records available for future workload revisions.</p>
        </div>
        {artifacts.length === 0 ? (
          <p className="px-6 py-5 text-sm text-[var(--text-soft)]">No artifacts ingested yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-[var(--surface-border)]">
              <thead className="bg-[var(--surface-subtle)]">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Artifact</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Version</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Channel</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Type</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Size</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Digest</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Adapter</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Details</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--surface-border)]">
                {artifacts.map(artifact => (
                  <tr key={artifact.id}>
                    <td className="px-6 py-4">
                      <p className="font-medium text-[var(--text-strong)]">{artifact.manifest.packageId ?? artifact.fileName}</p>
                      <p className="text-xs text-[var(--text-soft)]">{artifact.fileName}</p>
                      <p className="text-xs text-[var(--text-soft)]">{artifact.id}</p>
                    </td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">{artifact.manifest.version ?? '-'}</td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">{artifact.manifest.channel ?? '-'}</td>
                    <td className="px-6 py-4 font-mono text-xs text-[var(--text-soft)]">{artifact.manifest.artifactType ?? '-'}</td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">{artifact.sizeBytes != null ? `${artifact.sizeBytes.toLocaleString()} bytes` : '-'}</td>
                    <td className="px-6 py-4 font-mono text-xs text-[var(--text-soft)]">{artifact.digest ?? '-'}</td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">
                      {artifact.manifest.installAdapter?.command ?? '-'}
                    </td>
                    <td className="px-6 py-4">
                      <button
                        type="button"
                        onClick={() => setSelectedArtifact(artifact)}
                        className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-1.5 text-xs font-medium text-[var(--text-soft)] hover:bg-[var(--surface-border)]"
                      >
                        View
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <Modal open={Boolean(selectedArtifact)} onOpenChange={open => !open && setSelectedArtifact(null)}>
        <ModalContent className="w-[min(94vw,44rem)]">
          <ModalHeader>
            <ModalTitle>Artifact details</ModalTitle>
            <ModalDescription>Inspect full manifest before attaching to a workload revision.</ModalDescription>
          </ModalHeader>
          {selectedArtifact && (
            <div className="grid grid-cols-1 gap-3 px-4 pb-4 text-sm md:grid-cols-2">
              <Detail label="Artifact id" value={selectedArtifact.id} mono />
              <Detail label="File" value={selectedArtifact.fileName} />
              <Detail label="Package ID" value={selectedArtifact.manifest.packageId ?? '-'} />
              <Detail label="Version" value={selectedArtifact.manifest.version ?? '-'} />
              <Detail label="Channel" value={selectedArtifact.manifest.channel ?? '-'} />
              <Detail label="Artifact type" value={selectedArtifact.manifest.artifactType ?? '-'} />
              <Detail label="Verification" value={selectedArtifact.manifest.verificationResult ?? '-'} />
              <Detail label="Detached signature" value={selectedArtifact.detachedSignaturePresent ? 'Present' : 'Not provided'} />
              <Detail label="Adapter command" value={selectedArtifact.manifest.installAdapter?.command ?? '-'} />
              <Detail label="Adapter args" value={selectedArtifact.manifest.installAdapter?.arguments ?? '-'} />
              <Detail label="Detection type" value={selectedArtifact.manifest.detection?.type ?? '-'} />
              <Detail label="Detection path" value={selectedArtifact.manifest.detection?.path ?? '-'} />
              <Detail label="Size" value={selectedArtifact.sizeBytes != null ? `${selectedArtifact.sizeBytes.toLocaleString()} bytes` : '-'} />
              <Detail label="Digest" value={selectedArtifact.digest ?? '-'} mono />
              <Detail label="Stored at" value={new Date(selectedArtifact.createdAt).toLocaleString()} />
            </div>
          )}
        </ModalContent>
      </Modal>
    </div>
  )
}

function Field({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label className="block text-sm text-[var(--text-soft)]">
      {label}
      <input
        type="text"
        value={value}
        onChange={event => onChange(event.target.value)}
        className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
      />
    </label>
  )
}

function Detail({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
      <p className="text-xs uppercase tracking-wide text-[var(--text-soft)]">{label}</p>
      <p className={`mt-1 text-[var(--text-strong)] ${mono ? 'font-mono text-xs break-all' : ''}`}>{value}</p>
    </div>
  )
}
