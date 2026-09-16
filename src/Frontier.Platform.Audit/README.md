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
export hosted service, and three boot invariants: `SigningKeyCheck`, `SigningProfileCheck` and
`CosmosTopologyCheck`.

Configuration it expects:

| Key | Purpose |
|---|---|
| `Cosmos:Endpoint`, `Cosmos:Key`, `Cosmos:Database` | The account holding `audit-records` |
| `ConnectionStrings:AzureWebJobsStorage` | Blob storage for archival export |
| `AuditSigning:KeyIdentifier` | The Key Vault key records are signed with, e.g. `https://{vault}.vault.azure.net/keys/{name}` (versionless). Unset selects the local HMAC dev key, which `SigningProfileCheck` **refuses outside the local-emulator profile** (ADR-PA33) |

The vault is reached with `DefaultAzureCredential` (ADR-SEC3). No key, secret or connection string
for signing appears in configuration — only the key's location (ADR-SEC5).

**To prove the signing path against a real vault** (on demand; needs an EC P-256 key with `sign`/`verify`
and the `Key Vault Crypto User` role, plus an `az login`):

```bash
AuditSigning__KeyIdentifier="https://{vault}.vault.azure.net/keys/{name}" \
  dotnet test tests/Frontier.Platform.Audit.Tests --filter "Category=RequiresAzureKeyVault"
```

`KeyVaultLiveSigningTests` signs, verifies locally from the public key, resolves the exact key
version, verifies a legacy HMAC record beside an ES256 one in a single chain, and runs the
`SigningKeyCheck` boot probe. It creates and modifies nothing in the vault, is excluded from both CI
jobs by trait, and skips with a clear message when `AuditSigning:KeyIdentifier` is unset (it also
reads it from user-secrets on that test project).

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
| `IKeyProvider` | Resolves the material that **verifies** a given key version. `KeyVaultKeyProvider` returns ES256 public keys; `DevKeyProvider` serves the local HMAC key |
| `IAuditSigningService` | **Signs** a record hash with the current key version. `KeyVaultAuditSigningService` signs inside Key Vault (sign-only access); `HmacAuditSigningService` is the local profile |
| `ISigningKeyRing` | Chooses both of the above per `SigningKeyPurpose` (execution vs governance) |
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
signature    = ES256(record_hash, Key Vault key version)     # deployed (ADR-PA33)
             = HMAC-SHA256(record_hash, dev key)             # local profile, and every record
                                                             # written before ADR-PA33
