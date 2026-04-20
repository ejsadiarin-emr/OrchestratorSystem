import type { KeyboardEvent, ReactNode } from 'react'

export function RowTrigger({
  label,
  onActivate,
  className,
  children,
}: {
  label: string
  onActivate: () => void
  className?: string
  children: ReactNode
}) {
  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      event.stopPropagation()
      onActivate()
    }
  }

  return (
    <button
      type="button"
      onClick={event => {
        event.stopPropagation()
        onActivate()
      }}
      onKeyDown={onKeyDown}
      aria-label={label}
      className={className ?? 'text-left focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent)]'}
    >
      {children}
    </button>
  )
}
