# Decisions

Settled decisions for this repository. Deliberately short: this is the record of what must not
be re-litigated or accidentally undone, not a history of how the libraries were built. That
history lives in `frontier-workflow`'s `IMPLEMENTATION-PLAN.md` and `DESIGN-DECISIONS.md`,
which were **not** ported — they are the record of the other repo's build.

---

## ADR-PA1 — `Platform.Abstractions` is the zero-dependency kernel

`Frontier.Platform.Abstractions` has no Frontier references **and no third-party packages** —
only the base class library.

Every other package sits above it, so anything added here is inherited by all nine and by every
consumer. The cost of a casual addition compounds; the constraint is what keeps it cheap to
depend on.

Enforced by `PlatformAbstractions_HasNoFrontierOrThirdPartyDependencies`.

## ADR-PA2 — no platform library references `Frontier.Reason.*`

No assembly in this repo may reference, or contain a type depending on, any `Frontier.Reason.*`
assembly. Ever.

This is the guarantee that made the extraction possible and the one that keeps it reversible.
It is enforced at two levels because they catch different things:
`PlatformLibrary_DoesNotReferenceReasonWorkflowAssemblies` inspects the assembly reference
graph; `PlatformAssemblyTypes_DoNotDependOnReasonWorkflowTypes` (NetArchTest) inspects type
dependencies and catches leakage a reference check cannot see, such as a compile-linked source
file.

Where a library needs data the consuming solution owns, it declares a **consumer-owned port**
that the solution implements in its composition root. `IReferencedRolesSource` in
`ModelRoleConfig` is the worked example: the governance rule stays platform-owned, and how
referenced roles are discovered stays solution-owned.

**Never weaken one of these tests to make a build pass.** If a boundary genuinely needs to
change, the reason gets recorded here first.

## ADR-PA3 — the engine lands here, and it stays workload-neutral

`Frontier.Platform.Workflow.Model` is the first of the workflow engine's packages to arrive
(the consuming repo tracks this as ADR-E3a step 3). The platform is no longer only governance:
it is governance **plus** the engine those workloads run on.

The condition attached to that is not decorative. **No package in this family may name a
workload's contract type** — not in a signature, not in a doc comment the design agent reads
back as schema description, not in a test fixture. The first workload's vocabulary reaching a
platform package would recreate exactly the fusion this move exists to undo, and it would do so
invisibly, because everything would still compile and every test would still pass.

Three things follow, and each has already caught something real:

- Doc comments are API here. The schema generator reads these XML summaries and hands them to
  the design agent as field descriptions, so a workload-flavoured example in a `<summary>` ships
  as guidance to every consumer. Four such examples were neutralised on arrival.
- Test fixtures are subject to the same rule. `ContractTypeSetTests` arrived using the first
  workload's contracts as its fixtures and was re-based onto the engine's own.
- Dead types do not travel. Three types that nothing referenced (`ClientEntity`,
  `DeadLetterRecord`, `EventResolutionResult`) stayed behind rather than becoming permanent
  compatibility obligations for a feature nobody has built yet.

**The XML documentation is a runtime artifact, not just IDE comfort.** The schema generator
reads these summaries and turns them into the design agent's field descriptions, so the doc
file has to reach the consumer's output directory — and NuGet does not put it there by default.
Consumers that read it must set `CopyDocumentationFilesFromPackages`. This was found by the
consumer smoke test, not by reasoning: packing includes the `.xml`, restoring does not deploy
it, and the failure mode is silent — descriptions become empty and nothing throws. The smoke
test now asserts the file resolves.

The model itself depends only on `Platform.Abstractions` — not on `Serialization`. It declares
canonical wire shape through attributes and leaves the profile that writes those bytes to the
consumer, so nothing about adopting the model commits a consumer to a serializer.

Moving these types between assemblies changed no stored byte: `ContractMigrator` keys on the
stored `schema_version` string, never on a CLR type name (ADR-PA1). The golden files moved
byte-identical for the same reason — a golden rewritten to suit its new home would discard the
evidence it exists to carry.

---

## ADR-PA4 — the audit family speaks `artifact`, and pre-rename records are refused rather than migrated

