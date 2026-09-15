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

## ADR-PA12 — an execution id is read from the right, and reading it from the left was a live defect

> **Superseded by ADR-PA15 (2026-09-01):** the readers this rule governed are gone. It remains the
> correct reading of any id written under it, and its lesson about production-shaped test data is
> carried forward rather than retired.

`ExecutionId.Parse` splits at the **last** separator. Everything before it is the engagement id;
the final segment is the workflow id.

This is what doc 16 §3 has always specified — *"the workflow suffix is always the final `::`
segment of an instance ID, so instance-ID parsing stays unambiguous regardless of how many segments
the engagement ID itself has"* — and it is not what any implementation did. Both retired
`ExecutionIdParser` copies took `parts[0]` and `parts[1]`, as did the consuming repo's two
hand-rolled splits.

**That was wrong in production shape, not merely in theory.** Engagement ids are composite and
config-templated (doc 16 ADR-E2: `{type}::{client}::{site}`, e.g. `E2E::Acme::Admin-Website`), so a
real execution id has four or more segments. Reading from the left returns `("E2E", "Acme")`: wrong
engagement, wrong workflow. The three callers are the audit spine —

- `AuditSigner` derives the audit record's **partition key** from it,
- `CosmosAuditRecordStore` derives the partition key it reads by,
- `AuditConsolidator` writes both identity fields into the consolidated record.

So every audit record for a composite-engagement execution would have been written to a partition
named after the engagement's *type*, carrying a workflow id that was actually the client name. On
the evidence chain this system treats as its moat.

**Why it survived.** Every test in both suites used `eng-1::wf-1`. A single-segment engagement id
makes reading from the left and reading from the right the same operation, so the bug was invisible
to a suite that never wrote down a realistic id. It surfaced only because `Mint` validated its
arguments and the consuming repo's E2E fixtures — which *do* use `E2E::Acme::HQ` — began failing.
The fixtures had been carrying the counter-example all along; nothing asked them.

The lesson is narrower and more useful than "test more": **an identifier's test data has to have the
shape production gives it.** A simplified id is not a simplified test, it is a different function.

*What stays ambiguous, deliberately.* A dispatcher child appends `::{workItemId}`, and
`eng-1::wf-1::item-1` cannot be told from a two-segment engagement id running `item-1`. `Parse` is
therefore defined for top-level ids — which is all three callers hold, audit consolidation running
only against top-level executions — and a child id read here yields the work-item id as its final
segment. That is pinned by a test rather than left to be discovered, and doc 16's own two
statements (§3's "always terminal" and §4's child format) do not agree; the consuming repo owns
reconciling them.

Behavioural change to a published package, in the direction of the specification. Signatures are
unchanged.

---

## ADR-PA13 — migration adapters go direct to current, so each one must be brought forward at every bump

`ContractMigrator` **does not chain**. It reads the stored `schema_version`, looks it up once, and
calls that single adapter. There is no 1.0 → 2.0 → 3.0 walk, and none is planned: a chain needs
every intermediate CLR shape kept alive as a type, which is a permanent tax for a rare event.

The consequence is the thing to record, because it is not visible from the code. **Every registered
adapter must reach the current schema by itself**, so a bump to 3.0 obliges someone to revisit the
adapter written for 1.0 back when current was 2.0. If they do not, it does not break — it keeps
producing a 2.0-shaped object which is then deserialized as the 3.0 type with the new fields left
at their defaults. Nothing throws. An execution paused at a gate long enough to cross two versions
is exactly what meets it, which is why the consuming repo tracks this as E15b.

*The guard is generic and self-updating.* `MigrationReachesCurrentSchemaTests` runs every registered
adapter over the real legacy goldens and asserts the result carries **the contract type's** current
version — obtained by deserializing a current golden with its `schema_version` removed and letting
the property initializer answer. Nothing in the test needs editing at the next bump; it simply
starts failing until each adapter is brought forward. Two companion rules: no adapter may be
registered for the current version, and the snapshot and definition tables must cover the same
stored versions, since they describe one wire break and a version adapted for only one of them
leaves half a paused execution readable.

**The existing tests could not have caught this, and the reason generalises.**
`ArtifactVocabularyMigrationTests` asserted the migrated version equalled
`ArtifactVocabularyMigration.RenamedSchemaVersion` — *the adapter's own constant*. At 3.0 that
constant still reads "2.0", so the assertion passes while the adapter is wrong. Verified rather
than argued: with `ExecutionSnapshot` temporarily moved to 3.0, the new test fails with
`Expected: "3.0", Actual: "2.0"` and the old one passes five for five. Both sites now compare
against the type. **An assertion that compares a thing to its own constant survives the change it
appears to guard** — the same shape as ADR-PA9's inert rule and ADR-PA10's decorative guard, which
is three times in one batch that a green test was the camouflage.

---

## ADR-PA14 — the durable engagement-context store is registrable, not just present

Shipping an implementation nothing can register is the same as not shipping it. `ContextAssembly`
carried `CosmosEngagementContextStore` — epoch-versioned, tested against the emulator — as an
`internal` type that no registration path reached, while `AddFrontierContextAssembly` hardwired the
compiled-in `Phase1EngagementContextStore` (two hard-coded engagement ids). A consumer running real
engagements therefore resolved **no dynamic context at all** for anything outside that catalogue,
and its entry nodes contract-violated permanently. frontier-workflow found this the first time an
automated test started an execution on a freshly created engagement (its S13.50).

**Decision.** `AddFrontierCosmosEngagementContext(IConfiguration)` is public and opt-in: it binds
this library's own `CosmosOptions` (each Cosmos-using library binds its own, so none depends on
another's registration order), `TryAdd`s the `CosmosClient` so a consumer's shared client wins, and
**replaces** rather than appends the `IEngagementContextStore` registration — leaving both would
make the resolved implementation depend on call order, which is true in one head and false in the
other. `AddFrontierContextAssembly` still needs no Cosmos configuration, so composing context
assembly in a test or a tool stays free of a database.

**Consequences.** The store's `Container` constructor is unchanged, so its integration tests are
untouched. Consumers opt in explicitly; those that do not keep today's behaviour exactly. Public
surface grows by one extension method and one options type, both tracked in `PublicAPI`.

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

## ADR-PA15 — an execution id is written, never read; identity travels as fields

`ExecutionId.Parse` and `ExecutionId.ParseOrNull` are removed. `Mint` stays. Nothing splits,
slices or pattern-matches an execution id to recover the parts that went into it; the engagement
and workflow travel as typed fields on the contracts that need them —
`ConsolidateAuditInput.EngagementId`/`WorkflowId`, and the `engagementId` parameter now taken by
`IAuditSigner.VerifyAsync`, `IAuditRecordStore.GetAsync` and `IAuditQueryService.GetAsync`.

**The id was doing two unrelated jobs.** As an addressing key it must be unique per run and may be
opaque; as a metadata carrier it must be structured and unambiguous. Every recurring defect here
came from the second job, never the first. ADR-PA12 was a parse bug: splitting from the left on a
composite engagement id (`E2E::Acme::Admin-Website::wf-sow`) returned the engagement's *type* as
the engagement and the client as the workflow, mis-partitioning every audit record for a real
engagement. The dispatcher child id (`::{workItemId}`) is indistinguishable from a longer
engagement id by inspection, so `Parse` had to be *documented around* rather than fixed. Both are
readings of a format that was never itself wrong.

**The evidence that this is a subtraction, not a redesign.** All five readers across the two
repositories wanted a single value between them — the engagement id, for a Cosmos partition key —
and two discarded the rest outright (`var (engagementId, _) = …`). Every caller already held it as
a typed value before it called. `AuditConsolidator` was the clearest case: it parsed the engagement
and workflow out of the string, *then* loaded the `ExecutionSnapshot` that has carried both as
required typed fields all along, and wrote the parsed values rather than the snapshot's.

No behaviour changes. The same values reach the same fields by a route that cannot silently be
wrong — and a mis-set field is a compile error or a validation failure, where a mis-parsed id was
neither.

*What this costs.* A public-surface break on the kernel (ADR-PA11) and on three audit interfaces,
so consumers update in lockstep. `ConsolidateAuditInput` gains two required properties, which is a
wire change to a DTF activity input — in-flight orchestrations replay the shape they recorded, so
this must not be deployed alongside a mid-flight execution that started before it. That is the
ADR-E15 accept-and-record posture, flagged rather than assumed.

*What it does not settle.* Whether the id gains a per-run discriminator, which is the consuming
repo's S13.51 and its ADR-EX1. This decision is a prerequisite for it: a run token in the id is
only safe once nothing is reading the id for its parts. `ExecutionId.Mint` therefore still requires
a single-segment workflow id, because the minted `{engagementId}::{workflowId}` remains the
derivable key that the affinity claim will be built on.

*Supersedes ADR-PA12* — not by contradicting it. "Read from the right" remains the correct reading
of any id written under it, and its lesson outlives its rule: **an identifier's test data has to
have the shape production gives it.** A single-segment engagement id made reading from the left and
from the right the same operation, which is why a whole suite missed the defect. The replacement
tests mint against composite engagement ids for exactly that reason.

---

## ADR-PA16 — a run is a field and a suffix, and the affinity key is what it extends

> **Superseded in part by ADR-PA20 (2026-09-10):** the suffix is gone — the run token is the whole
> instance id. Everything else here stands: the affinity key is still `Mint(engagementId, workflowId)`,
> still what the claim is taken on, and still not the instance id; `RunId` and `StartedAtUtc` are
> still fields. The `~` reasoning is kept as the record of why a separator has to survive Cosmos ids
> and URL paths, which is exactly the class of constraint ADR-PA20 hit next.

`ExecutionId.MintRun(engagementId, workflowId, runToken)` produces
`{engagementId}::{workflowId}#{runToken}`. `RunId` joins `GraphOrchestratorInput`,
`ExecutionSnapshot`, `ConsolidateAuditInput` — and `ExecutionSnapshot` also gains a real
`StartedAtUtc`.

