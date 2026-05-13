using System.Reflection;
using Forge.Entity;
using Forge.ObjectStorage.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.ObjectStorage.Http.DependencyInjection;

/// <summary>
/// DI registration helpers for <c>Forge.ObjectStorage.Http</c>.
/// See root ADR-0023 and <c>ObjectStorage.Http</c> ADR-0001.
/// </summary>
public static class ForgeObjectStorageHttpServiceCollectionExtensions
{
    /// <summary>
    /// Scans <paramref name="assemblies"/> for types carrying both <c>[Entity]</c> and
    /// <c>[ObjectBearing]</c> and registers an <see cref="ObjectOperationDescriptor"/> per type.
    /// <para>
    /// <c>MapObjectOperations()</c> owns all routes for each ObjectBearing entity:
    /// metadata CRUD at <c>api/entities/{path}</c> and binary content at
    /// <c>api/objects/{path}/content</c>. <c>MapOperations()</c> skips these types.
    /// </para>
    /// </summary>
    public static IServiceCollection AddForgeObjectStorageHttp(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var assembly in assemblies)
        {
            foreach (var entityType in assembly.GetTypes())
            {
                var entityAttr = entityType.GetCustomAttribute<EntityAttribute>();
                if (entityAttr is null) continue;

                var obAttr = entityType.GetCustomAttribute<ObjectBearingAttribute>();
                if (obAttr is null) continue;

                var path = entityAttr.Path ?? entityType.Name.ToLowerInvariant();
                services.AddSingleton(new ObjectOperationDescriptor(entityType, path, obAttr.StoreKey));
            }
        }

        return services;
    }

    /// <summary>
    /// Convenience overload — scans the assembly that contains <typeparamref name="T"/>.
    /// </summary>
    public static IServiceCollection AddForgeObjectStorageHttpFromAssemblyContaining<T>(
        this IServiceCollection services)
        where T : class
        => services.AddForgeObjectStorageHttp(typeof(T).Assembly);
}
