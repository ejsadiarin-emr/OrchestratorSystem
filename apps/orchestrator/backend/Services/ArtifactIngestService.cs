using System.Text.Json;
using DeploymentPoC.Orchestrator.Contracts.Api;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class ArtifactIngestService
{
    public ArtifactIngestResult Ingest(string fileName, Stream fileStream, ArtifactIngestManifest manifest, string actorId)
    {
        var errors = ValidateMinimalFields(manifest, fileName);
        if (errors.Count > 0)
        {
            return ArtifactIngestResult.ValidationFailure(errors);
        }

        if (string.Equals(manifest.VerificationResult, "fail", StringComparison.OrdinalIgnoreCase))
        {
            return ArtifactIngestResult.ValidationFailure(new List<ValidationFieldError>
            {
                new()
                {
                    Field = "manifest.verificationResult",
                    Error = "Verification result 'fail' blocks ingest"
                }
            });
        }

        var now = DateTime.UtcNow;

        var resolved = new ResolvedManifest
        {
            PackageId = manifest.PackageId!.Trim(),
            Version = manifest.Version!.Trim(),
            Channel = manifest.Channel!.Trim().ToLowerInvariant(),
            ArtifactType = ResolveArtifactType(manifest, fileName),
            InstallAdapter = ResolveAdapter(manifest, fileName),
            Detection = ResolveDetection(manifest),
            OriginMetadata = new OriginMetadata
            {
                Source = "internal-upload",
                Publisher = "unknown",
                IngestedBy = actorId,
                IngestedAtUtc = now,
                VerificationResult = string.IsNullOrWhiteSpace(manifest.VerificationResult)
                    ? "pass"
                    : manifest.VerificationResult.Trim().ToLowerInvariant()
            },
            PolicyTags = ResolvePolicyTags(manifest),
            Sources = new ResolvedManifestSources()
        };

        if (string.Equals(resolved.OriginMetadata.VerificationResult, "warn", StringComparison.Ordinal))
        {
            resolved.PolicyTags.RiskLevel = "high";
            resolved.PolicyTags.ApprovalRequired = true;
            resolved.Sources.PolicyTags.RiskLevel = "default";
            resolved.Sources.PolicyTags.ApprovalRequired = "default";
            resolved.PolicyTagsSources.RiskLevel = "default";
            resolved.PolicyTagsSources.ApprovalRequired = "default";
        }

        if (!CanResolveAdapterAndDetection(resolved))
        {
            return ArtifactIngestResult.ValidationFailure(new List<ValidationFieldError>
            {
                new()
                {
                    Field = "manifest.artifactType",
                    Error = "artifactType is required when adapter/detection cannot be inferred"
                }
            });
        }

        return ArtifactIngestResult.Success(resolved);
    }

    private static List<ValidationFieldError> ValidateMinimalFields(ArtifactIngestManifest manifest, string fileName)
    {
        var errors = new List<ValidationFieldError>();

        if (string.IsNullOrWhiteSpace(manifest.PackageId))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.packageId", Error = "packageId is required" });
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.version", Error = "version is required" });
        }

        var channel = manifest.Channel?.Trim().ToLowerInvariant();
        if (channel is not ("stable" or "canary" or "test"))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.channel", Error = "channel must be one of stable|canary|test" });
        }

        if (string.IsNullOrWhiteSpace(manifest.ArtifactType) && !TryInferArtifactType(fileName, out _))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.artifactType", Error = "artifactType is required when media type is not inferable" });
        }

        return errors;
    }

    private static string ResolveArtifactType(ArtifactIngestManifest manifest, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(manifest.ArtifactType))
        {
            return manifest.ArtifactType.Trim().ToLowerInvariant();
        }

        if (TryInferArtifactType(fileName, out var inferred))
        {
            return inferred;
        }

        return string.Empty;
    }

    private static InstallAdapter ResolveAdapter(ArtifactIngestManifest manifest, string fileName)
    {
        var adapter = manifest.InstallAdapter;
        if (adapter is not null)
        {
            return new InstallAdapter
            {
                Type = string.IsNullOrWhiteSpace(adapter.Type) ? ResolveAdapterType(fileName) : adapter.Type.Trim().ToLowerInvariant(),
                Command = string.IsNullOrWhiteSpace(adapter.Command) ? "artifact.bin" : adapter.Command.Trim(),
                Arguments = adapter.Arguments ?? string.Empty,
                ExpectedExitCodes = adapter.ExpectedExitCodes?.Count > 0 ? adapter.ExpectedExitCodes : new List<int> { 0, 3010 },
                TimeoutSeconds = adapter.TimeoutSeconds > 0 ? adapter.TimeoutSeconds.Value : 1800
            };
        }

        var type = ResolveAdapterType(fileName);
        return new InstallAdapter
        {
            Type = type,
            Command = "artifact.bin",
            Arguments = type switch
            {
                "msi" => "/qn /norestart",
                "exe" => "/quiet /norestart",
                _ => string.Empty
            },
            ExpectedExitCodes = new List<int> { 0, 3010 },
            TimeoutSeconds = 1800
        };
    }

    private static Detection ResolveDetection(ArtifactIngestManifest manifest)
    {
        if (manifest.Detection is not null)
        {
            return new Detection
            {
                Type = string.IsNullOrWhiteSpace(manifest.Detection.Type) ? "version_manifest" : manifest.Detection.Type.Trim(),
                Path = string.IsNullOrWhiteSpace(manifest.Detection.Path) ? manifest.PackageId ?? string.Empty : manifest.Detection.Path.Trim(),
                ExpectedVersion = string.IsNullOrWhiteSpace(manifest.Detection.ExpectedVersion) ? $"=={manifest.Version}" : manifest.Detection.ExpectedVersion.Trim()
            };
        }

        return new Detection
        {
            Type = "version_manifest",
            Path = manifest.PackageId ?? string.Empty,
            ExpectedVersion = $"=={manifest.Version}"
        };
    }

    private static PolicyTags ResolvePolicyTags(ArtifactIngestManifest manifest)
    {
        var tags = manifest.PolicyTags;
        return new PolicyTags
        {
            RetryabilityClass = string.IsNullOrWhiteSpace(tags?.RetryabilityClass) ? "transient_only" : tags!.RetryabilityClass!.Trim(),
            IdempotencyMode = string.IsNullOrWhiteSpace(tags?.IdempotencyMode) ? "version_check" : tags!.IdempotencyMode!.Trim(),
            RiskLevel = string.IsNullOrWhiteSpace(tags?.RiskLevel) ? "medium" : tags!.RiskLevel!.Trim(),
            ApprovalRequired = tags?.ApprovalRequired ?? false
        };
    }

    private static bool TryInferArtifactType(string fileName, out string artifactType)
    {
        artifactType = string.Empty;
        var lower = fileName.Trim().ToLowerInvariant();
        if (lower.EndsWith(".msi", StringComparison.Ordinal))
        {
            artifactType = "msi";
            return true;
        }

        if (lower.EndsWith(".exe", StringComparison.Ordinal))
        {
            artifactType = "exe";
            return true;
        }

        if (lower.EndsWith(".zip", StringComparison.Ordinal) || lower.EndsWith(".tar.gz", StringComparison.Ordinal))
        {
            artifactType = "archive";
            return true;
        }

        return false;
    }

    private static string ResolveAdapterType(string fileName)
    {
        if (TryInferArtifactType(fileName, out var type))
        {
            return type;
        }

        return string.Empty;
    }

    private static bool CanResolveAdapterAndDetection(ResolvedManifest manifest)
    {
        return !string.IsNullOrWhiteSpace(manifest.ArtifactType)
               && !string.IsNullOrWhiteSpace(manifest.InstallAdapter.Type)
               && !string.IsNullOrWhiteSpace(manifest.InstallAdapter.Command)
               && manifest.InstallAdapter.ExpectedExitCodes.Count > 0
               && manifest.InstallAdapter.TimeoutSeconds > 0
               && !string.IsNullOrWhiteSpace(manifest.Detection.Type)
               && !string.IsNullOrWhiteSpace(manifest.Detection.Path)
               && !string.IsNullOrWhiteSpace(manifest.Detection.ExpectedVersion);
    }
}

