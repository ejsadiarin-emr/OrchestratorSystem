import { MemoryRouter } from 'react-router-dom'
import { ReactNode } from 'react'

export function TestRouterWrapper({ children }: { children: ReactNode }) {
  return (
    <MemoryRouter>
      {children}
    </MemoryRouter>
  )
}
