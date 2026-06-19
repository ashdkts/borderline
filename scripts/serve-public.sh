#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${1:-8080}"

CLOUDFLARED="${ROOT}/.tools/cloudflared"
if [[ ! -x "${CLOUDFLARED}" && -x "${ROOT}/../littlescreens/.tools/cloudflared" ]]; then
  CLOUDFLARED="${ROOT}/../littlescreens/.tools/cloudflared"
fi

if [[ ! -x "${CLOUDFLARED}" ]]; then
  echo "cloudflared not found. Use GitHub Releases or ./scripts/serve-releases.sh on your LAN." >&2
  exit 1
fi

if ! curl -sf "http://127.0.0.1:${PORT}/latest.json" >/dev/null 2>&1; then
  echo "Starting local release server on port ${PORT}..."
  python3 -m http.server "${PORT}" --directory "${ROOT}/releases" &
  SERVER_PID=$!
  trap 'kill ${SERVER_PID} 2>/dev/null || true' EXIT
  sleep 1
fi

echo "Opening a public tunnel (no router setup required)..."
echo "On Windows, download:"
echo "  <tunnel-url>/borderline.exe"
echo
exec "${CLOUDFLARED}" tunnel --url "http://127.0.0.1:${PORT}"
