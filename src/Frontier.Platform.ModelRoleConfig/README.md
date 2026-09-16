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

Only the composition root should call this. It binds and validates the `ModelRoleGovernance` section
(`MappingGovernanceOptions`: `DecisionMaxAttempts` 8, `DecisionBaseDelayMs` 25, `DecisionMaxDelayMs`
1000 — how often a decision re-reads and re-allocates when another approval got there first) and the
`Cosmos` section
(`Cosmos:Endpoint`, `Cosmos:Key`, `Cosmos:Database`), registers a `CosmosClient` wired to the
shared `CanonicalProfile`, the role registry, the resolver, the mapping pinner, the governance service, and two boot
invariants: `CosmosTopologyCheck` and `RoleCatalogueCheck`.

### The consumer supplies one port

| Port | The question it answers |
|---|---|
| `IReferencedRolesSource` | Which role ids do this deployment's *published workflow definitions* reference? |
| `IMappingDecisionRecorder` | Where does a governance decision get recorded before it is made? |

`IMappingDecisionRecorder` is how every propose/approve/reject/withdraw/promote/rollback reaches
your signed governance audit chain. This library is governance-tier and may not reference
`Frontier.Platform.Audit` (ADR-PA5), so it declares the port and you adapt it over your own
governance audit writer. **There is no default registration on purpose:** a no-op default would
turn an unwired consumer into a silent audit gap, so an unregistered recorder fails at
composition rather than at the first decision.

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
  shadow or canary. A chain is all models or all agents, never mixed (ADR-PA27).
- `ChainEntry` — the abstract chain entry: a provider and the ISO 4217 currency of its costs
  (ADR-PA21). `TargetId` is what the circuit breaker and audit key on.
- `ModelEntry` — a model: model id, per-1k-token input/output/cache-read cost at scale 4, context
  window, max output tokens, an optional caching-strategy key, and — for providers such as
  `azure-openai` — an optional endpoint and deployment. The cost fields are what Guardrails builds
  an estimate from.
- `AgentEntry` — a registered remote agent reached over A2A (ADR-PA27): the consumer registry's
  resource name and version, and a positive fixed cost per invocation, which is the estimate for
  that call. Whether the resource is active is checked by the consumer, which owns the registry.
- `RolloutRing` — `shadow` (duplicated for comparison, never served) → `canary` (an
  engagement-stable percentage of new executions) → `fleet` (all of them).

**Resolution**

| Interface | What it does |
|---|---|
| `IRoleRegistry` | The catalogue, a role's active mapping, and any historical mapping version |
| `IModelResolver` | `ResolutionRequest` → `ResolvedModel`: ring assignment, canary bucketing, and the fallback-chain walk |
| `IMappingPinner` | Pins the version each role is **served** at execution start, one `ModelRolePin` per role (ADR-PA29) |
| `IMappingGovernanceService` | Propose → approve (canary) → promote (fleet), with reject/withdraw, plus instant rollback |
| `IMappingVersionHistory` | A role's append-only version history, each row marked with whether it is current |

`ResolvedModel` carries the audit fields as well as the model: which role, under which mapping
version, and **which chain position was served**. A sustained non-zero chain position is an alarm
signal, not a detail — it means the primary is failing and nobody noticed.

**Governance (ADR-PA32).** A mapping change is proposed, not edited. `ProposeAsync` stores a
`MappingChangeProposal` in the role's own partition beside its versions; `ApproveAsync` allocates the
next mapping version and makes it live in the **canary** ring; `PromoteAsync` moves it to **fleet**;
`RejectAsync` and `WithdrawAsync` close it unused. The service stamps `ApprovedBy`,
`EffectiveFromUtc` and the version itself — they are never taken from the caller — and the approver
may never be the proposer.

Three properties are worth knowing because they are load-bearing:

- **Version allocation is concurrency-safe.** An approval creates `{roleId}:v{n}` with a *create-only*
  write inside one transactional batch that also replaces the proposal under its ETag and repoints
  `current`. Two approvals that both computed the same `n` cannot both land; the loser re-reads,
  re-allocates and retries. There is no counter to drift from the documents.
- **Promotion appends rather than rewrites.** A version is immutable and its ring is part of it, so
  promoting to fleet writes a *new* version carrying the same chain instead of editing the canary one.
  The history of what was live, in which ring, and when stays intact.
- **A shadow version never becomes `current`.** Shadow execution itself is deferred — nothing
  duplicates an invocation yet — but the state machine keeps a `shadow` slot so enabling it later is
  additive. A shadow version is stored and never pointed at, so it cannot serve and cannot trip
  `RoleCatalogueCheck`.

