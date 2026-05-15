using DeploymentPoC.Orchestrator.Models;
using DeploymentPoC.Orchestrator.Services;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DeploymentPoC.Orchestrator.Tests.Services;

[TestFixture]
public class UploadSessionServiceTests
{
    private string _tempRoot = null!;
    private Mock<IConfiguration> _configMock = null!;
    private UploadSessionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "upload-test-" + Guid.NewGuid().ToString("N"));
        _configMock = new Mock<IConfiguration>();
        _configMock.Setup(c => c["ArtifactStore:RootPath"]).Returns(_tempRoot);
        _service = new UploadSessionService(_configMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public void CreateSession_ReturnsSession_WithTempDirectory()
    {
        var session = _service.CreateSession(null);

        Assert.That(session, Is.Not.Null);
        Assert.That(session.SessionId, Is.Not.Empty);
        Assert.That(session.CreatedAtUtc, Is.LessThanOrEqualTo(DateTime.UtcNow));
        Assert.That(session.TempDirectory, Does.StartWith(_tempRoot));
        Assert.That(Directory.Exists(session.TempDirectory), Is.True);
    }

    [Test]
    public void CreateSession_WithManifest_StoresManifest()
    {
        var manifest = new ArtifactIngestManifest
        {
            PackageId = "test-pkg",
            Version = "1.0.0"
        };

        var session = _service.CreateSession(manifest);

        Assert.That(session.Manifest, Is.Not.Null);
        Assert.That(session.Manifest!.PackageId, Is.EqualTo("test-pkg"));
        Assert.That(session.Manifest.Version, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void GetSession_ReturnsSession_WhenExists()
    {
        var session = _service.CreateSession(null);
        var retrieved = _service.GetSession(session.SessionId);

        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved!.SessionId, Is.EqualTo(session.SessionId));
    }

    [Test]
    public void GetSession_ReturnsNull_WhenNotFound()
    {
        Assert.That(_service.GetSession("nonexistent"), Is.Null);
    }

    [Test]
    public async Task ReceiveChunk_WritesChunkFile_AndTracksIndex()
    {
        var session = _service.CreateSession(null);
        var chunkData = new byte[] { 1, 2, 3, 4, 5 };
        using var stream = new MemoryStream(chunkData);

        await _service.ReceiveChunk(session.SessionId, 0, 3, stream);

        Assert.That(session.TotalChunks, Is.EqualTo(3));
        Assert.That(session.ReceivedChunks, Has.Member(0));

        var chunkPath = Path.Combine(session.TempDirectory, "chunk_0");
        Assert.That(File.Exists(chunkPath), Is.True);
        var savedData = await File.ReadAllBytesAsync(chunkPath);
        Assert.That(savedData, Is.EqualTo(chunkData));
    }

    [Test]
    public void ReceiveChunk_Throws_WhenSessionNotFound()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.ReceiveChunk("nonexistent", 0, 1, stream));

        Assert.That(ex!.Message, Is.EqualTo("Upload session not found."));
    }

    [Test]
    public async Task ReceiveChunk_Throws_WhenSessionAlreadyComplete()
    {
        var session = _service.CreateSession(null);
        session.IsComplete = true;

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.ReceiveChunk(session.SessionId, 0, 1, stream));

        Assert.That(ex!.Message, Is.EqualTo("Upload session is already complete."));
    }

    [Test]
    public async Task CompleteSession_AssemblesChunks_InOrder_AndCleansUp()
    {
        var session = _service.CreateSession(null);
        var data0 = new byte[] { 1, 2, 3 };
        var data1 = new byte[] { 4, 5, 6 };
        var data2 = new byte[] { 7, 8, 9 };

        await _service.ReceiveChunk(session.SessionId, 0, 3, new MemoryStream(data0));
        await _service.ReceiveChunk(session.SessionId, 1, 3, new MemoryStream(data1));
        await _service.ReceiveChunk(session.SessionId, 2, 3, new MemoryStream(data2));

        var assembledPath = _service.CompleteSession(session.SessionId);

        Assert.That(File.Exists(assembledPath), Is.True);
        var assembled = await File.ReadAllBytesAsync(assembledPath);
        Assert.That(assembled, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        Assert.That(session.IsComplete, Is.True);
        Assert.That(session.FinalFilePath, Is.EqualTo(assembledPath));

        for (int i = 0; i < 3; i++)
        {
            Assert.That(File.Exists(Path.Combine(session.TempDirectory, $"chunk_{i}")), Is.False,
                $"Chunk {i} should be cleaned up");
        }
    }

    [Test]
    public void CompleteSession_Throws_WhenSessionNotFound()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _service.CompleteSession("nonexistent"));
        Assert.That(ex!.Message, Is.EqualTo("Upload session not found."));
    }

    [Test]
    public async Task CompleteSession_Throws_WhenChunkMissing()
    {
        var session = _service.CreateSession(null);
        await _service.ReceiveChunk(session.SessionId, 0, 3, new MemoryStream(new byte[] { 1, 2, 3 }));
        await _service.ReceiveChunk(session.SessionId, 2, 3, new MemoryStream(new byte[] { 7, 8, 9 }));

        var ex = Assert.Throws<InvalidOperationException>(() => _service.CompleteSession(session.SessionId));
        Assert.That(ex!.Message, Is.EqualTo("Chunk 1 is missing."));
    }

    [Test]
    public async Task CompleteSession_ReturnsCachedPath_WhenAlreadyComplete()
    {
        var session = _service.CreateSession(null);
        await _service.ReceiveChunk(session.SessionId, 0, 1, new MemoryStream(new byte[] { 1 }));
        var firstPath = _service.CompleteSession(session.SessionId);
        var secondPath = _service.CompleteSession(session.SessionId);

        Assert.That(secondPath, Is.EqualTo(firstPath));
    }

    [Test]
    public void DeleteSession_RemovesSession_AndTempDirectory()
    {
        var session = _service.CreateSession(null);
        Assert.That(Directory.Exists(session.TempDirectory), Is.True);

        _service.DeleteSession(session.SessionId);

        Assert.That(_service.GetSession(session.SessionId), Is.Null);
        Assert.That(Directory.Exists(session.TempDirectory), Is.False);
    }

    [Test]
    public void DeleteSession_DoesNotThrow_WhenSessionNotFound()
    {
        Assert.DoesNotThrow(() => _service.DeleteSession("nonexistent"));
    }
}
