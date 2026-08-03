#!/usr/bin/env bash
# Vendored verbatim from frontier-workflow/.github/scripts/check-vulnerabilities.sh @ 8df00ee.
# Drift check:
#   git -C ../frontier-workflow log --oneline 8df00ee.. -- .github/scripts/check-vulnerabilities.sh
#
# Scans every project's resolved NuGet packages (including transitive) for
# known vulnerabilities via the NuGet Advisory Database.
#
# `dotnet list package --vulnerable` always exits 0, even when vulnerable
# packages are found - it only ever *prints* a "has the following vulnerable
# packages" section per project. This script greps for that marker and fails
# the build if any project has one, so CI actually gates on it.
#
# Requires a prior `dotnet restore` (run as part of `dotnet build` already).
set -euo pipefail

output=$(dotnet list package --vulnerable --include-transitive 2>&1)
echo "$output"

if echo "$output" | grep -q "has the following vulnerable packages"; then
  echo "::error::Vulnerable NuGet packages detected (see report above)."
  echo "::error::Resolve by bumping the affected PackageVersion entries in Directory.Packages.props to a patched version, then re-run this check."
  exit 1
fi

echo "No known vulnerabilities found."
