# Frontier.Platform.Hitl

Human-in-the-loop approvals: opening a gate, recording the decision, querying what is waiting,
and planning the rollback when a decision is a rejection.

**This package is solution-agnostic and holds no orchestration.** It has no reference to a durable
task framework and schedules nothing. A caller drives it from its own activity shells — that
separation is what lets a solution take approvals without taking an interpreter (ADR-PA5), and
it is why `ApprovalRequestFactory` and `IRollbackPlanner` are public: the shells live with the
consumer, so the pure logic they need must be reachable from there.

## Install

```bash
dotnet add package Frontier.Platform.Hitl
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierHitl(configuration);
```

Only the composition root should call this. It binds and validates the `Cosmos` configuration
section (`Cosmos:Endpoint`, `Cosmos:Key`, `Cosmos:Database`), registers a `CosmosClient` wired to
the shared `CanonicalProfile`, the approval store and query service, the rollback planner, and the
`CosmosTopologyCheck` boot invariant.

## What it contains

**The contracts**

- `ApprovalRequest` — the `approvals` container's document: one per *gate visit*, partitioned by
  engagement. Carries the gate's kind and approver roles, the section refs shown to the approver,
  its status, when it opened, when it escalates, and the embedded `HitlDecision` once decided.
- `GateOpenRequest` — everything needed to open a gate, built by the caller's orchestrator from
  the gate node and the current execution state.
- `ApprovalRequestStatus` — `pending` → `decided`, with `escalated` when the timeout fires and
  `expired` as a reporting-only terminal state.
- `RollbackPlan` — the invalid set (regenerate) and the restore set (keep the approved snapshot).

**The services**

| Interface | What it does |
|---|---|
| `IApprovalStore` | Convergent upsert of an approval request; and the recovery sweep's read of every decided request across engagements |
| `IApprovalQueryService` | Point-read by id, list by engagement, list pending across engagements, list escalated |
| `IRollbackPlanner` | Splits a rejected gate's downstream set into what is regenerated and what is restored |

**The pure helpers**

- `ApprovalRequestFactory.Open(GateOpenRequest)` — builds a new pending request. The id is
  `{executionId}:{gateId}:{occurrence}`, distinct per visit, so a re-visit after rollback cannot
  be satisfied by a stale decision event. `escalate_at_utc` is `null` when the gate declares no
  timeout. Sandbox executions get a 7-day TTL; real gates never expire (`ttl = -1`).
- `RollbackPlanner.Plan(...)` — the invalid set is the rollback target plus the cascade's
  downstream set; the restore set is the execution's approved snapshot refs minus the invalid set.

## Two things the caller owns

**Replay-safe time.** `GateOpenRequest.RequestedAtUtc` is supplied by the caller and must be the
orchestrator's replay-stable current time, not `DateTime.UtcNow`. It determines the request's
persisted timestamp and derives its escalation deadline — a wall-clock read inside an activity
would give a replayed execution a different escalation time than the original.

**Purity of the planner.** `IRollbackPlanner` is consulted from inside an orchestrator body, where
every decision is replayed. It takes the cascade's downstream set as an argument rather than
computing one, so it has no cascade dependency and no I/O: a total function of its arguments, with
the same answer on every replay. An implementation that reaches for a store breaks replay.

## Key invariants

- **The `approvals` container is a projection, never orchestration truth.** Durable history remains
  authoritative. Upserts are convergent, so a retried activity reproduces the same document.
- **Partition key is `/engagementId`**, and the request id is deterministic per gate visit.
- **The platform never auto-decides.** An escalation timer moves a request to `escalated` and,
  at most, to the reporting-only `expired` state. It does not approve or reject on a human's behalf.
- **The decision write is ETag-guarded.** Embedding the decision into a pending request is the one
  place in the platform that does a conditional update; a lost update here would lose a decision.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
