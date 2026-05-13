#!/usr/bin/env bash
set -euo pipefail

# download-amazing-workload-artifacts.sh
# Downloads "Amazing Workload" and "SSMS Workload" installer binaries and generates manifests.
# Downloads both older and newer versions of DBeaver, Python, and SSMS.
#
# Features:
# - Caches installers locally to avoid redundant downloads.
# - Outputs manifests and final zip archives directly to dist/artifacts/.
# - Re-creates zip archives only when source manifests or installers change.
# - Creates separate zip archives for Amazing Workload and SSMS Workload.
#
# Note: SSMS installers are bootstrappers that download additional payload
# during installation; internet access is required on the target machine
# during the Install step.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="$PROJECT_DIR/dist/artifacts"
WORKLOADS_DIR="$PROJECT_DIR/dist/workloads"
CACHE_DIR="$PROJECT_DIR/.artifact-cache"

mkdir -p "$OUTPUT_DIR"
mkdir -p "$WORKLOADS_DIR"
mkdir -p "$CACHE_DIR"

# --- Older versions (from workloads-older.json) ---
DBEAVER_VERSION_OLDER="24.3.0"
PYTHON_VERSION_OLDER="3.13.3"
SSMS_VERSION_OLDER="19.3"

# --- Newer versions (from workloads-newer.json) ---
DBEAVER_VERSION_NEWER="26.0.3"
PYTHON_VERSION_NEWER="3.14.4"
SSMS_VERSION_NEWER="22.5.2"

# Filenames
DBEAVER_EXE_OLDER="dbeaver-ce-${DBEAVER_VERSION_OLDER}-x86_64-setup.exe"
PYTHON_EXE_OLDER="python-${PYTHON_VERSION_OLDER}-amd64.exe"
SSMS_EXE_OLDER="SSMS-Setup-ENU-${SSMS_VERSION_OLDER}.exe"

DBEAVER_EXE_NEWER="dbeaver-ce-${DBEAVER_VERSION_NEWER}-windows-x86_64.exe"
PYTHON_EXE_NEWER="python-${PYTHON_VERSION_NEWER}-amd64.exe"
SSMS_EXE_NEWER="vs_SSMS-${SSMS_VERSION_NEWER}.exe"

# Download URLs
DBEAVER_URL_OLDER="https://github.com/dbeaver/dbeaver/releases/download/${DBEAVER_VERSION_OLDER}/${DBEAVER_EXE_OLDER}"
PYTHON_URL_OLDER="https://www.python.org/ftp/python/${PYTHON_VERSION_OLDER}/${PYTHON_EXE_OLDER}"
SSMS_URL_OLDER="https://go.microsoft.com/fwlink/?linkid=2257624&clcid=0x409"

DBEAVER_URL_NEWER="https://github.com/dbeaver/dbeaver/releases/download/${DBEAVER_VERSION_NEWER}/${DBEAVER_EXE_NEWER}"
PYTHON_URL_NEWER="https://www.python.org/ftp/python/${PYTHON_VERSION_NEWER}/${PYTHON_EXE_NEWER}"
SSMS_URL_NEWER="https://aka.ms/ssms/22/release/vs_SSMS.exe"

get_cached_path() {
  echo "$CACHE_DIR/$1"
}

download_file() {
  local url="$1"
  local dest="$2"
  if [ -f "$dest" ]; then
    local size
    size=$(stat -c%s "$dest" 2>/dev/null || stat -f%z "$dest" 2>/dev/null)
    local mb
    mb=$(echo "scale=2; $size / 1048576" | bc 2>/dev/null || echo "?")
    echo "Using cached $(basename "$dest") (${mb} MB)"
    return
  fi
  echo "Downloading $(basename "$dest") ..."
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL -o "$dest" "$url"
  elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$dest" "$url"
  else
    echo "Error: neither curl nor wget is available." >&2
    exit 1
  fi
  local size
  size=$(stat -c%s "$dest" 2>/dev/null || stat -f%z "$dest" 2>/dev/null)
  local mb
  mb=$(echo "scale=2; $size / 1048576" | bc 2>/dev/null || echo "?")
  echo "  -> ${mb} MB"
}

