#!/usr/bin/env bash
# The publish workflow's first gate (git-workflow skill, "Releasing"): a tag publishes only
# from a release commit on main — `release: Ship vX.Y.Z` — so a tag placed on an ordinary
# commit, or on a branch, fails in seconds and publishes nothing.
#
#   check-release-tag.sh v0.38.0                         # as the workflow runs it
#   RELEASE_MAIN_REF=HEAD check-release-tag.sh v9.9.9    # local test: treat the checkout as main
set -euo pipefail

tag="${1:?usage: check-release-tag.sh <tag>}"
main_ref="${RELEASE_MAIN_REF:-origin/main}"

if ! git rev-parse --verify --quiet "$main_ref" >/dev/null; then
  git fetch --quiet origin main:refs/remotes/origin/main
fi

sha="$(git rev-parse --verify "${tag}^{commit}")"
subject="$(git log -1 --format=%s "$sha")"

if ! git merge-base --is-ancestor "$sha" "$main_ref"; then
  echo "::error::Tag $tag points at ${sha:0:7}, which is not on $main_ref."
  exit 1
fi

case "$subject" in
  "release: "*) ;;
  *)
    echo "::error::Tag $tag points at '$subject', not a release commit. Ship the public API in a 'release: Ship $tag' PR, merge it, and tag that merge commit."
    exit 1
    ;;
esac

# The tag name as a whole word, so v0.38.0 does not match v0.38.01 or v10.38.0.
escaped="${tag//./\\.}"
pattern="(^|[^0-9A-Za-z.])${escaped}"'([^0-9A-Za-z.]|$)'
if ! printf '%s\n' "$subject" | grep -qE "$pattern"; then
  echo "::error::Release commit '$subject' does not name the tag $tag."
  exit 1
fi

echo "Tag $tag is on release commit ${sha:0:7}: $subject"
