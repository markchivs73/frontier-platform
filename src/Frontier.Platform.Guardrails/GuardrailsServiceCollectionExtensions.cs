using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// DI registration for the Guardrails library (engineering-standards: each library
/// wires its own internals; only Host calls these extensions).
/// </summary>
public static class GuardrailsServiceCollectionExtensions
{
    /// <summary>
    /// The kill switch read cache doc 07 §5a names ("about 10 s"), for a consumer that binds no value of its own to
    /// <see cref="AddFrontierGuardrails(IServiceCollection, Container, Container, TimeSpan)"/>.
    /// </summary>
    public static readonly TimeSpan DefaultKillSwitchCacheDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers <see cref="IAdmissionController"/>, <see cref="IBudgetLedger"/>,
    /// <see cref="IBudgetHierarchy"/>, and <see cref="IKillSwitch"/> (Phase 1 in-memory PoC).
    /// <see cref="AdmissionController"/> reads the kill switch, then applies the compiled-in
    /// <see cref="Phase1GuardrailPolicyCatalogue"/> (doc 07 §9); <see cref="BudgetLedger"/>
    /// holds the in-process usage map (doc 07 §6 "or in-memory for PoC") and so is
    /// registered as a singleton. <see cref="IBudgetHierarchy"/> enforces hierarchical
    /// budgets (S6.5). The kill switch here is in-process, read uncached: it stops admissions in this
    /// process only. For S6.5a (Cosmos backing), use <see cref="AddFrontierGuardrails(IServiceCollection, Container)"/>;
    /// for a kill switch shared across processes, <see cref="AddFrontierGuardrails(IServiceCollection, Container, Container, TimeSpan)"/>.
    /// </summary>
    public static IServiceCollection AddFrontierGuardrails(this IServiceCollection services) =>
        services
            .AddSingleton<IBudgetLedger, BudgetLedger>()
            .AddInProcessKillSwitch()
            .AddAdmissionAndHierarchy();

    /// <summary>
    /// Cosmos-backed variant (S6.5a): registers <see cref="CosmosBudgetLedger"/> for the
    /// <c>guardrail-ledger</c> container (PK /engagement_id). All other components same as
    /// the in-memory variant, the in-process kill switch included. Switch between this and
    /// <see cref="AddFrontierGuardrails()"/> based on deployment configuration.
    /// </summary>
    public static IServiceCollection AddFrontierGuardrails(this IServiceCollection services, Container guardRailLedgerContainer) =>
        services
            .AddSingleton<IBudgetLedger>(sp => new CosmosBudgetLedger(guardRailLedgerContainer))
            .AddInProcessKillSwitch()
            .AddAdmissionAndHierarchy();

    /// <summary>
    /// Cosmos-backed variant with the shared fleet kill switch (doc 07 §5a, S13.187): <see cref="CosmosBudgetLedger"/>
    /// over <paramref name="guardRailLedgerContainer"/>, and <see cref="IKillSwitch"/> as one <c>kill-switch</c>
    /// document in <paramref name="platformConfigContainer"/>, the consumer's <c>platform-config</c> container
    /// (partition key <c>/config_type</c>). The governed activate and resume routes write it through
    /// <see cref="IKillSwitch"/>, uncached; admission reads it through a cache of <paramref name="killSwitchCacheDuration"/>
    /// (<see cref="DefaultKillSwitchCacheDuration"/> unless the deployment says otherwise), so every worker stops
    /// admitting within that window of a switch thrown anywhere.
    /// </summary>
    public static IServiceCollection AddFrontierGuardrails(this IServiceCollection services, Container guardRailLedgerContainer, Container platformConfigContainer, TimeSpan killSwitchCacheDuration)
    {
        ArgumentNullException.ThrowIfNull(platformConfigContainer);
        return services.AddSharedGuardrails(_ => guardRailLedgerContainer, _ => platformConfigContainer, killSwitchCacheDuration);
    }

    /// <summary>
    /// The same shared variant, composed the way the other platform libraries compose their Cosmos stores: binds and
    /// validates <see cref="CosmosOptions"/> from the <c>Cosmos</c> section of <paramref name="configuration"/>, and
    /// resolves the <c>guardrail-ledger</c> and <c>platform-config</c> containers from the <see cref="CosmosClient"/>
    /// the composition root registers, when the ledger and the kill switch are first resolved. Nothing reaches Cosmos
    /// at registration, so a head composes without a client until it resolves a guardrail.
    /// </summary>
    public static IServiceCollection AddFrontierGuardrails(this IServiceCollection services, IConfiguration configuration, TimeSpan killSwitchCacheDuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<CosmosOptions>()
            .Bind(configuration.GetSection("Cosmos"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services.AddSharedGuardrails(
            sp => GuardrailsContainers.Ledger(sp.GetRequiredService<CosmosClient>(), sp.GetRequiredService<IOptions<CosmosOptions>>()),
            sp => GuardrailsContainers.PlatformConfig(sp.GetRequiredService<CosmosClient>(), sp.GetRequiredService<IOptions<CosmosOptions>>()),
            killSwitchCacheDuration);
    }

    /// <summary>The Cosmos ledger and the shared kill switch over containers resolved when first needed.</summary>
    internal static IServiceCollection AddSharedGuardrails(this IServiceCollection services, Func<IServiceProvider, Container> ledger, Func<IServiceProvider, Container> platformConfig, TimeSpan killSwitchCacheDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(killSwitchCacheDuration, TimeSpan.Zero);

        return services
            .AddSingleton<IBudgetLedger>(sp => new CosmosBudgetLedger(ledger(sp)))
            .AddSingleton<IKillSwitch>(sp => new CosmosKillSwitch(platformConfig(sp), TimeProvider.System))
            .AddKillSwitchGate(killSwitchCacheDuration)
            .AddAdmissionAndHierarchy();
    }

    /// <summary>The in-process <see cref="KillSwitch"/>, read uncached: there is no store to spare.</summary>
    internal static IServiceCollection AddInProcessKillSwitch(this IServiceCollection services) =>
        services
            .AddSingleton<IKillSwitch, KillSwitch>()
            .AddKillSwitchGate(TimeSpan.Zero);

    /// <summary>
    /// Admission's cached read of whichever <see cref="IKillSwitch"/> is registered. The logger is optional, so a
    /// consumer that registers no logging still composes (the warning is then dropped, never the admission).
    /// </summary>
    internal static IServiceCollection AddKillSwitchGate(this IServiceCollection services, TimeSpan cacheDuration) =>
        services.AddSingleton(sp => new KillSwitchGate(
            sp.GetRequiredService<IKillSwitch>(),
            cacheDuration,
            TimeProvider.System,
            sp.GetService<ILogger<KillSwitchGate>>() ?? NullLogger<KillSwitchGate>.Instance));

    /// <summary>The admission controller and the budget hierarchy, over whichever ledger and kill switch are registered.</summary>
    internal static IServiceCollection AddAdmissionAndHierarchy(this IServiceCollection services) =>
        services
            .AddSingleton<IAdmissionController, AdmissionController>()
            .AddSingleton(sp => new BudgetHierarchy(sp.GetRequiredService<IBudgetLedger>(), Phase1GuardrailPolicyCatalogue.Default))
            .AddSingleton<IBudgetHierarchy>(sp => sp.GetRequiredService<BudgetHierarchy>());
}
