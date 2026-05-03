using System.Text.RegularExpressions;

namespace DeploymentPoC.Agent.Steps;

public static class VersionComparer
{
    /// <summary>
    /// Compares two version strings with normalization.
    /// Returns true if they match (prefix matching supported).
    /// </summary>
    public static bool Matches(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            return false;
            
        var expectedNorm = NormalizeVersion(expected);
        var actualNorm = NormalizeVersion(actual);
        
        if (expectedNorm.Length == 0 || actualNorm.Length == 0)
            return false;
            
        // Prefix matching: "3.14" matches "3.14.4", "3.14.4150"
        var minLength = Math.Min(expectedNorm.Length, actualNorm.Length);
        for (int i = 0; i < minLength; i++)
        {
            if (expectedNorm[i] != actualNorm[i])
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Extracts the first numeric-dot sequence and splits into segments.
    /// "Python 3.13.3" -> [3, 13, 3]
    /// "24.3.0.202412091607" -> [24, 3, 0, 202412091607]
    /// "2.48.1.windows.1" -> [2, 48, 1]
    /// </summary>
    public static long[] NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return Array.Empty<long>();
            
        // Extract first sequence of digits and dots
        var match = Regex.Match(version, @"\d+(?:\.\d+)*");
        if (!match.Success)
            return Array.Empty<long>();
            
        return match.Value
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.TryParse(s, out var v) ? v : 0)
            .ToArray();
    }
}
