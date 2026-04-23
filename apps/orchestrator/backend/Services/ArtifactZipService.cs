using System.IO.Compression;
using System.Text.Json;

namespace DeploymentPoC.Orchestrator.Services;

public sealed class ArtifactZipService
{
    public sealed class ExtractedArtifact
    {
        public Stream MediaStream { get; set; } = Stream.Null;
        public ArtifactIngestManifest Manifest { get; set; } = new();
        public string BaseName { get; set; } = string.Empty;
        public string MediaFileName { get; set; } = string.Empty;
    }

    public sealed class ExtractionResult
    {
        public List<ExtractedArtifact> Artifacts { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public ExtractionResult ExtractAndValidateSingleZip(Stream zipStream)
    {
        var result = new ExtractionResult();

        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.FullName) && !e.FullName.EndsWith('/'))
                .ToList();

            if (entries.Count == 0)
            {
                result.Errors.Add("Zip archive is empty.");
                return result;
            }

            // group by base name
            var groups = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var baseName = GetBaseName(entry.FullName);
                if (!groups.ContainsKey(baseName))
                {
                    groups[baseName] = new List<ZipArchiveEntry>();
                }
                groups[baseName].Add(entry);
            }

            var validPairs = new List<(ZipArchiveEntry Media, ZipArchiveEntry Manifest, string BaseName)>();
            foreach (var group in groups)
            {
                var manifestEntry = group.Value.FirstOrDefault(e =>
                    e.FullName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase));
                var mediaEntries = group.Value
                    .Where(e => !e.FullName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (manifestEntry is null)
                {
                    foreach (var e in group.Value)
                    {
                        result.Errors.Add($"Unpaired file: {e.FullName} (missing manifest)");
                    }
                    continue;
                }

                if (mediaEntries.Count != 1)
                {
                    foreach (var e in group.Value)
                    {
                        result.Errors.Add($"Unpaired file: {e.FullName} (expected exactly one media file per manifest)");
                    }
                    continue;
                }

                validPairs.Add((mediaEntries[0], manifestEntry, group.Key));
            }

            if (validPairs.Count != 1)
            {
                result.Errors.Add($"Expected exactly one artifact pair in zip, found {validPairs.Count}.");
                return result;
            }

            var pair = validPairs[0];
            var manifest = ReadManifest(pair.Manifest);
            if (manifest is null)
            {
                result.Errors.Add("Failed to parse manifest.json.");
                return result;
            }

            var mediaStream = CopyEntryToMemoryStream(pair.Media);

            result.Artifacts.Add(new ExtractedArtifact
            {
                MediaStream = mediaStream,
                Manifest = manifest,
                BaseName = pair.BaseName,
                MediaFileName = pair.Media.FullName
            });
        }
        catch (InvalidDataException ex)
        {
            result.Errors.Add($"Invalid zip archive: {ex.Message}");
        }

        return result;
    }

    public ExtractionResult ExtractAndValidateBulkZip(Stream zipStream)
    {
        var result = new ExtractionResult();

        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.FullName) && !e.FullName.EndsWith('/'))
                .ToList();

            if (entries.Count == 0)
            {
                result.Errors.Add("Zip archive is empty.");
                return result;
            }

            // group by base name
            var groups = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var baseName = GetBaseName(entry.FullName);
                if (!groups.ContainsKey(baseName))
                {
                    groups[baseName] = new List<ZipArchiveEntry>();
                }
                groups[baseName].Add(entry);
            }

            foreach (var group in groups)
            {
                var manifestEntry = group.Value.FirstOrDefault(e =>
                    e.FullName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase));
                var mediaEntries = group.Value
                    .Where(e => !e.FullName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (manifestEntry is null)
                {
                    foreach (var e in group.Value)
                    {
                        result.Errors.Add($"Unpaired file: {e.FullName} (missing manifest)");
                    }
                    continue;
                }

                if (mediaEntries.Count != 1)
                {
                    foreach (var e in group.Value)
                    {
                        result.Errors.Add($"Unpaired file: {e.FullName} (expected exactly one media file per manifest)");
                    }
                    continue;
                }

                var manifest = ReadManifest(manifestEntry);
                if (manifest is null)
                {
                    result.Errors.Add($"Failed to parse manifest for {group.Key}.");
                    continue;
                }

                var mediaStream = CopyEntryToMemoryStream(mediaEntries[0]);

                result.Artifacts.Add(new ExtractedArtifact
                {
                    MediaStream = mediaStream,
                    Manifest = manifest,
                    BaseName = group.Key,
                    MediaFileName = mediaEntries[0].FullName
                });
            }
        }
        catch (InvalidDataException ex)
        {
            result.Errors.Add($"Invalid zip archive: {ex.Message}");
        }

        return result;
    }

    private static string GetBaseName(string fileName)
    {
        // flat root only: reject paths with directories
        var name = Path.GetFileName(fileName);
        if (name.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            return name.Substring(0, name.Length - ".manifest.json".Length);
        }

        // for media files, remove last extension
        var extension = Path.GetExtension(name);
        if (!string.IsNullOrEmpty(extension))
        {
            return name.Substring(0, name.Length - extension.Length);
        }

        return name;
    }

    private static ArtifactIngestManifest? ReadManifest(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<ArtifactIngestManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static MemoryStream CopyEntryToMemoryStream(ZipArchiveEntry entry)
    {
        var ms = new MemoryStream();
        using var stream = entry.Open();
        stream.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
}