**Why a suffix and not a new format.** The two-argument `Mint` stays exactly as it was, because it
is no longer the addressing key alone: it is the **affinity key**, the derivable value the consumer
takes a claim on to enforce one *live* run per engagement-workflow. `MintRun` extends it rather
than replacing it, and a test pins that relationship — if the run token were folded into the middle
of the id, or the two-argument form were retired, the claim would have nothing deterministic to key
on and the K9 guarantee would have no mechanism at all.

**Why `~`.** The run is not another level of the engagement hierarchy. Sharing `::` for both would
recreate precisely the ambiguity that makes a dispatcher child id unreadable.

*The mark was `#` for one commit, and `#` was wrong on two counts that no amount of taste would have
caught.* An execution id is carried **verbatim** into Cosmos document ids — the consumer's
`execution-snapshots` ids are `{executionId}:{sequence}` — and Cosmos forbids `/ \ ? #` in an item
id, so every snapshot write for a run-scoped execution would have failed. It is also carried in URL
path segments (`/api/gates/{executionId}/{nodeId}`), where `#` begins a fragment and truncates the
path. `~` is unreserved in RFC 3986, legal in a Cosmos id, and already this estate's sanitisation
target (the consumer's registry ids map `/` to `~`). Caught while writing the consumer's affinity
store, before anything minted a run-scoped id — the cheapest moment it could have been caught, and
a test now pins both properties.

Because nothing parses an execution id (ADR-PA15), the *choice* of mark is a readability question —
but its legality in the stores and routes that carry the id is not, and that distinction is what
the first choice missed.

**Why the token is supplied, not generated here.** `Platform.Abstractions` is the zero-dependency
kernel (ADR-PA1), so it cannot take a ULID package; and generation must happen in the composition
root before scheduling, never inside an orchestrator body where non-deterministic values are banned
outright. The kernel validates the token's shape and refuses one carrying either separator.

**Every field is additive and optional, and that is a rule now.** `RunId` and `StartedAtUtc` are
nullable: inputs and documents written before them replay and read as `null`, meaning "the single
run of a pre-change execution". This is the ADR-E15 compatibility floor honoured rather than
excepted — the consuming repo's S13.51/ADR-PA15 exception was a breach worth recording and the last
of its kind, and S10.7 makes the rule mechanical. `GraphExecutionState.StartedAtUtc` is `required`
by contrast, because it is internal orchestrator state and never a wire shape.

**Why `StartedAtUtc` is here rather than deferred.** The B3 surface was returning
`CheckpointedAtUtc` as the start time under a standing note. With one run per engagement-workflow
that is merely imprecise; with several it orders a runs list by *last activity*, so a long first run
appears to start after a quick second one. It is captured once from `context.CurrentUtcDateTime` at
the start of the walk, so it is deterministic under replay and identical on every checkpoint.

*What this does not do.* It does not mint run ids, take the affinity claim, or make anything
re-runnable — those are the consumer's (ADR-EX1). This is the vocabulary that makes them
expressible, shipped first so the consumer has something to build against.

---

## ADR-PA17 — what a workflow needs before it runs is asked, not re-derived

`IWorkflowEntryInspector.GetEntry(definition)` returns the entry node's id, its input contract type,
and **the dynamic context fields its `ContextRequest` declares**. It declines (`null`) on exactly the
conditions `ITestRunInputSchemaProvider` declines: no single resolvable `AgentTaskNode` entry.

**Why it is here rather than in the consumer.** Entry detection is control-graph knowledge —
`ControlGraphWalker.FindEntryNodeIds`, nodes with no incoming control edge — and that walker is
`internal` to this package because it exists for the validation rules. A consumer needing "which node
runs first, and what does it require" would otherwise re-derive the walk from `Edges` and
`EdgeKind.Control`. That is how one rule ends up written in six places, which is the defect ADR-PA11
was written about; publishing the question is cheaper than policing the copies.

**Why `RequiredDynamicFields` and not the input contract.** The consuming repo's start path was about
to be built on the entry *contract* schema, and that would have been wrong: a caller does not supply
the entry node's payload. Context assembly builds it, from baseline components plus the dynamic
fields the node requests, and the caller's "input" writes that **engagement context**. So the
question a trigger must answer before scheduling is *"which dynamic fields does the first node ask
for, and does this engagement already hold them?"* — the first half of which is this type. A workflow
declaring fields here can still start with **no input at all** where the engagement already holds
them, which is how the consumer's seeded PoC engagement runs and would have been broken by keying on
the contract instead.

*Additive only.* A new interface and a new record; nothing existing changes shape, so the ADR-E15
compatibility floor is honoured rather than excepted (the S10.7 rule).

*What it deliberately does not do.* It reports requirements; it does not resolve them. Whether an
engagement satisfies them is the consumer's question, because the context store is the consumer's
composition-root concern and this package must not reach for it (ADR-PA2, and the port rule).

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

## ADR-PA18 — a run reads the dynamic-context epoch it was pinned to, and its evidence names it

`DynamicTier` has always carried `EngagementId`, `DynamicEpoch` and `AssembledFromSnapshotRef`, and
`ContextAssemblerSimple` has always filled them with `"unknown"`, `0`, `"unknown"`. The store
(`CosmosEngagementContextStore`) is append-only and versioned by epoch; the read path resolved
`:current` and threw the epoch away before the caller saw it. So two runs of one engagement-workflow
with different context produced evidence that could not say which version either read — a linkage
gap, not a retention one. The consumer's doc 04 §4 step 3 specifies the read as
`DynamicTier(engagementId, snapshot.Epoch, snapshot.Ref, …)` from a snapshot "frozen-at-init or last
refresh"; this restores that. (frontier-workflow S13.60 / C-42.)

**Decision.** `IEngagementContextStore` gains an epoch-addressed read that returns the version with
its provenance (`EngagementContextSnapshot`: epoch, document id, content hash, content). The
orchestrator carries the pin inline as `GraphOrchestratorInput.DynamicContextEpoch` /
`DynamicContextHash` — resolved by the Host before scheduling, exactly as the definition is — seeds
`GraphExecutionState` from it, and every `AgentTaskActivityInput` reads `state`, never the input, so
an explicit refresh (ADR-CR1) can move the pin without the snapshot lying. The composer reads the
pinned epoch and **throws** if it does not resolve: a run pinned to bytes the store cannot produce is
an evidential failure, not a fallback case. `ExecutionSnapshot` and `AuditRecord` gain
`dynamic_context_epoch` / `dynamic_context_hash` beside the definition version and hash they have
always carried; the audit record copies from the snapshot, which holds the final pin. All additions
are optional and omit-null, so existing goldens and signed bytes are byte-identical.

**What is not decided here.** Nothing moves the pin yet — the refresh signal loop is the consumer's
S13.62. `Phase1EngagementContextStore` keeps no history and answers a non-current epoch with
`null` rather than the latest; Host never resolves it (ADR-PA14), so that honesty is free.

Breaking for implementors of `IEngagementContextStore` and `IContextAssembler` (both gain a member);
no consumer implements either outside test fakes. *ADR-PA17 is claimed by #26, in flight; this is
numbered past it deliberately.*

## ADR-PA19 — the canonical profile reads a polymorphic document in any key order, and still writes it in one

System.Text.Json's polymorphic reader requires the type discriminator to be the first property of
the object. That is a streaming-performance choice in the library, not a property of JSON, and it
made every query read of a `[JsonPolymorphic]` document in this platform depend on the store
returning keys in written order — a property no store documents. The Azure service happens to
preserve order; the Cosmos emulator's June 2026 build happened to; its September build's query path
does not: its query path returns every object's keys in Postgres `jsonb` order — shortest key
first, then bytewise — with `id` appended last. A discriminator therefore stays first only when it is
the shortest key in its object, so the same read survives for one document shape and throws for
another.
`CosmosDefinitionStore`'s `SELECT *` listings of `DefinitionVersionDocument` (polymorphic
`WorkflowNode`s inside) are exposed; the existing PhaseC listing test happened to pass against the
September build because that fixture's node keys came back with `node_type` still first. The
consumer's engagement-event store did not get that luck and failed. (frontier-workflow S13.64.)

**Decision.** `CanonicalProfile.Options` sets `AllowOutOfOrderMetadataProperties = true`. It is
**read-side only**: the profile still writes the discriminator first, so canonical bytes — and every
definition hash, cache key and audit signature computed over them — are byte-identical before and
after. The one profile stays one profile. The documented cost is buffering on very large objects;
ADR-E1 keeps payload tonnage off the graph, so documents here are small by construction.

**Consequences.** A test proves a discriminator-last graph deserialises to the same canonical bytes
and that the profile's own writes still lead with the discriminator. Pinning the emulator image is
the consumer's concern and is about CI determinism, not correctness — after this change the
platform reads correctly against any build.

### ADR-PA17 — amended 2026-09-10: the sandbox's sample input reaches the executor as context

`TestRunRequest.SampleInputs` was accepted by `TestRunService.StartAsync` and discarded before the
executor was called — the consumer's S13.50 shape one layer over, found as S13.61's second finding.
`ITestRunExecutor.StartAsync` now takes the input as what ADR-PA17 already says it is: the sandbox
engagement's dynamic context, canonical JSON, or `null` when nothing was supplied (an empty object
is "nothing"). The executor writes it before scheduling; what a run with no context gets is the
implementation's call — the consumer seeds a default brief when the entry node needs one.
Breaking for implementors of `ITestRunExecutor`; the consumer's adapter is the only one.


---

## ADR-PA20 — the instance id is the run token; the affinity key stays a key

`ExecutionId.ForRun(runToken)` returns the token: a DTF instance id is the run's opaque token and
nothing else. `ExecutionId.MintRun` and `RunSeparator` are removed. `ExecutionId.Mint(engagementId,
workflowId)` is unchanged and is now named for what it always was — the **affinity key**, a Cosmos
document id the one-live-run claim is taken on, never an instance id. `ExecutionId.MaxInstanceIdLength`
(100) states the scheduler's cap once.