`AuditTelemetryRecord.section_key`, `AgentInvocation.section_key` and
`ValidatorOutcome.target_section_key` become `artifact_key` / `target_artifact_key`.
`AuditRecord` and `SignedAuditRecord` bump to schema **2.0**. This completes the vocabulary
decision the consuming repo's ADR-E3a deferred to the point where audit and the interpreter are
settled together (its D5(a)); it is a **breaking public API change**, shipped as `audit!:`.

**There is deliberately no migration adapter, and that is the whole substance of this ADR.**

Verification here does not hash the stored bytes. `AuditChainVerifier.IsSignatureValid`
rehydrates the record, recomputes its canonical bytes through `CanonicalProfile`, and compares
the result to the stored hash and signature. A migration adapter would therefore *not* rescue a
pre-rename record: it would rehydrate as 2.0, re-serialize to different bytes, and fail its
signature. The record would read fine and verify as broken — and a broken hash chain is the
signal this system reserves for **tampering**.

So the choice was never "migrate or not". It was: make a schema change indistinguishable from
altered evidence, carry a permanently versioned hasher through the most safety-critical code in
the platform, or refuse to read incompatible records at all. The third is the honest one at this
point in the project's life, and `AuditRecordSchemaGuard` implements it: an incompatible major
throws `ContractViolationException` naming the version found, rather than returning degraded
evidence. Minor versions stay readable — omit-null defaults cover a field a build has not heard
of; only a major says the bytes mean something different.

**Cost, measured rather than assumed.** Four signed records existed at decision time, all in the
local emulator, all carrying populated `section_key` (19 occurrences across their agent
invocations), all reseedable. No deployed environment exists. That is the entire protected
population, and it is the cheapest this rename will ever be — the moment real evidence is
retained, the only remaining option is the versioned hasher, and it becomes permanent.

Pre-rename golden files are not preserved as fixtures. With no adapter they would assert nothing
and imply support that does not exist; git history holds the 1.0 bytes if they are ever needed.

---

## ADR-PA5 — the platform is two tiers now, and the dependency runs one way

`Frontier.Platform.Workflow.Orchestration` — the interpreter — depends on Audit, HITL,
ContextAssembly, ModelRoleConfig, Guardrails and Resilience. Until now every platform library
referenced only Abstractions and Serialization, and an architecture test said so of *all* of
them.

That flat rule was written when the platform was only governance. The engine is a **composition
layer**: walking a DAG means calling an approval store, assembling context, resolving a model,
staging audit telemetry. Re-abstracting the platform's own interfaces behind a second set of
ports to preserve flatness would be ceremony, not design — `IApprovalStore` is already an
interface, and wrapping it would buy nothing.

So the graph gains a tier:

- **Governance tier** — Abstractions, Serialization, Audit, ContextAssembly, Guardrails, Hitl,
  ModelRoleConfig, Observability, Resilience. Still flat: each references only Abstractions and
  Serialization among platform libraries, so each stays independently consumable and adding one
  never drags in a sibling.
- **Engine tier** — Workflow.Model, Workflow.Orchestration. May depend on governance.

**The half that carries the weight is the direction.** `GovernanceLibrary_DoesNotDependOnTheEngine`
asserts that no governance library references an engine assembly. A two-tier graph is only worth
having if the dependency runs one way: a solution that wants audit or approvals and no
interpreter at all must still be able to take them. A single reference in the wrong direction
would collapse the tiers back into one graph — and it would compile perfectly, which is why it
is a test rather than a convention.

This is not a new decision so much as the consequence of one already taken: the consuming repo's
ADR-E3a D1 put the engine in this repo. The tiering is what that means structurally, written
down rather than left implicit.

*Found while doing it — the second invisible workload coupling.* The orchestrator body decided
whether an MCP tool was a write by consulting a hardcoded set of two demo connectors' tool
names. No type-based architecture test could see it: the coupling was in string literals. It is
now `IMcpWriteClassifier`, a consumer-owned port, and — because it is consulted from inside the
orchestrator body — its contract requires a pure, replay-stable answer, the same requirement
`IResiliencePolicyProvider` already carries. The first such coupling was `EntryContractBuilder`;
a third is known and scheduled in the consuming repo (a design prompt naming the workload's
entry contract). The pattern is worth stating: **the couplings that survive a type-level guard
are the ones written as strings.**

---

## ADR-PA6 — the compiler joins the engine tier, and publish governance comes with it

