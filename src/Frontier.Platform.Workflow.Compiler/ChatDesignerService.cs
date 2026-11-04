using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Extensions.AI;
using Frontier.Platform.Workflow.Model;


namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Chat designer service: manages persistent design conversation over drafts.
/// Doc 14 §1–2: fetch draft, submit message to agent, record turn. A turn never merges its
/// proposal: the designer approves changes through
/// <see cref="IProposalMergeService.ApplyApprovedChangesAsync(string, string, string, IReadOnlyList{string}, string, CancellationToken)"/> (S13.203).
/// Phase 1: real LLM call via IChatClient; the model is resolved through the
/// <c>deep-reasoning</c> role with adaptive thinking (doc 14 §3, S9.9a) via
/// <see cref="IDesignerModelProvider"/>. Phase 2 will replace this with MAF agents.
/// </summary>
internal sealed class ChatDesignerService : IChatDesignerService
{
    private readonly IDefinitionStore _store;
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
    private readonly IContractSnapshotCatalog _contractCatalog;

    /// <summary>
    /// Creates the service over its stores and catalogues. It takes no <see cref="IProposalMergeService"/>:
    /// a turn only records its proposal (S13.203). Every argument is required.
    /// </summary>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ChatDesignerService(
        IDefinitionStore store,
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
        IEntryContractCatalog entryContractCatalog,
        IContractSnapshotCatalog contractCatalog)
    {
        ArgumentNullException.ThrowIfNull(store);
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
        ArgumentNullException.ThrowIfNull(contractCatalog);
        _store = store;
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
        _contractCatalog = contractCatalog;
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
        var (proposalChanges, blockReason) = BuildProposalReview(draft, agentProposalJson, agentResult.UnknownKeys);

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
            ReplyTruncated = agentResult.ReplyTruncated ? true : null,
            MergeOutcome = null,
            ConflictSummary = null,
            Mentions = validatedMentions.Count > 0 ? validatedMentions : null
        };

        // Persist turn
        var persistedTurn = await _store.PersistDesignTurnAsync(turn, ct);

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

        var (client, options) = await ResolveDesignerCallAsync(draft.WorkflowId, ct).ConfigureAwait(false);

        // S9.12 is Option A (reveal-on-completion): the UI shows a "designing" indicator and reveals
        // the parsed result via the ChatTurnCompleted SignalR event, so streaming here is a
        // server-side resilience measure (S9.9a, doc 14 §3) — a long proposal arrives as it
        // generates instead of one blocking read holding the whole response in flight — not a
        // user-visible behaviour change.
        var response = await client.GetStreamingResponseAsync(messages, options, ct)
            .ToChatResponseAsync(ct).ConfigureAwait(false);
        return await StampProposalAsync(ResultOf(response), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// ADR-PA41: copies each MCP tool's registered contract refs and write flag onto the proposal's
    /// <c>mcp_tool</c> nodes (<see cref="McpToolRegistryStamp"/>). It runs on every proposal the agent
    /// returns, the first attempt and the repair alike, so validation, the diff and the merge all see
    /// the registry's values and the repair pass is never spent on fields the agent cannot know.
    /// </summary>
    internal async Task<DesignerAgentResult> StampProposalAsync(DesignerAgentResult result, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(result.ProposalDefinitionJson)) return result;
        var proposed = JsonSerializer.Deserialize<WorkflowDefinition>(result.ProposalDefinitionJson, CanonicalProfile.Options)!;
        var stamped = McpToolRegistryStamp.Apply(proposed, await _toolCatalog.GetToolsAsync(ct).ConfigureAwait(false));
        return result with { ProposalDefinitionJson = JsonSerializer.Serialize(stamped, CanonicalProfile.Options) };
    }

    /// <summary>
    /// S13.202 (K7): resolves the designer model once per turn and returns the selection's
    /// <see cref="DesignerModelSelection.ChatClient"/>, else the registered client, with the call's
    /// options, so the model id and the client come from the same selection.
    /// </summary>
    internal async Task<(IChatClient Client, ChatOptions Options)> ResolveDesignerCallAsync(string workflowId, CancellationToken ct)
    {
        var selection = await _modelProvider.GetAsync(workflowId, ct).ConfigureAwait(false);
        return (selection.ChatClient ?? _chatClient, BuildChatOptions(selection));
    }

    /// <summary>
    /// S9.9a (doc 14 §3): builds the designer call's <see cref="ChatOptions"/> from the
    /// <see cref="IDesignerModelProvider"/> resolution — model and output ceiling from the
    /// <c>deep-reasoning</c> role's active mapping instead of a hardcoded id, plus the
    /// provider-neutral <see cref="ChatClientOptionKeys.AdaptiveThinking"/> flag the
    /// provider adapter translates into its own thinking request shape.
    /// </summary>
    internal static ChatOptions BuildChatOptions(DesignerModelSelection selection)
    {
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
        // The proposal is a fresh definition, not the stored draft, so it carries no stored schema
        // version: passing the draft's would pin an old-schema finding on a brand-new proposal.
        var report = await _compiler.ValidateAsync(proposed, draft.DraftRevision, storedSchemaVersion: null, ct);
        return [.. UnknownKeyFindings(result.UnknownKeys), .. report.Findings.Where(f => f.Severity == ValidationSeverity.Error)];
    }

    /// <summary>Rule id for a proposal key the schema does not define (S13.210).</summary>
    internal const string UnknownKeyRuleId = "proposal.unknown-key";

    /// <summary>
    /// S13.210: one Error finding per key the model put in its proposal that the schema does not
    /// define. The parse dropped each one silently, so without these the designer never learns the
    /// model got a field wrong; as Error findings they drive the repair pass and block Apply.
    /// </summary>
    internal static IReadOnlyList<ValidationFinding> UnknownKeyFindings(IReadOnlyList<string>? unknownKeys) =>
        (unknownKeys ?? []).Select(key => new ValidationFinding(UnknownKeyRuleId, ValidationSeverity.Error,
            $"'{key}' is not a field the schema defines, so it was dropped. Use only the schema's field names.",
            FieldPath: key)).ToList();

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
    /// Keys the model invented that survived the repair pass (<paramref name="unknownKeys"/>, S13.210)
    /// block too, so the designer is told rather than shown a proposal missing what the model meant.
    /// </summary>
    internal (IReadOnlyList<ProposalChangeItem>? Changes, string? BlockReason) BuildProposalReview(
        DefinitionDraftDocument draft,
        string proposalDefinitionJson,
        IReadOnlyList<string>? unknownKeys)
    {
        if (string.IsNullOrEmpty(proposalDefinitionJson)) return (null, null);

        var proposed = JsonSerializer.Deserialize<WorkflowDefinition>(proposalDefinitionJson, CanonicalProfile.Options);
        if (proposed is null) return (null, null);

        var changes = ProposalChangeSetBuilder.Build(_diffService.Compute(draft.Definition, proposed), draft.Definition, proposed);
        List<ValidationFinding> blocking =
            [.. UnknownKeyFindings(unknownKeys), .. _compiler.ValidateStructural(proposed).Where(f => f.Severity == ValidationSeverity.Error)];
        var blockReason = blocking.Count == 0
            ? null
            : string.Join("; ", blocking.Select(f => $"{f.RuleId}: {f.Message}"));

        return (changes, blockReason);
    }

    /// <summary>
    /// S13.200: classifies a completed model response. A reply the model stopped at its output
    /// ceiling (<see cref="ChatFinishReason.Length"/>) is cut off mid-definition, so it is never
    /// parsed: the turn says plainly that the workflow is too large for one reply and is flagged
    /// <see cref="DesignerAgentResult.ReplyTruncated"/>. Every other reply goes to <see cref="BuildResult"/>.
    /// </summary>
    internal static DesignerAgentResult ResultOf(ChatResponse response) =>
        response.FinishReason == ChatFinishReason.Length
            ? new DesignerAgentResult(TruncatedReplyMessage, string.Empty, ReplyTruncated: true)
            : BuildResult(response.Text);

    /// <summary>Text shown when the model's reply hit its output-token ceiling before the definition was complete.</summary>
    internal const string TruncatedReplyMessage =
        "This workflow is too large for me to return in a single reply, so I couldn't propose this change. " +
        "Nothing in the draft has changed. Until I can return partial edits, make this change in the editor instead.";

    /// <summary>Parses the raw agent text into a proposal; falls back to plain reasoning when unparseable.</summary>
    internal static DesignerAgentResult BuildResult(string raw)
    {
        if (AgentProposalParser.TryParse(raw, out var proposal, out var unknownKeys) && proposal!.Definition is not null)
        {
            var definitionJson = JsonSerializer.Serialize(proposal.Definition, CanonicalProfile.Options);
            return new DesignerAgentResult(proposal.Reason ?? string.Empty, definitionJson,
                UnknownKeys: unknownKeys.Count > 0 ? unknownKeys : null);
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
    /// roles, the available MCP tools (ADR-CD7), the available agent roles (S9.27), the registry's
    /// contracts with their annotations (ADR-PA41), the engagement type, and the current draft
    /// (doc 14 §3), and instructs the model to return a single structured <see cref="AgentProposal"/>
    /// JSON object.
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
        var registryContracts = DesignerContractAnnotations.Ordered(await _contractCatalog.ListAsync(ct));
        var contracts = string.Join(", ", _schemaProvider.GetSchema().Contracts.Concat(registryContracts.Select(c => c.ContractRef)));
        var annotations = DesignerContractAnnotations.Render(registryContracts);
        var anyJsonTools = DesignerContractAnnotations.AnyJsonTools(tools);
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

            Available agent roles — use ONLY these role_id values for agent_task.role. Each role's
            `backing` says what serves it: `model` (a model chain) or `agent` (a registered agent).
            A role's `stakes`, when given, says what is commercially at stake if its output is wrong:
            `material` (it feeds client deliverables directly), `standard` (it is checked or
            summarised before use) or `mechanical` (no client-facing content):
            {{agentRolesJson}}

            Available contracts — the ONLY valid values for an agent_task's `input_contract_type`
            and `output_contract_type` and a `data` edge's `contract_type`:
            {{contracts}}

            Registry contracts — what each registry ref above (`{namespace}/{name}/{major}.{minor}`)
            means: its description, its properties' descriptions, its examples and its rules, the
            same annotations the designer's contract panel shows. Pick the contract whose meaning
            fits the step, not the one whose name sounds closest:
            {{annotations}}

            Available instructions — the ONLY valid values for an agent_task's `instructions_ref`
            (omit it on a role whose `backing` is `agent`):
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
            - An `agent_task` whose `role` is an agent role declaring an `input_contract_ref` or
              `output_contract_ref` MUST set its `input_contract_type` and `output_contract_type` to
              exactly those refs (`model-role.contracts-match`). A role that declares neither leaves
              the node free to name its own.
            - An `agent_task` on a role whose `backing` is `model` MUST set `instructions_ref` to one of
              the Available instructions above (they are file-backed; a ref not in that list fails the
              agent live at runtime). An `agent_task` on a role whose `backing` is `agent` may omit
              `instructions_ref`, because the agent brings its own instructions; omit the field rather
              than leaving it blank. Do NOT derive a ref from the node's role or name. Match each step
              to the closest-matching instruction; for an intermediate mapping/converter `agent_task`
              (one inserted only to reshape one contract into another), use `instructions/transform.md`.
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
                `data` edges and `decision` predicates like any step output. The designer copies
                the tool's `input_contract_ref`, `output_contract_ref` and `writes` from its registry
                record onto the node's `input_contract_type`, `output_contract_type` and `writes`
                after you propose it, so you need not author them; whatever you write there is
                replaced. A tool whose `writes` is true changes its connector's state. A tool with
                no `output_contract_ref` returns any JSON: no contract checks its result. Tools that
                return any JSON: {{anyJsonTools}}
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
/// the complete proposed <see cref="WorkflowDefinition"/> as canonical JSON (empty when none).
/// <paramref name="ReplyTruncated"/> is true when the model stopped at its output-token ceiling (S13.200).
/// <paramref name="UnknownKeys"/> lists the keys the proposal carried that the schema does not define,
/// which the parse dropped (S13.210); null when there were none.</summary>
internal sealed record DesignerAgentResult(
    string Reasoning, string ProposalDefinitionJson, bool ReplyTruncated = false, IReadOnlyList<string>? UnknownKeys = null);
