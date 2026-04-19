import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Badge } from "@/components/ui/badge"

interface Execution {
  id: string
  agent: string
  status: "running" | "completed" | "failed"
  duration: string
  startTime: string
}

const dummyExecutions: Execution[] = [
  {
    id: "exec_01a2b3c4d5e6f",
    agent: "research-agent",
    status: "completed",
    duration: "2m 34s",
    startTime: "2026-04-20 01:05",
  },
  {
    id: "exec_07g8h9i0j1k2l",
    agent: "builder-agent",
    status: "failed",
    duration: "1m 12s",
    startTime: "2026-04-20 00:45",
  },
  {
    id: "exec_03m4n5o6p7q8r",
    agent: "review-agent",
    status: "running",
    duration: "45s",
    startTime: "2026-04-20 00:30",
  },
  {
    id: "exec_09s0t1u2v3w4x",
    agent: "test-agent",
    status: "completed",
    duration: "5m 02s",
    startTime: "2026-04-20 00:15",
  },
  {
    id: "exec_05y6z7a8b9c0d",
    agent: "deploy-agent",
    status: "completed",
    duration: "3m 21s",
    startTime: "2026-04-20 00:00",
  },
]

interface ExecutionsTableProps {
  onRowClick?: (execution: Execution) => void
}

export function ExecutionsTable({ onRowClick }: ExecutionsTableProps) {
  const handleRowClick = (execution: Execution) => {
    if (onRowClick) {
      onRowClick(execution)
    }
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Execution ID</TableHead>
          <TableHead>Agent</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Duration</TableHead>
          <TableHead>Start Time</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {dummyExecutions.map((execution) => (
          <TableRow
            key={execution.id}
            onClick={() => handleRowClick(execution)}
            className={onRowClick ? "cursor-pointer" : ""}
          >
            <TableCell className="font-mono text-xs">
              {execution.id}
            </TableCell>
            <TableCell>{execution.agent}</TableCell>
            <TableCell>
              <Badge
                variant={
                  execution.status === "completed"
                    ? "default"
                    : execution.status === "failed"
                    ? "destructive"
                    : "secondary"
                }
              >
                {execution.status}
              </Badge>
            </TableCell>
            <TableCell>{execution.duration}</TableCell>
            <TableCell>{execution.startTime}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}