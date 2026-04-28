#!/usr/bin/env bash
set -euo pipefail

# download-amazing-workload-artifacts.sh
# Downloads "Amazing Workload" installer binaries and generates manifests.
# Downloads both older and newer versions of DBeaver and Python.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="$PROJECT_DIR/test-artifacts"
TEMP_DIR="$(mktemp -d)"

# --- Older versions (from workloads-older.json) ---
DBEAVER_VERSION_OLDER="24.3.0"
PYTHON_VERSION_OLDER="3.13.3"

# --- Newer versions (from workloads-newer.json) ---
DBEAVER_VERSION_NEWER="26.0.3"
PYTHON_VERSION_NEWER="3.14.4"

# Filenames
DBEAVER_EXE_OLDER="dbeaver-ce-${DBEAVER_VERSION_OLDER}-x86_64-setup.exe"
PYTHON_EXE_OLDER="python-${PYTHON_VERSION_OLDER}-amd64.exe"
DBEAVER_EXE_NEWER="dbeaver-ce-${DBEAVER_VERSION_NEWER}-windows-x86_64.exe"
PYTHON_EXE_NEWER="python-${PYTHON_VERSION_NEWER}-amd64.exe"

# Download URLs
DBEAVER_URL_OLDER="https://github.com/dbeaver/dbeaver/releases/download/${DBEAVER_VERSION_OLDER}/${DBEAVER_EXE_OLDER}"
PYTHON_URL_OLDER="https://www.python.org/ftp/python/${PYTHON_VERSION_OLDER}/${PYTHON_EXE_OLDER}"
DBEAVER_URL_NEWER="https://github.com/dbeaver/dbeaver/releases/download/${DBEAVER_VERSION_NEWER}/${DBEAVER_EXE_NEWER}"
PYTHON_URL_NEWER="https://www.python.org/ftp/python/${PYTHON_VERSION_NEWER}/${PYTHON_EXE_NEWER}"

echo "=== Downloading Amazing Workload artifacts to $TEMP_DIR ==="
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

download_file "$DBEAVER_URL_OLDER" "$DBEAVER_EXE_OLDER"
download_file "$PYTHON_URL_OLDER" "$PYTHON_EXE_OLDER"
download_file "$DBEAVER_URL_NEWER" "$DBEAVER_EXE_NEWER"
download_file "$PYTHON_URL_NEWER" "$PYTHON_EXE_NEWER"

echo ""
echo "=== Generating manifest files ==="

# DBeaver older manifest
DBEAVER_BASE_OLDER="${DBEAVER_EXE_OLDER%.*}"
cat > "${DBEAVER_BASE_OLDER}.manifest.json" <<EOF
{
  "packageId": "dbeaver",
  "version": "${DBEAVER_VERSION_OLDER}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${DBEAVER_EXE_OLDER}",
    "arguments": "/S /allusers",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "version_manifest",
    "path": "dbeaver",
    "expectedVersion": "${DBEAVER_VERSION_OLDER}"
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF

# Python older manifest
PYTHON_BASE_OLDER="${PYTHON_EXE_OLDER%.*}"
cat > "${PYTHON_BASE_OLDER}.manifest.json" <<EOF
{
  "packageId": "python",
  "version": "${PYTHON_VERSION_OLDER}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${PYTHON_EXE_OLDER}",
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

# DBeaver newer manifest
DBEAVER_BASE_NEWER="${DBEAVER_EXE_NEWER%.*}"
cat > "${DBEAVER_BASE_NEWER}.manifest.json" <<EOF
{
  "packageId": "dbeaver",
  "version": "${DBEAVER_VERSION_NEWER}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${DBEAVER_EXE_NEWER}",
    "arguments": "/S /allusers",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "version_manifest",
    "path": "dbeaver",
    "expectedVersion": "${DBEAVER_VERSION_NEWER}"
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF

# Python newer manifest
PYTHON_BASE_NEWER="${PYTHON_EXE_NEWER%.*}"
cat > "${PYTHON_BASE_NEWER}.manifest.json" <<EOF
{
  "packageId": "python",
  "version": "${PYTHON_VERSION_NEWER}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${PYTHON_EXE_NEWER}",
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

echo "Manifests generated."
echo ""
echo "=== Moving manifests to $OUTPUT_DIR ==="

# Move manifests to output dir
mv "${DBEAVER_BASE_OLDER}.manifest.json" "$OUTPUT_DIR/"
mv "${PYTHON_BASE_OLDER}.manifest.json" "$OUTPUT_DIR/"
mv "${DBEAVER_BASE_NEWER}.manifest.json" "$OUTPUT_DIR/"
mv "${PYTHON_BASE_NEWER}.manifest.json" "$OUTPUT_DIR/"

# Optionally move the binaries too (uncomment if desired)
# mv "$DBEAVER_EXE_OLDER" "$OUTPUT_DIR/"
# mv "$PYTHON_EXE_OLDER" "$OUTPUT_DIR/"
# mv "$DBEAVER_EXE_NEWER" "$OUTPUT_DIR/"
# mv "$PYTHON_EXE_NEWER" "$OUTPUT_DIR/"

echo ""
echo "=== Cleaning up temporary files ==="
cd "$PROJECT_DIR"
rm -rf "$TEMP_DIR"

echo ""
echo "Done. Manifests created in: $OUTPUT_DIR"
ls -lh "$OUTPUT_DIR"/*.manifest.json
