#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GH=""
for candidate in \
  "${ROOT}/.tools/gh_2.95.0_macOS_arm64/bin/gh" \
  "${ROOT}/../littlescreens/.tools/gh_2.95.0_macOS_arm64/bin/gh" \
  "$(command -v gh 2>/dev/null || true)"; do
  if [[ -n "${candidate}" && -x "${candidate}" ]]; then
    GH="${candidate}"
    break
  fi
done

VERSION="${1:-1.0.0}"

if [[ -z "${GH}" ]]; then
  echo "GitHub CLI not found. Install gh or copy it into .tools/." >&2
  exit 1
fi

if ! "${GH}" auth status >/dev/null 2>&1; then
  echo "Log in to GitHub first:"
  echo "  ${GH} auth login"
  exit 1
fi

cd "${ROOT}"

if [[ ! -d .git ]]; then
  git init
  git branch -M main
fi

if ! git remote get-url origin >/dev/null 2>&1; then
  echo "Creating GitHub repository borderline..."
  "${GH}" repo create borderline --public --source=. --remote=origin \
    --description "Windows display underscan/overscan margin tool"
fi

git add .
if git diff --cached --quiet; then
  echo "No changes to commit."
else
  git commit -m "Add Borderline Windows display margin tool"
fi

echo "Pushing main branch..."
git push -u origin main

TAG="v${VERSION}"
if git rev-parse "${TAG}" >/dev/null 2>&1; then
  echo "Tag ${TAG} already exists locally."
else
  git tag "${TAG}"
fi

echo "Pushing tag ${TAG} to trigger release..."
git push origin "${TAG}"

OWNER="$("${GH}" repo view --json owner -q .owner.login)"
echo
echo "Release workflow started. When it finishes, download from:"
echo "  https://github.com/${OWNER}/borderline/releases/latest/download/borderline.exe"
echo
echo "Track progress:"
echo "  ${GH} run list --workflow=release.yml"
