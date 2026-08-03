---
name: library-boundaries
description: Platform library ownership map, project reference rules, consumer-owned ports, DI wiring rules, architecture tests. Use when creating projects, adding package/project references, wiring DI, or writing architecture tests.
---

# Library boundaries & ownership

The premise: **governance is the IP** — each guarantee is enforced by exactly one library,
once. Duplicating a concern across libraries is how drift starts.

## Reference rules (enforced by architecture tests — never weaken one to pass)

1. **No platform library may reference any `Frontier.Reason.*` assembly** (ADR-PA2). This is
   the severability guarantee that let this repo exist. Enforced at two levels:
   `PlatformLibrary_DoesNotReferenceReasonWorkflowAssemblies` on the assembly graph, and
   `PlatformAssemblyTypes_DoNotDependOnReasonWorkflowTypes` (NetArchTest) on type dependencies
   — the second catches leakage the first cannot see, such as a compile-linked source file.
2. **`Platform.Abstractions` is the zero-dependency contract kernel** (ADR-PA1): no Frontier
   references and no third-party packages, only the BCL. Everything sits above it, so anything
   added here is inherited everywhere.
3. **`Serialization` references only `Platform.Abstractions`.**
4. **Every other platform library** references `Platform.Abstractions` + `Serialization` +
   its own external SDKs — never another platform library.

## Consumer-owned ports — how this repo stays severable

When a platform library needs data that only the consuming solution has, it **declares an
interface it owns** and the solution implements it in its composition root. The library never
reaches out; the dependency is inverted at the boundary.

The live example is `IReferencedRolesSource` in `ModelRoleConfig`. The rule "every referenced
role has an active fleet/canary mapping" is platform-owned governance. *How you discover which
roles a solution references* is solution knowledge. So the library declares the port, and the
consumer adapts it over whatever store it happens to use.

Apply this whenever the alternative would be a reference pointing outward:

- The port lives in the library that *needs* the data, not the one that has it.
- No SDK types on the port's surface — plain contracts only.
- The port is part of the public API surface, so it is a compatibility obligation. Design it
  as narrowly as the rule requires.

If you find yourself wanting a reference to a consuming assembly, you want a port.

## Ownership map (who owns what — put logic where it belongs)

| Concern | Owner |
|---|---|
| Canonical profile, contract converters, byte-stability | Serialization |
| Smart-enum base, contract versioning/migration, permanent-failure exceptions, shared kernel vocabulary | Abstractions |
| Three-tier context, cache breakpoints, refresh signals | ContextAssembly |
| Audit consolidation, signing, hash chains, retention, governance queries | Audit |
| Gates, approval routing, rollback mechanics | Hitl |
| Budgets, rate limits, cost alerts, kill switch | Guardrails |
| Role catalogue, role→model mapping, mapping governance | ModelRoleConfig |
| Retry/breaker/timeout *policy* as data; failure classification | Resilience |
| Metrics catalogue, maturity bands, trend alerting | Observability |

If a feature seems to need logic in two libraries, one of them owns it and the other consumes
a contract. Decide which before writing code.

## DI wiring

Each library exposes `AddFrontierXxx(this IServiceCollection, …)` registering its own internals
against its own abstractions. **Only the consumer's composition root calls these.** A library
registering another library's services is a violation. Implementations are `internal sealed`;
the public surface is interfaces, contracts and the registration extension.

## Common misplacements to reject

- Retry logic written inline instead of a named Polly profile (Resilience owns policy)
- A component fetching its own context (ContextAssembly owns assembly)
- Budget checks outside admission control (Guardrails owns admission)
- A model ID anywhere but a role mapping (roles only)
- Custom metric dimensions or ad-hoc instrumentation (Observability owns the dimension set)
- Serialization logic in Abstractions (converters belong in Serialization)

## What this repo does *not* own

Orchestration, workflow definitions, the definition compiler, cascade logic, section state and
the DTF interpreter all live in the consuming solution. Where this repo names one of their
concepts — `execution-snapshots` in a topology check, for example — it is consuming a **port**,
not claiming ownership.