public sealed class ArtifactIngestManifest
{
    public string? PackageId { get; set; }
    public string? Version { get; set; }
    public string? Channel { get; set; }
    public string? ArtifactType { get; set; }
    public string? VerificationResult { get; set; }
    public InstallAdapterInput? InstallAdapter { get; set; }
    public DetectionInput? Detection { get; set; }
    public PolicyTagsInput? PolicyTags { get; set; }
}

public sealed class InstallAdapterInput
{
    public string? Type { get; set; }
    public string? Command { get; set; }
    public string? Arguments { get; set; }
    public List<int>? ExpectedExitCodes { get; set; }
    public int? TimeoutSeconds { get; set; }
}

public sealed class DetectionInput
{
    public string? Type { get; set; }
    public string? Path { get; set; }
    public string? ExpectedVersion { get; set; }
}

public sealed class PolicyTagsInput
{
    public string? RetryabilityClass { get; set; }
    public string? IdempotencyMode { get; set; }
    public string? RiskLevel { get; set; }
    public bool? ApprovalRequired { get; set; }
}

public sealed class ArtifactIngestResult
{
    public bool IsValid { get; private set; }
    public List<ValidationFieldError> Errors { get; private set; } = new();
    public ResolvedManifest? ResolvedManifest { get; private set; }

