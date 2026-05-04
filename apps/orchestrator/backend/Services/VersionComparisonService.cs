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
