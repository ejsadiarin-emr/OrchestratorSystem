import re

file_path = r"C:\Users\E1560951\DeploymentPoC\apps\orchestrator\backend\Controllers\ArtifactsController.cs"
with open(file_path, "r", encoding="utf-16-le") as f:
    content = f.read()

# Block 2: non-zip path in Ingest (8 spaces indentation)
old2 = '''        var packageEntityId2 = DeterministicGuid($"{packageId2}-{version2}");
        var existingPackage2 = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId2, HttpContext.RequestAborted);
        if (existingPackage2 is null)
        {
            _db.Packages.Add(new PackageEntity
            {
                PackageId = packageEntityId2,
                Name = ingestResult.ResolvedManifest.PackageId,
                Version = ingestResult.ResolvedManifest.Version,
                SourcePath = ingestResult.ResolvedManifest.InstallAdapter?.Command ?? string.Empty,
                InstallType = ingestResult.ResolvedManifest.InstallAdapter?.Type ?? "exe",
                InstallArgs = ingestResult.ResolvedManifest.InstallAdapter?.Arguments ?? string.Empty,
                ExpectedExitCodesJson = JsonSerializer.Serialize(
                    ingestResult.ResolvedManifest.InstallAdapter?.ExpectedExitCodes ?? new List<int> { 0, 3010 }),
                DetectionConfigJson = JsonSerializer.Serialize(ingestResult.ResolvedManifest.Detection),
                TimeoutSeconds = ingestResult.ResolvedManifest.InstallAdapter?.TimeoutSeconds ?? 300,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(HttpContext.RequestAborted);
        }'''
new2 = "        await UpsertPackageEntityAsync(ingestResult.ResolvedManifest!, HttpContext.RequestAborted);"

if old2 in content:
    content = content.replace(old2, new2)
    print("Replacement 2 applied.")
else:
    print("Replacement 2 NOT found.")

# Block 4: CompleteUploadSession - find by surrounding unique text
# Find the text after SaveArtifactAndManifestAsync in CompleteUploadSession method
marker4 = '''            await _artifactStore.SaveArtifactAndManifestAsync(packageId, version, stream, resolvedManifestJson, HttpContext.RequestAborted, fileName: Path.GetFileName(assembledPath));

            var packageEntityId = DeterministicGuid($"{packageId}-{version}");
            var existingPackage = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId, HttpContext.RequestAborted);
            if (existingPackage is null)
            {
                _db.Packages.Add(new PackageEntity
                {
                    PackageId = packageEntityId,
                    Name = result.ResolvedManifest.PackageId,
                    Version = result.ResolvedManifest.Version,
                    SourcePath = result.ResolvedManifest.InstallAdapter?.Command ?? string.Empty,
                    InstallType = result.ResolvedManifest.InstallAdapter?.Type ?? "exe",
                    InstallArgs = result.ResolvedManifest.InstallAdapter?.Arguments ?? string.Empty,
                    ExpectedExitCodesJson = JsonSerializer.Serialize(
                        result.ResolvedManifest.InstallAdapter?.ExpectedExitCodes ?? new List<int> { 0, 3010 }),
                    DetectionConfigJson = JsonSerializer.Serialize(result.ResolvedManifest.Detection),
                    TimeoutSeconds = result.ResolvedManifest.InstallAdapter?.TimeoutSeconds ?? 300,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(HttpContext.RequestAborted);
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                resolvedManifest = result.ResolvedManifest,'''

new4 = '''            await _artifactStore.SaveArtifactAndManifestAsync(packageId, version, stream, resolvedManifestJson, HttpContext.RequestAborted, fileName: Path.GetFileName(assembledPath));

            await UpsertPackageEntityAsync(result.ResolvedManifest!, HttpContext.RequestAborted);

            return StatusCode(StatusCodes.Status201Created, new
            {
                resolvedManifest = result.ResolvedManifest,'''

if marker4 in content:
    content = content.replace(marker4, new4)
    print("Replacement 4 applied.")
else:
    print("Replacement 4 NOT found.")

with open(file_path, "w", encoding="utf-16-le") as f:
    f.write(content)

print("Done.")
