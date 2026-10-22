# Status — Frontier Platform (reusable .NET packages)

**Updated:** 2026-10-22
**State:** active
**One-liner:** Twelve lockstep NuGet packages at v0.37.0 (tagged 2026-10-22); v0.36.0 and v0.37.0 shipped this week for frontier-workflow's S13.119 and S13.120, and that consumer is on 0.37.0.

## Where it is
- `main` @ 02a110b = v0.37.0: 65 PRs, all merged, none open. v0.36.0 (#64, `hitl!:`) added `IApprovalStore.DecideAsync` with `ApprovalDecisionResult`/`ApprovalDecisionTransition` (one ETag-guarded `pending|escalated → decided` write), the visit-bound stale-decision guard in `GraphOrchestratorSteps.DecideAsync`, and the source break `ITestRunExecutor.RaiseGateDecisionAsync(executionId, engagementId, …)` — ADR-PA17 amended. v0.37.0 (#65) added `ExecutionSnapshot.failure_detail` (`ExecutionFailureDetail`, built by `FailureDetailSummary`: message only, redacted, bounded to 512) — ADR-PA26 amended, additive.
- Consumer adoption is current: frontier-workflow `FrontierPlatformVersion` is 0.37.0 (#360). Its docs still lag two platform ADRs: doc 12 does not mention `pin_model_roles` (ADR-PA29) and doc 20 still carries `base_version` (dropped by ADR-PA34); doc 05 has the ES256 wording (ADR-PA33).
- Release routine as practised: merge → `git tag -a vX.Y.Z <merge sha>` → push → `publish-packages.yml` (build, scan, tests, coverage gate, pack, push). `.claude/skills/git-workflow/SKILL.md` additionally says "changelog updated, Unshipped moved to Shipped" before tagging — there is no changelog file and no Shipped file has been touched since #23 (2026-09-01), so the skill and the practice disagree (see Next actions).
- ES256 audit signing in Key Vault was proved against a real vault on 2026-09-16 (`KeyVaultLiveSigningTests`, 5/5), an on-demand gate CI excludes with `Category!=RequiresAzureKeyVault`.
- Housekeeping: 65 remote branches besides `main`, each the head of a squash-merged PR (`git branch -r --merged` cannot tell, so check `merged_at` via the API before pruning). On the Mac: ~40 local branches tracking those remotes, `pr39` with no upstream, `test/S13.79-close-the-branch-gaps`, and `.worktrees/dispatcher-tests` on `test/dispatcher-cluster-tests`.

## Decisions made
- 2026-10-22 — ADR-PA26 amended: a `paused_on_failure` snapshot carries `failure_detail` {node, exception type, message}; only the exception's message, with `Bearer`/`Basic` tokens and `sig=`/`key=`/`token=`/`secret=`/`password=` values redacted, bounded to 512 characters; optional under the ADR-E15 floor; sweep-healed snapshots carry none. v0.37.0.
- 2026-10-22 — ADR-PA17 amended: a gate decision is a single ETag-guarded transition written by the API *before* the DTF raise; the orchestrator ignores a decision whose `RequestId` is not the open request's, because the gate event name carries no visit number; `ITestRunExecutor.RaiseGateDecisionAsync` takes the sandbox engagement id (source break, shipped in a minor under the 0.x rule). v0.36.0.
- 2026-10-22 — The fake orchestration context fails after 1000 plain-event redeliveries (`PlainDeliveryLimit`) instead of spinning; a mismatched fixture `RequestId` had cost a 22-minute hang.
- 2026-09-16 — Drop `base_version` and its approve-time 409; the concurrency token covers the same risk more precisely (ADR-PA34).
- 2026-09-16 — Fix the platform to meet doc 19/20 instead of amending the docs down: optional concurrency token, cross-role `States` query, `IMappingVersionHistory`, typed refusals, `ContractViolationException` unsealed (ADR-PA34).
- 2026-09-16 — Sign audit records with ES256 inside Key Vault; a non-local boot is refused without `AuditSigning:KeyIdentifier` (ADR-PA33).
- 2026-09-16 — Append the execution audit chain under a head-document guard and flag any fork from before the fix (ADR-PA31).
- 2026-09-15 — A mapping change is a proposal with a state machine; shadow execution deferred, canary→fleet promotion manual only (ADR-PA32).

## Blockers
- None in this repo.

## Next actions
- [15m] Decide the release routine (Mark): either follow `.claude/skills/git-workflow/SKILL.md` lines 58–59 at the next tag (move each package's `PublicAPI.Unshipped.txt` lines into `PublicAPI.Shipped.txt`, add a changelog) or amend the skill to describe what is actually done (tag at the merge commit; the Unshipped file is the record). Done = the skill and the next tag agree.
- [15m] Fix the stale package count in one `docs:` PR: `docs/DECISIONS.md` lines 15 and 498 and `.claude/skills/git-workflow/SKILL.md` lines 47–48 still say "all nine packages"; README says twelve. Done = `grep -rn "nine" docs/DECISIONS.md .claude/skills` finds no package count.
- [15m] Prune the 65 merged remote branches after Mark OKs it: confirm each is a merged PR head with `gh api 'repos/markchivs73/frontier-platform/pulls?state=closed&per_page=100' --paginate` (`merged_at` set, `head.ref`), then `git push origin --delete <branch>`.
- [15m] (Mac only) Local tidy: `git branch --merged main` lists nothing because PRs are squash-merged, so delete the local tracking branches by name once their remotes are pruned; check `pr39` against #39 first; `git worktree prune` before removing `.worktrees/`.
- [1h] Hitl follow-ups from S13.119's evidence, next minor: guard the escalation upsert with the same `ApprovalDecisionTransition` so a timer firing beside a decision cannot write `escalated` over `decided`; give `GetDecidedAsync` a `decided_at` window instead of a cross-partition scan that grows forever.
- [1h] In frontier-workflow: amend doc 12 §5 for the mapping pin (ADR-PA29) and doc 20 to drop `base_version` (ADR-PA34); run the emulator replay ADR-PA29 says has not yet verified the pin.
- [deep] S13.113 (consumer plan): RFC 8785 canonicalisation of untyped JSON inside contracts (ADR-E2's fingerprint rule) — the first platform slice of the E27 "contracts as data" track; lands before S13.114.
- [deep] Shadow execution after the cloud proof; when it lands, ADR-PA32 says approval requires evaluation evidence. Decide whether a live canary blocks a new proposal (ADR-PA32 leaves this to Mark).

## Parked ideas
- A `failure_detail` for sweep-healed snapshots: the consumer's `RecoverySweeper` has the innermost message but cannot name the failing node honestly, and the record requires one. Revisit if "paused, no detail" turns out to mislead.
- `Frontier.Platform.TestSupport` as a thirteenth package for `EmulatorCosmos` and `ContractRoundTripAssertions`; `tests/Shared` remains the highest-drift item ("Drift ownership").
- A pending-marker document for the propose race (ADR-PA32) and a per-state projection if the cross-partition proposals query starts to hurt (ADR-PA34).
- Choosing a licence before the repo is made public or changes hands ("No licence declared").
