#!/usr/bin/env bash
# After the packages are pushed: create the GitHub Release for the tag, with notes generated
# from the pull-request titles since the previous tag. There is no hand-maintained changelog:
# the commit log between two tags is the release input (git-workflow skill) and the generated
# notes are its published form. Idempotent — a re-run of the workflow leaves an existing
# release alone.
#
#   create-release.sh v0.38.0             # as the workflow runs it (GH_TOKEN with contents: write)
#   create-release.sh v0.38.0 --dry-run   # print the command instead of running it
set -euo pipefail

tag="${1:?usage: create-release.sh <tag> [--dry-run]}"
dry_run="${2:-}"

if gh release view "$tag" >/dev/null 2>&1; then
  echo "Release $tag already exists; nothing to do."
  exit 0
fi

previous="$(git describe --tags --abbrev=0 "${tag}^" 2>/dev/null || true)"
args=(release create "$tag" --title "$tag" --verify-tag --generate-notes)
if [ -n "$previous" ]; then
  args+=(--notes-start-tag "$previous")
fi

if [ "$dry_run" = "--dry-run" ]; then
  echo "would run: gh ${args[*]}  (previous tag: ${previous:-none})"
  exit 0
fi

gh "${args[@]}"
echo "Created release $tag with notes since ${previous:-the first commit}."
