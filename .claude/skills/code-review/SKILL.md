---
name: code-review
description: The PR review checklist — every box must be checked before approval and merge, no overrides. Use when reviewing a PR or preparing one for review.
---

# Code review checklist

**Rule: every applicable box checked before approve + merge. No box, no merge.** The PR
template mirrors this list; the reviewer verifies rather than trusts.

Remember what is different about this repo: **everything merged here becomes a versioned
artifact somebody else resolves.** A mistake does not stay inside one solution.

## Every PR

- [ ] CI fully green (build, unit, integration, architecture tests, coverage gate, pack)
- [ ] Coverage ≥95% per assembly; 100% on new code. Exclusions carry
      `[ExcludeFromCodeCoverage(Justification)]` and fit an allowed category
- [ ] No compiler warnings, no analyzer violations, no `#pragma warning disable`
- [ ] No `private` methods; methods ≤10–15 lines
- [ ] No removed or weakened tests without an explicit explanation in the description
- [ ] Doc comments on all new/changed public and internal types and methods
- [ ] Commit subjects follow `scope: Summary` (`scope!:` for a public-surface break)
- [ ] Scope matches the stated intent — no gold-plating, no drive-by refactors

## Public API surface

- [ ] The `PublicAPI.*.txt` diff is **reviewed deliberately**, not rubber-stamped. Every added
      line is a compatibility obligation to every consumer, permanently
- [ ] Anything newly `public` genuinely needs to be — `internal` is the default
- [ ] Removals and signature changes are marked `scope!:` and justified in the description

## Breaking changes

- [ ] `[Obsolete]` names both the replacement **and** the version deprecated in
- [ ] Removal only in a major version, never silently
- [ ] Changelog entry present
- [ ] Package README updated if the surface it documents changed

## Contracts or serialization

- [ ] Round-trip test for every new/changed contract
- [ ] Byte-stability test + **golden file committed**
- [ ] Existing golden files **unchanged** — a changed golden file on a shipped version means
      broken compatibility. Reject
- [ ] Smart enums serialize as canonical snake_case; unknown values throw, never coerce
- [ ] No package/SDK references added to Abstractions; converters live in Serialization
- [ ] Converters match by shape, not by type reference
- [ ] Version bump + migration adapter if the change is not additive-with-default

## Library structure, DI, boundaries

- [ ] No cross-platform-library references (only Abstractions + Serialization)
- [ ] No SDK types on public surfaces
- [ ] Implementations `internal sealed`; public surface is interfaces + contracts + `AddFrontierXxx()`
- [ ] Data the library does not own is reached through a **consumer-owned port**, never a
      reference pointing outward
- [ ] Architecture tests updated only if a boundary legitimately changed, with a recorded reason

## Dependencies

- [ ] Any new or bumped `PackageVersion` is deliberate — it becomes a **minimum version every
      consumer inherits**
- [ ] Vulnerability scan clean

## Review conduct

- Review for drift as much as for bugs: the failure mode here is a boundary quietly eroding.
- Feedback that changes a settled decision (an ADR in `docs/DECISIONS.md`) is raised as a
  question, not applied in-PR.
