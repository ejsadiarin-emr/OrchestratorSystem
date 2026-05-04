import { describe, expect, it } from 'vitest'
import type { OrchestratorHomeData } from '../../types'
import { buildWorkloadRows } from './workloadRows'

function makeHomeData(nodes: OrchestratorHomeData['nodes']): OrchestratorHomeData {
  return {
    kpis: {
      nodesOnline: 0,
      nodesOffline: 0,
      artifactsStored: 0,
      workloadDefinitions: 0,
      runningWorkloads: 0,
      activeRuns24h: 0,
      failedRuns24h: 0,
      pendingApprovals: 0,
      controlPlaneLatencyP95Ms: 0,
    },
    nodes,
    events: [],
    selectedNodeId: '',
    logsByNodeId: {},
    workloads: [],
  }
}

describe('buildWorkloadRows', () => {
  it('returns empty list for null data', () => {
    expect(buildWorkloadRows(null)).toEqual([])
  })

  it('aggregates per-workload counts with mixed revisions and package signal fallback', () => {
    const data = makeHomeData([
      {
        nodeId: 'node-a',
        hostname: 'a',
        displayName: 'Node A',
        health: 'online',
        assignedWorkload: 'Factory Base Install',
        workloadRevision: '1.0.0',
        runState: 'update',
        lastCheckInAge: '10s',
        riskLevel: 'low',
        revisionUpdateAvailable: true,
        packageUpdatesAvailable: true,
      },
      {
        nodeId: 'node-b',
        hostname: 'b',
        displayName: 'Node B',
        health: 'warning',
        assignedWorkload: 'Factory Base Install',
        workloadRevision: '1.1.0',
        runState: 'pending-approval',
        lastCheckInAge: '15s',
        riskLevel: 'med',
        revisionUpdateAvailable: false,
        packageUpdatesAvailable: true,
        packageUpdateCount: 0,
      },
      {
        nodeId: 'node-c',
        hostname: 'c',
        displayName: 'Node C',
        health: 'online',
        assignedWorkload: 'Observer Stack',
        workloadRevision: '0.9.0',
        runState: 'idle',
        lastCheckInAge: '12s',
        riskLevel: 'low',
        revisionUpdateAvailable: true,
        packageUpdatesAvailable: true,
      },
    ])

    const rows = buildWorkloadRows(data)
    const factory = rows.find(row => row.name === 'Factory Base Install')
    const observer = rows.find(row => row.name === 'Observer Stack')

    expect(factory).toBeTruthy()
    expect(factory).toMatchObject({
      nodesAssigned: 2,
      runningNodes: 2,
      nodesWithRevisionUpdates: 1,
      nodesWithPackageUpdates: 2,
      packageUpdateSignals: 1,
      mixedRevisions: true,
    })

    expect(observer).toBeTruthy()
    expect(observer).toMatchObject({
      nodesAssigned: 1,
      runningNodes: 0,
      nodesWithRevisionUpdates: 1,
      nodesWithPackageUpdates: 1,
      packageUpdateSignals: 1,
      mixedRevisions: false,
      revisionsLabel: '0.9.0',
    })
  })

  it('sorts by package signals desc, then nodes assigned desc, then name', () => {
    const data = makeHomeData([
      {
        nodeId: 'node-1',
        hostname: 'n1',
        displayName: 'Node 1',
        health: 'online',
        assignedWorkload: 'Zulu',
        workloadRevision: '1',
        runState: 'idle',
        lastCheckInAge: '1s',
        riskLevel: 'low',
        revisionUpdateAvailable: false,
        packageUpdatesAvailable: true,
        packageUpdateCount: 2,
      },
      {
        nodeId: 'node-2',
        hostname: 'n2',
        displayName: 'Node 2',
        health: 'online',
        assignedWorkload: 'Bravo',
        workloadRevision: '1',
        runState: 'idle',
        lastCheckInAge: '1s',
        riskLevel: 'low',
        revisionUpdateAvailable: false,
        packageUpdatesAvailable: true,
        packageUpdateCount: 3,
      },
      {
        nodeId: 'node-3',
        hostname: 'n3',
        displayName: 'Node 3',
        health: 'online',
        assignedWorkload: 'Alpha',
        workloadRevision: '1',
        runState: 'idle',
        lastCheckInAge: '1s',
        riskLevel: 'low',
        revisionUpdateAvailable: false,
        packageUpdatesAvailable: false,
      },
    ])

    const rows = buildWorkloadRows(data)
    expect(rows.map(row => row.name)).toEqual(['Bravo', 'Zulu', 'Alpha'])
  })
})
