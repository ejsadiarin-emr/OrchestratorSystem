import { describe, expect, it } from 'vitest'
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
