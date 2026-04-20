import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import WorkloadRuns from './WorkloadRuns'

describe('Workload Runs page', () => {
  beforeEach(async () => {
    render(<WorkloadRuns />)
    await screen.findByText('Workload Runs')
  })

  it('opens run creator popup and creates a run', async () => {
    expect(screen.getByRole('button', { name: 'Open Run Creator' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Open Run Creator' }))
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByText('Create Workload Run')).toBeInTheDocument()
    expect(within(dialog).getAllByText('install').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('update').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('rollback').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('cancel').length).toBeGreaterThan(0)

    fireEvent.click(within(dialog).getByRole('button', { name: 'Create Run' }))

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    expect(screen.getByText('run-003')).toBeInTheDocument()
  })

  it('opens diagnostics popup from run row', async () => {
    const row = screen.getByText('run-001').closest('tr')
    expect(row).not.toBeNull()
    fireEvent.click(row as HTMLTableRowElement)
    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByText('Run diagnostics')).toBeInTheDocument()
    expect(within(dialog).getByText('Timeline stream')).toBeInTheDocument()
    expect(within(dialog).getByText('workload run timeline')).toBeInTheDocument()
  })

  it('describes workload-runs runtime contracts', () => {
    expect(screen.getAllByText('/api/workload-runs', { exact: false }).length).toBeGreaterThan(0)
  })
})
