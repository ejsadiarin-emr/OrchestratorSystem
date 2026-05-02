import re

file_path = r"C:\Users\E1560951\DeploymentPoC\apps\orchestrator\backend\Controllers\ArtifactsController.cs"
with open(file_path, "r", encoding="utf-16-le") as f:
    content = f.read()

# Fix 1: zip path return statement
old1 = '''            {
                resolvedManifest = result.ResolvedManifest,
                packageEntityId
            });'''
new1 = '''            {
                resolvedManifest = result.ResolvedManifest,
                packageEntityId = DeterministicGuid($"{packageId}-{version}")
            });'''
content = content.replace(old1, new1)

# Fix 2: non-zip path return statement
old2 = '''        {
            resolvedManifest = ingestResult.ResolvedManifest,
            packageEntityId = packageEntityId2
        });'''
new2 = '''        {
            resolvedManifest = ingestResult.ResolvedManifest,
            packageEntityId = DeterministicGuid($"{packageId2}-{version2}")
        });'''
content = content.replace(old2, new2)

# Fix 3: CompleteUploadSession return statement
old3 = '''            {
                resolvedManifest = result.ResolvedManifest,
                packageEntityId
            });'''
new3 = '''            {
                resolvedManifest = result.ResolvedManifest,
                packageEntityId = DeterministicGuid($"{packageId}-{version}")
            });'''
content = content.replace(old3, new3)

with open(file_path, "w", encoding="utf-16-le") as f:
    f.write(content)

print("Done.")
