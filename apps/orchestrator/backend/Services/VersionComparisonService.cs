using System.Text.RegularExpressions;

namespace DeploymentPoC.Orchestrator.Services;

public static class VersionComparisonService
{
    /// <summary>
    /// Compares two version strings. Returns negative if a < b, zero if equal, positive if a > b.
    /// Returns null if versions cannot be compared (non-numeric segments after extraction).
    /// </summary>
    public static int? CompareVersions(string? versionA, string? versionB)
    {
        if (string.IsNullOrWhiteSpace(versionA) || string.IsNullOrWhiteSpace(versionB))
            return null;

        var segmentsA = NormalizeVersion(versionA);
        var segmentsB = NormalizeVersion(versionB);

        if (segmentsA.Length == 0 || segmentsB.Length == 0)
            return null;

        var maxLength = Math.Max(segmentsA.Length, segmentsB.Length);
        for (int i = 0; i < maxLength; i++)
        {
            var a = i < segmentsA.Length ? segmentsA[i] : 0;
            var b = i < segmentsB.Length ? segmentsB[i] : 0;
            if (a != b)
                return a.CompareTo(b);
        }

        return 0;
    }

    public static bool IsDowngrade(string? currentVersion, string? targetVersion)
    {
        var result = CompareVersions(currentVersion, targetVersion);
        return result.HasValue && result.Value > 0;
    }

    public static bool IsUpgrade(string? currentVersion, string? targetVersion)
    {
        var result = CompareVersions(currentVersion, targetVersion);
        return result.HasValue && result.Value < 0;
    }

    public static bool Matches(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            return false;

        var expectedNorm = NormalizeVersion(expected);
        var actualNorm = NormalizeVersion(actual);

        if (expectedNorm.Length == 0 || actualNorm.Length == 0)
            return false;

        var minLength = Math.Min(expectedNorm.Length, actualNorm.Length);
        for (int i = 0; i < minLength; i++)
        {
            if (expectedNorm[i] != actualNorm[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if targetVersion is the immediate next version after currentVersion
    /// in the ordered list of all published versions.
    /// </summary>
    public static bool IsSequentialRevision(string? currentVersion, string targetVersion, IEnumerable<string> allPublishedVersions)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
            return true; // Fresh install, no constraint

        var sorted = allPublishedVersions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v, new VersionStringComparer())
            .ToList();

        var currentIdx = sorted.IndexOf(currentVersion);
        var targetIdx = sorted.IndexOf(targetVersion);

        if (currentIdx < 0 || targetIdx < 0)
            return false;

        return targetIdx == currentIdx + 1;
    }

    private class VersionStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            var result = CompareVersions(x, y);
            if (result.HasValue)
                return result.Value;
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static long[] NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return Array.Empty<long>();

        var match = Regex.Match(version, @"\d+(?:\.\d+)*");
        if (!match.Success)
            return Array.Empty<long>();

        return match.Value
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.TryParse(s, out var v) ? v : 0)
            .ToArray();
    }
}
