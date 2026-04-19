import { User } from 'lucide-react'

interface TopbarProps {
  title: string
}

export default function Topbar({ title }: TopbarProps) {
  return (
    <header className="h-14 border-b border-border bg-background flex items-center justify-between px-4">
      <h2 className="text-lg font-medium">{title}</h2>
      <div className="flex items-center gap-2">
        <span className="text-sm text-muted-foreground">Admin</span>
        <div className="size-8 rounded-full bg-muted flex items-center justify-center">
          <User className="size-4 text-muted-foreground" />
        </div>
      </div>
    </header>
  )
}