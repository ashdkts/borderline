#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-1.1.0}"
DIST="${ROOT}/dist"
RELEASES="${ROOT}/releases"

mkdir -p "${DIST}" "${RELEASES}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK not found. Install .NET 8 SDK or rely on GitHub Actions to build." >&2
  exit 1
fi

echo "Building borderline.exe v${VERSION} (C# WinForms)..."
(
  cd "${ROOT}/Borderline"
  dotnet publish \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:Version="${VERSION}" \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "${DIST}/publish"
)

cp "${DIST}/publish/borderline.exe" "${DIST}/borderline.exe"
cp "${DIST}/borderline.exe" "${RELEASES}/borderline.exe"

if command -v shasum >/dev/null 2>&1; then
  SHA256="$(shasum -a 256 "${DIST}/borderline.exe" | awk '{print $1}')"
elif command -v sha256sum >/dev/null 2>&1; then
  SHA256="$(sha256sum "${DIST}/borderline.exe" | awk '{print $1}')"
else
  echo "Could not compute sha256." >&2
  exit 1
fi

cat > "${RELEASES}/latest.json" <<EOF
{
  "version": "${VERSION}",
  "url": "https://github.com/ashdkts/borderline/releases/download/v${VERSION}/borderline.exe",
  "sha256": "${SHA256}"
}
EOF

echo
echo "Build complete:"
echo "  ${DIST}/borderline.exe"
echo "  ${RELEASES}/borderline.exe"
