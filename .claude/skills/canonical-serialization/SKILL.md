---
name: canonical-serialization
description: The canonical JSON profile, contract authoring, converter authoring, hashing, versioning/migration rules. Use when adding or changing any contract type, converter, or migration in this repo.
---

# Canonical serialization & contracts

**This repo owns the profile.** `Frontier.Platform.Serialization` defines it and every
consumer inherits it — so a change here changes stored bytes and computed hashes in every
solution that consumes the package. Treat a serialization "detail" as a correctness bug.

Three features depend on byte-identical output: definition-hash identity, provider cache hits,
and audit signing.

## The profile (one shared `JsonSerializerOptions`, no per-call options ever)

- **Omit null** — never emit nulls (ADR-C1)
- `[JsonPropertyOrder]` on **every** property of **every** contract — explicit, gapless from 0,
  never reordered after a version ships
- snake_case wire names; ISO-8601 UTC with milliseconds for dates; decimals as strings with
  declared scale; enums as strings; invariant culture
- Contract types are `sealed record` POCOs in `Frontier.Platform.Abstractions` (zero
  dependencies). Converters live in `Frontier.Platform.Serialization`. **Never put
  serialization logic in Abstractions.**

## Authoring a new contract

1. Implement `IVersionedContract` with `SchemaVersion` (property order 0) and `Validate()`.
2. Every property gets an explicit `[JsonPropertyOrder]`.
3. Add a round-trip test, a **byte-stability test** (same object → identical bytes across runs
   and cultures), and a committed golden-file fixture of the canonical bytes.
4. Golden files are append-only history. A changed golden file on an existing version means you
   broke compatibility — stop.

## Authoring a converter

Converters recognise types **structurally, by reflection** — never by referencing the assembly
that declares them. `SmartEnumJsonConverterFactory` is the pattern: it matches any type with a
public instance `string Name` and a public static `FromName(string)`, so a consumer can define
its own smart enums and have them serialize canonically without this package knowing about them.

Keep that property. A converter that needs a reference to a consumer's assembly is a
severability violation.

## Hashing

`DefinitionHash` is the hash of an object's canonical bytes **excluding the hash field itself**.
Audit signing (HMAC-SHA256, per-engagement hash chains) consumes the same canonical bytes. Any
helper producing "bytes for signing or hashing" must route through the shared profile.

## Versioning & migration

- **Minor change** (additive, has a default): same major, deserializes with defaults, no adapter.
- **Major change:** bump the version, write a migration adapter, register it in
  `ContractMigrator`. Old shapes rehydrate via `Rehydrate<T>` at **read time**, lazily.
- `ContractMigrator` keys on the **stored `schema_version` string**, never on a CLR type name.
  This is why types can move between assemblies — as they did during the extraction — without
  touching a single stored document.
- **Never batch-rewrite stored documents.** Stored bytes are evidential. The migrated shape
  exists in memory and in new writes only.

## Smells to reject in review

- A `JsonSerializerOptions` constructed anywhere outside the Serialization project
- A contract property without `[JsonPropertyOrder]`
- `JsonIgnoreCondition` or naming-policy overrides on individual types
- Reordering or renumbering properties on a shipped version
- Hashing or signing code that serializes "manually"
- A converter that matches by type reference rather than by shape
