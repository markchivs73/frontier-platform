namespace Frontier.TestSupport;

/// <summary>
/// Single source of truth for the Cosmos emulator endpoint in integration tests (S9.23,
/// C-13 durable fix). The local stack and CI both run the <c>vnext-preview</c> emulator,
/// which speaks plain HTTP on 8081; <c>COSMOS_EMULATOR_ENDPOINT</c> overrides for
/// non-default ports (e.g. Aspire's persistent container with a remapped dynamic port).
/// Compile-linked into each integration-test project — one place to change the scheme,
/// never 20 hardcoded literals again.
/// </summary>
internal static class EmulatorCosmos
{
    /// <summary>Emulator gateway endpoint; env-overridable, defaults to the vnext-preview HTTP endpoint.</summary>
    internal static string Endpoint =>
        Environment.GetEnvironmentVariable("COSMOS_EMULATOR_ENDPOINT") ?? "http://localhost:8081";

    /// <summary>The well-known Cosmos emulator master key (https://learn.microsoft.com/azure/cosmos-db/emulator).</summary>
    internal const string Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
}
