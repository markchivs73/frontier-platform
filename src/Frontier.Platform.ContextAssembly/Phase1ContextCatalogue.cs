using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// The compiled-in Phase 1 context sources (doc 03 §2 baseline/dynamic tiers), frozen to
/// the PoC Gate 3 engagement matching <c>tools/dev-setup/cosmos-init.py</c>'s seed data —
/// same rationale as <c>Phase1RoleCatalogue</c> and <c>Phase1GuardrailPolicyCatalogue</c>:
/// a compiled-in catalogue stands in for the Cosmos-backed stores until a later stage
/// needs more than one engagement. <see cref="Phase1BaselineCatalogueStore"/> and
/// <see cref="Phase1EngagementContextStore"/> serve these values.
/// </summary>
public static class Phase1ContextCatalogue
{
    /// <summary>The single baseline catalogue id every PoC Gate 3 <c>AgentTaskNode.ContextRequest</c> resolves against.</summary>
    public const string BaselineCatalogueId = "firm-standards-v1";

    /// <summary>The engagement seeded by <c>cosmos-init.py</c> and used throughout the S4 gate tests.</summary>
    public const string SeedEngagementId = "ENGAGEMENT-12345";

    /// <summary>The engagement used by S9.28's real end-to-end execution proof (ticket-to-resource-assignment demo scenario).</summary>
    public const string HelpdeskEngagementId = "ENGAGEMENT-HELPDESK-1";

    /// <summary>
    /// The whole baseline catalogue document, keyed by component name. <see cref="Frontier.Platform.Abstractions.ContextRequest.BaselineComponents"/>
    /// names a subset of these keys (S4.1 nodes request <c>["firm-standards", "playbooks"]</c>); the Orchestration
    /// library's <c>ContextContentComposer</c> filters down to the requested keys before assembly.
    /// </summary>
    public static readonly string BaselineCatalogueJson =
        """
        {
          "firm-standards": "Firm standards (PoC Gate 3): write in plain British English, use GBP for all monetary figures, and state every assumption explicitly rather than implying it.",
          "playbooks": "Advisory SOW playbook (PoC Gate 3): a Scope section names objectives the client can verify are met; an Approach section ties its strategy and cost estimate back to those objectives; a Pricing section gives unit rates and discount terms consistent with the Approach's cost estimate."
        }
        """;

    /// <summary>
    /// Dynamic (engagement-specific) context documents, keyed by engagement id. Each value is the
    /// whole dynamic-context document for that engagement; <see cref="Frontier.Platform.Abstractions.ContextRequest.DynamicFields"/>
    /// names the subset of keys a given node requests (only <c>gen-scope</c> requests <c>["engagement_brief"]</c>
    /// per <c>cosmos-init.py</c>'s seed).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> EngagementContextJson = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SeedEngagementId] =
            """
            {
              "engagement_brief": "Design a product scope for a real-estate SaaS tool aimed at small letting agencies. Propose an approach and cost estimate to deliver it. Price the offering with unit rates and discount terms."
            }
            """,
        [HelpdeskEngagementId] =
            """
            {
              "engagement_brief": "Handle helpdesk ticket assignment: fetch the next unassigned ticket, match it to the best-skilled available developer, assign it, and record the assignment."
            }
            """,
    };
}
