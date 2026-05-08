using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Forge.Entity;
using Forge.Operations.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.Operations.Http.DependencyInjection;

/// <summary>
/// DI registration helpers for <c>Forge.Operations.Http</c>.
/// See Operations.Http ADR-0001 and ADR-0002.
/// </summary>
public static class OperationEndpointsHttpServiceCollectionExtensions
{
    /// <summary>
    /// The name of the HTTP request header from which the operation-aspect IRI is read.
    /// </summary>
    public const string AspectIriHeader = "X-Forge-Operation-AspectIri";

    /// <summary>
    /// Scans <paramref name="assemblies"/> for types carrying both <c>[Entity]</c> and
    /// <c>[OperationEndpoints]</c> and registers an <see cref="OperationEndpointDescriptor"/>
    /// per entity. Call <c>MapOperations()</c> after <c>app.Build()</c> to wire up the endpoints.
    /// <para>
    /// The aspect IRI is read from the <c>X-Forge-Operation-AspectIri</c> header directly
    /// inside <c>MapOperations()</c> — not via the shared <c>IExecutionAspectIriProvider</c>
    /// DI slot — so this call is safe to combine with <c>AddCapabilityHttp()</c>. See ADR-0002.
    /// </para>
    /// </summary>
    public static IServiceCollection AddOperationEndpointsHttp(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register JSON converters for EntityRef<T> and EntityRefCollection<T> so that
        // GET/LIST endpoints serialize owned-relation IRIs correctly. See ADR-0001.
        // Also register a TypeInfoResolver modifier that suppresses unresolved (lazy) inverse
        // collection properties from HTTP responses (key is omitted entirely). See ADR-0018.
        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.Converters.Add(new EntityRefJsonConverterFactory());
            o.SerializerOptions.Converters.Add(new EntityRefCollectionJsonConverterFactory());
            o.SerializerOptions.TypeInfoResolver =
                (o.SerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                .WithAddedModifier(SuppressUnresolvedEntityRefCollections);
        });

        foreach (var assembly in assemblies)
        {
            foreach (var entityType in assembly.GetTypes())
            {
                var entityAttr = entityType.GetCustomAttribute<EntityAttribute>();
                if (entityAttr is null) continue;

                var opAttr = entityType.GetCustomAttribute<OperationEndpointsAttribute>();
                if (opAttr is null) continue;

                // IdentityAttribute has Inherited = false so GetCustomAttribute(inherit: true)
                // does not traverse the base-type chain. Walk manually so that entity subtypes
                // (which must not redeclare [Identity] per ADR-0016 / FORGE0006) are still accepted.
                if (!HasIdentityAttributeOnTypeOrBases(entityType))
                    throw new InvalidOperationException(
                        $"Entity type '{entityType.FullName}' carries [OperationEndpoints] " +
                        "but is missing the required [Identity] attribute (checked on this type and all base types).");

                var path = opAttr.Path ?? entityAttr.Path
                    ?? throw new InvalidOperationException(
                        $"Entity type '{entityType.FullName}' carries [OperationEndpoints] but " +
                        "neither the attribute nor [Entity(Path = …)] specifies a route path. " +
                        "Add a path: [OperationEndpoints(\"my-path\")] or [Entity(Path = \"my-path\")].");

                services.AddSingleton(new OperationEndpointDescriptor(entityType, path));
            }
        }

        return services;
    }

    /// <summary>
    /// Convenience overload — scans the assembly that contains <typeparamref name="T"/>.
    /// </summary>
    public static IServiceCollection AddOperationEndpointsHttpFromAssemblyContaining<T>(
        this IServiceCollection services)
        where T : class
        => services.AddOperationEndpointsHttp(typeof(T).Assembly);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> or any of its base types
    /// carries <see cref="IdentityAttribute"/>. Because <see cref="IdentityAttribute"/> has
    /// <c>Inherited = false</c>, the standard <c>GetCustomAttribute(inherit: true)</c> overload
    /// does not traverse the type hierarchy; we walk it explicitly.
    /// </summary>
    private static bool HasIdentityAttributeOnTypeOrBases(Type type)
    {
        var t = type;
        while (t is not null)
        {
            if (t.GetCustomAttribute<IdentityAttribute>() is not null)
                return true;
            t = t.BaseType;
        }
        return false;
    }

    /// <summary>
    /// <see cref="JsonTypeInfo"/> modifier that sets <c>ShouldSerialize = false</c> on any
    /// property whose type implements <see cref="IEntityRefCollectionState"/> and whose runtime
    /// value reports <see cref="IEntityRefCollectionState.IsResolved"/> = <see langword="false"/>.
    /// This causes lazy inverse collection keys to be omitted entirely from HTTP responses (ADR-0018).
    /// </summary>
    private static void SuppressUnresolvedEntityRefCollections(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;
        foreach (var prop in typeInfo.Properties)
        {
            if (!typeof(IEntityRefCollectionState).IsAssignableFrom(prop.PropertyType)) continue;
            var originalShouldSerialize = prop.ShouldSerialize;
            prop.ShouldSerialize = (obj, value) =>
                (originalShouldSerialize?.Invoke(obj, value) ?? true) &&
                (value is not IEntityRefCollectionState s || s.IsResolved);
        }
    }
}