**The defect.** Durable Task Scheduler caps orchestration instance ids at 1–100 printable-ASCII
characters, and `Microsoft.DurableTask.Client` (1.24) enforces it before the request leaves the
process: *"Instance IDs must be between 1 and 100 characters; actual length is 111."* ADR-PA16's
`{engagementId}::{workflowId}~{runToken}` spent 32 of those on a v7-GUID token and left the rest to
two ids that are both caller-shaped: a UI-authored workflow id is a 36-character GUID, an engagement
id is composite and config-templated (`{type}::{client}::{site}`), and a sandbox engagement is
`SANDBOX-` + 32 hex. A sandbox run of any UI-authored workflow could not start (111 characters); a
real run on any templated engagement id longer than 29 characters could not either. Found 2026-09-10
by the first test run of a UI-authored workflow; every live proof before it ran the seeded
`advisory-sow` (12 characters) on `ENGAGEMENT-12345` (16) and fitted at 63.

**Why removing the prefix is the fix and not a workaround.** ADR-PA15 established that an execution
id is written and never read: identity travels as fields (`EngagementId`, `WorkflowId`, `RunId`) on
every contract that needs it, and nothing parses the id. The composite prefix was therefore
metadata riding in an address — legible on a dashboard, load-bearing nowhere. An address that carries
nothing cannot overflow, and the token's length is the composition root's to fix (32 characters
today; the cap is now a named rule the kernel checks). Shortening the tokens instead would have moved
the cliff, not removed it: engagement id templates are administrator-configured, so any fixed budget
is one config change from being exceeded.

**What is still keyed on the composite.** The affinity key — because the claim needs a *derivable*
key for "this engagement-workflow", and that is exactly what `Mint` produces. It is a Cosmos id
(255-character limit) and never reaches DTS. The relationship ADR-PA16 pinned ("`MintRun` extends the
affinity key") is retired with `MintRun`; the relationship that matters is pinned instead: `ForRun`
returns a value that contains no affinity key.

**What read the id and now reads a field.** `ApprovalRequestFactory` set a sandbox gate's TTL by the
execution id's `SANDBOX-` prefix; it reads `GateOpenRequest.EngagementId`. `IMcpToolCatalog.ResolveAsync`
took an `executionId` whose only use was the same prefix test; it takes the `engagementId`. Both
pipelines already held it. Consumer-side readers (the approvals inbox filter, the tool catalogue's
sandbox fencing, the orchestration factory) change in the same way in the consumer's S13.68.

**Dispatcher children.** `DispatcherOrchestrator` never set a child instance id — DTF assigns one —
so the `{engagementId}::{workflowId}::{workItemId}` shape the docs describe was never minted. The
child is identified by `GraphOrchestratorInput.WorkItemId`, a field; the consumer's S13.40 (which
asked how a child id could be parsed unambiguously) is answered by there being nothing to parse.

**Breaking.** `MintRun` and `RunSeparator` removed (`abstractions!:`); `IMcpToolCatalog.ResolveAsync`'s
second parameter is now the engagement id (same shape, different meaning — a consumer passing an
execution id would silently disable sandbox fencing, so the rename is deliberate and the consumer's
own tests pin it). ADR-E15's compatibility floor: an in-flight execution keeps the instance id it was
scheduled under; ids of both shapes coexist in stores and neither is ever parsed.

*Evidence.* Microsoft Learn, "Durable orchestrations overview" (instance ids: 1–100 characters,
printable ASCII, not `/ \ # ?`, not starting with `@`), read 2026-09-10; the scheduling exception
above, reproduced against the local DTS emulator the same day; ADR-PA15/PA16 for the position this
completes.

### ADR-PA20 — amended 2026-09-10 (same day): two readers in the sandbox channel

`TestRunService` derived the engagement id from the test-run id twice — `ExtractEngagementId` for the
snapshot read that drives reconciliation, and `EnrichWithArtifactContentAsync` for the section store's
keys — and neither was found by a search for `ExecutionId` because both parsed the string by hand.
With an opaque token the "engagement" became the token, the snapshot lookup hit the wrong partition,
and every sandbox run stayed *running* forever with its gate never auto-approved. `TestRunDocument`
gains an optional `engagementId`, written at start (and for a blocked run), and both readers use it; a
document without one is returned as persisted. Additive; the parser is deleted. The lesson is the one
ADR-PA15 recorded and this ADR repeated: the readers were never where the format was.

---

## ADR-PA21 — a cost is an amount and a currency, and two currencies are never added

Every cost amount on the platform drops the currency from its name and gains an ISO 4217 `currency`
code beside it. `ModelEntry.InputCostPer1kGbp`/`OutputCostPer1kGbp`/`CacheReadCostPer1kGbp` become
`InputCostPer1k`/`OutputCostPer1k`/`CacheReadCostPer1k` plus `Currency` (wire `input_cost_per_1k`,
`output_cost_per_1k`, `cache_read_cost_per_1k`, `currency`); `BudgetSpec.MaxCostGbp` becomes `MaxCost` +
`Currency`; `InvocationCostEstimate.EstimatedCostGbp` becomes `EstimatedCost` + `Currency`;
`UsageRecord.CostGbp` and `BudgetSnapshot.CostGbp` become `Cost` + `Currency`; `BudgetLedgerDocument`
and `ExecutionLedgerSnapshot` store `total_cost` + `currency`; Observability's `TierEconomics`,
`TierEconomicsRow` and `NodeMetrics` take neutral names + `Currency`. The Phase 1 catalogues are priced
in USD: claude-opus-4-8 at 0.0050 / 0.0250 / 0.0005 and claude-fable-5 at 0.0100 / 0.0500 / 0.0010
per 1,000 input / output / cache-read tokens, and the guardrail ceilings keep their numbers in USD
(default 2.00, sandbox C-28 0.50). Scales are unchanged: prices 4, budgets 2.

**The defect.** A sandbox test run of a seven-call workflow reported £1.32. The arithmetic was right;
the prices were not. claude-opus-4-8 was seeded at £0.03 / £0.15 per 1,000 tokens, a placeholder
about six times Anthropic's list price, and the currency itself was a guess hard-wired into
every member name. The consumer's API had already noticed the mismatch the other way: it served
these GBP amounts as `avg_cost_usd` and `spent_usd`. Two wrong labels on one number is what a
currency baked into a name produces. Nothing checks a name.

**Why a currency per amount, not one platform currency.** The ledger adds amounts that came from
different role mappings, and each mapping names its own model and so its own provider's price list.
A single configured currency would be one more unchecked assumption at the point where amounts
combine. With a code on each amount the combination point can check it. It also sets up the future
correctly. Currency conversion is the consumer repo's evolution item **E25**, and when it lands it
*removes a refusal* at the combination points. It does not add fields to every stored document.

**Mixing is refused, never converted.** No conversion exists, so wherever two amounts meet in different
currencies the platform throws `ContractViolationException`. That is the two-loop model's permanent
failure: `FailureClassifier` maps it to `contract_violation` and the outer DTF retry handler never
retries it, because no retry can make GBP comparable with USD. The refusals sit in `CostCurrency`
(Guardrails) and are applied at:

- **ledger accumulation** — `BudgetLedger.RecordUsageAsync` refuses usage whose currency differs from the
  engagement's recorded usage, before storing it; `CosmosBudgetLedger.RecordUsageAsync` refuses usage whose
  currency differs from the ledger document's `currency`. One currency per engagement makes every scope
  aggregate (invocation, execution, engagement) single-currency by construction.
- **hierarchy aggregation** — `BudgetHierarchy.BudgetHasCapacity` checks, before adding, that the scope
  ceiling, the snapshot's accumulated cost and the estimate share a currency. A snapshot with nothing
  recorded has a `null` currency and combines with anything. A ceiling with no currency is refused.
- **admission** — `AdmissionController.Admit` refuses a per-invocation cost ceiling in a currency other than
  the estimate's. *A judgement call:* Admit does not yet enforce the cost ceiling (tokens only), so
  nothing is added there. The check is placed anyway, so a model entry priced in the wrong currency
  fails at its first admission and not on the day cost enforcement lands.

**The metric.** `context.cost.saved_gbp` (unit `GBP`) becomes `context.cost.saved` with the static unit
`{cost}` and a `currency` attribute beside `engagement_type`. An OpenTelemetry instrument has one unit
for life, so a currency in the unit or the name would need an instrument per currency. As an
attribute, one instrument covers all of them, and a dashboard sums only within a `currency` series.
That is the same rule the ledger enforces. `currency` is a bounded ISO 4217 code, so it respects
ADR-O1's no-unbounded-dimension rule.

**Breaking, and a bounded exception to lazy migration.** This is a public-surface break in Guardrails,
ModelRoleConfig and Observability, and a wire break in two stored documents: the `model-role-config`
chain entries and the `guardrail-ledger` documents. `currency` is required on both, so a document
written before this change fails deserialization instead of being read as currency-less. No migration
adapter is written, and the exception is named and bounded the way ADR-PA4 and ADR-PA15 bounded
theirs. No deployed environment exists. These documents live only in local emulators, and those are
re-seeded (the consumer's `cosmos-init.py` seed must match `Phase1RoleCatalogue` byte-for-value and
changes with it). Neither document carries a `schema_version`, and neither is registered with
`ContractMigrator`, so ADR-PA13's adapter ratchet does not reach them. No golden file contains these
wire names, so none changes. Any stored cost document written after the first deployment is outside
this exception and gets an adapter. *ADR-E15's floor:* no DTF activity input or output carries a renamed
member. `AgentTaskActivityPipeline` builds `InvocationCostEstimate` inside the activity from the resolved
`ModelEntry`, `AgentTaskActivityInput` carries identifiers only, and `ResolvedModelSummary` carries no cost,
so no recorded orchestration history changes shape. Release: minor bump, v0.23.0 (major version 0).

*Evidence.* Anthropic first-party list prices, from the Claude API pricing table cached 2026-06-24:
claude-opus-4-8 $5 / $25 and claude-fable-5 $10 / $50 per million input / output tokens. Anthropic's
prompt-caching reference, read 2026-09-11, gives cache reads at ~0.1× the base input price. The £1.32
figure is the consumer's sandbox test run of a seven-call workflow against the old catalogue. That
catalogue's own doc comment already called its opus figures "PoC placeholders pending verified provider
pricing". Decided by the owner 2026-09-11.

