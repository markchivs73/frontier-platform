using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Microsoft.Extensions.Options;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>The real Corvus engine with default options, and a counting wrapper over it.</summary>
internal static class Engine
{
    internal static IOptions<ContractsOptions> Options(Action<ContractsOptions>? configure = null)
    {
        var options = new ContractsOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    internal static CorvusSchemaEngine Real() => new(Options());
}

/// <summary>Counts compilations and can be told to refuse every schema.</summary>
internal sealed class CountingEngine(ISchemaEngine inner, string? refuseWith = null) : ISchemaEngine
{
    public int Compilations { get; private set; }

    public IReadOnlyList<ContractViolation> MetaSchemaViolations(JsonElement schema, int maxViolations) => inner.MetaSchemaViolations(schema, maxViolations);

    public bool TryCompile(string hash, JsonElement schema, [NotNullWhen(true)] out ICompiledSchema? compiled, [NotNullWhen(false)] out string? error)
    {
        Compilations++;
        if (refuseWith is not null)
        {
            compiled = null;
            error = refuseWith;
            return false;
        }

        return inner.TryCompile(hash, schema, out compiled, out error);
    }
}
