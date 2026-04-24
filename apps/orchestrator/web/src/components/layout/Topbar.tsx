import { User } from 'lucide-react'

interface TopbarProps {
  title: string
  description?: string
}

export default function Topbar({ title, description }: TopbarProps) {
  return (
    <header className="flex min-h-16 items-center justify-between border-b border-[var(--surface-border)] bg-[var(--surface-glass)] px-4 backdrop-blur lg:px-6">
      <div>
        <h2 className="text-lg font-semibold text-[var(--text-strong)]">{title}</h2>
        <p className="text-xs text-[var(--text-soft)]">{description ?? 'Phase 1 workload-first operations console'}</p>
      </div>
      <div className="flex items-center gap-2">
        <span className="rounded-full bg-[var(--surface-subtle)] px-3 py-1 text-xs font-medium text-[var(--text-soft)]">Admin</span>
        <div className="flex size-8 items-center justify-center rounded-full bg-[var(--surface-subtle)]">
          <User className="size-4 text-[var(--text-soft)]" />
        </div>
      </div>
    </header>
  )
}