### ADR-PA21 — amended 2026-09-11 (same day): the test-run cost summary

The sandbox's cost summary was the one amount the ADR missed. `TestRunCostMetrics.EstimatedCost`
had no currency beside it, so the consumer's test-run screen could show a number but not say what
it was in. `TestRunCostMetrics` gains `Currency`, the ISO 4217 code of `EstimatedCost`, and
`TestRunService` stores it in the run document's cost-metrics dictionary under a `currency` key,
written only when non-null. The existing keys are unchanged. Unlike the ledger and catalogue
members above, it is **optional**. It is null when no invocation was priced. Stored test-run
documents predate it and are not migrated: they expire on their 7-day TTL, and until then they read
back as currency-less instead of failing. ADR-E15's floor holds: the member is additive, never
`required`, and rides no DTF activity input or output. Release: patch, v0.23.1.

### ADR-PA21 — amended 2026-09-11: the ledger keeps every execution's snapshot current, at scale 4

Two defects in `CosmosBudgetLedger` sat under this ADR's ledger members. First, only the create path
wrote `execution_snapshots`. The replace path advanced the engagement totals and left the map alone,
so an execution-scope snapshot returned the first invocation's usage for good, and every execution
after the document's first had no snapshot at all. Second, the execution-scope read used a LINQ
`ContainsKey` that the Cosmos SDK cannot translate, so it threw before reaching the map. Accumulation
now lives in the pure `BudgetLedgerAccumulation`, which advances the totals and the recorded
execution's snapshot together and refuses usage whose currency differs from the document's or from
the snapshot's. The read is a parameterised SQL `IS_DEFINED` query.

**The ledger's costs are scale 4, not scale 2.** "Budgets 2" above means `BudgetSpec.MaxCost`, a
ceiling, which is unchanged. `BudgetLedgerDocument.TotalCost` and `ExecutionLedgerSnapshot.TotalCost`
accumulate real usage, and one invocation costs a fraction of a cent (0.0012 USD, say). Scale 2 would
round that usage away. Both members were already written at the profile's default scale 4. They now
declare `[DecimalPrecision(4)]` explicitly, so the stored bytes are unchanged and no migration is
needed. Release: patch, v0.23.2.

## ADR-PA22 — verification resolves the key version that signed the record, not the current one

`AuditSigner.VerifyAsync` fetched one key from `IKeyProvider.GetCurrentKeyAsync` and handed it to
`AuditChainVerifier` for the whole chain, so every record was checked against whatever key is
current *now*. Each record has always stored the version it was signed under in `SigningKeyId`;
verification never read it. Audit chains are per **engagement** and span that engagement's entire
life, so the first rotation would have reported every engagement's pre-rotation records as broken
links — permanently, and with no way to tell that from tampering. Doc 05 §5 already specified the
correct behaviour ("verification resolves the key *version* from `SigningKeyId`, so old records
verify under old key versions forever; rotation requires no re-signing"), so this is a defect
against the spec, not a change of position. It is a K6 repair: the HMAC chain is only evidence if
it survives the key lifecycle.

**The surface.** `IKeyProvider` gains `GetKeyAsync(keyId, ct)` returning `null` for an
unresolvable id, with **no default interface implementation** — the only default available would
be to fall back to the current key, which is the defect itself, so every implementation must
answer by version or admit it cannot. A new internal `SigningKeyResolver` resolves a chain's
**distinct** key ids, one provider call per distinct id (a 500-record chain over two rotations
costs three calls, not 500). `AuditChainVerifier.Verify`/`FindBrokenLink`/`IsSignatureValid` take
an `IReadOnlyDictionary<string, SigningKey>` and look each record up by its own `SigningKeyId`.
`SignedAuditRecord` is **unchanged** — no bytes, hashes or signatures move, so every record
already stored stays verifiable and no golden file changes.

**Fail closed, but distinguishable.** An unresolvable key id makes `SignatureValid` false for the
target record *and* lists the id in the new optional `VerificationResult.UnresolvedKeyIds`
(`unresolved_key_ids`, property order 4). "The key version is gone" and "the signature is forged"
are different findings and an auditor must not have to guess which one they are looking at. The
list is **chain-wide**: any unresolvable id anywhere in the chain is reported even when the target
record itself verifies, because a caller auditing a chain needs to know a version has been
destroyed regardless of which record they asked about. It is optional and omit-null, so a clean
verification's bytes are unchanged.

**An unresolvable key does not stop the walk.** Hash-chain continuity needs no key, so
`FindBrokenLink` still walks past a record whose key is missing and still reports a genuine hash
discontinuity later in the chain. A missing key version is reported through `SignatureValid` and
`UnresolvedKeyIds`, never as a broken link.

**`VerifiedAgainstKeyId` is redefined — read this before upgrading.** It was "the key
verification was performed against", which in practice meant the current key. It now means the
**target record's own** key id: the version its signature was actually checked against. The shape
is unchanged (still `required string`, same wire name, same order), so nothing fails to compile or
deserialize, and that is exactly why it is called out here and in the release notes. Any consumer
reading it as "the key the system is currently signing with" is now wrong, and silently so. After
a rotation a pre-rotation record reports the older version here forever. This is the honest
meaning: reporting a key the record was not verified against was never useful.

*Evidence.* NIST SP 800-57 Part 1 Rev. 5 (May 2020) separates a key's **originator-usage period**
from its longer **recipient-usage period** — a key retired for producing protection must remain
available for *processing* already-protected data — which is the rule doc 05 §5 states as "old
versions are disabled for signing, retained for verify". HMAC is per RFC 2104. Sources read
2026-09-12.

Release: **minor, v0.24.0** (major version 0, so a breaking change may ship in a minor — README
"Versioning"). Breaking for any out-of-repo `IKeyProvider` implementation, which must now
implement `GetKeyAsync`; the in-repo `DevKeyProvider` resolves its own key id and returns `null`
for anything else. Tracked as S13.66.

## ADR-PA23 — a draft says when it predates the schema it is read with

`MigratingWorkflowDefinitionConverter.Read` adapted only stored versions with a registered adapter.
**Every other version deserialized straight through** — unrecognised keys were dropped with no log,
no exception and no finding. The nulled fields then surfaced as ordinary content errors
("rollback target … produces no section (no artifact_key)"), so a designer debugged a phantom
content problem on an innocent node instead of being told the draft predates the schema this build
reads. The read behaviour is deliberately unchanged: a definition that cannot be read faithfully is
never silently guessed at. What changes is that the condition is now *named*.

**The stored version must be probed and carried.** `ArtifactVocabularyMigration.Migrate` stamps the
current `schema_version` onto the node before deserializing, so a definition that has been read
always reports the current version — the stored one is gone by the time anything downstream could
classify it. `MigratingWorkflowDefinitionConverter.ProbeStoredSchemaVersion(json)` reads it from the
pre-migration bytes, and `DefinitionDraftDocument.StoredSchemaVersion` carries it on the storage
envelope. On the envelope, not in the definition: the definition's canonical bytes — and therefore
every published hash — stay byte-identical with and without the field (hard invariant 1, pinned by
`DefinitionDraftDocument_StoredSchemaVersion_DoesNotChangeTheDefinitionsCanonicalBytes`). Absent
reads as null, which means "not probed", never "current".

**Info when adapted, Error when unsupported** (decided by the owner, 2026-09-12). An adapted draft is
readable and correct — the designer only needs to know why it looks old — so an Error there would
block a publish that is fine. An unsupported stored version means fields were dropped, so the
definition on screen is not the definition that was saved; Error blocks publish, which is what an
unreadable draft deserves. Nothing at all is emitted when the version is current or was never
probed: the common path stays silent.

**A newer minor of the current major is Current, not a finding.** Same major means this build reads
it and the converter already deserializes it normally; minor versions are additive by convention, so
saying nothing matches what actually happens. A newer *major* is Unsupported — there is no backward
adapter and never will be, and treating "not older" as "fine" is how a downgrade silently corrupts a
draft. `DefinitionSchemaCompatibility.CurrentSchemaVersion` derives from
`ArtifactVocabularyMigration.RenamedSchemaVersion`, the same constant `WorkflowDefinition.SchemaVersion`
defaults to, so the classification cannot drift from the type at the next schema bump.

Rule `schema.version-supported` is pure-tier and **definition-scoped** (`NodeId`/`EdgeRef` null):
anchoring it to a node would send the designer to the wrong place, which is the failure this defect
is about. The doc 13 §4.2 catalogue row lives in the consumer repo and is tracked there;
`RuleCatalogueSpecCoverageTests` pins the catalogue as a checked-in fixture in this repo, so it
passes here on registration and does not depend on that amendment.

**Known gap at the time of this decision.** The probed version reaches the storage envelope and
`DefinitionValidationContext`, but no caller yet passes it into a validation run: wiring it through
`IDefinitionCompiler.ValidateStructural` broke seven pre-existing `TestRunServiceTests` as a mocking
artifact and sat outside the agreed surface, so it was reverted rather than absorbed here. The rule
and its plumbing are correct and tested; until that wiring lands, `schema.version-supported` cannot
fire in a real validation. Tracked as S13.34.

## ADR-PA24 — ADR-CR1's refresh lands at quiescence, merges, and reports a real epoch

S13.62 closed ADR-CR1's loop: the orchestrator now consumes a `DynamicContextRefreshRequired`
signal, decides what to do with it, and moves the run's dynamic-context pin. Four decisions were
open and are settled here.

**The event has one wire contract, in the kernel.** `DynamicContextRefreshRequired` lives in
`Frontier.Platform.Abstractions`, and both halves of the seam use it: the consumer's ingest surface
emits it, `GraphOrchestrator` consumes it. A consumer-side duplicate of the same event would be two
contracts for one wire — divergent bytes, divergent hashes, and nothing to fail when they drift
apart — which is what ADR-PA2 and K10 exist to prevent. It carries engagement id, reason, changed
fields, detected-at UTC, and an optional `components` list whose names are the **snake_case document
keys** (`engagement_profile`), not doc 18 §1's kebab-case tier-table spelling: the names have to
match the keys the merge writes under, and hard invariant 1 governs the wire.

**The name is `DynamicContextRefreshRequired`.** Doc 04 §8's code block raises
`DynamicContextRefreshNeeded` while constructing a `DynamicContextRefreshRequired` payload on the
very next line, so the block contradicts itself; doc 04's own prose above it, doc 16 §4's
refresh-class list and doc 18 §3 all say `Required`. Event names are contracts, so the three
consistent sources win and the doc's code block is wrong.

**Refresh at quiescence only** — doc 04 §8 gives the orchestrator three outcomes ("refresh now,
defer to next checkpoint, acknowledge") but never defines the predicate, so this is policy the docs
left open rather than behaviour invented against them. The orchestrator refreshes when no nodes are
in flight; a signal arriving mid-flight is held and applied at the next iteration that quiesces
(doc 04 §8's "queue for next checkpoint"). The reason is evidential, not aesthetic: the pin is
stamped onto every checkpoint and consolidated into the signed audit record (S13.60/S13.65), so
moving it while activities scheduled against the old epoch are still running would leave the
snapshot attesting to an epoch that half the completed steps never read. Two consequences are
deliberate and pinned by tests so nobody "fixes" them — nodes already in flight keep the epoch they
were scheduled with (that is what `AgentTaskActivityInput.DynamicContextEpoch` is *for*), and a
signal raised while a human gate is open is not observed until the gate returns, because a gate runs
inline in the walk rather than inside the walk's `Task.WhenAny`. DTF buffers it; nothing is lost;
doc 18 §3 states the same outcome from the other side.

The subscription is created **once, outside** the walk loop and kept **out of** `walk.Running`,
re-armed only after a signal is consumed. Both halves are load-bearing: re-creating it per iteration
writes a new subscription into history on every pass and consumes buffered events out of order, and
parking it in `walk.Running` would make the loop count it as outstanding work — so every ordinary
run, which is to say almost every run, would hang at the point the graph is otherwise finished. It
is left dangling when the walk ends and DTF discards it with the instance. It is not hand-rolled
from `Task.WhenAny` + `CreateTimer` + `CancellationTokenSource.Cancel`, for the reason already
recorded on `GraphOrchestratorSteps.TryWaitForArtifactUpdateAsync`: that races a `TimerFired`
history replay against an already-cancelled timer and throws.

**A scoped refresh merges; it does not replace.** `IEngagementContextStore` gains
`MergeDynamicContextAsync`, which writes only its named components and leaves every other key
byte-identical. This is a correctness fix, not tidiness. An ADR-CR1 refresh is *scoped* — doc 18 §3
raises it for named components and doc 04 §8's payload carries `changed_fields`, not a whole context
— but the only primitive available was `UpsertDynamicContextAsync`, which replaces the whole
document. Routing a scoped refresh through it deletes every key the refresh did not produce; the one
that matters is `engagement_brief`, because `ContextContentFilter.Filter` throws
`ContractViolationException` for a requested key the document no longer holds, and hard invariant 7
makes a contract violation **permanent** — never retried. A single scoped refresh would therefore
have permanently killed every entry node requesting the brief, on a live engagement, with no retry
to recover it. That is why this is a store primitive rather than a read-modify-write each caller is
trusted to get right. A named component is replaced **wholesale**, not merged into recursively: the
unit is the component, and doc 18 §2's stub→enriched transition legitimately *removes* fields that a
recursive merge would strand. Byte-identity still governs the epoch (ADR-EC1, doc 04 §8): a merge
that changes nothing writes nothing and reports the epoch already current, so a primed provider
cache stays primed.

**The epoch-0 no-op defect is fixed.** `DynamicContextRefresher` returned `Epoch: 0` on the
identical-bytes path. That constant is not "no epoch" — it is a valid epoch, the first one, since
the store's counter is 0-based. S13.60 made it reachable and harmful in one change by giving the run
a pin in `GraphExecutionState.DynamicContextEpoch`, and S13.62 is the first caller to assign a
refresh result to it: a run that refreshes at epoch 4, finds the bytes unchanged and stores the
returned `0` has silently dragged its pin back four epochs, after which every assembly reads stale
bytes while the snapshot and the signed audit record both attest to an epoch the run never read.
Wrong provenance is worse than none, because it reads as true. A no-op now reports the store's
*current* epoch, read through the existing `GetDynamicContextSnapshotAsync(id, null, ct)`; the no-op
semantics themselves are unchanged and must stay unchanged. The pre-existing refresher tests were
accidentally right — each seeds exactly one upsert, so the current epoch there genuinely *is* 0 —
which is precisely why the defect survived them.

**The content producer is the consumer's half.** The refresh activity takes rendered content through
`IDynamicContextContentProducer`, a port. The dynamic components themselves are consumer-side types
over the consumer's engagement entity and its CRM enrichment (doc 18 §1's "thin readers", ADR-EC1);
recreating them here would be an ADR-PA2 breach. The engine knows only that a refresh needs rendered
content and asks for it. The decision runs in the orchestrator body, the work runs in an activity —
hard invariant 2, enforced by `OrchestratorPurityTests`.

Release: **minor, v0.25.0 proposed** (major version 0, so a breaking change may ship in a minor —
README "Versioning"). Breaking for any out-of-repo `IEngagementContextStore` implementation, which
must now implement `MergeDynamicContextAsync`; `EngagementContextMerge.ApplyThroughAsync` is public
so that obligation is satisfiable in one line, over the interface's own members.

## ADR-PA25 — the dispatcher is a router, not a queue; and its generation boundary is where a version moves

S13.22 and S13.18 are recorded together because they are one change: the resolve-before-
`ContinueAsNew` edit and the body rewrite touch the same twelve lines of `DispatcherOrchestrator`.

**The defect.** `DispatcherOrchestrator` awaited `CallSubOrchestratorAsync` *inside* its loop,
before looping back to the `WorkItem` wait. One child parked at a human gate therefore blocked
every later work item — the exact inverse of doc 00 §4.4's "work items process in parallel: a child
paused at a human gate never blocks the queue" and doc 16 §4's children running "normal
run-to-completion semantics". A dispatcher-mode definition typically contains a gate, so the serial
form would have parked the queue on the first ticket and looked like nothing worse than a slow
system. It had never run: nothing raises `WorkItem` yet (that is the consumer half), which is why
five years of green tests said nothing about it.

**Spawning is parallel and unbounded, and `ContinueAsNew` counts spawns, not completions.** The
body races the `WorkItem` wait against its outstanding children and never throttles; the generation
boundary fires on the Nth *spawn*, with every child still in flight, and does not wait for them.
Both read as bugs and are the design, so both are commented at the call site and pinned by tests.
The evidence for the second is explicit: a sub-orchestration "runs as a child of the calling
(parent) orchestrator" as its own orchestration instance with its own history, which "can run
standalone for one-off device setup, or a parent orchestrator can schedule it as a
sub-orchestration" (Microsoft Learn, *Sub-orchestrations in Durable Task*, ms.date 2026-04-23,
updated 2026-08-03). What `continue-as-new` discards is stated just as precisely: "The results of
any incomplete tasks are discarded when an orchestration calls `continue-as-new`" (Microsoft Learn,
*Eternal Orchestrations in Durable Task*, ms.date 2026-04-23, updated 2026-08-05) — the *results*,
in the parent's own execution, not the child instances, which are separately scheduled and complete
on their own, writing their own snapshots and their own signed audit records. A rollover mid-flight
therefore orphans nothing. Restoring an `await` in the loop, adding a throttle, or putting a
`WhenAll` at the boundary would each turn the router back into the queue this replaced.

