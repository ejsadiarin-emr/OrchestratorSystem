using DeploymentPoC.Orchestrator.Controllers;
using DeploymentPoC.Orchestrator.Data;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        var uploadSession = new UploadSessionService(new ConfigurationBuilder().Build());
        var artifactZip = new ArtifactZipService();

        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<InstallerDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new InstallerDbContext(options);
        db.Database.EnsureCreated();

        var controller = new ArtifactsController(storeService, ingestService, uploadSession, artifactZip, db);

        var result = controller.GetAll();

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var returned = okResult!.Value as List<ArtifactStoreService.ArtifactListItem>;
        Assert.That(returned, Is.Not.Null);
        Assert.That(returned, Is.Empty);
    }
}