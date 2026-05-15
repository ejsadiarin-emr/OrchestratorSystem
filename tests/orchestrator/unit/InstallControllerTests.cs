using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using DeploymentPoC.Orchestrator;
using DeploymentPoC.Orchestrator.Controllers;

namespace DeploymentPoC.Orchestrator.Tests;

public class InstallControllerTests
{
    private readonly Mock<IPipeline<InstallContext>> _pipelineMock;
    private readonly Mock<ILogger<InstallController>> _loggerMock;
    private readonly InstallController _controller;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public InstallControllerTests()
    {
        _pipelineMock = new Mock<IPipeline<InstallContext>>();
        _loggerMock = new Mock<ILogger<InstallController>>();
        _controller = new InstallController(_pipelineMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task Install_ReturnsSuccess_WhenPipelineSucceeds()
    {
        var context = new InstallContext
        {
            PackageName = "SQLServer",
            TargetMachine = "WORKSTATION-01",
            Version = "2022",
            IsSuccessful = true,
            ExecutionLog = new List<string> { "Step1 completed", "Step2 completed" }
        };

        _pipelineMock
            .Setup(p => p.ExecuteAsync(It.IsAny<InstallContext>()))
            .ReturnsAsync(context);

        var request = new InstallRequest
        {
            PackageName = "SQLServer",
            TargetMachine = "WORKSTATION-01",
            Version = "2022"
        };

        var result = await _controller.Install(request);

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        var response = DeserializeResponse(okResult.Value);
        Assert.That(response.IsSuccessful, Is.True);
        Assert.That(response.ErrorMessage, Is.Null);
        Assert.That(response.ExecutionLog, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Install_ReturnsBadRequest_WhenPipelineFails()
    {
        var context = new InstallContext
        {
            PackageName = "SQLServer",
            TargetMachine = "WORKSTATION-01",
            Version = "2022",
            IsSuccessful = false,
            ErrorMessage = "Installation failed: disk full",
            ExecutionLog = new List<string> { "Step1 failed" }
        };

        _pipelineMock
            .Setup(p => p.ExecuteAsync(It.IsAny<InstallContext>()))
            .ReturnsAsync(context);

        var request = new InstallRequest
        {
            PackageName = "SQLServer",
            TargetMachine = "WORKSTATION-01",
            Version = "2022"
        };

        var result = await _controller.Install(request);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        var badRequestResult = (BadRequestObjectResult)result;
        Assert.That(badRequestResult.StatusCode, Is.EqualTo(400));
        var response = DeserializeResponse(badRequestResult.Value);
        Assert.That(response.IsSuccessful, Is.False);
        Assert.That(response.ErrorMessage, Is.EqualTo("Installation failed: disk full"));
        Assert.That(response.ExecutionLog, Has.Count.EqualTo(1));
    }

    private static InstallResponse DeserializeResponse(object? value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<InstallResponse>(json, _jsonOptions)!;
    }

    private record InstallResponse
    {
        public bool IsSuccessful { get; init; }
        public string? ErrorMessage { get; init; }
        public List<string> ExecutionLog { get; init; } = new();
    }
}