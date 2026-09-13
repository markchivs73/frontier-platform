# Frontier.Platform.Resilience

Retry, circuit breaking, bulkheads and timeouts — as **data**. A policy change is a profile
change, not a deployment.

The whole library turns on one question that every retry handler must answer identically: *is this
failure worth trying again?* There is one taxonomy, one classifier, and one mapping table, so the
inner Polly pipeline and the outer durable-task retry loop can never disagree about it. Two
handlers with two opinions is how a permanent failure gets retried five times at full token cost.

## Install

```bash
dotnet add package Frontier.Platform.Resilience
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierResilience();
```

Only the composition root should call this. There is no configuration to bind: it registers the
classifier, the policy provider, the circuit-state provider and the retry budget as singletons
over the compiled-in `Phase1ResilienceProfileCatalogue`, plus the `TimeoutHierarchyCheck` boot
invariant.

The catalogue is compiled in **on purpose**. Profiles are also seeded into a
`resilience-profiles` container field-for-field, but the provider reads the compiled-in copy — so
a cold start with the store unreachable still has a working pipeline. Resilience infrastructure
that needs a healthy database to tell you how to survive an unhealthy one is not resilience.

## The two loops

```
Orchestrator ──GetTaskOptions(profile)──► outer: durable-task activity retry
                                              │
Activity ──────GetPipeline(profile)──────► inner: Timeout(total)
                                                  → Bulkhead
                                                  → CircuitBreaker
                                                  → Retry
                                                  → Timeout(per-attempt)
                                                        → provider call
```

The inner loop absorbs a bad minute at the provider. The outer loop re-runs the whole activity
after the inner loop is exhausted. `IResiliencePolicyProvider` builds both from the same named
profile, so they cannot drift apart.

**`ResiliencePipeline` and `TaskOptions` appear directly on the public interface.** That is
deliberate: handing callers ready-to-use Polly and durable-task policy objects is this library's
entire purpose. Wrapping them behind a swappable abstraction would abstract away the thing being
delivered.

## What it contains

**The taxonomy**

`FailureClass` is three-way, and the three-way split is the design:

| Class | Retry? | Meaning |
|---|---|---|
| `transient` | Yes, with jittered backoff | Provider 5xx, network blip, open circuit |
| `deferred` | Yes, after the provider's stated delay | 429 with a `Retry-After`, Cosmos throttling |
| `permanent` | **Never** | Contract violation, budget exceeded — and anything unmapped |

`IFailureClassifier.Classify` returns a `FailureClassification`: the class, an optional
provider-stated `RetryAfter`, and a stable snake_case reason code for telemetry and triage.

**Unmapped exceptions classify `permanent`, with reason code `unclassified`.** That is the fail-safe
direction: don't retry what you don't understand. The alternative default burns tokens on a call
that was never going to succeed.

**The profile**

`ResilienceProfile` composes four specs — `InnerRetrySpec` (attempts, backoff strategy, base and
max delay), the per-attempt `TimeoutMs`, `CircuitBreakerSpec` (failure ratio, minimum throughput,
sampling window, break duration), `BulkheadSpec` (scope, max concurrent, max queue) and
`OuterRetrySpec` (activity re-runs) — under a `ProfileId` and a version.

**Circuit state**

`ICircuitStateProvider` reports breaker state per `(provider, modelId)` and publishes
`CircuitTransition`s to subscribers. Model-Role Config consults it to skip a chain entry whose
circuit is open; observability and alerting subscribe to the transitions.

**The amplification guard**

`IRetryBudget` bounds how many retries a *single execution* may spend across a sliding window,
independent of what any one profile allows. Without it, a graph of twenty nodes each allowed five
attempts is a hundred-attempt execution nobody authorised. Exhaustion converts a would-be retry
into immediate escalation with reason `retry_budget_exhausted`.

## Timeouts nest, and the boot check enforces it

```
per-attempt provider timeout  <  total pipeline timeout  <  durable activity timeout
```

Get this inverted and the outer layer kills the call before the inner layer can retry it — the
retries are configured, they simply never happen. `TimeoutHierarchyCheck` validates the ordering
for every compiled-in profile at startup and refuses to boot if one is wrong. Human-gate
escalation sits above all of it by construction: gate timeouts are orchestrator timers, not
activity calls.

## Called from an orchestrator body — so it must be pure

`IResiliencePolicyProvider` is consulted from inside an orchestrator body, where every decision is
replayed. Both methods are pure functions of the profile name over the compiled-in catalogue: no
I/O, no clock, the same answer on every replay. An implementation that fetches a profile from a
store breaks replay for every in-flight execution.

## Key invariants

- **Policy is data.** Retry behaviour is a `RetryPolicySpec` on a workflow definition and a named
  profile here — never a `catch` block with a loop in it.
- **A contract violation is never retried.** It is permanent by definition; retrying a prompt that
  will always fail validation is the most expensive mistake this library can make.
- **Unmapped means permanent.** Adding a mapping is a deliberate act.
- **One classifier, one table.** Both loops consult the same `Classify` call.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
