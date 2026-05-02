using System.Text.Json;
using System.Text.Json.Serialization;
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
        var artifactType = ResolveArtifactType(manifest, fileName);
        var (installAdapter, installAdapterSources) = ResolveAdapter(manifest, fileName, fileStream);
        var detection = ResolveDetection(manifest, artifactType);

        var resolved = new ResolvedManifest
        {
            PackageId = manifest.PackageId!.Trim(),
            Version = manifest.Version!.Trim(),
            Channel = manifest.Channel!.Trim().ToLowerInvariant(),
            ArtifactType = artifactType,
            InstallAdapter = installAdapter,
            Detection = detection,
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
            Sources = new ResolvedManifestSources
            {
                InstallAdapterSources = installAdapterSources
            }
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

        var conditionalErrors = ValidateConditionalFields(resolved);
        if (conditionalErrors.Count > 0)
        {
            return ArtifactIngestResult.ValidationFailure(conditionalErrors);
        }

        var schemaErrors = ValidateResolvedManifestSchema(resolved);
        if (schemaErrors.Count > 0)
        {
            return ArtifactIngestResult.ValidationFailure(schemaErrors);
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

    private static (InstallAdapter, InstallAdapterSourceBreakdown) ResolveAdapter(ArtifactIngestManifest manifest, string fileName, Stream fileStream)
    {
        var adapter = manifest.InstallAdapter;
        if (adapter is not null)
        {
            var sources = new InstallAdapterSourceBreakdown();
            var templateDefaults = TryGetTemplateDefaults(manifest.PackageId);
            var analyzerType = TryAnalyzeFileContent(fileStream, fileName);

            var command = string.IsNullOrWhiteSpace(adapter.Command) ? "{artifactPath}" : adapter.Command.Trim();
            if (!string.Equals(command, "{artifactPath}", StringComparison.OrdinalIgnoreCase))
            {
                var commandFileName = Path.GetFileName(command);
                var artifactFileName = Path.GetFileName(fileName);
                if (string.Equals(commandFileName, artifactFileName, StringComparison.OrdinalIgnoreCase))
                {
                    command = "{artifactPath}";
                }
            }

            var result = new InstallAdapter
            {
                Type = string.IsNullOrWhiteSpace(adapter.Type)
                    ? (analyzerType ?? templateDefaults?.Type ?? ResolveAdapterType(fileName))
                    : adapter.Type.Trim().ToLowerInvariant(),
                Command = command,
                Arguments = adapter.Arguments ?? string.Empty,
                UninstallArgs = adapter.UninstallArgs ?? string.Empty,
                UninstallCommand = adapter.UninstallCommand ?? string.Empty,
                UpgradeBehavior = string.IsNullOrWhiteSpace(adapter.UpgradeBehavior) ? "InPlace" : adapter.UpgradeBehavior.Trim(),
                ExpectedExitCodes = adapter.ExpectedExitCodes?.Count > 0 ? adapter.ExpectedExitCodes : new List<int> { 0 },
                TimeoutSeconds = adapter.TimeoutSeconds > 0 ? adapter.TimeoutSeconds.Value : 1800
            };

            sources.Type = string.IsNullOrWhiteSpace(adapter.Type)
                ? (analyzerType is not null ? "analyzer" : (templateDefaults is not null ? "template" : "default"))
                : "admin";
            sources.Command = string.IsNullOrWhiteSpace(adapter.Command) ? "default" : "admin";
            sources.Arguments = adapter.Arguments is null ? "default" : "admin";
            sources.UninstallArgs = adapter.UninstallArgs is null ? "default" : "admin";
            sources.UninstallCommand = adapter.UninstallCommand is null ? "default" : "admin";
            sources.UpgradeBehavior = string.IsNullOrWhiteSpace(adapter.UpgradeBehavior) ? "default" : "admin";
            sources.ExpectedExitCodes = adapter.ExpectedExitCodes?.Count > 0 ? "admin" : "default";
            sources.TimeoutSeconds = adapter.TimeoutSeconds > 0 ? "admin" : "default";
            return (result, sources);
        }

        var type = ResolveAdapterType(fileName);
        if (string.IsNullOrWhiteSpace(type))
        {
            var emptySources = new InstallAdapterSourceBreakdown
            {
                Type = "default",
                Command = "default",
                Arguments = "default",
                UninstallArgs = "default",
                UninstallCommand = "default",
                UpgradeBehavior = "default",
                ExpectedExitCodes = "default",
                TimeoutSeconds = "default"
            };
            return (new InstallAdapter
            {
                Type = string.Empty,
                Command = string.Empty,
                Arguments = string.Empty,
                UninstallArgs = string.Empty,
                UninstallCommand = string.Empty,
                UpgradeBehavior = "InPlace",
                ExpectedExitCodes = new List<int>(),
                TimeoutSeconds = 0
            }, emptySources);
        }

        var defaultSources = new InstallAdapterSourceBreakdown
        {
            Type = "default",
            Command = "default",
            Arguments = "default",
            UninstallArgs = "default",
            UninstallCommand = "default",
            UpgradeBehavior = "default",
            ExpectedExitCodes = "default",
            TimeoutSeconds = "default"
        };
        return (new InstallAdapter
        {
            Type = type,
            Command = "{artifactPath}",
            Arguments = string.Empty,
            UninstallArgs = string.Empty,
            UninstallCommand = string.Empty,
            UpgradeBehavior = "InPlace",
            ExpectedExitCodes = new List<int> { 0 },
            TimeoutSeconds = 1800
        }, defaultSources);
    }

    private static Detection ResolveDetection(ArtifactIngestManifest manifest, string artifactType)
    {
        if (string.IsNullOrWhiteSpace(artifactType) || artifactType == "unknown")
        {
            return new Detection
            {
                Type = manifest.Detection?.Type ?? string.Empty,
                Path = manifest.Detection?.Path ?? string.Empty,
                ExpectedVersion = manifest.Detection?.ExpectedVersion ?? string.Empty
            };
        }

        if (manifest.Detection is not null)
        {
            return new Detection
            {
                Type = string.IsNullOrWhiteSpace(manifest.Detection.Type) ? "version_manifest" : manifest.Detection.Type.Trim(),
                Path = string.IsNullOrWhiteSpace(manifest.Detection.Path) ? manifest.PackageId ?? string.Empty : manifest.Detection.Path.Trim(),
                ExpectedVersion = manifest.Detection.ExpectedVersion?.Trim() ?? string.Empty
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

    private static InstallAdapterInput? TryGetTemplateDefaults(string? packageId)
    {
        // TODO: Replace with real template resolution from template store
        // For now, returns null unless packageId follows template naming convention
        if (!string.IsNullOrWhiteSpace(packageId) && packageId.StartsWith("template-", StringComparison.OrdinalIgnoreCase))
        {
            return new InstallAdapterInput
            {
                Type = "msi",
                Command = "msiexec.exe",
                Arguments = "/qn /norestart",
                ExpectedExitCodes = new List<int> { 0, 3010 },
                TimeoutSeconds = 1800
            };
        }
        return null;
    }

    private static string? TryAnalyzeFileContent(Stream? fileStream, string fileName)
    {
        if (fileStream is null || !fileStream.CanRead)
        {
            return null;
        }

        try
        {
            var position = fileStream.Position;
            var buffer = new byte[8];
            var read = fileStream.Read(buffer, 0, buffer.Length);
            fileStream.Position = position;

            if (read >= 4)
            {
                // PE executable magic: MZ header
                if (buffer[0] == 0x4D && buffer[1] == 0x5A)
                {
                    return "exe";
                }
                // MSI magic: D0 CF 11 E0 (OLE compound document)
                if (buffer[0] == 0xD0 && buffer[1] == 0xCF && buffer[2] == 0x11 && buffer[3] == 0xE0)
                {
                    return "msi";
                }
                // ZIP magic: PK
                if (buffer[0] == 0x50 && buffer[1] == 0x4B)
                {
                    return "archive";
                }
            }
        }
        catch
        {
            // Analysis failure should not block ingest; fall back to extension-based detection
        }

        return null;
    }

    private static List<ValidationFieldError> ValidateResolvedManifestSchema(ResolvedManifest manifest)
    {
        var errors = new List<ValidationFieldError>();

        if (string.IsNullOrWhiteSpace(manifest.PackageId))
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.packageId", Error = "packageId is required" });
        if (string.IsNullOrWhiteSpace(manifest.Version))
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.version", Error = "version is required" });
        if (string.IsNullOrWhiteSpace(manifest.Channel))
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.channel", Error = "channel is required" });
        if (string.IsNullOrWhiteSpace(manifest.ArtifactType))
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.artifactType", Error = "artifactType is required" });

        if (manifest.InstallAdapter is null)
        {
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.installAdapter", Error = "installAdapter is required" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.InstallAdapter.Type))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.installAdapter.type", Error = "installAdapter.type is required" });
            if (string.IsNullOrWhiteSpace(manifest.InstallAdapter.Command))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.installAdapter.command", Error = "installAdapter.command is required" });
            if (manifest.InstallAdapter.ExpectedExitCodes is null || manifest.InstallAdapter.ExpectedExitCodes.Count == 0)
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.installAdapter.expectedExitCodes", Error = "installAdapter.expectedExitCodes is required" });
            if (manifest.InstallAdapter.TimeoutSeconds <= 0)
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.installAdapter.timeoutSeconds", Error = "installAdapter.timeoutSeconds is required" });
        }

        if (manifest.Detection is null)
        {
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.detection", Error = "detection is required" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.Detection.Type))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.detection.type", Error = "detection.type is required" });
            if (string.IsNullOrWhiteSpace(manifest.Detection.Path))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.detection.path", Error = "detection.path is required" });
            if (string.IsNullOrWhiteSpace(manifest.Detection.ExpectedVersion))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.detection.expectedVersion", Error = "detection.expectedVersion is required" });
        }

        if (manifest.OriginMetadata is null)
        {
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.originMetadata", Error = "originMetadata is required" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.OriginMetadata.Source))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.originMetadata.source", Error = "originMetadata.source is required" });
            if (string.IsNullOrWhiteSpace(manifest.OriginMetadata.Publisher))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.originMetadata.publisher", Error = "originMetadata.publisher is required" });
            if (string.IsNullOrWhiteSpace(manifest.OriginMetadata.IngestedBy))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.originMetadata.ingestedBy", Error = "originMetadata.ingestedBy is required" });
            if (string.IsNullOrWhiteSpace(manifest.OriginMetadata.VerificationResult))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.originMetadata.verificationResult", Error = "originMetadata.verificationResult is required" });
        }

        if (manifest.PolicyTags is null)
        {
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.policyTags", Error = "policyTags is required" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(manifest.PolicyTags.RiskLevel))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.policyTags.riskLevel", Error = "policyTags.riskLevel is required" });
            if (string.IsNullOrWhiteSpace(manifest.PolicyTags.RetryabilityClass))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.policyTags.retryabilityClass", Error = "policyTags.retryabilityClass is required" });
            if (string.IsNullOrWhiteSpace(manifest.PolicyTags.IdempotencyMode))
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.policyTags.idempotencyMode", Error = "policyTags.idempotencyMode is required" });
        }

        if (manifest.Sources is null)
        {
            errors.Add(new ValidationFieldError { Field = "resolvedManifest.sources", Error = "sources is required" });
        }
        else
        {
            if (manifest.Sources.PolicyTags is null)
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.sources.policyTags", Error = "sources.policyTags is required" });
            if (manifest.Sources.InstallAdapterSources is null)
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.sources.installAdapterSources", Error = "sources.installAdapterSources is required" });
            if (manifest.Sources.DetectionSources is null)
                errors.Add(new ValidationFieldError { Field = "resolvedManifest.sources.detectionSources", Error = "sources.detectionSources is required" });
        }

        return errors;
    }

    private static List<ValidationFieldError> ValidateConditionalFields(ResolvedManifest manifest)
    {
        var errors = new List<ValidationFieldError>();

        if (string.IsNullOrWhiteSpace(manifest.InstallAdapter.Type))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.installAdapter.type", Error = "installAdapter.type is required when adapter cannot be resolved" });
        }
        if (string.IsNullOrWhiteSpace(manifest.InstallAdapter.Command))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.installAdapter.command", Error = "installAdapter.command is required when adapter cannot be resolved" });
        }
        if (string.IsNullOrWhiteSpace(manifest.InstallAdapter.Arguments))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.installAdapter.arguments", Error = "installAdapter.arguments is required when adapter cannot be resolved" });
        }
        if (manifest.InstallAdapter.ExpectedExitCodes.Count == 0)
        {
            errors.Add(new ValidationFieldError { Field = "manifest.installAdapter.expectedExitCodes", Error = "installAdapter.expectedExitCodes is required when adapter cannot be resolved" });
        }
        if (manifest.InstallAdapter.TimeoutSeconds <= 0)
        {
            errors.Add(new ValidationFieldError { Field = "manifest.installAdapter.timeoutSeconds", Error = "installAdapter.timeoutSeconds is required when adapter cannot be resolved" });
        }
        if (string.IsNullOrWhiteSpace(manifest.Detection.Type))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.detection.type", Error = "detection.type is required when detection cannot be resolved" });
        }
        if (string.IsNullOrWhiteSpace(manifest.Detection.Path))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.detection.path", Error = "detection.path is required when detection cannot be resolved" });
        }
        if (string.IsNullOrWhiteSpace(manifest.Detection.ExpectedVersion))
        {
            errors.Add(new ValidationFieldError { Field = "manifest.detection.expectedVersion", Error = "detection.expectedVersion is required when detection cannot be resolved" });
        }

        return errors;
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
    public string? UninstallArgs { get; set; }
    public string? UninstallCommand { get; set; }
    public string? UpgradeBehavior { get; set; }
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
    public string UninstallArgs { get; set; } = string.Empty;
    public string UninstallCommand { get; set; } = string.Empty;
    public string UpgradeBehavior { get; set; } = "InPlace";
    public List<int> ExpectedExitCodes { get; set; } = new() { 0 };
    public int TimeoutSeconds { get; set; } = 300;
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
    public InstallAdapterSourceBreakdown InstallAdapterSources { get; set; } = new();
    public DetectionSourceBreakdown DetectionSources { get; set; } = new();
}

