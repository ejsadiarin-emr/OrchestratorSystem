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
    <div className="space-y-8">
      <header>
        <h1 className="text-2xl font-bold text-gray-800">Installer Artifact Ingestion</h1>
        <p className="text-sm text-gray-600 mt-2">
          Mocked Phase 1 flow: one multipart POST to <code>/api/artifacts</code> with required
          <code> file</code>, required <code>manifest</code>, optional <code>detachedSignature</code>.
        </p>
      </header>

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div className="xl:col-span-2 bg-white rounded-lg shadow p-6 space-y-5">
          <h2 className="text-lg font-semibold text-gray-800">Upload & Verify</h2>

          {error && (
            <div className="bg-red-50 border border-red-200 rounded-md p-3 text-sm text-red-700">{error}</div>
          )}

          {successMessage && (
            <div className="bg-green-50 border border-green-200 rounded-md p-3 text-sm text-green-700">
              {successMessage}
            </div>
          )}

          <form onSubmit={handleAnalyze} className="space-y-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <label className="text-sm text-gray-700">
                Installer file (multipart <code>file</code> part)
                <input
                  type="text"
                  value={fileDraft.fileName}
                  onChange={event => setFileDraft(current => ({ ...current, fileName: event.target.value }))}
                  placeholder="EJ-Installer-1.13.0.msi"
                  className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                  required
                />
              </label>
              <label className="text-sm text-gray-700">
                File size bytes
                <input
                  type="number"
                  min={1}
                  value={fileDraft.fileSizeBytes || ''}
                  onChange={event =>
                    setFileDraft(current => ({ ...current, fileSizeBytes: Number(event.target.value) || 0 }))
                  }
                  className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                  required
                />
              </label>
            </div>

            <label className="text-sm text-gray-700 block">
              Optional company detached signature
              <input
                type="text"
                value={fileDraft.detachedSignature}
                onChange={event => setFileDraft(current => ({ ...current, detachedSignature: event.target.value }))}
                placeholder="base64-signature"
                className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
              />
            </label>

            <button type="submit" className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700">
              Analyze and Prefill Metadata
            </button>
          </form>

          {manifest && (
            <form onSubmit={handleStore} className="space-y-4 border-t border-gray-200 pt-5">
              <h3 className="font-semibold text-gray-800">Manifest (multipart JSON part)</h3>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <Field label="Name" value={manifest.name} onChange={value => setManifest({ ...manifest, name: value })} />
                <Field
                  label="Version"
                  value={manifest.version}
                  onChange={value => setManifest({ ...manifest, version: value })}
                />
                <label className="text-sm text-gray-700">
                  Channel
                  <select
                    value={manifest.channel}
                    onChange={event => setManifest({ ...manifest, channel: event.target.value as ManifestChannel })}
                    className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
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
                <div className="text-sm text-red-700 bg-red-50 border border-red-200 rounded-md px-3 py-2">
                  {channelError}
                </div>
              )}

              <label className="text-sm text-gray-700 block">
                Install args
                <input
                  type="text"
                  value={manifest.installArgs}
                  onChange={event => setManifest({ ...manifest, installArgs: event.target.value })}
                  className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                />
              </label>

              <label className="text-sm text-gray-700 block">
                Digest SHA256
                <input
                  type="text"
                  value={manifest.digestSha256}
                  onChange={event => setManifest({ ...manifest, digestSha256: event.target.value })}
                  className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                />
              </label>

              <label className="text-sm text-gray-700 block">
                Signing identity
                <input
                  type="text"
                  value={manifest.signingIdentity}
                  onChange={event => setManifest({ ...manifest, signingIdentity: event.target.value })}
                  className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
                />
              </label>

              <div className="rounded-md border border-gray-200 p-4 bg-gray-50 space-y-2">
                <h4 className="font-medium text-gray-800">Origin metadata</h4>
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
                  className="bg-emerald-600 text-white px-4 py-2 rounded-md hover:bg-emerald-700 disabled:bg-gray-400"
                >
                  {stage === 'uploading' ? 'Storing...' : 'Verify and Store Artifact'}
                </button>
                <button
                  type="button"
                  onClick={resetForm}
                  className="bg-gray-200 text-gray-800 px-4 py-2 rounded-md hover:bg-gray-300"
                >
                  Reset Draft
                </button>
              </div>
            </form>
          )}
        </div>

        <aside className="bg-white rounded-lg shadow p-6">
          <h2 className="text-lg font-semibold text-gray-800 mb-4">Ingest Timeline</h2>
          <ol className="space-y-3">
            {steps.map(step => (
              <li key={step.id} className="flex items-start gap-3">
                <span
                  className={`mt-1 h-2.5 w-2.5 rounded-full ${
                    step.status === 'completed'
                      ? 'bg-emerald-500'
                      : step.status === 'running'
                      ? 'bg-blue-500'
                      : 'bg-gray-300'
                  }`}
                />
                <div>
                  <p className="text-sm font-medium text-gray-800">{step.label}</p>
                  <p className="text-xs text-gray-500 uppercase">{step.status}</p>
                </div>
              </li>
            ))}
          </ol>
        </aside>
      </div>

      <section className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-800">Artifacts in Store</h2>
          <p className="text-xs text-gray-500 mt-1">Immutable records keyed by manifest identity and digest.</p>
        </div>
        {artifacts.length === 0 ? (
          <p className="px-6 py-5 text-sm text-gray-500">No artifacts ingested yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Artifact</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Channel</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Digest</th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Origin metadata</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {artifacts.map(artifact => (
                  <tr key={artifact.id}>
                    <td className="px-6 py-4">
                      <p className="font-medium text-gray-800">{artifact.fileName}</p>
                      <p className="text-xs text-gray-500">{artifact.id}</p>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-700">{artifact.manifest.channel}</td>
                    <td className="px-6 py-4 text-xs text-gray-600 font-mono">{artifact.manifest.digestSha256.slice(0, 18)}...</td>
                    <td className="px-6 py-4 text-sm text-gray-600">
                      {artifact.manifest.originMetadata.publisher}
                      <div className="text-xs text-gray-500">{artifact.manifest.originMetadata.sourceUrl}</div>
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
    <label className="text-sm text-gray-700 block">
      {label}
      <input
        type="text"
        value={value}
        onChange={event => onChange(event.target.value)}
        className="mt-1 w-full border border-gray-300 rounded-md px-3 py-2"
      />
    </label>
  )
}
