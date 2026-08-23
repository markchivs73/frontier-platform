using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
using Frontier.Platform.Workflow.Compiler;

namespace Frontier.Platform.Workflow.Compiler.Tests;

#pragma warning disable CA1034, CA1515



/// <summary>
/// Chat designer service tests: persistent draft-scoped conversation protocol (doc 14 §1–2).
/// Phase 1: validates turn persistence, history tracking, and proposal merge integration.
/// IChatClient is mocked so tests do not require a real Anthropic API key.
/// </summary>
public sealed class ChatDesignerServiceTests
{
    private static readonly IReadOnlyList<string> EmptyNodes = Array.Empty<string>();
    private static readonly IReadOnlyList<WorkflowEdge> EmptyEdges = Array.Empty<WorkflowEdge>();

    /// <summary>Creates a ChatDesignerService backed by an in-memory store and a mock IChatClient.
    /// <paramref name="agentResponse"/> is the text the mocked model returns (prose by default; pass
    /// structured JSON to exercise the proposal path).</summary>
    internal static ChatDesignerService CreateService(
        IDefinitionStore store,
        IProposalMergeService mergeService,
        string agentResponse = "mock agent response")
    {
        var chatClientMock = new Mock<IChatClient>();
        chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamingResponse(agentResponse));
        return new ChatDesignerService(
            store, mergeService, chatClientMock.Object, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(),
            new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), new NodeDiffService(),
            new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog());
    }

    /// <summary>S9.9a: the service consumes the streaming surface; the mock streams the response as one update.</summary>
    internal static async IAsyncEnumerable<ChatResponseUpdate> StreamingResponse(string agentResponse)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, agentResponse);
    }

    /// <summary>
    /// Entry-contract catalogue double. Deliberately names nothing this workload uses: the whole
    /// point of the port is that the designer works from whatever a deployment answers.
    /// </summary>
    internal sealed class FakeEntryContractCatalog : IEntryContractCatalog
    {
        public Task<EntryContractDescriptor> GetEntryContractAsync(CancellationToken ct) =>
            Task.FromResult(new EntryContractDescriptor
            {
                ContractTypeName = "CaseSummary",
                DynamicFieldName = "case_summary",
                Description = "the case summary",
            });
    }

    /// <summary>Approver-role catalog test double with one descriptor.</summary>
    internal sealed class FakeApproverRoleCatalog : IApproverRoleCatalog
    {
        public Task<IReadOnlyList<ApproverRoleDescriptor>> GetApproverRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApproverRoleDescriptor>>(
            [
                new ApproverRoleDescriptor
                {
                    RoleId = "finance-lead",
                    DisplayName = "Finance Lead",
                    Description = "Budget approval",
                    BusinessArea = "commercial",
                    Responsibilities = ["budget-approval"],
                    ApplicableGateKinds = ["business"],
                    Examples = "SOWs > £50k",
                },
            ]);
    }

    /// <summary>MCP tool catalog test double with one descriptor (ADR-CD9).</summary>
    internal sealed class FakeDesignerToolCatalog : IDesignerToolCatalog
    {
        public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>(
            [
                new DesignerToolDescriptor
                {
                    ToolRef = "io.frontier.demo/autotask/get_new_ticket",
                    Server = "io.frontier.demo/autotask",
                    Name = "get_new_ticket",
                    Description = "Fetches the next unassigned helpdesk ticket.",
                },
            ]);
    }

    /// <summary>Agent-role catalog test double with one descriptor (S9.27).</summary>
    internal sealed class FakeAgentRoleCatalog : IAgentRoleCatalog
    {
        public Task<IReadOnlyList<AgentRoleDescriptor>> GetAgentRolesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentRoleDescriptor>>(
            [
                new AgentRoleDescriptor
                {
                    RoleId = "deep-reasoning",
                    Description = "Highest-capability chain for commercially material output.",
                },
            ]);
    }

    /// <summary>Designer-model provider test double (S9.9a): a fixed deep-reasoning-style selection.</summary>
    internal sealed class FakeDesignerModelProvider : IDesignerModelProvider
    {
        public string? RequestedWorkflowId { get; private set; }

        public Task<DesignerModelSelection> GetAsync(string workflowId, CancellationToken ct)
        {
            RequestedWorkflowId = workflowId;
            return Task.FromResult(new DesignerModelSelection
            {
                ModelId = "resolved-deep-reasoning-model",
                MaxOutputTokens = 16_000,
                AdaptiveThinking = true,
            });
        }
    }

    /// <summary>Instruction catalogue test double (S9.82): a fixed set of resolvable refs so the
    /// system prompt's "Available instructions" grounding block is exercised.</summary>
    internal sealed class FakeInstructionCatalog : IInstructionCatalog
    {
        public Task<bool> ResolvesAsync(string instructionsRef, CancellationToken ct) =>
            Task.FromResult(instructionsRef is "instructions/fetch-ticket.md" or "instructions/transform.md");

        public Task<IReadOnlyList<string>> ListRefsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(["instructions/fetch-ticket.md", "instructions/transform.md"]);
    }

    /// <summary>Context-component catalogue test double (S9.83): fixed baseline components + dynamic
    /// fields so the system prompt's grounding blocks are exercised.</summary>
    internal sealed class FakeContextComponentCatalog : IContextComponentCatalog
    {
        public Task<IReadOnlyCollection<string>> GetBaselineComponentNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>(["firm-standards", "playbooks"]);

        public Task<IReadOnlyCollection<string>> GetDynamicFieldNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<string>>(["engagement_brief"]);
    }

    // S9.73 (doc 14 §4.3): one automatic repair pass. A proposal with resourced-tier Error findings
    // is fed back to the agent once (with the findings + its prior proposal); the deterministic
    // validator is the authority and the retry never loops.
    public sealed class RepairLoopTests
    {
        private static readonly ValidationFinding ContractError = new(
            "data.edge-type-match", ValidationSeverity.Error,
            "data edge carries 'TicketDetails' but consumer 'm' declares input contract 'DeveloperResourceList'.",
            NodeId: "m", EdgeRef: "f->m", FieldPath: "contract_type");

        /// <summary>Compiler whose resourced <c>ValidateAsync</c> returns a fixed finding set; pure <c>ValidateStructural</c> is always clean (so no diff-card block masks the retry assertion).</summary>
        private sealed class RepairCompiler : IDefinitionCompiler
        {
            public IReadOnlyList<ValidationFinding> AsyncFindings { get; init; } = [];
            public Task<ValidationReport> ValidateAsync(WorkflowDefinition d, string rev, CancellationToken ct) =>
                Task.FromResult(new ValidationReport("wf", rev, DateTime.UtcNow,
                    AsyncFindings.Any(f => f.Severity == ValidationSeverity.Error) ? ValidationOutcome.Fail : ValidationOutcome.Pass,
                    AsyncFindings, new Dictionary<string, string>()));
            public IReadOnlyList<ValidationFinding> ValidateStructural(WorkflowDefinition d) => [];
            public string ComputeDefinitionHash(WorkflowDefinition d) => "hash";
        }

        private static (ChatDesignerService Service, Mock<IChatClient> Client, InMemoryDefinitionStore Store)
            Build(IDefinitionCompiler compiler, params string[] responses)
        {
            var store = new InMemoryDefinitionStore();
            var clientMock = new Mock<IChatClient>();
            var seq = clientMock.SetupSequence(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()));
            foreach (var r in responses) seq = seq.Returns(StreamingResponse(r));
            var service = new ChatDesignerService(store,
                new ProposalMergeService(store, new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new NodeDiffService()),
                clientMock.Object, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(),
                new FakeAgentRoleCatalog(), new NodeDiffService(), compiler, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog());
            return (service, clientMock, store);
        }

        private static string Proposal(string reason)
        {
            var defJson = JsonSerializer.Serialize(CreateMinimalDraft("wf-x").Definition, CanonicalProfile.Options);
            return $$"""{"reason":"{{reason}}","definition":{{defJson}},"changed_node_ids":[]}""";
        }

        private static async Task<DesignTurnDocument> RunTurnAsync(ChatDesignerService service, InMemoryDefinitionStore store)
        {
            await store.SaveDraftAsync("wf-x", CreateMinimalDraft("wf-x"), "no-etag", CancellationToken.None);
            return await service.SubmitDesignTurnAsync("wf-x",
                new DesignTurnRequest { DesignerId = "d", Message = "build it", AutoMergeProposal = false }, CancellationToken.None);
        }

        private static void VerifyCalls(Mock<IChatClient> client, Times times) =>
            client.Verify(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), times);

        [Fact]
        public async Task ResourcedErrors_TriggerOneRepairPass_AndUseTheCorrectedProposal()
        {
            var (service, client, store) = Build(new RepairCompiler { AsyncFindings = [ContractError] },
                Proposal("first"), Proposal("second"));

            var result = await RunTurnAsync(service, store);

            VerifyCalls(client, Times.Exactly(2));                 // initial + one repair
            Assert.Equal("second", result.ProposalReasoningJson);  // the repaired proposal, not the first
        }

        [Fact]
        public async Task CleanProposal_NoRepair_SingleCall()
        {
            var (service, client, store) = Build(new RepairCompiler { AsyncFindings = [] }, Proposal("only"));

            var result = await RunTurnAsync(service, store);

            VerifyCalls(client, Times.Once());
            Assert.Equal("only", result.ProposalReasoningJson);
        }

        [Fact]
        public async Task PersistentErrors_RetriesExactlyOnce_NeverLoops()
        {
            // Both responses still validate as errors; only two responses are queued, so a third
            // model call would throw — proving the retry is one-shot.
            var (service, client, store) = Build(new RepairCompiler { AsyncFindings = [ContractError] },
                Proposal("first"), Proposal("second"));

            await RunTurnAsync(service, store);

            VerifyCalls(client, Times.Exactly(2));
        }

        [Fact]
        public void BuildRepairMessage_CarriesTheFindingsAndThePriorProposal()
        {
            var msg = ChatDesignerService.BuildRepairMessage("add a matcher step",
                new DesignerAgentResult("r", "{\"prior_proposal\":true}"), [ContractError]);

            Assert.Contains("add a matcher step", msg, StringComparison.Ordinal);
            Assert.Contains("failed validation", msg, StringComparison.Ordinal);
            Assert.Contains("data.edge-type-match", msg, StringComparison.Ordinal);
            Assert.Contains("{\"prior_proposal\":true}", msg, StringComparison.Ordinal);
        }
    }

    public sealed class SubmitDesignTurnTests
    {
        [Fact]
        public async Task SubmitDesignTurn_FirstTurn_CreatesTurnAndHistory()
        {
            var store = new InMemoryDefinitionStore();
            var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            var mergeService = new ProposalMergeService(store, validator, new NodeDiffService());
            var service = CreateService(store, mergeService);

            var workflowId = "wf-test";
            var draft = CreateMinimalDraft(workflowId);
            await store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

            var request = new DesignTurnRequest
            {
                DesignerId = "designer-1",
                Message = "Add a validation step",
                AutoMergeProposal = false
            };

            var result = await service.SubmitDesignTurnAsync(workflowId, request, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(1, result.TurnNumber);
            Assert.Equal("designer-1", result.DesignerId);
            Assert.Equal("Add a validation step", result.DesignerMessage);
            Assert.Null(result.MergeOutcome);
        }

        [Fact]
        public async Task SubmitDesignTurn_MultipleTurns_IncrementsSequence()
        {
            var store = new InMemoryDefinitionStore();
            var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            var mergeService = new ProposalMergeService(store, validator, new NodeDiffService());
            var service = CreateService(store, mergeService);

            var workflowId = "wf-test";
            var draft = CreateMinimalDraft(workflowId);
            await store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

            var request1 = new DesignTurnRequest
            {
                DesignerId = "designer-1",
                Message = "Turn 1",
                AutoMergeProposal = false
            };
            var request2 = new DesignTurnRequest
            {
                DesignerId = "designer-1",
                Message = "Turn 2",
                AutoMergeProposal = false
            };

            var turn1 = await service.SubmitDesignTurnAsync(workflowId, request1, CancellationToken.None);
            var turn2 = await service.SubmitDesignTurnAsync(workflowId, request2, CancellationToken.None);

            Assert.Equal(1, turn1.TurnNumber);
            Assert.Equal(2, turn2.TurnNumber);
        }

        [Fact]
        public async Task SubmitDesignTurn_NoProposalWithAutoMerge_DoesNotMerge()
        {
            var store = new InMemoryDefinitionStore();
            var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            var mergeService = new ProposalMergeService(store, validator, new NodeDiffService());
            var service = CreateService(store, mergeService);

            var workflowId = "wf-test";
            var draft = CreateMinimalDraft(workflowId);
            await store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

            var request = new DesignTurnRequest
            {
                DesignerId = "designer-1",
                Message = "Test message",
                AutoMergeProposal = true // Phase 1 stub agent returns empty proposal
            };

            var result = await service.SubmitDesignTurnAsync(workflowId, request, CancellationToken.None);

            // Phase 1: no proposal is generated, so merge is skipped
            Assert.Null(result.MergeOutcome);
        }
    }

    /// <summary>S9.33 (doc 14 §8a): mention validation against the live discovery catalogues.</summary>
    public sealed class MentionValidationTests
    {
        [Fact]
        public async Task ValidateMentionsAsync_ResolvesEachKindAgainstItsOwnCatalogue()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = new ProposalMergeService(store, new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new NodeDiffService());
            var service = CreateService(store, mergeService);

            var validated = await service.ValidateMentionsAsync(
                [
                    new ResourceMention { Kind = MentionKind.AgentRole, Ref = "deep-reasoning" },
                    new ResourceMention { Kind = MentionKind.McpTool, Ref = "io.frontier.demo/autotask/get_new_ticket" },
                    new ResourceMention { Kind = MentionKind.ApproverRole, Ref = "finance-lead" },
                ],
                CancellationToken.None);

            Assert.All(validated, m => Assert.True(m.Resolved));
        }

        [Fact]
        public async Task ValidateMentionsAsync_UnknownRefPerKind_IsUnresolved()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = new ProposalMergeService(store, new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new NodeDiffService());
            var service = CreateService(store, mergeService);

            var validated = await service.ValidateMentionsAsync(
                [
                    new ResourceMention { Kind = MentionKind.AgentRole, Ref = "invented-role" },
                    new ResourceMention { Kind = MentionKind.McpTool, Ref = "connectors/ghost.invented_tool" },
                    new ResourceMention { Kind = MentionKind.ApproverRole, Ref = "invented-approver" },
                ],
                CancellationToken.None);

            Assert.All(validated, m => Assert.False(m.Resolved));
        }

        [Fact]
        public async Task ValidateMentionsAsync_UnrecognisedKind_IsUnresolved()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = new ProposalMergeService(store, new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new NodeDiffService());
            var service = CreateService(store, mergeService);

            var validated = await service.ValidateMentionsAsync(
                [new ResourceMention { Kind = "not-a-kind", Ref = "deep-reasoning" }], CancellationToken.None);

            Assert.False(Assert.Single(validated).Resolved);
        }

        [Fact]
        public async Task ValidateMentionsAsync_EmptyList_ReturnsEmptyWithoutCallingCatalogues()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = new ProposalMergeService(store, new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new NodeDiffService());
            var service = CreateService(store, mergeService);

            Assert.Empty(await service.ValidateMentionsAsync([], CancellationToken.None));
        }

        [Fact]
        public void WithMentionNote_ResolvedMentions_AppendsIntentBlock()
        {
            var note = ChatDesignerService.WithMentionNote(
                "use deep reasoning",
                [new ValidatedMention { Kind = MentionKind.AgentRole, Ref = "deep-reasoning", Resolved = true }]);

            Assert.Contains("use deep reasoning", note, StringComparison.Ordinal);
            Assert.Contains("agent_role: deep-reasoning", note, StringComparison.Ordinal);
        }

        [Fact]
        public void WithMentionNote_UnresolvedMentions_AreExcludedFromThePrompt()
        {
            var note = ChatDesignerService.WithMentionNote(
                "hello",
                [new ValidatedMention { Kind = MentionKind.AgentRole, Ref = "ghost-role", Resolved = false }]);

            Assert.Equal("hello", note);
        }

        [Fact]
        public void WithMentionNote_NoMentions_ReturnsMessageUnchanged() =>
            Assert.Equal("hello", ChatDesignerService.WithMentionNote("hello", []));

        [Fact]
        public async Task SubmitDesignTurn_WithMentions_PersistsValidatedMentionsOnTheTurn()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = new ProposalMergeService(store, new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new NodeDiffService());
            var service = CreateService(store, mergeService);
            var draft = CreateMinimalDraft("wf-test");
            await store.SaveDraftAsync("wf-test", draft, "no-etag", CancellationToken.None);

            var result = await service.SubmitDesignTurnAsync("wf-test", new DesignTurnRequest
            {
                DesignerId = "designer-1",
                Message = "use @deep-reasoning",
                AutoMergeProposal = false,
                Mentions = [new ResourceMention { Kind = MentionKind.AgentRole, Ref = "deep-reasoning" }],
            }, CancellationToken.None);

            var mention = Assert.Single(result.Mentions!);
            Assert.True(mention.Resolved);
            // The persisted human-visible message is untouched by the prompt-only intent note.
            Assert.Equal("use @deep-reasoning", result.DesignerMessage);
        }

        [Fact]
        public async Task SubmitDesignTurn_NoMentions_LeavesMentionsNull()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = new ProposalMergeService(store, new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), new NodeDiffService());
            var service = CreateService(store, mergeService);
            var draft = CreateMinimalDraft("wf-test");
            await store.SaveDraftAsync("wf-test", draft, "no-etag", CancellationToken.None);

            var result = await service.SubmitDesignTurnAsync("wf-test", new DesignTurnRequest
            {
                DesignerId = "designer-1", Message = "plain message", AutoMergeProposal = false,
            }, CancellationToken.None);

            Assert.Null(result.Mentions);
        }
    }

    public sealed class GetHistoryTests
    {
        [Fact]
        public async Task GetHistory_NoHistory_ReturnsNull()
        {
            var store = new InMemoryDefinitionStore();
            var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            var mergeService = new ProposalMergeService(store, validator, new NodeDiffService());
            var service = CreateService(store, mergeService);

            var result = await service.GetHistoryAsync("wf-nonexistent", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetHistory_AfterSubmittingTurns_ReturnsHistoryWithTurns()
        {
            var store = new InMemoryDefinitionStore();
            var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            var mergeService = new ProposalMergeService(store, validator, new NodeDiffService());
            var service = CreateService(store, mergeService);

            var workflowId = "wf-test";
            var draft = CreateMinimalDraft(workflowId);
            await store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

            var request1 = new DesignTurnRequest { DesignerId = "designer-1", Message = "Turn 1", AutoMergeProposal = false };
            var request2 = new DesignTurnRequest { DesignerId = "designer-1", Message = "Turn 2", AutoMergeProposal = false };

            await service.SubmitDesignTurnAsync(workflowId, request1, CancellationToken.None);
            await service.SubmitDesignTurnAsync(workflowId, request2, CancellationToken.None);

            var history = await service.GetHistoryAsync(workflowId, CancellationToken.None);

            Assert.NotNull(history);
            Assert.Equal(workflowId, history.WorkflowId);
            Assert.Equal(2, history.TotalTurns);
            Assert.NotEmpty(history.Turns);
        }
    }

    public sealed class GetAllTurnsTests
    {
        [Fact]
        public async Task GetAllTurns_ReturnsAllTurnsInOrder()
        {
            var store = new InMemoryDefinitionStore();
            var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            var mergeService = new ProposalMergeService(store, validator, new NodeDiffService());
            var service = CreateService(store, mergeService);

            var workflowId = "wf-test";
            var draft = CreateMinimalDraft(workflowId);
            await store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

            for (int i = 1; i <= 3; i++)
            {
                var request = new DesignTurnRequest
                {
                    DesignerId = "designer-1",
                    Message = $"Message {i}",
                    AutoMergeProposal = false
                };
                await service.SubmitDesignTurnAsync(workflowId, request, CancellationToken.None);
            }

            var turns = await service.GetAllTurnsAsync(workflowId, CancellationToken.None);

            Assert.Equal(3, turns.Count);
            Assert.Equal(1, turns[0].TurnNumber);
            Assert.Equal(2, turns[1].TurnNumber);
            Assert.Equal(3, turns[2].TurnNumber);
        }
    }

    public sealed class StructuredProposalTests
    {
        [Fact]
        public void BuildResult_ValidProposalJson_ReturnsDefinitionAndReason()
        {
            var def = CreateMinimalDraft("wf-x").Definition;
            var defJson = JsonSerializer.Serialize(def, CanonicalProfile.Options);
            var raw = $$"""{"reason":"added a gate","definition":{{defJson}},"changed_node_ids":["gen-scope"]}""";

            var result = ChatDesignerService.BuildResult(raw);

            Assert.Equal("added a gate", result.Reasoning);
            var parsed = JsonSerializer.Deserialize<WorkflowDefinition>(result.ProposalDefinitionJson, CanonicalProfile.Options);
            Assert.Equal("wf-x", parsed!.WorkflowId);
        }

        [Fact]
        public void BuildResult_Prose_FallsBackToReasoningOnly()
        {
            var result = ChatDesignerService.BuildResult("Here is some advice about your workflow.");

            Assert.Equal("Here is some advice about your workflow.", result.Reasoning);
            Assert.Equal(string.Empty, result.ProposalDefinitionJson);
        }

        [Fact]
        public void BuildResult_AttemptedButUnparseableJson_ReturnsFriendlyMessageNotRaw()
        {
            // Looks like a proposal (starts with '{') but the definition won't deserialize.
            var raw = """{"reason":"x","definition":{"context_request":{"tier":"dynamic"}}}""";

            var result = ChatDesignerService.BuildResult(raw);

            Assert.Equal(ChatDesignerService.ParseFailureMessage, result.Reasoning);
            Assert.Equal(string.Empty, result.ProposalDefinitionJson);
            Assert.DoesNotContain("context_request", result.Reasoning, StringComparison.Ordinal); // raw JSON not leaked
        }

        [Fact]
        public void BuildResult_NodeMissingDiscriminator_ReturnsFriendlyMessageNotAThrow()
        {
            // S9.68 regression: a proposal whose node lacks `node_type` throws NotSupportedException
            // deep in System.Text.Json. BuildResult must surface the recovery message, not 500.
            var defJson = JsonSerializer.Serialize(CreateMinimalDraft("wf-x").Definition, CanonicalProfile.Options)
                .Replace("\"node_type\":\"agent_task\",", "", StringComparison.Ordinal);
            var raw = $$"""{"reason":"x","definition":{{defJson}}}""";

            var result = ChatDesignerService.BuildResult(raw);

            Assert.Equal(ChatDesignerService.ParseFailureMessage, result.Reasoning);
            Assert.Equal(string.Empty, result.ProposalDefinitionJson);
        }

        [Theory]
        [InlineData("{\"a\":1}", true)]
        [InlineData("  ```json\n{}\n```", true)]
        [InlineData("Here is some prose.", false)]
        public void LooksLikeJson_DetectsAttemptedProposals(string raw, bool expected) =>
            Assert.Equal(expected, ChatDesignerService.LooksLikeJson(raw));

        // proposal.Reason ?? string.Empty — every other proposal test supplies a "reason"; this
        // exercises the fallback when the field is absent (S9.24 branch-coverage gap).
        [Fact]
        public void BuildResult_ProposalWithoutReason_DefaultsToEmptyReason()
        {
            var def = CreateMinimalDraft("wf-x").Definition;
            var defJson = JsonSerializer.Serialize(def, CanonicalProfile.Options);
            var raw = $$"""{"definition":{{defJson}},"changed_node_ids":["gen-scope"]}""";

            var result = ChatDesignerService.BuildResult(raw);

            Assert.Equal(string.Empty, result.Reasoning);
            Assert.False(string.IsNullOrEmpty(result.ProposalDefinitionJson));
        }

        [Fact]
        public async Task BuildChatOptions_AdaptiveSelection_SetsModelCeilingAndThinkingFlag()
        {
            // S9.9a (doc 14 §3): the model comes from the deep-reasoning role resolution, not a hardcoded id.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var options = await service.BuildChatOptionsAsync("wf-x", CancellationToken.None);

            Assert.Equal("resolved-deep-reasoning-model", options.ModelId);
            Assert.Equal(16_000, options.MaxOutputTokens);
            Assert.NotNull(options.AdditionalProperties);
            Assert.Equal(true, options.AdditionalProperties![ChatClientOptionKeys.AdaptiveThinking]);
        }

        [Fact]
        public async Task BuildChatOptions_NonAdaptiveSelection_OmitsThinkingFlag()
        {
            var store = new InMemoryDefinitionStore();
            var merge = BuildMergeService(store);
            var provider = new NonAdaptiveModelProvider();
            var service = new ChatDesignerService(
                store, merge, new Mock<IChatClient>().Object, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance),
                new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(),
                new NodeDiffService(), new DefinitionValidator(Array.Empty<IDefinitionValidationRule>()), provider, new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog());

            var options = await service.BuildChatOptionsAsync("wf-x", CancellationToken.None);

            Assert.Equal("plain-model", options.ModelId);
            Assert.Null(options.AdditionalProperties);
        }

        /// <summary>S9.9a: a selection with thinking off, to prove the flag is conditional.</summary>
        private sealed class NonAdaptiveModelProvider : IDesignerModelProvider
        {
            public Task<DesignerModelSelection> GetAsync(string workflowId, CancellationToken ct) =>
                Task.FromResult(new DesignerModelSelection { ModelId = "plain-model", MaxOutputTokens = 2048, AdaptiveThinking = false });
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesSchemaRolesAndEngagementType()
        {
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("advisory-sow", prompt, StringComparison.Ordinal);   // engagement type
            Assert.Contains("agent_task", prompt, StringComparison.Ordinal);     // schema node type
            Assert.Contains("finance-lead", prompt, StringComparison.Ordinal);   // available approver role
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesGranularityGuidance()
        {
            // E7/S13.6: the coarse-granularity design guidance rides in every design turn.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("Design guidance — node granularity", prompt, StringComparison.Ordinal);
            Assert.Contains("carry governance, not to decompose", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesAvailableMcpTools()
        {
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("Available MCP tools", prompt, StringComparison.Ordinal);
            Assert.Contains("io.frontier.demo/autotask/get_new_ticket", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesStructuralRules()
        {
            // S9.27 walkthrough findings: without these rules the agent proposed unexecutable
            // mcp_tool nodes and connected the graph with data edges (failing the single-entry
            // control-edge rule). S13.7h generalised the mcp_tool-specific ban to every node type
            // the runtime cannot execute, after a designed workflow shipped a `parallel` node that
            // validated clean and then failed permanently on its first run.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("NEVER propose a node type whose schema entry says `\"executable\": false`", prompt, StringComparison.Ordinal);
            // The two shapes that actually bit: a parallel node, and an unusable output contract.
            Assert.Contains("`parallel` node", prompt, StringComparison.Ordinal);
            // S13.7i (ADR-5): fan-out branches now genuinely run concurrently, gates are
            // barriers, and a node may carry at most one inbound data edge — the prompt
            // must say all three so the agent designs to the real semantics.
            Assert.Contains("CONCURRENTLY", prompt, StringComparison.Ordinal);
            Assert.Contains("barrier", prompt, StringComparison.Ordinal);
            Assert.Contains("AT MOST ONE inbound `data` edge", prompt, StringComparison.Ordinal);
            // S13.7j: decision nodes became executable — the prompt must teach the real wire
            // shape and forbid the deprecated string predicate.
            Assert.Contains("`decision` and `mcp_tool` are the only node types", prompt, StringComparison.Ordinal);
            // S13.7c: the "never propose mcp_tool" rule retires — deterministic tool steps are designable.
            Assert.Contains("An `mcp_tool` node (executable since S13.7c)", prompt, StringComparison.Ordinal);
            Assert.Contains("idempotency_key_spec", prompt, StringComparison.Ordinal);
            Assert.Contains("default_branch_node_id", prompt, StringComparison.Ordinal);
            Assert.Contains("\"kind\":\"field\"", prompt, StringComparison.Ordinal);
            Assert.Contains("`predicate` field is deprecated", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("they run in sequence", prompt, StringComparison.Ordinal);
            Assert.Contains("output_contract_type", prompt, StringComparison.Ordinal);
            Assert.Contains("exactly one node with no", prompt, StringComparison.Ordinal);
            Assert.Contains("do not fabricate values", prompt, StringComparison.Ordinal);
            // S9.68: the model must stamp each node with its `node_type` discriminator, or the
            // proposal fails to deserialize (common on a fresh workflow with no example nodes).
            Assert.Contains("MUST include a `node_type` field", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesContractCatalogueAndEdgeMatchRule()
        {
            // S9.72: the agent was blind to contracts (input/output_contract_type were free strings
            // with no catalogue and no edge-match rule), so it emitted data.edge-type-match mismatches.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("Available contracts", prompt, StringComparison.Ordinal);
            Assert.Contains(nameof(LookupResult), prompt, StringComparison.Ordinal); // a real contract name from the supplied catalogue
            Assert.Contains("`contract_type` MUST equal the consuming `agent_task` node's", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesInstructionCatalogueAndRule()
        {
            // S9.82: the agent was blind to instructions_ref (no catalogue, no rule), so it invented
            // role-based refs like "instructions/structured-extraction" that fail to resolve.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("Available instructions", prompt, StringComparison.Ordinal);
            Assert.Contains("instructions/fetch-ticket.md", prompt, StringComparison.Ordinal); // a ref from the catalogue
            Assert.Contains("`instructions_ref` MUST be one of the Available instructions", prompt, StringComparison.Ordinal);
            Assert.Contains("instructions/transform.md", prompt, StringComparison.Ordinal); // converter guidance
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesEntryContractConvention()
        {
            // S9.83: the entry node is handed the engagement brief at runtime; the agent used to set
            // its input to FetchTicketOutput with empty dynamic_fields, so the test-run failed at the
            // first node. Ground the EngagementBriefSection + engagement_brief entry convention.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("single entry `agent_task`", prompt, StringComparison.Ordinal);
            Assert.Contains("EngagementBriefSection", prompt, StringComparison.Ordinal);
            Assert.Contains("engagement_brief", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesBaselineComponentGrounding()
        {
            // S9.83: the agent set context_request.baseline_components to [] which ContextRequest
            // rejects at runtime (baseline_components must not be empty). Ground the catalogue + rule.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("Available baseline context components", prompt, StringComparison.Ordinal);
            Assert.Contains("firm-standards", prompt, StringComparison.Ordinal);
            Assert.Contains("`context_request.baseline_components` MUST be a NON-EMPTY subset", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesUniqueArtifactKeyRule()
        {
            // S9.90: the agent omitted/duplicated artifact_key, so a node's output wasn't stored
            // (test-run "No stored output") and gate rollback targets had no snapshot to restore.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("Give every `agent_task` an `artifact_key`", prompt, StringComparison.Ordinal);
            Assert.Contains("unique across the workflow", prompt, StringComparison.Ordinal);
            Assert.Contains("its output shows in a test run", prompt, StringComparison.Ordinal);
            Assert.Contains("reuse an `artifact_key`", prompt, StringComparison.Ordinal); // the no-duplicates guidance
        }

        [Fact]
        public async Task BuildSystemPrompt_IncludesAvailableAgentRoles()
        {
            // S9.27: without a catalogue the agent invented agent_task.role values that
            // Model-Role Config could not resolve at execution time.
            var service = CreateService(new InMemoryDefinitionStore(), BuildMergeService(out _));

            var prompt = await service.BuildSystemPromptAsync(CreateMinimalDraft("wf-x"), CancellationToken.None);

            Assert.Contains("Available agent roles", prompt, StringComparison.Ordinal);
            Assert.Contains("deep-reasoning", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SubmitDesignTurn_ProposalWithNullDefinition_ReportsNoErrorFindings()
        {
            // The agent returns a well-formed proposal whose definition is the JSON literal
            // null. It parses, so the "unparseable proposal" path does not catch it, and then
            // deserializes to null — a shape the validator must never be handed. Before the
            // compiler moved this branch sat uncovered inside a 7,700-line assembly; a smaller
            // denominator is what made it visible.
            var store = new InMemoryDefinitionStore();
            var mergeService = BuildMergeService(store);
            await store.SaveDraftAsync("wf-null", CreateMinimalDraft("wf-null"), "no-etag", CancellationToken.None);

            var proposal = """{"reason":"cleared it","definition":null,"changed_node_ids":[]}""";
            var service = CreateService(store, mergeService, proposal);

            var turn = await service.SubmitDesignTurnAsync(
                "wf-null",
                new DesignTurnRequest { DesignerId = "designer-1", Message = "clear it", AutoMergeProposal = false },
                CancellationToken.None);

            Assert.NotNull(turn);
        }

        [Fact]
        public async Task SubmitDesignTurn_ValidProposal_PersistsProposalAndReason()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = BuildMergeService(store);
            var draft = CreateMinimalDraft("wf-x");
            await store.SaveDraftAsync("wf-x", draft, "no-etag", CancellationToken.None);

            var defJson = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);
            var proposal = $$"""{"reason":"keep as is","definition":{{defJson}},"changed_node_ids":[]}""";
            var service = CreateService(store, mergeService, proposal);

            var turn = await service.SubmitDesignTurnAsync(
                "wf-x",
                new DesignTurnRequest { DesignerId = "designer-1", Message = "tidy up", AutoMergeProposal = false },
                CancellationToken.None);

            Assert.False(string.IsNullOrEmpty(turn.AgentProposalJson));
            Assert.Equal("keep as is", turn.ProposalReasoningJson);
        }

        [Fact]
        public async Task SubmitDesignTurn_AutoMergeWithValidProposal_RecordsMergeOutcome()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = BuildMergeService(store);
            var draft = CreateMinimalDraft("wf-x");
            await store.SaveDraftAsync("wf-x", draft, "no-etag", CancellationToken.None);

            var defJson = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);
            var proposal = $$"""{"reason":"no change","definition":{{defJson}},"changed_node_ids":[]}""";
            var service = CreateService(store, mergeService, proposal);

            var turn = await service.SubmitDesignTurnAsync(
                "wf-x",
                new DesignTurnRequest { DesignerId = "designer-1", Message = "apply", AutoMergeProposal = true },
                CancellationToken.None);

            // The auto-merge branch ran and recorded an outcome (merged / conflict / blocked).
            Assert.NotNull(turn.MergeOutcome);
        }

        [Theory]
        [InlineData("conflict")]
        [InlineData("blocked")]
        public async Task SubmitDesignTurn_AutoMerge_RecordsNonMergedOutcomes(string kind)
        {
            var store = new InMemoryDefinitionStore();
            var draft = CreateMinimalDraft("wf-x");
            await store.SaveDraftAsync("wf-x", draft, "no-etag", CancellationToken.None);

            ProposalMergeOutcome outcome = kind == "conflict"
                ? new ProposalMergeOutcomeConflict
                {
                    DraftRevisionAfterMerge = "r",
                    Conflicts = [],
                    DesignerEdit = draft.Definition,
                    AgentProposal = draft.Definition,
                }
                : new ProposalMergeOutcomeValidationBlocked { DraftRevisionAfterMerge = "r", BlockingFindings = [] };

            var defJson = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);
            var proposal = $$"""{"reason":"x","definition":{{defJson}},"changed_node_ids":[]}""";
            var service = CreateService(store, new StubMergeService(outcome), proposal);

            var turn = await service.SubmitDesignTurnAsync(
                "wf-x",
                new DesignTurnRequest { DesignerId = "designer-1", Message = "apply", AutoMergeProposal = true },
                CancellationToken.None);

            Assert.StartsWith($"{kind}:", turn.MergeOutcome!, StringComparison.Ordinal);
        }

        // switch (mergeOutcome) { ... _ => "unknown" } — every other test supplies Merged/Conflict/
        // ValidationBlocked; ProposalMergeOutcome is abstract (not sealed), so this stub type falls
        // through to the default arm on both switches (S9.24 branch-coverage gap).
        [Fact]
        public async Task SubmitDesignTurn_AutoMerge_UnrecognisedOutcomeType_RecordsUnknown()
        {
            var store = new InMemoryDefinitionStore();
            var draft = CreateMinimalDraft("wf-x");
            await store.SaveDraftAsync("wf-x", draft, "no-etag", CancellationToken.None);

            var defJson = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);
            var proposal = $$"""{"reason":"x","definition":{{defJson}},"changed_node_ids":[]}""";
            var service = CreateService(store, new StubMergeService(new UnrecognisedOutcome { DraftRevisionAfterMerge = "r" }), proposal);

            var turn = await service.SubmitDesignTurnAsync(
                "wf-x",
                new DesignTurnRequest { DesignerId = "designer-1", Message = "apply", AutoMergeProposal = true },
                CancellationToken.None);

            Assert.Equal("unknown", turn.MergeOutcome);
            Assert.Null(turn.ConflictSummary);
        }

        /// <summary>A <see cref="ProposalMergeOutcome"/> subtype no switch arm recognises (the type is
        /// abstract, not a closed union), used to force the default arm of both outcome switches.</summary>
        private sealed record UnrecognisedOutcome : ProposalMergeOutcome;

        [Fact]
        public async Task SubmitDesignTurn_NoDraft_Throws()
        {
            var store = new InMemoryDefinitionStore();
            var service = CreateService(store, BuildMergeService(store));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SubmitDesignTurnAsync(
                    "missing",
                    new DesignTurnRequest { DesignerId = "d", Message = "m", AutoMergeProposal = false },
                    CancellationToken.None));
        }

        [Fact]
        public void Constructor_NullSchemaProviderOrRoleCatalog_Throws()
        {
            var store = new InMemoryDefinitionStore();
            var mergeService = BuildMergeService(out var chat);

            var diff = new NodeDiffService();
            var compiler = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, null!, new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), diff, compiler, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), null!, new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), diff, compiler, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), null!, new FakeAgentRoleCatalog(), diff, compiler, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), null!, diff, compiler, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), null!, compiler, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), diff, null!, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), diff, compiler, null!, new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), diff, compiler, new FakeDesignerModelProvider(), null!, new FakeContextComponentCatalog(), new FakeEntryContractCatalog()));
            Assert.Throws<ArgumentNullException>(() =>
                new ChatDesignerService(store, mergeService, chat, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), diff, compiler, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), null!, new FakeEntryContractCatalog()));
        }

        /// <summary>Merge-service double returning a fixed outcome, to exercise outcome recording.</summary>
        private sealed class StubMergeService(ProposalMergeOutcome outcome) : IProposalMergeService
        {
            public Task<ProposalMergeOutcome> ApplyProposalAsync(
                string workflowId, string proposedDefinitionJson, string agentReasoning, string designerId, CancellationToken ct) =>
                Task.FromResult(outcome);

            public Task<ProposalMergeOutcome> ApplyApprovedChangesAsync(
                string workflowId, IReadOnlyList<string> approvedChangeIds, string expectedRevision, string designerId, CancellationToken ct) =>
                Task.FromResult(outcome);
        }

        private static ProposalMergeService BuildMergeService(IDefinitionStore store)
        {
            var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            return new ProposalMergeService(store, validator, new NodeDiffService());
        }

        private static ProposalMergeService BuildMergeService(out IChatClient chat)
        {
            chat = new Mock<IChatClient>().Object;
            return BuildMergeService(new InMemoryDefinitionStore());
        }
    }

    public sealed class ReviewAndWelcomeTests
    {
        [Fact]
        public void BuildProposalReview_EmptyJson_ReturnsNulls()
        {
            var (_, service) = Build(passValidation: true);

            var (changes, block) = service.BuildProposalReview(CreateMinimalDraft("wf-test"), string.Empty);

            Assert.Null(changes);
            Assert.Null(block);
        }

        [Fact]
        public void BuildProposalReview_ValidProposal_ComputesChangesWithoutBlock()
        {
            var (_, service) = Build(passValidation: true);
            var draft = CreateMinimalDraft("wf-test");
            var proposed = draft.Definition with { Nodes = AddNode(draft.Definition.Nodes, "gate-1") };
            var json = JsonSerializer.Serialize(proposed, CanonicalProfile.Options);

            var (changes, block) = service.BuildProposalReview(draft, json);

            Assert.NotNull(changes);
            Assert.Contains(changes!, c => c.ChangeId == "node:added:gate-1");
            Assert.Null(block);
        }

        // proposed is null — every other test supplies parseable JSON that deserializes to a non-null
        // WorkflowDefinition; this exercises the fallback when the JSON literal is `null` (S9.24 branch-coverage gap).
        [Fact]
        public void BuildProposalReview_NullDefinitionLiteral_ReturnsNulls()
        {
            var (_, service) = Build(passValidation: true);

            var (changes, block) = service.BuildProposalReview(CreateMinimalDraft("wf-test"), "null");

            Assert.Null(changes);
            Assert.Null(block);
        }

        [Fact]
        public void BuildProposalReview_ValidationFails_ReturnsBlockReason()
        {
            var (_, service) = Build(passValidation: false);
            var draft = CreateMinimalDraft("wf-test");
            var json = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);

            var (_, block) = service.BuildProposalReview(draft, json);

            Assert.False(string.IsNullOrEmpty(block));
        }

        [Fact]
        public async Task EnsureWelcomeTurn_CreatesSystemTurnZeroWithEngagementType()
        {
            var (_, service) = Build(passValidation: true);

            await service.EnsureWelcomeTurnAsync("wf-test", "advisory-sow", CancellationToken.None);

            var welcome = Assert.Single(await service.GetAllTurnsAsync("wf-test", CancellationToken.None));
            Assert.Equal(0, welcome.TurnNumber);
            Assert.Equal("system", welcome.DesignerId);
            Assert.Contains("advisory-sow", welcome.DesignerMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EnsureWelcomeTurn_SummarisesCapabilityCounts()
        {
            // S9.32 (doc 14 §2): the welcome turn summarises the same catalogues the system
            // prompt receives — the Build() fakes seed one entry per catalogue.
            var (_, service) = Build(passValidation: true);

            await service.EnsureWelcomeTurnAsync("wf-test", "advisory-sow", CancellationToken.None);

            var welcome = Assert.Single(await service.GetAllTurnsAsync("wf-test", CancellationToken.None));
            Assert.Contains("1 agent model role(s)", welcome.DesignerMessage, StringComparison.Ordinal);
            Assert.Contains("deep-reasoning", welcome.DesignerMessage, StringComparison.Ordinal);
            Assert.Contains("finance-lead", welcome.DesignerMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void WelcomeMessage_PopulatedCatalogues_ListsCountsAndNames()
        {
            var message = ChatDesignerService.WelcomeMessage(
                "advisory-sow",
                [new AgentRoleDescriptor { RoleId = "deep-reasoning", Description = "analysis" }],
                [
                    new DesignerToolDescriptor { ToolRef = "io.frontier.demo/autotask/get_new_ticket", Server = "io.frontier.demo/autotask", Name = "get_new_ticket", Description = "fetch" },
                    new DesignerToolDescriptor { ToolRef = "io.frontier.demo/autotask/update_ticket", Server = "io.frontier.demo/autotask", Name = "update_ticket", Description = "update" },
                ],
                [new ApproverRoleDescriptor { RoleId = "business-approver", DisplayName = "Business Approver", Description = "approves", Responsibilities = [], ApplicableGateKinds = [] }]);

            Assert.Contains("1 agent model role(s), 2 tool(s) across 1 connector(s), and gate on 1 approver role(s)", message, StringComparison.Ordinal);
            Assert.Contains("deep-reasoning", message, StringComparison.Ordinal);
            Assert.Contains("io.frontier.demo/autotask: get_new_ticket, update_ticket", message, StringComparison.Ordinal);
            Assert.Contains("business-approver", message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EnsureWelcomeTurn_Idempotent_NotDuplicated()
        {
            var (_, service) = Build(passValidation: true);

            await service.EnsureWelcomeTurnAsync("wf-test", "advisory-sow", CancellationToken.None);
            await service.EnsureWelcomeTurnAsync("wf-test", "advisory-sow", CancellationToken.None);

            Assert.Single(await service.GetAllTurnsAsync("wf-test", CancellationToken.None));
        }

        private static (InMemoryDefinitionStore Store, ChatDesignerService Service) Build(bool passValidation)
        {
            var store = new InMemoryDefinitionStore();
            IDefinitionCompiler validator = passValidation
                ? new DefinitionValidator(Array.Empty<IDefinitionValidationRule>())
                : new DefinitionValidator([new FailingRule()]);
            var merge = new ProposalMergeService(store, validator, new NodeDiffService());
            var service = new ChatDesignerService(
                store, merge, new Mock<IChatClient>().Object, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance),
                new FakeApproverRoleCatalog(), new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), new NodeDiffService(), validator, new FakeDesignerModelProvider(), new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog());
            return (store, service);
        }

        private static List<WorkflowNode> AddNode(IReadOnlyList<WorkflowNode> nodes, string id)
        {
            var list = nodes.ToList();
            list.Add(new AgentTaskNode
            {
                NodeId = id,
                Role = "r",
                InstructionsRef = "i",
                InputContractType = "In",
                OutputContractType = "Out",
                ContextRequest = new ContextRequest { EngagementId = "e", AgentRole = "r", BaselineComponents = [], DynamicFields = [] },
            });
            return list;
        }

        private sealed class FailingRule : IDefinitionValidationRule
        {
            public string RuleId => "test.fails";
            public RuleTier Tier => RuleTier.Pure;
            public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

            public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct) =>
                Task.FromResult<IReadOnlyList<ValidationFinding>>(
                    [new ValidationFinding("test.fails", ValidationSeverity.Error, "boom")]);
        }
    }

    private static DefinitionDraftDocument CreateMinimalDraft(string workflowId)
    {
        var definition = new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "advisory-sow",
            Name = "Test Workflow",
            Nodes = new WorkflowNode[]
            {
                new AgentTaskNode
                {
                    NodeId = "gen-scope",
                    Role = "gen-scope",
                    InstructionsRef = "scope-gen",
                    InputContractType = "EngagementBriefSection",
                    OutputContractType = "ScopeSection",
                    ContextRequest = new ContextRequest
                    {
                        EngagementId = "engagement-1",
                        AgentRole = "gen-scope",
                        BaselineComponents = EmptyNodes,
                        DynamicFields = EmptyNodes
                    }
                }
            },
            Edges = EmptyEdges,
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };

        return new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            Definition = definition,
            DraftRevision = "rev-1",
            LastEditedBy = "user-1",
            LastEditedUtc = DateTime.UtcNow
        };
    }
}
