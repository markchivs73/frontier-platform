using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.AI;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Builds the JSON-Schema response format <see cref="MafAgentInvoker"/> sends with every
/// structured-output request (S9.28). <see cref="ChatResponseFormat.ForJsonSchema{T}"/>
/// alone is not enough: <see cref="System.Text.Json.Schema.JsonSchemaExporter"/> cannot
/// introspect custom converters, so every property whose type is handled by one of
/// <see cref="CanonicalProfile"/>'s converters (fixed-precision decimals, ISO-8601
/// <see cref="DateTime"/>s, smart enums) came out as the boolean accept-anything schema
/// <c>true</c> — which the Anthropic API rejects with <c>invalid_request_error</c>
/// ("For 'object' type, property 'properties' is not supported"). Found live in S9.28's
/// execution proof: <c>MatchDeveloperOutput.Score</c> (the first <see cref="decimal"/> on
/// any LLM-produced contract) produced <c>"score": true</c> and failed every
/// <c>match_developer</c> invocation; <c>ApproachSection</c>'s decimal means the advisory
/// SOW workflow had the same latent failure. The <see cref="TransformNode"/> hook rewrites
/// each converter-opaque node into the schema of its canonical wire form.
/// </summary>
internal static class CanonicalOutputSchema
{
    private static readonly ConcurrentDictionary<Type, ChatResponseFormat> Cache = new();

    /// <summary>The response format for <typeparamref name="TOutput"/>, cached per type (schema generation reflects over the whole type graph).</summary>
    internal static ChatResponseFormat For<TOutput>() => For(typeof(TOutput));

    /// <summary>Non-generic <see cref="For{TOutput}"/>, keyed for the per-type cache.</summary>
    /// <exception cref="NotSupportedException">
    /// For <see cref="Frontier.Platform.Workflow.Model.TypedPayload"/>: its free-form
    /// <c>payload</c>/<c>facts</c> cannot be honestly schematised from the CLR type — the
    /// real schema is the capability-declared <c>schema_ref</c>, which agent output binding
    /// consumes only under the ADR-AG1 schema-validated variant (ADR-E2 deferral (c),
    /// E4/E13). Refusing here fails fast at invocation with the design reference, rather
    /// than live at the model API with an opaque 400.
    /// </exception>
    internal static ChatResponseFormat For(Type outputType)
    {
        if (outputType == typeof(TypedPayload))
        {
            throw new NotSupportedException(
                "TypedPayload is not an agent output contract yet: its free-form payload is schematised from its " +
                "schema_ref under the ADR-AG1 schema-validated variant (ADR-E2 deferral (c), arrives with E4/E13). " +
                "Declare the concrete output contract instead.");
        }

        return Cache.GetOrAdd(outputType, static type => ChatResponseFormat.ForJsonSchema(
            AIJsonUtilities.CreateJsonSchema(
                type,
                serializerOptions: CanonicalProfile.Options,
                inferenceOptions: new AIJsonSchemaCreateOptions { TransformSchemaNode = TransformNode }),
            schemaName: type.Name));
    }

