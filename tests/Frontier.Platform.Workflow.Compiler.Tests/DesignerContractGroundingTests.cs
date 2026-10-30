using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;
using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;

using Microsoft.Extensions.AI;

using Moq;

namespace Frontier.Platform.Workflow.Compiler.Tests;

#pragma warning disable CA1034, CA1515 // xunit discovers nested public test classes, as in ChatDesignerServiceTests

/// <summary>
/// ADR-PA41 (consumer register S5-1, S5-2): the design agent reads the same contract annotations the
/// designer's contract panel shows, marks a tool that returns any JSON, and marks a discovered
/// contract's gaps.
/// </summary>
public sealed class DesignerContractGroundingTests
{
    internal const string InvoiceRef = "acme/invoice/1.0";
    internal const string TicketRef = "io.frontier.demo.autotask/get-ticket-output/1.0";

    /// <summary>An authored contract: described, with an example and two rules.</summary>
    internal static ContractSnapshot Invoice { get; } = Snapshot(InvoiceRef, """
        {
          "description": "An invoice raised against one engagement. Not for credit notes.",
          "type": "object",
          "properties": {
            "total": { "description": "The invoice total, as a canonical decimal string.", "type": "string" },
            "lines": {
              "description": "One entry per billed item.",
              "type": "array",
              "items": { "type": "object", "properties": { "sku": { "description": "The item's stock code.", "type": "string" } }, "additionalProperties": false }
            }
          },
          "additionalProperties": false,
          "examples": [ { "total": "10.00", "lines": [ { "sku": "A1" } ] } ]
        }
        """,
        new ContractRule { Rule = "self.total != ''", Message = "The total must not be empty.", FieldPath = "/total" },
        new ContractRule { Rule = "size(self.lines) > 0", Message = "An invoice bills at least one item." });

    /// <summary>A discovered contract, as an MCP server supplied it: no descriptions, no examples, open.</summary>
    internal static ContractSnapshot Ticket { get; } = Snapshot(TicketRef, """
        { "type": "object", "properties": { "id": { "type": "string" } } }
        """,
        new ContractRule { Rule = "self.id != ''", Message = "" });

    private static ContractSnapshot Snapshot(string contractRef, string schema, params ContractRule[] rules)
    {
        using var document = JsonDocument.Parse(schema);
        var element = document.RootElement.Clone();
        return new ContractSnapshot { ContractRef = contractRef, Schema = element, Rules = rules, Hash = ContractHash.Compute(element, rules) };
    }

    public sealed class RenderTests
    {
        [Fact]
        public void Render_MatchesTheGoldenBlock()
        {
            // The text the agent reads. A change here changes every design turn's prompt, so it is
            // reviewed as a golden diff, never regenerated to make the test pass.
            var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "designer_contract_annotations.txt"));

            var rendered = DesignerContractAnnotations.Render(DesignerContractAnnotations.Ordered([Ticket, Invoice]));

