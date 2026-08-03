using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Registers the canonical <see cref="JsonSerializerOptions"/> profile (doc 01 ADR-C1):
/// snake_case property names, nulls omitted, and smart enums (TD-11) serialized as
/// their canonical string via <see cref="SmartEnumJsonConverterFactory"/>. Every
/// library follows this <c>AddFrontierXxx()</c> pattern (doc 00 §9) — only Host calls it.
/// </summary>
public static class SerializationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the shared, canonical <see cref="JsonSerializerOptions"/> singleton and the
    /// <see cref="CanonicalProfileCheck"/> boot invariant (doc 12 §6).
    /// </summary>
    public static IServiceCollection AddFrontierSerialization(this IServiceCollection services) =>
        services
            .AddSingleton(CanonicalProfile.Options)
            .AddSingleton<IStartupCheck, CanonicalProfileCheck>();
}
