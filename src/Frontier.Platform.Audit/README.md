# Frontier.Platform.Audit

The **evidence tier**: what an execution did, hashed into a per-engagement chain, signed, and
stored append-only. Everything else in the platform records state that can be rebuilt; this
package records state that must not be.

That distinction drives every decision here. An audit record is not a projection — there is no
"rebuild it from history" fallback, because the record *is* the history once the orchestration
that produced it has aged out. So the store is create-only, the chain is verifiable back to
genesis, and a schema the current build cannot read is refused rather than degraded.

## Install

```bash
dotnet add package Frontier.Platform.Audit
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierAudit(configuration);
```

Only the composition root should call this. It binds and validates the `Cosmos` configuration
section, registers the `CosmosClient` and `BlobServiceClient` (both wired to the shared
`CanonicalProfile`), the record store, signer, query service and telemetry staging, the archival
export hosted service, and two boot invariants: `SigningKeyCheck` and `CosmosTopologyCheck`.

Configuration it expects:

| Key | Purpose |
|---|---|
| `Cosmos:Endpoint`, `Cosmos:Key`, `Cosmos:Database` | The account holding `audit-records` |
| `ConnectionStrings:AzureWebJobsStorage` | Blob storage for archival export |

## What it contains

**The contracts**

- `AuditRecord` — one execution's unsigned record: identity, the pinned definition version and
  hash, start/close timestamps, terminal status, the orchestration event timeline, agent
  invocations, validator outcomes, human decisions and aggregated cache metrics.
- `SignedAuditRecord` — the same fields plus `previous_record_hash`, `record_hash`, `signature`
  and `signing_key_id`. This is what lands in Cosmos.
- `WorkflowEvent` / `WorkflowEventType` — the durable-history event kinds the consolidator maps
  onto the timeline (task scheduled/completed/failed/retried, external event, timer, sub-orchestration,
  execution started/completed).
- `AgentInvocation`, `ToolCall`, `ValidatorOutcome`, `HumanDecisionRecord`, `CacheMetrics` /
  `CacheTierMetrics` — the per-invocation evidence the record aggregates.
- `AuditTelemetryRecord` — one invocation's staged telemetry, written by the invocation pipeline
  as it happens and read back at consolidation.
- `AuditQuery` / `AuditSummary` / `VerificationResult` — the governance read surface.

**The services**

| Interface | What it does |
|---|---|
| `IAuditRecordStore` | Point-read one record, read an engagement's chain in order, create (never update) |
| `IAuditSigner` | Chain, sign, persist; and re-verify a record's signature and its chain back to genesis |
| `IAuditQueryService` | The governance and empirical-validation reads: one record, a filtered list, a full chain |
| `IAuditTelemetryStaging` | Per-invocation staging, upserted idempotently under activity retry |
| `IKeyProvider` | Resolves the current signing key. `DevKeyProvider` is registered for local dev; a Key Vault-backed implementation swaps in here without touching a consumer |
| `IAuditRecordExporter` | Writes a record's canonical bytes to immutable Blob storage (internal; driven by the change-feed handler) |

**Archival**

`ArchivalAuditExportHostedService` runs the Cosmos change feed over `audit-records` and
`ArchivalAuditChangeFeedHandler` exports each new record's canonical bytes to Blob storage — for
compliance retention, SIEM ingestion, and as a comparison copy for tamper detection. The handler
strips Cosmos-injected system metadata (`_rid`, `_etag`, `_ts` and friends) first — those are never
part of a contract's canonical bytes, and an archive that carried them would not re-verify.
Re-processing an event is convergent: the same execution id overwrites the same blob.

## How the chain works

Records chain **per engagement**, in `closed_at_utc` order:

```
genesis = SHA-256(engagementId)
record_hash  = SHA-256(canonical bytes of fields 0–14, with 15–17 cleared)
signature    = HMAC-SHA256(record_hash, signing key)
```

Each record's `previous_record_hash` is its predecessor's `record_hash`, back to genesis.
`IAuditSigner.VerifyAsync` re-derives the whole chain, recomputes every hash and signature, and
reports both whether the target record's signature is valid and where — if anywhere — the chain
first breaks.

**Verification recomputes; it does not re-hash the stored bytes.** `AuditChainVerifier` rehydrates
each record and re-serializes it through `CanonicalProfile`. That is why this package is one of
the two hardest constraints on canonical serialization in the platform: a change to the profile,
a renamed wire property, a reordered member, or a different culture would all produce different
bytes for the same record and break every stored signature.

## A schema change must never look like tampering

`AuditRecordSchemaGuard` refuses to return a record whose `schema_version` major differs from the
one this build reads (currently `2.0`), throwing `ContractViolationException` — a permanent
failure, never retried — that names the version it found.

This is deliberate and it is the substance of **ADR-PA4**. Because verification recomputes bytes,
a migration adapter would not rescue a pre-rename record: it would rehydrate as the new shape,
re-serialize differently, and fail its signature. The operator would see a broken hash chain,
which is the signal this system reserves for altered evidence. Refusing to read is the honest
outcome. Minor versions stay readable — omit-null covers a field a build has not heard of; only a
major says the bytes mean something different.

Records written before the artifact-vocabulary rename (`section_key` → `artifact_key`) are not
migrated. Git history holds the 1.0 bytes if they are ever needed.

## Key invariants

- **`audit-records` is append-only.** `CreateAsync` throws if the `{executionId}:audit` document
  already exists. A retried sign is not expected to change a closed execution's record.
- **Partition key is `/engagement_id`**, and the document id is deterministic
  (`{executionId}:audit`) so every read of a known execution is a point read.
- **Bytes are evidential.** Never batch-rewrite a stored document — the signature is over the
  bytes' meaning, and a rewrite is indistinguishable from tampering.
- **An execution id is written, never parsed** (ADR-PA15). `AuditRecord` carries `engagement_id`
  and `workflow_id` as explicit fields; the partition key comes from the typed field, not from
  splitting the composite id. Reading it back out is what produced ADR-PA12's mis-partitioning
  defect.
- **Sandbox runs are marked, not mixed.** `AuditRecord.Sandbox` is `true` for a `SANDBOX-`-prefixed
  engagement and omitted entirely otherwise, so existing golden bytes are unaffected and the
  aggregation layer can filter test runs out of empirical evidence with a one-line predicate.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`. A change to a wire name or property order
here is a break to stored evidence, not a refactor — it ships as `audit!:` with an ADR.
