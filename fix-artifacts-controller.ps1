$file = "C:\Users\E1560951\DeploymentPoC\apps\orchestrator\backend\Controllers\ArtifactsController.cs"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::Unicode)

# Replacement 1: zip path in Ingest
$old1 = @'
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
'@
$new1 = "            await UpsertPackageEntityAsync(result.ResolvedManifest!, HttpContext.RequestAborted);"
if ($content.Contains($old1)) {
    $content = $content.Replace($old1, $new1)
    Write-Host "Replacement 1 applied."
} else {
    Write-Host "Replacement 1 NOT found."
}

# Replacement 2: non-zip path in Ingest
$old2 = @'
            var packageEntityId2 = DeterministicGuid($"{packageId2}-{version2}");
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
            }
'@
$new2 = "            await UpsertPackageEntityAsync(ingestResult.ResolvedManifest!, HttpContext.RequestAborted);"
if ($content.Contains($old2)) {
    $content = $content.Replace($old2, $new2)
    Write-Host "Replacement 2 applied."
} else {
    Write-Host "Replacement 2 NOT found."
}

# Replacement 3: ProcessBulkArtifactsAsync
$old3 = @'
            var packageEntityId = DeterministicGuid($"{packageId}-{version}");
            var existingPackage = await _db.Packages.SingleOrDefaultAsync(p => p.PackageId == packageEntityId, cancellationToken);
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
                await _db.SaveChangesAsync(cancellationToken);
            }
'@
$new3 = "            await UpsertPackageEntityAsync(result.ResolvedManifest!, cancellationToken);"
if ($content.Contains($old3)) {
    $content = $content.Replace($old3, $new3)
    Write-Host "Replacement 3 applied."
} else {
    Write-Host "Replacement 3 NOT found."
}

# Replacement 4: CompleteUploadSession
$old4 = @'
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
'@
$new4 = "            await UpsertPackageEntityAsync(result.ResolvedManifest!, HttpContext.RequestAborted);"
if ($content.Contains($old4)) {
    $content = $content.Replace($old4, $new4)
    Write-Host "Replacement 4 applied."
} else {
    Write-Host "Replacement 4 NOT found."
}

[System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::Unicode)
Write-Host "Done."