`Frontier.Platform.Workflow.Compiler` completes the engine tier: structural validation, the
design-language schema, and the publish lifecycle — draft, validate, propose, approve, publish,
pin, retire — over Cosmos.

**Publish governance moved deliberately, and it was not obvious.** The consuming repo's ADR-E3a
named "the DefinitionCompiler engine + structural rules" and said nothing about the lifecycle,
so it could equally have stayed. It moved because none of it names a workload: versioned publish
with approval and pinning is what *any* deployment needs, and leaving it behind would mean a
second workload reimplementing draft/propose/approve from scratch — the exact fusion the
severability work exists to undo. The precedent settled it: `Platform.Hitl` is already a
Cosmos-backed, solution-agnostic store driven by its callers, so the platform owning a store is
established rather than novel.

**There were no workload rule packs to leave behind.** ADR-E3a anticipated them; all 29 rules
turned out to be structural — graph shape, data-edge agreement, determinism, versioning,
retention. What stays registerable is the *extension point*: a rule is an
`IDefinitionValidationRule` registration, so a deployment adds its own policy without forking
anything. The shipped set encodes what makes a workflow **executable**, never what makes one
acceptable to a particular business.

*The stricter analyzers here caught something the consuming repo did not run.* `AnalysisLevel`
is `latest-all`, and `ComputeDefinitionHash` tripped CA1850 and CA1308. CA1850 was adopted —
behaviour-identical. **CA1308 is suppressed on purpose**: the hash is wire-visible, stored on
every published definition, pinned by running executions and used as a cache key, so switching
to upper-case hex would silently invalidate every stored hash and every pin. The rule guards
locale-sensitive normalisation of user text; this is hex from a digest. Worth stating plainly,
because "satisfy the analyzer" is the obvious and wrong move.

*Consumability was checked before the move this time.* When the interpreter moved it took three
PRs to become implementable from another assembly (ADR-PA5). Here the four types a consumer
reaches were identified up front, and the smoke test wires the compiler and implements every
catalogue port — so it passed first time. The lesson generalises: **a package's public surface
is not a property you can observe from inside the repo that produces it.**

---

## ADR-PA7 — the design agent lives here, and names no consumer's contract

The chat-designer protocol joins `Workflow.Compiler`, completing the engine's arrival. Its
dependency set was already entirely in that package, so it needs no thirteenth library.

`ChatDesignerService` is **internal**: consumers resolve `IChatDesignerService`. A concrete
class is a permanent obligation, and nothing outside needs to construct this one.

**The condition on it living here is the one ADR-PA3 stated and this move tested.** The agent's
system prompt used to name one deployment's entry contract and dynamic field as *string
literals* — invisible to every type-level guard, and about to be published. `IEntryContractCatalog`
now supplies them, alongside `IChatClient` and `IDesignerModelProvider`.

*The rule that came out of it, worth keeping:* four workload couplings were found across this
programme — a type reference, a hardcoded classification, a demo connector quoted in an error
message, and this prompt. Only the first was visible to an architecture test. **The couplings
that survive a type-level guard are the ones written as strings**, so a move should include a
literal scan of the moving source for the consumer's contract names and identifiers. Applied to
this move, it found exactly one remaining instance — a `//` comment in `ExampleSkeletonBuilder`
that shipped at v0.7.0 — and confirmed the designer itself was clean.

*Agreement matters more than neutrality here.* The agent is told to request a field the runtime
then reads. If those ever disagree, every workflow the agent designs validates and then fails
live, in a way that looks like a model problem. The README says plainly: derive both from one
constant.

---

## ADR-PA8 — a definition replayed from history migrates, and that is what keeps replay working

