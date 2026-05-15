import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import type { ArtifactManifest, InstallAdapterInput, DetectionInput, PolicyTagsInput, Node, NodeWorkloadState, WorkloadRun, WorkloadDefinition, ArtifactRecord } from '../types'
import {
  issueEnrollmentToken,
  suggestManifestFromFile,
  uploadArtifact,
  validateManifestChannel,
  getOrchestratorHomeData,
  transformOrchestratorHomeData,
} from './api'

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn())
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('api channel validation', () => {
  it('accepts stable/canary/test and rejects others', () => {
    expect(validateManifestChannel('stable')).toBe(true)
    expect(validateManifestChannel('canary')).toBe(true)
    expect(validateManifestChannel('test')).toBe(true)
    expect(validateManifestChannel('beta')).toBe(false)
  })

  it('rejects upload when manifest has invalid channel', async () => {
    const manifest = suggestManifestFromFile('Widget-2.4.1.msi', 2048)
    await expect(
      uploadArtifact({
        file: new File(['x'], 'Widget-2.4.1.msi'),
        manifest: { ...manifest, channel: 'beta' as never },
      }),
    ).rejects.toThrow('manifest.channel must be one of stable, canary, test')
  })

  it('rejects upload when manifest JSON part is missing', async () => {
    await expect(
      uploadArtifact({
        file: new File(['x'], 'Widget-2.4.1.msi'),
        manifest: undefined as never,
      }),
    ).rejects.toThrow('manifest JSON part is required for multipart upload')
  })

  it('rejects upload when file is missing', async () => {
    await expect(
      uploadArtifact({
        file: undefined as never,
        manifest: { packageId: 'test', version: '1.0.0', channel: 'stable' },
      }),
    ).rejects.toThrow('file is required for multipart upload')
  })
})

describe('ArtifactManifest matches backend ArtifactIngestManifest contract', () => {
  it('has all required top-level fields from backend contract', () => {
    const manifest: ArtifactManifest = {
      packageId: 'EJ-Installer',
      version: '1.12.0',
      channel: 'stable',
      artifactType: 'msi',
      verificationResult: 'verified',
      installAdapter: {
        type: 'msi',
        command: 'msiexec',
        arguments: '/quiet /norestart',
        expectedExitCodes: [0],
        timeoutSeconds: 300,
      },
      detection: {
        type: 'registry',
        path: 'HKLM\\Software\\EJ',
      },
      policyTags: {
        retryabilityClass: 'retryable',
        idempotencyMode: 'enforced',
        riskLevel: 'low',
        approvalRequired: false,
      },
    }

    expect(manifest).toHaveProperty('packageId')
    expect(manifest).toHaveProperty('version')
    expect(manifest).toHaveProperty('channel')
    expect(manifest).toHaveProperty('artifactType')
    expect(manifest).toHaveProperty('verificationResult')
    expect(manifest).toHaveProperty('installAdapter')
    expect(manifest).toHaveProperty('detection')
    expect(manifest).toHaveProperty('policyTags')
  })

  it('provides InstallAdapterInput sub-fields matching backend InstallAdapterInput', () => {
    const adapter: InstallAdapterInput = {
      type: 'msi',
      command: 'msiexec',
      arguments: '/quiet /norestart',
      expectedExitCodes: [0, 3010],
      timeoutSeconds: 600,
    }

    expect(adapter).toHaveProperty('type')
    expect(adapter).toHaveProperty('command')
    expect(adapter).toHaveProperty('arguments')
    expect(adapter).toHaveProperty('expectedExitCodes')
    expect(adapter).toHaveProperty('timeoutSeconds')
    expect(Array.isArray(adapter.expectedExitCodes)).toBe(true)
    expect(typeof adapter.timeoutSeconds).toBe('number')
  })

  it('provides DetectionInput sub-fields matching backend DetectionInput', () => {
    const detection: DetectionInput = {
      type: 'registry',
      path: 'HKLM\\Software\\EJ',
    }

    expect(detection).toHaveProperty('type')
    expect(detection).toHaveProperty('path')
  })

  it('provides PolicyTagsInput sub-fields matching backend PolicyTagsInput', () => {
    const policyTags: PolicyTagsInput = {
      retryabilityClass: 'retryable',
      idempotencyMode: 'enforced',
      riskLevel: 'low',
      approvalRequired: true,
    }

    expect(policyTags).toHaveProperty('retryabilityClass')
    expect(policyTags).toHaveProperty('idempotencyMode')
    expect(policyTags).toHaveProperty('riskLevel')
    expect(policyTags).toHaveProperty('approvalRequired')
    expect(typeof policyTags.approvalRequired).toBe('boolean')
  })
})

