// Engine-neutral contract fixtures for the interpreter's tests.
//
// These were this workload's contracts until E3b step 3. A platform package's tests cannot
// reach for a consumer's vocabulary any more than the package itself can, so the shapes are
// preserved verbatim — every assertion still checks what it checked before — under names that
// belong to nobody in particular.

using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// The engagement's scope (doc 01 §4, C-1: minimal Phase 1 shape frozen for the PoC).
/// Produced by the Scope-generating <see cref="AgentTaskNode"/> in the seed workflow
/// (doc 02 §11) and consumed downstream via typed Data edges (doc 00 §3.3).
/// </summary>
internal sealed record SummaryArtifact : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Short human-readable title for the engagement's scope.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>The engagement's objectives, in priority order.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("objectives")]
    public required IReadOnlyList<string> Objectives { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(Title))
        {
            violations.Add("title must not be empty.");
        }

        if (Objectives.Count == 0)
        {
            violations.Add("objectives must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(SummaryArtifact), violations);
        }
    }
}
/// <summary>
/// The engagement's delivery approach (doc 01 §4, C-1: minimal Phase 1 shape frozen for
/// the PoC). Produced by the Approach-generating <see cref="AgentTaskNode"/>, which
/// consumes <see cref="SummaryArtifact"/> via a Data edge (doc 00 §3.3).
/// </summary>
internal sealed record PlanArtifact : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Narrative description of how the engagement will be delivered.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("strategy")]
    public required string Strategy { get; init; }

    /// <summary>
    /// Rough cost estimate for the approach, in the engagement's billing currency.
    /// Serialized via the canonical profile's default-scale fixed-precision decimal
    /// converter (doc 01 §3.3); a money-specific scale is a Stage 6 hardening concern.
    /// </summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("cost_estimate")]
    public required decimal CostEstimate { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(Strategy))
        {
            violations.Add("strategy must not be empty.");
        }

        if (CostEstimate < 0)
        {
            violations.Add("cost_estimate must not be negative.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(PlanArtifact), violations);
        }
    }
}
/// <summary>
/// The engagement's pricing (doc 01 §4, C-1: minimal Phase 1 shape frozen for the PoC).
/// Produced by the Pricing-generating <see cref="AgentTaskNode"/>, which consumes
/// <see cref="PlanArtifact"/> via a Data edge (doc 00 §3.3).
/// </summary>
internal sealed record RateCardArtifact : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "2.0";

    /// <summary>
    /// Hourly (or per-unit) billing rate per role, in the engagement's billing currency
    /// (S9.29: a list of <see cref="UnitRate"/>, not a role-keyed dictionary — Anthropic's
    /// structured-output schema has no way to express an LLM-chosen dynamic key set;
    /// <c>additionalProperties</c> on an object schema must be literal <c>false</c>, so a
    /// genuinely open-ended map can never be produced by a real model call. Found live
    /// running <c>AuditGateTests</c> — the first gate test to exercise this field against
    /// a live model since it was authored at S4.1 — with
    /// <c>invalid_request_error: "'additionalProperties: object' is not supported"</c>).
    /// </summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("unit_rates")]
    public required IReadOnlyList<UnitRate> UnitRates { get; init; }

    /// <summary>Narrative description of any discount applied to the standard rates.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("discount_terms")]
    public string DiscountTerms { get; init; } = string.Empty;

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (UnitRates.Count == 0)
        {
            violations.Add("unit_rates must not be empty.");
        }

        if (UnitRates.Any(rate => string.IsNullOrWhiteSpace(rate.Role)))
        {
            violations.Add("unit_rates entries must have a non-empty role.");
        }

        if (UnitRates.Any(rate => rate.Rate < 0))
        {
            violations.Add("unit_rates values must not be negative.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(RateCardArtifact), violations);
        }
    }
}

