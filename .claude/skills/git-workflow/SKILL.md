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
  `guardrails`, `hitl`, `modelroleconfig`, `observability`, `resilience`, `serialization` —
  or one of `meta`, `ci`, `docs`, `deps`.
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

Versions come from git tags via MinVer. Tagging `v1.2.3` on `main` publishes `1.2.3` for **all
nine packages** — they version in lockstep, so a change to one republishes all of them (see
`docs/DECISIONS.md`).

```bash
git tag v1.2.3 && git push origin v1.2.3
```

The publish workflow runs the same build, vulnerability scan, test suite and coverage gate as
PR CI before it packs and pushes.

Before tagging: confirm `main` is green, the changelog is updated, and any `PublicAPI.Unshipped.txt`
entries have been moved into `PublicAPI.Shipped.txt`.

While the major version is `0`, breaking changes may ship in a minor. After 1.0, majors only.

**A published version is immutable** — GitHub Packages will not accept a re-push of the same
version. Fix forward with a new patch.
