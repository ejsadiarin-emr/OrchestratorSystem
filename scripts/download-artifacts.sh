#!/usr/bin/env bash
# Download real artifacts for testing artifact upload and import
# All downloads are cached in .artifact-cache/ (gitignored)
# Generates manifest JSONs and creates ZIP files in dist/imports/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CACHE_DIR="$REPO_ROOT/.artifact-cache"
IMPORTS_DIR="$REPO_ROOT/dist/imports"

mkdir -p "$CACHE_DIR"
mkdir -p "$IMPORTS_DIR"

download_if_missing() {
    local url="$1"
    local file="$2"
    if [ ! -f "$file" ]; then
        echo "Downloading $(basename "$file") ..."
        curl -fsSL -o "$file" "$url"
        echo "Saved: $file"
    else
        echo "Already exists: $file"
    fi
}

write_manifest() {
    local pkg_id="$1"
    local pkg_name="$2"
    local version="$3"
    local filename="$4"
    local install_args="$5"
    local uninstall_cmd="$6"
    local uninstall_args="$7"
    local update_strategy="$8"
    local detect_type="$9"
    local detect_key="${10}"
    local detect_value_name="${11}"
    local detect_expected="${12}"

    local manifest_file="$CACHE_DIR/${filename%.exe}.json"

    cat > "$manifest_file" <<EOF
{
  "packageId": "$pkg_id",
  "packageName": "$pkg_name",
  "version": "$version",
  "installerFile": "$filename",
  "installCommand": "$filename",
  "installArgs": "$install_args",
  "uninstallCommand": "$uninstall_cmd",
  "uninstallArgs": "$uninstall_args",
  "updateStrategy": "$update_strategy",
  "detection": {
    "type": "$detect_type",
    "key": "$detect_key",
    "valueName": "$detect_value_name",
    "expectedValue": "$detect_expected"
  }
}
EOF
    echo "Manifest: $manifest_file"
}

create_zip() {
    local zip_path="$1"
    shift
    local files=("$@")
    if command -v zip >/dev/null 2>&1; then
        local abs_files=()
        for f in "${files[@]}"; do
            abs_files+=("$CACHE_DIR/$f")
        done
        zip -j "$zip_path" "${abs_files[@]}"
    else
        python3 - "$zip_path" "$CACHE_DIR" "${files[@]}" <<'PY'
import sys, zipfile, os
zip_path = sys.argv[1]
cache_dir = sys.argv[2]
files = sys.argv[3:]
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zf:
    for f in files:
        zf.write(os.path.join(cache_dir, f), f)
PY
    fi
}

# --- DBeaver CE 24.1.0 (older) ---
DBEAVER_OLD_FILE="dbeaver-ce-24.1.0-x86_64-setup.exe"
download_if_missing "https://github.com/dbeaver/dbeaver/releases/download/24.1.0/dbeaver-ce-24.1.0-x86_64-setup.exe" "$CACHE_DIR/$DBEAVER_OLD_FILE"
write_manifest "dbeaver-ce" "DBeaver Community Edition" "24.1.0" "$DBEAVER_OLD_FILE" "/S" "MsiExec.exe" "/X{2C2B8C8C-5C5C-4C5C-8C5C-2C2B8C8C5C5C} /qn" "overinstall" "registry" "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DBeaver Community_is1" "DisplayVersion" "24.1.0"

# --- Python for Windows 3.12.4 (older) ---
PYTHON_OLD_FILE="python-3.12.4-amd64.exe"
download_if_missing "https://www.python.org/ftp/python/3.12.4/python-3.12.4-amd64.exe" "$CACHE_DIR/$PYTHON_OLD_FILE"
write_manifest "python-windows" "Python for Windows" "3.12.4" "$PYTHON_OLD_FILE" "/quiet InstallAllUsers=1 PrependPath=1" "MsiExec.exe" "/X{12345678-1234-1234-1234-123456789012} /qn" "overinstall" "registry" "HKLM\\SOFTWARE\\Python\\PythonCore\\3.12\\InstallPath" "ExecutablePath" "C:\\Program Files\\Python312\\python.exe"

