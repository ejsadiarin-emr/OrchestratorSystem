import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  createWorkloadRevision,
  deleteWorkload,
  getWorkload,
  importBulkWorkloads,
  listArtifacts,
  listWorkloads,
  publishWorkloadRevision,
} from '../services/api'
import { Modal, ModalContent, ModalDescription, ModalFooter, ModalHeader, ModalTitle } from '../components/ui/modal'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import type { ArtifactRecord, WorkloadDefinition, WorkloadRevision, BulkWorkloadImportResultItem } from '../types'
import { Upload, FileJson, AlertTriangle, CheckCircle2, XCircle, Trash2, Info, Loader2, Plus } from 'lucide-react'

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

function statusBadgeClass(state?: string) {
  switch (state) {
    case 'published':
      return 'border-[var(--status-success-border)] bg-[var(--status-success-bg)] text-[var(--status-success-text)]'
    case 'draft':
      return 'border-amber-200 bg-amber-50 text-amber-700'
    default:
      return 'bg-[var(--surface-muted)] text-[var(--text-soft)]'
  }
}

type DropMode = 'workloadDefinition' | 'workloadRevision' | null

export default function Workloads() {
  const [workloads, setWorkloads] = useState<WorkloadDefinition[]>([])
  const [artifacts, setArtifacts] = useState<ArtifactRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [isRevisionModalOpen, setIsRevisionModalOpen] = useState(false)
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false)
  const [detailWorkload, setDetailWorkload] = useState<(WorkloadDefinition & { revisions: WorkloadRevision[] }) | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  const [dropMode, setDropMode] = useState<DropMode>(null)
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false)
  const [isImportModalOpen, setIsImportModalOpen] = useState(false)
  const [workloadToDelete, setWorkloadToDelete] = useState<WorkloadDefinition | null>(null)
  const [expandedInitSteps, setExpandedInitSteps] = useState<Set<string>>(new Set())

  const toggleInitSteps = (stepId: string) => {
    setExpandedInitSteps(prev => {
      const next = new Set(prev)
      if (next.has(stepId)) next.delete(stepId)
      else next.add(stepId)
      return next
    })
  }

  const hasInitSteps = (step: { preInitSteps?: string[]; postInitSteps?: string[] }) =>
    (step.preInitSteps && step.preInitSteps.length > 0) ||
    (step.postInitSteps && step.postInitSteps.length > 0)

  // Drag-drop state for bulk workloads
  const [bulkFile, setBulkFile] = useState<File | null>(null)
  const [bulkFileName, setBulkFileName] = useState<string>('')
  const [isBulkImporting, setIsBulkImporting] = useState(false)
  const [bulkResults, setBulkResults] = useState<BulkWorkloadImportResultItem[]>([])
  const [bulkError, setBulkError] = useState<string>('')
  const [isDragging, setIsDragging] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [revisionForm, setRevisionForm] = useState<RevisionForm>({
    workloadId: '',
    revision: '1.0.0',
    packageIds: [],
  })

  const refresh = async () => {
    const [workloadData, artifactData] = await Promise.all([listWorkloads(), listArtifacts()])
    setWorkloads(workloadData)
    setArtifacts(artifactData)
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
    return artifacts.filter(artifact =>
      revisionForm.packageIds.includes(artifact.packageEntityId ?? artifact.id))
  }, [artifacts, revisionForm.packageIds])

  const canCreateRevision = revisionForm.packageIds.length >= 1

  const onCreateRevision = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setMessage(null)

    try {
      const created = await createWorkloadRevision({
        workloadId: revisionForm.workloadId,
        revision: revisionForm.revision,
        packageSteps: selectedPackages.map((artifact, index) => ({
          packageId: artifact.packageEntityId ?? artifact.id,
          packageName: artifact.manifest.packageId ?? artifact.fileName,
          packageVersion: artifact.manifest.version ?? '0.0.0',
          packageIndex: index + 1,
          stepId: 'install-or-upgrade',
        })),
      })
      setRevisionForm(current => ({ ...current, packageIds: [] }))
      await refresh()
      setMessage(`Created WorkloadRevision draft ${created.revision}`)
      setIsRevisionModalOpen(false)

      // refresh detail modal if open for same workload
      if (detailWorkload && detailWorkload.id === created.workloadId) {
        setDetailLoading(true)
        try {
          const updated = await getWorkload(created.workloadId)
          setDetailWorkload(updated)
        } catch {
          // ignore detail refresh errors
        } finally {
          setDetailLoading(false)
        }
      }
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to create revision draft.')
    }
  }

  const onPublishRevision = async (workloadId: string, revisionId: string) => {
    setError(null)
    setMessage(null)

    try {
      const published = await publishWorkloadRevision(workloadId, revisionId)
      await refresh()
      setMessage(`Published revision ${published.revision}. Revision is now immutable.`)

      // refresh detail modal if open for same workload
      if (detailWorkload && detailWorkload.id === workloadId) {
        setDetailLoading(true)
        try {
          const updated = await getWorkload(workloadId)
          setDetailWorkload(updated)
        } catch {
          // ignore detail refresh errors
        } finally {
          setDetailLoading(false)
        }
      }
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to publish revision.')
    }
  }

  const onDeleteWorkload = async () => {
    if (!workloadToDelete) return
    setError(null)
    setMessage(null)

    try {
      await deleteWorkload(workloadToDelete.id)
      await refresh()
      setMessage(`Deleted workload "${workloadToDelete.name}".`)
      setIsDeleteModalOpen(false)
      setWorkloadToDelete(null)
      if (detailWorkload?.id === workloadToDelete.id) {
        setIsDetailModalOpen(false)
        setDetailWorkload(null)
      }
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to delete workload.')
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
    }
  }

  const triggerFileInput = () => {
    fileInputRef.current?.click()
  }

  const handleFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) {
      const isJson = file.name.endsWith('.json') || file.name.endsWith('.jsonc')
      if (!isJson) {
        setError('Only JSON files (.json, .jsonc) are accepted.')
        return
      }
      setBulkFile(file)
      setBulkFileName(file.name)
      setBulkResults([])
      setBulkError('')
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
    setIsDragging(false)
    setDropMode(null)
  }, [])

  const openWorkloadDetail = async (workloadId: string) => {
    setDetailWorkload(null)
    setDetailLoading(true)
    setIsDetailModalOpen(true)
    try {
      const workload = await getWorkload(workloadId)
      setDetailWorkload(workload)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to load workload details.')
    } finally {
      setDetailLoading(false)
    }
  }

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-[var(--text-strong)]">Workload Definitions</h1>
        <p className="mt-1 text-sm text-[var(--text-soft)]">Define WorkloadDefinition entities, then create immutable WorkloadRevision records with 1 or more ordered package steps.</p>
      </div>

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

      <div>
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-3">
            <h2 className="text-lg font-semibold text-[var(--text-strong)]">Workload Definitions</h2>
            <span className="rounded-full bg-[var(--surface-subtle)] px-2.5 py-0.5 text-xs font-medium text-[var(--text-soft)]">
              {workloads.length}
            </span>
          </div>
          <Button
            size="sm"
            onClick={() => setIsImportModalOpen(true)}
            className="rounded-full bg-[var(--accent)] px-3 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]"
          >
            <Plus className="h-4 w-4" />
          </Button>
        </div>
        {workloads.length === 0 ? (
          <div className="rounded-lg border border-dashed border-[var(--surface-border)] p-8 text-center">
            <p className="text-sm text-[var(--text-soft)]">No workload definitions yet</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {workloads.map(item => (
              <Card key={item.id}>
                <CardHeader>
                  <CardTitle>{item.name}</CardTitle>
                  <CardDescription>{item.description}</CardDescription>
                </CardHeader>
                <CardContent className="space-y-2">
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="secondary">
                      {item.latestRevision?.revision ?? 'No revision yet'}
                    </Badge>
                    <Badge variant="outline" className={statusBadgeClass(item.latestRevision?.state)}>
                      {item.latestRevision?.state ?? 'n/a'}
                    </Badge>
                  </div>
                  <div className="text-xs text-[var(--text-soft)] space-y-1">
                    <p>Created: {new Date(item.createdAt).toLocaleDateString()}</p>
                    <p>Revisions: {item.revisionCount ?? 0}</p>
                  </div>
                </CardContent>
                <CardFooter className="flex justify-between">
                  <Button variant="outline" size="sm" onClick={() => openWorkloadDetail(item.id)}>
                    <Info className="mr-1.5 h-3.5 w-3.5" />
                    View Details
                  </Button>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      onClick={() => {
                        setWorkloadToDelete(item)
                        setIsDeleteModalOpen(true)
                      }}
                      className="text-red-600 hover:bg-red-50 hover:text-red-700"
                      title="Delete workload"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </CardFooter>
              </Card>
            ))}
          </div>
        )}
      </div>

      <Modal open={isImportModalOpen} onOpenChange={setIsImportModalOpen}>
        <ModalContent className="w-[min(92vw,28rem)]">
          <ModalHeader>
            <ModalTitle>Import Workload Definitions</ModalTitle>
            <ModalDescription>Drag & drop a workloads.json file to bulk import.</ModalDescription>
          </ModalHeader>
          <div className="space-y-4 px-4 pb-4">
            <section
              onClick={triggerFileInput}
              onDragOver={e => handleDragOver(e, 'workloadDefinition')}
              onDragLeave={handleDragLeave}
              onDrop={e => handleDrop(e, 'workloadDefinition')}
              className={`rounded-2xl border-2 border-dashed p-8 text-center transition-colors cursor-pointer ${
                isDragging && dropMode === 'workloadDefinition'
                  ? 'border-blue-500 bg-blue-50/50'
                  : 'border-[var(--surface-border)] bg-[var(--surface)] hover:border-[var(--text-soft)]'
              }`}
            >
              <input
                type="file"
                accept=".json,.jsonc"
                onChange={handleFileInputChange}
                className="hidden"
                ref={fileInputRef}
              />
              <div className="flex flex-col items-center gap-3">
                <div className="flex h-12 w-12 items-center justify-center rounded-full bg-[var(--accent)]/10">
                  <Upload className="h-6 w-6 text-[var(--accent)]" />
                </div>
                <div>
                  <p className="text-sm font-semibold text-[var(--text-strong)]">Drop file here</p>
                  <p className="mt-1 text-xs text-[var(--text-soft)]">or click to browse</p>
                </div>
              </div>
            </section>

            {bulkFile && (
              <div className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-glass)] p-4 space-y-3">
                <div className="flex items-center gap-3">
                  <FileJson className="h-5 w-5 text-[var(--text-soft)] shrink-0" />
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
                    <p className="text-sm font-medium text-[var(--text-strong)]">Results</p>
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
                              <CheckCircle2 className="h-4 w-4 shrink-0" />
                            ) : (
                              <XCircle className="h-4 w-4 shrink-0" />
                            )}
                            {result.name}
                          </span>
                          <span className="text-xs">
                            {result.status === 'success' ? 'Success' : `Failed`}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {isBulkImporting && (
                  <div className="flex items-center gap-2 text-sm text-[var(--text-soft)]">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    <span>Importing workloads...</span>
                  </div>
                )}

                <button
                  onClick={handleBulkImport}
                  disabled={isBulkImporting || !bulkFile}
                  className="inline-flex w-full items-center justify-center rounded-lg px-6 py-3 text-sm font-semibold text-white shadow-lg transition-all duration-200 ease-out hover:scale-[1.02] hover:shadow-xl active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100 disabled:hover:shadow-lg"
                  style={{
                    background: 'linear-gradient(135deg, var(--accent) 0%, var(--accent-strong) 100%)',
                  }}
                >
                  <Upload className="mr-2 h-4 w-4" />
                  {isBulkImporting ? 'Importing...' : 'Import Workloads'}
                </button>
              </div>
            )}
          </div>
        </ModalContent>
      </Modal>

      <Modal open={isRevisionModalOpen} onOpenChange={setIsRevisionModalOpen}>
        <ModalContent className="w-[min(92vw,40rem)]">
          <ModalHeader>
            <ModalTitle>Create Workload Revision Draft</ModalTitle>
            <ModalDescription>Choose a workload and select 1 or more ordered package steps.</ModalDescription>
          </ModalHeader>
          <form onSubmit={onCreateRevision} className="space-y-3 px-4 pb-4">
            <label className="block text-sm text-[var(--text-soft)]">
              Workload
              <select
                value={revisionForm.workloadId}
                onChange={event => {
                  const workloadId = event.target.value
                  setRevisionForm(current => ({ ...current, workloadId }))
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
              Package steps (1 or more)
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
                  <option key={artifact.id} value={artifact.packageEntityId ?? artifact.id}>
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

      <Modal open={isDetailModalOpen} onOpenChange={setIsDetailModalOpen}>
        <ModalContent className="w-[min(92vw,40rem)] max-h-[90vh] overflow-y-auto">
          <ModalHeader>
            <ModalTitle>Workload details</ModalTitle>
            {detailWorkload && (
              <ModalDescription>{detailWorkload.name}</ModalDescription>
            )}
          </ModalHeader>
          {detailLoading && (
            <div className="px-4 pb-4 text-sm text-[var(--text-soft)]">Loading...</div>
          )}
          {!detailLoading && detailWorkload && (
            <div className="space-y-4 px-4 pb-4">
              <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--text-soft)]">
                <p>
                  Description:{' '}
                  <span className="font-medium text-[var(--text-strong)]">
                    {detailWorkload.description || '-'}
                  </span>
                </p>
                <p className="mt-1">
                  Created:{' '}
                  <span className="font-medium text-[var(--text-strong)]">
                    {new Date(detailWorkload.createdAt).toLocaleString()}
                  </span>
                </p>
                <p className="mt-1">
                  Revisions:{' '}
                  <span className="font-medium text-[var(--text-strong)]">
                    {detailWorkload.revisions.length}
                  </span>
                </p>
              </section>

              <div className="flex items-center justify-between">
                <p className="text-sm font-medium text-[var(--text-strong)]">Revisions</p>
              </div>

              <section className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3 text-sm">
                {detailWorkload.revisions.length === 0 ? (
                  <p className="text-[var(--text-soft)]">No revisions yet.</p>
                ) : (
                  <div className="space-y-2">
                    {detailWorkload.revisions.map(revision => (
                      <div key={revision.id} className="rounded-lg border border-[var(--surface-border)] bg-[var(--surface)] p-3">
                        <div className="flex items-center justify-between gap-2">
                          <p className="font-medium text-[var(--text-strong)]">
                            {revision.revision}
                          </p>
                          <div className="flex items-center gap-2">
                            <span className={`rounded-full px-2 py-0.5 text-xs font-medium border ${statusBadgeClass(revision.state)}`}>
                              {revision.state}
                            </span>
                            {revision.state === 'draft' && (
                              <button
                                onClick={() => onPublishRevision(detailWorkload.id, revision.id)}
                                className="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-700"
                              >
                                Publish
                              </button>
                            )}
                          </div>
                        </div>
                        <div className="mt-2 space-y-2">
                          {revision.defaultShell && (
                            <p className="text-[11px] text-[var(--text-soft)]">
                              Shell: <span className="font-mono text-[var(--text-strong)]">{revision.defaultShell}</span>
                            </p>
                          )}
                          {revision.packageSteps?.map(step => (
                            <div key={step.stepId} className="rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-2">
                              <div className="flex items-center justify-between gap-2">
                                <p className="text-xs text-[var(--text-soft)]">
                                  {step.packageIndex}. {step.packageName} {step.packageVersion}
                                </p>
                                {hasInitSteps(step) && (
                                  <button
                                    onClick={() => toggleInitSteps(step.stepId)}
                                    className="text-[11px] text-[var(--accent)] hover:text-[var(--accent-strong)] focus:outline-none"
                                  >
                                    {expandedInitSteps.has(step.stepId) ? 'Collapse' : 'Init Steps'}
                                  </button>
                                )}
                              </div>
                              {hasInitSteps(step) && expandedInitSteps.has(step.stepId) && (
                                <div className="mt-2 space-y-1.5 border-t border-[var(--surface-border)] pt-2">
                                  {step.preInitSteps && step.preInitSteps.length > 0 && (
                                    <div>
                                      <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--text-soft)]">Pre-init</p>
                                      {step.preInitSteps.map((cmd, ci) => (
                                        <pre key={`pre-${ci}`} className="mt-0.5 rounded bg-slate-900 px-2 py-1 text-[11px] font-mono text-slate-200 overflow-x-auto">{cmd}</pre>
                                      ))}
                                    </div>
                                  )}
                                  {step.postInitSteps && step.postInitSteps.length > 0 && (
                                    <div>
                                      <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--text-soft)]">Post-init</p>
                                      {step.postInitSteps.map((cmd, ci) => (
                                        <pre key={`post-${ci}`} className="mt-0.5 rounded bg-slate-900 px-2 py-1 text-[11px] font-mono text-slate-200 overflow-x-auto">{cmd}</pre>
                                      ))}
                                    </div>
                                  )}
                                </div>
                              )}
                            </div>
                          ))}
                          {revision.postWorkloadSteps && revision.postWorkloadSteps.length > 0 && (
                            <div className="mt-3 rounded-lg border-2 border-dashed border-[var(--surface-border)] bg-[var(--surface-subtle)] p-3">
                              <p className="text-[11px] font-semibold uppercase tracking-wide text-[var(--text-soft)] mb-2">Post-workload Steps</p>
                              {revision.postWorkloadSteps.map((cmd, ci) => (
                                <pre key={`pws-${ci}`} className="mt-1 rounded bg-slate-900 px-2 py-1 text-[11px] font-mono text-slate-200 overflow-x-auto">{cmd}</pre>
                              ))}
                            </div>
                          )}
                          {revision.preWorkloadSteps && revision.preWorkloadSteps.length > 0 && (
                            <div className="rounded-md border border-[var(--surface-border)] bg-[var(--surface-subtle)] p-2">
                              <p className="text-[10px] font-semibold uppercase tracking-wide text-[var(--text-soft)]">Pre-workload Steps</p>
                              {revision.preWorkloadSteps.map((cmd, ci) => (
                                <pre key={`prws-${ci}`} className="mt-1 rounded bg-slate-900 px-2 py-1 text-[11px] font-mono text-slate-200 overflow-x-auto">{cmd}</pre>
                              ))}
                            </div>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </section>
            </div>
          )}
        </ModalContent>
      </Modal>

      <Modal open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
        <ModalContent className="w-[min(92vw,28rem)]">
          <ModalHeader>
            <ModalTitle>Delete Workload</ModalTitle>
            <ModalDescription>
              Are you sure you want to delete "{workloadToDelete?.name}"? This will remove all revisions, runs, and node states associated with this workload. This action cannot be undone.
            </ModalDescription>
          </ModalHeader>
          <ModalFooter className="px-4 pb-4 pt-2 sm:flex-row sm:justify-end">
            <button
              type="button"
              onClick={() => {
                setIsDeleteModalOpen(false)
                setWorkloadToDelete(null)
              }}
              className="rounded-lg border border-[var(--surface-border)] px-4 py-2 text-sm text-[var(--text-soft)] hover:bg-[var(--surface-subtle)]"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={onDeleteWorkload}
              className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700"
            >
              Delete
            </button>
          </ModalFooter>
        </ModalContent>
      </Modal>


    </div>
  )
}
