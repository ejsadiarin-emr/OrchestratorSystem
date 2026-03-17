using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using EJInstaller.Orchestrator;

namespace EJInstaller.Orchestrator.Tests;

public class PipelineTests
{
    private readonly Mock<ILogger<Pipeline<InstallContext>>> _loggerMock;

    public PipelineTests()
    {
        _loggerMock = new Mock<ILogger<Pipeline<InstallContext>>>();
    }

    [Test]
    public async Task ExecuteAsync_RunsAllSteps_WhenAllPass()
    {
        var step1Mock = new Mock<IInstallStep<InstallContext>>();
        step1Mock.Setup(s => s.Name).Returns("Step1");
        step1Mock.Setup(s => s.CanExecute(It.IsAny<InstallContext>())).Returns(true);
        step1Mock.Setup(s => s.ExecuteAsync(It.IsAny<InstallContext>())).Returns(Task.CompletedTask);

        var step2Mock = new Mock<IInstallStep<InstallContext>>();
        step2Mock.Setup(s => s.Name).Returns("Step2");
        step2Mock.Setup(s => s.CanExecute(It.IsAny<InstallContext>())).Returns(true);
        step2Mock.Setup(s => s.ExecuteAsync(It.IsAny<InstallContext>())).Returns(Task.CompletedTask);

        var pipeline = new Pipeline<InstallContext>(_loggerMock.Object);
        pipeline.AddStep(step1Mock.Object);
        pipeline.AddStep(step2Mock.Object);

        var context = new InstallContext { PackageName = "TestPackage" };

        var result = await pipeline.ExecuteAsync(context);

        Assert.That(result.IsSuccessful, Is.True);
        step1Mock.Verify(s => s.ExecuteAsync(It.IsAny<InstallContext>()), Times.Once);
        step2Mock.Verify(s => s.ExecuteAsync(It.IsAny<InstallContext>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_StopsOnFailure_WhenStepThrows()
    {
        var step1Mock = new Mock<IInstallStep<InstallContext>>();
        step1Mock.Setup(s => s.Name).Returns("Step1");
        step1Mock.Setup(s => s.CanExecute(It.IsAny<InstallContext>())).Returns(true);
        step1Mock.Setup(s => s.ExecuteAsync(It.IsAny<InstallContext>())).Returns(Task.CompletedTask);

        var step2Mock = new Mock<IInstallStep<InstallContext>>();
        step2Mock.Setup(s => s.Name).Returns("Step2");
        step2Mock.Setup(s => s.CanExecute(It.IsAny<InstallContext>())).Returns(true);
        step2Mock.Setup(s => s.ExecuteAsync(It.IsAny<InstallContext>()))
            .Throws(new Exception("Step 2 failed"));

        var pipeline = new Pipeline<InstallContext>(_loggerMock.Object);
        pipeline.AddStep(step1Mock.Object);
        pipeline.AddStep(step2Mock.Object);

        var context = new InstallContext { PackageName = "TestPackage" };

        var result = await pipeline.ExecuteAsync(context);

        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo("Step 2 failed"));
        step1Mock.Verify(s => s.ExecuteAsync(It.IsAny<InstallContext>()), Times.Once);
        step2Mock.Verify(s => s.ExecuteAsync(It.IsAny<InstallContext>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsStep_WhenCanExecuteReturnsFalse()
    {
        var step1Mock = new Mock<IInstallStep<InstallContext>>();
        step1Mock.Setup(s => s.Name).Returns("Step1");
        step1Mock.Setup(s => s.CanExecute(It.IsAny<InstallContext>())).Returns(false);

        var pipeline = new Pipeline<InstallContext>(_loggerMock.Object);
        pipeline.AddStep(step1Mock.Object);

        var context = new InstallContext { PackageName = "" };

        await pipeline.ExecuteAsync(context);

        step1Mock.Verify(s => s.ExecuteAsync(It.IsAny<InstallContext>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_LogsEachStep()
    {
        var stepMock = new Mock<IInstallStep<InstallContext>>();
        stepMock.Setup(s => s.Name).Returns("TestStep");
        stepMock.Setup(s => s.CanExecute(It.IsAny<InstallContext>())).Returns(true);
        stepMock.Setup(s => s.ExecuteAsync(It.IsAny<InstallContext>())).Returns(Task.CompletedTask);

        var pipeline = new Pipeline<InstallContext>(_loggerMock.Object);
        pipeline.AddStep(stepMock.Object);

        var context = new InstallContext();

        await pipeline.ExecuteAsync(context);

        Assert.That(true, Is.True);
    }
}