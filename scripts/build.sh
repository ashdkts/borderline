#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Prefer a local .tools/go, then fall back to littlescreens' bundled Go.
if [[ -x "${ROOT}/.tools/go/bin/go" ]]; then
  export PATH="${ROOT}/.tools/go/bin:${PATH}"
elif [[ -x "${ROOT}/../littlescreens/.tools/go/bin/go" ]]; then
  export PATH="${ROOT}/../littlescreens/.tools/go/bin:${PATH}"
fi

DIST="${ROOT}/dist"
RELEASES="${ROOT}/releases"

mkdir -p "${DIST}" "${RELEASES}"

echo "Building borderline.exe for Windows..."
(
  cd "${ROOT}/borderline-app"
  GOOS=windows GOARCH=amd64 CGO_ENABLED=0 \
    go build -ldflags="-s -w -H windowsgui" -o "${DIST}/borderline.exe" .
)

if command -v shasum >/dev/null 2>&1; then
  SHA256="$(shasum -a 256 "${DIST}/borderline.exe" | awk '{print $1}')"
elif command -v sha256sum >/dev/null 2>&1; then
  SHA256="$(sha256sum "${DIST}/borderline.exe" | awk '{print $1}')"
else
  echo "Could not compute sha256; install shasum or sha256sum." >&2
  exit 1
fi

cat > "${RELEASES}/latest.json" <<EOF
{
  "version": "1.0.0",
  "url": "http://127.0.0.1:8080/borderline.exe",
  "sha256": "${SHA256}"
}
EOF

cp "${DIST}/borderline.exe" "${RELEASES}/borderline.exe"

echo
echo "Build complete:"
echo "  ${DIST}/borderline.exe"
echo "  ${RELEASES}/borderline.exe"
echo "  ${RELEASES}/latest.json"
