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
            ExecutionLog = new List<string> { "Step1 completed" }
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
        Assert.That(okResult.Value, Is.Not.Null);
    }

    [Test]
    public async Task Install_ReturnsError_WhenPipelineFails()
    {
        var context = new InstallContext
        {
            PackageName = "SQLServer",
            IsSuccessful = false,
            ErrorMessage = "Installation failed",
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

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        Assert.That(okResult.Value, Is.Not.Null);
    }
}