public sealed class InstallAdapterSourceBreakdown
{
    public string Type { get; set; } = "default";
    public string Command { get; set; } = "default";
    public string Arguments { get; set; } = "default";
    public string UninstallArgs { get; set; } = "default";
    public string UninstallCommand { get; set; } = "default";
    public string UpgradeBehavior { get; set; } = "default";
    public string ExpectedExitCodes { get; set; } = "default";
    public string TimeoutSeconds { get; set; } = "default";
}

public sealed class DetectionSourceBreakdown
{
    public string Type { get; set; } = "default";
    public string Path { get; set; } = "default";
    public string ExpectedVersion { get; set; } = "default";
}

public sealed class PolicyTagSourceBreakdown
{
    public string RetryabilityClass { get; set; } = "default";
    public string IdempotencyMode { get; set; } = "default";
    public string RiskLevel { get; set; } = "default";
    public string ApprovalRequired { get; set; } = "default";
}

internal sealed class FlexibleEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var intValue = reader.GetInt32();
            if (Enum.IsDefined(typeof(T), intValue))
            {
                return (T)(object)intValue;
            }

            throw new JsonException($"Value {intValue} is not defined for enum {typeof(T).Name}");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (Enum.TryParse<T>(stringValue, ignoreCase: true, out var result))
            {
                return result;
            }

            throw new JsonException($"Value \"{stringValue}\" is not defined for enum {typeof(T).Name}");
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when parsing enum {typeof(T).Name}");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

[JsonConverter(typeof(FlexibleEnumConverter<ManifestFieldSource>))]
public enum ManifestFieldSource
{
    Admin,
    Template,
    Analyzer,
    Default
}
