export interface Artifact {
  id: number
  packageId: string
  packageName: string
  version: string
  installerFile: string
  uploadedAt: string
}

export interface BulkImportResult {
  imported: { packageId: string; version: string; installerFile: string }[]
  failed: { file: string; reason: string }[]
}
