using DeploymentPoC.Contracts.Runtime.Probes;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace DeploymentPoC.Agent.Steps;

public static class DetectEndpointHandler
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task HandleAsync(HttpContext context)
    {
        DetectRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<DetectRequest>(
                context.Request.Body, Options, context.RequestAborted);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = 400;
            return;
        }

        if (request is null)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var results = new List<PackageDetectionResult>();

        foreach (var pkg in request.Packages)
        {
            var detectResult = await PackageDetector.DetectAsync(pkg.Detection, context.RequestAborted);
            results.Add(new PackageDetectionResult
            {
                PackageId = pkg.PackageId,
                Name = pkg.Name,
                Status = MapStatus(detectResult.Status),
                ActualVersion = detectResult.ActualVersion
            });
        }

        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        DiskInfo diskInfo;
        try
        {
            var drive = new DriveInfo(systemDrive);
            diskInfo = new DiskInfo
            {
                FreeBytes = drive.AvailableFreeSpace,
                TotalBytes = drive.TotalSize,
                Drive = drive.Name
            };
        }
        catch
        {
            diskInfo = new DiskInfo { Drive = systemDrive };
        }

        var response = new NodeDetectResponse
        {
            Results = results,
            DiskInfo = diskInfo
        };

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, response, Options, context.RequestAborted);
    }

    private static DeploymentPoC.Contracts.Runtime.Probes.PreCheckStatus MapStatus(PreCheckStatus status)
    {
        return status switch
        {
            PreCheckStatus.AlreadySatisfied => DeploymentPoC.Contracts.Runtime.Probes.PreCheckStatus.AlreadySatisfied,
            PreCheckStatus.WrongVersion => DeploymentPoC.Contracts.Runtime.Probes.PreCheckStatus.WrongVersion,
            PreCheckStatus.NotPresent => DeploymentPoC.Contracts.Runtime.Probes.PreCheckStatus.NotPresent,
            _ => DeploymentPoC.Contracts.Runtime.Probes.PreCheckStatus.NotPresent
        };
    }
}
