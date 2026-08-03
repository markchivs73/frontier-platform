#!/usr/bin/env bash
# Adapted from frontier-workflow/tools/run-unit-tests.sh @ 8df00ee (dropped the demo/ and
# Playwright cases; builds Release to match CI and the published artifact).
# Drift check: git -C ../frontier-workflow log --oneline 8df00ee.. -- tools/run-unit-tests.sh
#
# Runs the unit test suite (excludes [Trait("Category", "Integration")]) with
# Coverlet XPlat coverage, per test project, and enforces the >=95% line+branch
# per-assembly threshold - mirrors the CI `test` job exactly.
#
# Solution-wide `dotnet test FrontierPlatform.slnx` runs every test project's
# build/instrumentation in parallel, which corrupts Coverlet's per-assembly
# AssemblyLoadContext attribution (duplicate "Assembly N" modules with near-zero
# hits) and can race source generators regenerating the same production project's
# obj/ output from multiple test-project builds at once (observed:
# LoggerMessage.g.cs "does not exist (any more)"). Collecting per project,
# sequentially, avoids both.
set -euo pipefail

cd "$(dirname "$0")/.."

rm -rf coverage

dotnet build FrontierPlatform.slnx -c Release

for proj in tests/*/; do
  # Skip non-project directories (e.g. tests/Shared/, compile-linked helper sources
  # with no .csproj of their own — `dotnet test` on one fails with MSB1003).
  compgen -G "${proj}*.csproj" > /dev/null || continue
  dotnet test "$proj" -c Release --no-build --filter "Category!=Integration" \
    --collect:"XPlat Code Coverage" --settings coverlet.runsettings \
    --results-directory ./coverage
done

python3 .github/scripts/coverage_by_assembly.py ./coverage 95