write_if_changed() {
  local path="$1"
  local value="$2"
  if [ -f "$path" ]; then
    local existing
    existing=$(cat "$path")
    if [ "$existing" = "$value" ]; then
      return
    fi
  fi
  printf '%s' "$value" > "$path"
}

zip_needs_rebuild() {
  local zip_path="$1"
  shift
  local sources=("$@")
  if [ ! -f "$zip_path" ]; then
    return 0
  fi
  local zip_time
  zip_time=$(stat -c%Y "$zip_path" 2>/dev/null || stat -f%m "$zip_path" 2>/dev/null)
  for src in "${sources[@]}"; do
    if [ ! -f "$src" ]; then
      return 0
    fi
    local src_time
    src_time=$(stat -c%Y "$src" 2>/dev/null || stat -f%m "$src" 2>/dev/null)
    if [ "$src_time" -gt "$zip_time" ]; then
      return 0
    fi
  done
  return 1
}

create_zip_archive() {
  local zip_path="$1"
  shift
  local sources=("$@")
  if [ -f "$zip_path" ]; then
    rm -f "$zip_path"
  fi
  local tmpdir
  tmpdir="$(mktemp -d)"
  local files_to_zip=()
  for src in "${sources[@]}"; do
    cp "$src" "$tmpdir/"
    files_to_zip+=("$(basename "$src")")
  done
  (cd "$tmpdir" && zip -q "$zip_path" "${files_to_zip[@]}")
  rm -rf "$tmpdir"
}

echo "=== Downloading Amazing Workload and SSMS Workload artifacts (cached to $CACHE_DIR) ==="

CACHED_DBEAVER_OLDER=$(get_cached_path "$DBEAVER_EXE_OLDER")
CACHED_PYTHON_OLDER=$(get_cached_path "$PYTHON_EXE_OLDER")
CACHED_SSMS_OLDER=$(get_cached_path "$SSMS_EXE_OLDER")

CACHED_DBEAVER_NEWER=$(get_cached_path "$DBEAVER_EXE_NEWER")
CACHED_PYTHON_NEWER=$(get_cached_path "$PYTHON_EXE_NEWER")
CACHED_SSMS_NEWER=$(get_cached_path "$SSMS_EXE_NEWER")

download_file "$DBEAVER_URL_OLDER" "$CACHED_DBEAVER_OLDER"
download_file "$PYTHON_URL_OLDER" "$CACHED_PYTHON_OLDER"
download_file "$SSMS_URL_OLDER" "$CACHED_SSMS_OLDER"

download_file "$DBEAVER_URL_NEWER" "$CACHED_DBEAVER_NEWER"
download_file "$PYTHON_URL_NEWER" "$CACHED_PYTHON_NEWER"
download_file "$SSMS_URL_NEWER" "$CACHED_SSMS_NEWER"

echo ""
echo "=== Generating manifest files in $OUTPUT_DIR ==="

# DBeaver older manifest
DBEAVER_BASE_OLDER="${DBEAVER_EXE_OLDER%.*}"
DBEAVER_MANIFEST_PATH_OLDER="$OUTPUT_DIR/${DBEAVER_BASE_OLDER}.manifest.json"
DBEAVER_MANIFEST_JSON_OLDER=$(cat <<EOF
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
    "uninstallArgs": "/S /allusers",
    "uninstallCommand": "%ProgramFiles%\\\\DBeaver\\\\uninstall.exe",
    "upgradeBehavior": "InPlace",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "version_manifest",
    "path": "dbeaver"
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF
)
write_if_changed "$DBEAVER_MANIFEST_PATH_OLDER" "$DBEAVER_MANIFEST_JSON_OLDER"

# Python older manifest
PYTHON_BASE_OLDER="${PYTHON_EXE_OLDER%.*}"
PYTHON_MANIFEST_PATH_OLDER="$OUTPUT_DIR/${PYTHON_BASE_OLDER}.manifest.json"
PYTHON_MANIFEST_JSON_OLDER=$(cat <<EOF
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
    "uninstallArgs": "/uninstall /quiet",
    "uninstallCommand": "{artifactPath}",
    "upgradeBehavior": "UninstallFirst",
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
)
write_if_changed "$PYTHON_MANIFEST_PATH_OLDER" "$PYTHON_MANIFEST_JSON_OLDER"

