import { unzip } from 'fflate'
import type { ArtifactManifest } from '../types'

export async function extractZipEntries(file: File): Promise<string[]> {
  const buffer = await file.arrayBuffer()
  const data = new Uint8Array(buffer)

  return new Promise((resolve, reject) => {
    unzip(data, (err, result) => {
      if (err) {
        reject(err)
        return
      }
      resolve(Object.keys(result))
    })
  })
}

export function detectArtifactPairs(entries: string[]): { baseName: string; mediaFile: string; manifestFile: string }[] {
  const manifestFiles = entries.filter(e => e.endsWith('.manifest.json'))
  const mediaFiles = entries.filter(e => !e.endsWith('.manifest.json') && !e.endsWith('/'))

  const pairs: { baseName: string; mediaFile: string; manifestFile: string }[] = []

  for (const manifestFile of manifestFiles) {
    const baseName = manifestFile.slice(0, -'.manifest.json'.length)
    const mediaFile = mediaFiles.find(m => {
      const mediaBase = m.includes('.') ? m.slice(0, m.lastIndexOf('.')) : m
      return mediaBase === baseName
    })
    if (mediaFile) {
      pairs.push({ baseName, mediaFile, manifestFile })
    }
  }

  return pairs
}

export async function extractManifestFromZip(file: File, manifestPath: string): Promise<ArtifactManifest> {
  const buffer = await file.arrayBuffer()
  const data = new Uint8Array(buffer)

  return new Promise((resolve, reject) => {
    unzip(data, (err, result) => {
      if (err) {
        reject(err)
        return
      }
      const entry = result[manifestPath]
      if (!entry) {
        reject(new Error(`Manifest file ${manifestPath} not found in zip`))
        return
      }
      try {
        const text = new TextDecoder().decode(entry)
        const parsed = JSON.parse(text) as ArtifactManifest
        resolve(parsed)
      } catch (parseErr) {
        reject(parseErr)
      }
    })
  })
}

export function isZipFile(fileName: string): boolean {
  const lower = fileName.toLowerCase()
  return lower.endsWith('.zip') || lower.endsWith('.tar.gz')
}