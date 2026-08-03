using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Observability;

/// <summary>
/// One entry in the metric catalogue (doc 11 §4): name, instrument type, unit, and the
/// closed dimension set (doc 11 §3 — no dimension may be id-shaped / unbounded cardinality).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by Phase1MetricCatalogueTests.")]
public sealed record MetricDefinition(
    string Name,
    MetricInstrumentType InstrumentType,
    string Unit,
    IReadOnlyList<string> Dimensions,
    string Description);
