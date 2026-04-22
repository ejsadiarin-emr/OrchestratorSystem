import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import type { ArtifactManifest, InstallAdapterInput, DetectionInput, PolicyTagsInput } from '../types'
import {
  issueEnrollmentToken,
  suggestManifestFromFile,
  uploadArtifact,
  validateManifestChannel,
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
        expectedVersion: '1.12.0',
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
      expectedVersion: '1.12.0',
    }

    expect(detection).toHaveProperty('type')
    expect(detection).toHaveProperty('path')
    expect(detection).toHaveProperty('expectedVersion')
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
    expect(manifest.detection!.expectedVersion).toBe('2.4.1')
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
        detection: { type: 'registry', path: 'HKLM\\Widget', expectedVersion: '2.4.1' },
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