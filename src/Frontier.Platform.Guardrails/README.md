# Frontier.Platform.Guardrails

Admission control and budget metering. The unbypassable check immediately before a model call —
positioned exactly the way context assembly is unbypassable for prompts, and for the same reason:
a control that can be routed around is not a control.

Two halves that must stay in step: a **pre-call estimate** decides whether an invocation may
proceed and at what output ceiling, and a **post-call usage record** meters what it actually cost.
The estimate is only as good as the metering behind it.

## Install

```bash
dotnet add package Frontier.Platform.Guardrails
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierGuardrails();                        // in-process ledger and kill switch
services.AddFrontierGuardrails(guardrailLedgerContainer); // Cosmos-backed ledger, in-process kill switch
services.AddFrontierGuardrails(                           // Cosmos-backed ledger and shared kill switch
    guardrailLedgerContainer,
    platformConfigContainer,
    GuardrailsServiceCollectionExtensions.DefaultKillSwitchCacheDuration);
services.AddFrontierGuardrails(                           // the same, containers resolved from DI
    configuration,
    GuardrailsServiceCollectionExtensions.DefaultKillSwitchCacheDuration);
```

Only the composition root should call these. All four register the admission controller, the
budget hierarchy and the kill switch; they differ in the `IBudgetLedger` and `IKillSwitch`
implementations. The Cosmos variants use `guardrail-ledger` (partition key `/engagement_id`) and,
for the shared kill switch, the consumer's `platform-config` (partition key `/config_type`). This
library does not build its own `CosmosClient`. The container overloads take the containers
directly. The configuration overload binds `Cosmos:Database` into `CosmosOptions` and resolves
both containers from the `CosmosClient` the composition root registers, when a guardrail is first
resolved, as the other platform libraries' Cosmos stores do.

Pick the in-process variants for tests, tools and single-process runs. Anything with more than one
process — an API head that throws the switch and workers that must stop — needs a shared variant:
an in-process kill switch stops admissions only in the process it was thrown in.

## What it contains

**The decision path**

| Interface | What it does |
|---|---|
| `IAdmissionController` | `InvocationCostEstimate` → `AdmissionDecision`, against the resolved policy |
| `IBudgetLedger` | Records actual usage; returns an aggregated `BudgetSnapshot` for a scope |
| `IBudgetHierarchy` | Checks and records across invocation → engagement → platform scopes without double-counting |
| `IKillSwitch` | Global emergency brake: deny everything, independent of budgets |

## The kill switch

The shared switch is one document in `platform-config`: id `kill-switch:fleet`, config type
`kill-switch`, holding `active`, `reason` and `changed_at`. `IKillSwitch` reads and writes it
uncached, for the governed activate and resume routes. Admission reads it through a cache of the
duration given at registration (doc 07 §5a: about 10 s), so a switch thrown anywhere stops new
admissions on every worker within that window. A missing document means never thrown; a missing
container is an error, not "never thrown".

While it is thrown, `IAdmissionController.AdmitAsync` throws `GuardrailsSuspendedException`
(Abstractions): a **permanent** failure in the `guardrail` category, never retried. Invocations
already past admission finish. If the switch cannot be read, the policy's `FailureMode` decides, as
below: `FailOpenWithAudit` admits and logs a `guardrail_bypass` warning (through
`ILogger<KillSwitchGate>` when the consumer registers logging); `FailClosed` rethrows the read
failure, which the activity retry treats as transient. A failed read is never cached.

**The policy**

- `GuardrailPolicy` — a named, layered policy: platform default → engagement-type default →
  per-engagement override, most-specific wins per field. Carries a `BudgetSpec` per scope, a soft
  threshold percentage for alerting, and a `FailureMode`.
- `BudgetSpec` — a ceiling: max tokens, max cost in GBP, max agent invocations. Any field left
  `null` is unbounded at that scope. `MaxAgentInvocations` is the runaway-loop fuse — a cascade
  regeneration ping-pong or a buggy loop node surfaces here as a hard, attributable stop rather
  than a bill.
- `BudgetScopeKind` / `BudgetScopeRef` — invocation, execution, engagement, fleet. Fleet is
  alert-only and never an admission scope.
- `Phase1GuardrailPolicyCatalogue` — the compiled-in policies: a default, and a tighter `sandbox`
  policy automatically selected for `SANDBOX-`-prefixed executions so a test run cannot spend
  like a real one.

**The data**

- `InvocationCostEstimate` — built once context assembly has fixed the prompt size and role
  resolution has fixed the model and its cost per token.
- `UsageRecord` — actual input/output tokens and cost, from the provider's reported usage.
- `AdmissionDecision` / `AdmissionResult` / `BudgetSnapshot`.

## Shape, don't truncate

`AdmissionResult` has four outcomes, and the interesting one is `ProceedWithWarning`. When
remaining budget is positive but smaller than the node asked for, admission proceeds with a lower
`GrantedMaxOutputTokens` — the provider is told to produce less, rather than the call being denied
or its output cut off after the fact. A truncated response is a corrupted artifact; a shorter one
that knew it was short is not.

`Deny` is for a hard ceiling that would be breached: the caller raises `BudgetExceededException`,
which is a **permanent** failure and is never retried. `Deferred` carries a `RetryAfter` and is
transient.

## When the ledger itself is down

`FailureMode` decides, and the default is deliberate: **`FailOpenWithAudit`** — admit the
invocation, emit a `guardrail_bypass` audit event, and raise an alarm. A commercial platform that
stops working because its metering is unreachable has traded a cost risk for an availability
outage. Regulated deployments override to `FailClosed`, where a deny is treated as transient
because the store may recover.

That default is a policy statement, not an oversight. Change it per deployment; do not change it
by accident.

## Key invariants

- **Metering is idempotent on `CorrelationId`.** An activity retry must not double-count usage —
  at any level of the hierarchy.
- **Admission sits immediately before the model call**, after context assembly and role
  resolution have fixed the estimate's inputs. Earlier and it is estimating the wrong thing;
  later and it is not a gate.
- **A denial is permanent, a deferral is transient.** The two are different failure classes and
  the retry machinery treats them differently.
- **Fleet scope never denies.** It exists to alert.
- **Costs are decimals at a declared scale**, serialized as strings by the canonical profile.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
