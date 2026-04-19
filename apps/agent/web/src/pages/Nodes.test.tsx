import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import Nodes from './Nodes'

describe('Nodes page bootstrap flow', () => {
  beforeEach(async () => {
    render(<Nodes />)
    await screen.findByText('Agent Bootstrap & Enrollment')
  })

  it('issues token and then simulates first connect with auto metadata', async () => {
    fireEvent.click(screen.getByText('Issue Token (POST)'))

    await waitFor(() => {
      expect(screen.getByText(/Issued enroll-/)).toBeInTheDocument()
    })

    fireEvent.click(screen.getByText('Simulate First Connect'))

    await waitFor(() => {
      expect(screen.getByText(/auto-collected on first connect/)).toBeInTheDocument()
    })

    expect(screen.getByText('Registered Nodes')).toBeInTheDocument()
  })
})