            Assert.Equal(golden.ReplaceLineEndings("\n").TrimEnd('\n'), rendered);
        }

        [Fact]
        public void Render_ALegibleContract_StatesEveryAnnotation_AndNoGaps()
        {
            var entry = DesignerContractAnnotations.Render(Invoice);

            Assert.Contains("- acme/invoice/1.0", entry, StringComparison.Ordinal);
            Assert.Contains("Description: An invoice raised against one engagement. Not for credit notes.", entry, StringComparison.Ordinal);
            Assert.Contains("/schema/properties/total: The invoice total, as a canonical decimal string.", entry, StringComparison.Ordinal);
            Assert.Contains("/schema/properties/lines/items/properties/sku: The item's stock code.", entry, StringComparison.Ordinal);
            Assert.Contains("""{"total":"10.00","lines":[{"sku":"A1"}]}""", entry, StringComparison.Ordinal);
            Assert.Contains("/total: The total must not be empty.", entry, StringComparison.Ordinal);
            Assert.Contains("(whole payload): An invoice bills at least one item.", entry, StringComparison.Ordinal);
            Assert.DoesNotContain("Annotation gaps", entry, StringComparison.Ordinal);
        }

        [Fact]
        public void Render_ADiscoveredContract_MarksEachGapTheLintFinds()
        {
            var entry = DesignerContractAnnotations.Render(Ticket);

            Assert.Contains("Description: (none)", entry, StringComparison.Ordinal);
            Assert.Contains("/schema/properties/id: (no description)", entry, StringComparison.Ordinal);
            Assert.Contains("  Examples:\n    (none)", entry, StringComparison.Ordinal);
            Assert.Contains("(whole payload): (no message)", entry, StringComparison.Ordinal);
            Assert.Contains("Annotation gaps", entry, StringComparison.Ordinal);
            foreach (var gap in ContractLegibility.Gaps(Ticket))
            {
                Assert.Contains($"    {gap.Location}: {gap.Message}", entry, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Render_AContractWithNoPropertiesOrRules_SaysNone()
        {
            var entry = DesignerContractAnnotations.Render(Snapshot("acme/flag/1.0", """{ "description": "A flag.", "type": "boolean", "examples": [true] }"""));

            Assert.Contains("  Properties:\n    (none)", entry, StringComparison.Ordinal);
            Assert.Contains("  Examples:\n    true", entry, StringComparison.Ordinal);
            Assert.Contains("  Rules:\n    (none)", entry, StringComparison.Ordinal);
        }

        [Fact]
        public void Render_NoContracts_SaysSo() =>
            Assert.Equal(DesignerContractAnnotations.NoContracts, DesignerContractAnnotations.Render([]));

        [Fact]
        public void Ordered_SortsByRefOrdinally_AndKeepsTheFirstEntryForARef()
        {
            var shadow = Snapshot(InvoiceRef, """{ "description": "A second copy." }""");

            var ordered = DesignerContractAnnotations.Ordered([Ticket, Invoice, shadow]);

            Assert.Equal([InvoiceRef, TicketRef], ordered.Select(c => c.ContractRef));
            Assert.Same(Invoice, ordered[0]);
        }

        [Theory]
        [InlineData("""{ "examples": "not a list" }""")]
        [InlineData("""{ "type": "string" }""")]
        [InlineData("true")]
        public void Examples_NoExamplesArray_YieldsNone(string schema)
        {
            using var document = JsonDocument.Parse(schema);

            Assert.Empty(DesignerContractAnnotations.Examples(document.RootElement));
        }

        [Fact]
        public void AnyJsonTools_ListsEachToolWithNoOutputContract_OrNone()
        {
            DesignerToolDescriptor[] tools =
            [
                Tool("io.frontier.demo/autotask/get_ticket", TicketRef),
                Tool("io.frontier.demo/autotask/search", null),
                Tool("io.frontier.demo/autotask/close", null),
            ];

            Assert.Equal("io.frontier.demo/autotask/search, io.frontier.demo/autotask/close", DesignerContractAnnotations.AnyJsonTools(tools));
            Assert.Equal(DesignerContractAnnotations.None, DesignerContractAnnotations.AnyJsonTools([tools[0]]));
        }
    }

    public sealed class PromptTests
    {
        [Fact]
        public async Task BuildSystemPrompt_CarriesEachRegistryContractsAnnotations()
        {
            var prompt = await PromptAsync(new ChatDesignerServiceTests.FakeDesignerToolCatalog(), Invoice, Ticket);

            Assert.Contains("Registry contracts — what each registry ref above", prompt, StringComparison.Ordinal);
            Assert.Contains(DesignerContractAnnotations.Render(DesignerContractAnnotations.Ordered([Invoice, Ticket])), prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_OffersRegistryRefsBesideTheClrNames()
        {
            var prompt = await PromptAsync(new ChatDesignerServiceTests.FakeDesignerToolCatalog(), Ticket, Invoice);

            Assert.Contains($", {InvoiceRef}, {TicketRef}\n", prompt, StringComparison.Ordinal);
            Assert.Contains(nameof(LookupResult), prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_NoRegistryContracts_SaysSo()
        {
            var prompt = await PromptAsync(new ChatDesignerServiceTests.FakeDesignerToolCatalog());

            Assert.Contains(DesignerContractAnnotations.NoContracts, prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_MarksAToolWithNoOutputContract_AsAnyJson()
        {
            var prompt = await PromptAsync(new ToolCatalog(Tool("io.frontier.demo/autotask/get_ticket", TicketRef), Tool("io.frontier.demo/autotask/search", null)));

            Assert.Contains("return any JSON: io.frontier.demo/autotask/search\n", prompt, StringComparison.Ordinal);
            Assert.Contains("no `output_contract_ref` returns any JSON", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_EveryToolHasAnOutputContract_ListsNoneAsAnyJson()
        {
            var prompt = await PromptAsync(new ToolCatalog(Tool("io.frontier.demo/autotask/get_ticket", TicketRef)));

            Assert.Contains("return any JSON: (none)\n", prompt, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildSystemPrompt_SaysWhatARolesAndAToolsContractRefsMean()
        {
            var prompt = await PromptAsync(new ToolCatalog(Tool("io.frontier.demo/autotask/get_ticket", TicketRef)));

            Assert.Contains("`model-role.contracts-match`", prompt, StringComparison.Ordinal);
            Assert.Contains("The designer copies", prompt, StringComparison.Ordinal);
            Assert.Contains("you need not author them", prompt, StringComparison.Ordinal);
            Assert.Contains($"\"output_contract_ref\":\"{TicketRef}\"", prompt, StringComparison.Ordinal);
        }

        private static async Task<string> PromptAsync(IDesignerToolCatalog tools, params ContractSnapshot[] contracts)
        {
            var store = new InMemoryDefinitionStore();
            var compiler = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
            var service = new ChatDesignerService(store, new ProposalMergeService(store, compiler, new NodeDiffService()), Mock.Of<IChatClient>(),
                new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance),
                new ChatDesignerServiceTests.FakeApproverRoleCatalog(), tools, new ChatDesignerServiceTests.FakeAgentRoleCatalog(),
                new NodeDiffService(), compiler, new ChatDesignerServiceTests.FakeDesignerModelProvider(), new ChatDesignerServiceTests.FakeInstructionCatalog(),
                new ChatDesignerServiceTests.FakeContextComponentCatalog(), new ChatDesignerServiceTests.FakeEntryContractCatalog(),
                new ChatDesignerServiceTests.FakeContractSnapshotCatalog(contracts));
            return await service.BuildSystemPromptAsync(ChatDesignerServiceTests.CreateMinimalDraft("wf-x"), CancellationToken.None);
        }
    }

    private static DesignerToolDescriptor Tool(string toolRef, string? outputRef) => new()
    {
        ToolRef = toolRef,
        Server = "io.frontier.demo/autotask",
        Name = toolRef[(toolRef.LastIndexOf('/') + 1)..],
        Description = "A demo tool.",
        OutputContractRef = outputRef,
    };

    private sealed class ToolCatalog(params DesignerToolDescriptor[] tools) : IDesignerToolCatalog
    {
        public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>(tools);
    }
}
