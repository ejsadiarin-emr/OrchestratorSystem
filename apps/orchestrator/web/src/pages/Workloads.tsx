import { useEffect, useMemo, useState } from 'react'
import {
  createWorkloadDefinitionDraft,
  createWorkloadRevision,
  listArtifacts,
  listWorkloadRevisions,
  listWorkloads,
  publishWorkloadRevision,
} from '../services/api'
import type { ArtifactRecord, WorkloadDefinition, WorkloadRevision } from '../types'

interface DraftForm {
  name: string
  description: string
}

interface RevisionForm {
  workloadId: string
  revision: string
  packageIds: string[]
}

export default function Workloads() {
  const [workloads, setWorkloads] = useState<WorkloadDefinition[]>([])
  const [artifacts, setArtifacts] = useState<ArtifactRecord[]>([])
  const [revisions, setRevisions] = useState<WorkloadRevision[]>([])
  const [selectedWorkloadId, setSelectedWorkloadId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [draftForm, setDraftForm] = useState<DraftForm>({
    name: '',
    description: '',
  })
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
    refresh()
      .catch(() => setError('Failed to load workloads and revisions.'))
      .finally(() => setLoading(false))
  }, [])

  const selectedPackages = useMemo(() => {
    return artifacts.filter(artifact => revisionForm.packageIds.includes(artifact.id))
  }, [artifacts, revisionForm.packageIds])

  const canCreateRevision = revisionForm.packageIds.length >= 2 && revisionForm.packageIds.length <= 3

  const onCreateDraft = async (event: React.FormEvent) => {
    event.preventDefault()
    setError(null)
    setMessage(null)

    try {
      const created = await createWorkloadDefinitionDraft(draftForm)
      setDraftForm({ name: '', description: '' })
      await refresh(created.id)
      setMessage(`Created WorkloadDefinition draft: ${created.name}`)
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Failed to create workload draft.')
    }
  }

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
          packageName: artifact.manifest.name,
          packageVersion: artifact.manifest.version,
          packageIndex: index + 1,
          stepId: 'install-or-upgrade',
        })),
      })
      setRevisionForm(current => ({ ...current, packageIds: [] }))
      await refresh(revisionForm.workloadId)
      setMessage(`Created WorkloadRevision draft ${created.revision}`)
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

  if (loading) {
    return <div className="text-center py-8">Loading...</div>
  }

  return (
    <div className="space-y-6">
      <header className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-strong)]">Workload Definitions</h1>
        <p className="mt-2 text-sm text-[var(--text-soft)]">
          Define WorkloadDefinition entities, then create immutable WorkloadRevision records with 2-3 ordered package steps.
        </p>
      </header>

      {error && <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}
      {message && <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">{message}</div>}

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Create Draft WorkloadDefinition</h2>
          <form onSubmit={onCreateDraft} className="mt-4 space-y-3">
            <label className="block text-sm text-[var(--text-soft)]">
              Name
              <input
                value={draftForm.name}
                onChange={event => setDraftForm(current => ({ ...current, name: event.target.value }))}
                className="mt-1 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
                placeholder="Line-A Baseline"
                required
              />
            </label>
            <label className="block text-sm text-[var(--text-soft)]">
              Description
              <textarea
                value={draftForm.description}
                onChange={event => setDraftForm(current => ({ ...current, description: event.target.value }))}
                className="mt-1 h-24 w-full rounded-lg border border-[var(--surface-border)] px-3 py-2"
                placeholder="Deterministic starter workload for plant nodes"
                required
              />
            </label>
            <button type="submit" className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)]">
              Create Draft
            </button>
          </form>
        </section>

        <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
          <h2 className="text-lg font-semibold text-[var(--text-strong)]">Create / Publish WorkloadRevision</h2>
          <p className="mt-1 text-xs text-[var(--text-soft)]">PoC rule: each revision must include exactly 2-3 package steps.</p>
          <form onSubmit={onCreateRevision} className="mt-4 space-y-3">
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
                    {artifact.manifest.name} {artifact.manifest.version}
                  </option>
                ))}
              </select>
            </label>
            <button
              type="submit"
              disabled={!canCreateRevision}
              className="rounded-lg bg-[var(--accent)] px-4 py-2 text-sm font-medium text-white hover:bg-[var(--accent-strong)] disabled:cursor-not-allowed disabled:opacity-50"
            >
              Create Revision Draft
            </button>
          </form>
        </section>
      </div>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h2 className="text-lg font-semibold text-[var(--text-strong)]">Definitions and Latest Revision</h2>
        <div className="mt-4 overflow-x-auto">
          <table className="min-w-full divide-y divide-[var(--surface-border)]">
            <thead>
              <tr className="text-left text-xs uppercase tracking-wide text-[var(--text-soft)]">
                <th className="px-4 py-3">Workload</th>
                <th className="px-4 py-3">Description</th>
                <th className="px-4 py-3">Latest Revision</th>
                <th className="px-4 py-3">Revision Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--surface-border)] text-sm">
              {workloads.map(item => (
                <tr key={item.id}>
                  <td className="px-4 py-3 font-medium text-[var(--text-strong)]">{item.name}</td>
                  <td className="px-4 py-3 text-[var(--text-soft)]">{item.description}</td>
                  <td className="px-4 py-3 text-[var(--text-soft)]">{item.latestRevision?.revision ?? 'No revision yet'}</td>
                  <td className="px-4 py-3">
                    <span className="rounded-full bg-slate-100 px-2 py-1 text-xs text-slate-700">
                      {item.latestRevision?.state ?? 'draft'}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="rounded-2xl border border-[var(--surface-border)] bg-[var(--surface)] p-6 shadow-[var(--surface-shadow)]">
        <h2 className="text-lg font-semibold text-[var(--text-strong)]">Revision List</h2>
        <p className="mt-1 text-xs text-[var(--text-soft)]">Publish action transitions draft revisions to immutable published state.</p>
        <div className="mt-4 space-y-3">
          {revisions.length === 0 ? (
            <p className="text-sm text-[var(--text-soft)]">No revisions for selected workload.</p>
          ) : (
            revisions.map(revision => (
              <div key={revision.id} className="rounded-xl border border-[var(--surface-border)] p-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-semibold text-[var(--text-strong)]">Revision {revision.revision}</p>
                    <p className="text-xs text-[var(--text-soft)]">{revision.packageSteps.length} package steps</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="rounded-full bg-slate-100 px-2 py-1 text-xs text-slate-700">{revision.state}</span>
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