# SSMS older manifest
SSMS_BASE_OLDER="${SSMS_EXE_OLDER%.*}"
SSMS_MANIFEST_PATH_OLDER="$OUTPUT_DIR/${SSMS_BASE_OLDER}.manifest.json"
SSMS_MANIFEST_JSON_OLDER=$(cat <<EOF
{
  "packageId": "ssms",
  "version": "${SSMS_VERSION_OLDER}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${SSMS_EXE_OLDER}",
    "arguments": "/install /quiet /norestart",
    "uninstallArgs": "/uninstall /quiet",
    "uninstallCommand": "%ProgramFiles(x86)%\\\\Microsoft SQL Server Management Studio 19\\\\Common7\\\\IDE\\\\Ssms.exe",
    "upgradeBehavior": "UninstallFirst",
    "expectedExitCodes": [0, 3010],
    "timeoutSeconds": 600
  },
  "detection": {
    "type": "file",
    "path": "C:\\\\Program Files (x86)\\\\Microsoft SQL Server Management Studio 19\\\\Common7\\\\IDE\\\\Ssms.exe"
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "medium",
    "approvalRequired": false
  }
}
EOF
)
write_if_changed "$SSMS_MANIFEST_PATH_OLDER" "$SSMS_MANIFEST_JSON_OLDER"

# DBeaver newer manifest
DBEAVER_BASE_NEWER="${DBEAVER_EXE_NEWER%.*}"
DBEAVER_MANIFEST_PATH_NEWER="$OUTPUT_DIR/${DBEAVER_BASE_NEWER}.manifest.json"
DBEAVER_MANIFEST_JSON_NEWER=$(cat <<EOF
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
    "uninstallArgs": "/S /allusers",
    "uninstallCommand": "%ProgramFiles%\\\\DBeaver\\\\uninstall.exe",
    "upgradeBehavior": "InPlace",
    "expectedExitCodes": [0],
    "timeoutSeconds": 300
  },
  "detection": {
    "type": "version_manifest",
    "path": "dbeaver"
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "low",
    "approvalRequired": false
  }
}
EOF
)
write_if_changed "$DBEAVER_MANIFEST_PATH_NEWER" "$DBEAVER_MANIFEST_JSON_NEWER"

# Python newer manifest
PYTHON_BASE_NEWER="${PYTHON_EXE_NEWER%.*}"
PYTHON_MANIFEST_PATH_NEWER="$OUTPUT_DIR/${PYTHON_BASE_NEWER}.manifest.json"
PYTHON_MANIFEST_JSON_NEWER=$(cat <<EOF
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
    "uninstallArgs": "/uninstall /quiet",
    "uninstallCommand": "{artifactPath}",
    "upgradeBehavior": "UninstallFirst",
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
)
write_if_changed "$PYTHON_MANIFEST_PATH_NEWER" "$PYTHON_MANIFEST_JSON_NEWER"

# SSMS newer manifest
SSMS_BASE_NEWER="${SSMS_EXE_NEWER%.*}"
SSMS_MANIFEST_PATH_NEWER="$OUTPUT_DIR/${SSMS_BASE_NEWER}.manifest.json"
SSMS_MANIFEST_JSON_NEWER=$(cat <<EOF
{
  "packageId": "ssms",
  "version": "${SSMS_VERSION_NEWER}",
  "channel": "stable",
  "artifactType": "exe",
  "verificationResult": "pass",
  "installAdapter": {
    "type": "exe",
    "command": "${SSMS_EXE_NEWER}",
    "arguments": "--quiet --norestart --wait",
    "uninstallArgs": "uninstall --quiet --norestart",
    "uninstallCommand": "${SSMS_EXE_NEWER}",
    "upgradeBehavior": "UninstallFirst",
    "expectedExitCodes": [0, 3010],
    "timeoutSeconds": 600
  },
  "detection": {
    "type": "file",
    "path": "C:\\\\Program Files\\\\Microsoft SQL Server Management Studio 22\\\\Release\\\\Common7\\\\IDE\\\\Ssms.exe"
  },
  "policyTags": {
    "retryabilityClass": "non-idempotent",
    "idempotencyMode": "none",
    "riskLevel": "medium",
    "approvalRequired": false
  }
}
EOF
)
write_if_changed "$SSMS_MANIFEST_PATH_NEWER" "$SSMS_MANIFEST_JSON_NEWER"

