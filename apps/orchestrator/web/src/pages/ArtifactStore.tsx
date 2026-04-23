import { useCallback, useEffect, useState } from 'react'
import type { ArtifactManifest, ArtifactRecord, BulkIngestResultItem } from '../types'
import { deleteArtifact, listArtifacts, suggestManifestFromFile, uploadArtifactWithProgress, uploadBulkArtifacts } from '../services/api'
import { detectArtifactPairs, extractManifestFromZip, extractZipEntries, isZipFile } from '../lib/zip-preview'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Stepper } from '@/components/ui/stepper'
import { cn } from '@/lib/utils'
import { Trash2, Upload, FileArchive, FileCode, AlertTriangle, CheckCircle2, XCircle, Package, X, Info } from 'lucide-react'
import { Modal, ModalContent, ModalHeader, ModalTitle, ModalDescription, ModalFooter } from '@/components/ui/modal'

const ingestSteps = [
  { id: 'receive', label: 'Receive' },
  { id: 'analyze', label: 'Analyze' },
  { id: 'verify', label: 'Verify' },
  { id: 'store', label: 'Store' },
]

function formatBytes(bytes?: number): string {
  if (bytes === undefined || bytes === null) return '-'
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

function riskBadgeVariant(level?: string): 'default' | 'secondary' | 'destructive' | 'outline' | 'ghost' | 'link' {
  switch (level?.toLowerCase()) {
    case 'low':
      return 'secondary'
    case 'med':
    case 'medium':
      return 'outline'
    case 'high':
      return 'destructive'
    default:
      return 'default'
  }
}

type DetectedMode = 'standalone' | 'singleZip' | 'bulkZip' | null

export default function ArtifactStore() {
  const [artifacts, setArtifacts] = useState<ArtifactRecord[]>([])
  const [loadingArtifacts, setLoadingArtifacts] = useState(true)

  const [file, setFile] = useState<File | null>(null)
  const [manifest, setManifest] = useState<ArtifactManifest>({})
  const [isDragging, setIsDragging] = useState(false)

  const [uploadProgress, setUploadProgress] = useState(0)
  const [uploadStep, setUploadStep] = useState(-1)
  const [uploadStatus, setUploadStatus] = useState('')
  const [isUploading, setIsUploading] = useState(false)
  const [uploadError, setUploadError] = useState('')

  const [detectedMode, setDetectedMode] = useState<DetectedMode>(null)
  const [zipAnalyzing, setZipAnalyzing] = useState(false)
  const [zipPairs, setZipPairs] = useState<{ baseName: string; mediaFile: string; manifestFile: string }[]>([])
  const [zipUnpaired, setZipUnpaired] = useState<string[]>([])
  const [singleZipManifest, setSingleZipManifest] = useState<ArtifactManifest | null>(null)

  const [bulkResults, setBulkResults] = useState<BulkIngestResultItem[]>([])
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [deleteError, setDeleteError] = useState('')
  const [selectedArtifact, setSelectedArtifact] = useState<ArtifactRecord | null>(null)

  const fetchArtifacts = useCallback(() => {
    setLoadingArtifacts(true)
    listArtifacts()
      .then(data => {
        setArtifacts(data)
      })
      .catch(() => {
        // keep existing artifacts on error
      })
      .finally(() => {
        setLoadingArtifacts(false)
      })
  }, [])

  useEffect(() => {
    fetchArtifacts()
  }, [fetchArtifacts])

  const resetUpload = useCallback(() => {
    setFile(null)
    setManifest({})
    setUploadProgress(0)
    setUploadStep(-1)
    setUploadStatus('')
    setIsUploading(false)
    setUploadError('')
    setDetectedMode(null)
    setZipPairs([])
    setZipUnpaired([])
    setSingleZipManifest(null)
    setBulkResults([])
    setDeleteError('')
  }, [])

  const analyzeFile = useCallback(async (selectedFile: File) => {
    setFile(selectedFile)
    setUploadError('')
    setDetectedMode(null)

    if (isZipFile(selectedFile.name)) {
      setZipAnalyzing(true)
      try {
        const entries = await extractZipEntries(selectedFile)
        const pairs = detectArtifactPairs(entries)
        const pairedFiles = new Set(pairs.flatMap(p => [p.mediaFile, p.manifestFile]))
        const unpaired = entries.filter(e => !pairedFiles.has(e) && !e.endsWith('/'))

        setZipPairs(pairs)
        setZipUnpaired(unpaired)

        if (pairs.length === 1) {
          setDetectedMode('singleZip')
          const manifestData = await extractManifestFromZip(selectedFile, pairs[0].manifestFile)
          setSingleZipManifest(manifestData)
        } else if (pairs.length > 1) {
          setDetectedMode('bulkZip')
        } else {
          setDetectedMode(null)
          setUploadError('No valid artifact pair found in zip')
        }
      } catch (err) {
        setUploadError(`Failed to analyze zip: ${err instanceof Error ? err.message : String(err)}`)
      } finally {
        setZipAnalyzing(false)
      }
    } else {
      setDetectedMode('standalone')
      const suggested = suggestManifestFromFile(selectedFile.name, selectedFile.size)
      setManifest(suggested)
    }
  }, [])

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(true)
  }

  const handleDragLeave = () => {
    setIsDragging(false)
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    const droppedFile = e.dataTransfer.files[0]
    if (droppedFile) {
      analyzeFile(droppedFile)
    }
  }

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const selectedFile = e.target.files?.[0]
    if (selectedFile) {
      analyzeFile(selectedFile)
    }
  }

  const handleIngest = async () => {
    if (!file || !detectedMode) return

    setIsUploading(true)
    setUploadProgress(0)
    setUploadStep(0)
    setUploadStatus('Uploading...')
    setUploadError('')

    try {
      let result
      if (detectedMode === 'standalone') {
        result = await uploadArtifactWithProgress(
          { file, manifest },
          (loaded, total) => {
            setUploadProgress(Math.round((loaded / total) * 100))
          },
        )
      } else if (detectedMode === 'singleZip') {
        const useManifest = singleZipManifest ?? manifest
        result = await uploadArtifactWithProgress(
          { file, manifest: useManifest },
          (loaded, total) => {
            setUploadProgress(Math.round((loaded / total) * 100))
          },
        )
      } else {
        return
      }

      setUploadProgress(100)
      setUploadStep(result.steps.length)
      setUploadStatus('Ingest complete')
      fetchArtifacts()
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'Ingest failed')
      setUploadStatus('Ingest failed')
    } finally {
      setIsUploading(false)
    }
  }

  const handleBulkIngest = async () => {
    if (!file || detectedMode !== 'bulkZip') return

    setIsUploading(true)
    setUploadProgress(0)
    setUploadStep(0)
    setUploadStatus('Uploading bulk artifacts...')
    setUploadError('')

    try {
      const result = await uploadBulkArtifacts(file, (loaded, total) => {
        setUploadProgress(Math.round((loaded / total) * 100))
      })

      setUploadProgress(100)
      setUploadStep(4)
      setBulkResults(result.results)
      setUploadStatus('Bulk ingest complete')
      fetchArtifacts()
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'Bulk ingest failed')
      setUploadStatus('Bulk ingest failed')
    } finally {
      setIsUploading(false)
    }
  }

  const handleDelete = async (artifact: ArtifactRecord) => {
    if (!artifact.manifest.packageId || !artifact.manifest.version) return
    setDeletingId(artifact.id)
    setDeleteError('')
    try {
      await deleteArtifact(artifact.manifest.packageId, artifact.manifest.version)
      fetchArtifacts()
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : 'Failed to delete artifact')
    } finally {
      setDeletingId(null)
    }
  }

  const handleViewDetails = (artifact: ArtifactRecord) => {
    setSelectedArtifact(artifact)
  }

  const updateManifest = (patch: Partial<ArtifactManifest>) => {
    setManifest(prev => ({ ...prev, ...patch }))
  }

  const dropzoneLabel = detectedMode === null
    ? 'Drop installer or zip file here'
    : detectedMode === 'standalone'
      ? 'Drop another installer or zip file'
      : 'Drop another zip file'

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-[var(--text-strong)]">Artifact Store</h1>
        <p className="mt-1 text-sm text-[var(--text-soft)]">Upload and manage installer artifacts</p>
      </div>

      {/* Upload Section */}
      <div className="space-y-4">
        {/* Dropzone */}
        <div
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
          className={cn(
            'flex flex-col items-center justify-center rounded-xl border-2 border-dashed p-8 transition-colors',
            isDragging
              ? 'border-blue-500 bg-blue-50/50'
              : 'border-[var(--surface-border)] bg-[var(--surface-subtle)] hover:border-[var(--text-soft)]'
          )}
        >
          <input
            type="file"
            accept=".msi,.exe,.zip,.tar.gz"
            onChange={handleFileSelect}
            className="hidden"
            id="artifact-file-input"
          />
          <label
            htmlFor="artifact-file-input"
            className="cursor-pointer text-center"
          >
            <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-[var(--accent)]/10">
              <Upload className="h-6 w-6 text-[var(--accent)]" />
            </div>
            <p className="text-sm font-medium text-[var(--text-strong)]">
              {dropzoneLabel}
            </p>
            <p className="mt-1 text-xs text-[var(--text-soft)]">or click to browse</p>
          </label>
        </div>

        {/* File info and actions */}
        {file && (
          <div className="space-y-4">
            <div className="flex items-center gap-3 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-3">
              {detectedMode === 'standalone' ? (
                <FileCode className="h-5 w-5 text-[var(--text-soft)]" />
              ) : (
                <FileArchive className="h-5 w-5 text-[var(--text-soft)]" />
              )}
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-[var(--text-strong)] truncate">{file.name}</p>
                <p className="text-xs text-[var(--text-soft)]">{formatBytes(file.size)}</p>
              </div>
              <button
                onClick={resetUpload}
                className="text-xs text-[var(--text-soft)] hover:text-[var(--text-strong)]"
              >
                Remove
              </button>
            </div>

            {zipAnalyzing && (
              <p className="text-sm text-[var(--text-soft)]">Analyzing zip...</p>
            )}

            {uploadError && (
              <div className="flex items-start gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{uploadError}</span>
              </div>
            )}

            {/* Standalone manifest form */}
            {detectedMode === 'standalone' && (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-4 space-y-3">
                <h3 className="text-sm font-semibold text-[var(--text-strong)]">Manifest</h3>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Package ID</label>
                    <input
                      type="text"
                      value={manifest.packageId ?? ''}
                      onChange={e => updateManifest({ packageId: e.target.value })}
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Version</label>
                    <input
                      type="text"
                      value={manifest.version ?? ''}
                      onChange={e => updateManifest({ version: e.target.value })}
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Channel</label>
                    <select
                      value={manifest.channel ?? 'stable'}
                      onChange={e => updateManifest({ channel: e.target.value })}
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    >
                      <option value="stable">stable</option>
                      <option value="canary">canary</option>
                      <option value="test">test</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Artifact Type</label>
                    <input
                      type="text"
                      value={manifest.artifactType ?? ''}
                      onChange={e => updateManifest({ artifactType: e.target.value })}
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    />
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Install Command</label>
                    <input
                      type="text"
                      value={manifest.installAdapter?.command ?? ''}
                      onChange={e =>
                        updateManifest({
                          installAdapter: { ...manifest.installAdapter, command: e.target.value },
                        })
                      }
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Install Arguments</label>
                    <input
                      type="text"
                      value={manifest.installAdapter?.arguments ?? ''}
                      onChange={e =>
                        updateManifest({
                          installAdapter: { ...manifest.installAdapter, arguments: e.target.value },
                        })
                      }
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    />
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Detection Type</label>
                    <input
                      type="text"
                      value={manifest.detection?.type ?? ''}
                      onChange={e =>
                        updateManifest({
                          detection: { ...manifest.detection, type: e.target.value },
                        })
                      }
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Detection Path</label>
                    <input
                      type="text"
                      value={manifest.detection?.path ?? ''}
                      onChange={e =>
                        updateManifest({
                          detection: { ...manifest.detection, path: e.target.value },
                        })
                      }
                      className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none"
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-xs font-medium text-[var(--text-strong)] mb-1">Risk Level</label>
                  <select
                    value={manifest.policyTags?.riskLevel ?? 'low'}
                    onChange={e =>
                      updateManifest({
                        policyTags: { ...manifest.policyTags, riskLevel: e.target.value },
                      })
                    }
                    className="w-full rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--text-strong)] focus:border-[var(--accent)] focus:outline-none sm:w-48"
                  >
                    <option value="low">low</option>
                    <option value="med">med</option>
                    <option value="high">high</option>
                  </select>
                </div>
                <button
                  onClick={handleIngest}
                  disabled={isUploading}
                  className="inline-flex items-center justify-center rounded-lg px-6 py-3 text-sm font-semibold text-white shadow-lg transition-all duration-200 ease-out hover:scale-[1.02] hover:shadow-xl active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100 disabled:hover:shadow-lg"
                  style={{
                    background: 'linear-gradient(135deg, var(--accent) 0%, var(--accent-strong) 100%)',
                  }}
                >
                  <Upload className="mr-2 h-4 w-4" />
                  Ingest Artifact
                </button>
              </div>
            )}

            {/* Single Zip summary */}
            {detectedMode === 'singleZip' && !zipAnalyzing && zipPairs.length === 1 && (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-4 space-y-3">
                <div className="flex items-center gap-2">
                  <CheckCircle2 className="h-5 w-5 text-green-600" />
                  <p className="text-sm text-[var(--text-strong)]">
                    Found artifact: <span className="font-medium">{zipPairs[0].baseName}</span>
                  </p>
                </div>
                {singleZipManifest && (
                  <div className="text-xs text-[var(--text-soft)] space-y-1 pl-7">
                    <p>Package ID: {singleZipManifest.packageId ?? '-'}</p>
                    <p>Version: {singleZipManifest.version ?? '-'}</p>
                    <p>Channel: {singleZipManifest.channel ?? '-'}</p>
                  </div>
                )}
                <div className="pl-7">
                  <button
                    onClick={handleIngest}
                    disabled={isUploading}
                    className="inline-flex items-center justify-center rounded-lg px-6 py-3 text-sm font-semibold text-white shadow-lg transition-all duration-200 ease-out hover:scale-[1.02] hover:shadow-xl active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100 disabled:hover:shadow-lg"
                    style={{
                      background: 'linear-gradient(135deg, var(--accent) 0%, var(--accent-strong) 100%)',
                    }}
                  >
                    <Upload className="mr-2 h-4 w-4" />
                    Ingest Artifact
                  </button>
                </div>
              </div>
            )}

            {/* Bulk Zip summary */}
            {detectedMode === 'bulkZip' && !zipAnalyzing && (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-4 space-y-3">
                <div className="flex items-center gap-2">
                  <Package className="h-5 w-5 text-[var(--accent)]" />
                  <p className="text-sm font-semibold text-[var(--text-strong)]">
                    Found {zipPairs.length} artifact{zipPairs.length !== 1 ? 's' : ''}
                  </p>
                </div>
                {zipPairs.length > 0 && (
                  <ul className="text-sm text-[var(--text-strong)] space-y-1 pl-7">
                    {zipPairs.map(pair => (
                      <li key={pair.baseName} className="flex items-center gap-2">
                        <CheckCircle2 className="h-3.5 w-3.5 text-green-600" />
                        {pair.baseName}
                      </li>
                    ))}
                  </ul>
                )}
                {zipUnpaired.length > 0 && (
                  <div className="flex items-start gap-2 rounded-md bg-amber-50 p-3 text-sm text-amber-800">
                    <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                    <div>
                      <p className="font-medium">Warning: unpaired files</p>
                      <ul className="mt-1 space-y-0.5">
                        {zipUnpaired.map(f => (
                          <li key={f}>{f}</li>
                        ))}
                      </ul>
                    </div>
                  </div>
                )}
                <div className="pl-7">
                  <button
                    onClick={handleBulkIngest}
                    disabled={isUploading || zipPairs.length === 0}
                    className="inline-flex items-center justify-center rounded-lg px-6 py-3 text-sm font-semibold text-white shadow-lg transition-all duration-200 ease-out hover:scale-[1.02] hover:shadow-xl active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100 disabled:hover:shadow-lg"
                    style={{
                      background: 'linear-gradient(135deg, var(--accent) 0%, var(--accent-strong) 100%)',
                    }}
                  >
                    <Upload className="mr-2 h-4 w-4" />
                    Ingest All
                  </button>
                </div>
              </div>
            )}

            {/* Upload progress */}
            {(isUploading || uploadProgress > 0 || uploadStatus) && (
              <div className="space-y-3 rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-4">
                <Progress value={uploadProgress} />
                <Stepper steps={ingestSteps} activeStep={uploadStep} />
                <p className="text-sm text-[var(--text-soft)]">{uploadStatus}</p>
              </div>
            )}

            {/* Bulk results */}
            {bulkResults.length > 0 && (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-4 space-y-2">
                <h3 className="text-sm font-semibold text-[var(--text-strong)]">Results</h3>
                <div className="space-y-1">
                  {bulkResults.map((result, idx) => (
                    <div
                      key={idx}
                      className={cn(
                        'flex items-center justify-between rounded-md px-3 py-2 text-sm',
                        result.status === 'success'
                          ? 'bg-green-50 text-green-800'
                          : 'bg-red-50 text-red-800'
                      )}
                    >
                      <span className="flex items-center gap-2 font-medium">
                        {result.status === 'success' ? (
                          <CheckCircle2 className="h-4 w-4" />
                        ) : (
                          <XCircle className="h-4 w-4" />
                        )}
                        {result.fileName}
                      </span>
                      <span>{result.status === 'success' ? 'Success' : `Failed: ${result.reason ?? ''}`}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Artifact Inventory */}
      <div>
        <h2 className="text-lg font-semibold text-[var(--text-strong)] mb-4">Artifact Inventory</h2>
        {deleteError && (
          <div className="mb-4 flex items-start gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{deleteError}</span>
            <button onClick={() => setDeleteError('')} className="ml-auto text-red-400 hover:text-red-600">
              <X className="h-4 w-4" />
            </button>
          </div>
        )}
        {loadingArtifacts ? (
          <p className="text-sm text-[var(--text-soft)]">Loading artifacts...</p>
        ) : artifacts.length === 0 ? (
          <div className="rounded-lg border border-dashed border-[var(--surface-border)] p-8 text-center">
            <p className="text-sm text-[var(--text-soft)]">No artifacts stored yet</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {artifacts.map(artifact => (
              <Card key={artifact.id}>
                <CardHeader>
                  <CardTitle>{artifact.manifest.packageId ?? artifact.fileName}</CardTitle>
                  <CardDescription>{artifact.fileName}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-2">
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="secondary">{artifact.manifest.version ?? '-'}</Badge>
                    <Badge variant="outline">{artifact.manifest.channel ?? 'stable'}</Badge>
                    <Badge variant="default">{artifact.manifest.artifactType ?? 'unknown'}</Badge>
                  </div>
                  <div className="text-xs text-[var(--text-soft)] space-y-1">
                    <p>Size: {formatBytes(artifact.sizeBytes)}</p>
                    <p>Created: {new Date(artifact.createdAt).toLocaleDateString()}</p>
                  </div>
                  <Badge variant={riskBadgeVariant(artifact.manifest.policyTags?.riskLevel)}>
                    Risk: {artifact.manifest.policyTags?.riskLevel ?? 'unknown'}
                  </Badge>
                </CardContent>
                <CardFooter className="flex justify-between">
                  <Button variant="outline" size="sm" onClick={() => handleViewDetails(artifact)}>
                    <Info className="mr-1.5 h-3.5 w-3.5" />
                    View Details
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    onClick={() => handleDelete(artifact)}
                    disabled={deletingId === artifact.id}
                    className="text-red-600 hover:bg-red-50 hover:text-red-700"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </CardFooter>
              </Card>
            ))}
          </div>
        )}
      </div>

      {/* Artifact Detail Modal */}
      <Modal open={selectedArtifact !== null} onOpenChange={open => !open && setSelectedArtifact(null)}>
        <ModalContent className="w-[min(92vw,36rem)]">
          <ModalHeader>
            <ModalTitle>{selectedArtifact?.manifest.packageId ?? 'Artifact Details'}</ModalTitle>
            <ModalDescription>{selectedArtifact?.fileName}</ModalDescription>
          </ModalHeader>
          <div className="space-y-4 overflow-y-auto px-4 pb-2 text-sm text-[var(--text-strong)]">
            {selectedArtifact && (
              <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3">
                  <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                    <p className="text-xs text-[var(--text-soft)] mb-1">Version</p>
                    <p className="font-medium">{selectedArtifact.manifest.version ?? '-'}</p>
                  </div>
                  <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                    <p className="text-xs text-[var(--text-soft)] mb-1">Channel</p>
                    <p className="font-medium">{selectedArtifact.manifest.channel ?? 'stable'}</p>
                  </div>
                  <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                    <p className="text-xs text-[var(--text-soft)] mb-1">Type</p>
                    <p className="font-medium">{selectedArtifact.manifest.artifactType ?? 'unknown'}</p>
                  </div>
                  <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                    <p className="text-xs text-[var(--text-soft)] mb-1">Size</p>
                    <p className="font-medium">{formatBytes(selectedArtifact.sizeBytes)}</p>
                  </div>
                </div>
                <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 space-y-2">
                  <p className="text-xs font-semibold text-[var(--text-soft)] uppercase tracking-wide">Install Adapter</p>
                  <div className="grid grid-cols-2 gap-2 text-xs">
                    <div>
                      <span className="text-[var(--text-soft)]">Command:</span>{' '}
                      <span className="font-mono text-[var(--text-strong)]">{selectedArtifact.manifest.installAdapter?.command ?? '-'}</span>
                    </div>
                    <div>
                      <span className="text-[var(--text-soft)]">Arguments:</span>{' '}
                      <span className="font-mono text-[var(--text-strong)]">{selectedArtifact.manifest.installAdapter?.arguments ?? '-'}</span>
                    </div>
                    <div>
                      <span className="text-[var(--text-soft)]">Timeout:</span>{' '}
                      <span className="text-[var(--text-strong)]">{selectedArtifact.manifest.installAdapter?.timeoutSeconds ?? 300}s</span>
                    </div>
                    <div>
                      <span className="text-[var(--text-soft)]">Type:</span>{' '}
                      <span className="text-[var(--text-strong)]">{selectedArtifact.manifest.installAdapter?.type ?? '-'}</span>
                    </div>
                  </div>
                </div>
                <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 space-y-2">
                  <p className="text-xs font-semibold text-[var(--text-soft)] uppercase tracking-wide">Detection</p>
                  <div className="grid grid-cols-2 gap-2 text-xs">
                    <div>
                      <span className="text-[var(--text-soft)]">Type:</span>{' '}
                      <span className="text-[var(--text-strong)]">{selectedArtifact.manifest.detection?.type ?? '-'}</span>
                    </div>
                    <div className="col-span-2">
                      <span className="text-[var(--text-soft)]">Path:</span>{' '}
                      <span className="font-mono text-[var(--text-strong)]">{selectedArtifact.manifest.detection?.path ?? '-'}</span>
                    </div>
                  </div>
                </div>
                <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 space-y-2">
                  <p className="text-xs font-semibold text-[var(--text-soft)] uppercase tracking-wide">Policy</p>
                  <div className="grid grid-cols-2 gap-2 text-xs">
                    <div>
                      <span className="text-[var(--text-soft)]">Risk Level:</span>{' '}
                      <Badge variant={riskBadgeVariant(selectedArtifact.manifest.policyTags?.riskLevel)}>
                        {selectedArtifact.manifest.policyTags?.riskLevel ?? 'unknown'}
                      </Badge>
                    </div>
                    <div>
                      <span className="text-[var(--text-soft)]">Approval Required:</span>{' '}
                      <span className="text-[var(--text-strong)]">{selectedArtifact.manifest.policyTags?.approvalRequired ? 'Yes' : 'No'}</span>
                    </div>
                    <div>
                      <span className="text-[var(--text-soft)]">Retryability:</span>{' '}
                      <span className="text-[var(--text-strong)]">{selectedArtifact.manifest.policyTags?.retryabilityClass ?? '-'}</span>
                    </div>
                    <div>
                      <span className="text-[var(--text-soft)]">Idempotency:</span>{' '}
                      <span className="text-[var(--text-strong)]">{selectedArtifact.manifest.policyTags?.idempotencyMode ?? '-'}</span>
                    </div>
                  </div>
                </div>
                <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 space-y-1 text-xs">
                  <p className="text-xs font-semibold text-[var(--text-soft)] uppercase tracking-wide">Metadata</p>
                  <p><span className="text-[var(--text-soft)]">Created:</span> <span className="text-[var(--text-strong)]">{new Date(selectedArtifact.createdAt).toLocaleString()}</span></p>
                  <p><span className="text-[var(--text-soft)]">Digest:</span> <span className="font-mono text-[var(--text-strong)] break-all">{selectedArtifact.digest ?? '-'}</span></p>
                </div>
              </div>
            )}
          </div>
          <ModalFooter>
            <Button variant="outline" className="w-full" onClick={() => setSelectedArtifact(null)}>
              Close
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </div>
  )
}
