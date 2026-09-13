# Frontier.Platform.Abstractions

The platform contract kernel — the root of the Frontier package graph and the only Frontier
assembly that other platform libraries may depend on freely.

**It has zero dependencies.** Not just no Frontier dependencies: no third-party packages
either, only the base class library. Every other package here sits above it, so anything added
to this one is inherited by all of them. An architecture test
(`PlatformAbstractions_HasNoFrontierOrThirdPartyDependencies`) enforces this on every build.

## Install

```bash
dotnet add package Frontier.Platform.Abstractions
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

There is no `AddFrontier…()` call: this package is types only, with nothing to register.

## What it contains

**Base machinery**

- `SmartEnum<T>` — the CRTP base for domain concepts that need behaviour alongside a value.
  Serializes as its canonical `snake_case` `Name` via `Frontier.Platform.Serialization`, which
  recognises it structurally rather than by reference.
- `IVersionedContract` — the marker carrying `schema_version`, which drives migration.
- `ContractMigrator` — read-time rehydration: minor additions fill from defaults, major bumps
  go through a registered adapter. It keys on the **stored** `schema_version` string, never on
  a CLR type name, which is why types can be moved between assemblies without touching data.

**Permanent-failure exceptions**

- `ContractViolationException` and `BudgetExceededException`. Both signal failures that must
  never be retried, as distinct from transient faults, which are retried per the policies in
  `Frontier.Platform.Resilience`.

**Execution identity**

- `ExecutionId` — the `{engagementId}::{workflowId}` instance format, stated once. It is kernel
  vocabulary because every tier needs to *mint* it and no lower assembly is visible to them all:
  a workload's composition root mints an id for scheduling, and the interpreter and audit family
  address durable state by it. Before this type, the format was written out in six places across
  two repositories (ADR-PA11).

  **An execution id is written, never read.** It is an addressing key, and nothing may split,
  slice or pattern-match it to recover the parts that went into it — identity travels as typed
  fields on the contracts instead. The parsing this type used to publish is gone (ADR-PA15), and
  it is worth knowing why: every reader wanted the engagement id to derive a partition key, and
  every caller already held it typed. Reading it back out instead mis-partitioned every audit
  record for a composite engagement id (ADR-PA12) and left dispatcher child ids permanently
  ambiguous. Both defects were in the reading, never in the format.

  `RunSeparator` is `~`, not `#`, for two hard reasons rather than taste: an execution id is
  embedded verbatim in Cosmos document ids, where `#` is illegal, and in URL path segments, where
  `#` begins a fragment and truncates the path.

  Publishing `Mint` does not loosen the one-instance-per-engagement-workflow invariant, which
  governs who may mint an id *for scheduling*, not who may know the format — and a named call is
  materially easier to police than a bare two-part string.

**Shared kernel contracts**

`EngagementId`, `ContextRequest`, `ExecutionStatus`, `GateKind`, `DecisionKind`,
`HitlDecision`, `ResolvedModelSummary`. These live here because more than one platform library
needs them; a type used by only one library belongs in that library.

## Key invariants

- **Nothing gets added here casually.** A type earns a place in the kernel by being needed by
  two or more platform libraries, or by crossing the boundary between a platform library and
  its consumer. Anything else belongs closer to where it is used.
- **Wire bytes are frozen.** These types are serialized into stored documents and hashed into
  audit records. Renaming a property or reordering members changes stored data and invalidates
  hashes; it is a breaking change, not a refactor.
- Every public member is tracked in `PublicAPI.Shipped.txt`. Because everything depends on this
  package, a break here is a break everywhere.
