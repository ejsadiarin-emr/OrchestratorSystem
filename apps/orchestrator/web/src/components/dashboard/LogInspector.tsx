import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '../ui/sheet'
import type { MiniLogLine } from '../../types'

function getLevelColor(level?: string) {
  switch (level?.toLowerCase()) {
    case 'error':
      return 'text-red-600'
    case 'warn':
      return 'text-amber-600'
    case 'info':
      return 'text-blue-600'
    case 'debug':
      return 'text-slate-500'
    default:
      return 'text-slate-700'
  }
}

interface LogInspectorProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  nodeId: string
  logs: MiniLogLine[]
}

export function LogInspector({ open, onOpenChange, nodeId, logs }: LogInspectorProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="min-w-[600px] sm:max-w-[720px] bg-[var(--surface)] border-[var(--surface-border)]">
        <SheetHeader>
          <SheetTitle className="text-[var(--text-strong)]">Log Inspector</SheetTitle>
          <SheetDescription className="text-[var(--text-soft)]">
            Full log output for node: <span className="font-mono text-[var(--text-strong)]">{nodeId}</span>
          </SheetDescription>
        </SheetHeader>

        <div className="mt-6 h-[calc(100vh-180px)] overflow-y-auto rounded-lg border border-[var(--surface-border)] bg-slate-950 p-4 font-mono text-xs">
          {logs.length === 0 ? (
            <p className="text-slate-400">No logs available for this node.</p>
          ) : (
            <div className="space-y-1">
              {logs.map((log, idx) => (
                <div key={log.id || idx} className="flex gap-3 hover:bg-slate-900/50">
                  <span className="shrink-0 select-none text-slate-500">{log.at}</span>
                  <span className={`shrink-0 select-none ${getLevelColor(log.level)}`}>
                    [{log.level?.toUpperCase() || 'LOG'}]
                  </span>
                  <span className="text-slate-200 whitespace-pre-wrap break-all">{log.message}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}