```

**`signing_key_id` is the algorithm discriminator.** A key version has exactly one algorithm, so the
record needs no algorithm field and none was added: ids beginning `dev-key/` verify by HMAC, Key
Vault key identifiers verify by ES256. The signed payload is `UTF8(record_hash)` either way, so the
hash chain is algorithm-independent and records written across the change verify side by side with
no re-signing and no schema version bump. Verification is **local under both** — under ES256 it needs
only the version's public key, so an auditor verifies a whole chain with no vault call and no grant.

Each record's `previous_record_hash` is its predecessor's `record_hash`, back to genesis.
`IAuditSigner.VerifyAsync` re-derives the whole chain, recomputes every hash and signature, and
reports both whether the target record's signature is valid and where — if anywhere — the chain
first breaks.

**Verification follows the hash links, not the stored order.** A hash chain's order *is* its append
order, and every record names its predecessor. `closed_at_utc` is a business field that happened to
agree with append order before the concurrency guard existed; under the guard, which close wins the
head race is independent of its timestamp, so an intact chain is routinely stored out of timestamp
order. Walking the stored order would report that as a break. Reads still come back in
`closed_at_utc` order — only verification changed. A stored record the links cannot reach is reported
in `unreachable_records`, its own finding, distinct from a signature mismatch and from a fork.

**The append is guarded** (ADR-PA31). One engagement can run several workflows, so two executions
can close at the same moment. Each engagement therefore has a head document,
`chain-head:{engagementId}`, in the same container and partition as its records, holding the chain's
length and its latest `record_hash`. An append reads the head with its ETag, signs against it, and
writes the record and the new head in **one transactional batch** with If-Match on the head. Losing
that race is not a failure: the signer re-reads, re-hashes, re-signs and tries again, per
`ExecutionAuditOptions` (section `ExecutionAudit`: `AppendMaxAttempts` 8, `AppendBaseDelayMs` 25,
`AppendMaxDelayMs` 1000). Exhausting the attempts throws `AuditChainAppendException` and the record
is *not* stored — treat the audit as unwritten.

Nothing about the signed record changed for this: the head is separate bookkeeping, `doc_type` sits
on the document wrapper outside the signed `record`, and no schema version moved. Every record
stored before ADR-PA31 verifies exactly as it did.

**An engagement whose chain predates the upgrade has no head.** The first append after upgrading
reads the head, finds none, falls back to the stored chain's tail, chains from it, and creates the
head from it in the same batch — recording how many records already existed as
`unguarded_record_count`. There is no backfill job and nothing stored is rewritten.

**Forks are reported as forks, never as tampering.** If two records claim the same
`previous_record_hash`, `VerificationResult.Forks` names them. A fork lying wholly within the records
that predate the head is `legacy_fork` — the footprint of the pre-ADR-PA31 defect, where two closes
read the same tail; every record in it still verifies against its own key, and none of it was
altered. A fork reaching past that boundary is `guarded_fork`, which the platform cannot produce and
which warrants investigation. Either kind makes `chain_valid` false: the kind explains a finding, it
never clears one. Nothing forked is ever re-signed or rewritten.

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
- **The container holds two document kinds.** Signed records (`doc_type: "record"`, or absent on
  records written before ADR-PA31) and one chain head per engagement (`doc_type: "chain_head"`).
  Every reader filters on that — the chain query, the governance query projection, and the archival
  change feed, which never archives a head because a head legitimately changes and an immutable
  archive is for things that do not.
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
- **Sandbox records expire; real ones never do.** The stored document carries a per-item `ttl`:
  seven days for a `sandbox: true` record (doc 13 §5 — not an evidential record), `-1` for every
  other record. The ttl sits on the document wrapper, outside the signed record, and relies on
  the `audit-records` container's `defaultTtl: -1`.

## Governance audit (ADR-PA30)

Changes made **outside** an execution — an approver role edited or retired, a model-role mapping
approved or rolled back — are recorded through `IGovernanceAuditService`, one signed record per
mutation. The platform stays vocabulary-neutral: `event_type` and `subject_type` are snake_case
strings the consumer owns.

```csharp
var record = await governanceAudit.AppendAsync(new GovernanceAuditEntry
{
    EventType = "approver_role_retired", SubjectType = "approver_role", SubjectId = "finance-lead",
    Actor = principalOid, Reason = request.Reason, OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
    Change = new TypedPayload { SchemaRef = "schemas/approver-role-change/1.0", Payload = diff },
}, ct);
```

- **Fail closed.** `AppendAsync` returns the stored, signed record or throws. An invalid entry (empty
  or `unknown` actor, empty reason, non-snake_case types) is a `ContractViolationException` — never
  retry it. `GovernanceAuditAppendException` means the append did not land: answer 503 and do not
  perform the mutation.
- **Idempotent.** `record_id` is SHA-256 over the entry's JCS bytes, so resending the identical entry
  returns the record already stored.
- **Compensation.** When the mutation fails after its audit landed, append a second entry with
  `compensates_record_id` set to the first record's id. Nothing stored is ever altered.
- **One chain per scope** (`deployment` in phase 1), ordered by `sequence`, never by time. Genesis is
  `SHA-256("governance:" + scope)`. An append reads the head document `chain-head:{scope}`, then in one
  transactional batch creates `{scope}:{sequence:D12}`, a `record-id:{recordId}` marker, and replaces
  the head with If-Match. On 412/409 it re-reads, re-hashes and retries per `GovernanceAuditOptions`
  (section `GovernanceAudit`: `AppendMaxAttempts` 8, `AppendBaseDelayMs` 25, `AppendMaxDelayMs` 1000).
- **Hashing.** `record_hash = SHA-256(JCS(canonical record with record_hash and signature empty))`;
  the signature is ES256 (or HMAC in the local profile), as for the execution chain. The key comes
  from `ISigningKeyRing` for `SigningKeyPurpose.GovernanceAudit` — today the same versioned audit key.
  Unlike the execution chain this one **hashes `signing_key_id`**, so the version is chosen before the
  hash exists and `GovernanceAuditHasher.Attach` refuses a signature made under a different version
  (a rotation racing the append), rather than storing a record whose key id is wrong.
- **Verification** (`VerifyAsync`, or the pure `GovernanceAuditChainVerifier.Verify` over an archive
  copy) never throws for a broken chain; it lists every break with its position and kind
  (`signature_mismatch`, `sequence_gap`, `out_of_order`, `hash_link_break`, `unresolved_key`,
  `scope_mismatch`, `head_mismatch`). An unresolvable key version fails closed.
- **Storage.** Container `governance-audit-records`, partition key `/scope`, `defaultTtl: -1`, checked by
  `CosmosTopologyCheck`. Records (not heads or markers) are archived to Blob
  `governance-audit-records-archive` under the change-feed lease prefix `archival-governance-audit-records`.

## Versioning

Published in lockstep with the rest of the platform under one `FrontierPlatformVersion`. Every
public member is tracked in `PublicAPI.Shipped.txt`. A change to a wire name or property order
here is a break to stored evidence, not a refactor — it ships as `audit!:` with an ADR.
