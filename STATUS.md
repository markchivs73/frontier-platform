# Status — Frontier Platform (reusable .NET packages)

**Updated:** 2026-10-20
**State:** paused
**One-liner:** Twelve lockstep NuGet packages at v0.35.0; the S13.101/S13.107 governance and Key Vault signing work is merged, and nothing has been committed since 2026-09-16.

## Where it is
- `main` @ 914bc24 (v0.35.0-1): 62 PRs, all merged and none open. The last tag, v0.35.0 (#61), shipped the mapping-governance surface. #62 added only a live Key Vault test, so no release is owed.
- ES256 audit signing in Key Vault was proved against a real vault on 2026-09-16 (`KeyVaultLiveSigningTests`, 5/5). It is an on-demand gate that CI excludes with `Category!=RequiresAzureKeyVault`.
- Model-role mapping governance works end to end: proposals, a state machine, create-only versions, concurrency tokens, cross-role queries and typed refusals (ADR-PA32, ADR-PA34).
- Adoption downstream is behind. Per ADR-PA34, `frontier-workflow` is still on 0.22.2 and its S13.101 slice has not been built against 0.34+/0.35.
- Remote branches: 59 feature branches dated 2026-08-22 → 2026-09-16 are still on origin. Each one is the head of a merged PR (#1, #3–#62), but `git branch -r --merged origin/main` lists only `main` because the PRs were squash-merged. No branch has unmerged work. #2 (`docs/verify-fast-path`) was closed without merging, and its branch is already gone.
- Local housekeeping on the Mac: there is a local `pr39` with no upstream, a local `test/S13.79-close-the-branch-gaps` whose remote is gone, and a `.worktrees/` folder.

## Decisions made
- 2026-09-16 — Drop `base_version` and its approve-time 409. The concurrency token covers the same risk more precisely (ADR-PA34).
- 2026-09-16 — Fix the platform to meet doc 19/20 instead of amending the docs down. Adds the optional concurrency token, the cross-role `States` query, `IMappingVersionHistory` and typed refusals, and unseals `ContractViolationException` (ADR-PA34).
- 2026-09-16 — Sign audit records with ES256 inside Key Vault. Ed25519 is unavailable on standard vaults. A non-local boot is refused without `AuditSigning:KeyIdentifier` (ADR-PA33).
- 2026-09-16 — Append the execution audit chain under a head-document guard, and flag any fork from before the fix (ADR-PA31).
- 2026-09-15 — A mapping change is a proposal with a state machine. Shadow execution is deferred until after the cloud proof, and canary→fleet promotion is manual only (ADR-PA32).
- 2026-09-15 — Governance changes get their own signed chain. The RFC 8785 canonicalizer moves into Serialization (ADR-PA30).
- 2026-09-15 — Pin the model-role mapping version at execution start, gated by `pin_model_roles` (ADR-PA29).
- 2026-09-13 — A role's chain may resolve to a remote agent. Its tool use is recorded as self-reported (ADR-PA27, ADR-PA28).

## Blockers
- None in this repo. The next real value is consumer-side: bumping `frontier-workflow` from 0.22.2 to 0.35.0.

## Next actions
- [15m] Fix the stale package count. README says twelve, but `docs/DECISIONS.md` lines 15 and 497–498 ("all nine") and `.claude/skills/git-workflow/SKILL.md` line 47 ("all nine packages") still say nine. Make it one `docs: ...` PR.
- [15m] Prune the 59 merged remote branches, after Mark OKs it. Confirm each branch is the head of a merged PR with `gh api 'repos/markchivs73/frontier-platform/pulls?state=closed&per_page=100'` (check `merged_at`), then `git push origin --delete <branch>`.
- [15m] (Mac only) Local tidy-up: check `pr39` against #39 and delete it if it holds nothing new, delete `test/S13.79-close-the-branch-gaps`, then `git worktree list` and `git worktree prune` before removing `.worktrees/`.
- [1h] In `frontier-workflow`: bump the packages to 0.35.0 and adopt the ADR-PA34 source breaks (`expectedConcurrencyToken` on the four decisions; `MappingProposalQuery.States`).
- [1h] In `frontier-workflow`: amend doc 05 §5 using the S13.107 hand-off wording (ADR-PA33), doc 12 §5 for the pin placement (ADR-PA29), and doc 20 to drop `base_version`.
- [1h] Run the consumer emulator replay that ADR-PA29 says has not yet verified the mapping pin.
- [deep] Shadow execution after the cloud proof. When it lands, ADR-PA32 says to add the rule that approval requires evaluation evidence.
- [deep] Decide whether a live canary should block a new proposal. ADR-PA32 leaves this rule to Mark.

## Parked ideas
- `Frontier.Platform.TestSupport` as a tenth package for `EmulatorCosmos` and `ContractRoundTripAssertions`. `tests/Shared` is the highest-drift item and is deferred ("Drift ownership").
- A pending-marker document to close the check-then-act race on simultaneous proposes. Revisit only if proposals become machine-driven (ADR-PA32).
- A per-state projection document if the cross-partition proposals query starts to hurt (ADR-PA34).
- Choosing a licence before the repo is made public or changes hands ("No licence declared").
