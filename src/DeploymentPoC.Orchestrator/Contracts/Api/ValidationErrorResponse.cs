namespace DeploymentPoC.Orchestrator.Contracts.Api;

public sealed class ValidationErrorResponse
{
    public string Code { get; set; } = "validation_failed";
    public string Message { get; set; } = "Validation failed";
    public List<ValidationFieldError> Errors { get; set; } = new();
}

public sealed class ValidationFieldError
{
    public string Field { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
