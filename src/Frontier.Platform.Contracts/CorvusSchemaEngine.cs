using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using JsonSchemaResultsCollector = Corvus.Text.Json.JsonSchemaResultsCollector;
using JsonSchemaResultsLevel = Corvus.Text.Json.JsonSchemaResultsLevel;

using Microsoft.Extensions.Options;

using CorvusSchema = Corvus.Text.Json.Validator.JsonSchema;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The Corvus.Text.Json.Validator adapter (TD-18), configured as ADR-E27 D3 requires: file and HTTP
/// resolution off, <c>format</c> as an annotation, 2020-12 as the default dialect, and the regex
/// timeout from <see cref="ContractsOptions"/>. Corvus caches compiled schemas process-wide by URI,
/// so each one is compiled under a URI made from its contract hash and the cache is refreshed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "SDK adapter glue; every behaviour is exercised through ContractValidator and ContractRegistrationCheck tests against the real engine.")]
internal sealed class CorvusSchemaEngine(IOptions<ContractsOptions> options) : ISchemaEngine
{
    private readonly CorvusSchema.Options _options = new(
        additionalSchemaFiles: [],
        allowFileSystemAndHttpResolution: false,
        defaultDialect: CorvusSchema.Options.Default.DefaultDialect,
        alwaysAssertFormat: false,
        additionalDocumentResolver: null,
        codeGeneration: default,
        regexMatchTimeout: TimeSpan.FromMilliseconds(options.Value.RegexMatchTimeoutMilliseconds));

    private readonly Lazy<CorvusSchema> _metaSchema = new(() => CorvusSchema.From(SchemaReferences.Dialect2020_12, new CorvusSchema.Options(
        additionalSchemaFiles: [],
        allowFileSystemAndHttpResolution: false,
        defaultDialect: CorvusSchema.Options.Default.DefaultDialect,
        alwaysAssertFormat: false,
        additionalDocumentResolver: null,
        codeGeneration: default,
        regexMatchTimeout: TimeSpan.FromMilliseconds(options.Value.RegexMatchTimeoutMilliseconds))));

    /// <inheritdoc />
    public IReadOnlyList<ContractViolation> MetaSchemaViolations(System.Text.Json.JsonElement schema, int maxViolations) =>
        new Compiled(_metaSchema.Value).Validate(schema.GetRawText(), maxViolations);

    /// <inheritdoc />
    public bool TryCompile(string hash, System.Text.Json.JsonElement schema, [NotNullWhen(true)] out ICompiledSchema? compiled, [NotNullWhen(false)] out string? error)
    {
        try
        {
            compiled = new Compiled(CorvusSchema.FromText(schema.GetRawText(), "urn:frontier:contract:" + hash, _options, refreshCache: true));
            error = null;
            return true;
        }
#pragma warning disable CA1031 // Corvus reports an uncompilable schema through several exception types; each is the same refusal.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            compiled = null;
            error = ex.Message;
            return false;
        }
    }

    private sealed class Compiled(CorvusSchema schema) : ICompiledSchema
    {
        public IReadOnlyList<ContractViolation> Validate(string json, int maxViolations)
        {
            using var collector = JsonSchemaResultsCollector.Create(JsonSchemaResultsLevel.Detailed);
            try
            {
                return schema.Validate(json, collector) ? [] : SchemaViolations.From(Failures(collector), maxViolations);
            }
            catch (RegexMatchTimeoutException)
            {
                return [SchemaViolations.PatternTimeout];
            }
        }

        private static List<SchemaFailure> Failures(JsonSchemaResultsCollector collector)
        {
            var failures = new List<SchemaFailure>();
            foreach (var result in collector.EnumerateResults())
            {
                if (!result.IsMatch)
                {
                    failures.Add(new SchemaFailure(result.GetDocumentEvaluationLocationText(), result.GetEvaluationLocationText(), result.GetMessageText()));
                }
            }

            return failures;
        }
    }
}
