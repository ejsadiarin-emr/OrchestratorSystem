import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import Jobs from './Jobs'

describe('Jobs page delivery protocol rendering', () => {
  beforeEach(async () => {
    render(<Jobs />)
    await screen.findByText('Jobs & Delivery Flow')
  })

  it('shows delivery stage labels and allows opening details modal', async () => {
    expect(screen.getByText('Delivery Stage')).toBeInTheDocument()
    expect(screen.getAllByText(/Ranged GET loop|HEAD request|AssignJob|Digest\/signature verify/).length).toBeGreaterThan(0)

    const rowCell = screen.getAllByRole('cell').find(cell => cell.textContent?.includes('EJ Installer'))
    expect(rowCell).toBeDefined()

    if (rowCell) {
      fireEvent.click(rowCell)
    }

    await waitFor(() => {
      expect(screen.getByText(/delivery details/)).toBeInTheDocument()
      expect(screen.getByText('Protocol events')).toBeInTheDocument()
    })
  })

  it('can reach failed terminal state during validation for test channel artifact', async () => {
    fireEvent.change(screen.getByLabelText('Artifact'), { target: { value: 'artifact-001' } })
    fireEvent.change(screen.getByLabelText('Target node'), { target: { value: 'node-001' } })
    fireEvent.click(screen.getByText('Create Job'))

    await waitFor(() => {
      const table = screen.getByRole('table')
      expect(within(table).getByText('AssignJob')).toBeInTheDocument()
    })

    await waitFor(
      () => {
        const table = screen.getByRole('table')
        expect(within(table).getAllByText('failed').length).toBeGreaterThan(0)
      },
      { timeout: 10000 },
    )
  })
})
