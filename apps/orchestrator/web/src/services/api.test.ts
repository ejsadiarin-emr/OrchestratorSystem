import { describe, expect, it } from 'vitest'
import type { ArtifactManifest, InstallAdapterInput, DetectionInput, PolicyTagsInput } from '../types'
import {
  issueEnrollmentToken,
  suggestManifestFromFile,
  uploadArtifact,
  validateManifestChannel,
} from './api'

describe('api channel validation', () => {
  it('accepts stable/canary/test and rejects others', () => {
    expect(validateManifestChannel('stable')).toBe(true)
    expect(validateManifestChannel('canary')).toBe(true)
    expect(validateManifestChannel('test')).toBe(true)
    expect(validateManifestChannel('beta')).toBe(false)
  })

  it('rejects upload when manifest.channel is invalid', async () => {
    const manifest = suggestManifestFromFile('Widget-2.4.1.msi', 2048)
    await expect(
      uploadArtifact({
        fileName: 'Widget-2.4.1.msi',
        fileSizeBytes: 2048,
        manifest: { ...manifest, channel: 'beta' as never },
      }),
    ).rejects.toThrow('manifest.channel must be one of stable, canary, test')
  })

  it('rejects upload when manifest JSON part is missing', async () => {
    await expect(
      uploadArtifact({
        fileName: 'Widget-2.4.1.msi',
        fileSizeBytes: 2048,
        manifest: undefined as never,
      }),
    ).rejects.toThrow('manifest JSON part is required for multipart upload')
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

describe('api enrollment semantics', () => {
  it('issues single-use token with requested URL via POST-style function', async () => {
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