**Every refusal has its own type (ADR-PA34).** They all derive from `ContractViolationException`, so
they stay permanent failures that are never retried and every existing `catch` keeps working — but a
caller maps a status code by catching a type rather than by matching message text:

| Refusal | Means | Map to |
|---|---|---|
| `InvalidMappingChangeException` | No role or reason, empty/mixed chain, target-less entry, canary percent outside 1–100 | 400 |
| `UnknownMappingProposalException` | The role holds no proposal by that id | 404 |
| `UnknownMappingVersionException` | No such mapping version, or no `current` pointer | 404 |
| `DistinctApproverRequiredException` | The decider is the proposer | 403 |
| `ProposerOnlyWithdrawalException` | Someone other than the proposer tried to withdraw | 403 |
| `IllegalMappingTransitionException` | The proposal is not in a state that decision may move | 409 |
| `MappingProposalAlreadyPendingException` | The role already holds a proposal awaiting a decision | 409 |
| `MappingVersionNotRollbackEligibleException` | The target version is a shadow that never served | 409 |
| `MappingConcurrencyConflictException` | The expected concurrency token is stale | 409 |

**Deciding safely against a stale view (ADR-PA34).** Every proposal you read carries a
`ConcurrencyToken`. Pass it back as `expectedConcurrencyToken` and a decision taken on a view
somebody else has already moved is **refused**, naming the proposal's new state and who decided it —
which is what an operator has to see. Omit it and the decision proceeds unguarded, for a caller with
no prior view to be stale.

This is deliberately *not* the same thing as the decision retry. The retry loses a race for a mapping
*version* and re-allocating still produces the decision you asked for, so it is retried silently. A
stale token means the *proposal* moved, so retrying would decide something you have never seen — that
one stops.

**Listing proposals.** `MappingProposalQuery` takes an **optional** `RoleId` and a **set** of
`States`. Naming a role keeps the query inside that role's partition; omitting it runs a bounded
cross-partition query — page size capped, continuation-token paged, fan-out capped — which is what
answers "every proposal awaiting a decision, across every role" for an admin screen. There is no
default state filter: the caller chooses, so "everything" stays expressible.

Four rules the service enforces (map the first to 409 — the request is not malformed, the role is
occupied — and the rest per the table above):

- **One undecided proposal per role.** A second propose is refused while one is awaiting a decision,
  and the refusal names the proposal in the way and who raised it. The slot is released once that
  proposal is *decided* — approved, promoted, rejected or withdrawn — not only when it is terminal.
  An approver should never have to work out which of several competing proposals wins.
- **Canary percent is 1–100.** Zero is refused: it would approve a mapping that reports itself live in
  the canary ring while every engagement is served its fleet predecessor. Omitting the field in JSON
  binds it as 0, so this is the likely accident, not an exotic one.
- **Only the proposer may withdraw.** Killing your own idea is a withdrawal; ending someone else's is
  a rejection.
- **A rejection needs a distinct approver**, the same rule approval carries — it is a recorded
  decision on another principal's change.

`RollbackToVersionAsync` requires its target, refuses a missing or shadow one, and returns a
`MappingRollbackResult` naming the previous version, the current version and when it took effect.
The older `ProposeChangeAsync`, `ApproveAsync(proposalId, approverId, …)` and `RollbackAsync` overloads
are `[Obsolete]` — they carry no actor or reason, which a governance record requires.

**Pinning (ADR-PA29).** `ModelRolePin` records a role's served mapping version and its ring:
canary assignment and the shadow/canary → fleet fallback are decided once, when the pin is taken.
A `ResolutionRequest` carrying `Pin` reads exactly that version and walks its fallback chain; it
never re-evaluates rings, so a rollback or promotion reaches new executions only. A role with no
mapping fails the pin as a `ContractViolationException` — permanent, before any agent runs. The
pin is a separate interface so `IModelResolver` and `IRoleRegistry` keep their published shape.

## Storage

`model-role-config` holds append-only version documents (`{roleId}:v{n}`), a `current`
pointer, and — since ADR-PA32 — proposal documents (`{roleId}:proposal:{proposalId}`, `doc_type`
`mapping_proposal`) in the same `/role_id` partition, which is what lets an approval allocate a
version and move its proposal atomically. A proposal is the one mutable document of the three:
each decision replaces it under an ETag guard, while the version it produces is never rewritten. A chain entry writes `target: "agent"` only for an agent; an entry without `target` is a
model, so every document written before ADR-PA27 reads unchanged, and a mixed or incomplete chain
is refused when read. Rollback repoints `{roleId}:current` at an existing version through `IRoleMappingWriter`
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
