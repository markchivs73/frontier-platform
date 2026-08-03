// S11.7 (ADR-PA2): the pre-Stage-11 InternalsVisibleTo grants (Serialization/ContextAssembly/
// Audit/Hitl + their tests) were verified unused — no assembly consumes this kernel's
// internals (SmartEnum.DiscoverValues and ContractMigrator.ReadSchemaVersion are covered via
// public surface). No friend grants remain; add one only with a concrete consumer.