The workflow definition rides inline in the orchestration input (the consuming repo's ADR-2), so
it lives in durable history and is rehydrated on **every replay**. Snapshots migrated; stored
definitions migrate as of ADR-PA7's release; history did not.

The consuming repo raised this as a deploy-time fork — *freeze the definition model at
deployment, or build the history seam first*. Investigation collapsed it: the worker already
configures `JsonDataConverter(CanonicalProfile.Options)`, System.Text.Json honours property-level
converters, and the converter's only dependency is the model itself. Moving it from the compiler
into `Workflow.Model` and attributing one property closes the gap. The expensive option turned
out to be unnecessary.

**Migrating on a replay path deserves suspicion, so state why it is safe.** Determinism requires
that identical recorded bytes yield identical decisions. The recorded bytes never change, and the
adapter is a pure total function of them, so every replay yields the identical definition — pinned
by a test that rehydrates three times and compares canonical bytes. More importantly the
migration *restores* the run's own semantics rather than altering them: the value was always
`"scope"`, only the property carrying it was renamed. Without migration a running execution
replays with null artifact keys and makes **different** scheduling decisions than the run it is
replaying, which is the one thing replay may never do. The seam is not a risk to replay; its
absence was.

**What this does not cover, and must not be read as covering.** This is safe because the change
was a *rename* — adaptable, value-preserving. A change to what a field **means**, or one that
drops information, cannot be adapted and remains a drain-and-declare event: a named phase
boundary, as ADR-PA4 took for audit and the consuming repo's ADR-E15 exception took for activity
names. The seam widens the class of changes that are cheap; it does not make every change cheap.

Activity inputs and results also travel in history. The pattern established here applies to them,
but each contract needs its own adapters — nothing here migrates them, and claiming otherwise
would be worse than the gap.

---

## ADR-PA9 — a rule registered into a tier nothing executes is worse than no rule

`determinism.sample-eval` is retired. It was the compiler's only `RuleTier.Runtime` rule, and
**nothing executes that tier** — `DefinitionValidator` runs Pure and Resourced, and no other code
references Runtime at all. The rule body also returned no findings unconditionally. So it was
inert twice over, while appearing in the catalogue as a governance rule the product performs.

That is the failure worth naming: **the catalogue is a claim.** A row in it says "this is
checked". A rule that cannot fire makes the claim false in a way that no test catches, because
every test of an empty rule passes.

Its own doc comment compounded it by citing a blocker that had been removed — "Phase 1 has no
designer sample-data channel yet; wiring lands with S9.38". S9.38 shipped, `ITestRunService`
exposes `SampleInputs`, and the comment still read as pending work. A stale rationale is how an
inert rule survives review.

*Retired rather than built, deliberately.* Building it means a design-time overload of
`PredicateEvaluator` (the live decision-routing code used inside the orchestrator body) **and** an
executor for the Runtime tier inside the test-run channel. That is a feature, for an Info-severity
convenience, and the decision legibility it would provide was already delivered another way — test
runs surface the selected branch and skipped nodes. Doc 13 §4.2 R4 stays specified and unbuilt,
with the first workload wanting predicate previews as its trigger.

*What replaces it is a guard, not a comment.* `NoRuleIsRegisteredIntoATierNothingExecutes` fails if
anything is registered into Runtime again. `RuleTier.Runtime` keeps its place as the declared seam
— removing it would hide the gap rather than close it — but now says in its own documentation that
it has no executor, so the next person registering into it learns that at the point of the mistake.

**This is the first breaking change to a published package here.** Everything since v0.1.0 has been
additive. `DeterminismSampleEvalRule` was public and is gone.

---

## ADR-PA10 — orchestrator purity is checked by walking what replay executes, not what the source says

Hard invariant 2 — orchestrator bodies are pure, no `DateTime.Now`, no GUIDs, no I/O — had no
mechanism. `GraphOrchestrator`'s own doc comment states the rule, the behavioural orchestrator
tests are thorough, and nothing failed the build when a body broke it. This is the same shape as
ADR-PA9: a claim written where a check should be. It is the more dangerous instance, because a
non-deterministic body does not throw. It replays, diverges from the recorded history, and
corrupts the execution quietly and later.

**The guard reads IL, not source, and follows the call closure.** `GraphOrchestrator.RunAsync` is
four delegating lines; the walk it governs lives in `GraphOrchestratorSteps`. A check scoped to the
orchestrator type would have passed while inspecting nothing. Orchestrators are discovered by their
Durable Task interface rather than listed, so a second orchestrator is covered on arrival — the
property the P1 backlog item asked for.

*The finding that justifies the whole approach.* The first working version passed against a
`DateTime.UtcNow` deliberately planted in `GraphOrchestratorSteps`. An `async` method's body
compiles into a generated state-machine type that **no call instruction points at** — the stub
hands it to a builder — so a call-following walk sails past every asynchronous body, which here is
nearly all of them. The guard reported success while checking almost nothing. It now follows
`StateMachineAttribute` into the generated type, and a regression test pins that it reaches a
`MoveNext` body rather than only the stub that launches it.

The lesson generalises past this test: **a guard's own green result is evidence of nothing until it
has been shown to fail.** Planting a real violation, in the real code, is the cheapest way to learn
that a mechanism is decorative — and it is the second time in this batch that the check needed
checking.

**What it deliberately does not cover, stated rather than implied.** The traversal stops at the
platform boundary, so an impure implementation of an injected port — `IMcpWriteClassifier`,
`IRollbackPlanner`, `IResiliencePolicyProvider`, all consulted inside the body — is invisible to
it. Those carry a documented purity contract instead, which is a weaker guarantee honestly stated.
Activities are invisible for the right reason: they are reached by name through the durable
context, never by a call instruction, and doing I/O is their job.

**K10 gets the same treatment.** One shared `JsonSerializerOptions` profile is invariant 1, and it
too was unenforced. Two source rules now hold it: only `Frontier.Platform.Serialization` may
construct the options, and every `JsonSerializer` call must pass the canonical profile. This found
one real fork — `Phase1EngagementContextStore` serialized with framework defaults — fixed here
rather than allowlisted, since for a bare string the bytes are identical and the exception would
have outlived its reason. A passed-through `options` parameter is tolerated, because
`ContractMigrator` and the migrating converters take the live options and must honour them; that
tolerance is the rule's stated limit, not an oversight.

---

## ADR-PA11 — the execution-id format is kernel vocabulary, because `internal` did not survive the splits

`ExecutionId` joins `Frontier.Platform.Abstractions`: `Mint`, `Parse`, `ParseOrNull` and the
`Separator` constant for the `{engagementId}::{workflowId}` instance-id format (invariant 3).

**The reason it belongs in the kernel is the count.** The format was written out in *eight* places
across two repositories — twice in this one. Two helper copies (`Workflow.Orchestration` and
`Audit`), two identical test suites covering them, one sanctioned mint site in the consuming
repo's composition root, one duplicate mint, and two hand-rolled splits in its controllers, one of
which had already drifted (it returns everything after the first separator, so a dispatcher child
id would yield `{workflowId}::{workItemId}`).

None of that was carelessness, which is the part worth recording. Each copy was created at an
**assembly boundary**, by someone with no other option: `Audit`'s copy appeared when it was severed
from `Orchestration` at S11.6 — the note of the day says so plainly ("Audit keeps its own") — and
the consuming repo's four appeared when the engine moved out at E3b. An `internal` helper is a
correct decision inside one assembly and a silent instruction to duplicate after a split. This
codebase has now split twice, and the format duplicated both times.

So the generalisable rule is not "make things public". It is that **a repo split converts every
`internal` shared helper into a fork waiting to happen**, and the ones to promote are those whose
callers ended up on both sides of the new wall. That test names this one exactly.

*Kept deliberately small.* The segments come back as a named tuple, not a declared type. ADR-PA1's
whole argument is that the kernel is inherited by every package and every consumer, so a record
carrying two strings — fourteen public symbols once the analyzer enumerated them — would have been
a poor trade for what a tuple says with none.

*Two readings, because the callers genuinely differ.* `Parse` throws: the platform's own sites
receive ids the platform minted, so a malformed one is a programming error. `ParseOrNull` returns
null: both controllers hold identifiers that may legitimately not be execution ids and fall back to
using the value as-is. Collapsing them onto the throwing form would have changed API behaviour
silently, which is the failure this ADR is about.

`Mint` additionally refuses a segment containing the separator. Such an id looks well-formed and
parses back to values other than those minted — visible only downstream, if at all.

*Publishing `Mint` does not loosen invariant 3.* The invariant governs who mints an id **for
scheduling**, not who may know the format, and the consuming repo's guard gets stronger for it: it
currently matches an interpolated-string shape and cannot distinguish `{engagementId}::{workflowId}`
from any other two-part key, so it carries an allowlist. A named call site needs no such heuristic.

Additive: the deleted copies were `internal`, so no published surface changed.

---

## Lockstep versioning

All nine packages take their version from a single git tag via MinVer. A change to one library
republishes all nine at the new version.

The alternative — nine tag prefixes, nine changelogs, nine version histories — buys precision
nobody has asked for and costs real ceremony on every release. Accepted trade: one tag, one
changelog, some packages republished with no changes in them.

While the major version is `0`, breaking changes may ship in a minor. After 1.0, majors only,
at deliberate boundaries.

## Deprecation policy (restated for package consumers)

`frontier-workflow`'s rule is that deprecated code survives "until a phase boundary". Consumers
of a package have no phase boundaries, so:

- `[Obsolete]` must name **both** the replacement and the version it was deprecated in.
- Removal only in a major version.
- A deprecation is always a changelog entry. Never silent.

## Public API surface is tracked

Every public member appears in that library's `PublicAPI.Shipped.txt` or
`PublicAPI.Unshipped.txt`; `Microsoft.CodeAnalysis.PublicApiAnalyzers` fails the build otherwise.

In a single solution an accidental `public` is harmless. Here it is a permanent compatibility
obligation to whoever resolved the version. This makes each surface change a reviewable diff
and turns the major-vs-minor decision into diff-reading rather than judgement.

## Dependency floors are a published decision

Central package management means the exact `PackageVersion` pinned here becomes a **minimum
version in every published nuspec**, which every consumer inherits into its resolution graph.
Bumping a floor is no longer an internal choice. Do it deliberately, not casually.

## No licence declared

These are private packages on an authenticated feed, so no `PackageLicenseExpression` is set.

Known consequence: the rights are undefined if this repository is ever made public or changes
hands. Revisit before either happens.

## Newtonsoft.Json is a Cosmos SDK build requirement

Several libraries reference `Newtonsoft.Json` without using it in code. The Cosmos SDK's build
targets **mandate an explicit reference** and fail the build without one, regardless of the
fact that serialization here goes through System.Text.Json via `CanonicalProfile`.

Do not "clean up" these references. Microsoft intends to migrate the Cosmos SDK to
System.Text.Json in a future major version, with no published timeline; the references can go
then. An abstraction layer isolating Cosmos-facing serialization was considered and deferred as
not yet worth its cost.

---

## Drift ownership

Some concerns exist in both this repo and `frontier-workflow`. The principle: **split by
audience, not by copy.** A file that says something different in each repo cannot drift; a file
that says the same thing in both always will.

| Concern | Owner | Mechanism |
|---|---|---|
| `canonical-serialization` skill | split | This repo owns the **producer** half: profile definition, converter authoring, golden-file rules. `frontier-workflow`'s copy is the **consumer contract** — put `[JsonPropertyOrder]` on every property, never construct your own options, golden-file every contract. The profile itself lives in the package. |
| `cosmos-conventions` skill | split | This repo owns the conventions and the platform containers. `frontier-workflow` keeps its own container inventory and snapshot-writer rules. Containers named here but owned there (`execution-snapshots`) are **ports**, verified for presence only. |
| `library-boundaries` skill | split by scope | Here: the platform-internal graph and how ports are declared. There: the `Reason.*` map, plus — once it consumes packages — a rule that a `ProjectReference` to a platform library is a violation, the mirror image of ADR-PA2. |
| `tests/Shared` | duplicated, knowingly | `EmulatorCosmos` and `ContractRoundTripAssertions` are generic and both repos need them. The clean fix is a tenth package, `Frontier.Platform.TestSupport`, holding **only** those two — not `HitlFixtures`/`TelemetrySamples`, which would drag half the graph into it. Deferred. **This is the highest-drift item in the repo**; it is a known state, not an accident. |
| `.editorconfig`, `Directory.Build.targets`, `coverlet.runsettings`, `coverage_by_assembly.py`, `check-vulnerabilities.sh` | `frontier-workflow` owns; this repo vendors | Genuinely identical, no audience split available. Each carries a provenance header naming the source commit and a runnable drift-check command. Both clones sit side by side under `~/Documents/repos`. |
| PR template, CI workflows, `CLAUDE.md` | divergent by design | These intentionally say different things. **Do not "helpfully" re-sync them.** |

## Skills deliberately not ported

`dtf-determinism` (no DTF here), `implementation-plan` (no plan file), `local-dev` (no Aspire,
DTS or Playwright). `definition-of-done` was folded into `code-review` and the PR template
rather than kept separate.