# --- SSMS 22.0 (older) ---
SSMS_OLD_FILE="SSMS-22.0.exe"
download_if_missing "https://aka.ms/ssms/22/release/vs_SSMS.exe" "$CACHE_DIR/$SSMS_OLD_FILE"
write_manifest "ssms" "SQL Server Management Studio" "22.0" "$SSMS_OLD_FILE" "--quiet --norestart --wait" "MsiExec.exe" "/X{ABCDEF12-3456-7890-ABCD-EF1234567890} /qn" "overinstall" "registry" "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Microsoft SQL Server Management Studio - 22.0" "DisplayVersion" "22.0"

# --- DBeaver CE 24.2.0 (newer) ---
DBEAVER_NEW_FILE="dbeaver-ce-24.2.0-x86_64-setup.exe"
download_if_missing "https://github.com/dbeaver/dbeaver/releases/download/24.2.0/dbeaver-ce-24.2.0-x86_64-setup.exe" "$CACHE_DIR/$DBEAVER_NEW_FILE"
write_manifest "dbeaver-ce" "DBeaver Community Edition" "24.2.0" "$DBEAVER_NEW_FILE" "/S" "MsiExec.exe" "/X{3D3C9D9D-6D6D-5D6D-9D6D-3D3C9D9D6D6D} /qn" "overinstall" "registry" "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DBeaver Community_is1" "DisplayVersion" "24.2.0"

# --- Python for Windows 3.13.0 (newer) ---
PYTHON_NEW_FILE="python-3.13.0-amd64.exe"
download_if_missing "https://www.python.org/ftp/python/3.13.0/python-3.13.0-amd64.exe" "$CACHE_DIR/$PYTHON_NEW_FILE"
write_manifest "python-windows" "Python for Windows" "3.13.0" "$PYTHON_NEW_FILE" "/quiet InstallAllUsers=1 PrependPath=1" "MsiExec.exe" "/X{23456789-2345-2345-2345-234567890123} /qn" "overinstall" "registry" "HKLM\\SOFTWARE\\Python\\PythonCore\\3.13\\InstallPath" "ExecutablePath" "C:\\Program Files\\Python313\\python.exe"

# --- VS Code 1.91.1 (newer) ---
VSCODE_NEW_FILE="VSCodeUserSetup-x64-1.91.1.exe"
download_if_missing "https://update.code.visualstudio.com/1.91.1/win32-x64-user/stable" "$CACHE_DIR/$VSCODE_NEW_FILE"
write_manifest "vscode" "Visual Studio Code" "1.91.1" "$VSCODE_NEW_FILE" "/VERYSILENT /NORESTART" "MsiExec.exe" "/X{34567890-3456-3456-3456-345678901234} /qn" "overinstall" "registry" "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{GUID}_is1" "DisplayVersion" "1.91.1"

# Create ZIP files
echo ""
echo "Creating ZIP files..."

create_zip "$IMPORTS_DIR/artifacts-older.zip" \
    "$DBEAVER_OLD_FILE" "${DBEAVER_OLD_FILE%.exe}.json" \
    "$PYTHON_OLD_FILE" "${PYTHON_OLD_FILE%.exe}.json" \
    "$SSMS_OLD_FILE" "${SSMS_OLD_FILE%.exe}.json"

create_zip "$IMPORTS_DIR/artifacts-newer.zip" \
    "$DBEAVER_NEW_FILE" "${DBEAVER_NEW_FILE%.exe}.json" \
    "$PYTHON_NEW_FILE" "${PYTHON_NEW_FILE%.exe}.json" \
    "$VSCODE_NEW_FILE" "${VSCODE_NEW_FILE%.exe}.json"

echo ""
echo "=== Download & Packaging Summary ==="
echo "Cache directory: $CACHE_DIR"
echo "Imports directory: $IMPORTS_DIR"
echo ""
echo "Artifacts in cache:"
ls -lh "$CACHE_DIR"
echo ""
echo "ZIP files:"
ls -lh "$IMPORTS_DIR"
echo ""
echo "Upload examples:"
echo "  curl -X POST http://localhost:5000/api/artifacts/upload -F \"packageId=dbeaver-ce\" -F \"version=24.1.0\" -F \"packageName=DBeaver Community Edition\" -F \"file=@$CACHE_DIR/$DBEAVER_OLD_FILE\""
echo "  curl -X POST http://localhost:5000/api/artifacts/import -F \"files=@$IMPORTS_DIR/artifacts-older.zip\""
echo "  curl -X POST http://localhost:5000/api/artifacts/import -F \"files=@$IMPORTS_DIR/artifacts-newer.zip\""
