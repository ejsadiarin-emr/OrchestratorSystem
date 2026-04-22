using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.Tests.Controllers;

public class ArtifactsControllerGetAllTests
{
    private ArtifactStoreService CreateStoreService(string rootPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactStore:RootPath"] = rootPath
            })
            .Build();
        return new ArtifactStoreService(config);
    }

    [Test]
    public void GetAll_ReturnsArtifactsFromService()
    {
        var storeService = CreateStoreService("/tmp/non-existent-store-for-controller-test");
        var ingestService = new ArtifactIngestService();
        var controller = new ArtifactsController(storeService, ingestService);

        var result = controller.GetAll();

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var returned = okResult.Value as List<ArtifactStoreService.ArtifactListItem>;
        Assert.That(returned, Is.Not.Null);
        Assert.That(returned, Is.Empty);
    }
}