# Frontier.Platform.Observability

Telemetry contracts, the metric catalogue, and the empirical-evidence read surface.

The organising decision here is the **two-store rule**, and it is worth stating before anything
else: operational telemetry goes to the OTEL backend, and *empirical conclusions are never read
from it*. Sampled, short-retention, unsigned data is right for a dashboard and wrong for a
statement about whether an agent is trustworthy. Those questions are answered from the audit
store, which is complete, signed and long-retention.

So this library has two distinct halves — the metric-emitting side that feeds OTEL, and the
querying side that reads evidence — and they do not share a data source.

## Install

```bash
dotnet add package Frontier.Platform.Observability
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierObservability(configuration);
```

Only the composition root should call this. It registers the context metrics emitter, the metric
catalogue, the empirical query service, the maturity tracker, the execution monitor feed, the
recovery-findings recorder, and the `OtelPipelineCheck` boot invariant.

## What it contains

**The catalogue**

`IMetricCatalogue` is the authoritative list of platform metrics — each a `MetricDefinition` with
a name, `MetricInstrumentType` (counter, observable gauge, histogram), unit, description, and a
**closed dimension set**.

It also exposes the single `PlatformMeter`. Every recorder in the platform uses that meter and
never creates its own, so every metric reaches the same OTEL pipeline by construction rather than
by each library remembering to configure one.

**No dimension may be id-shaped.** An execution id or engagement id as a metric dimension is
unbounded cardinality — it turns a metric into a log with a much worse cost profile, and it is the
single easiest way to make a metrics backend unusable. That is why the dimension set is closed
rather than open.

**Metric emission**

| Interface | What it does |
|---|---|
| `IContextMetricsEmitter` | Per-tier context-assembly outcomes — cache hit, tokens, bytes, cost delta — and an engagement rollup |
| `IRecoveryFindingsRecorder` | Increments `recovery.findings`, tagged by `RecoveryFindingType` |

`ContextTierMetrics` and `EngagementMetricsSnapshot` are the shapes those carry.

`RecoveryFindingType` deserves reading on its own — it enumerates what the recovery sweep can
find: a stale `is_latest` projection flag healed, a lost gate-decision event re-raised, a
terminally-statused execution with no audit record, and a permanently faulted instance whose
projection still claimed to be running. **A healthy platform reports ~0 across all of them.** A
non-zero count is what the governance health endpoint and the OTEL dashboards alert on — these are
not routine maintenance counters.

**Empirical evidence**

| Interface | What it answers |
|---|---|
| `IEmpiricalQueryService` | Cache economics per tier, retry distribution, validator outcomes, gate evidence, and the per-node measured-reality overlay for the design canvas |
| `IMaturityTracker` | Assessments per (agent role × engagement type) |
| `IExecutionMonitorFeed` | A live `ExecutionMetricEvent` stream for a running execution |

`EmpiricalScope` narrows a query by engagement type, workflow, definition version, agent role and
date range; every field is optional and `null` means "all values".

**Maturity**

`MaturityAssessment` reports a `MaturityBand` — `provisional` → `calibrated` → `trusted` — per
(agent role × engagement type), together with the sample size, window, and the validator pass,
HITL rejection and override rates that produced it. `MaturityThresholds` holds the cut-offs as
versioned data (90-day window, minimum sample of 20 by default) precisely because they will be
tuned against real evidence and that tuning should be governed rather than committed.

Two properties matter more than the numbers:

- **`EvidenceQueryRef` makes an assessment reproducible.** A band nobody can re-derive is an
  opinion.
- **Bands are computed, not acted on.** They are displayed and audit-referenced. Governance
  behaviour that keys off them — lighter gates for a trusted agent — is a later, separate
  decision. Computing a band and immediately acting on it would couple a measurement change to a
  control change.

## Phase 1 status

`Phase1EmpiricalQueryService`, `Phase1MaturityTracker` and `Phase1ExecutionMonitorFeed` are
honest stubs: they return empty results until the change-feed aggregation layer populates
`metrics-aggregates`, and the monitor feed's real span-processor tap is later work. The interfaces
are stable; the data is not there yet. Treat an empty result as "no aggregation layer", not as
"no evidence".

## Key invariants

- **The two-store rule.** Empirical conclusions come from the audit store, never the OTEL backend.
- **One meter.** Recorders use `IMetricCatalogue.PlatformMeter`.
- **Closed, low-cardinality dimensions.** Never an id.
- **Maturity is computed, not enforced.**

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`.
