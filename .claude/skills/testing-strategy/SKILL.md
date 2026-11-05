---
name: testing-strategy
description: Test pyramid, coverage rules, what to test at which layer, mock boundaries, emulator usage. Use when writing tests, deciding test scope, or structuring a test project.
---

# Testing strategy

The bar: **CI enforces ≥95% line and branch coverage per assembly; 100% is expected on new
code at review.** The no-private-methods and ≤15-line-method rules (engineering-standards)
exist partly to make that achievable — everything is small and independently reachable.

Today every assembly here sits at 100% line coverage. The gate is the floor, not the target.

## The pyramid

| Layer | Share | What it tests | Runs |
|---|---|---|---|
| **Unit** | ~70% | Single class, fakes for all dependencies, no I/O. Parsing, state transitions, validation rules, policy translation, smart-enum behaviour. | Every build, every PR. Coverage gate applies. |
| **Integration** | ~25% | A library plus its real adapters against the **Cosmos emulator** — never SDK mocks. Store round-trips, ETag guards, audit chain writes, contract↔storage shape. | Every PR, in a dedicated job; coverage reported, not gated. |
| **Architecture** | ~5% | The boundary rules themselves: no `Frontier.Reason.*` reference, Abstractions stays dependency-free, no cross-platform-library references. | Every build, alongside the unit suite. |

There is no E2E layer here. A library repo cannot meaningfully run one; that belongs to the
consuming solution.

## Coverage rules

- Tool: **Coverlet XPlat**, collected **per test project** via `./tools/run-unit-tests.sh`.
  Never collect solution-wide — parallel runs corrupt coverlet's per-assembly
  AssemblyLoadContext attribution and silently deflate the gate.
- Legitimate exclusions via `[ExcludeFromCodeCoverage(Justification = "…")]`, justification
  required:
  - record/POCO auto-properties and assignment-only constructors
  - abstract base bodies and interface members
  - `ToString()`/`GetHashCode()` overrides on records
  - SDK adapter glue with no logic: SDK calls, draining a feed iterator page by page, mapping a
    document to its domain type (`null` passing through), translating a status code (404 → `null`,
    409/412 → `false`) where the SDK reports it as an exception, and applying an option the caller
    already decided (adding the pointer upsert to a batch when `makeCurrent` is set — the decision
    is tested where it is made). Nothing else.
  - Adding a category needs a code-review conversation, not a silent attribute.
- **"Integration-tested" is not a justification for decision logic (S13.193).** A retry loop, a
  lost-update/ETag decision, a `switch` over a scope or state, query selection, skipping rows a
  point read no longer finds, epoch or version numbering, a batch-outcome interpretation — none of
  it may sit under an excluded adapter. Split the adapter: an `internal` port in the library
  (`IApprovalDocuments`, `IBudgetLedgerDocuments`, `IEngagementContextDocuments`) implemented by the
  excluded Cosmos glue class, and the decisions in a non-excluded class unit-tested over a
  hand-written fake of that port. Where a split is not yet done, pull the decision into an
  `internal static` rules class (`DefinitionWriteRules`, `ModelRoleConfigWrites`,
  `ModelRoleConfigQueries`, `ModelRoleConfigReads`) and test that. Unit tests fake the port, never
  Cosmos SDK behaviour (a `Mock<Container>` handed to a DI registration test as a placeholder
  is not a behaviour fake). Nothing enforces this rule but review.
- **The integration job reports coverage; it does not gate on it.** CI's `integration` job collects
  Coverlet per test project, as the unit job does, and writes a per-assembly table to the job summary
  beside the unit gate's table. Coverlet always honours `[ExcludeFromCodeCoverage]` (even when
  `ExcludeByAttribute` omits it), so that table cannot see excluded glue either — which is exactly
  why decision logic must not be excluded: the unit gate is the only place it is measured.
- **Every implementation of an interface gets its own tests** — a shared interface is not
  shared behaviour.
- Coverage is never gamed: a test that executes code without asserting behaviour fails review.

## Mock boundaries

- **Unit tests:** fake at the library's own interfaces. Hand-written fakes or a mocking library,
  your choice, but assert behaviour rather than call counts where possible.
- **Integration tests:** real emulator. **Never mock the Cosmos SDK** — the storage shape is
  part of what is under test.
- **Model calls:** deterministic fakes. No test in this repo may call a live model.

## Integration test conventions

Every Cosmos-backed fixture **provisions and tears down its own database** — create in
`InitializeAsync`, delete in `DisposeAsync`, endpoint from `EmulatorCosmos.Endpoint`. This
keeps CI to a bare emulator with no provisioning step, and it is the reason the suite is
portable. Do not write a fixture that assumes externally-created containers.

Mark them `[Trait("Category", "Integration")]`. **The trait is what the filters use** — putting
a test in an `*.Integration` namespace does nothing, and a unit test that lives in such a
namespace without the trait will be missed by a namespace-based filter, which is exactly the
bug that hid 19 tests from the publish job.

## Test conventions

- AAA structure; one behaviour per test; no shared mutable state (use builders/factories).
- Names: `Method_Scenario_ExpectedOutcome` (e.g. `Parse_UnknownWireValue_Throws`).
- Mirror the project layout: `tests/Frontier.Platform.X.Tests` per library, with emulator-backed
  tests under an `Integration/` folder inside it.
- Shared helpers compile-linked from `tests/Shared` (namespace `Frontier.TestSupport`).
- Required special tests where applicable: **byte-stability + golden file** for any contract,
  **architecture tests** for any boundary rule.
