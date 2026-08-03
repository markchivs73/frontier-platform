using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Guardrails;
using Frontier.Platform.Hitl;
using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Observability;
using Frontier.Platform.Resilience;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Consumer smoke test. Compiles and runs against the packed .nupkg files rather than
// project references, so it catches the failures packing alone cannot: a dependency
// missing from a nuspec, a type that is internal but needed, or a registration extension
// that cannot be reached from outside the assembly.
//
// It deliberately does no I/O. Nothing here should need an emulator, a network or a key.

Console.WriteLine("Frontier.Platform consumer smoke test");
Console.WriteLine(new string('-', 38));

// 1. The kernel types are reachable, and the canonical profile actually works.
var engagementId = new EngagementId("eng-smoke-001");
var bytes = CanonicalProfile.SerializeCanonical(engagementId);
var hash = CanonicalProfile.Hash(engagementId);

Console.WriteLine($"  canonical bytes : {System.Text.Encoding.UTF8.GetString(bytes)}");
Console.WriteLine($"  canonical hash  : {hash}");

if (bytes.Length == 0 || string.IsNullOrWhiteSpace(hash))
{
    throw new InvalidOperationException("Canonical serialization produced no output.");
}

// Byte stability is the property everything else depends on — assert it survives packaging.
if (!CanonicalProfile.SerializeCanonical(new EngagementId("eng-smoke-001")).SequenceEqual(bytes))
{
    throw new InvalidOperationException("Canonical serialization is not byte-stable across calls.");
}

Console.WriteLine("  byte stability  : ok");

// 2. Every library's registration extension is reachable and runs. Configuration values are
//    syntactically valid placeholders; nothing connects to anything.
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Cosmos:Endpoint"] = "https://localhost:8081",
        ["Cosmos:Database"] = "frontier-platform-smoke",
        ["Cosmos:Key"] = "c21vaGUtc21va2UtdGVzdC1wbGFjZWhvbGRlci1rZXktdmFsdWU=",
    })
    .Build();

var services = new ServiceCollection();
services.AddFrontierSerialization();
services.AddFrontierResilience();
services.AddFrontierGuardrails();
services.AddFrontierContextAssembly();
services.AddFrontierAudit(configuration);
services.AddFrontierHitl(configuration);
services.AddFrontierModelRoleConfig(configuration);
services.AddFrontierObservability(configuration);

Console.WriteLine($"  registrations   : {services.Count} services across 8 libraries");

if (services.Count == 0)
{
    throw new InvalidOperationException("No services were registered.");
}

// 3. The container builds. This is where a missing transitive dependency surfaces as a
//    TypeLoadException or FileNotFoundException rather than a quiet no-op.
using var provider = services.BuildServiceProvider();
var options = provider.GetRequiredService<System.Text.Json.JsonSerializerOptions>();

Console.WriteLine($"  resolved profile: {options.PropertyNamingPolicy?.GetType().Name ?? "default"}");
Console.WriteLine();
Console.WriteLine("PASS - all nine packages restored, loaded and registered.");
