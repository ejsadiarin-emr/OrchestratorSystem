import { useEffect, useMemo, useState } from 'react'
import {
  getSupportedChannels,
  listArtifacts,
  suggestManifestFromFile,
  uploadArtifact,
  validateManifestChannel,
} from '../services/api'
import type { ArtifactManifest, ArtifactRecord, IngestStep, ManifestChannel } from '../types'

type UploadStage = 'idle' | 'prefilled' | 'uploading' | 'stored'

interface FileDraft {
  fileName: string
  fileSizeBytes: number
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
  const [fileDraft, setFileDraft] = useState<FileDraft>({ fileName: '', fileSizeBytes: 0, detachedSignature: '' })
  const [manifest, setManifest] = useState<ArtifactManifest | null>(null)
  const [steps, setSteps] = useState<IngestStep[]>(emptyIngestSteps)
  const [error, setError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)

  useEffect(() => {
    listArtifacts()
      .then(data => setArtifacts(data))
      .finally(() => setLoading(false))
  }, [])

  const channelError = useMemo(() => {
    if (!manifest) {
      return null
    }

    return validateManifestChannel(manifest.channel)
      ? null
      : 'manifest.channel must be one of stable, canary, or test.'
  }, [manifest])

  const handleAnalyze = (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setSuccessMessage(null)

    if (!fileDraft.fileName || fileDraft.fileSizeBytes <= 0) {
      setError('Provide installer file name and file size to model multipart file upload part.')
      return
    }

    const prefetched = suggestManifestFromFile(fileDraft.fileName, fileDraft.fileSizeBytes)
    setManifest(prefetched)
    setSteps([
      { ...emptyIngestSteps[0], status: 'completed' },
      { ...emptyIngestSteps[1], status: 'completed' },
      { ...emptyIngestSteps[2] },
      { ...emptyIngestSteps[3] },
    ])
    setStage('prefilled')
  }

  const handleStore = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setSuccessMessage(null)

    if (!manifest) {
      setError('Run Analyze first to prefill metadata.')
      return
    }

    if (!validateManifestChannel(manifest.channel)) {
      setError('manifest.channel must be one of stable, canary, or test.')
      return
    }

    setStage('uploading')

    try {
      const result = await uploadArtifact({
        fileName: fileDraft.fileName,
        fileSizeBytes: fileDraft.fileSizeBytes,
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
    setFileDraft({ fileName: '', fileSizeBytes: 0, detachedSignature: '' })
    setManifest(null)
    setSteps(emptyIngestSteps)
    setStage('idle')
    setError(null)
    setSuccessMessage(null)
  }

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-6">
      <header className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-strong)]">Installer Artifact Ingestion</h1>
        <p className="mt-2 text-sm text-[var(--text-soft)]">
          Mocked Phase 1 flow: one multipart POST to <code>/api/artifacts</code> with required
          <code> file</code>, required <code>manifest</code>, optional <code>detachedSignature</code>.
        </p>
      </header>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div className="space-y-5 rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)] xl:col-span-2">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Upload & Verify</h2>

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

          <form onSubmit={handleAnalyze} className="space-y-4">
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <label className="text-sm text-[var(--text-soft)]">
                Installer file (multipart <code>file</code> part)
                <input
                  type="text"
                  value={fileDraft.fileName}
                  onChange={event => setFileDraft(current => ({ ...current, fileName: event.target.value }))}
                  placeholder="EJ-Installer-1.13.0.msi"
                  className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                  required
                />
              </label>
              <label className="text-sm text-[var(--text-soft)]">
                File size bytes
                <input
                  type="number"
                  min={1}
                  value={fileDraft.fileSizeBytes || ''}
                  onChange={event =>
                    setFileDraft(current => ({ ...current, fileSizeBytes: Number(event.target.value) || 0 }))
                  }
                  className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                  required
                />
              </label>
            </div>

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

            <button
              type="submit"
              className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
            >
              Analyze and Prefill Metadata
            </button>
          </form>