The same page fixes the other half of the boundary: in .NET "`continue-as-new` preserves
unprocessed events by default … unprocessed events are delivered when the orchestration next calls
`waitForExternalEvent`". That default is load-bearing here — a work item raised between the Nth
spawn and the new generation must cross, or an ingest surface accepted an event and dropped it —
and it is also what makes the refresh drain necessary, below.

**The `WorkItem` wait is subscribed once per generation and re-armed only after a delivery.** DTF
writes one history record per subscription, so a wait re-created inside the loop grows history on
every pass and consumes buffered events out of order. This is not a new hazard: it is the one
already documented on the ADR-CR1 refresh wait in `GraphOrchestratorSteps`, and the dispatcher
inherits it wholesale the moment the loop races that wait against outstanding children.

**`EnsureSupported` gains a work-item carve-out; rewriting the child's definition was rejected.** A
dispatcher hands its child its *own* pinned definition, whose mode is `dispatcher`, so every
spawned child would have died on the `Mode != OneShot` guard — a contract violation, permanent and
never retried (invariant 7). The carve-out is exactly as wide as a non-blank `WorkItemId`: a child
of a dispatcher carries one, a top-level execution never does. The alternative — rewriting the
child's definition to `OneShot` before spawning — mutates a pinned definition and changes its
`definition_hash`, a K3/ADR-2 breach. The hash is what the signed audit record pins to prove which
graph version produced the output, so a child whose definition was edited in flight would attest to
a version that was never published. The mode guard gives, not the definition. The dispatcher's own
mode guard moves from `InvalidOperationException` to `ContractViolationException` at the same time:
one vocabulary for one class of mistake, and the permanent-failure classification is read off the
exception type.

