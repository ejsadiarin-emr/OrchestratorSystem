using System.Text;
using System.Text.Json;
using DeploymentPoC.Agent.Steps;
using DeploymentPoC.Contracts.Runtime.Probes;
using DeploymentPoC.Contracts.Runtime.RunPayloads;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using ProbesPreCheckStatus = DeploymentPoC.Contracts.Runtime.Probes.PreCheckStatus;

namespace DeploymentPoC.Agent.Tests;

public sealed class DetectEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static DefaultHttpContext CreateContext(DetectRequest request)
    {
        var context = new DefaultHttpContext();
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<DetectResponse> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<DetectResponse>(json, JsonOptions)!;
    }

    [Test]
    public async Task ValidRequest_WithMultiplePackages_ReturnsPerPackageResults()
    {
        var request = new DetectRequest
        {
            Packages = new List<PackageDetectionRequest>
            {
                new() { PackageId = Guid.NewGuid(), Name = "pkg-a", Version = "1.0.0", Detection = new DetectionConfig { Type = "file", Path = "cmd.exe" } },
                new() { PackageId = Guid.NewGuid(), Name = "pkg-b", Version = "1.0.0", Detection = new DetectionConfig { Type = "file", Path = "nonexistent-file-xyz.abc" } }
            }
        };

        var context = CreateContext(request);
        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Results.Count, Is.EqualTo(2));

        Assert.That(response.Results[0].Name, Is.EqualTo("pkg-a"));
        Assert.That(response.Results[1].Name, Is.EqualTo("pkg-b"));

        Assert.That(response.DiskInfo, Is.Not.Null);
        Assert.That(response.DiskInfo.Drive, Is.Not.Null);
    }

    [Test]
    public async Task EmptyPackagesArray_Returns200WithEmptyResults()
    {
        var request = new DetectRequest { Packages = new List<PackageDetectionRequest>() };
        var context = CreateContext(request);

        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Results, Is.Empty);
        Assert.That(response.DiskInfo, Is.Not.Null);
    }

    [Test]
    public async Task PackageWithFileDetectionType_ReturnsCorrectStatusAndVersion()
    {
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var request = new DetectRequest
        {
            Packages = new List<PackageDetectionRequest>
            {
                new()
                {
                    PackageId = Guid.NewGuid(),
                    Name = "cmd",
                    Version = "1.0.0",
                    Detection = new DetectionConfig
                    {
                        Type = "file",
                        Path = cmdPath
                    }
                }
            }
        };

        var context = CreateContext(request);
        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo(ProbesPreCheckStatus.AlreadySatisfied));
    }

    [Test]
    public async Task PackageWithVersionManifestDetectionType_ReturnsCorrectStatus()
    {
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var request = new DetectRequest
        {
            Packages = new List<PackageDetectionRequest>
            {
                new()
                {
                    PackageId = Guid.NewGuid(),
                    Name = "cmd-manifest",
                    Version = "1.0.0",
                    Detection = new DetectionConfig
                    {
                        Type = "version_manifest",
                        Path = cmdPath
                    }
                }
            }
        };

        var context = CreateContext(request);
        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo(ProbesPreCheckStatus.AlreadySatisfied));
    }

    [Test]
    public async Task PackageWithVersionManifest_WithMatchingVersion_ReturnsAlreadySatisfied()
    {
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var actualVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(cmdPath).FileVersion ?? "10.0";

        var request = new DetectRequest
        {
            Packages = new List<PackageDetectionRequest>
            {
                new()
                {
                    PackageId = Guid.NewGuid(),
                    Name = "cmd-match",
                    Version = actualVersion,
                    Detection = new DetectionConfig
                    {
                        Type = "version_manifest",
                        Path = cmdPath
                    }
                }
            }
        };

        var context = CreateContext(request);
        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo(ProbesPreCheckStatus.AlreadySatisfied));
    }

    [Test]
    public async Task PackageWithRegistryDetectionType_ReturnsNotPresent()
    {
        var request = new DetectRequest
        {
            Packages = new List<PackageDetectionRequest>
            {
                new()
                {
                    PackageId = Guid.NewGuid(),
                    Name = "registry-pkg",
                    Version = "1.0.0",
                    Detection = new DetectionConfig
                    {
                        Type = "registry",
                        Path = @"HKLM\Software\Test"
                    }
                }
            }
        };

        var context = CreateContext(request);
        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo(ProbesPreCheckStatus.NotPresent));
    }

    [Test]
    public async Task NullBody_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("invalid json"));
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task NonExistentFile_WithVersionManifest_ReturnsNotPresent()
    {
        var request = new DetectRequest
        {
            Packages = new List<PackageDetectionRequest>
            {
                new()
                {
                    PackageId = Guid.NewGuid(),
                    Name = "missing",
                    Version = "1.0.0",
                    Detection = new DetectionConfig
                    {
                        Type = "version_manifest",
                        Path = "nonexistent-command-xyz123"
                    }
                }
            }
        };

        var context = CreateContext(request);
        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo(ProbesPreCheckStatus.NotPresent));
    }

    [Test]
    public async Task UnsupportedDetectionType_ReturnsNotPresent()
    {
        var request = new DetectRequest
        {
            Packages = new List<PackageDetectionRequest>
            {
                new()
                {
                    PackageId = Guid.NewGuid(),
                    Name = "unsupported",
                    Version = "1.0.0",
                    Detection = new DetectionConfig
                    {
                        Type = "unknown_type",
                        Path = "test"
                    }
                }
            }
        };

        var context = CreateContext(request);
        await DetectEndpointHandler.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));

        var response = await ReadResponseAsync(context);
        Assert.That(response.Results, Has.Count.EqualTo(1));
        Assert.That(response.Results[0].Status, Is.EqualTo(ProbesPreCheckStatus.NotPresent));
    }

    private sealed class DetectResponse
    {
        public List<PackageDetectionResult> Results { get; set; } = new();
        public DiskInfo DiskInfo { get; set; } = new();
    }
}
