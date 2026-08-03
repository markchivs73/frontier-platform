# Frontier.Platform.Serialization

## What it owns

The canonical `JsonSerializerOptions` profile (doc 01 ADR-C1) shared by every
subsystem and external agent. Stage 0 establishes the registration pattern and
smart-enum converter (TD-11); Stage 1 (S1.1) extends `CreateOptions()` with the
full canonical profile (`[JsonPropertyOrder]`, ISO-8601-UTC-ms dates, string
decimals with declared scale, invariant culture).

## Depends on / exposes

- **Zero Frontier dependencies** — enforced by the
  `Serialization_HasNoFrontierDependencies` architecture test
  (`library-boundaries`). Smart enums from
  `Frontier.Reason.Workflow.Abstractions` (or any subsystem) are recognised by
  shape via reflection, not by reference.
- Exposes `AddFrontierSerialization()` — registers the shared
  `JsonSerializerOptions` singleton (Web defaults, omit-null,
  `SmartEnumJsonConverterFactory`). Only the composition root (Host) calls it.
- Exposes `SmartEnumJsonConverter<TEnum>` and `SmartEnumJsonConverterFactory`:
  any type with a public instance `string Name` property and a public static
  `TEnum FromName(string)` resolver — the shape of `SmartEnum<TEnum>` — round-trips
  as its canonical snake_case `Name` string.

## Key invariants

- Never add a reference to `Frontier.Reason.Workflow.Abstractions` or any other
  Frontier project here — the architecture test fails the build if you do.
- Wire bytes for a smart enum must never change for a style preference
  (`canonical-serialization`) — see the golden-file test below.

## How to extend

Additional converters/options for the canonical profile (Stage 1+) register
inside `SerializationServiceCollectionExtensions.CreateOptions()`.

## Testing notes

- `SmartEnumJsonConverterTests` / `SmartEnumJsonConverterFactoryTests` cover the
  round-trip, unknown-name, and shape-validation failure paths using the
  `ExampleStatus` and `TypeWithNameButNoFromName` fixtures.
- `GoldenFileTests` compares `JsonSerializer.SerializeToUtf8Bytes(ExampleStatus.InProgress, ...)`
  byte-for-byte against `GoldenFiles/example_status.json` — the byte-stability
  pattern Stage 1 reuses for the full contract set.
