import { describe, expect, it } from 'vitest'
import { compareVersions, isDowngrade, isSequentialUpgrade, isUpgrade, parseVersion } from './versionComparison'

describe('parseVersion', () => {
  it('parses simple versions', () => {
    expect(parseVersion('1.0.0')).toEqual([1, 0, 0])
    expect(parseVersion('2.5')).toEqual([2, 5])
  })

  it('strips non-numeric prefixes/suffixes', () => {
    expect(parseVersion('v1.0.0')).toEqual([1, 0, 0])
    expect(parseVersion('1.0.0-alpha')).toEqual([1, 0, 0])
  })

  it('returns empty for unparseable strings', () => {
    expect(parseVersion('')).toEqual([])
    expect(parseVersion('abc')).toEqual([])
  })
})

describe('compareVersions', () => {
  it('returns 0 for equal versions', () => {
    expect(compareVersions('1.0.0', '1.0.0')).toBe(0)
    expect(compareVersions('1.0', '1.0.0')).toBe(0)
  })

  it('returns negative when a < b', () => {
    expect(compareVersions('1.0.0', '1.1.0')).toBeLessThan(0)
    expect(compareVersions('1.5.0', '2.0.0')).toBeLessThan(0)
  })

  it('returns positive when a > b', () => {
    expect(compareVersions('1.1.0', '1.0.0')).toBeGreaterThan(0)
    expect(compareVersions('2.0.0', '1.5.0')).toBeGreaterThan(0)
  })

  it('handles patch segment differences', () => {
    expect(compareVersions('1.0.10', '1.0.2')).toBeGreaterThan(0)
  })

  it('zero-pads shorter versions', () => {
    expect(compareVersions('3.14', '3.14.4')).toBeLessThan(0)
  })

  it('returns NaN for unparseable versions', () => {
    expect(compareVersions('abc', '1.0.0')).toBeNaN()
    expect(compareVersions('1.0.0', '')).toBeNaN()
  })
})

describe('isDowngrade', () => {
  it('returns true when current > target', () => {
    expect(isDowngrade('2.0.0', '1.0.0')).toBe(true)
  })

  it('returns false when current <= target', () => {
    expect(isDowngrade('1.0.0', '2.0.0')).toBe(false)
    expect(isDowngrade('1.0.0', '1.0.0')).toBe(false)
  })

  it('returns false for unparseable versions', () => {
    expect(isDowngrade('abc', '1.0.0')).toBe(false)
  })
})

describe('isUpgrade', () => {
  it('returns true when current < target', () => {
    expect(isUpgrade('1.0.0', '2.0.0')).toBe(true)
  })

  it('returns false when current >= target', () => {
    expect(isUpgrade('2.0.0', '1.0.0')).toBe(false)
    expect(isUpgrade('1.0.0', '1.0.0')).toBe(false)
  })
})

describe('isSequentialUpgrade', () => {
  it('returns true for immediate predecessor', () => {
    expect(isSequentialUpgrade('1.0.0', '1.0.1', ['1.0.0', '1.0.1', '1.0.2'])).toBe(true)
    expect(isSequentialUpgrade('1.0.0', '2.0.0', ['1.0.0', '2.0.0'])).toBe(true)
  })

  it('returns false for version jump', () => {
    expect(isSequentialUpgrade('1.0.0', '1.0.2', ['1.0.0', '1.0.1', '1.0.2'])).toBe(false)
    expect(isSequentialUpgrade('1.0.0', '3.0.0', ['1.0.0', '2.0.0', '3.0.0'])).toBe(false)
  })

  it('returns false when current or target not in list', () => {
    expect(isSequentialUpgrade('1.0.0', '2.0.0', ['2.0.0', '3.0.0'])).toBe(false)
    expect(isSequentialUpgrade('1.0.0', '2.0.0', ['1.0.0', '3.0.0'])).toBe(false)
  })

  it('handles unsorted input', () => {
    expect(isSequentialUpgrade('1.0.0', '1.0.1', ['1.0.2', '1.0.0', '1.0.1'])).toBe(true)
  })
})
