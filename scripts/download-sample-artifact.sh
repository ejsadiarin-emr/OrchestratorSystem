#!/usr/bin/env bash
# Download real artifacts for testing artifact upload
# All downloads are cached in .artifact-cache/ (gitignored)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CACHE_DIR="$REPO_ROOT/.artifact-cache"

mkdir -p "$CACHE_DIR"

# --- Go (golang) ---
GO1259="$CACHE_DIR/Go_1.25.9.msi"
if [ ! -f "$GO1259" ]; then
    echo "Downloading Go 1.25.9 ..."
    curl -fsSL -o "$GO1259" "https://go.dev/dl/go1.25.9.windows-amd64.msi"
    echo "Saved: $GO1259"
else
    echo "Already exists: $GO1259"
fi

GO1262="$CACHE_DIR/Go_1.26.2.msi"
if [ ! -f "$GO1262" ]; then
    echo "Downloading Go 1.26.2 ..."
    curl -fsSL -o "$GO1262" "https://go.dev/dl/go1.26.2.windows-amd64.msi"
    echo "Saved: $GO1262"
else
    echo "Already exists: $GO1262"
fi

# --- SSMS (SQL Server Management Studio) ---
# NOTE: SSMS 22+ uses a Visual Studio Installer bootstrapper (vs_SSMS.exe), not a standalone MSI.
# For silent installation: vs_SSMS.exe --quiet --norestart --wait
# Reference: https://learn.microsoft.com/en-us/sql/ssms/install/install

SSMS22="$CACHE_DIR/SSMS_22.0.exe"
if [ ! -f "$SSMS22" ]; then
    echo "Downloading SSMS 22 (Visual Studio Installer bootstrapper)..."
    curl -fsSL -o "$SSMS22" "https://aka.ms/ssms/22/release/vs_SSMS.exe"
    echo "Saved: $SSMS22 (bootstrapper, not MSI)"
else
    echo "Already exists: $SSMS22"
fi

# NOTE: Microsoft does not provide stable direct-download URLs for older SSMS versions (19.x/20.x).
# SSMS 19/20 used SSMS-Setup-ENU.exe with /Install /Quiet /NoRestart switches.
# To obtain an older SSMS installer, download manually from the SSMS Release History:
# https://learn.microsoft.com/en-us/sql/ssms/release-notes-ssms
#
# For testing with 2 SSMS versions, you can:
# 1. Download SSMS 19.x from the release history page
# 2. Rename it to match the PackageId_Version.ext format, e.g., SSMS_19.3.exe
# 3. Place it in .artifact-cache/ alongside the other files

echo ""
echo "=== Download Summary ==="
echo "Go 1.25.9:     $GO1259"
echo "Go 1.26.2:     $GO1262"
echo "SSMS 22:       $SSMS22 (bootstrapper)"
echo ""
echo "Upload examples:"
echo "  curl -X POST http://localhost:5000/api/artifacts/upload -F \"packageId=Go\" -F \"version=1.25.9\" -F \"packageName=Go\" -F \"file=@$GO1259\""
echo "  curl -X POST http://localhost:5000/api/artifacts/upload -F \"packageId=Go\" -F \"version=1.26.2\" -F \"packageName=Go\" -F \"file=@$GO1262\""
echo "  curl -X POST http://localhost:5000/api/artifacts/import -F \"files=@$GO1259\" -F \"files=@$GO1262\""
