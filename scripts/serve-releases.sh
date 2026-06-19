#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${1:-8080}"

echo "Serving releases from ${ROOT}/releases on http://127.0.0.1:${PORT}/"
echo "Download: http://127.0.0.1:${PORT}/borderline.exe"
exec python3 -m http.server "${PORT}" --directory "${ROOT}/releases"
