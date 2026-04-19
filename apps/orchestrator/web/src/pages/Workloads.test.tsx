import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import Workloads from './Workloads'

describe('Workloads page', () => {
  beforeEach(async () => {
    render(<Workloads />)
    await screen.findByText('Workload Definitions')
  })

  it('renders workload list and latest revision column', async () => {
    expect(screen.getByText('Latest Revision')).toBeInTheDocument()
    expect(screen.getAllByText('Factory Base Install').length).toBeGreaterThan(0)
  })
})
