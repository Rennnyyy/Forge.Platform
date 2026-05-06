using Forge.Entity;

namespace Forge.Operations.Http;

/// <summary>
/// Internal descriptor carrying the entity type and route path for a single
/// <see cref="OperationEndpointsAttribute"/>-annotated entity.
/// Registered as a singleton by <c>AddOperationEndpointsHttp()</c> and consumed
/// by <c>MapOperations()</c> at application start-up.
/// </summary>
internal sealed class OperationEndpointDescriptor
{
    /// <summary>The entity CLR type (carries <c>[Entity]</c>, <c>[Identity]</c>, <c>[OperationEndpoints]</c>).</summary>
    public Type EntityType { get; }

    /// <summary>The route path segment, e.g. <c>"artists"</c>.</summary>
    public string Path { get; }

    public OperationEndpointDescriptor(Type entityType, string path)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EntityType = entityType;
        Path = path;
    }
}
