namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The Phase 1 role set (doc 08 §4 "starting set — deliberately small"; C-3). Since v0.45.0
/// <see cref="Catalogue"/> is <b>not</b> the catalogue: <see cref="IRoleRegistry.GetCatalogueAsync"/>
/// reads stored definitions (ADR-PA40). It is kept as the migration source — the consumer's seed
/// writes these four roles as model-backed v1 definitions and must equal them field for field.
/// <see cref="DeepReasoningMappingV1"/> remains the source the seeded v1 mapping must match.
///
/// <see cref="CapabilityRequirements"/> values below are PoC-grade placeholders — doc 08
/// does not specify numeric profiles for Phase 1, and they are refined empirically (doc
/// 08 §7's evaluation loop) once Check/Sense agents (Stage 6) give <c>fast</c> and
/// <c>structured-extraction</c> real usage to measure.
/// </summary>
public static class Phase1RoleCatalogue
{
    /// <summary>The four Phase 1 roles (doc 08 §4 table), model-backed and unversioned: the migration source, not the catalogue.</summary>
    [Obsolete("Deprecated in v0.45.0: no longer the role catalogue (ADR-PA40); IRoleRegistry.GetCatalogueAsync reads stored definitions. Kept only as the source the v1 definition migration must equal.")]
    public static readonly RoleCatalogue Catalogue = new()
    {
        Roles =
        [
            new RoleDefinition
            {
                RoleId = "deep-reasoning",
                Description = "Scope/Approach/Pricing Act agents, chat designer: highest-capability chain for commercially material output.",
                Stakes = StakesLevel.Material,
                Backing = RoleBacking.Model,
                Requirements = new CapabilityRequirements
                {
                    MinContextWindow = 200_000,
                    NeedsToolUse = true,
                    NeedsStructuredOutput = true,
                    MaxLatencyMs = 120_000,
                },
            },
            new RoleDefinition
            {
                RoleId = "fast",
                Description = "Check agents, cascade-impact summaries, routing: latency-sensitive and cheap, validators run often.",
                Stakes = StakesLevel.Standard,
                Backing = RoleBacking.Model,
                Requirements = new CapabilityRequirements
                {
                    MinContextWindow = 32_000,
                    NeedsToolUse = false,
                    NeedsStructuredOutput = true,
                    MaxLatencyMs = 5_000,
                },
            },
            new RoleDefinition
            {
                RoleId = "structured-extraction",
                Description = "Sense agents, MCP-result normalisation: strict structured output reliability.",
                Stakes = StakesLevel.Standard,
                Backing = RoleBacking.Model,
                Requirements = new CapabilityRequirements
                {
                    MinContextWindow = 32_000,
                    NeedsToolUse = false,
                    NeedsStructuredOutput = true,
                    MaxLatencyMs = 15_000,
                },
            },
            new RoleDefinition
            {
                RoleId = "embeddings",
                Description = "Know-layer retrieval (Phase 1 minimal).",
                Stakes = StakesLevel.Mechanical,
                Backing = RoleBacking.Model,
                Requirements = new CapabilityRequirements
                {
                    MinContextWindow = 8_000,
                    NeedsToolUse = false,
                    NeedsStructuredOutput = false,
                    MaxLatencyMs = 2_000,
                },
            },
        ],
    };

    /// <summary>
    /// The frozen v1 <c>deep-reasoning</c> mapping (C-3, amended during S4.8/QG-4): chain =
    /// [claude-opus-4-8 primary, claude-fable-5 fallback], provider <c>"anthropic"</c>.
    /// Seeded into the <c>model-role-config</c> container by
    /// <c>tools/dev-setup/cosmos-init.py</c> — this constant is the source of truth the
    /// seed JSON must match byte-for-value. Originally ordered claude-fable-5 primary
    /// (doc 08 §6's worked example); reordered after QG-4 gate testing found
    /// claude-fable-5 returns <c>not_found_error</c> on this account's API key, and
    /// <see cref="ModelResolver"/> always serves chain entry 0 (fallback-chain walking is
    /// deferred — see its doc comment). Cost figures are Anthropic's first-party list prices in
    /// USD per 1,000 tokens (pricing table cached 2026-06-24: claude-opus-4-8 $5/$25 and
    /// claude-fable-5 $10/$50 per million input/output tokens), with cache reads at 0.1× base
    /// input per Anthropic's prompt-caching documentation (ADR-PA21). They replaced GBP
    /// placeholders roughly six times list.
    /// </summary>
    public static readonly RoleMapping DeepReasoningMappingV1 = new()
    {
        RoleId = "deep-reasoning",
        MappingVersion = 1,
        Chain =
        [
            new ModelEntry
            {
                Provider = "anthropic",
                ModelId = "claude-opus-4-8",
                InputCostPer1k = 0.0050m,
                OutputCostPer1k = 0.0250m,
                CacheReadCostPer1k = 0.0005m,
                Currency = "USD",
                ContextWindow = 200_000,
                MaxOutputTokens = 16_000,
            },
            new ModelEntry
            {
                Provider = "anthropic",
                ModelId = "claude-fable-5",
                InputCostPer1k = 0.0100m,
                OutputCostPer1k = 0.0500m,
                CacheReadCostPer1k = 0.0010m,
                Currency = "USD",
                ContextWindow = 200_000,
                MaxOutputTokens = 16_000,
            },
        ],
        Ring = RolloutRing.Fleet,
        CanaryPercent = 0,
        ChangeReason = "S4.8 QG-4 amendment: claude-fable-5 returns not_found_error on this account's API key; reordered claude-opus-4-8 to entry 0 (ModelResolver always serves entry 0 — chain-fallback walking is deferred) so the gate tests exercise a real, callable model.",
        ApprovedBy = "user:mark",
        EffectiveFromUtc = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc),
        EvaluationEvidenceRef = null,
    };
}
