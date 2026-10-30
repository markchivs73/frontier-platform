---
name: git-workflow
description: Commit message format, branching model, merge strategy, release tagging. Use when committing, branching, merging, or cutting a release.
---

# Git workflow

Trunk-based. `main` is always green and always releasable.

## Branching & merging

- Feature branches off `main`, short-lived (a couple of days at most).
- **Squash merge** — one commit per unit of work on `main`.
- CI must be green before merge: build, unit + architecture tests, coverage gate, integration
  tests, pack.

## Commit messages

```
scope: Imperative summary
scope!: Imperative summary     ← breaks that package's public API
```

- **scope** is a library short name — `abstractions`, `audit`, `contextassembly`,
  `guardrails`, `hitl`, `modelroleconfig`, `observability`, `resilience`, `serialization`,
  `workflow.compiler`, `workflow.model`, `workflow.orchestration` — or one of `meta`, `ci`,
  `docs`, `deps`, `release` (the commit that ships the public API before a tag; see Releasing).
- **`!`** marks a break to that package's public surface. It must agree with the
  `PublicAPI.*.txt` diff.
- No trailing period. Subject ≤72 characters. Imperative mood ("Add", not "Added").

Enforced by `.github/scripts/validate-commit-messages.sh`, which derives the valid scope list
from `ls src/` at runtime so it can never drift from the actual libraries.

Why it is worth the ceremony: with lockstep versioning, the log between two tags is the only
input to the release decision.

```bash
git log v1.1.0..v1.2.0 --grep '^audit'    # per-package release notes
git log v1.1.0..v1.2.0 --grep '!:'        # is the next release a major?
```

The body carries the reasoning — what was wrong, what changed, what was verified, and anything
found along the way that a future reader would otherwise have to rediscover.

## Releasing

Versions come from git tags via MinVer. Tagging `vX.Y.Z` on `main` publishes `X.Y.Z` for **all
thirteen packages** — they version in lockstep, so a change to one republishes all of them (see
`docs/DECISIONS.md`).

A release is a pull request, then a tag on its merge commit — never a tag on whatever `main`
happens to be:

0. If the release moves the pin of an engine package that carries conformance evidence, check the
   evidence for the new version first, and record it in ADR-PA38 in the same PR as the bump:
   **Celly** — its CI for the commit the new package was built from is green, and its cel-spec
   conformance count and known-failures list have not regressed; **Corvus.Text.Json.Validator** —
   Bowtie's 2020-12 report for the new version still shows no failures or errors. No third-party
   repository code is copied in to do this (ADR-PA38).
1. On a branch from `main`, run `python3 tools/ship-public-api.py`. It moves every package's
   `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt`, applies each `*REMOVED*`
   marker by deleting the shipped line it names, and leaves the Unshipped files holding only
   their header. Build once (`dotnet build FrontierPlatform.slnx -c Release -warnaserror`) to
   prove RS0016/RS0017 still hold. Nothing else goes in that commit.
2. Commit it as `release: Ship vX.Y.Z` — the tag name must appear in the subject — and merge
   the PR like any other.
3. Tag the merge commit and push the tag:

   ```bash
   git tag -a vX.Y.Z <merge sha> -m "vX.Y.Z: <one line on what the release carries>"
   git push origin vX.Y.Z
   ```

The publish workflow refuses the tag unless the tagged commit is on `main`, is a `release:`
commit naming the tag (`.github/scripts/check-release-tag.sh`), and
`python3 tools/ship-public-api.py --check` finds no unshipped entry — so a forgotten step fails
in seconds, before anything is packed or pushed: delete the tag, ship the API, re-tag. It then
runs the same build, vulnerability scan, test suite and coverage gate as PR CI, packs, pushes,
and creates the GitHub Release with notes generated from the pull-request titles since the
previous tag (`.github/scripts/create-release.sh`). There is no hand-maintained changelog: the
log between two tags is the release input (above) and the generated notes are its published form.

Why shipping matters: the PublicAPI analyzer only guards what is in `PublicAPI.Shipped.txt`.
Deleting an unshipped entry is silent; deleting a shipped one is RS0017 unless the PR writes a
`*REMOVED*` marker. Shipping at every tag is what makes the next release's breaks visible as
more than a `!` in a commit subject.

While the major version is `0`, breaking changes may ship in a minor. After 1.0, majors only.

**A published version is immutable** — GitHub Packages will not accept a re-push of the same
version. Fix forward with a new patch.