/// <summary>One role's billing rate within a <see cref="RateCardArtifact"/> (S9.29).</summary>
internal sealed record UnitRate
{
    /// <summary>The role or service line this rate applies to, e.g. <c>"architect"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>The billing rate for <see cref="Role"/>, in the engagement's billing currency.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("rate")]
    public required decimal Rate { get; init; }
}
/// <summary>
/// The engagement brief supplied to the Scope-generating <see cref="AgentTaskNode"/> as its
/// validated input contract (doc 02 §11, S4.1/C-2: PoC Gate 3 workflow). Sourced from the
/// dynamic context tier's <c>engagement_brief</c> field (doc 03) so that every node in the
/// graph has a typed, validated input shape (S4.2's input-contract validation step).
/// </summary>
internal sealed record BriefArtifact : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Free-text description of the engagement to be scoped.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("narrative")]
    public required string Narrative { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(Narrative))
        {
            violations.Add("narrative must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(BriefArtifact), violations);
        }
    }
}
/// <summary>
/// A helpdesk ticket fetched from the <c>autotask-demo</c> connector's <c>get_new_ticket</c>
/// tool (S9.25/S9.28, ticket-to-resource-assignment demo scenario). Produced by the
/// <c>fetch_ticket</c> <see cref="AgentTaskNode"/> and consumed downstream via a Data edge
/// (doc 00 §3.3) by the resource-matching node.
/// </summary>
internal sealed record LookupResult : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ticket's stable identifier, e.g. <c>TCK-1001</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>Short summary of the ticket.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Full description of the work needed.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Skills a developer resource needs to resolve this ticket.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("required_skills")]
    public required IReadOnlyList<string> RequiredSkills { get; init; }

    /// <summary>Ticket priority: <c>"low"</c>, <c>"medium"</c>, or <c>"high"</c>.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(TicketId))
        {
            violations.Add("ticket_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            violations.Add("title must not be empty.");
        }

        if (RequiredSkills.Count == 0)
        {
            violations.Add("required_skills must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(LookupResult), violations);
        }
    }
}
/// <summary>
/// The ticket details passed to the resource-matching <see cref="AgentTaskNode"/>
/// (S9.25/S9.28, ticket-to-resource-assignment demo scenario), via a Data edge (doc 00
/// §3.3) from <c>fetch_ticket</c>. Shape mirrors <see cref="LookupResult"/> — the
/// ticket flows through unchanged so the matching agent can reason over its required
/// skills against the <c>teamreview-demo</c> connector's developer resources.
/// </summary>
internal sealed record MatchRequest : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ticket's stable identifier, e.g. <c>TCK-1001</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>Short summary of the ticket.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Full description of the work needed.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Skills a developer resource needs to resolve this ticket.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("required_skills")]
    public required IReadOnlyList<string> RequiredSkills { get; init; }

    /// <summary>Ticket priority: <c>"low"</c>, <c>"medium"</c>, or <c>"high"</c>.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(TicketId))
        {
            violations.Add("ticket_id must not be empty.");
        }

        if (RequiredSkills.Count == 0)
        {
            violations.Add("required_skills must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(MatchRequest), violations);
        }
    }
}
/// <summary>
/// The resource-matching <see cref="AgentTaskNode"/>'s recommendation (S9.25/S9.28,
/// ticket-to-resource-assignment demo scenario): which <c>teamreview-demo</c> developer
/// resource best fits the ticket's required skills, with a confidence <see cref="Score"/>
/// and the agent's <see cref="Rationale"/> for the choice — the evidence S9.28's real
/// execution proof checks for in the signed audit record. Consumed downstream via a Data
/// edge (doc 00 §3.3) by <c>assign_resource</c>.
/// </summary>
internal sealed record ScoredMatch : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ticket this recommendation is for (passed through for downstream nodes).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>The chosen developer resource's stable identifier, e.g. <c>dev-1</c>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("selected_resource_id")]
    public required string SelectedResourceId { get; init; }

    /// <summary>The chosen developer resource's display name.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("resource_name")]
    public required string ResourceName { get; init; }

    /// <summary>Confidence that this resource is the best skill match, in the range 0.0–1.0.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("score")]
    public required decimal Score { get; init; }

    /// <summary>Why this resource was chosen over the alternatives.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(TicketId))
        {
            violations.Add("ticket_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(SelectedResourceId))
        {
            violations.Add("selected_resource_id must not be empty.");
        }

        if (Score is < 0 or > 1)
        {
            violations.Add("score must be between 0.0 and 1.0.");
        }

        if (string.IsNullOrWhiteSpace(Rationale))
        {
            violations.Add("rationale must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(ScoredMatch), violations);
        }
    }
}
/// <summary>
/// The assignment details passed to the closing <see cref="AgentTaskNode"/> (S9.25/S9.28,
/// ticket-to-resource-assignment demo scenario), via a Data edge (doc 00 §3.3) from
/// <c>assign_resource</c> — enough for the agent to call the <c>autotask-demo</c>
/// connector's <c>update_ticket</c> tool and close the loop. Shape mirrors
/// <see cref="AssignmentResult"/> — a Data edge's payload deserializes as the
/// downstream node's declared input contract, so the two shapes must agree field for
/// field (the same mirroring <see cref="MatchRequest"/> has with
/// <see cref="LookupResult"/>; found live in S9.28 when the original shape declared
/// fields the upstream payload doesn't carry and every <c>update_ticket</c> run failed
/// deserialization).
/// </summary>
internal sealed record UpdateRequest : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ticket id to update, e.g. <c>TCK-1001</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>The developer resource id now assigned to the ticket.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("resource_id")]
    public required string ResourceId { get; init; }

    /// <summary>Why this resource was assigned (carried through from the matching step for the audit trail).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(TicketId))
        {
            violations.Add("ticket_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(ResourceId))
        {
            violations.Add("resource_id must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(UpdateRequest), violations);
        }
    }
}
/// <summary>
/// The updated helpdesk ticket from the <c>autotask-demo</c> connector's
/// <c>update_ticket</c> tool (S9.25/S9.28, ticket-to-resource-assignment demo scenario).
/// Produced by the <c>update_ticket</c> <see cref="AgentTaskNode"/> — the workflow's final
/// node, closing the ticket-to-resource-assignment loop.
/// </summary>
internal sealed record UpdateResult : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ticket's stable identifier.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>The ticket's status after the update.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>The developer resource id assigned to this ticket.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("assigned_resource_id")]
    public required string AssignedResourceId { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(TicketId))
        {
            violations.Add("ticket_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(Status))
        {
            violations.Add("status must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(AssignedResourceId))
        {
            violations.Add("assigned_resource_id must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(UpdateResult), violations);
        }
    }
}
/// <summary>
/// The matched resource passed to the assignment <see cref="AgentTaskNode"/> (S9.25/S9.28,
/// ticket-to-resource-assignment demo scenario), via a Data edge (doc 00 §3.3) from
/// <c>match_developer</c> — enough for the agent to call the <c>teamreview-demo</c>
/// connector's <c>assign_resource_to_booking</c> tool.
/// </summary>
internal sealed record AssignmentRequest : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ticket this assignment is for.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>The developer resource id to assign, e.g. <c>dev-1</c>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("selected_resource_id")]
    public required string SelectedResourceId { get; init; }

    /// <summary>Why this resource was chosen — passed through to the booking tool call.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(TicketId))
        {
            violations.Add("ticket_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(SelectedResourceId))
        {
            violations.Add("selected_resource_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(Rationale))
        {
            violations.Add("rationale must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(AssignmentRequest), violations);
        }
    }
}
/// <summary>
/// The recorded booking from the <c>teamreview-demo</c> connector's
/// <c>assign_resource_to_booking</c> tool (S9.25/S9.28, ticket-to-resource-assignment demo
/// scenario). Produced by the <c>assign_resource</c> <see cref="AgentTaskNode"/> and
/// consumed downstream via a Data edge (doc 00 §3.3) by <c>update_ticket</c>.
/// </summary>
internal sealed record AssignmentResult : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ticket this resource was assigned to.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("ticket_id")]
    public required string TicketId { get; init; }

    /// <summary>The assigned developer resource's id.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("resource_id")]
    public required string ResourceId { get; init; }

    /// <summary>The rationale recorded for this assignment.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (string.IsNullOrWhiteSpace(TicketId))
        {
            violations.Add("ticket_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(ResourceId))
        {
            violations.Add("resource_id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(Rationale))
        {
            violations.Add("rationale must not be empty.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(AssignmentResult), violations);
        }
    }
}

/// <summary>
/// The contract set the interpreter's tests supply, mirroring what a composition root does at
/// runtime (E16 option 2). Discovered from this assembly rather than listed, so a new fixture
/// contract needs no edit here — the same property the consuming repo's equivalent has.
/// </summary>
internal static class TestContractSet
{
    /// <summary>Every concrete <see cref="IVersionedContract"/> declared by this test assembly.</summary>
    internal static IContractTypeSet Instance { get; } = new ContractTypeSet(
        typeof(SummaryArtifact).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IVersionedContract).IsAssignableFrom(t)));
}
