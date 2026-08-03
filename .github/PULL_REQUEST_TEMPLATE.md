# Summary

<!-- What changed and why. If this breaks a published API, say so here first. -->

## Public API impact

<!-- One of: "No public surface change" / a summary of what was added / what broke and why.
     Must agree with the PublicAPI.*.txt diff and the commit scope (`scope!:` for a break). -->

## Checklist

All applicable boxes must be checked before approval. No box, no merge.
Authoritative detail: `.claude/skills/code-review/SKILL.md`.

- [ ] CI green — build, unit, integration, architecture tests, coverage gate, pack
- [ ] Coverage ≥95% per assembly; 100% on new code (exclusions justified)
- [ ] No warnings, no analyzer violations, no `#pragma warning disable`
- [ ] Doc comments on all new/changed public and internal surface
- [ ] `PublicAPI.*.txt` diff reviewed deliberately — every added line is a permanent obligation
- [ ] Breaking change? `[Obsolete]` names the replacement and the version, changelog updated,
      commit scoped `!`
- [ ] Package README updated if the surface it documents changed
- [ ] Architecture tests not weakened (changing one needs a recorded reason in `docs/DECISIONS.md`)
- [ ] New/bumped `PackageVersion` is deliberate — it becomes a floor every consumer inherits

## Verification

<!-- What you actually ran, and what it showed. Not "tests pass" — which tests, what output. -->
