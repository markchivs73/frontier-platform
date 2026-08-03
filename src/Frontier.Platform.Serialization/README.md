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
