using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Extensions.AI;
using Frontier.Platform.Workflow.Model;


namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Chat designer service: manages persistent design conversation over drafts.
/// Doc 14 §1–2: fetch draft, submit message to agent, record turn, optionally merge proposal.
/// Phase 1: real LLM call via IChatClient; the model is resolved through the
/// <c>deep-reasoning</c> role with adaptive thinking (doc 14 §3, S9.9a) via
/// <see cref="IDesignerModelProvider"/>. Phase 2 will replace this with MAF agents.
/// </summary>
internal sealed class ChatDesignerService : IChatDesignerService
{
    private readonly IDefinitionStore _store;
    private readonly IProposalMergeService _mergeService;
    private readonly IChatClient _chatClient;
    private readonly IWorkflowSchemaProvider _schemaProvider;
    private readonly IApproverRoleCatalog _roleCatalog;
    private readonly IDesignerToolCatalog _toolCatalog;
    private readonly IAgentRoleCatalog _agentRoleCatalog;
    private readonly INodeDiffService _diffService;
    private readonly IDefinitionCompiler _compiler;
    private readonly IDesignerModelProvider _modelProvider;
    private readonly IInstructionCatalog _instructionCatalog;
    private readonly IContextComponentCatalog _componentCatalog;
    private readonly IEntryContractCatalog _entryContractCatalog;

