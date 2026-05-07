/**
 * Parse a version string into numeric segments.
 * Returns empty array if unparseable.
 */
export function parseVersion(version: string): number[] {
  if (!version || !version.trim()) return []

  const match = version.match(/\d+(?:\.\d+)*/)
  if (!match) return []

  return match[0]
    .split('.')
    .filter(Boolean)
    .map(s => {
      const n = parseInt(s, 10)
      return isNaN(n) ? 0 : n
    })
}

/**
 * Compare two version strings.
 * Returns negative if a < b, 0 if equal, positive if a > b.
 * Returns NaN if either is unparseable.
 */
export function compareVersions(a: string, b: string): number {
  const segsA = parseVersion(a)
  const segsB = parseVersion(b)

  if (segsA.length === 0 || segsB.length === 0) return NaN

  const maxLen = Math.max(segsA.length, segsB.length)
  for (let i = 0; i < maxLen; i++) {
    const va = i < segsA.length ? segsA[i] : 0
    const vb = i < segsB.length ? segsB[i] : 0
    if (va !== vb) return va - vb
  }

  return 0
}

export function isDowngrade(currentVersion: string, targetVersion: string): boolean {
  const result = compareVersions(currentVersion, targetVersion)
  return !isNaN(result) && result > 0
}

export function isUpgrade(currentVersion: string, targetVersion: string): boolean {
  const result = compareVersions(currentVersion, targetVersion)
  return !isNaN(result) && result < 0
}

/**
 * Check if targetVersion is the immediate next version after currentVersion
 * in the ordered list of all published versions.
 */
export function isSequentialUpgrade(
  currentVersion: string,
  targetVersion: string,
  allVersions: string[],
): boolean {
  const sorted = [...allVersions].sort((a, b) => {
    const cmp = compareVersions(a, b)
    if (isNaN(cmp)) return a.localeCompare(b)
    return cmp
  })

  const currentIdx = sorted.indexOf(currentVersion)
  const targetIdx = sorted.indexOf(targetVersion)

  if (currentIdx < 0 || targetIdx < 0) return false
  return targetIdx === currentIdx + 1
}
