using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// DI registration for the Guardrails library (engineering-standards: each library
/// wires its own internals; only Host calls these extensions).
/// </summary>
public static class GuardrailsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAdmissionController"/>, <see cref="IBudgetLedger"/>,
    /// <see cref="IBudgetHierarchy"/>, and <see cref="IKillSwitch"/> (Phase 1 in-memory PoC).
    /// <see cref="AdmissionController"/> is stateless over the compiled-in
    /// <see cref="Phase1GuardrailPolicyCatalogue"/> (doc 07 §9); <see cref="BudgetLedger"/>
    /// holds the in-process usage map (doc 07 §6 "or in-memory for PoC") and so is
    /// registered as a singleton. <see cref="IBudgetHierarchy"/> enforces hierarchical
    /// budgets (S6.5). <see cref="IKillSwitch"/> provides platform-wide admission control (S6.5).
    /// For S6.5a (Cosmos backing), use <see cref="AddFrontierGuardrails(IServiceCollection, Container)"/>.
    /// </summary>
    public static IServiceCollection AddFrontierGuardrails(this IServiceCollection services) =>
        services
            .AddSingleton<IAdmissionController, AdmissionController>()
            .AddSingleton<IBudgetLedger, BudgetLedger>()
            .AddSingleton(sp => new BudgetHierarchy(sp.GetRequiredService<IBudgetLedger>(), Phase1GuardrailPolicyCatalogue.Default))
            .AddSingleton<IBudgetHierarchy>(sp => sp.GetRequiredService<BudgetHierarchy>())
            .AddSingleton<IKillSwitch, KillSwitch>();

    /// <summary>
    /// Cosmos-backed variant (S6.5a): registers <see cref="CosmosBudgetLedger"/> for the
    /// <c>guardrail-ledger</c> container (PK /engagementId). All other components same as
    /// the in-memory variant. Switch between this and <see cref="AddFrontierGuardrails()"/>
    /// based on deployment configuration.
    /// </summary>
    public static IServiceCollection AddFrontierGuardrails(this IServiceCollection services, Container guardRailLedgerContainer) =>
        services
            .AddSingleton<IAdmissionController, AdmissionController>()
            .AddSingleton<IBudgetLedger>(sp => new CosmosBudgetLedger(guardRailLedgerContainer))
            .AddSingleton(sp => new BudgetHierarchy(sp.GetRequiredService<IBudgetLedger>(), Phase1GuardrailPolicyCatalogue.Default))
            .AddSingleton<IBudgetHierarchy>(sp => sp.GetRequiredService<BudgetHierarchy>())
            .AddSingleton<IKillSwitch, KillSwitch>();
}