    /// <summary>
    /// Rewrites converter-opaque boolean schema nodes into their canonical wire-form
    /// schema, and gives every dictionary-shaped node (e.g. <c>PricingSection.UnitRates</c>,
    /// an <c>IReadOnlyDictionary&lt;string, decimal&gt;</c>) an explicit
    /// <c>additionalProperties</c> schema for its value type — <see
    /// cref="System.Text.Json.Schema.JsonSchemaExporter"/> emits a bare
    /// <c>{"type":"object"}</c> for a dictionary whose value type has a custom converter
    /// (the same opacity as a scalar property, just silently dropping
    /// <c>additionalProperties</c> instead of surfacing as boolean <c>true</c>), and
    /// Anthropic rejects an object schema with no <c>additionalProperties</c> at all
    /// (<c>"'additionalProperties' must be explicitly set to false"</c> — found live
    /// running <c>PricingSection</c> through <c>AuditGateTests</c>, the first
    /// dictionary-shaped contract property any live gate test exercised). Every other
    /// node passes through unchanged. A boolean node for a scalar type this method does
    /// not recognise is left as-is — <c>CanonicalOutputSchemaTests</c>' sweep over every
    /// contract type fails on any that remain, so an unhandled converter is a PR-time test
    /// failure, not a production Anthropic 400.
    /// </summary>
    internal static JsonNode TransformNode(AIJsonSchemaCreateContext context, JsonNode node)
    {
        if (context.TypeInfo.Kind == JsonTypeInfoKind.Dictionary && context.TypeInfo.ElementType is { } elementType)
        {
            return WithAdditionalProperties(node, elementType, context);
        }

        if (node.GetValueKind() is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
        {
            return node;
        }

        // Nullable<T> properties (e.g. DateTime? EnrichedAtUtc) opaque the same way as T:
        // STJ wraps the underlying converter, so schematize the underlying type.
        return SchemaForType(Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type, context) ?? node;
    }

    /// <summary>Adds an explicit <c>additionalProperties</c> schema for <paramref name="valueType"/> to <paramref name="node"/> (which may already be a partial object schema, or fully opaque boolean <c>true</c>).</summary>
    internal static JsonObject WithAdditionalProperties(JsonNode node, Type valueType, AIJsonSchemaCreateContext context)
    {
        var valueSchema = SchemaForType(Nullable.GetUnderlyingType(valueType) ?? valueType, context) ?? new JsonObject { ["type"] = "string" };
        var dictionarySchema = node as JsonObject ?? new JsonObject { ["type"] = "object" };
        dictionarySchema["additionalProperties"] = valueSchema;
        return dictionarySchema;
    }

    /// <summary>The canonical wire-form schema for a converter-opaque <paramref name="type"/>, or <see langword="null"/> if this method doesn't recognise it.</summary>
    internal static JsonObject? SchemaForType(Type type, AIJsonSchemaCreateContext context)
    {
        if (type == typeof(decimal))
        {
            return DecimalSchema(context.GetCustomAttribute<DecimalPrecisionAttribute>()?.Scale ?? 4);
        }

        if (type == typeof(DateTime))
        {
            return DateTimeSchema();
        }

        return IsSmartEnum(type) ? SmartEnumSchema(type) : null;
    }

    /// <summary>Schema of <see cref="FixedPrecisionDecimalConverter"/>'s wire form: a string with exactly <paramref name="scale"/> decimal places.</summary>
    internal static JsonObject DecimalSchema(int scale) => new()
    {
        ["type"] = "string",
        ["pattern"] = string.Create(CultureInfo.InvariantCulture, $"^-?[0-9]+\\.[0-9]{{{scale}}}$"),
        ["description"] = string.Create(CultureInfo.InvariantCulture, $"Fixed-precision decimal encoded as a string with exactly {scale} decimal places, e.g. \"{1.25m.ToString("F" + scale, CultureInfo.InvariantCulture)}\""),
    };

    /// <summary>Schema of <see cref="Iso8601UtcDateTimeConverter"/>'s wire form.</summary>
    internal static JsonObject DateTimeSchema() => new()
    {
        ["type"] = "string",
        ["description"] = "ISO-8601 UTC timestamp with milliseconds, e.g. \"2026-01-08T12:00:00.000Z\"",
    };

    /// <summary>Duck-type check matching <see cref="SmartEnumJsonConverterFactory.CanConvert"/> exactly.</summary>
    internal static bool IsSmartEnum(Type type) =>
        type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string)
        && type.GetMethod("FromName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(string)]) is not null;

    /// <summary>Schema of <see cref="SmartEnumJsonConverter{TEnum}"/>'s wire form: the declared values' canonical snake_case names.</summary>
    internal static JsonObject SmartEnumSchema(Type type)
    {
        var values = (IEnumerable)type.GetProperty("List", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        var nameProperty = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)!;

        var names = new JsonArray();
        foreach (var value in values)
        {
            names.Add((string)nameProperty.GetValue(value)!);
        }

        return new JsonObject { ["type"] = "string", ["enum"] = names };
    }
}
