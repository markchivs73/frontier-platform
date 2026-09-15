# Frontier.Platform.Serialization

The canonical JSON profile shared by every Frontier subsystem and by external agent
development. Its output is **byte-stable**: definition hashing, cache-key equality and audit
signing all depend on the same object producing identical bytes across runs, machines and
culture settings.

## Install

```bash
dotnet add package Frontier.Platform.Serialization
```

Requires a GitHub Packages source with a `read:packages` token — see the
[repository README](https://github.com/markchivs73/frontier-platform).

## Use

```csharp
services.AddFrontierSerialization();
```

Only the composition root should call this. It registers the shared `JsonSerializerOptions`
singleton; consumers take it by injection rather than constructing their own.

For direct use outside DI, `CanonicalProfile.Options` exposes the same configured instance.

## What the profile guarantees

- Nulls omitted
- Explicit property order via `[JsonPropertyOrder]`
- `snake_case` wire names
- ISO-8601 UTC dates with millisecond precision
- Decimals as strings at a declared scale (`[DecimalPrecision]`)
- Enums and smart enums as canonical `snake_case` strings
- Invariant culture throughout

## Smart enum support

`SmartEnumJsonConverter<TEnum>` and `SmartEnumJsonConverterFactory` recognise smart enums **by
shape, via reflection** — any type with a public instance `string Name` property and a public
static `TEnum FromName(string)` resolver round-trips as its `Name` string. This is deliberate:
it means the converter never needs a reference to the assembly declaring the enum, so
consumers can define their own smart enums and have them serialize canonically.

`SmartEnum<T>` itself lives in `Frontier.Platform.Abstractions`.

## What else lives here

Two things sit in this package because they are *defined by* the canonical profile rather than
merely using it.

**`ContextPackage`** — the assembled three-tier prompt context (`BaselineTier`, `DynamicTier`,
`RealTimeTier`, `CacheHint`). It is produced by `Frontier.Platform.ContextAssembly`, but its shape
crosses every subsystem boundary and is hashed into cache keys, so the contract belongs with the
profile that fixes its bytes.

**The boot-check seam** — `IStartupCheck` and `StartupCheckResult`. A library registers a check
for the invariant it owns; the host runs every registered check before the process reports ready,
and a failure means the process refuses to start rather than failing on first invocation. Several
libraries use this (`SigningKeyCheck`, `CosmosTopologyCheck`, `RoleCatalogueCheck`,
`TimeoutHierarchyCheck`, `OtelPipelineCheck`), which is why the two tiny types live in the one
package everything already depends on.

This package registers its own: **`CanonicalProfileCheck`** serializes a committed fixture through
the profile and compares the SHA-256 against a known-good constant. A mismatch means the profile —
naming, ordering, omit-null, converters — has drifted from what definition hashing, cache keys and
audit signing were built against. It is the cheapest insurance in the platform, and if it fails,
stored bytes have already changed meaning.

### RFC 8785 canonicalisation and the ADR-E2 envelope (ADR-PA30)

- `JsonCanonicalizer.Canonicalize` returns the RFC 8785 (JCS) bytes of any JSON: members sorted by
  UTF-16 code units, `JSON.stringify` string escapes, ECMAScript number form, no whitespace. ADR-E2
  requires it wherever untyped JSON is hashed, cached or signed. Hash the JCS form of the canonical
  profile's output, never the output alone, when a contract carries a `JsonElement`.
- `TypedPayload` and `PayloadRef` (namespace `Frontier.Platform.Workflow.Model`) are compiled here
  and forwarded from `Frontier.Platform.Workflow.Model`, so governance libraries can carry the
  envelope without depending on the engine.

## Key invariants

- **Wire bytes never change for a style preference.** Renaming a member, reordering
  properties or switching enum casing is a breaking change to every stored document and every
  computed hash, not a refactor.
- This library depends on `Frontier.Platform.Abstractions` and nothing else in the Frontier
  graph. The `Serialization_OnlyReferencesPlatformAbstractions` architecture test enforces it.

## Extending

Additional converters register inside `SerializationServiceCollectionExtensions.CreateOptions()`.
Any change to the profile needs a golden-file test proving the bytes for existing contracts are
unchanged.
