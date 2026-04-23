import { cn } from '@/lib/utils'

interface ProgressProps {
  value: number
  className?: string
}

export function Progress({ value, className }: ProgressProps) {
  const clamped = Math.max(0, Math.min(100, value))

  return (
    <div className={cn('w-full bg-gray-200 rounded-full h-2 overflow-hidden', className)}>
      <div
        className="bg-blue-600 rounded-full h-2 transition-all duration-300 ease-out"
        style={{ width: `${clamped}%` }}
      />
    </div>
  )
}