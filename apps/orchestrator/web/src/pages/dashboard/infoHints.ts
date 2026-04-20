export const INFO_HINTS = {
  riskNode: 'Risk (Node) is derived from node health, heartbeat age, and workload update drift signals.',
  reason: 'Reason is the primary operator-facing reason code for elevated risk or blocked progress.',
  revisionUpdates: 'Revision Updates count nodes where a newer workload revision appears available for that workload context.',
  packageUpdateSignals:
    'Package Update Signals are aggregate telemetry indicators from nodes and may not represent artifact-store authoritative diffs.',
  nodesRunning: 'Nodes Running counts nodes currently in non-terminal run states for the workload.',
  pendingApprovals: 'Pending Approvals counts gated actions requiring explicit operator approval before execution can proceed.',
} as const

export type InfoHintKey = keyof typeof INFO_HINTS