    public ChatDesignerService(
        IDefinitionStore store,
        IProposalMergeService mergeService,
        IChatClient chatClient,
        IWorkflowSchemaProvider schemaProvider,
        IApproverRoleCatalog roleCatalog,
        IDesignerToolCatalog toolCatalog,
        IAgentRoleCatalog agentRoleCatalog,
        INodeDiffService diffService,
        IDefinitionCompiler compiler,
        IDesignerModelProvider modelProvider,
        IInstructionCatalog instructionCatalog,
        IContextComponentCatalog componentCatalog,
        IEntryContractCatalog entryContractCatalog)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mergeService);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(roleCatalog);
        ArgumentNullException.ThrowIfNull(toolCatalog);
        ArgumentNullException.ThrowIfNull(agentRoleCatalog);
        ArgumentNullException.ThrowIfNull(diffService);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(modelProvider);
        ArgumentNullException.ThrowIfNull(instructionCatalog);
        ArgumentNullException.ThrowIfNull(componentCatalog);
        ArgumentNullException.ThrowIfNull(entryContractCatalog);
        _store = store;
        _mergeService = mergeService;
        _chatClient = chatClient;
        _schemaProvider = schemaProvider;
        _roleCatalog = roleCatalog;
        _toolCatalog = toolCatalog;
        _agentRoleCatalog = agentRoleCatalog;
        _diffService = diffService;
        _compiler = compiler;
        _modelProvider = modelProvider;
        _instructionCatalog = instructionCatalog;
        _componentCatalog = componentCatalog;
        _entryContractCatalog = entryContractCatalog;
    }

    public async Task<DesignTurnDocument> SubmitDesignTurnAsync(
        string workflowId,
        DesignTurnRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(workflowId);
        ArgumentNullException.ThrowIfNull(request);

        // Fetch current draft
        var draft = await _store.GetDraftAsync(workflowId, ct);
        if (draft == null)
            throw new InvalidOperationException($"No draft found for workflow {workflowId}");

        // Get or create chat history
        var history = await _store.GetChatHistoryAsync(workflowId, ct)
                      ?? new ChatHistoryDocument
                      {
                          Id = $"{workflowId}:chat-history",
                          WorkflowId = workflowId,
                          DraftId = $"{workflowId}:draft",
                          NextTurnNumber = 1,
                          LastMessageAtUtc = DateTime.UtcNow,
                          TurnDocumentIds = new List<string>().AsReadOnly()
                      };

        var turnNumber = history.NextTurnNumber;
        var turnId = $"{workflowId}:turn:{turnNumber}";

        // S9.33 (doc 14 §8a): validate every mention against the live discovery catalogues —
        // never trust the client-asserted fact, matching this doc's existing posture on draft
        // revisions/proposal state (§4/§9). Only resolved mentions reach the agent's prompt.
        var validatedMentions = await ValidateMentionsAsync(request.Mentions, ct);

        // Invoke LLM with current draft context and conversation history.
        // Phase 2 will replace this with a MAF agent invocation.
        var agentResult = await InvokeDesignerAgentAsync(draft, history, request.Message, validatedMentions, ct);
        // S9.73 (doc 14 §4.3): one automatic repair pass — if the proposal has resourced-tier Error
        // findings (contract/graph), re-prompt the agent once with those findings + its prior proposal
        // so it can self-correct. The deterministic validator is the authority; the retry never loops.
        agentResult = await RepairProposalOnceAsync(draft, history, request.Message, validatedMentions, agentResult, ct);
        var agentReasoning = agentResult.Reasoning;
        var agentProposalJson = agentResult.ProposalDefinitionJson;

        // Compute the authoritative diff + pure-tier validation over the proposal (doc 14 §4.1).
        var (proposalChanges, blockReason) = BuildProposalReview(draft, agentProposalJson);

        // Create turn document
        var turn = new DesignTurnDocument
        {
            Id = turnId,
            WorkflowId = workflowId,
            DraftId = $"{workflowId}:draft",
            TurnNumber = turnNumber,
            DesignerId = request.DesignerId,
            CreatedAtUtc = DateTime.UtcNow,
            DesignerMessage = request.Message,
            DraftRevisionAtTurn = draft.DraftRevision,
            AgentProposalJson = agentProposalJson,
            ProposalReasoningJson = agentReasoning,
            ProposalChanges = proposalChanges,
            ProposalBlockReason = blockReason,
            MergeOutcome = null,
            ConflictSummary = null,
            Mentions = validatedMentions.Count > 0 ? validatedMentions : null
        };

        // Persist turn
        var persistedTurn = await _store.PersistDesignTurnAsync(turn, ct);

        // Optionally merge proposal (if provided and no errors)
        if (request.AutoMergeProposal && !string.IsNullOrEmpty(agentProposalJson))
        {
            var mergeOutcome = await _mergeService.ApplyProposalAsync(
                workflowId,
                agentProposalJson,
                agentReasoning,
                request.DesignerId,
                ct);

            // Record merge outcome in turn
            var outcomeStr = mergeOutcome switch
            {
                ProposalMergeOutcomeMerged => "merged",
                ProposalMergeOutcomeConflict conflict => $"conflict:{conflict.Conflicts.Count}",
                ProposalMergeOutcomeValidationBlocked blocked => $"blocked:{blocked.BlockingFindings.Count}",
                _ => "unknown"
            };

            var conflictSummary = mergeOutcome switch
            {
                ProposalMergeOutcomeConflict conflict => string.Join("; ", conflict.Conflicts.Select(c => $"{c.NodeId} (designer vs. agent)")),
                ProposalMergeOutcomeValidationBlocked blocked => string.Join("; ", blocked.BlockingFindings.Select(f => $"{f.RuleId}: {f.Message}")),
                _ => null
            };

            persistedTurn = persistedTurn with
            {
                MergeOutcome = outcomeStr,
                ConflictSummary = conflictSummary
            };

            persistedTurn = await _store.PersistDesignTurnAsync(persistedTurn, ct);
        }

        // Update chat history
        var newTurnIds = new List<string>(history.TurnDocumentIds) { turnId };
        var updatedHistory = history with
        {
            NextTurnNumber = turnNumber + 1,
            LastMessageAtUtc = DateTime.UtcNow,
            TurnDocumentIds = newTurnIds.AsReadOnly()
        };
        await _store.CreateOrUpdateChatHistoryAsync(updatedHistory, ct);

        return persistedTurn;
    }

    public async Task<ChatHistoryData?> GetHistoryAsync(
        string workflowId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(workflowId);

        var history = await _store.GetChatHistoryAsync(workflowId, ct);
        if (history == null)
            return null;

        var turns = await _store.GetAllDesignTurnsAsync(workflowId, ct);

        return new ChatHistoryData
        {
            WorkflowId = workflowId,
            DraftId = history.DraftId,
            TotalTurns = turns.Count,
            LastMessageAtUtc = history.LastMessageAtUtc,
            Turns = turns
        };
    }

    public async Task<IReadOnlyList<DesignTurnDocument>> GetAllTurnsAsync(
        string workflowId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(workflowId);
        return await _store.GetAllDesignTurnsAsync(workflowId, ct);
    }

    /// <inheritdoc />
    public async Task EnsureWelcomeTurnAsync(string workflowId, string engagementType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(workflowId);

        // Idempotent: a history already exists once any turn (incl. this welcome turn) is written.
        if (await _store.GetChatHistoryAsync(workflowId, ct) is not null) return;

        // S9.32 (doc 14 §2, doc 19 §A3-R6): the welcome turn summarises the capability
        // catalogue — the same three catalogs the system prompt and the A3-R8 panel use,
        // so the designer's and the agent's vocabulary are visibly the same from turn zero.
        var agentRoles = await _agentRoleCatalog.GetAgentRolesAsync(ct);
        var tools = await _toolCatalog.GetToolsAsync(ct);
        var approverRoles = await _roleCatalog.GetApproverRolesAsync(ct);

        var turnId = $"{workflowId}:turn:0";
        var welcome = new DesignTurnDocument
        {
            Id = turnId,
            WorkflowId = workflowId,
            DraftId = $"{workflowId}:draft",
            TurnNumber = 0,
            DesignerId = "system",
            CreatedAtUtc = DateTime.UtcNow,
            DesignerMessage = WelcomeMessage(engagementType, agentRoles, tools, approverRoles),
            DraftRevisionAtTurn = string.Empty,
            AgentProposalJson = null,
            ProposalReasoningJson = null,
            ProposalChanges = null,
            ProposalBlockReason = null,
            MergeOutcome = null,
            ConflictSummary = null,
        };

        await _store.PersistDesignTurnAsync(welcome, ct);
        await _store.CreateOrUpdateChatHistoryAsync(
            new ChatHistoryDocument
            {
                Id = $"{workflowId}:chat-history",
                WorkflowId = workflowId,
                DraftId = $"{workflowId}:draft",
                NextTurnNumber = 1,
                LastMessageAtUtc = DateTime.UtcNow,
                TurnDocumentIds = new List<string> { turnId }.AsReadOnly(),
            },
            ct);
    }

    /// <summary>
    /// Builds the welcome-turn body: the interaction model, the capability-count summary with
    /// detail (S9.32, doc 14 §2 — sourced from the same catalogues as the A3-R8 panel and the
    /// agent's system prompt), plus engagement-type examples.
    /// </summary>
    internal static string WelcomeMessage(
        string engagementType,
        IReadOnlyList<AgentRoleDescriptor> agentRoles,
        IReadOnlyList<DesignerToolDescriptor> tools,
        IReadOnlyList<ApproverRoleDescriptor> approverRoles)
    {
        var serverCount = tools.Select(t => t.Server).Distinct(StringComparer.Ordinal).Count();
        var agentRoleNames = string.Join(", ", agentRoles.Select(r => r.RoleId));
        var toolsByServer = string.Join("; ", tools
            .GroupBy(t => t.Server, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Name))}"));
        var approverRoleNames = string.Join(", ", approverRoles.Select(r => r.RoleId));

        return
            $"""
            Welcome — let's design your '{engagementType}' workflow together.

            I can design with {agentRoles.Count} agent model role(s), {tools.Count} tool(s) across {serverCount} connector(s), and gate on {approverRoles.Count} approver role(s).
            • Agent model roles: {(agentRoleNames.Length > 0 ? agentRoleNames : "none configured")}
            • Tools: {(toolsByServer.Length > 0 ? toolsByServer : "none configured")}
            • Approver roles: {(approverRoleNames.Length > 0 ? approverRoleNames : "none configured")}

            How this works: describe a change in plain language, review the proposed changes I show
            you, approve the ones you want, and they're saved automatically to your draft.

            Try things like:
            • "Add a Business approval gate before the pricing step"
            • "Insert a validation step after scope generation"
            • "Add a parallel branch for the technical and commercial reviews"
            """;
    }

    /// <summary>
    /// Calls the design agent with the schema, available approver roles, engagement type, current
    /// draft, and conversation history (doc 14 §3), then parses the response into a structured
    /// proposal (doc 14 §4). A parsed proposal yields the complete <see cref="WorkflowDefinition"/>
    /// JSON for the server-side diff; an unparseable response degrades to plain reasoning.
    /// </summary>
    internal async Task<DesignerAgentResult> InvokeDesignerAgentAsync(
        DefinitionDraftDocument draft,
        ChatHistoryDocument history,
        string userMessage,
        IReadOnlyList<ValidatedMention> mentions,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, await BuildSystemPromptAsync(draft, ct)) };

        // Include previous turns for conversation continuity (only when history exists).
        if (history.NextTurnNumber > 1)
        {
            var previousTurns = await _store.GetAllDesignTurnsAsync(draft.WorkflowId, ct);
            foreach (var pt in previousTurns.OrderBy(t => t.TurnNumber))
            {
                messages.Add(new ChatMessage(ChatRole.User, pt.DesignerMessage));
                if (!string.IsNullOrEmpty(pt.ProposalReasoningJson))
                    messages.Add(new ChatMessage(ChatRole.Assistant, pt.ProposalReasoningJson));
            }
        }

        // S9.33 (doc 14 §8a): resolved mentions are appended to the prompt content only — the
        // persisted DesignTurnDocument.DesignerMessage stays the designer's exact original text.
        // Explicit user intent this way is a stronger grounding signal than free prose and
        // directly targets the hallucinated-tool/role class the S9.27 walkthrough exposed.
        messages.Add(new ChatMessage(ChatRole.User, WithMentionNote(userMessage, mentions)));

        var options = await BuildChatOptionsAsync(draft.WorkflowId, ct).ConfigureAwait(false);

        // S9.12 is Option A (reveal-on-completion): the UI shows a "designing" indicator and reveals
        // the parsed result via the ChatTurnCompleted SignalR event, so streaming here is a
        // server-side resilience measure (S9.9a, doc 14 §3) — a long proposal arrives as it
        // generates instead of one blocking read holding the whole response in flight — not a
        // user-visible behaviour change.
        var response = await _chatClient.GetStreamingResponseAsync(messages, options, ct)
            .ToChatResponseAsync(ct).ConfigureAwait(false);
        return BuildResult(response.Text ?? string.Empty);
    }

    /// <summary>
    /// S9.9a (doc 14 §3): builds the designer call's <see cref="ChatOptions"/> from the
    /// <see cref="IDesignerModelProvider"/> resolution — model and output ceiling from the
    /// <c>deep-reasoning</c> role's active mapping instead of a hardcoded id, plus the
    /// provider-neutral <see cref="ChatClientOptionKeys.AdaptiveThinking"/> flag the
    /// provider adapter translates into its own thinking request shape.
    /// </summary>
    internal async Task<ChatOptions> BuildChatOptionsAsync(string workflowId, CancellationToken ct)
    {
        var selection = await _modelProvider.GetAsync(workflowId, ct).ConfigureAwait(false);
        var options = new ChatOptions { ModelId = selection.ModelId, MaxOutputTokens = selection.MaxOutputTokens };
        if (selection.AdaptiveThinking)
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary { [ChatClientOptionKeys.AdaptiveThinking] = true };
        }

        return options;
    }

    /// <summary>
    /// Validates each mention against the live discovery catalogue for its kind (doc 14 §8a) —
    /// the same three catalogues <see cref="BuildSystemPromptAsync"/> constrains the agent to,
    /// so a mention can never claim a resource the agent was never allowed to invent either.
    /// </summary>
    internal async Task<IReadOnlyList<ValidatedMention>> ValidateMentionsAsync(
        IReadOnlyList<ResourceMention> mentions,
        CancellationToken ct)
    {
        if (mentions.Count == 0) return [];

        var knownAgentRoles = (await _agentRoleCatalog.GetAgentRolesAsync(ct)).Select(r => r.RoleId).ToHashSet(StringComparer.Ordinal);
        var knownTools = (await _toolCatalog.GetToolsAsync(ct)).Select(t => t.ToolRef).ToHashSet(StringComparer.Ordinal);
        var knownApproverRoles = (await _roleCatalog.GetApproverRolesAsync(ct)).Select(r => r.RoleId).ToHashSet(StringComparer.Ordinal);

        return mentions.Select(m => new ValidatedMention
        {
            Kind = m.Kind,
            Ref = m.Ref,
            Resolved = m.Kind switch
            {
                MentionKind.AgentRole => knownAgentRoles.Contains(m.Ref),
                MentionKind.McpTool => knownTools.Contains(m.Ref),
                MentionKind.ApproverRole => knownApproverRoles.Contains(m.Ref),
                _ => false,
            },
        }).ToList();
    }

    /// <summary>Appends resolved mentions as an explicit-intent note to the prompt content sent to the model.</summary>
    internal static string WithMentionNote(string userMessage, IReadOnlyList<ValidatedMention> mentions)
    {
        var resolved = mentions.Where(m => m.Resolved).ToList();
        if (resolved.Count == 0) return userMessage;

        var note = string.Join('\n', resolved.Select(m => $"- {m.Kind}: {m.Ref}"));
        return $"{userMessage}\n\n[Designer explicitly mentioned these resources:]\n{note}";
    }

    /// <summary>
    /// S9.73 (doc 14 §4.3): one automatic repair pass. Runs the full <b>resourced</b> validator over a
    /// parsed proposal and, if it has Error findings, re-invokes the agent exactly once with those
    /// findings plus its prior proposal appended so it can self-correct. The deterministic validator
    /// stays the authority — no second LLM judges contracts; a single retry never loops.
    /// </summary>
    internal async Task<DesignerAgentResult> RepairProposalOnceAsync(
        DefinitionDraftDocument draft, ChatHistoryDocument history, string userMessage,
        IReadOnlyList<ValidatedMention> mentions, DesignerAgentResult firstAttempt, CancellationToken ct)
    {
        var errors = await ProposalErrorFindingsAsync(draft, firstAttempt, ct);
        if (errors.Count == 0) return firstAttempt;
        var repairMessage = BuildRepairMessage(userMessage, firstAttempt, errors);
        return await InvokeDesignerAgentAsync(draft, history, repairMessage, mentions, ct);
    }

    /// <summary>The proposal's resourced-tier Error findings (empty when there is no parseable proposal).</summary>
    internal async Task<IReadOnlyList<ValidationFinding>> ProposalErrorFindingsAsync(
        DefinitionDraftDocument draft, DesignerAgentResult result, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(result.ProposalDefinitionJson)) return [];
        var proposed = JsonSerializer.Deserialize<WorkflowDefinition>(result.ProposalDefinitionJson, CanonicalProfile.Options);
        if (proposed is null) return [];
        var report = await _compiler.ValidateAsync(proposed, draft.DraftRevision, ct);
        return report.Findings.Where(f => f.Severity == ValidationSeverity.Error).ToList();
    }

    /// <summary>The one-shot repair prompt: the original ask + the failing findings + the prior proposal to correct.</summary>
    internal static string BuildRepairMessage(string userMessage, DesignerAgentResult firstAttempt, IReadOnlyList<ValidationFinding> errors)
    {
        var findings = string.Join("\n", errors.Select(f => $"- {f.RuleId}: {f.Message}"));
        return $"""
            {userMessage}

            Your previous proposal failed validation with these errors. Return a corrected COMPLETE
            definition that fixes ALL of them (change only what the errors require):
            {findings}

            Your previous proposal was:
            {firstAttempt.ProposalDefinitionJson}
            """;
    }

    /// <summary>
    /// Computes the authoritative change set and pure-tier validation for a proposal (doc 14 §4.1).
    /// Returns (null, null) when there is no proposal; a non-null block reason means the proposal
    /// failed validation and the diff card should disable Apply (the merge re-validates regardless).
    /// </summary>
    internal (IReadOnlyList<ProposalChangeItem>? Changes, string? BlockReason) BuildProposalReview(
        DefinitionDraftDocument draft,
        string proposalDefinitionJson)
    {
        if (string.IsNullOrEmpty(proposalDefinitionJson)) return (null, null);

        var proposed = JsonSerializer.Deserialize<WorkflowDefinition>(proposalDefinitionJson, CanonicalProfile.Options);
        if (proposed is null) return (null, null);

        var changes = ProposalChangeSetBuilder.Build(_diffService.Compute(draft.Definition, proposed), draft.Definition, proposed);
        var blocking = _compiler.ValidateStructural(proposed)
            .Where(f => f.Severity == ValidationSeverity.Error)
            .ToList();
        var blockReason = blocking.Count == 0
            ? null
            : string.Join("; ", blocking.Select(f => $"{f.RuleId}: {f.Message}"));

        return (changes, blockReason);
    }

    /// <summary>Parses the raw agent text into a proposal; falls back to plain reasoning when unparseable.</summary>
    internal static DesignerAgentResult BuildResult(string raw)
    {
        if (AgentProposalParser.TryParse(raw, out var proposal) && proposal!.Definition is not null)
        {
            var definitionJson = JsonSerializer.Serialize(proposal.Definition, CanonicalProfile.Options);
            return new DesignerAgentResult(proposal.Reason ?? string.Empty, definitionJson);
        }

        // Parse failed. If the model clearly attempted a proposal (JSON-ish), don't dump the raw blob
        // into the chat — show a friendly recovery message. A genuine plain-text answer passes through.
        return new DesignerAgentResult(LooksLikeJson(raw) ? ParseFailureMessage : raw, string.Empty);
    }

    /// <summary>Recovery text shown when the agent attempted a proposal but it could not be parsed.</summary>
    internal const string ParseFailureMessage =
        "I drafted some changes but couldn't structure them into a valid proposal. " +
        "Could you rephrase or simplify your request?";

    /// <summary>True if the text looks like an attempted JSON proposal (object or fenced block).</summary>
    internal static bool LooksLikeJson(string raw)
    {
        var trimmed = raw.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith("```", StringComparison.Ordinal);
    }

    /// <summary>
    /// Assembles the agent's system prompt from the design-language schema, the available approver
    /// roles, the available MCP tools (ADR-CD7), the available agent roles (S9.27), the engagement
    /// type, and the current draft (doc 14 §3), and instructs the model to return a single
    /// structured <see cref="AgentProposal"/> JSON object.
    /// </summary>
    internal async Task<string> BuildSystemPromptAsync(DefinitionDraftDocument draft, CancellationToken ct)
    {
        var schemaJson = JsonSerializer.Serialize(_schemaProvider.GetSchema(), CanonicalProfile.Options);
        var roles = await _roleCatalog.GetApproverRolesAsync(ct);
        var rolesJson = JsonSerializer.Serialize(roles, CanonicalProfile.Options);
        var tools = await _toolCatalog.GetToolsAsync(ct);
        var toolsJson = JsonSerializer.Serialize(tools, CanonicalProfile.Options);
        var agentRoles = await _agentRoleCatalog.GetAgentRolesAsync(ct);
        var agentRolesJson = JsonSerializer.Serialize(agentRoles, CanonicalProfile.Options);
        var contracts = string.Join(", ", _schemaProvider.GetSchema().Contracts);
        var instructions = string.Join(", ", await _instructionCatalog.ListRefsAsync(ct));
        var baselineComponents = string.Join(", ", await _componentCatalog.GetBaselineComponentNamesAsync(ct));
        var dynamicFields = string.Join(", ", await _componentCatalog.GetDynamicFieldNamesAsync(ct));
        var entry = await _entryContractCatalog.GetEntryContractAsync(ct);
        var definitionJson = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);

        return $$"""
            You are an expert workflow designer that edits a typed workflow definition for a user.
            Engagement type: {{draft.Definition.EngagementType}}

            Definition-language schema — use ONLY these node types, fields, and enum values. For any
            field typed `object:<Name>`, fill it using that object's fields from the schema's `objects`
            section (e.g. a `context_request` must use `engagement_id`, `agent_role`,
            `baseline_components`, `dynamic_fields` — do not invent other keys):
            {{schemaJson}}

            Available approver roles — use ONLY these role_id values for human_gate approver_roles:
            {{rolesJson}}

            Available MCP tools — use ONLY these tool_ref values for agent_task.tool_refs
            (each is "{reverse-dns-server}/{tool}", ADR-CD9; copy the value verbatim):
            {{toolsJson}}

            Available agent roles — use ONLY these role_id values for agent_task.role:
            {{agentRolesJson}}

            Available contracts — the ONLY valid values for an agent_task's `input_contract_type`
            and `output_contract_type` and a `data` edge's `contract_type`:
            {{contracts}}

            Available instructions — the ONLY valid values for an agent_task's `instructions_ref`:
            {{instructions}}

            Available baseline context components — the values an agent_task's
            `context_request.baseline_components` may contain:
            {{baselineComponents}}

            Available dynamic context fields — the values an agent_task's
            `context_request.dynamic_fields` may contain:
            {{dynamicFields}}

            Structural rules (violating any of these produces an unexecutable or invalid workflow):
            - Every `agent_task`'s `context_request.baseline_components` MUST be a NON-EMPTY subset of the
              Available baseline context components above (an empty list is rejected at runtime; never
              use "*"). When unsure, include all of them. Its `dynamic_fields` are drawn from the
              Available dynamic context fields (the entry node includes `"{{entry.DynamicFieldName}}"`
              per the rule below).
            - A `data` edge's `contract_type` MUST equal the consuming `agent_task` node's
              `input_contract_type`, and both must be one of the Available contracts above. To connect
              two steps whose contracts differ, insert an intermediate `agent_task` that consumes the
              upstream contract and produces the downstream one — never point mismatched contracts at
              each other.
            - Every `agent_task`'s `instructions_ref` MUST be one of the Available instructions above
              (they are file-backed; a ref not in that list fails the agent live at runtime). Do NOT
              derive a ref from the node's role or name. Match each step to the closest-matching
              instruction; for an intermediate mapping/converter `agent_task` (one inserted only to
              reshape one contract into another), use `instructions/transform.md`.
            - The workflow's single entry `agent_task` (the one with no incoming `control` edge) is
              handed {{entry.Description}} at runtime, not an upstream `data` payload — so it MUST set
              `input_contract_type` to `{{entry.ContractTypeName}}` and include
              `"{{entry.DynamicFieldName}}"` in its `context_request.dynamic_fields`. It still produces
              whatever `output_contract_type` its job requires — a step that fetches a record reads
              that input and outputs the record's own contract. Every non-entry node takes its input
              from its upstream `data` edge as usual. Leave `engagement_id` blank — the runtime fills
              it in.
            - Give every `agent_task` an `artifact_key`: a short snake_case name for what that step
              produces (e.g. `ticket`, `match`, `booking`), **unique across the workflow**. It is
              where the node's output is stored — so its output shows in a test run, and a
              `human_gate`'s `rollback_to_node_id` can restore that node's approved snapshot. Do NOT
              reuse an `artifact_key` across nodes (a later node's output would overwrite the earlier
              one's, and the earlier step would show no output); a converter/mapping step gets its
              own key too.
            - Every node object in `definition.nodes` MUST include a `node_type` field whose value is
              one of the discriminators listed in the schema's `node_types` (e.g.
              `"node_type": "agent_task"`). A node without `node_type` is rejected.
            - NEVER propose a node type whose schema entry says `"executable": false` — the runtime
              does not implement it, so the workflow would validate, publish, and then fail
              permanently on its first run. Today that means `agent_task`, `human_gate`,
              `decision` and `mcp_tool` are the only node types you may use.
              • A `decision` node routes conditionally (S13.7j): author its `branches` list — each
                branch is `{"target_node_id": ..., "condition": ...}` where a condition is either
                `{"kind":"field","field_path":"{artifact_key}.{wire_field}","operator":"gt|lt|gte|lte|eq|neq|in|contains|starts_with|ends_with","value":"..."}`
                (canonical strings: decimals quoted, dates ISO-8601; `in` uses `values` instead) or
                `{"kind":"logical","op":"and|or|not","operands":[...]}` (`not` takes exactly one).
                Branches evaluate in order, first match routes; `default_branch_node_id` is the
                mandatory fallback. Every branch target (and the default) must ALSO have a control
                edge from the decision node. A `field_path` must name a section produced UPSTREAM
                of the decision, using the wire field names from the "Available contracts" list;
                unselected branches are skipped at execution, not run. The legacy string
                `predicate` field is deprecated — never author it.
              • An `mcp_tool` node (executable since S13.7c) calls ONE registered tool
                deterministically — no agent, no model cost. Use it when a step is exactly one tool
                call whose inputs come from upstream data (e.g. "update the ticket", "book the
                resource"); keep an `agent_task` with `tool_refs` when judgment must select tools,
                interpret results, or compose several calls. An `mcp_tool` node requires `tool_ref`
                (from the catalogue), `timeout_seconds`, and `idempotency_key_spec` (required for
                writes — `mcp.write-idempotency`); its single inbound `data` edge's payload maps
                top-level wire fields onto the tool's arguments, and its result feeds downstream
                `data` edges and `decision` predicates like any step output.
              • Independent steps are expressed as ordinary `agent_task` nodes with control edges
                fanning out from their shared predecessor and converging on a shared successor — NOT
                with a `parallel` node. The runtime executes such branches CONCURRENTLY (ADR-5:
                the edges are the parallelism spec), so describing them as parallel is accurate.
                A `human_gate` is a barrier: it opens only after every in-flight branch settles,
                so the approver always reviews a consistent, finished state.
              • A node may have AT MOST ONE inbound `data` edge (`data.single-data-predecessor`):
                converge branches with control edges; a joining step that needs several branches'
                content reads their sections rather than declaring multiple data inputs.
            - An `agent_task`'s `output_contract_type` MUST be one of the "Available contract types"
              listed above and nothing else. That list already excludes internal projection and
              persistence types, which the model provider cannot emit as structured output — naming
              one anyway fails the run, not just validation.
            - The `control` edges alone must form the workflow's spine: exactly one node with no
              incoming control edge (the single entry), and every other node reachable from it
              via control edges. `data` edges supplement the spine; they never replace it.
            - When an "Available …" list above is empty, do not fabricate values for the fields
              it governs — leave those node kinds/fields out and explain why in plain text.

            Design guidance — node granularity (ADR-E8-era platform guidance, S13.6): prefer ONE
            capable, well-tooled `agent_task` per business step over a cluster of small specialist
            agents. Decompose into multiple `agent_task` nodes only where the workflow genuinely
            needs a governance boundary between them — a `human_gate`, a `decision` branch, a
            separately reusable/approvable output, or a different agent role. The graph exists to
            carry governance, not to decompose one step's reasoning into sub-agents.

            The current workflow definition is:
            {{definitionJson}}

            When the user asks for a change, respond with ONLY a JSON object (no prose, no markdown
            fences) of the form:
            {"reason": "<one short sentence>", "definition": <complete WorkflowDefinition>, "changed_node_ids": ["<node_id>", ...]}
            The "definition" must be the COMPLETE workflow definition (every node and edge), not a patch.
            If the user only asks a question, answer in plain text instead.
            """;
    }
}

/// <summary>Outcome of one design-agent turn: the prose reasoning and, when a proposal was parsed,
/// the complete proposed <see cref="WorkflowDefinition"/> as canonical JSON (empty when none).</summary>
internal sealed record DesignerAgentResult(string Reasoning, string ProposalDefinitionJson);
