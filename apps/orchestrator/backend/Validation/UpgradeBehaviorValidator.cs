namespace DeploymentPoC.Orchestrator.Validation;

/// <summary>
/// Validates UpgradeBehavior values for package entities.
/// This is a deep module: a small, stable interface that encapsulates
/// the complete domain rule for what constitutes a valid upgrade behavior.
/// </summary>
public static class UpgradeBehaviorValidator
{
    /// <summary>
    /// The set of allowed upgrade behavior values.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "InPlace",
        "UninstallFirst",
        "SideBySide"
    };

    /// <summary>
    /// The default value used for backward compatibility when no explicit
    /// value is provided (e.g., during database migration).
    /// </summary>
    public const string DefaultValue = "InPlace";

    /// <summary>
    /// Validates that the provided value is a non-null, non-empty string
    /// that matches one of the allowed upgrade behavior values.
    /// </summary>
    /// <param name="value">The upgrade behavior value to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating success or failure with an error message.</returns>
    public static ValidationResult Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Failure("UpgradeBehavior is required. Allowed values: InPlace, UninstallFirst, SideBySide.");
        }

        if (!AllowedValues.Contains(value))
        {
            return ValidationResult.Failure(
                $"UpgradeBehavior '{value}' is not valid. Allowed values: InPlace, UninstallFirst, SideBySide.");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Returns true if the value is valid; otherwise false.
    /// </summary>
    public static bool IsValid(string? value)
        => Validate(value).IsValid;

    /// <summary>
    /// Normalizes the value to canonical casing, or returns the default
    /// if null/empty. Does NOT validate.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultValue;

        // Return canonical casing
        foreach (var allowed in AllowedValues)
        {
            if (allowed.Equals(value, StringComparison.OrdinalIgnoreCase))
                return allowed;
        }

        return value; // Pass through invalid values; caller should validate
    }
}

public sealed class ValidationResult
{
    public bool IsValid { get; }
    public string? Error { get; }

    private ValidationResult(bool isValid, string? error)
    {
        IsValid = isValid;
        Error = error;
    }

    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Failure(string error) => new(false, error);
}
