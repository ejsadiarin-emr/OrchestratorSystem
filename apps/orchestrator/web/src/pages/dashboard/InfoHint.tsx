import { useState } from 'react'
import { INFO_HINTS, type InfoHintKey } from './infoHints'

export function InfoHint({ label, hintKey }: { label: string; hintKey: InfoHintKey }) {
  const [open, setOpen] = useState(false)

  return (
    <span
      className="relative inline-flex items-center"
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
    >
      <button
        type="button"
        aria-label={`Info: ${label}`}
        className="inline-flex h-4 w-4 items-center justify-center rounded-full border border-[var(--surface-border)] bg-[var(--surface-subtle)] text-[10px] font-semibold normal-case text-[var(--text-soft)] hover:bg-[var(--surface-muted)]"
      >
        i
      </button>
      {open && (
        <span
          role="tooltip"
          className="absolute left-0 top-5 z-10 max-w-56 rounded-md border border-[var(--surface-border)] bg-[var(--surface)] px-2 py-1 text-[10px] normal-case tracking-normal text-[var(--text-soft)] shadow-[var(--surface-shadow)]"
        >
          {INFO_HINTS[hintKey]}
        </span>
      )}
    </span>
  )
}
