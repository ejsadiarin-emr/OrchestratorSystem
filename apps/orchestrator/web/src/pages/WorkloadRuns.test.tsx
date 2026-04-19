import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import WorkloadRuns from './WorkloadRuns'

describe('Workload Runs page', () => {
  beforeEach(async () => {
    render(<WorkloadRuns />)
    await screen.findByText('Workload Runs')
  })

  it('renders run creation lifecycle mode options', () => {
    expect(screen.getByText('Create Workload Run')).toBeInTheDocument()
    expect(screen.getAllByText('install').length).toBeGreaterThan(0)
    expect(screen.getAllByText('update').length).toBeGreaterThan(0)
    expect(screen.getAllByText('rollback').length).toBeGreaterThan(0)
    expect(screen.getAllByText('cancel').length).toBeGreaterThan(0)
  })

  it('describes workload-runs runtime contracts', () => {
    expect(screen.getAllByText('/api/workload-runs', { exact: false }).length).toBeGreaterThan(0)
  })
})