describe('suggestManifestFromFile produces backend-conformant shape', () => {
  it('returns ArtifactManifest with packageId, version, channel, and installAdapter', () => {
    const manifest = suggestManifestFromFile('EJ-Installer-2.4.1.msi', 2048)

    expect(manifest.packageId).toBe('EJ-Installer')
    expect(manifest.version).toBe('2.4.1')
    expect(manifest.channel).toBe('stable')
    expect(manifest.artifactType).toBe('msi')
    expect(manifest.installAdapter).toBeDefined()
    expect(manifest.installAdapter!.type).toBe('msi')
    expect(manifest.installAdapter!.command).toBe('msiexec')
    expect(manifest.detection).toBeDefined()
    expect(manifest.detection!.type).toBe('registry')
    expect(manifest.policyTags).toBeDefined()
  })

  it('infers msi artifact type from .msi extension', () => {
    const manifest = suggestManifestFromFile('Package-1.0.0.msi', 1024)
    expect(manifest.artifactType).toBe('msi')
    expect(manifest.installAdapter!.type).toBe('msi')
  })

  it('infers exe artifact type from .exe extension', () => {
    const manifest = suggestManifestFromFile('Setup-3.2.1.exe', 2048)
    expect(manifest.artifactType).toBe('exe')
    expect(manifest.installAdapter!.type).toBe('exe')
  })

  it('infers zip artifact type from .zip extension', () => {
    const manifest = suggestManifestFromFile('Bundle-0.5.0.zip', 4096)
    expect(manifest.artifactType).toBe('zip')
  })
})

