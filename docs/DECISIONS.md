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
