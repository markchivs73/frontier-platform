# Frontier.Platform.ModelRoleConfig

Total indirection between agents and models. A workflow node names a **role** — `deep-reasoning`,
never `claude-fable-5`. This library holds the catalogue of roles, the versioned mappings from
role to model chain, and the governance loop that changes one.

The point is that changing which model serves a role is a **governed, auditable release**, not a
config edit. A mapping has a version, a rollout ring, a change reason, an approver, and a link to
the evidence that justified it — and an execution pins the version it started under, so a rollout
mid-flight cannot change what a running workflow is talking to.

## Install

```bash
dotnet add package Frontier.Platform.ModelRoleConfig
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierModelRoleConfig(configuration);
```

Only the composition root should call this. It binds and validates the `Cosmos` section
(`Cosmos:Endpoint`, `Cosmos:Key`, `Cosmos:Database`), registers a `CosmosClient` wired to the
shared `CanonicalProfile`, the role registry, the resolver, the governance service, and two boot
invariants: `CosmosTopologyCheck` and `RoleCatalogueCheck`.

### The consumer supplies one port

| Port | The question it answers |
|---|---|
| `IReferencedRolesSource` | Which role ids do this deployment's *published workflow definitions* reference? |

This library owns the invariant — every referenced role must have an active fleet or canary
mapping — and refuses to boot if one is orphaned. It cannot own the discovery, because a package
does not know where its consumer keeps published definitions (ADR-PA2). Supply the adapter, or
the `RoleCatalogueCheck` has nothing to check.

`ICircuitBreakerQuery` is the seam over Resilience, so the resolver can skip a chain entry whose
provider circuit is open without this library taking a dependency on Resilience. The default
registration answers "always closed"; wire a real implementation when you want fallback-on-open.

## What it contains

**The catalogue**

- `RoleDefinition` — what a role is for, what is commercially at stake if its output is wrong
  (`StakesLevel`: material / standard / mechanical), and the `CapabilityRequirements` any model in
  its chain must satisfy (minimum context window, tool use, structured output, latency budget).
- `RoleCatalogue` / `Phase1RoleCatalogue` — the declared set of roles.

**The mapping**

- `RoleMapping` — a versioned role → model release: the ordered `Chain` (index 0 primary, the rest
  fallbacks), its `RolloutRing`, canary percentage, change reason, approver, effective-from
  timestamp, an optional evidence ref, and the fleet version it can fall back to while it is still
  shadow or canary.
- `ModelEntry` — one chain entry: provider, model id, per-1k-token input/output/cache-read cost in
  GBP at scale 4, context window, max output tokens, and an optional caching-strategy key. The
  cost fields are what Guardrails builds an estimate from.
- `RolloutRing` — `shadow` (duplicated for comparison, never served) → `canary` (an
  engagement-stable percentage of new executions) → `fleet` (all of them).

**Resolution**

| Interface | What it does |
|---|---|
| `IRoleRegistry` | The catalogue, a role's active mapping, and any historical mapping version |
| `IModelResolver` | `ResolutionRequest` → `ResolvedModel`: ring assignment, canary bucketing, and the fallback-chain walk |
| `IMappingGovernanceService` | Propose → approve → roll out, plus instant rollback to a prior version |

`ResolvedModel` carries the audit fields as well as the model: which role, under which mapping
version, and **which chain position was served**. A sustained non-zero chain position is an alarm
signal, not a detail — it means the primary is failing and nobody noticed.

## Storage

`model-role-config` holds append-only version documents (`{roleId}:v{n}`) plus a `current`
pointer. Rollback repoints `{roleId}:current` at an existing version through `IRoleMappingWriter`
— it never rewrites a version document, so the history of what was live and when stays intact.

## Key invariants

- **A node names a role, never a model.** A model id anywhere in a workflow definition defeats the
  entire indirection.
- **Mappings are versioned and append-only**, and an execution pins the version it resolved under.
- **Canary assignment is engagement-stable.** The same engagement lands in the same ring for the
  life of the mapping, so a comparison is a comparison rather than noise.
- **The boot check is a real gate.** A published definition referencing a role with no active
  fleet or canary mapping stops the process, rather than failing on the first invocation that
  needs it.
- **Costs are decimals at a declared scale**, serialized as strings by the canonical profile. They
  feed budget arithmetic; a float here is a rounding bug in a ledger.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
