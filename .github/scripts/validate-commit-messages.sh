#!/usr/bin/env bash
# Validates commit subjects against this repo's convention (git-workflow skill):
#
#   scope: Imperative summary
#   scope!: Imperative summary     <- breaks that package's public API
#
# scope is a platform library short name or one of the fixed non-library scopes below.
#
# The library scopes are DERIVED FROM ls src/ at runtime rather than hardcoded, so this
# script cannot drift from the actual set of libraries — adding or renaming a library
# updates the allowed scopes automatically.
#
# Reads subjects from COMMIT_SUBJECTS (one per line), matching the CI step's interface.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"

# Frontier.Platform.ModelRoleConfig -> modelroleconfig.
# Plain while-read rather than mapfile: macOS ships bash 3.2, where mapfile does not exist,
# and this script has to run locally as well as on the CI runner.
all_scopes=()
while IFS= read -r scope; do
  [ -n "$scope" ] && all_scopes+=("$scope")
done < <(
  find "$repo_root/src" -maxdepth 1 -mindepth 1 -type d -exec basename {} \; \
    | sed 's/^Frontier\.Platform\.//' \
    | tr '[:upper:]' '[:lower:]' \
    | sort
)

all_scopes+=(meta ci docs deps)

scope_alternation="$(IFS='|'; echo "${all_scopes[*]}")"
# scope, optional '!', ': ', then a capitalised imperative summary with no trailing period.
pattern="^(${scope_alternation})!?: [A-Z][^.]*[^.[:space:]]$"

failed=0
while IFS= read -r subject; do
  [ -z "$subject" ] && continue
  case "$subject" in
    Merge*|Revert*) continue;;
  esac

  if ! printf '%s' "$subject" | grep -qE "$pattern"; then
    echo "::error::Invalid commit subject: $subject"
    failed=1
    continue
  fi

  if [ "${#subject}" -gt 72 ]; then
    echo "::error::Commit subject exceeds 72 characters (${#subject}): $subject"
    failed=1
  fi
done <<< "${COMMIT_SUBJECTS:-}"

if [ "$failed" -ne 0 ]; then
  echo "::error::Expected '<scope>: Imperative summary' ('<scope>!:' for a public API break)."
  echo "::error::Valid scopes: ${all_scopes[*]}"
  exit 1
fi

echo "All commit subjects valid."