describe('uploadArtifact sends real HTTP request with FormData', () => {
  it('POSTs to /api/artifacts with file and manifest in FormData', async () => {
    const mockResponse = {
      resolvedManifest: {
        packageId: 'Widget',
        version: '2.4.1',
        channel: 'stable',
        artifactType: 'msi',
        installAdapter: { type: 'msi', command: 'msiexec', arguments: '/quiet', expectedExitCodes: [0], timeoutSeconds: 300 },
        detection: { type: 'registry', path: 'HKLM\\Widget' },
        policyTags: { retryabilityClass: 'retryable', idempotencyMode: 'enforced', riskLevel: 'low', approvalRequired: false },
        originMetadata: { source: 'test', publisher: 'test', ingestedBy: 'anonymous', ingestedAtUtc: '2026-01-01T00:00:00Z', verificationResult: 'derived' },
      },
    }
    ;(globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce(
      new Response(JSON.stringify(mockResponse), { status: 201, headers: { 'Content-Type': 'application/json' } }),
    )

    const file = new File(['fake content'], 'Widget-2.4.1.msi', { type: 'application/octet-stream' })
    const manifest: ArtifactManifest = {
      packageId: 'Widget',
      version: '2.4.1',
      channel: 'stable',
      artifactType: 'msi',
    }

    const result = await uploadArtifact({ file, manifest })

    expect(globalThis.fetch).toHaveBeenCalledTimes(1)
    const [url, options] = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0]
    expect(url).toBe('/api/artifacts')
    expect(options.method).toBe('POST')

    const sentBody = options.body as FormData
    expect(sentBody.get('file')).toBe(file)
    expect(sentBody.get('manifest')).toBe(JSON.stringify(manifest))

    expect(result.artifact).toBeDefined()
    expect(result.artifact.fileName).toBe('Widget-2.4.1.msi')
    expect(result.steps).toHaveLength(4)
  })

  it('throws with validation error details on 400 response', async () => {
    const errorBody = {
      code: 'validation_failed',
      message: 'Validation failed',
      errors: [
        { field: 'manifest.packageId', error: 'packageId is required' },
        { field: 'manifest.version', error: 'version is required' },
      ],
    }
    ;(globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce(
      new Response(JSON.stringify(errorBody), { status: 400, headers: { 'Content-Type': 'application/json' } }),
    )

    const file = new File(['x'], 'test.msi')
    await expect(
      uploadArtifact({ file, manifest: { packageId: 'Widget', version: '1.0.0', channel: 'stable' } }),
    ).rejects.toThrow('manifest.packageId: packageId is required; manifest.version: version is required')
  })
})

describe('api enrollment semantics', () => {
  it('issues single-use token with requested URL via POST-style function', async () => {
    ;(globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          tokenId: 'token-new',
          token: 'enroll-abc123',
          issuedAtUtc: new Date().toISOString(),
          expiresAtUtc: new Date(Date.now() + 30 * 60 * 1000).toISOString(),
          requestedBy: 'qa.user',
          orchestratorUrl: 'https://orch.example.local:5000',
          singleUse: true,
          used: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )

    const token = await issueEnrollmentToken({
      requestedBy: 'qa.user',
      orchestratorUrl: 'https://orch.example.local:5000',
      ttlMinutes: 30,
    })

    expect(token.singleUse).toBe(true)
    expect(token.used).toBe(false)
    expect(token.orchestratorUrl).toBe('https://orch.example.local:5000')
  })
})

describe('getOrchestratorHomeData', () => {
  it('composes home data from real API endpoints', async () => {
    const nodesResponse = [
      {
        id: 'node-001',
        hostname: 'wj-plant-01',
        ipAddress: '10.30.2.41',
        status: 'online',
        description: 'Plant line A host',
        osVersion: 'Windows Server 2022',
        agentVersion: '0.1.0',
        firstConnectedAt: new Date().toISOString(),
        lastSeenAt: new Date().toISOString(),
      },
      {
        id: 'node-002',
        hostname: 'wj-plant-02',
        ipAddress: '10.30.2.42',
        status: 'offline',
        description: 'Plant line B host',
        osVersion: 'Windows Server 2022',
        agentVersion: '0.1.0',
        firstConnectedAt: new Date().toISOString(),
        lastSeenAt: new Date().toISOString(),
      },
    ]

    const workloadStatesResponse = [
      {
        nodeId: 'node-001',
        workloadId: 'workload-001',
        workloadRevision: '1.1.0',
        runId: 'run-001',
        status: 'running',
        updatedAt: new Date().toISOString(),
      },
    ]

    const runsResponse = [
      {
        runId: 'run-001',
        workloadId: 'workload-001',
        revisionId: 'rev-001',
        workloadVersion: '1.1.0',
        mode: 'install',
        state: 'Running',
        createdAtUtc: new Date().toISOString(),
        updatedAtUtc: new Date().toISOString(),
        completedAtUtc: null,
        riskLevel: 'low',
        nodeIds: ['node-001'],
      },
    ]

    const artifactsResponse = [
      { id: 'artifact-001', packageId: 'pkg-001', version: '1.0.0', fileName: 'pkg.msi', createdAt: new Date().toISOString() },
    ]

    const workloadsResponse = {
      workloads: [
        { workloadId: 'workload-001', name: 'Factory Base Install', description: 'Baseline', publishedRevisionId: 'rev-001', createdAtUtc: new Date().toISOString(), updatedAtUtc: new Date().toISOString() },
      ],
    }

    const mockFetch = globalThis.fetch as ReturnType<typeof vi.fn>
    mockFetch.mockImplementation(async (url: string) => {
      if (url === '/api/nodes') {
        return new Response(JSON.stringify(nodesResponse), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (url === '/api/nodes/workload-states') {
        return new Response(JSON.stringify(workloadStatesResponse), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (url === '/api/workload-runs') {
        return new Response(JSON.stringify(runsResponse), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (url === '/api/artifacts') {
        return new Response(JSON.stringify(artifactsResponse), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (url === '/api/workloads') {
        return new Response(JSON.stringify(workloadsResponse), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response(null, { status: 404 })
    })

    const data = await getOrchestratorHomeData()

    expect(mockFetch).toHaveBeenCalledWith('/api/nodes')
    expect(mockFetch).toHaveBeenCalledWith('/api/workload-runs')
    expect(mockFetch).toHaveBeenCalledWith('/api/artifacts')
    expect(mockFetch).toHaveBeenCalledWith('/api/workloads')
    expect(mockFetch).toHaveBeenCalledWith('/api/nodes/workload-states')

    expect(data.kpis.nodesOnline).toBe(1)
    expect(data.kpis.nodesOffline).toBe(1)
    expect(data.kpis.workloadDefinitions).toBe(1)
    expect(data.kpis.runningWorkloads).toBe(1)
    expect(data.kpis.artifactsStored).toBe(1)
    expect(data.nodes.length).toBe(2)
    expect(data.nodes[0].nodeId).toBe('node-001')
    expect(data.nodes[0].health).toBe('online')
    expect(data.nodes[0].assignedWorkload).toBe('Factory Base Install')
    expect(data.nodes[1].health).toBe('offline')
    expect(data.selectedNodeId).toBe('node-001')
  })
})

function makeNode(overrides: Partial<Node> = {}): Node {
  return {
    id: 'n1',
    hostname: 'host-1',
    displayName: 'Node One',
    ipAddress: '10.0.0.1',
    status: 'online',
    description: '',
    osVersion: '',
    agentVersion: '',
    lastSeenAt: new Date().toISOString(),
    ...overrides,
  }
}

function makeWorkloadState(overrides: Partial<NodeWorkloadState> = {}): NodeWorkloadState {
  return {
    nodeId: 'n1',
    workloadId: 'w1',
    workloadRevision: '1.0.0',
    runId: 'run-1',
    status: 'running',
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

function makeWorkloadRun(overrides: Partial<WorkloadRun> = {}): WorkloadRun {
  return {
    id: 'run-1',
    workloadId: 'w1',
    workloadName: 'MyWorkload',
    workloadRevision: '1.0.0',
    mode: 'install',
    targetNodeIds: ['n1'],
    targetNodeHostnames: ['host-1'],
    status: 'running',
    createdAt: new Date().toISOString(),
    timeline: [],
    ...overrides,
  }
}

function makeWorkloadDefinition(overrides: Partial<WorkloadDefinition> = {}): WorkloadDefinition {
  return {
    id: 'w1',
    name: 'MyWorkload',
    description: '',
    createdAt: new Date().toISOString(),
    ...overrides,
  }
}

describe('transformOrchestratorHomeData', () => {
  it('handles empty arrays gracefully', () => {
    const result = transformOrchestratorHomeData([], [], [], [], [])

    expect(result.kpis.nodesOnline).toBe(0)
    expect(result.kpis.nodesOffline).toBe(0)
    expect(result.kpis.workloadDefinitions).toBe(0)
    expect(result.kpis.runningWorkloads).toBe(0)
    expect(result.kpis.artifactsStored).toBe(0)
    expect(result.kpis.activeRuns24h).toBe(0)
    expect(result.kpis.failedRuns24h).toBe(0)
    expect(result.kpis.pendingApprovals).toBe(0)
    expect(result.kpis.controlPlaneLatencyP95Ms).toBe(0)
    expect(result.nodes).toEqual([])
    expect(result.selectedNodeId).toBe('')
    expect(result.workloads).toEqual([])
    expect(result.events).toHaveLength(1)
    expect(result.events[0].id).toBe('evt-healthy')
    expect(result.events[0].severity).toBe('info')
  })

  it('computes KPI metrics from raw API data', () => {
    const nodes: Node[] = [
      makeNode({ id: 'n1', status: 'online', hostname: 'online-node' }),
      makeNode({ id: 'n2', status: 'online', hostname: 'online-node-2' }),
      makeNode({ id: 'n3', status: 'offline', hostname: 'offline-node' }),
    ]
    const workloads: WorkloadDefinition[] = [
      makeWorkloadDefinition({ id: 'w1', name: 'Workload A' }),
      makeWorkloadDefinition({ id: 'w2', name: 'Workload B' }),
    ]
    const runs: WorkloadRun[] = [
      makeWorkloadRun({ id: 'run-1', status: 'running' }),
      makeWorkloadRun({ id: 'run-2', status: 'running' }),
      makeWorkloadRun({ id: 'run-3', status: 'completed' }),
      makeWorkloadRun({ id: 'run-4', status: 'failed' }),
    ]
    const artifacts: ArtifactRecord[] = [
      { id: 'a1', fileName: 'pkg.msi', createdAt: new Date().toISOString(), manifest: {} as ArtifactManifest },
    ]
    const workloadStates: NodeWorkloadState[] = [
      makeWorkloadState({ nodeId: 'n1', status: 'queued' }),
    ]

    const result = transformOrchestratorHomeData(nodes, workloadStates, runs, artifacts, workloads)

    expect(result.kpis.nodesOnline).toBe(2)
    expect(result.kpis.nodesOffline).toBe(1)
    expect(result.kpis.workloadDefinitions).toBe(2)
    expect(result.kpis.runningWorkloads).toBe(2)
    expect(result.kpis.artifactsStored).toBe(1)
    expect(result.kpis.pendingApprovals).toBe(1)
  })

  it('maps node status to health correctly', () => {
    const online = makeNode({ id: 'n1', status: 'online' })
    const offline = makeNode({ id: 'n2', status: 'offline' })
    const installing = makeNode({ id: 'n3', status: 'installing' })
    const unknown = makeNode({ id: 'n4', status: 'unknown' })

    const result = transformOrchestratorHomeData([online, offline, installing, unknown], [], [], [], [])

    expect(result.nodes[0].health).toBe('online')
    expect(result.nodes[1].health).toBe('offline')
    expect(result.nodes[2].health).toBe('warning')
    expect(result.nodes[3].health).toBe('warning')
  })

  it('maps workload state status to runState correctly', () => {
    const baseNode = makeNode({ id: 'n1', status: 'online' })

    const runningState = makeWorkloadState({ nodeId: 'n1', status: 'running' })
    const queuedState = makeWorkloadState({ nodeId: 'n1', status: 'queued' })

    const running = transformOrchestratorHomeData([baseNode], [runningState], [], [], [])
    expect(running.nodes[0].runState).toBe('update')

    const pending = transformOrchestratorHomeData([baseNode], [queuedState], [], [], [])
    expect(pending.nodes[0].runState).toBe('pending-approval')
  })

  it('maps offline node without workload state to failed', () => {
    const offlineNode = makeNode({ id: 'n1', status: 'offline' })

    const result = transformOrchestratorHomeData([offlineNode], [], [], [], [])
    expect(result.nodes[0].runState).toBe('failed')
    expect(result.nodes[0].health).toBe('offline')
  })

  it('maps online node without workload state to idle', () => {
    const onlineNode = makeNode({ id: 'n1', status: 'online' })

    const result = transformOrchestratorHomeData([onlineNode], [], [], [], [])
    expect(result.nodes[0].runState).toBe('idle')
  })

  it('populates assignedWorkload from workload definition name', () => {
    const node = makeNode({ id: 'n1' })
    const state = makeWorkloadState({ nodeId: 'n1', workloadId: 'w1' })
    const workload = makeWorkloadDefinition({ id: 'w1', name: 'MyWorkload' })

    const result = transformOrchestratorHomeData([node], [state], [], [], [workload])
    expect(result.nodes[0].assignedWorkload).toBe('MyWorkload')
    expect(result.nodes[0].workloadRevision).toBe('1.0.0')
  })

  it('sets assignedWorkload to empty when workload not found', () => {
    const node = makeNode({ id: 'n1' })
    const state = makeWorkloadState({ nodeId: 'n1', workloadId: 'nonexistent' })

    const result = transformOrchestratorHomeData([node], [state], [], [], [])
    expect(result.nodes[0].assignedWorkload).toBe('')
  })

  it('generates critical events for offline nodes', () => {
    const offlineNode = makeNode({ id: 'n1', status: 'offline' })

    const result = transformOrchestratorHomeData([offlineNode], [], [], [], [])

    const offlineEvents = result.events.filter(e => e.severity === 'critical')
    expect(offlineEvents).toHaveLength(1)
    expect(offlineEvents[0].title).toBe('Node heartbeat timeout')
    expect(offlineEvents[0].nodeId).toBe('n1')
  })

  it('generates high-severity events for failed runs', () => {
    const failedRun: WorkloadRun = makeWorkloadRun({
      id: 'run-1',
      status: 'failed',
      workloadName: 'BadWorkload',
      completedAt: new Date().toISOString(),
    })

    const result = transformOrchestratorHomeData([], [], [failedRun], [], [])

    const highEvents = result.events.filter(e => e.severity === 'high')
    expect(highEvents).toHaveLength(1)
    expect(highEvents[0].title).toBe('Workload run failed')
    expect(highEvents[0].runId).toBe('run-1')
    expect(highEvents[0].detail).toContain('BadWorkload')
  })

  it('produces healthy system event when no issues exist', () => {
    const onlineNode = makeNode({ id: 'n1', status: 'online' })

    const result = transformOrchestratorHomeData([onlineNode], [], [], [], [])

    expect(result.events).toHaveLength(1)
    expect(result.events[0].id).toBe('evt-healthy')
    expect(result.events[0].severity).toBe('info')
  })

  it('computes check-in age without throwing', () => {
    const node = makeNode({ id: 'n1', lastSeenAt: new Date(Date.now() - 30_000).toISOString() })

    const result = transformOrchestratorHomeData([node], [], [], [], [])
    expect(result.nodes[0].lastCheckInAge).toMatch(/^\d+s$/)
  })

  it('selects first node id as selectedNodeId', () => {
    const n1 = makeNode({ id: 'node-a' })
    const n2 = makeNode({ id: 'node-b' })

    const result = transformOrchestratorHomeData([n1, n2], [], [], [], [])
    expect(result.selectedNodeId).toBe('node-a')
  })

  it('sets riskLevel to low for all nodes', () => {
    const onlineNode = makeNode({ id: 'n1', status: 'online' })
    const offlineNode = makeNode({ id: 'n2', status: 'offline' })

    const result = transformOrchestratorHomeData([onlineNode, offlineNode], [], [], [], [])
    expect(result.nodes[0].riskLevel).toBe('low')
    expect(result.nodes[1].riskLevel).toBe('low')
  })

  it('provides logsByNodeId as empty object', () => {
    const result = transformOrchestratorHomeData([], [], [], [], [])
    expect(result.logsByNodeId).toEqual({})
  })

  it('includes workloads in output for frontend use', () => {
    const workloads: WorkloadDefinition[] = [
      makeWorkloadDefinition({ id: 'w1', name: 'Workload A' }),
    ]

    const result = transformOrchestratorHomeData([], [], [], [], workloads)
    expect(result.workloads).toHaveLength(1)
    expect(result.workloads[0].id).toBe('w1')
  })

  it('does not include healthy event when issues exist', () => {
    const offlineNode = makeNode({ id: 'n1', status: 'offline' })

    const result = transformOrchestratorHomeData([offlineNode], [], [], [], [])

    const healthyEvents = result.events.filter(e => e.id === 'evt-healthy')
    expect(healthyEvents).toHaveLength(0)
  })

  it('includes displayName on dashboard nodes', () => {
    const node = makeNode({ id: 'n1', displayName: 'Plant A - Host 1' })

    const result = transformOrchestratorHomeData([node], [], [], [], [])
    expect(result.nodes[0].displayName).toBe('Plant A - Host 1')
  })
})