**The rollover port fails loud.** `IDispatcherVersionResolver` is declared here and deliberately
**not** DI-registered — the `IDynamicContextContentProducer` precedent: what a published version is,
and what an engagement's pin means, is the consumer's knowledge (doc 16 §8, ADR-E7), and a default
would let a misconfigured deployment roll a dispatcher onto the wrong definition instead of failing
to start. Resolution runs in an activity because it is a store read (invariant 2) and at the
generation boundary because a running generation stays pinned to the definition its history
recorded (invariant 6). `null` means "no successor — continue on the current version" (doc 16 §8's
third bullet; ADR-E15 D2's "the queue never stalls on a retired version"). **Failure must throw and
must never be reported as `null`**, and the XML doc says so: conflating "nothing newer" with
"lookup failed" would pin a dispatcher to a stale definition silently and forever, with no later
moment at which the mistake surfaces — the same silent-fallback shape as the epoch-0 no-op
(ADR-PA24) and the S13.66 key provider. The next generation's input is *rebuilt* around the
resolved definition while preserving `RunId`, `EngagementId` and `InitiatedBy`: a generation change
is the same run continuing, so a new run id would fork the audit chain (ADR-EX1) and a dropped
initiator would break the S13.19 attribution chain at an arbitrary 100-item boundary.

**`WorkItem.Payload` becomes an ADR-E2 `TypedPayload`.** It was `required object`, which is not a
contract: `object` deserializes as a `JsonElement`, so every consumer re-inspects an untyped blob
and ADR-E1 tonnage has nowhere to live. This is K4 erosion in the one place it matters most — the
only path carrying **external** input across the DTF history boundary, where the bytes are
evidential and replayed for the life of an eternal instance. The contract now implements
`IVersionedContract`, validates (cascading the envelope's own violations), and has a golden file.
Adding `schema_version` at property order 0 renumbers the rest, which canonical-serialization
normally forbids outright; it is safe **because of** the defect above — nothing has ever raised a
`WorkItem`, so no stored or replayed bytes exist. It is a break in theory only, and only until the
first dispatcher runs.

Two consequences were followed rather than worked around. `ContinueAsNewThreshold` became
`internal` so tests reference the boundary instead of hard-coding 100. And `CanonicalOutputSchema`
now refuses any contract that *carries* a `TypedPayload`, not just `TypedPayload` itself: making
`WorkItem` a versioned contract brought it into the schema sweep, where its free-form payload
exported as the boolean schema Anthropic rejects. Inventing a schema for it would contradict ADR-E2
deferral (c) — the honest schema is the capability-declared `schema_ref` — and a work item is not
an agent output contract in any case. The sweep now skips whatever the generator refuses, and a
new test pins the refused set to exactly `TypedPayload` and `WorkItem` so refusal stays a visible
decision rather than a quiet exemption.

**The dispatcher drains refresh signals it will never act on.** S13.62's fan-out raises
`DynamicContextRefreshRequired` at every live instance of an engagement, dispatchers included. A
dispatcher has no walk and no epoch to move, so it never acts on one — but with unprocessed events
preserved by default (cited above), every such signal would be carried into the next generation,
and the next, forever: unbounded history growth for the life of an eternal instance, entirely
silently. A drain-and-discard wait bounds it. Excluding dispatchers from the fan-out on the
consumer side is a separate, complementary fix; this half holds even if that one is forgotten.

Release: **minor, v0.26.0 proposed** (major version 0, so a breaking change may ship in a minor —
README "Versioning"). **Breaking on two counts, both on `WorkItem`:** `Payload` is retyped from
`object` to `TypedPayload`, and the new `schema_version` at property order 0 renumbers every other
property — a wire-compatibility break that canonical-serialization normally forbids outright. Both
are safe for exactly one reason, and it is the defect above rather than any property of the change:
nothing has ever raised a `WorkItem`, so no stored or replayed bytes exist to break. That reason
expires the moment the first dispatcher runs.

## ADR-PA26 — the projection records a run's mode and its work item, and a child writes its own sequence 0

ADR-PA25 made the dispatcher spawn a child per work item. `ExecutionSnapshot` carried neither the
mode nor the item, so every child run of an engagement looked identical in the projection and in
the engagement timeline: nobody could say which ticket a given run was serving.

**The mode is stored, not derived, and not reduced to a boolean.** `execution_mode` (property
order 21) is the definition's existing `ExecutionMode` smart enum on its canonical wire string;
`work_item_id` (order 22) is the child's item, null on every run that is not one. An
`is_dispatcher` flag was
rejected: the question a projection has to answer is *what kind of run is this*, and a derived
boolean is wrong the moment a third mode exists. The two fields are read together — a child is
handed its parent's pinned definition unaltered (ADR-PA25 — rewriting it would move the
`definition_hash`), so a child's mode is `dispatcher` too. The router is therefore `execution_mode
== dispatcher && work_item_id == null`, and a child is `dispatcher` with an item. Neither field
alone separates them, which is the concrete reason the pair is stored rather than a flag.

**The field is `execution_mode`, not `mode`.** It was `mode` when #44 merged — matching
`WorkflowDefinition.mode`, the contract it is copied from — and was renamed before v0.27.0 was
tagged, so nothing had ever shipped under that spelling and no stored bytes carry it. The
projection's readers are the boundary that matters here, and `execution_mode` is their established
vocabulary; the consumer binds the CLR name by reflection, which is why the property is
`ExecutionMode` rather than `Mode`. Internal consistency with the definition contract lost to
consistency where the value is actually read.

**The leak this closes.** S13.62's refresh fan-out filters on status alone, so it raises
`DynamicContextRefreshRequired` at dispatcher instances, which never wait on it; `ContinueAsNew`'s
`preserveUnprocessedEvents: true` then carries those events into every generation — silent,
unbounded history growth. The dispatcher's own drain (ADR-PA25) bounds it from inside; excluding
routers from the fan-out is the consumer's complementary half, and it was impossible because the
projection could not say which runs were routers. Now it can.

**A dispatcher child writes its own sequence-0 snapshot.** Sequence 0 is the pre-start projection
slot, filled by the Host's `OrchestrationFactory` for a normal run (S4.7a) — which is why
`GraphExecutionState.Sequence` starts at 1. A child bypasses that factory entirely, so nothing
wrote its slot and the child stayed invisible until its first node completed: the S13.70 shape, a
run that exists but cannot be seen, already fixed once for test runs. `GraphOrchestrator` now calls
`WriteChildStartSnapshotAsync` before the walk, which writes through the same
`SnapshotStateActivity` path at sequence 0 and does nothing at all for a top-level run. It is an
activity call, never body I/O (invariant 2), and the discriminator is the same non-blank
`WorkItemId` that `EnsureSupported`'s dispatcher-child carve-out already uses, so the two cannot
drift.

Both properties are **optional, omit-null and appended** — nothing is renumbered, no member is
`required`, and bytes recorded before them read back as null per the ADR-E15 floor. Every existing
snapshot golden is byte-identical, pinned by a test that re-serializes the unchanged sample against
the pre-change golden rather than by inspection.

Release: **minor, v0.27.0 proposed** — purely additive, but the consumer's fan-out exclusion needs
a version to bump to.

## ADR-PA27 — a role's chain may resolve to a remote agent; the invoker learns what it resolved to

frontier-workflow is wiring A2A agents hosted in Azure AI Foundry into workflows (its S13.80–S13.89).
PLATFORM-EVOLUTION-CANDIDATES E4 and E6 already settle *how an external agent is reached*: through
`role`, not a new node field — E4 says A2A slots behind `IAgentInvoker` "with no orchestrator or
definition changes", and E6 says runtime resolves only which instance of the named thing serves. So a
role's chain has to be able to hold an agent. It could not: every chain entry was a `ModelEntry` whose
token prices, context window and max output tokens are required, and `AgentInvocationRequest` carried
only `ModelId`, so the invoker could not tell an agent from a model, or `azure-openai` from `anthropic`.

**Two entry types, one chain, never mixed.** `ChainEntry` is an abstract base holding `Provider` and
`Currency`; `ModelEntry` keeps every member it had, and `AgentEntry` names a registry resource
(`ResourceName`, `ResourceVersion`) with a fixed `CostPerInvocation`. A nullable-fields `ModelEntry`
was rejected (the consumer's choice, 2026-09-13): it would type an agent as a model with holes, and
every reader would have to guess which holes are allowed. A chain is **all-model or all-agent** — a
fallback from a model to a remote agent, or back, would change the kind of thing that produced a
section mid-outage, which ADR-M2's "the audit pins the exact model used" does not tolerate.

**An agent's cost is per invocation, and positive.** A remote agent reports no tokens the platform can
price, so the estimate is `CostPerInvocation`, and the entry grants no output-token budget and
contributes no context window. Zero is refused: `EstimateCost` would admit it under every ceiling, making
budget enforcement silently blind for exactly the calls that cost real money elsewhere.

**The guard splits by who can see what.** The platform checks shape — a mixed chain, or an agent entry
without its provider, resource or a positive cost, is a permanent `ContractViolationException` — both
when a mapping document is read and when a mapping is resolved. That covers a mapping edited outside
any publish path. Whether the named resource is *active* is the consumer's check, because only the
consumer can see its registry: it refuses a publish against an inactive resource and fails the call
permanently if the resource has been retired since. The plan had put both rules in mapping approval;
`MappingGovernanceService`'s propose and approve are still `NotSupported` stubs, so there was nowhere
to put them, and a platform port onto the consumer's registry was rejected as a dependency the resolver
would take on every call.

**The stored document is additive; an absent target is the migration.** One wire record,
`ChainEntryDocument` (was `ModelEntryDocument`), carries both kinds. `target` (property order 9) is
written only for an agent; the model fields became nullable on the wire but a model entry writes
them all and never writes `target`, so its bytes are unchanged — pinned by a test that serializes a
Phase-1 entry against its pre-change bytes. Reading treats an absent `target` as a model, which is the
adapter for every mapping stored before this ADR. An unknown target, or a missing field for the target
named, is a permanent violation naming the field.

**The invoker receives the resolved entry.** `AgentInvocationRequest.Target` (required) is the
`ChainEntry` itself, so a consumer invoker routes on provider, reads an `azure-openai` model's new
optional `Endpoint`/`Deployment`, or finds the agent's registry resource. `ModelId` stays and carries the
target's identifier (`ChainEntry.TargetId`: the model id, or the agent's resource name), which is also
what the circuit breaker keys on.

**Attribution rides the existing summary.** `ResolvedModelSummary` gains `resource_name`,
`resource_version` and `card_hash` at orders 6–8, optional and omit-null, so every existing audit and
telemetry golden is byte-identical. This is E6's Article 12 requirement — which resource produced this
output — on the `AgentInvocation`/`StepCompletion` path that already carries ADR-E8's directing-human
chain. The card hash is the **pinned** snapshot's, never a live fetch, and only the invoker knows it, so
it returns on `AgentInvocationOutcome.CardHash` and is ignored for a model target.

**`azure-openai` gets the OpenAI caching strategy.** Without a registration it fell to
`NoCachingStrategy`: calls worked, and OpenAI-family models cache matching prefixes server-side
regardless, but the prompt layout and cache-hit metrics were lost. It now resolves
`OpenAiCachingStrategy`, beside the existing `openai` registration (the consumer's call, 2026-09-13).

*Evidence (accessed 2026-09-13):* frontier-workflow's S13.81 spike against a live Foundry agent — the
agent is addressed by a registry resource with a pinned card, answers with an `AgentTask` rather than a
token-metered response, and so has no per-token price to estimate from; Microsoft Learn, Azure AI
Foundry incoming A2A — agents are called over A2A with Entra auth, separately from model deployments;
PLATFORM-EVOLUTION-CANDIDATES E4 and E6 (role-based reach, and the Article 12 attribution requirement);
the code audit for this change — the single `AgentInvocationRequest` construction site
(`AgentTaskActivityPipeline`) set only `ModelId`, and `CachingStrategyRegistry` registered no
`azure-openai` provider.

**Addendum (v0.30.0, frontier-workflow S13.88) — the remote task id rides too.** The consumer's A2A invoker
found on execution (its S13.85) that the outcome had no slot for the agent's own task id, so an audit
record could name the resource and the pinned card but not *which run on the agent's side* produced
the section — the join key an operator needs when reading both sides. `AgentInvocationOutcome.RemoteTaskId`
and `AgentInvocationResult.RemoteTaskId` (optional, bridged by the dispatcher like the card hash) and
`ResolvedModelSummary.remote_task_id` (order 9, omit-null, ignored for a model target) close it.
Additive; every existing golden is byte-identical. Release: **minor, v0.30.0.**

Release: **minor, v0.29.0 proposed — source-breaking.** `RoleMapping.Chain` and `ResolvedModel.Entry`
change type to `ChainEntry`, `ModelEntry`'s `Provider` and `Currency` move to the base, and
`AgentInvocationRequest.Target` is required. No stored bytes change and nothing is renamed on the wire.
The only consumer is frontier-workflow, which moves in lockstep: its model-role display and test-run
cost reader branch on the entry kind, and its invoker sets nothing new on the outcome until an agent
path exists.

## ADR-PA28 — a remote agent's tool use is recorded as self-reported, never as observed

frontier-workflow's S13.89 closes the governance gap decision 2A left open: a remote A2A agent's
tools run on its side, so the platform never sees them, and the audit could only mark them *not
visible*. The fleet convention that closes it asks each agent to say which tools it used — and the
audit has to carry that answer without letting a claim pass for evidence.

**The convention can only live in the reply payload.** Microsoft Learn, "Enable incoming A2A on a
Foundry agent" (accessed 2026-09-14): incoming A2A is text-only, the reply is the artifact text the
agent writes, and Foundry's A2A guidance describes an opacity contract — the caller never sees the
server agent's prompt, model or tools. Nothing in the A2A response surfaces tool calls and a prompt
agent controls only its text, so a `tools_used` list can ride nowhere but inside the reply the agent
composes. That is exactly why the audit must say where the fact came from: a list the agent wrote is
the agent's word, and K6 says a claim is never rendered as observed evidence.

**`ToolCall` says how the platform knows.** `ToolCallProvenance` is a smart enum, `observed` for a
call the platform made itself (the MCP path, S9.25) and `self_reported` for one lifted from a reply.
`ToolCall.provenance` (order 3) and `note` (order 4) are optional and omit-null. The platform's own
writers never set either, so every existing audit and telemetry golden is byte-identical, and a
reader treats an **absent provenance as `observed`** — the adapter for every record written before
this field. `note` carries the agent's stated purpose for a self-reported call, capped at **200
characters** (ADR-E1 tonnage — an auditor needs the *why*, not the arguments); `AuditRecord.Validate`
and `SignedAuditRecord.Validate` refuse a longer one as a contract violation. Lifting `tools_used`
out of the reply before contract binding, dropping malformed claims, and labelling self-reported
calls in the UI are the consumer's (S13.89b–c).

*Evidence (accessed 2026-09-14):* Microsoft Learn, "Enable incoming A2A on a Foundry agent" — text-only
incoming A2A, the reply is the agent's artifact text, and the opacity contract over prompt, model and
tools; frontier-workflow's S13.81/S13.88 spikes against a live Foundry agent; the platform-keeps
register, K6.

Release: **minor, v0.31.0 — additive.** Two optional properties and one smart enum; no stored bytes
change, nothing is renamed on the wire, and every golden is unchanged.

## ADR-PA29 — an execution pins the model-role mapping version it was served, at its own start

frontier-workflow's S13.102 found that doc 08 §5's pin was specified but never built:
`AgentTaskActivityPipeline.BuildResolutionRequest` set `MappingVersion = null`, so every agent
invocation resolved the *current* pointer. A rollback or an approval therefore changed a running
execution between nodes. That contradicts doc 08 §2 principle 6, ADR-M1's execution-pinned
versions, and ADR-M3's promise that a rollback reaches new executions while in-flight executions
stay pinned.

**Placement and the compatibility gate.** Doc 08 §5 puts the pin in `PinMappingsActivity`, called
by the orchestrator at execution start. Doc 12 §5 sketched it factory-side; this ADR follows doc
08, and the consumer amends doc 12 §5. The activity is `GraphOrchestrator`'s **first action**, and
it is scheduled only when the new optional input member `GraphOrchestratorInput.pin_model_roles`
(order 7) is true. The gate is required, not cautious. DurableTask.Core checks activity names
against recorded history on replay, so an unconditional pin at sequence 0 would fail every
in-flight run. An input recorded without the flag replays exactly as before: no pin, and
resolution against the current mapping. The pin uses the `snapshot-persistence` retry profile.

**The pin records the version actually served.** `ModelRolePin { role_id, mapping_version, ring }`
is decided once, by `IMappingPinner`. That covers canary assignment (a hash of the engagement id
against `CanaryPercent`) and the shadow or unassigned-canary fallback to `PredecessorFleetVersion`.
`ModelResolver` shares the ring rules through one internal `ServedMappingSelector`, so the two
cannot drift. A `ResolutionRequest` carrying the optional `Pin` reads exactly that version and
walks its fallback chain. It **never re-evaluates rings**, because re-evaluating would re-ask a
question the pin already answered. Doc 08 §5's continuity exception still holds: a fallback within
the pinned version may serve, recorded as `ChainPosition > 0`, under the same `mapping_version`.
The per-invocation `mapping_version` in the signed audit therefore equals the pinned version, and
the record's shape is unchanged. `IModelResolver` and `IRoleRegistry` gain no members, because
consumers' test doubles implement them. `IMappingPinner` is new, and `AddFrontierModelRoleConfig`
registers it.

**Roles and results.** The roles are collected purely from the inline definition (hard invariant
2): every `AgentTaskNode`'s role, deduplicated and ordinally sorted. Nodes are a flat list, and
parallel and loop nodes reference other nodes by id and contain none, so nothing is nested. The
activity returns a list ordered by role id, not a dictionary, so its recorded bytes are canonical.
The orchestrator keeps the pins in execution state, and each `AgentTaskActivityInput` carries its
node's entry in the optional `pinned_mapping` (order 14). A null entry means the old behaviour.

**An unmapped role fails the start.** The pinner turns a missing mapping into a
`ContractViolationException` naming the role. That is a permanent failure, never retried
(invariant 7), and it happens before any agent runs. Failing a run at its first node is better
than failing it half-written.

**Dispatcher.** The router pins nothing. `BuildChildInput` and `BuildNextGenerationInput` carry
`pin_model_roles` forward, and each child pins at its own start. A long-lived dispatcher therefore
gives every ticket the mapping current when that ticket began, which matches its per-child
definition and epoch pins. Sandbox runs pin the same way.

**Shadow-ready.** S13.101 sequences shadow execution after the cloud proof. A shadow candidate
will be pinned alongside the served version as optional `candidate_version` and `candidate_ring`
members on `ModelRolePin`, which is additive and needs no rework.

**Residual skew — accepted and recorded under ADR-E15.** A Host on the new package could schedule
a flagged run onto a worker still running an old package. That worker ignores the unknown member,
so the run executes unpinned and resolves current, as every run did before this ADR. Nothing
fails and nothing misreplays, because the old worker's history never holds a pin action. The run
simply lacks the guarantee. Deploying workers before or with the Host closes the window.

*Evidence:* frontier-workflow doc 08 §2 (principle 6), §5 (resolution flow and
`PinMappingsActivity`), §7–§8 (rollback reaches new executions only), §11 ADR-M1; ADR-M3;
DurableTask.Core 3.8.0's replay check on scheduled activity names (the S13.102 design check,
2026-09-15). A live emulator replay has not yet verified this; the consumer's emulator test does
that.

Release: **minor, v0.32.0 — additive.** New optional members `GraphOrchestratorInput.pin_model_roles`,
`AgentTaskActivityInput.pinned_mapping` and `ResolutionRequest.Pin`; new types `ModelRolePin`,
`IMappingPinner`, `PinMappingsRequest` and `PinMappingsActivity`; and the constant
`WorkflowActivityNames.PinMappingsActivity`. Null members are omitted, so every recorded input
keeps its bytes.

## ADR-PA30 — governance changes get their own signed chain, and the ADR-E2 envelope moves down to carry it

frontier-workflow's S13.103 found that doc 05's signed record covers executions only. An approver
role created, edited or retired (S13.100), or a model-role mapping approved or rolled back
(S13.101), left no evidential trail; the consumer's `IGovernanceAuditWriter` seam was a no-op. That
is a K6 gap against ADR-E8's attribution rule and EU AI Act Art. 12's record-keeping obligation.
Doc 15 §3 already promises that "each change passes through the consumer's governance audit seam;
the signed governance audit record lands with S13.103". This ADR builds the platform half. Mark's
decisions, 2026-09-15.

**The record.** `SignedGovernanceAuditRecord` (schema 1.0, every property ordered, snake_case,
omit-null): `schema_version`, `record_id`, `scope`, `sequence`, `event_type`, `subject_type`,
`subject_id`, `subject_version?`, `actor`, `actor_upn?`, `reason`, `occurred_at_utc`,
`correlation_id?`, `engagement_id?`, `change` (an ADR-E2 `TypedPayload`), `before_hash?`,
`after_hash?`, `compensates_record_id?`, then `previous_record_hash`, `record_hash`, `signature`,
`signing_key_id`. Callers submit a `GovernanceAuditEntry`, which carries the same caller fields. `subject_version` is a string, so a
role version, a mapping version and a content hash all fit without the platform choosing.

**Vocabulary neutrality.** `event_type` and `subject_type` are validated snake_case strings, not
platform enums. The consumer owns the catalogue (ADR-E2, ADR-E3a). Validation is structural only:
snake_case types and scope, non-empty `subject_id` and `reason`, and ADR-E8's actor rule. The actor
is required, is never `unknown` in any case or padding, and a `system:` actor must name its origin.
Every violation is a `ContractViolationException`, which is permanent and never retried.

**Hashing.** `record_id = SHA-256("governance-entry:" ‖ JCS(canonical entry))`, so an identical
retry derives the same id. `record_hash = SHA-256(JCS(canonical record with record_hash and
signature empty))`, and `signature = HMAC-SHA256(record_hash, key)` (RFC 2104). The canonical
profile fixes the typed shell. RFC 8785 JCS over the whole document fixes the untyped `change`
content that rides inside it, as ADR-E2 decision 2 requires. Unlike the execution chain,
`signing_key_id` is inside the hash, so swapping it to another valid version is a signature
mismatch rather than a silent re-attribution. JCS numbers are IEEE-754 doubles, so a payload number
beyond double precision is hashed at double precision. Callers who need exact values carry them as
strings, as the canonical profile already does for decimals.

**Chain scope and head allocation.** There is one chain per scope, and phase 1 has one scope,
`deployment`. The chain is ordered by `sequence`, never by timestamp. Genesis is
`SHA-256("governance:" + scope)`; the execution genesis hashes the bare engagement id. The two can
only coincide if an engagement id is literally `governance:<scope>`, which composite engagement ids
(`{a}::{b}::{c}`, doc 16 ADR-E2) do not produce. The head document `chain-head:{scope}` holds
`{sequence, record_hash}`. An append reads the head and its ETag, allocates `sequence + 1`, signs,
and then runs **one transactional batch** on the scope's partition. The batch creates
`{scope}:{sequence:D12}`, creates a `record-id:{recordId}` marker, and creates the head (genesis) or
replaces it with If-Match. The zero-padded id makes id order equal sequence order, and verification
reads in that order. The marker makes idempotency hold under concurrency. Two identical appends
that both miss the existence check cannot both land, because the second marker create conflicts;
its retry then finds the stored record and returns it.

**The retry is optimistic-concurrency re-hashing, not transient-fault handling.** A 412 (the head
moved) or a 409 (sequence, head or marker already exists) means another writer won; the append
re-reads, re-hashes and re-signs. The policy is data. `GovernanceAuditOptions` (section
`GovernanceAudit`) holds `AppendMaxAttempts` 8, `AppendBaseDelayMs` 25 and `AppendMaxDelayMs` 1000,
validated at start, with exponential backoff capped and jittered into its upper half. It is owned
by Audit, not taken from the Resilience catalogue, because ADR-PA5 keeps governance libraries
referencing only Abstractions and Serialization among platform packages. Throttling and
unavailability are left to the Cosmos SDK's own retries (doc 10 §4's `storage` profile posture). A
batch failing with any other status throws `GovernanceAuditAppendException`, as does exhausting the
attempts.

**Storage and partition key.** The container is `governance-audit-records`, partition key
`/scope`, `defaultTtl: -1`; it is added to `CosmosTopologyCheck`. Invariant 4 does not apply,
because this is not an engagement-hot container. Most governance changes (a deployment's role
catalogue, its model mappings) belong to no engagement, and a transactional batch and an
If-Match-guarded head require every write of one chain to share one logical partition. A partition
per scope is the only key that gives a single serialisable chain. At phase-1 governance write
rates, one partition is far inside Cosmos's per-partition limits. Signed records are archived to
Blob `governance-audit-records-archive` by the existing change-feed pattern, under their own
processor and lease prefix `archival-governance-audit-records`. Heads and markers are mutable
bookkeeping and are not archived.

**Key purpose.** A new `SigningKeyPurpose` (`execution_audit`, `governance_audit`) and
`ISigningKeyRing.GetProvider(purpose)` are added. The registered `SharedSigningKeyRing` returns the
one `IKeyProvider` for every purpose, so governance records reuse the versioned audit key and
record `signing_key_id`. A separate governance key later is a new ring registration; no existing
signature changes. The execution signer still takes `IKeyProvider` directly.

**Fail closed, and compensation.** `AppendAsync` returns the stored, signed record or throws. The
consumer turns `GovernanceAuditAppendException` into a 503 and does not perform the mutation. When
the mutation fails *after* its audit landed, the consumer appends a compensating entry whose
`compensates_record_id` names the earlier record. I chose a typed field over an `event_type`
naming convention. The contract validates it (it must be non-empty, and the service refuses it with
a `ContractViolationException` unless the named record exists in the scope), it is covered by the
hash, and it keeps the event vocabulary wholly consumer-owned. Nothing stored is ever altered.

**Verification.** The pure `GovernanceAuditChainVerifier.Verify(scope, records, head, keys)` backs
`IGovernanceAuditService.VerifyAsync` and works equally over an archive copy. It never throws for a
broken chain. It lists every break with its 1-based stored position, record id, recorded sequence
and kind: `signature_mismatch` (content or hash altered), `sequence_gap` (a record missing before
it), `out_of_order` (reordered or duplicated), `hash_link_break`, `unresolved_key`,
`scope_mismatch`, or `head_mismatch` (a forged or stale head, or records without a head). As in
ADR-PA22, each record's own key version is resolved, one provider call per distinct id, and an
unresolvable version fails closed. Here that means `valid: false`, with the id also listed in
`unresolved_key_ids`.

**The envelope moves to Serialization.** `TypedPayload` and `PayloadRef` lived in
`Frontier.Platform.Workflow.Model`, which is engine tier, so Audit could not carry them without
breaking ADR-PA5. Both are now compiled into `Frontier.Platform.Serialization`, which both tiers
may reference. They keep the namespace `Frontier.Platform.Workflow.Model` and are
`[TypeForwardedTo]` from the Model assembly. A type forward requires the full type name to be
unchanged, and keeping it means:
*binary* compatibility, because an assembly compiled against Model still resolves the types through
the forwards; *source* compatibility, because `using Frontier.Platform.Workflow.Model;` still finds
them and Model now references Serialization, so every Model consumer receives the assembly
transitively; and *wire* compatibility, because no bytes, goldens or schema versions change. The
PublicAPI entries moved from Model's Unshipped file to Serialization's. Model lists the forwarded
members with the analyzer's `(forwarded, contained in Frontier.Platform.Serialization)` suffix.
Model's single-dependency comment was updated. No architecture test changed: Audit references only
Abstractions and Serialization. **ADR-E2 deferral (b) is resolved:** `JsonCanonicalizer` (RFC 8785)
lives in Serialization, and governance audit signing is its first in-repo consumer. It writes JSON
text directly and constructs no `JsonSerializerOptions`, so K10's one-profile rule holds. It is
tested against the RFC's §3.2.2 and §3.2.3 samples and Appendix B number vectors.

**Out of scope.** (1) The execution chain is untouched. `AuditSigner`'s read-then-create can fork
under concurrent closes on one engagement; that is S13.106, which will adopt this ADR's head-document
pattern and decide the treatment of already-stored chains. (2) A Key Vault `IKeyProvider` is S13.107:
`AddFrontierAudit` still registers only `DevKeyProvider`, so until then governance records, like
execution records, are signed with the development key. (3) Multiple scopes and a separate
governance key are enabled but not configured.

*Evidence (accessed 2026-09-15):* RFC 8785, *JSON Canonicalization Scheme* (rfc-editor.org,
Informational, 2020): property names sorted as UTF-16 code units, numbers per ECMA-262 §7.1.12.1,
Appendix B vectors. RFC 2104, *HMAC* (1997), as already cited by ADR-PA22. NIST SP 800-57 Part 1
Rev. 5, *Recommendation for Key Management: Part 1 – General* (May 2020): originator-usage versus
recipient-usage periods, the basis for verifying old records under retired versions (as ADR-PA22).
EU AI Act Article 12(1)–(2) (artificialintelligenceact.eu): automatic recording of events over the
system's lifetime, including events relevant to substantial modification. Microsoft Learn,
*Transactional batch operations in Azure Cosmos DB* (updated 2026-04-27): operations sharing a
partition key succeed or fail together; a failed operation carries its own status (409 for an
existing item) and the rest 424; limits of 100 operations, 2 MB and 5 s. Microsoft Learn,
*Database transactions and optimistic concurrency control* (updated 2026-04-27): `_etag` with
`if-match`, rejected with HTTP 412 when stale.

Release: **minor, additive**. New public types in Audit (`GovernanceAuditEntry`,
`SignedGovernanceAuditRecord`, `IGovernanceAuditService`, `GovernanceAuditQuery`,
`GovernanceAuditPage`, `GovernanceAuditChainVerifier`, `GovernanceAuditVerificationResult`,
`GovernanceAuditChainBreak`, `GovernanceAuditBreakKind`, `GovernanceAuditChainHead`,
`GovernanceAuditScopes`, `GovernanceAuditOptions`, `GovernanceAuditAppendException`,
`SigningKeyPurpose`, `ISigningKeyRing`) and in Serialization (`JsonCanonicalizer`, plus the moved
envelope). `AddFrontierAudit` gains registrations, a hosted service and a topology row. The
topology row means **a consumer must create `governance-audit-records` before upgrading**, or the
boot check fails. Tracked as S13.103.