          {manifest && (
            <form onSubmit={handleStore} className="space-y-4 border-t border-[var(--surface-border)] pt-5">
              <h3 className="font-semibold text-[var(--text-strong)]">Manifest (multipart JSON part)</h3>
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <Field label="Name" value={manifest.name} onChange={value => setManifest({ ...manifest, name: value })} />
                <Field
                  label="Version"
                  value={manifest.version}
                  onChange={value => setManifest({ ...manifest, version: value })}
                />
                <label className="text-sm text-[var(--text-soft)]">
                  Channel
                  <select
                    value={manifest.channel}
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
                  label="Install type"
                  value={manifest.installType}
                  onChange={value =>
                    setManifest({ ...manifest, installType: value === 'exe' || value === 'zip' ? value : 'msi' })
                  }
                />
              </div>

              {channelError && (
                <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-3 py-2 text-sm text-[var(--status-danger-text)]">
                  {channelError}
                </div>
              )}

              <label className="block text-sm text-[var(--text-soft)]">
                Install args
                <input
                  type="text"
                  value={manifest.installArgs}
                  onChange={event => setManifest({ ...manifest, installArgs: event.target.value })}
                  className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                />
              </label>

              <label className="block text-sm text-[var(--text-soft)]">
                Digest SHA256
                <input
                  type="text"
                  value={manifest.digestSha256}
                  onChange={event => setManifest({ ...manifest, digestSha256: event.target.value })}
                  className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                />
              </label>

              <label className="block text-sm text-[var(--text-soft)]">
                Signing identity
                <input
                  type="text"
                  value={manifest.signingIdentity}
                  onChange={event => setManifest({ ...manifest, signingIdentity: event.target.value })}
                  className="mt-1 w-full rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] px-3 py-2"
                />
              </label>

              <div className="space-y-2 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-4">
                <h4 className="font-medium text-[var(--text-strong)]">Origin metadata</h4>
                <Field
                  label="Source URL"
                  value={manifest.originMetadata.sourceUrl}
                  onChange={value =>
                    setManifest({
                      ...manifest,
                      originMetadata: { ...manifest.originMetadata, sourceUrl: value },
                    })
                  }
                />
                <Field
                  label="Publisher"
                  value={manifest.originMetadata.publisher}
                  onChange={value =>
                    setManifest({
                      ...manifest,
                      originMetadata: { ...manifest.originMetadata, publisher: value },
                    })
                  }
                />
              </div>

              <div className="flex gap-3">
                <button
                  type="submit"
                  disabled={Boolean(channelError) || stage === 'uploading'}
                  className="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-[var(--surface-border)]"
                >
                  {stage === 'uploading' ? 'Storing...' : 'Verify and Store Artifact'}
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
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Artifacts in Store</h2>
          <p className="mt-1 text-xs text-[var(--text-soft)]">Immutable records keyed by manifest identity and digest.</p>
        </div>
        {artifacts.length === 0 ? (
          <p className="px-6 py-5 text-sm text-[var(--text-soft)]">No artifacts ingested yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-[var(--surface-border)]">
              <thead className="bg-[var(--surface-subtle)]">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Artifact</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Channel</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Digest</th>
                  <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wide text-[var(--text-soft)]">Origin metadata</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--surface-border)]">
                {artifacts.map(artifact => (
                  <tr key={artifact.id}>
                    <td className="px-6 py-4">
                      <p className="font-medium text-[var(--text-strong)]">{artifact.fileName}</p>
                      <p className="text-xs text-[var(--text-soft)]">{artifact.id}</p>
                    </td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">{artifact.manifest.channel}</td>
                    <td className="px-6 py-4 font-mono text-xs text-[var(--text-soft)]">{artifact.manifest.digestSha256.slice(0, 18)}...</td>
                    <td className="px-6 py-4 text-sm text-[var(--text-soft)]">
                      {artifact.manifest.originMetadata.publisher}
                      <div className="text-xs text-[var(--text-soft)]">{artifact.manifest.originMetadata.sourceUrl}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
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
