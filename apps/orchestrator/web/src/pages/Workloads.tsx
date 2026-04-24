import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  createWorkloadRevision,
  importBulkWorkloads,
  listArtifacts,
  listWorkloadRevisions,
  listWorkloads,
  publishWorkloadRevision,
} from '../services/api'
import { Modal, ModalContent, ModalDescription, ModalFooter, ModalHeader, ModalTitle } from '../components/ui/modal'
import type { ArtifactRecord, WorkloadDefinition, WorkloadRevision, BulkWorkloadImportResultItem } from '../types'
import { Upload, FileJson, AlertTriangle, CheckCircle2, XCircle } from 'lucide-react'

interface RevisionForm {
  workloadId: string
  revision: string
  packageIds: string[]
}

function formatBytes(bytes?: number): string {
  if (bytes === undefined || bytes === null) return '-'
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`
}

type DropMode = 'workloadDefinition' | 'workloadVersion' | null

export default function Workloads() {
  const [workloads, setWorkloads] = useState<WorkloadDefinition[]>([])
  const [artifacts, setArtifacts] = useState<ArtifactRecord[]>([])
  const [revisions, setRevisions] = useState<WorkloadRevision[]>([])
  const [selectedWorkloadId, setSelectedWorkloadId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [isDraftModalOpen, setIsDraftModalOpen] = useState(false)
  const [isRevisionModalOpen, setIsRevisionModalOpen] = useState(false)
  const [dropMode, setDropMode] = useState<DropMode>(null)

  // Drag-drop state for bulk workloads
  const [bulkFile, setBulkFile] = useState<File | null>(null)
  const [bulkFileName, setBulkFileName] = useState<string>('')
  const [isBulkImporting, setIsBulkImporting] = useState(false)
  const [bulkResults, setBulkResults] = useState<BulkWorkloadImportResultItem[]>([])
  const [bulkError, setBulkError] = useState<string>('')
  const [isDragging, setIsDragging] = useState(false)

  const [revisionForm, setRevisionForm] = useState<RevisionForm>({
    workloadId: '',
    revision: '1.0.0',
    packageIds: [],
  })

  const refresh = async (activeWorkloadId?: string) => {
    const [workloadData, artifactData] = await Promise.all([listWorkloads(), listArtifacts()])
    setWorkloads(workloadData)
    setArtifacts(artifactData)

    const fallbackWorkloadId = activeWorkloadId || selectedWorkloadId || workloadData[0]?.id || ''
    setSelectedWorkloadId(fallbackWorkloadId)

    if (fallbackWorkloadId) {
      const revisionData = await listWorkloadRevisions(fallbackWorkloadId)
      setRevisions(revisionData)
      setRevisionForm(current => ({ ...current, workloadId: fallbackWorkloadId }))
    } else {
      setRevisions([])
      setRevisionForm(current => ({ ...current, workloadId: '' }))
    }
  }

  useEffect(() => {
    let active = true

    const loadInitialData = async () => {
      try {
        const [workloadData, artifactData] = await Promise.all([listWorkloads(), listArtifacts()])
        if (!active) {
          return
        }

        setWorkloads(workloadData)
        setArtifacts(artifactData)

        const fallbackWorkloadId = workloadData[0]?.id || ''
        setSelectedWorkloadId(fallbackWorkloadId)

        if (fallbackWorkloadId) {
          const revisionData = await listWorkloadRevisions(fallbackWorkloadId)
          if (!active) {
            return
          }
          setRevisions(revisionData)
          setRevisionForm(current => ({ ...current, workloadId: fallbackWorkloadId }))
        } else {
          setRevisions([])
          setRevisionForm(current => ({ ...current, workloadId: '' }))
        }
      } catch {
        if (active) {
          setError('Failed to load workloads and revisions.')
        }
      } finally {
        if (active) {
          setLoading(false)
        }
      }
    }

    void loadInitialData()

    return () => {
      active = false
    }
  }, [])

  const selectedPackages = useMemo(() => {
    return artifacts.filter(artifact => revisionForm.packageIds.includes(artifact.id))
  }, [artifacts, revisionForm.packageIds])

  const canCreateRevision = revisionForm.packageIds.length >= 2 && revisionForm.packageIds.length <= 3

  const onCreateRevision = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setMessage(null)

    try {
      const created = await createWorkloadRevision({
        workloadId: revisionForm.workloadId,
        revision: revisionForm.revision,
        packageSteps: selectedPackages.map((artifact, index) => ({
          packageId: artifact.id,
          packageName: artifact.manifest.packageId ?? artifact.fileName,
          packageVersion: artifact.manifest.version ?? '0.0.0',
          packageIndex: index + 1,
          stepId: 'install-or-upgrade',
        })),
      })
      setRevisionForm(current => ({ ...current, packageIds: [] }))
      await refresh(revisionForm.workloadId)
      setMessage(`Created WorkloadRevision draft ${created.revision}`)
      setIsRevisionModalOpen(false)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to create revision draft.')
    }
  }

  const onPublishRevision = async (revisionId: string) => {
    setError(null)
    setMessage(null)

    try {
      const published = await publishWorkloadRevision(selectedWorkloadId, revisionId)
      await refresh(selectedWorkloadId)
      setMessage(`Published revision ${published.revision}. Revision is now immutable.`)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to publish revision.')
    }
  }

  const handleDragOver = (e: React.DragEvent, mode: DropMode) => {
    e.preventDefault()
    setIsDragging(true)
    setDropMode(mode)
  }

  const handleDragLeave = () => {
    setIsDragging(false)
    setDropMode(null)
  }

  const handleDrop = (e: React.DragEvent, mode: DropMode) => {
    e.preventDefault()
    setIsDragging(false)
    setDropMode(null)

    const droppedFile = e.dataTransfer.files[0]
    if (!droppedFile) return

    const isJson = droppedFile.name.endsWith('.json') || droppedFile.name.endsWith('.jsonc')
    if (!isJson) {
      setError('Only JSON files (.json, .jsonc) are accepted for workload definitions.')
      return
    }

    if (mode === 'workloadDefinition') {
      setBulkFile(droppedFile)
      setBulkFileName(droppedFile.name)
      setBulkResults([])
      setBulkError('')
      setIsDraftModalOpen(true)
    }
  }

  const handleBulkImport = async () => {
    if (!bulkFile) return

    setIsBulkImporting(true)
    setBulkError('')
    setBulkResults([])

    try {
      const result = await importBulkWorkloads(bulkFile)
      setBulkResults(result.results)
      if (result.results.some(r => r.status === 'success')) {
        setMessage(`Successfully imported ${result.results.filter(r => r.status === 'success').length} workload(s).`)
      }
      await refresh()
    } catch (err) {
      setBulkError(err instanceof Error ? err.message : 'Bulk import failed')
    } finally {
      setIsBulkImporting(false)
    }
  }

  const resetBulkImport = useCallback(() => {
    setBulkFile(null)
    setBulkFileName('')
    setBulkResults([])
    setBulkError('')
    setIsDraftModalOpen(false)
    setIsDragging(false)
    setDropMode(null)
  }, [])

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-6">
      {error && (
        <div className="rounded-lg border border-[var(--status-danger-border)] bg-[var(--status-danger-bg)] px-4 py-3 text-sm text-[var(--status-danger-text)]">
          {error}
        </div>
      )}
      {message && (
        <div className="rounded-lg border border-[var(--status-success-border)] bg-[var(--status-success-bg)] px-4 py-3 text-sm text-[var(--status-success-text)]">
          {message}
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        <section
          onDragOver={e => handleDragOver(e, 'workloadDefinition')}
          onDragLeave={handleDragLeave}
          onDrop={e => handleDrop(e, 'workloadDefinition')}
          className={`rounded-2xl border-2 border-dashed p-6 shadow-[var(--surface-shadow)] transition-colors ${
            isDragging && dropMode === 'workloadDefinition'
              ? 'border-blue-500 bg-blue-50/50'
              : 'border-[var(--surface-border)] bg-[var(--surface)]'
          }`}
        >
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-full bg-[var(--accent)]/10">
              <Upload className="h-5 w-5 text-[var(--accent)]" />
            </div>
            <div>
              <h2 className="text-lg font-semibold text-[var(--text-strong)]">Import Workload Definitions</h2>
              <p className="mt-1 text-xs text-[var(--text-soft)]">Drag & drop a workloads.json file to bulk import workload definitions with pre-defined packages.</p>
            </div>
          </div>
        </section>

        <section
          onDragOver={e => handleDragOver(e, 'workloadVersion')}
          onDragLeave={handleDragLeave}
          onDrop={e => handleDrop(e, 'workloadVersion')}
          className={`rounded-2xl border-2 border-dashed p-6 shadow-[var(--surface-shadow)] transition-colors ${
            isDragging && dropMode === 'workloadVersion'
              ? 'border-blue-500 bg-blue-50/50'
              : 'border-[var(--surface-border)] bg-[var(--surface)]'
          }`}
        >
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-full bg-[var(--accent)]/10">
              <FileJson className="h-5 w-5 text-[var(--accent)]" />
            </div>
            <div>
              <h2 className="text-lg font-semibold text-[var(--text-strong)]">Create Workload Version Draft</h2>
              <p className="mt-1 text-xs text-[var(--text-soft)]">PoC rule: each revision must include exactly 2-3 package steps.</p>
              <button
                type="button"
                onClick={() => setIsRevisionModalOpen(true)}
                disabled={workloads.length === 0}
                className="mt-3 rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
              >
                Create Workload Version Draft
              </button>
            </div>
          </div>
        </section>
      </div>

      <Modal open={isDraftModalOpen} onOpenChange={setIsDraftModalOpen}>
        <ModalContent className="w-[min(92vw,40rem)]">
          <ModalHeader>
            <ModalTitle>Bulk Import Workload Definitions</ModalTitle>
            <ModalDescription>
              {bulkFileName ? `File: ${bulkFileName}` : 'Drag & drop a workloads.json file to bulk import workload definitions.'}
            </ModalDescription>
          </ModalHeader>

          <div className="space-y-4 px-4 pb-4">
            {!bulkFile ? (
              <div
                onDragOver={e => {
                  e.preventDefault()
                  setIsDragging(true)
                }}
                onDragLeave={() => setIsDragging(false)}
                onDrop={e => {
                  e.preventDefault()
                  setIsDragging(false)
                  const file = e.dataTransfer.files[0]
                  if (file && (file.name.endsWith('.json') || file.name.endsWith('.jsonc'))) {
                    setBulkFile(file)
                    setBulkFileName(file.name)
                  } else {
                    setBulkError('Only JSON files (.json, .jsonc) are accepted.')
                  }
                }}
                className={`flex flex-col items-center justify-center rounded-xl border-2 border-dashed p-8 transition-colors ${
                  isDragging
                    ? 'border-blue-500 bg-blue-50/50'
                    : 'border-[var(--surface-border)] bg-[var(--surface-subtle)]'
                }`}
              >
                <input
                  type="file"
                  accept=".json,.jsonc"
                  onChange={e => {
                    const file = e.target.files?.[0]
                    if (file) {
                      setBulkFile(file)
                      setBulkFileName(file.name)
                    }
                  }}
                  className="hidden"
                  id="workload-json-input"
                />
                <label htmlFor="workload-json-input" className="cursor-pointer text-center">
                  <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-[var(--accent)]/10">
                    <FileJson className="h-6 w-6 text-[var(--accent)]" />
                  </div>
                  <p className="text-sm font-medium text-[var(--text-strong)]">
                    Drop workloads.json file here
                  </p>
                  <p className="mt-1 text-xs text-[var(--text-soft)]">or click to browse</p>
                </label>
              </div>
            ) : (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-4 space-y-3">
                <div className="flex items-center gap-3">
                  <FileJson className="h-5 w-5 text-[var(--text-soft)]" />
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-[var(--text-strong)] truncate">{bulkFileName}</p>
                    <p className="text-xs text-[var(--text-soft)]">{formatBytes(bulkFile.size)}</p>
                  </div>
                  <button
                    onClick={resetBulkImport}
                    className="text-xs text-[var(--text-soft)] hover:text-[var(--text-strong)]"
                  >
                    Remove
                  </button>
                </div>

                {bulkError && (
                  <div className="flex items-start gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700">
                    <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                    <span>{bulkError}</span>
                  </div>
                )}

                {bulkResults.length > 0 && (
                  <div className="rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 space-y-2">
                    <p className="text-sm font-medium text-[var(--text-strong)]">Import Results</p>
                    <div className="space-y-1">
                      {bulkResults.map((result, idx) => (
                        <div
                          key={idx}
                          className={`flex items-center justify-between rounded-md px-3 py-2 text-sm ${
                            result.status === 'success'
                              ? 'bg-green-50 text-green-800'
                              : 'bg-red-50 text-red-800'
                          }`}
                        >
                          <span className="flex items-center gap-2 font-medium">
                            {result.status === 'success' ? (
                              <CheckCircle2 className="h-4 w-4" />
                            ) : (
                              <XCircle className="h-4 w-4" />
                            )}
                            {result.name} ({result.slug})
                          </span>
                          <span>{result.status === 'success' ? 'Success' : `Failed: ${result.reason ?? ''}`}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                <button
                  onClick={handleBulkImport}
                  disabled={isBulkImporting || !bulkFile}
                  className="inline-flex items-center justify-center rounded-lg px-6 py-3 text-sm font-semibold text-white shadow-lg transition-all duration-200 ease-out hover:scale-[1.02] hover:shadow-xl active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100 disabled:hover:shadow-lg"
                  style={{
                    background: 'linear-gradient(135deg, var(--accent) 0%, var(--accent-strong) 100%)',
                  }}
                >
                  <Upload className="mr-2 h-4 w-4" />
                  {isBulkImporting ? 'Importing...' : 'Import Workloads'}
                </button>
              </div>
            )}

            <ModalFooter className="px-0 pb-0 pt-2 sm:flex-row sm:justify-end">
              <button
                type="button"
                onClick={() => {
                  resetBulkImport()
                  setIsDraftModalOpen(false)
                }}
                className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
              >
                Cancel
              </button>
            </ModalFooter>
          </div>
        </ModalContent>
      </Modal>

      <Modal open={isRevisionModalOpen} onOpenChange={setIsRevisionModalOpen}>
        <ModalContent className="w-[min(92vw,40rem)]">
          <ModalHeader>
            <ModalTitle>Create Workload Version Draft</ModalTitle>
            <ModalDescription>Choose a workload and select 2-3 ordered package steps.</ModalDescription>
          </ModalHeader>
          <form onSubmit={onCreateRevision} className="space-y-3 px-4 pb-4">
            <label className="block text-sm text-[var(--text-soft)]">
              Workload
              <select
                value={revisionForm.workloadId}
                onChange={event => {
                  const workloadId = event.target.value
                  setRevisionForm(current => ({ ...current, workloadId }))
                  setSelectedWorkloadId(workloadId)
                  listWorkloadRevisions(workloadId).then(setRevisions)
                }}
                className="mt-1 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
                required
              >
                <option value="">Select workload...</option>
                {workloads.map(item => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="block text-sm text-[var(--text-soft)]">
              Revision
              <input
                value={revisionForm.revision}
                onChange={event => setRevisionForm(current => ({ ...current, revision: event.target.value }))}
                className="mt-1 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
                required
              />
            </label>
            <label className="block text-sm text-[var(--text-soft)]">
              Package steps (2-3)
              <select
                multiple
                value={revisionForm.packageIds}
                onChange={event => {
                  const values = Array.from(event.target.selectedOptions).map(option => option.value)
                  setRevisionForm(current => ({ ...current, packageIds: values }))
                }}
                className="mt-1 h-28 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
              >
                {artifacts.map(artifact => (
                  <option key={artifact.id} value={artifact.id}>
                    {artifact.manifest.packageId} {artifact.manifest.version}
                  </option>
                ))}
              </select>
            </label>
            <ModalFooter className="px-0 pb-0 pt-2 sm:flex-row sm:justify-end">
              <button
                type="button"
                onClick={() => setIsRevisionModalOpen(false)}
                className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={!canCreateRevision}
                className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
              >
                Create Revision Draft
              </button>
            </ModalFooter>
          </form>
        </ModalContent>
      </Modal>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h2 className="text-lg font-semibold text-[var(--text-strong)]">Definitions and Latest Version</h2>
        <div className="mt-4 overflow-x-auto">
          <table className="min-w-full divide-y divide-[var(--surface-border)]">
            <thead>
              <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                <th className="px-4 py-3">Workload</th>
                <th className="px-4 py-3">Description</th>
                <th className="px-4 py-3">Latest Version</th>
                <th className="px-4 py-3">Version Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--surface-border)] text-sm">
              {workloads.map(item => (
                <tr key={item.id}>
                  <td className="px-4 py-3 font-medium text-[var(--text-strong)]">{item.name}</td>
                  <td className="px-4 py-3 text-[var(--text-soft)]">{item.description}</td>
                  <td className="px-4 py-3 text-[var(--text-soft)]">{item.latestRevision?.revision ?? 'No version yet'}</td>
                  <td className="px-4 py-3">
                    <span className="rounded-full bg-[var(--surface-muted)] px-2 py-1 text-xs text-[var(--text-soft)]">
                      {item.latestRevision?.state ?? 'n/a'}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h2 className="text-lg font-semibold text-[var(--text-strong)]">Version List</h2>
        <p className="mt-1 text-xs text-[var(--text-soft)]">Publish action transitions draft revisions to immutable published state.</p>
        <div className="mt-4 space-y-3">
          {revisions.length === 0 ? (
            <p className="text-sm text-[var(--text-soft)]">No versions for selected workload.</p>
          ) : (
            revisions.map(revision => (
              <div key={revision.id} className="rounded-xl border border-[var(--surface-border)] p-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-semibold text-[var(--text-strong)]">Version {revision.revision}</p>
                    <p className="text-xs text-[var(--text-soft)]">{revision.packageSteps.length} package steps</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="rounded-full bg-[var(--surface-muted)] px-2 py-1 text-xs text-[var(--text-soft)]">{revision.state}</span>
                    {revision.state === 'draft' && (
                      <button
                        onClick={() => onPublishRevision(revision.id)}
                        className="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-700"
                      >
                        Publish Revision
                      </button>
                    )}
                  </div>
                </div>
              </div>
            ))
          )}
        </div>
      </section>
    </div>
  )
}
