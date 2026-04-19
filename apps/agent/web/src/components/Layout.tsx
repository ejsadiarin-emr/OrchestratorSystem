import { Link, useLocation } from 'react-router-dom'
import { layoutAppTitle, layoutNavItems } from '../../../../../shared/web-common/layoutShell'

export default function Layout({ children }: { children: React.ReactNode }) {
  const location = useLocation()

  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] text-[var(--text-strong)]">
      <nav className="border-b border-[var(--surface-border)] bg-[var(--surface-glass)] backdrop-blur">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex min-h-16 flex-col gap-3 py-3 lg:h-20 lg:flex-row lg:items-center lg:justify-between lg:py-0">
            <div className="flex items-center">
              <div>
                <span className="text-xl font-semibold tracking-tight">{layoutAppTitle}</span>
                <p className="text-xs text-[var(--text-soft)]">PoC Phase 1 workload-first agent local shell</p>
              </div>
            </div>
            <div className="flex flex-wrap gap-1">
              {layoutNavItems.map((item) => (
                <Link
                  key={item.path}
                  to={item.path}
                  className={`inline-flex items-center rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                    location.pathname === item.path
                      ? 'bg-[var(--accent)] text-white shadow-sm'
                      : 'text-[var(--text-soft)] hover:bg-slate-100'
                  }`}
                >
                  {item.label}
                </Link>
              ))}
            </div>
          </div>
        </div>
      </nav>
      <main className="max-w-7xl mx-auto px-4 py-8 sm:px-6 lg:px-8">
        {children}
      </main>
    </div>
  )
}
