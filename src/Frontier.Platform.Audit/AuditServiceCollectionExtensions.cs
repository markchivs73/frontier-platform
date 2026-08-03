using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frontier.Platform.Audit;

/// <summary>
/// Composition-root registration for <c>Frontier.Platform.Audit</c> (doc 00 §9:
/// every library exposes <c>AddFrontierXxx()</c>; only Host calls it). Registers the
/// local-dev <see cref="IKeyProvider"/> seam — Stage 5 swaps in a Key Vault-backed
/// implementation here without changing any consumer — the <see cref="SigningKeyCheck"/>
/// boot invariant (doc 12 §6), the <see cref="IAuditTelemetryStaging"/> store (doc 05
/// §9, C-14), the
/// (doc 05 §4, S5.4), the <see cref="IAuditRecordStore"/> and <see cref="IAuditSigner"/>
/// (doc 05 §5-6, S5.5), the <see cref="IAuditQueryService"/> (doc 05 §7, S5.7), the
/// <see cref="IAuditRecordExporter"/> and <see cref="ArchivalAuditExportHostedService"/>
/// (doc 05 §8, S6.3 archival to Blob), plus their shared <see cref="CosmosTopologyCheck"/>
/// boot invariant.
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="CosmosOptions"/> (doc 12 §4 "options with teeth"),
    /// and registers the <see cref="CosmosClient"/>, <see cref="BlobServiceClient"/>,
    /// <see cref="IAuditTelemetryStaging"/>, <see cref="IAuditRecordStore"/>,
    /// <see cref="IAuditSigner"/>, <see cref="IAuditQueryService"/>, archival exporter,
    /// and hosted service alongside the signing-key seam and boot invariants.
    /// </summary>
    public static IServiceCollection AddFrontierAudit(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CosmosOptions>()
            .Bind(configuration.GetSection("Cosmos"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services
            .AddSingleton<IKeyProvider, DevKeyProvider>()
            .AddSingleton<IStartupCheck, SigningKeyCheck>()
            .AddSingleton(CreateCosmosClient)
            .AddSingleton<IAuditTelemetryStaging, CosmosAuditTelemetryStaging>()
            .AddSingleton<IAuditRecordStore, CosmosAuditRecordStore>()
            .AddSingleton<IAuditSigner, AuditSigner>()
            .AddSingleton<IAuditQueryService, AuditQueryService>()
            .AddSingleton<IAuditRecordExporter, BlobAuditRecordExporter>()
            .AddSingleton(CreateBlobServiceClient)
            .AddHostedService<ArchivalAuditExportHostedService>()
            .AddSingleton<IStartupCheck, CosmosTopologyCheck>();
    }

    /// <summary>
    /// Builds the <see cref="CosmosClient"/> wired to the shared <see cref="CanonicalProfile"/>
    /// (canonical-serialization: no other <c>JsonSerializerOptions</c> instance may be
    /// constructed anywhere). <see cref="ConnectionMode.Gateway"/> matches every other
    /// Cosmos client in this codebase and is required by the Linux Cosmos DB emulator,
    /// which does not expose the Direct/TCP port range.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "SDK factory method; exercised by integration tests against the Cosmos emulator (S0.5/CI integration job), not the unit-coverage gate.")]
    internal static CosmosClient CreateCosmosClient(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<CosmosOptions>>().Value;
        var clientOptions = new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = CanonicalProfile.Options,
            ConnectionMode = ConnectionMode.Gateway,
            // The Cosmos emulator uses a self-signed TLS cert that is not in the OS trust
            // store. Bypass validation when connecting to localhost (emulator only).
            HttpClientFactory = IsLocalEmulator(options.Endpoint)
                ? () => new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true, ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator })
                : null
        };

        return new CosmosClient(options.Endpoint, options.Key, clientOptions);
    }

    /// <summary>
    /// Builds the <see cref="BlobServiceClient"/> for audit record archival to immutable
    /// Blob storage (doc 05 §8: compliance archives, SIEM export, tampering-detection
    /// comparison copies). Uses the storage endpoint and key from configuration
    /// (typically the same Azurite instance as other subsystems in local dev).
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "SDK factory method (doc 05 §8); exercised by the Audit archival integration test against Azurite (S0.5/CI integration job), not the unit-coverage gate.")]
    internal static BlobServiceClient CreateBlobServiceClient(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("Configuration 'ConnectionStrings:AzureWebJobsStorage' is required for audit record archival.");

        // S9.24: the SDK defaults to its newest known service version (e.g. 2026-06-06),
        // which real Azure supports but the Azurite emulator (CI + local dev) typically
        // lags behind by many releases — pin to a long-established version both sides
        // are guaranteed to understand rather than chasing whichever Azurite tag happens
        // to be current.
        var options = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2025_01_05);
        return new BlobServiceClient(connectionString, options);
    }

    private static bool IsLocalEmulator(string endpoint) =>
        endpoint.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