echo "Manifests generated."
echo ""
echo "=== Creating zip archives (skipping if up-to-date) ==="

AMAZING_V1_ZIP_NAME="amazing-workload-artifacts-v1.zip"
AMAZING_V2_ZIP_NAME="amazing-workload-artifacts-v2.zip"
SSMS_V1_ZIP_NAME="ssms-workload-artifacts-v1.zip"
SSMS_V2_ZIP_NAME="ssms-workload-artifacts-v2.zip"

AMAZING_V1_ZIP_PATH="$OUTPUT_DIR/$AMAZING_V1_ZIP_NAME"
AMAZING_V2_ZIP_PATH="$OUTPUT_DIR/$AMAZING_V2_ZIP_NAME"
SSMS_V1_ZIP_PATH="$OUTPUT_DIR/$SSMS_V1_ZIP_NAME"
SSMS_V2_ZIP_PATH="$OUTPUT_DIR/$SSMS_V2_ZIP_NAME"

# Build Amazing Workload v1 zip (older DBeaver + Python + manifests)
AMAZING_V1_SOURCES=(
  "$DBEAVER_MANIFEST_PATH_OLDER"
  "$PYTHON_MANIFEST_PATH_OLDER"
  "$CACHED_DBEAVER_OLDER"
  "$CACHED_PYTHON_OLDER"
)

if zip_needs_rebuild "$AMAZING_V1_ZIP_PATH" "${AMAZING_V1_SOURCES[@]}"; then
  create_zip_archive "$AMAZING_V1_ZIP_PATH" "${AMAZING_V1_SOURCES[@]}"
  echo "Created $AMAZING_V1_ZIP_NAME"
else
  echo "Skipped $AMAZING_V1_ZIP_NAME (up-to-date)"
fi

# Build SSMS Workload v1 zip (older SSMS + manifest)
SSMS_V1_SOURCES=(
  "$SSMS_MANIFEST_PATH_OLDER"
  "$CACHED_SSMS_OLDER"
)

if zip_needs_rebuild "$SSMS_V1_ZIP_PATH" "${SSMS_V1_SOURCES[@]}"; then
  create_zip_archive "$SSMS_V1_ZIP_PATH" "${SSMS_V1_SOURCES[@]}"
  echo "Created $SSMS_V1_ZIP_NAME"
else
  echo "Skipped $SSMS_V1_ZIP_NAME (up-to-date)"
fi

# Build Amazing Workload v2 zip (newer DBeaver + Python + manifests)
AMAZING_V2_SOURCES=(
  "$DBEAVER_MANIFEST_PATH_NEWER"
  "$PYTHON_MANIFEST_PATH_NEWER"
  "$CACHED_DBEAVER_NEWER"
  "$CACHED_PYTHON_NEWER"
)

if zip_needs_rebuild "$AMAZING_V2_ZIP_PATH" "${AMAZING_V2_SOURCES[@]}"; then
  create_zip_archive "$AMAZING_V2_ZIP_PATH" "${AMAZING_V2_SOURCES[@]}"
  echo "Created $AMAZING_V2_ZIP_NAME"
else
  echo "Skipped $AMAZING_V2_ZIP_NAME (up-to-date)"
fi

# Build SSMS Workload v2 zip (newer SSMS + manifest)
SSMS_V2_SOURCES=(
  "$SSMS_MANIFEST_PATH_NEWER"
  "$CACHED_SSMS_NEWER"
)

if zip_needs_rebuild "$SSMS_V2_ZIP_PATH" "${SSMS_V2_SOURCES[@]}"; then
  create_zip_archive "$SSMS_V2_ZIP_PATH" "${SSMS_V2_SOURCES[@]}"
  echo "Created $SSMS_V2_ZIP_NAME"
else
  echo "Skipped $SSMS_V2_ZIP_NAME (up-to-date)"
fi

echo ""
echo "Done. Zip archives in: $OUTPUT_DIR"
ls -lh "$OUTPUT_DIR"/*workload* 2>/dev/null || true

# Copy workload definitions to dist/workloads for runtime import
if [ -d "$PROJECT_DIR/test-workloads" ]; then
  cp "$PROJECT_DIR"/test-workloads/*.json "$WORKLOADS_DIR/"
  echo ""
  echo "Copied workload definitions to: $WORKLOADS_DIR"
fi
