namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The frozen Phase 1 role catalogue (doc 08 §4 "starting set — deliberately small";
/// C-3: frozen as-is for Stage 4, no new roles). <see cref="Catalogue"/> backs
/// <see cref="IRoleRegistry.GetCatalogueAsync"/> directly — the catalogue is config in
/// the sense that it's the only thing an <c>AgentTaskNode.Role</c> may reference, but its
/// Phase 1 membership is a frozen code constant, not a Cosmos document (only role→model
/// <em>mappings</em> are versioned config, doc 08 §6).
///
/// <see cref="CapabilityRequirements"/> values below are PoC-grade placeholders — doc 08
/// does not specify numeric profiles for Phase 1, and they are refined empirically (doc
/// 08 §7's evaluation loop) once Check/Sense agents (Stage 6) give <c>fast</c> and
/// <c>structured-extraction</c> real usage to measure.
/// </summary>
public static class Phase1RoleCatalogue
{
    /// <summary>The four Phase 1 roles (doc 08 §4 table).</summary>
    public static readonly RoleCatalogue Catalogue = new()
    {
        Roles =
        [
            new RoleDefinition
            {
                RoleId = "deep-reasoning",
                Description = "Scope/Approach/Pricing Act agents, chat designer: highest-capability chain for commercially material output.",
                Stakes = StakesLevel.Material,
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
    /// deferred — see its doc comment). Cost figures for claude-fable-5 follow doc 08 §6's
    /// worked example; claude-opus-4-8's figures are PoC placeholders pending verified
    /// provider pricing.
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
                InputCostPer1kGbp = 0.0300m,
                OutputCostPer1kGbp = 0.1500m,
                CacheReadCostPer1kGbp = 0.0030m,
                ContextWindow = 200_000,
                MaxOutputTokens = 16_000,
            },
            new ModelEntry
            {
                Provider = "anthropic",
                ModelId = "claude-fable-5",
                InputCostPer1kGbp = 0.0180m,
                OutputCostPer1kGbp = 0.0900m,
                CacheReadCostPer1kGbp = 0.0018m,
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
