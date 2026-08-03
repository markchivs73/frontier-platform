---
name: cosmos-conventions
description: Cosmos DB container conventions, partition keys, id patterns, query and TTL/archival discipline for the platform stores. Use when touching containers, repositories, or Cosmos queries in this repo.
---

# Cosmos conventions

**This repo owns the conventions; each consuming solution owns its own container inventory.**
Cardinal rule (ADR-S2/ADR-4): **Cosmos holds projections and evidential records — never
orchestration truth.** Where a consumer runs a durable execution engine, its history wins every
disagreement; projections are rebuildable.

## Platform containers

| Container | PK | id pattern | Writes | Owner |
|---|---|---|---|---|
| `approvals` | `/engagementId` | `{requestId}` | insert + single ETag-guarded decision update | Hitl |
| `audit-records` | `/engagementId` | `{executionId}:audit` | append-only, once at close | Audit |
| `audit-telemetry-staging` | `/engagementId` | per-invocation | append-only, 30-day TTL | Audit |
| engagement context | `/engagementId` | per-engagement | upsert, content-hashed with epoch bump | ContextAssembly |
| model role config | config-scoped | per-role | mutable with governance | ModelRoleConfig |
| budget ledger | `/engagementId` | per-scope | append-only | Guardrails |

**PK is `/engagementId` everywhere except config stores (ADR-S1).** A new container with a
different PK needs an explicit recorded reason in `docs/DECISIONS.md`.

Containers named in a topology check but not listed above (`execution-snapshots`, for example)
are **ports** — owned by the consuming solution, verified here only for presence. Do not add
write paths to them.

## Query discipline

- Hot queries are single-partition by design. A new cross-partition query in a hot path is a
  design smell — check whether the audit trail should answer it instead.
- Decision and rollback paths use point-reads plus ETag guards (Session consistency default).
- Readers may see stale projections. Surface the checkpoint timestamp; **never hide staleness**.
- Indexing: exclude large payload paths; check the composite-index list before adding a new
  query pattern.

## TTL & archival

Archival rides the **change feed** to Blob (cool tier) — never scan-and-copy, and TTL deletion
must never race the archive. Audit and approvals: 7 years in Cosmos with periodic immutable
export. **No batch rewrites of stored documents, ever** — stored bytes are evidential.

## Testing

Integration tests hit the **emulator, never SDK mocks** — the storage shape is part of what is
under test, and contract round-trip tests double as storage-shape tests.

Every integration fixture in this repo **provisions and tears down its own database**
(`CreateDatabaseIfNotExistsAsync` in `InitializeAsync`, delete in `DisposeAsync`). This is why
CI needs only a bare emulator with no provisioning script. Keep new fixtures self-provisioning:
a fixture that depends on externally-created containers couples this repo's test suite to a
consumer's setup.

Endpoint comes from `EmulatorCosmos.Endpoint` (`tests/Shared`), which reads
`COSMOS_EMULATOR_ENDPOINT` and defaults to `http://localhost:8081`. Never hardcode it.