    public static ArtifactIngestResult ValidationFailure(List<ValidationFieldError> errors)
    {
        return new ArtifactIngestResult
        {
            IsValid = false,
            Errors = errors
        };
    }

    public static ArtifactIngestResult Success(ResolvedManifest resolvedManifest)
    {
        return new ArtifactIngestResult
        {
            IsValid = true,
            ResolvedManifest = resolvedManifest
        };
    }
}

public sealed class ResolvedManifest
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public InstallAdapter InstallAdapter { get; set; } = new();
    public Detection Detection { get; set; } = new();
    public OriginMetadata OriginMetadata { get; set; } = new();
    public PolicyTags PolicyTags { get; set; } = new();
    public PolicyTagSourceBreakdown PolicyTagsSources { get; set; } = new();
    public ResolvedManifestSources Sources { get; set; } = new();
}

public sealed class InstallAdapter
{
    public string Type { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public List<int> ExpectedExitCodes { get; set; } = new();
    public int TimeoutSeconds { get; set; }
}

public sealed class Detection
{
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ExpectedVersion { get; set; } = string.Empty;
}

public sealed class OriginMetadata
{
    public string Source { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string IngestedBy { get; set; } = string.Empty;
    public DateTime IngestedAtUtc { get; set; }
    public string VerificationResult { get; set; } = string.Empty;
}

public sealed class PolicyTags
{
    public string RetryabilityClass { get; set; } = string.Empty;
    public string IdempotencyMode { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public bool ApprovalRequired { get; set; }
}

public sealed class ResolvedManifestSources
{
    public ManifestFieldSource ArtifactType { get; set; } = ManifestFieldSource.Default;
    public ManifestFieldSource InstallAdapter { get; set; } = ManifestFieldSource.Default;
    public ManifestFieldSource Detection { get; set; } = ManifestFieldSource.Default;
    public ManifestFieldSource PolicyTagsComposite { get; set; } = ManifestFieldSource.Default;
    public PolicyTagSourceBreakdown PolicyTags { get; set; } = new();
}

public sealed class PolicyTagSourceBreakdown
{
    public string RetryabilityClass { get; set; } = "default";
    public string IdempotencyMode { get; set; } = "default";
    public string RiskLevel { get; set; } = "default";
    public string ApprovalRequired { get; set; } = "default";
}

public enum ManifestFieldSource
{
    Admin,
    Template,
    Analyzer,
    Default
}
