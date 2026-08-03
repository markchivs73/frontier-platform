---
name: engineering-standards
description: Clean Architecture mapping, smart enums, interface/abstraction conventions, DI rules, general code standards. Use when designing any class/interface surface, creating types, or making structural code decisions.
---

# Engineering standards

These express Mark's preferred engineering style mapped onto this platform's architecture. Where a standard touches a platform invariant (wire format, dependency rules), the reconciliation below is the rule — don't re-trade it.

## Clean Architecture — how it maps here

The 11-library design *is* Clean Architecture; don't impose a different folder taxonomy on top of it:

| Clean Architecture ring | This repo |
|---|---|
| Domain core (entities, value objects) | `Frontier.Platform.Abstractions` — POCOs, smart enums, zero dependencies |
| Application (use cases, services) | The governance libraries (CascadeLogic, Hitl, Guardrails, …) — depend inward on Contracts + abstractions only |
| Infrastructure (adapters) | SDK-facing implementations (Cosmos repositories, DTF wiring, MAF invocation, Key Vault signing) — implement library abstractions |
| Composition root / presentation | `Host` (DI + boot), `Api`/`Ui` (REST/SignalR clients of the platform) |

**The dependency rule is inward, enforced by the S0.6 architecture tests.** A library importing an SDK type into its public surface is piercing the ring — see Abstractions below.

## Smart enums (preferred over standard .NET enums)

Use smart enums for domain concepts that carry behaviour or constrained transitions: `SectionStatus` (legal transitions live on the type — `CanTransitionTo(...)`), `ExecutionStatus`, gate kinds, failure classification (`IsRetryable`), decision kinds. A standard enum is acceptable only for pure structural discriminators with no behaviour and no invalid-combination risk (e.g. `EdgeKind`); when in doubt, smart enum.

**Non-negotiable reconciliation with the canonical profile:**
- Wire format is unchanged: smart enums serialize as **snake_case strings**, byte-identical to what a string enum would produce. Converters live in `Frontier.Platform.Serialization` (never in Abstractions).
- Every smart enum gets the standard contract tests: round-trip, byte-stability, golden file.
- Unknown wire values on deserialize follow contract-versioning rules (fail for current-version reads; migration adapters handle renames) — a smart enum must never silently coerce.
- DTF orchestration inputs/outputs pass through the same profile — smart enums in `WorkflowDefinition`/`ExecutionSnapshot` must round-trip through replay identically.
- ⚙️ **TD-11**: hand-rolled smart-enum base in Contracts (~50 lines, preserves the zero-dependency rule — recommended) vs `Ardalis.SmartEnum` (adds Contracts' only package dependency). Decide at S0.1, record in the plan register.

## Interfaces & abstractions

- Every cross-library and every SDK dependency sits behind an interface. The interface lives with the **consumer-facing library** (e.g. `ISnapshotStore` in SectionState; the Cosmos implementation is infrastructure).
- **No leaky abstractions:** no `CosmosClient`, `Container`, provider SDK types, or DTF types on any public interface. This is what keeps providers swappable (ADR-CA1) and libraries independently testable.
- Implementations are `internal sealed`; the public surface of a library is interfaces + contracts + its DI registration extension. Keep public types minimal — public is a commitment.
- One capability per interface; no `IXxxManager` grab-bags. If an interface needs a second unrelated method group, it's two interfaces.

## Dependency injection

- **Constructor injection only.** No service locator, no static singletons, no `IServiceProvider` passed around, no property injection.
- Each library exposes `AddFrontierXxx(this IServiceCollection, ...)` registering its own internals against its own abstractions. **Only Host calls these** — the library decides *how* it wires itself; Host decides *what* gets wired (composition-root rule, doc 12).
- Lifetimes: stateless services are singletons by default; anything else is a documented, deliberate choice.
- Configuration via the options pattern (`IOptions<T>`), bound + validated at boot — failures surface through doc 12's boot invariant checks, never at first use.

## General code standards

- **No `private` methods.** Members are `public` or `internal` only — if a class needs a private helper, that helper is a hidden responsibility: extract it into its own small `internal` class behind an interface where it can be independently tested. This is the testability expression of SRP.
- **Methods are ≤10–15 lines.** Beyond that, the method is probably crossing a responsibility boundary — decompose. (Hard cap enforced in code review, not by analyzer pedantry; a 17-line switch expression is fine, a 17-line procedure is not.)
- **Doc comments (`///`) on every public and internal type and method** — the *why* and the constraints (referencing ADRs/docs where relevant), never restating the *what*. If a comment would just paraphrase the code, improve the names instead.
- `sealed` by default; `record` for immutable data; file-scoped namespaces; no regions.
- Async all the way down; every public async method takes a `CancellationToken`; no `async void`; no `.Result`/`.Wait()`.
- Guard clauses at public boundaries; contract `Validate()` for domain invariants. Failure handling follows the two-loop model: classified exceptions (transient/permanent), **not** `Result<T>` monads — DTF's activity model is exception-based and Resilience classifies on exception type.
- `.editorconfig` + Roslyn analyzers committed at S0.1; analyzer warnings are errors like everything else. ⚙️ **TD-12**: analyzer pack choice (built-in .NET analyzers at `AnalysisLevel: latest-all` — recommended baseline — optionally + StyleCop.Analyzers); record at S0.1.
- Tests: AAA structure, named `Method_Scenario_ExpectedOutcome`, one behaviour per test; integration tests against emulators, never SDK mocks.
