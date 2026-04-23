using System.Collections.Concurrent;
using DeploymentPoC.Orchestrator.Models;
using Microsoft.Extensions.Configuration;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class UploadSessionService
{
    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new();
    private readonly string _artifactRoot;

    public UploadSessionService(IConfiguration configuration)
    {
        var configuredRoot = configuration["ArtifactStore:RootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "artifacts");

        _artifactRoot = Path.GetFullPath(configuredRoot);
    }

    public UploadSession CreateSession(ArtifactIngestManifest? manifest)
    {
        var session = new UploadSession
        {
            Manifest = manifest,
            TempDirectory = Path.Combine(_artifactRoot, "_temp", Guid.NewGuid().ToString("N"))
        };

        Directory.CreateDirectory(session.TempDirectory);
        _sessions[session.SessionId] = session;
        return session;
    }

    public UploadSession? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    public async Task ReceiveChunk(string sessionId, int index, int totalChunks, Stream chunkStream)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException("Upload session not found.");
        }

        if (session.IsComplete)
        {
            throw new InvalidOperationException("Upload session is already complete.");
        }

        session.TotalChunks = totalChunks;
        var chunkPath = Path.Combine(session.TempDirectory, $"chunk_{index}");

        await using var file = File.Create(chunkPath);
        await chunkStream.CopyToAsync(file);

        session.ReceivedChunks.Add(index);
    }

    public string CompleteSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException("Upload session not found.");
        }

        if (session.IsComplete)
        {
            return session.FinalFilePath!;
        }

        var assembledPath = Path.Combine(session.TempDirectory, "assembled");
        using (var assembled = File.Create(assembledPath))
        {
            for (int i = 0; i < session.TotalChunks; i++)
            {
                var chunkPath = Path.Combine(session.TempDirectory, $"chunk_{i}");
                if (!File.Exists(chunkPath))
                {
                    throw new InvalidOperationException($"Chunk {i} is missing.");
                }

                using var chunk = File.OpenRead(chunkPath);
                chunk.CopyTo(assembled);
            }
        }

        session.FinalFilePath = assembledPath;
        session.IsComplete = true;

        // clean up individual chunk files after assembly
        for (int i = 0; i < session.TotalChunks; i++)
        {
            var chunkPath = Path.Combine(session.TempDirectory, $"chunk_{i}");
            try
            {
                if (File.Exists(chunkPath))
                {
                    File.Delete(chunkPath);
                }
            }
            catch
            {
                // never fail cleanup
            }
        }

        return assembledPath;
    }

    public void DeleteSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            try
            {
                if (Directory.Exists(session.TempDirectory))
                {
                    Directory.Delete(session.TempDirectory, recursive: true);
                }
            }
            catch
            {
                // never fail cleanup
            }
        }
    }
}
