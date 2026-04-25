#!/usr/bin/env bash
set -euo pipefail

# download-test-artifacts.sh
# Downloads installer binaries and generates manifests for test artifacts.
# Creates a zip bundle similar to artifact-bulk.zip but with different versions.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="$PROJECT_DIR/test-artifacts"
TEMP_DIR="$(mktemp -d)"

# Versions for the alternative test artifacts
GIT_VERSION="2.47.1"
NODE_VERSION="22.14.0"
PYTHON_VERSION="3.13.3"
ZIP_VERSION="24.09"

# Filenames
GIT_EXE="Git-${GIT_VERSION}-64-bit.exe"
NODE_MSI="node-v${NODE_VERSION}-x64.msi"
PYTHON_EXE="python-${PYTHON_VERSION}-amd64.exe"
ZIP_EXE="7z$(echo "$ZIP_VERSION" | tr -d '.')-x64.exe"

# Download URLs
GIT_URL="https://github.com/git-for-windows/git/releases/download/v${GIT_VERSION}.windows.1/${GIT_EXE}"
NODE_URL="https://nodejs.org/dist/v${NODE_VERSION}/${NODE_MSI}"
PYTHON_URL="https://www.python.org/ftp/python/${PYTHON_VERSION}/${PYTHON_EXE}"
ZIP_URL="https://www.7-zip.org/a/${ZIP_EXE}"

echo "=== Downloading test artifacts to $TEMP_DIR ==="
cd "$TEMP_DIR"

download_file() {
  local url="$1"
  local filename="$2"
  echo "Downloading $filename ..."
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL -o "$filename" "$url"
  elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$filename" "$url"
  else
    echo "Error: neither curl nor wget is available." >&2
    exit 1
  fi
  echo "  -> $(ls -lh "$filename" | awk '{print $5}')"
}

download_file "$GIT_URL" "$GIT_EXE"
download_file "$NODE_URL" "$NODE_MSI"
download_file "$PYTHON_URL" "$PYTHON_EXE"
download_file "$ZIP_URL" "$ZIP_EXE"

echo ""
echo "=== Generating manifest files ==="

# Git manifest
cat > "${GIT_EXE}.manifest.json" <<EOF
{
  "packageId": "git",
  "version": "${GIT_VERSION}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${GIT_EXE}",
    "arguments": "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /COMPONENTS=\"icons,extreg,shellassoc\"",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF

# Node.js manifest
cat > "${NODE_MSI}.manifest.json" <<EOF
{
  "packageId": "nodejs",
  "version": "${NODE_VERSION}",
  "channel": "stable",
  "artifactType": "msi",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "msi",
    "command": "${NODE_MSI}",
    "arguments": "/quiet /norestart",
    "expectedExitCodes": [
      0,
      3010
    ],
    "timeoutSeconds": 300
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF

# Python manifest
cat > "${PYTHON_EXE}.manifest.json" <<EOF
{
  "packageId": "python",
  "version": "${PYTHON_VERSION}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${PYTHON_EXE}",
    "arguments": "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF

# 7-Zip manifest
cat > "${ZIP_EXE}.manifest.json" <<EOF
{
  "packageId": "7zip",
  "version": "${ZIP_VERSION}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${ZIP_EXE}",
    "arguments": "/S /D=C:\\Program Files\\7-Zip",
    "expectedExitCodes": [0],
    "timeoutSeconds": 120
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF

echo "Manifests generated."
echo ""
echo "=== Creating artifact-bulk-alt.zip ==="

zip -j "$OUTPUT_DIR/artifact-bulk-alt.zip" \
  "$GIT_EXE" \
  "${GIT_EXE}.manifest.json" \
  "$NODE_MSI" \
  "${NODE_MSI}.manifest.json" \
  "$PYTHON_EXE" \
  "${PYTHON_EXE}.manifest.json" \
  "$ZIP_EXE" \
  "${ZIP_EXE}.manifest.json"

echo ""
echo "=== Cleaning up temporary files ==="
cd "$PROJECT_DIR"
rm -rf "$TEMP_DIR"

echo ""
echo "Done. Created: $OUTPUT_DIR/artifact-bulk-alt.zip"
ls -lh "$OUTPUT_DIR/artifact-bulk-alt.zip"
